using System;
using System.Threading;
using LootLocker.Requests;
using UnityEngine;

/// <summary>
/// ILootLockerApi 的 concrete adapter（ADR-016 決策 1/2／LB9/LB10）——全專案唯一引用 LootLocker.* 的類。
/// 負責 SDK 回應 → 領域型別映射（metadata→playerName、score→score），零 SDK 型別洩漏至 provider。
/// 主執行緒斷言：建構捕捉 ManagedThreadId，每個 SDK callback 入口比對，不符→LogError＋丟棄該 callback
/// （不引入 dispatcher，ADR-016 決策 2）。SDK 呼叫失敗不拋例外中斷遊戲（NFR-07 try/catch＋log）。
/// </summary>
internal sealed class LootLockerApi : ILootLockerApi
{
    private readonly int mainThreadId;

    public LootLockerApi()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId; // GameManager.Awake（主執行緒）
    }

    public void StartGuestSession(string deviceId, Action<bool> onComplete)
    {
        try
        {
            LootLockerSDKManager.StartGuestSession(deviceId, response =>
            {
                if (!OnMainThread(nameof(StartGuestSession))) return;
                onComplete?.Invoke(response != null && response.success);
            });
        }
        catch (Exception e)
        {
            Debug.LogError("[LootLockerApi] StartGuestSession threw: " + e);
            onComplete?.Invoke(false);
        }
    }

    public void SubmitScore(string memberId, int score, string leaderboardKey, string metadata, Action<bool, int> onComplete)
    {
        try
        {
            LootLockerSDKManager.SubmitScore(memberId, score, leaderboardKey, metadata, response =>
            {
                if (!OnMainThread(nameof(SubmitScore))) return;
                bool success = response != null && response.success;
                onComplete?.Invoke(success, success ? response.rank : -1);
            });
        }
        catch (Exception e)
        {
            Debug.LogError("[LootLockerApi] SubmitScore threw: " + e);
            onComplete?.Invoke(false, -1);
        }
    }

    public void GetScoreList(string leaderboardKey, int count, Action<bool, LeaderboardEntry[]> onComplete)
    {
        try
        {
            LootLockerSDKManager.GetScoreList(leaderboardKey, count, response =>
            {
                if (!OnMainThread(nameof(GetScoreList))) return;
                if (response == null || !response.success)
                {
                    onComplete?.Invoke(false, null);
                    return;
                }
                onComplete?.Invoke(true, MapEntries(response.items));
            });
        }
        catch (Exception e)
        {
            Debug.LogError("[LootLockerApi] GetScoreList threw: " + e);
            onComplete?.Invoke(false, null);
        }
    }

    /// <summary>主執行緒斷言（ADR-016 決策 2）：不符→LogError＋丟棄 callback（絕不在非主執行緒呼叫 UI callback）。</summary>
    private bool OnMainThread(string callbackName)
    {
        if (Thread.CurrentThread.ManagedThreadId == mainThreadId) return true;
        Debug.LogError("[LootLockerApi] " + callbackName + " callback fired off the main thread; discarding (ADR-016 決策 2).");
        return false;
    }

    /// <summary>SDK 回應 → 領域型別映射：metadata→playerName、score→score（LB9 零 SDK 型別洩漏）。</summary>
    private static LeaderboardEntry[] MapEntries(LootLockerLeaderboardMember[] items)
    {
        if (items == null) return new LeaderboardEntry[0];
        LeaderboardEntry[] entries = new LeaderboardEntry[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            entries[i] = new LeaderboardEntry
            {
                playerName = items[i] != null ? items[i].metadata : string.Empty,
                score = items[i] != null ? items[i].score : 0
            };
        }
        return entries;
    }
}
