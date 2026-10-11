using NUnit.Framework;

/// <summary>
/// PlayerNameStore EditMode 測試（保留自舊 LeaderboardTests.cs；名稱記憶 seam 形狀不變，ADR-015 決策 3／LB5）。
/// 經 internal 建構 seam 注入 fake persistence，不碰真實 PlayerPrefs。
/// </summary>
public class PlayerNameStoreTests
{
    private sealed class FakeNamePersistence : PlayerNameStore.IPersistence
    {
        public string Stored;
        public string Load() => Stored;
        public void Save(string name) => Stored = name;
    }

    [Test]
    public void NoRecord_LoadsEmptyString()
    {
        var store = new PlayerNameStore(new FakeNamePersistence());
        Assert.AreEqual(string.Empty, store.Load());
    }

    [Test]
    public void SaveThenLoad()
    {
        var persistence = new FakeNamePersistence();
        var store = new PlayerNameStore(persistence);
        store.Save("Alice");
        Assert.AreEqual("Alice", store.Load());
        Assert.AreEqual("Alice", persistence.Stored);
    }

    [Test]
    public void TrimsTo12Chars()
    {
        var store = new PlayerNameStore(new FakeNamePersistence());
        store.Save("12345678901234567890"); // 20 chars
        Assert.AreEqual(12, store.Load().Length);
        Assert.AreEqual("123456789012", store.Load());
    }

    [Test]
    public void NullSave_StoresEmpty()
    {
        var store = new PlayerNameStore(new FakeNamePersistence());
        store.Save(null);
        Assert.AreEqual(string.Empty, store.Load());
    }
}
