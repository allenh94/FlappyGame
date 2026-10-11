using UnityEngine;

/// <summary>
/// 遊戲狀態權威查詢 seam（ADR-011 GA1）：gameplay 系統唯一狀態查詢入口。
/// static singleton——GameManager 於 Awake 註冊、OnDestroy 反註冊。
/// 只做狀態查詢（IsPlaying 等），不承接任何命令（Die/StartGame/Pause 仍走 GameManager，GA3）。
/// 凍結政策不變（GA2）：各系統仍自查 IsPlaying()（輪詢），只改查詢來源。
/// </summary>
public static class GameStateAuthority
{
    private static GameManager instance;

    /// <summary>GameManager 於 Awake 註冊（static singleton 生命週期）。</summary>
    public static void Register(GameManager manager)
    {
        instance = manager;
    }

    /// <summary>GameManager 於 OnDestroy 反註冊，避免引用已銷毀實例。</summary>
    public static void Unregister(GameManager manager)
    {
        if (instance == manager)
            instance = null;
    }

    /// <summary>唯一狀態查詢（ADR-006 G1：IsPlaying() = state == Playing）。</summary>
    public static bool IsPlaying()
    {
        return instance != null && instance.IsPlaying();
    }

    /// <summary>暫停查詢（ADR-011 擴張，2026-09-30）——BlobAnimator 淡出守衛用（非暫停即可淡出）。查詢非命令。</summary>
    public static bool IsPaused()
    {
        return instance != null && instance.IsPaused();
    }
}