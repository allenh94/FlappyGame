using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    /// <summary>遊戲狀態機（ADR-006 D1；dying-state FR-01：Dying 插在 Playing 之後，語意為「Playing 的死亡子階段」）。</summary>
    public enum GameState { Menu, Ready, Playing, Dying, Paused, GameOver }

    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private PipeSpawner pipeSpawner;
    [SerializeField] private BGManager backgroundManager;
    [SerializeField] private BlobAnimator blobAnimator; // 死亡動畫控制（T4 場景接線；OnEnable fallback）
    [SerializeField] private AudioManager audioManager; // 音效層（場景接線；OnEnable FindFirstObjectByType fallback）

    [Header("Boundaries")]
    [SerializeField] private float floorY = -5f;
    [SerializeField] private float ceilingY = 5f;

    private ScoreSession scoreSession; // 得分／最高分／破紀錄判定唯一真源（ADR-008）
    private ILeaderboardProvider leaderboardProvider; // 榜單持久化唯一真源（ADR-015 決策 1/2；ADR-016 事實更正 2；LB1）
    private PlayerNameStore playerNameStore; // 名稱記憶 seam（ADR-015 決策 3；LB5）
    private Vector3 playerStartPosition;
    private GameState state;

    /// <summary>狀態變更事件——UI 訂閱此事件依 GameState 切換畫面（ADR-006 D5）。</summary>
    public event System.Action<GameState> OnStateChanged;

    /// <summary>得分模組唯讀 accessor（S3）——UI／ScoreTrigger 經此讀取／計分，不直接觸碰 PlayerPrefs。</summary>
    public ScoreSession Score => scoreSession;

    /// <summary>榜單模組唯讀 accessor（S3）——UI／流程經此讀寫榜單，不直接觸碰 PlayerPrefs（LB4）。</summary>
    public ILeaderboardProvider Leaderboard => leaderboardProvider;

    /// <summary>名稱記憶 seam 唯讀 accessor（ADR-015 決策 3）——UI 經此讀寫玩家名稱，不直接觸碰 PlayerPrefs（LB5）。</summary>
    public PlayerNameStore PlayerName => playerNameStore;

    private void Awake()
    {
        scoreSession = new ScoreSession();
        // ADR-015 決策 2／ADR-016：程式碼 new LootLockerLeaderboardProvider（否決 [SerializeField] interface——Unity 序列化器不支援 interface 欄位，靜默忽略即 NRE）
        leaderboardProvider = new LootLockerLeaderboardProvider();
        // ADR-015 決策 3：名稱記憶 seam（獨立於 LeaderboardProvider 接口）
        playerNameStore = new PlayerNameStore();
        // ADR-011：註冊為 GameStateAuthority 的權威查詢實例（static singleton 生命週期）
        GameStateAuthority.Register(this);
    }

    private void OnDestroy()
    {
        // ADR-011：反註冊，避免 static 引用已銷毀實例
        GameStateAuthority.Unregister(this);
    }

    private void OnEnable()
    {
        // T3：死亡淡出完成 → EndGame（淡出由 BlobAnimator 內部啟動，不訂閱 DissasembleComplete）
        if (blobAnimator == null)
            blobAnimator = FindFirstObjectByType<BlobAnimator>(); // 場景未接線（T4）fallback（比對 BlobAnimator 模式）
        if (audioManager == null)
            audioManager = FindFirstObjectByType<AudioManager>(); // 場景未接線 fallback（比照 BlobAnimator 模式）
        if (blobAnimator != null)
        {
            blobAnimator.OnDeathFadeComplete += HandleDeathFadeComplete;
            blobAnimator.AssembleComplete += HandleAssembleComplete; // A2：Assemble 播完 → SetState(Playing)
        }
    }

    private void OnDisable()
    {
        if (blobAnimator != null)
        {
            blobAnimator.OnDeathFadeComplete -= HandleDeathFadeComplete;
            blobAnimator.AssembleComplete -= HandleAssembleComplete;
        }
    }

    /// <summary>死亡淡出完成（alpha 觸底）→ EndGame（FR-15：GameOver UI 延後到淡出後）。</summary>
    private void HandleDeathFadeComplete()
    {
        EndGame();
    }

    /// <summary>A2：Assemble 動畫播完（state-query fire）→ 遊戲開始。
    /// 防禦性守衛：僅 Ready 態接受——Assemble 在 Ready 態播放，播完才進 Playing。</summary>
    private void HandleAssembleComplete()
    {
        if (state == GameState.Ready) SetState(GameState.Playing); // 防禦性守衛
    }

    private void Start()
    {
        playerStartPosition = player.transform.position;
        // FR-02：啟動停 Menu，不直入遊戲
        SetState(GameState.Menu);
    }

    private void Update()
    {
        // BC-1 順序關鍵：地板/天花板檢查先於 P 鍵檢查。
        // 同幀死亡＋按 P 時，Die() 先置 state=Dying，P 檢查因 state 非 Playing/Paused 而跳過。
        // H1（ADR-013 決策 8，dying-state FR-07）：死亡流程期間 state==Dying，`state == Playing` 已排除——不會每幀重觸發 Die()
        if (state == GameState.Playing)
        {
            // Check floor/ceiling collision
            if (player.transform.position.y <= floorY || player.transform.position.y >= ceilingY)
            {
                Die();
            }
        }

        // T6：P 鍵暫停（僅 Playing/Paused 有效；Menu/GameOver 無視）
        if (state == GameState.Playing || state == GameState.Paused)
        {
            if (Keyboard.current.pKey.wasPressedThisFrame)
            {
                if (state == GameState.Playing) Pause();
                else Resume();
            }
        }
    }

    /// <summary>唯一狀態變異點——所有轉移一律經此設欄位＋fire 事件＋同步背景狀態。</summary>
    private void SetState(GameState newState)
    {
        state = newState;
        // 背景狀態隨 GameState 單向驅動（T2/B8）：
        // Playing 正常捲動；Menu 慢速捲動（環境感）；Paused/GameOver 凍結
        if (backgroundManager != null)
        {
            switch (newState)
            {
                case GameState.Playing: backgroundManager.SetNormal(); break;
                case GameState.Menu: backgroundManager.SetAmbient(); break;
                // FR-04：Ready 期間背景層處 ScrollingBack motion 倒回，不受凍結控制（不落入 default SetFrozen(true)）
                case GameState.Ready: break;
                // dying-state FR-03：死亡瞬間背景立即凍結（顯式 case，比照 ADR-012 case Ready 的意圖明確原則；不依賴 default 分支）
                case GameState.Dying: backgroundManager.SetFrozen(true); break;
                default: backgroundManager.SetFrozen(true); break; // Paused / GameOver
            }
        }
        if (OnStateChanged != null)
            OnStateChanged(state);
    }

    /// <summary>Menu/GameOver → Playing。先隱藏 Bird＋釋放水管（畫面只留背景倒回），
    /// 背景過渡完成（1 秒）後恢復 Bird＋重置水管，避免重生瞬間撞殘留水管。
    /// FR-03：先進 Ready 過渡態（IsPlaying() 立即 false，倒帶期間各系統守衛生效），
    /// 倒帶完成才與 ShowPlayerAtStart 同幀進 Playing（FR-05 同幀原子）。</summary>
    public void StartGame()
    {
        // FR-02 重入擋：Ready 過渡期間重複呼叫被忽略（不重啟倒帶、不重設分數）
        // dying-state（Tech Lead 建議）：Dying 態也擋——死亡流程中不應重啟新局（防禦性硬化，比照 ADR-012 Ready 先例）
        if (state == GameState.Playing || state == GameState.Ready || state == GameState.Dying) return;
        scoreSession.Begin(); // 重置本局分數／破紀錄旗標（ADR-008，fire OnScoreChanged(0)）
        SetState(GameState.Ready); // 先進過渡態——倒帶期間 IsPlaying()==false
        HidePlayerAndPipes(); // 隱藏 Bird＋釋放水管，畫面只留背景倒回
        if (backgroundManager != null)
            backgroundManager.ScrollBackToStart(() => {
                if (backgroundManager != null) backgroundManager.SetFrozen(true); // 背景凍結（Assemble 期間）
                ShowPlayerAtStart(); // 鳥現身＋PlayAssemble（Ready 態播）
            });
        else
            { if (backgroundManager != null) backgroundManager.SetFrozen(true); ShowPlayerAtStart(); }
    }

    /// <summary>Playing → Paused。</summary>
    public void Pause()
    {
        if (state != GameState.Playing) return;
        SetState(GameState.Paused);
    }

    /// <summary>Paused → Playing。</summary>
    public void Resume()
    {
        if (state != GameState.Paused) return;
        SetState(GameState.Playing);
    }

    /// <summary>GameOver 唯一入口——死亡與暫停→結束共用（FR-07）。</summary>
    public void EndGame()
    {
        // dying-state FR-02：死亡淡出完成時 state 是 Dying（OnDeathFadeComplete → EndGame）；
        // 保留 Paused 給「暫停→結束」按鈕路徑。
        if (state != GameState.Dying && state != GameState.Paused) return;
        // 破紀錄判定（快照→計算→覆寫）封閉在 ScoreSession.End() 內（S2），
        // 須在 SetState(GameOver) fire OnStateChanged 前呼叫，UI 才能讀到正確的 RecordBroken/PreviousHighScore。
        scoreSession.End();
        SetState(GameState.GameOver);
    }

    /// <summary>GameOver → Menu。隱藏 Bird 與水管，只留背景反向快退到初始位（1 秒過渡）。</summary>
    public void BackToMenu()
    {
        if (state != GameState.GameOver) return;
        HidePlayerAndPipes(); // 隱藏 Bird＋釋放水管，Menu 僅見背景倒回動畫
        SetState(GameState.Menu);
        if (backgroundManager != null) backgroundManager.ScrollBackToMenu();
    }

    /// <summary>隱藏 Bird 與釋放所有水管（回 Menu / Restart 用）——畫面只留背景過渡動畫。
    /// 用 Renderer+collider 隱藏（ADR-007），非 SetActive 停用 gameplay 物件。</summary>
    private void HidePlayerAndPipes()
    {
        if (player != null) player.SetVisible(false);
        if (pipeSpawner != null) pipeSpawner.ResetAll();
    }

    /// <summary>顯示 Bird 並重置（Restart 過渡完成後呼叫）。</summary>
    private void ShowPlayerAtStart()
    {
        player.SetVisible(true);
        player.Reset(playerStartPosition);
        if (blobAnimator != null)
        {
            blobAnimator.ResetVisual();   // 恢復 alpha=1＋清 gate 旗標
            blobAnimator.PlayAssemble();  // Ready 態播 Assemble（BlobAnimator speed 每幀求值＝1，B1 修復後）；播完 fire AssembleComplete → HandleAssembleComplete → SetState(Playing)
        }
    }

    /// <summary>死亡流程入口（FR-15/FR-18，dying-state）：僅 Playing 態死亡 → SetState(Dying) → 凍結鳥＋停 collider → 播 Disassemble。
    /// 死亡流程進入 Dying 態（FR-16「維持 Playing」已撤銷）——IsPlaying() 立即 false，水管/生成/計分自查守衛自然凍結；
    /// EndGame() 延後到淡出完成（OnDeathFadeComplete）後。重入防護由 state 承擔（state==Dying 時 return，BC-3）。</summary>
    public void Die()
    {
        if (state != GameState.Playing) return;      // 守衛＝重入防護（BC-3）：state==Dying 時直接 return
        SetState(GameState.Dying);                    // dying-state FR-02：死亡瞬間進 Dying 態（背景立即凍結）
        player.Die();                                 // 凍結原地＋速度歸零（PlayerController.Die 既有）
        // PlayerController.Die() 未停 collider（T2 領域）——T3 用既有 public 介面補停（FR-15）
        Collider2D bodyCollider = player.GetComponent<Collider2D>();
        if (bodyCollider != null) bodyCollider.enabled = false;
        blobAnimator.PlayDisassemble();               // 播死亡動畫（T4 場景接線）
        if (audioManager != null) audioManager.PlayGameOver(); // 死亡瞬間音效（僅 Die() 路徑，每次死亡一次）
    }

    /// <summary>GameOver → Playing（等同 StartGame）。</summary>
    public void Restart()
    {
        if (state != GameState.GameOver) return;
        StartGame();
    }

    /// <summary>NFR-01 語義保持：ScoreTrigger/CollisionHandler 相容。</summary>
    public bool IsPlaying()
    {
        return state == GameState.Playing;
    }

    /// <summary>暫停查詢（dying-state，2026-09-30）——BlobAnimator 淡出守衛用（非暫停即可淡出）。查詢非命令。</summary>
    public bool IsPaused()
    {
        return state == GameState.Paused;
    }

    public GameState GetGameState()
    {
        return state;
    }
}