using UnityEngine;

/// <summary>
/// 得分／最高分／破紀錄判定／PlayerPrefs 持久化的唯一真源（ADR-008 深模組）。
/// 呼叫端只需學：Begin()、AddPoint()、End()→bool，加上四個唯讀 accessor。
/// 破紀錄順序（快照 → 計算 → 覆寫）封閉在 End() 內（S2），PlayerPrefs 讀寫隔離在內部 adapter（S4）。
/// </summary>
public class ScoreSession
{
    // ---- 內部持久化 adapter（S4：不暴露外部；internal seam 供 EditMode 測試注入 fake persistence）----
    internal interface IPersistence
    {
        int LoadHighScore();
        void SaveHighScore(int score);
    }

    /// <summary>PlayerPrefs 實作——ScoreSession 內部私有，呼叫端不觸碰持久化（S4）。</summary>
    private sealed class PlayerPrefsPersistence : IPersistence
    {
        private const string HighScoreKey = "flipbird_high_score";

        public int LoadHighScore()
        {
            return PlayerPrefs.GetInt(HighScoreKey, 0);
        }

        public void SaveHighScore(int score)
        {
            PlayerPrefs.SetInt(HighScoreKey, score);
            PlayerPrefs.Save();
        }
    }

    private readonly IPersistence persistence;
    private int currentScore;
    private int previousHighScore; // End() 當下快照的舊最高分（破紀錄動畫起點）
    private bool recordBroken;

    /// <summary>本局目前分數。</summary>
    public int CurrentScore => currentScore;

    /// <summary>End() 當下快照的舊最高分（破紀錄動畫起點）。</summary>
    public int PreviousHighScore => previousHighScore;

    /// <summary>持久化最高分（PlayerPrefs 單一真源）。</summary>
    public int HighScore => persistence.LoadHighScore();

    /// <summary>本局是否破紀錄（End() 判定）。</summary>
    public bool RecordBroken => recordBroken;

    /// <summary>逐分事件——每加一分 fire 一次，帶目前分數（HUD 訂閱此事件即時更新顯示）。
    /// 為何用事件而非 return：interface 是 test surface——事件把「何時要重繪」的複雜度收進 Score 模組，
    /// caller 免於每幀輪詢 CurrentScore；AddPoint() 維持 void，計分呼叫端（ScoreTrigger）不需處理顯示職責。</summary>
    public event System.Action<int> OnScoreChanged;

    /// <summary>正式建構入口——注入 PlayerPrefs 持久化。</summary>
    public ScoreSession() : this(new PlayerPrefsPersistence()) { }

    /// <summary>測試注入 seam（internal）：供 EditMode 注入 fake persistence，不碰 PlayerPrefs。</summary>
    internal ScoreSession(IPersistence persistence)
    {
        this.persistence = persistence;
    }

    /// <summary>開始新一局：重置 current score、清除 recordBroken，並 fire OnScoreChanged(0)（M1 修復）。
    /// 為何 fire 事件：新局分數歸零本身就是「HUD 該重繪」的事件——UI 訂閱 OnScoreChanged 自然重置顯示，
    /// 無需 UI 自行判斷「何時是新局」（那是 Score 模組的知識）。Resume（Paused→Playing）不呼叫 Begin，
    /// 故不 fire，HUD 維持暫停前分數。</summary>
    public void Begin()
    {
        currentScore = 0;
        recordBroken = false;
        if (OnScoreChanged != null)
            OnScoreChanged(currentScore);
    }

    /// <summary>計分 +1，並 fire OnScoreChanged（帶目前分數）供 HUD 即時更新。</summary>
    public void AddPoint()
    {
        currentScore++;
        if (OnScoreChanged != null)
            OnScoreChanged(currentScore);
    }

    /// <summary>結束本局：快照舊最高分 → 計算 recordBroken → 破紀錄則覆寫（S2 順序封閉）。
    /// 回傳是否破紀錄。</summary>
    public bool End()
    {
        // 順序關鍵（S2）：先快照舊最高分，再計算 recordBroken，最後才覆寫——
        // 若先覆寫再快照，破紀錄動畫起點會讀到已覆寫的新值。
        previousHighScore = persistence.LoadHighScore();
        recordBroken = currentScore > previousHighScore;
        if (recordBroken)
            persistence.SaveHighScore(currentScore);
        return recordBroken;
    }
}