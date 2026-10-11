using System;

/// <summary>
/// 榜單讀寫統一接口（FR-02，async callback 式；ADR-015 決策 1/4／LB2）。
/// 線上實作＝LootLockerLeaderboardProvider（LootLocker 後端；ADR-016）；介面契約不變，換實作改組裝點（NFR-05 修正）。
///
/// callback 契約三點（ADR-015 決策 4／LB2）：
/// ① 主執行緒保證——callback 保證在 Unity 主執行緒呼叫；未來異步實作若在背景執行緒回呼，實作類必須自行 marshal 到主執行緒。
/// ② 同步回呼容忍——本地實作同步立即回呼；callback 內執行「顯示/渲染」動作本身，不假設顯示已完成（同步回呼時 LeaderboardScreen 尚未顯示）。
/// ③ Submit callback 內禁再 LoadAll——SubmitResult.Entries 已含提交後完整榜單，再 LoadAll 於同步回呼時會重入。
/// </summary>
public interface ILeaderboardProvider
{
    /// <summary>讀取完整榜單（已排序：降冪＋同分 FCFS）。corrupt 時回傳空陣列（FR-07）。</summary>
    void LoadAll(Action<LeaderboardEntry[]> callback);

    /// <summary>提交條目：插入→排序→截斷 10 筆→覆寫→回傳 SubmitResult（原子，FR-06）。</summary>
    void Submit(LeaderboardEntry entry, Action<SubmitResult> callback);
}

/// <summary>
/// Submit 回傳結果（FR-02/FR-06）——Qualified＝是否上榜；Rank＝1-based 名次（未上榜 -1）；
/// Entries＝提交後完整榜單（Qualify Animation 用，Submit callback 內禁再 LoadAll）。
/// </summary>
public readonly struct SubmitResult
{
    public readonly bool Qualified;
    public readonly int Rank;
    public readonly LeaderboardEntry[] Entries;

    public SubmitResult(bool qualified, int rank, LeaderboardEntry[] entries)
    {
        Qualified = qualified;
        Rank = rank;
        Entries = entries;
    }
}

/// <summary>
/// Qualify 判定單一事實來源（FR-05／LB6）——純函式，Submit 內部與 UI 顯示判定共用。
/// 未滿 10 筆：score &gt; 0；滿 10 筆：score &gt; 第 10 名分數；與第 10 名同分不算（BC-2）。
/// entries 為 null/空陣列視為空榜單（corrupt 防護 FR-07 配套，ADR-015 修訂 5）——score &gt; 0 即上榜。
/// </summary>
public static class LeaderboardRules
{
    public static bool Qualifies(int score, LeaderboardEntry[] entries)
    {
        if (score <= 0) return false;
        if (entries == null || entries.Length == 0) return true; // 空榜單：score > 0 即上榜（ADR-015 修訂 5／LB6）
        if (entries.Length < 10) return true;
        return score > entries[entries.Length - 1].score; // 滿 10 筆：嚴格大於第 10 名（同分不算，BC-2）
    }
}

/// <summary>
/// JsonUtility wrapper class（A1 防護）——JsonUtility 不支援頂層裸陣列，需包一層。
/// 設 public 以消除 JsonUtility 對 internal 型別的序列化風險（行為與 internal 等價）。
/// </summary>
[Serializable]
public class LeaderboardData
{
    public LeaderboardEntry[] entries;
}