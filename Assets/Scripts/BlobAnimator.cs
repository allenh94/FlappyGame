using UnityEngine;

/// <summary>
/// 鳥的 Animator 控制層（ADR-013 決策 4，blob-animator FR-19）。
/// 掛 Bird 上，持有 Animator 與 SpriteRenderer（死亡淡出目標）。
/// 職責：命令（程式→動畫）、事件（動畫→程式，state-query 核心時序）、
/// 暫停接線（訂閱 GameManager.OnStateChanged 控 animator.speed）、死亡淡出（alpha→0）。
/// 查詢一律走 GameStateAuthority（ADR-011 GA1）；GameManager 引用僅用於 OnStateChanged 事件訂閱。
/// </summary>
public class BlobAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer; // 死亡淡出目標
    [SerializeField] private GameManager gameManager; // 僅供 OnStateChanged 事件訂閱（ADR-011）

    [Header("Death Fade")]
    [SerializeField] private float fadeSpeed = 1f; // alpha 每秒線性遞減量

    // 事件（動畫→程式，供訂閱者）：
    // 命名規則（CS0102 修正）：C# 事件去 "On" 前綴（JumpRise 等）；無衝突事件維持 house style（OnDeathFadeComplete）。
    // 核心時序由 state-query 驅動（B2，ADR-013 決策 3 修訂）——Animation Event 回呼已移除，避免雙重 fire。
    /// <summary>Jump 剪輯 frame3 起點（012 蓄力播完）——PlayerController 訂閱設 flapForce（FR-10）。</summary>
    public event System.Action JumpRise;
    /// <summary>Assemble 剪輯末端——GameManager 訂閱 → SetState(Playing)（A2）＋保留 hook（未來音效）。</summary>
    public event System.Action AssembleComplete;
    /// <summary>Land 剪輯末端——保留 hook（未來落地音效/擠壓）。</summary>
    public event System.Action LandComplete;
    /// <summary>Disassemble 剪輯末端——保留 hook（未來死亡音效/解體特效；淡出由內部啟動）。</summary>
    public event System.Action DisassembleComplete;
    /// <summary>死亡淡出完成（alpha 觸底）——GameManager 訂閱 → EndGame（FR-15）。</summary>
    public event System.Action OnDeathFadeComplete;

    private bool isFadingOut;       // 死亡淡出進行中（僅 state-query 偵測 Disassemble 播完啟動）
    private bool fadeCompleteFired; // 防止 OnDeathFadeComplete 重複 fire
    private bool isCharging;        // Jump 012 蓄力中（PlayJump→true、JumpRise fire→false，FR-11）
    private bool isAssembling;      // Assemble 播放中（PlayAssemble→true、AssembleComplete fire→false，FR-05）
    // state-query once 旗標（B2）：各狀態 fire 一次後鎖定，命令方法重播時重置
    private bool jumpRiseFired, assembleCompleteFired, landCompleteFired, disassembleCompleteFired;
    private GameManager.GameState lastState; // 最近一次 OnStateChanged 快照（B1：speed 改 Update 每幀求值）

    /// <summary>Assemble 播放中查詢（T2 輸入 gate 用，FR-05/FR-11）。</summary>
    public bool IsAssembling => isAssembling;

    private void Awake()
    {
        // 防禦性解析：Animator/SpriteRenderer 與本元件同掛 Bird（T4 場景接線）
        if (animator == null)
            animator = GetComponent<Animator>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        // AN5：暫停接線——訂閱 OnStateChanged 控 animator.speed（含 Menu/Ready→0）
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>(); // 新 API，避免 CS0618（比對 UIController 模式）
        if (gameManager != null)
            gameManager.OnStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null)
            gameManager.OnStateChanged -= HandleGameStateChanged;
    }

    private void Update()
    {
        UpdateAnimatorSpeed();
        UpdateAnimationStateSignals();
        if (!isFadingOut) return;
        FadeOut();
    }

    // ── state-query 核心時序（B2，ADR-013 決策 3 修訂）──
    // 取代 Animation Event 回呼：Update 讀 AnimatorStateInfo，依 state＋normalizedTime fire C# 事件。
    // once 旗標防重複 fire；命令方法重播時重置對應旗標。

    private void UpdateAnimationStateSignals()
    {
        if (animator == null) return;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName("Jump") && info.normalizedTime >= 0.1f && !jumpRiseFired)
        { jumpRiseFired = true; 
            isCharging = false;
            JumpRise?.Invoke(); }
        if (info.IsName("Assemble") && info.normalizedTime >= 1f && !assembleCompleteFired)
        { assembleCompleteFired = true; isAssembling = false; AssembleComplete?.Invoke(); }
        if (info.IsName("Land") && info.normalizedTime >= 1f && !landCompleteFired)
        { landCompleteFired = true; LandComplete?.Invoke(); }
        if (info.IsName("Disassemble") && info.normalizedTime >= 1f && !disassembleCompleteFired&&assembleCompleteFired)
        { disassembleCompleteFired = true; DisassembleComplete?.Invoke();
           isFadingOut = true; fadeCompleteFired = false; }
    }

    // ── 命令（程式→動畫，對應 .controller 狀態轉換；狀態名與契約一致）──
    // 全部 animator.Play(name, 0, 0f) 強制從頭播（B2 雙保險——對同態重入不可靠）

    public void PlayHidden() { animator.Play("Hidden", 0, 0f); }

    public void PlayAssemble()
    {
        if (isAssembling) return; // FR-05：Assemble 播放期間無視重入
        isAssembling = true;
        assembleCompleteFired = false;
        animator.Play("Assemble", 0, 0f);
    }

    public void PlayJump()
    {
        if (isCharging || isAssembling) return; // FR-11：012 蓄力期間無視輸入；Assemble 期間無視輸入
        isCharging = true;
        jumpRiseFired = false;
        animator.Play("Jump", 0, 0f);
    }

    public void PlayLand()
    {
        if (isAssembling || isCharging) return; // Assemble/蓄力期間不切 Land（012 蓄力時鳥仍在墜落，速度<0 不應中斷 Jump）
        landCompleteFired = false;
        animator.Play("Land", 0, 0f);
    }

    public void PlayDisassemble()
    {
        disassembleCompleteFired = false;
        animator.Play("Disassemble", 0, 0f);
    }

    // ── 暫停接線（AN5，ADR-013 決策 5；B1 修復：speed 改 Update 每幀求值）──

    private void HandleGameStateChanged(GameManager.GameState state)
    {
        // B1：只存狀態快照；speed 由 Update 每幀求值——
        // A2 流程下 PlayAssemble()（設 isAssembling=true）發生在 SetState(Ready) fire 之後，
        // 若在此求值 speed 會停在 0，Assemble 凍結第一幀、AssembleComplete 永不 fire（卡死 Ready）。
        lastState = state;
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null) return; // 場景未接線（T4）防 NRE
        // A2：Ready＋Assemble 時 speed=1（Assemble 播放）；Dying 態（Disassemble 播放中）speed=1；
        // 其餘非 Playing（Menu/Ready 非 Assemble/Paused/GameOver）→ 0
        // 不碰 timeScale、不改 GameManager（ADR-006/011）
        // dying-state FR-04：Dying 態必須 speed=1——否則 Disassemble 凍結第 0 幀 → disassembleCompleteFired 永不 fire → 淡出永不啟動 → 卡死
        animator.speed = (lastState == GameManager.GameState.Playing || lastState == GameManager.GameState.Dying || isAssembling) ? 1f : 0f;
    }

    // ── 死亡淡出（AN6，H5 守衛）──

    private void FadeOut()
    {
        // H5（ADR-013 決策 8，dying-state FR-05）：淡出守衛改「非暫停即可淡出」——
        // Dying 態淡出必須執行（IsPlaying()==false 會凍結淡出 → OnDeathFadeComplete 永不 fire → EndGame 永不執行 → 卡死）；
        // Paused 態仍凍結（H5 原意「暫停時淡出凍結」保留）。查詢走 ADR-011 seam。
        if (GameStateAuthority.IsPaused()) return;

        Color color = spriteRenderer.color;
        color.a = Mathf.Max(0f, color.a - fadeSpeed * Time.deltaTime);
        spriteRenderer.color = color;

        if (color.a <= 0f && !fadeCompleteFired)
        {
            fadeCompleteFired = true;
            isFadingOut = false;
            OnDeathFadeComplete?.Invoke();
        }
    }

    /// <summary>重設視覺狀態（Restart 流程用）：恢復 alpha=1、取消進行中淡出。
    /// 死亡淡出把 alpha 降到 0，Restart 時若未恢復，鳥會隱形（Renderer.enabled=true 但 alpha=0）。
    /// 由 T2/T3 於 ShowPlayerAtStart/Reset 流程呼叫。</summary>
    public void ResetVisual()
    {
        isFadingOut = false;
        fadeCompleteFired = false;
        isCharging = false;
        isAssembling = false;
        jumpRiseFired = false;
        assembleCompleteFired = false;
        landCompleteFired = false;
        disassembleCompleteFired = false;
        
        if (spriteRenderer != null)
        {
            Color color = spriteRenderer.color;
            color.a = 1f;
            spriteRenderer.color = color;
        }
    }
}