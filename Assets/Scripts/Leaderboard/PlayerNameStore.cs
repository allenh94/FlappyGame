using UnityEngine;

/// <summary>
/// 名稱記憶 seam（ADR-015 決策 3／LB5）——key "flipbird_player_name"。
/// 獨立於 LeaderboardProvider 接口（名稱記憶非榜單職責，不污染接口契約）。
/// UI 經 GameManager.PlayerName accessor 讀寫，不直接觸碰 PlayerPrefs（NFR-03 grep 驗收）。
/// </summary>
public class PlayerNameStore
{
    private const string PlayerNameKey = "flipbird_player_name";
    private const int MaxNameLength = 12; // 決策 A=a：上限 12 字元（此處裁剪防護，呼叫端亦限）

    // ---- 內部持久化 adapter（LB4：不暴露外部；internal seam 供 EditMode 測試注入 fake persistence）----
    internal interface IPersistence
    {
        string Load();
        void Save(string name);
    }

    /// <summary>PlayerPrefs 實作——PlayerNameStore 內部私有，呼叫端不觸碰持久化（LB4）。</summary>
    private sealed class PlayerPrefsPersistence : IPersistence
    {
        public string Load()
        {
            return PlayerPrefs.GetString(PlayerNameKey, string.Empty);
        }

        public void Save(string name)
        {
            PlayerPrefs.SetString(PlayerNameKey, name);
            PlayerPrefs.Save();
        }
    }

    private readonly IPersistence persistence;

    /// <summary>正式建構入口——注入 PlayerPrefs 持久化。</summary>
    public PlayerNameStore() : this(new PlayerPrefsPersistence()) { }

    /// <summary>測試注入 seam（internal）：供 EditMode 注入 fake persistence，不碰 PlayerPrefs（比照 ScoreSession.cs:60-63）。</summary>
    internal PlayerNameStore(IPersistence persistence)
    {
        this.persistence = persistence;
    }

    /// <summary>讀取上次輸入的名稱；無記錄回空字串（契約由 store 保證，不依賴 adapter 實作）。</summary>
    public string Load()
    {
        string value = persistence.Load();
        return value ?? string.Empty;
    }

    /// <summary>記憶名稱（下次預填）。上限 12 字元此處裁剪防護；null 視為空字串。</summary>
    public void Save(string name)
    {
        if (name == null) name = string.Empty;
        if (name.Length > MaxNameLength)
            name = name.Substring(0, MaxNameLength);
        persistence.Save(name);
    }
}