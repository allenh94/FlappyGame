using NUnit.Framework;

/// <summary>
/// LeaderboardRules 純函式 EditMode 測試（保留自舊 LeaderboardTests.cs；本地 provider 已刪除）。
/// Qualify 三規則＋null/空陣列＝空榜單（ADR-015 修訂 5／LB6）。
/// </summary>
public class LeaderboardRulesTests
{
    private static LeaderboardEntry Entry(string name, int score)
    {
        return new LeaderboardEntry { playerName = name, score = score };
    }

    [Test]
    public void Qualifies_LessThan10_RequiresPositiveScore()
    {
        var entries = new[] { Entry("A", 10), Entry("B", 5) };
        Assert.IsTrue(LeaderboardRules.Qualifies(1, entries));   // BC-1：score=1 上榜
        Assert.IsFalse(LeaderboardRules.Qualifies(0, entries));
        Assert.IsFalse(LeaderboardRules.Qualifies(-1, entries));
    }

    [Test]
    public void Qualifies_Full10_RequiresGreaterThan10th()
    {
        var entries = new LeaderboardEntry[10];
        for (int i = 0; i < 10; i++) entries[i] = Entry("P" + i, 100 - i); // 100..91

        Assert.IsTrue(LeaderboardRules.Qualifies(101, entries));  // 高於第 10 名
        Assert.IsFalse(LeaderboardRules.Qualifies(91, entries));  // BC-2：與第 10 名同分不算
        Assert.IsFalse(LeaderboardRules.Qualifies(90, entries));  // 低於第 10 名
    }

    [Test]
    public void Qualifies_NullOrEmpty_IsEmptyBoard()
    {
        Assert.IsTrue(LeaderboardRules.Qualifies(1, null));
        Assert.IsTrue(LeaderboardRules.Qualifies(5, new LeaderboardEntry[0]));
        Assert.IsFalse(LeaderboardRules.Qualifies(0, null));
        Assert.IsFalse(LeaderboardRules.Qualifies(0, new LeaderboardEntry[0]));
    }
}
