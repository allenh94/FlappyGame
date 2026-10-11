using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 榜單 LootLocker 線上後端唯一實作（FR-01／ADR-016 決策 1/7）。
/// 單一自足：lazy guest session＋讀寫穿透快取＋提交失敗待送佇列＋唯一 member_id 全在內部；
/// SDK 呼叫全封裝於 internal ILootLockerApi seam（LB9），provider 邏輯不觸碰 SDK 型別。
/// 純 C#（零 MonoBehaviour，NFR-07）；依賴 UnityEngine（PlayerPrefs/JsonUtility/SystemInfo/Debug）＋System。
/// 依賴方向單向（LB3）：UI → GameManager.Leaderboard → 本類 → ILootLockerApi → SDK。
/// callback 契約（ADR-015 決策 4／ADR-016 決策 2）：主執行緒保證（SDK 已主執行緒，adapter 斷言）、
/// 同步回呼容忍、Submit callback 內禁再 LoadAll（本類 Submit 內以 GetScoreList 取榜，非 LoadAll）。
/// </summary>
public class LootLockerLeaderboardProvider : ILeaderboardProvider
{
    // TODO(Human)：真 leaderboard key 待提供——目前為佔位值（ADR-016 決策 1／FR-01）。
    private const string LeaderboardKey = "flappy_game_high_scores";

    private const string CacheKey = "flappy_game_high_scores_leaderboard_cache";     // 上次成功榜（FR-05／LB11）
    private const string PendingKey = "flappy_game_high_scores_leaderboard_pending"; // 待送佇列（FR-06／LB11）
    private const int MaxEntries = 10;   // GetScoreList count（FR-07）
    private const int MaxPending = 20;   // 佇列上限（FR-06／BC-8，FIFO drop-oldest）

    private readonly ILootLockerApi api;
    private readonly List<Action<bool>> sessionCallbacks = new List<Action<bool>>();

    private bool sessionReady;
    private bool sessionInProgress;
    private bool isFlushing;
    private LeaderboardEntry[] cachedEntries; // 記憶體快照（避免重複讀取損壞值）

    /// <summary>正式建構入口——注入 concrete LootLockerApi（全專案唯一引用 SDK 的類）。</summary>
    public LootLockerLeaderboardProvider() : this(new LootLockerApi()) { }

    /// <summary>測試注入 seam（internal）：供 EditMode 注入 fake ILootLockerApi，不碰真網路（比照 ScoreSession/Local 的 internal seam）。</summary>
    internal LootLockerLeaderboardProvider(ILootLockerApi api)
    {
        this.api = api;
    }

    // ---- LoadAll（讀寫穿透快取，FR-05／BC-1）----

    /// <summary>讀取榜單：session ready → GetScoreList；成功寫快取＋回榜；失敗讀快取；皆敗回空榜（永不懸置）。</summary>
    public void LoadAll(Action<LeaderboardEntry[]> callback)
    {
        // session 早已 ready 時，LoadAll 成功後機會性 flush；若本次才建立 session，
        // 則由 EnsureSession 的 session-ready 轉移負責 flush（避免同步回呼下同一轉移 flush 兩次）。
        bool sessionWasReady = sessionReady;
        EnsureSession(success =>
        {
            if (success) FetchAndCache(callback, flushAfter: sessionWasReady);
            else callback(LoadCacheOrEmpty()); // session 失敗：讀快取／空榜，不報錯（BC-3）
        });
    }

    private void FetchAndCache(Action<LeaderboardEntry[]> callback, bool flushAfter)
    {
        api.GetScoreList(LeaderboardKey, MaxEntries, (success, entries) =>
        {
            if (success)
            {
                cachedEntries = entries ?? new LeaderboardEntry[0];
                SaveCache(cachedEntries);
                callback(cachedEntries);
                if (flushAfter) FlushPending(); // 機會性 flush（ADR-016 決策 4；不在 Submit callback 內，避免巢狀）
            }
            else
            {
                Debug.LogWarning("[LootLockerLeaderboardProvider] GetScoreList failed; serving cache/empty (BC-1).");
                callback(LoadCacheOrEmpty());
            }
        });
    }

    // ---- Submit（伺服器權威，FR-07／LB15）----

    /// <summary>提交條目：唯一 memberId → session → SubmitScore → GetScoreList → Qualified=rank∈top10。
    /// 失敗且過守衛→入佇列；永遠回呼（決策 4 第 7 點）。</summary>
    public void Submit(LeaderboardEntry entry, Action<SubmitResult> callback)
    {
        if (entry == null)
        {
            callback(new SubmitResult(false, -1, LoadCacheOrEmpty()));
            return;
        }

        string memberId = Guid.NewGuid().ToString("N"); // 唯一 member_id（FR-04／BC-5；補送沿用同一 id）

        EnsureSession(success =>
        {
            if (!success)
            {
                EnqueueIfQualifies(memberId, entry);
                callback(new SubmitResult(false, -1, LoadCacheOrEmpty())); // 永遠回呼
                return;
            }
            SubmitToServer(memberId, entry, callback);
        });
    }

    private void SubmitToServer(string memberId, LeaderboardEntry entry, Action<SubmitResult> callback)
    {
        api.SubmitScore(memberId, entry.score, LeaderboardKey, entry.playerName, (success, rank) =>
        {
            if (!success)
            {
                EnqueueIfQualifies(memberId, entry);
                callback(new SubmitResult(false, -1, LoadCacheOrEmpty()));
                return;
            }

            // 伺服器權威：再取榜單（Submit callback 內禁再 LoadAll——此處直接 GetScoreList，非 LoadAll）
            api.GetScoreList(LeaderboardKey, MaxEntries, (listSuccess, entries) =>
            {
                if (listSuccess)
                {
                    cachedEntries = entries ?? new LeaderboardEntry[0];
                    SaveCache(cachedEntries);
                }
                else
                {
                    entries = LoadCacheOrEmpty(); // 取榜失敗：退回快取，Qualified 仍由伺服器 rank 決定
                }
                bool qualified = rank >= 1 && rank <= MaxEntries;
                callback(new SubmitResult(qualified, qualified ? rank : -1, entries));
            });
        });
    }

    // ---- session（lazy，FR-02／LB12）----

    /// <summary>lazy guest session：首個網路操作觸發；成功→ready＋flush；失敗→走失敗路徑，下次再試。
    /// 併發操作共用同一次 StartGuestSession（sessionCallbacks 等待佇列）。</summary>
    private void EnsureSession(Action<bool> onComplete)
    {
        if (sessionReady)
        {
            onComplete(true);
            return;
        }

        sessionCallbacks.Add(onComplete);
        if (sessionInProgress) return; // 已有 session 建立中——排入等待，避免重複 StartGuestSession

        sessionInProgress = true;
        api.StartGuestSession(SystemInfo.deviceUniqueIdentifier, success =>
        {
            sessionInProgress = false;
            if (success) sessionReady = true;

            Action<bool>[] callbacks = sessionCallbacks.ToArray();
            sessionCallbacks.Clear();
            foreach (Action<bool> cb in callbacks) cb(success);

            if (success) FlushPending(); // session 建立成功後 flush（FR-08）
        });
    }

    // ---- 待送佇列（FR-06／FR-08／LB13）----

    private void EnqueueIfQualifies(string memberId, LeaderboardEntry entry)
    {
        if (!LeaderboardRules.Qualifies(entry.score, LoadCacheOrEmpty())) return; // 排隊條件（決策 8）

        List<PendingLeaderboardEntry> pending = LoadPending();
        pending.Add(new PendingLeaderboardEntry { memberId = memberId, playerName = entry.playerName, score = entry.score });
        while (pending.Count > MaxPending) pending.RemoveAt(0); // FIFO drop-oldest（BC-8）
        SavePending(pending);
    }

    private void FlushPending()
    {
        if (isFlushing || !sessionReady) return; // 防重入（LB13）
        List<PendingLeaderboardEntry> pending = LoadPending();
        if (pending.Count == 0) return;

        isFlushing = true;
        FlushNext(pending, 0);
    }

    private void FlushNext(List<PendingLeaderboardEntry> pending, int index)
    {
        if (index >= pending.Count)
        {
            isFlushing = false;
            return;
        }

        PendingLeaderboardEntry item = pending[index];
        api.SubmitScore(item.memberId, item.score, LeaderboardKey, item.playerName, (success, rank) =>
        {
            if (success) RemovePending(item.memberId); // 成功移除；失敗保留（下次再試）
            FlushNext(pending, index + 1);
        });
    }

    private void RemovePending(string memberId)
    {
        List<PendingLeaderboardEntry> pending = LoadPending();
        int removed = pending.RemoveAll(p => p.memberId == memberId);
        if (removed > 0) SavePending(pending);
    }

    // ---- 持久化（PlayerPrefs 內部 adapter，LB4／LB11）----

    private LeaderboardEntry[] LoadCacheOrEmpty()
    {
        if (cachedEntries != null) return cachedEntries;
        string json = PlayerPrefs.GetString(CacheKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new LeaderboardEntry[0];
        try
        {
            LeaderboardData data = JsonUtility.FromJson<LeaderboardData>(json);
            if (data == null || data.entries == null) return new LeaderboardEntry[0];
            cachedEntries = data.entries;
            return cachedEntries;
        }
        catch (Exception)
        {
            return new LeaderboardEntry[0]; // corrupt：回空榜、不 crash（BC-1）
        }
    }

    private void SaveCache(LeaderboardEntry[] entries)
    {
        PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(new LeaderboardData { entries = entries }));
        PlayerPrefs.Save();
    }

    private List<PendingLeaderboardEntry> LoadPending()
    {
        string json = PlayerPrefs.GetString(PendingKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new List<PendingLeaderboardEntry>();
        try
        {
            PendingLeaderboardData data = JsonUtility.FromJson<PendingLeaderboardData>(json);
            if (data == null || data.entries == null) return new List<PendingLeaderboardEntry>();
            return new List<PendingLeaderboardEntry>(data.entries);
        }
        catch (Exception)
        {
            return new List<PendingLeaderboardEntry>(); // corrupt：視為空佇列、不 crash
        }
    }

    private void SavePending(List<PendingLeaderboardEntry> pending)
    {
        PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(new PendingLeaderboardData { entries = pending.ToArray() }));
        PlayerPrefs.Save();
    }
}

/// <summary>
/// SDK 隔離 seam（ADR-016 決策 1／LB9）——provider 只依賴此介面；回傳領域型別，零 LootLocker 型別洩漏。
/// 顯式成功旗標（取代 rank&lt;0 sentinel）：失敗與「未上榜 rank=-1」可區分（A4）。
/// </summary>
internal interface ILootLockerApi
{
    void StartGuestSession(string deviceId, Action<bool> onComplete);
    void SubmitScore(string memberId, int score, string leaderboardKey, string metadata, Action<bool, int> onComplete);
    void GetScoreList(string leaderboardKey, int count, Action<bool, LeaderboardEntry[]> onComplete);
}

/// <summary>待送佇列項（FR-06）——memberId 提交當下產生、補送沿用（LB14）。</summary>
[Serializable]
public class PendingLeaderboardEntry
{
    public string memberId;
    public string playerName;
    public int score;
}

/// <summary>JsonUtility wrapper（不支援頂層裸陣列）。</summary>
[Serializable]
public class PendingLeaderboardData
{
    public PendingLeaderboardEntry[] entries;
}
