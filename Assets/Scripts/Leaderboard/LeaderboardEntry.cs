using System;

/// <summary>
/// 榜單條目資料模型（FR-01）——玩家名字＋分數。
/// 供 LeaderboardProvider 讀寫與 UI 顯示共用；UI 不得直接觸碰 PlayerPrefs（NFR-03）。
/// </summary>
[Serializable]
public class LeaderboardEntry
{
    public string playerName;
    public int score;
}