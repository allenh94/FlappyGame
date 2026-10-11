using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// LootLockerLeaderboardProvider EditMode 測試（T3）——注入 fake ILootLockerApi，不碰真網路。
/// 覆蓋：快取穿透／待送佇列（上限 20 drop-oldest）／唯一 memberId／伺服器權威 Qualified 推導／
/// session 失敗不 crash／corrupt 快取回空／flush（memberId 沿用、成功移除）。
/// provider 無 persistence seam（ADR-016 決策 1 僅 ILootLockerApi seam），故測試使用真實 PlayerPrefs；
/// SetUp/TearDown 清理 cache/pending 兩 key 保持 hermetic，不碰 flipbird_high_score／flipbird_player_name。
/// </summary>
public class LootLockerLeaderboardProviderTests
{
    private const string CacheKey = "flipbird_leaderboard_cache";
    private const string PendingKey = "flipbird_leaderboard_pending";

    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey(CacheKey);
        PlayerPrefs.DeleteKey(PendingKey);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.DeleteKey(CacheKey);
        PlayerPrefs.DeleteKey(PendingKey);
    }

    // ---- fake ILootLockerApi（internal seam 注入）----

    private sealed class FakeApi : ILootLockerApi
    {
        public bool SessionSuccess = true;
        public bool SubmitSuccess = true;
        public bool ListSuccess = true;
        public int SubmitRank = 1;
        public LeaderboardEntry[] ListEntries = new LeaderboardEntry[0];

        public int SessionCalls;
        public int SubmitCalls;
        public int ListCalls;
        public readonly List<string> SessionDeviceIds = new List<string>();
        public readonly List<SubmittedCall> Submitted = new List<SubmittedCall>();

        public struct SubmittedCall
        {
            public string memberId;
            public int score;
            public string metadata;
        }

        public void StartGuestSession(string deviceId, Action<bool> onComplete)
        {
            SessionCalls++;
            SessionDeviceIds.Add(deviceId);
            onComplete(SessionSuccess);
        }

        public void SubmitScore(string memberId, int score, string leaderboardKey, string metadata, Action<bool, int> onComplete)
        {
            SubmitCalls++;
            Submitted.Add(new SubmittedCall { memberId = memberId, score = score, metadata = metadata });
            onComplete(SubmitSuccess, SubmitSuccess ? SubmitRank : -1);
        }

        public void GetScoreList(string leaderboardKey, int count, Action<bool, LeaderboardEntry[]> onComplete)
        {
            ListCalls++;
            onComplete(ListSuccess, ListEntries);
        }
    }

    private static LeaderboardEntry Entry(string name, int score)
    {
        return new LeaderboardEntry { playerName = name, score = score };
    }

    private static LeaderboardEntry[] LoadAll(LootLockerLeaderboardProvider provider)
    {
        LeaderboardEntry[] result = null;
        provider.LoadAll(entries => result = entries);
        return result;
    }

    private static SubmitResult Submit(LootLockerLeaderboardProvider provider, LeaderboardEntry entry)
    {
        SubmitResult result = default;
        provider.Submit(entry, r => result = r);
        return result;
    }

    private static PendingLeaderboardEntry[] LoadPending()
    {
        string json = PlayerPrefs.GetString(PendingKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new PendingLeaderboardEntry[0];
        PendingLeaderboardData data = JsonUtility.FromJson<PendingLeaderboardData>(json);
        return data != null && data.entries != null ? data.entries : new PendingLeaderboardEntry[0];
    }

    private static void SeedPending(params PendingLeaderboardEntry[] entries)
    {
        PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(new PendingLeaderboardData { entries = entries }));
    }

    // ---- LoadAll 快取穿透（FR-05／BC-1）----

    [Test]
    public void LoadAll_Success_WritesCacheAndReturnsBoard()
    {
        var api = new FakeApi { ListSuccess = true, ListEntries = new[] { Entry("A", 100), Entry("B", 50) } };
        var provider = new LootLockerLeaderboardProvider(api);

        LeaderboardEntry[] result = LoadAll(provider);

        Assert.AreEqual(2, result.Length);
        Assert.AreEqual("A", result[0].playerName);
        Assert.AreEqual(100, result[0].score);
        Assert.IsTrue(PlayerPrefs.HasKey(CacheKey), "成功榜應寫入快取");
    }

    [Test]
    public void LoadAll_NetworkFailure_ReadsCache()
    {
        PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(new LeaderboardData { entries = new[] { Entry("Cached", 42) } }));
        var api = new FakeApi { ListSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        LeaderboardEntry[] result = LoadAll(provider);

        Assert.AreEqual(1, result.Length);
        Assert.AreEqual("Cached", result[0].playerName);
        Assert.AreEqual(42, result[0].score);
    }

    [Test]
    public void LoadAll_NetworkAndCacheFail_ReturnsEmpty()
    {
        var api = new FakeApi { ListSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        LeaderboardEntry[] result = LoadAll(provider);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [Test]
    public void LoadAll_CorruptCache_ReturnsEmpty()
    {
        PlayerPrefs.SetString(CacheKey, "{not valid json!!!");
        var api = new FakeApi { ListSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        LeaderboardEntry[] result = LoadAll(provider);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    // ---- Submit 伺服器權威（FR-07／LB15）----

    [Test]
    public void Submit_Success_DerivesQualifiedRankEntries()
    {
        var api = new FakeApi
        {
            SubmitSuccess = true,
            SubmitRank = 3,
            ListSuccess = true,
            ListEntries = new[] { Entry("A", 100), Entry("B", 80), Entry("Me", 60) }
        };
        var provider = new LootLockerLeaderboardProvider(api);

        SubmitResult result = Submit(provider, Entry("Me", 60));

        Assert.IsTrue(result.Qualified);
        Assert.AreEqual(3, result.Rank);
        Assert.AreEqual(3, result.Entries.Length);
        Assert.AreEqual("Me", result.Entries[2].playerName);
    }

    [Test]
    public void Submit_Success_RankOutsideTop10_NotQualified()
    {
        var api = new FakeApi
        {
            SubmitSuccess = true,
            SubmitRank = 11,
            ListSuccess = true,
            ListEntries = new LeaderboardEntry[10]
        };
        var provider = new LootLockerLeaderboardProvider(api);

        SubmitResult result = Submit(provider, Entry("Me", 1));

        Assert.IsFalse(result.Qualified);
        Assert.AreEqual(-1, result.Rank);
    }

    [Test]
    public void Submit_Success_ListFailure_FallsBackToCache()
    {
        PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(new LeaderboardData { entries = new[] { Entry("Cached", 7) } }));
        var api = new FakeApi { SubmitSuccess = true, SubmitRank = 1, ListSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        SubmitResult result = Submit(provider, Entry("Me", 7));

        Assert.IsTrue(result.Qualified);
        Assert.AreEqual(1, result.Rank);
        Assert.AreEqual(1, result.Entries.Length);
        Assert.AreEqual("Cached", result.Entries[0].playerName);
    }

    // ---- 待送佇列（FR-06／FR-08／BC-2／BC-8）----

    [Test]
    public void Submit_Failure_Qualifies_Enqueues()
    {
        var api = new FakeApi { SubmitSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        SubmitResult result = Submit(provider, Entry("Me", 50));

        Assert.IsFalse(result.Qualified);
        Assert.AreEqual(-1, result.Rank);
        PendingLeaderboardEntry[] pending = LoadPending();
        Assert.AreEqual(1, pending.Length);
        Assert.AreEqual("Me", pending[0].playerName);
        Assert.AreEqual(50, pending[0].score);
        Assert.IsFalse(string.IsNullOrEmpty(pending[0].memberId));
    }

    [Test]
    public void Submit_Failure_DoesNotQualify_NotEnqueued()
    {
        // 滿 10 筆榜單，第 10 名 91；提交 50 不過守衛
        var full = new LeaderboardEntry[10];
        for (int i = 0; i < 10; i++) full[i] = Entry("P" + i, 100 - i);
        PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(new LeaderboardData { entries = full }));
        var api = new FakeApi { SubmitSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        Submit(provider, Entry("Low", 50));

        Assert.AreEqual(0, LoadPending().Length);
    }

    [Test]
    public void Submit_Failure_QueueCapsAt20_DropsOldest()
    {
        var api = new FakeApi { SubmitSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        for (int i = 1; i <= 25; i++)
            Submit(provider, Entry("P" + i, i));

        PendingLeaderboardEntry[] pending = LoadPending();
        Assert.AreEqual(20, pending.Length);
        Assert.AreEqual("P6", pending[0].playerName);   // 最舊 5 項被丟棄
        Assert.AreEqual("P25", pending[19].playerName);
    }

    // ---- session（FR-02／BC-3）----

    [Test]
    public void SessionFailure_DoesNotCrash_LoadAllReturnsEmpty()
    {
        var api = new FakeApi { SessionSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        LeaderboardEntry[] result = LoadAll(provider);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [Test]
    public void SessionFailure_SubmitQueuesIfQualifies()
    {
        var api = new FakeApi { SessionSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        SubmitResult result = Submit(provider, Entry("Me", 5));

        Assert.IsFalse(result.Qualified);
        Assert.AreEqual(1, LoadPending().Length);
    }

    [Test]
    public void SessionSuccess_FlushesPending_ReusesMemberId_RemovesOnSuccess()
    {
        SeedPending(
            new PendingLeaderboardEntry { memberId = "id-1", playerName = "A", score = 10 },
            new PendingLeaderboardEntry { memberId = "id-2", playerName = "B", score = 20 });
        var api = new FakeApi { SessionSuccess = true, SubmitSuccess = true };
        var provider = new LootLockerLeaderboardProvider(api);

        LoadAll(provider); // 觸發 lazy session → 成功 → flush

        Assert.AreEqual(2, api.Submitted.Count);
        Assert.AreEqual("id-1", api.Submitted[0].memberId);
        Assert.AreEqual("id-2", api.Submitted[1].memberId);
        Assert.AreEqual(0, LoadPending().Length, "成功補送應從佇列移除");
    }

    [Test]
    public void SessionSuccess_FlushFailure_KeepsPending()
    {
        SeedPending(new PendingLeaderboardEntry { memberId = "id-1", playerName = "A", score = 10 });
        var api = new FakeApi { SessionSuccess = true, SubmitSuccess = false };
        var provider = new LootLockerLeaderboardProvider(api);

        LoadAll(provider);

        Assert.AreEqual(1, api.Submitted.Count);
        Assert.AreEqual(1, LoadPending().Length, "補送失敗應保留");
    }

    // ---- 唯一 memberId（FR-04／BC-5）----

    [Test]
    public void Submit_Consecutive_GeneratesUniqueMemberIds()
    {
        var api = new FakeApi { SubmitSuccess = true, SubmitRank = 1, ListSuccess = true };
        var provider = new LootLockerLeaderboardProvider(api);

        Submit(provider, Entry("A", 10));
        Submit(provider, Entry("B", 20));

        Assert.AreEqual(2, api.Submitted.Count);
        Assert.IsFalse(string.IsNullOrEmpty(api.Submitted[0].memberId));
        Assert.IsFalse(string.IsNullOrEmpty(api.Submitted[1].memberId));
        Assert.AreNotEqual(api.Submitted[0].memberId, api.Submitted[1].memberId);
    }

    // ---- metadata 空字串（BC-4）----

    [Test]
    public void Submit_EmptyMetadata_StillSubmits()
    {
        var api = new FakeApi { SubmitSuccess = true, SubmitRank = 1, ListSuccess = true };
        var provider = new LootLockerLeaderboardProvider(api);

        Submit(provider, Entry(string.Empty, 10));

        Assert.AreEqual(1, api.Submitted.Count);
        Assert.AreEqual(string.Empty, api.Submitted[0].metadata);
    }
}
