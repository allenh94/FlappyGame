using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// UI 控制腳本（ADR-006 D4-D6；leaderboard ADR-015）。
/// 掛持久根（GameManager 根或專屬 "UI" 根），不掛會被停用的 Canvas。
/// 訂閱 GameManager.OnStateChanged，依 GameState 切換 MenuCanvas / OverlayCanvas 顯示。
/// 按鈕接線：UI→GameManager 公開方法（NFR-05 低耦合）。
/// LeaderboardScreen 為「非 GameState 顯示」（ADR-015 決策 7／LB8）——顯示/隱藏由顯示旗標
/// （menuBrowseMode／gameOverQualifyMode）管理，不經 OnStateChanged state 切換；
/// 隱藏掛 OnStateChanged 開頭（StopPanelTweens 旁）＋返回鈕事件。
/// </summary>
public class UIController : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private GameObject menuCanvas;
    [SerializeField] private GameObject overlayCanvas;
    [SerializeField] private GameObject leaderboardScreen; // 獨立 LeaderboardScreen Canvas（FR-08，非 Menu/Overlay 子物件）

    [Header("HUD（live 計分）")]
    [SerializeField] private TextMeshProUGUI scoreText; // 遊玩中逐分更新的 HUD ScoreText（ScoreCanvas 下，恆可見）

    [Header("Overlay Panes（單面板雙態）")]
    [SerializeField] private GameObject pausePane;      // Paused：繼續/結束
    [SerializeField] private GameObject gameOverPane;   // GameOver：重新開始/回主選單＋分數（High Score 顯示已退休，FR-12）
    [SerializeField] private TextMeshProUGUI gameOverScoreText;
    [SerializeField] private TextMeshProUGUI gameOverTitleText; // GameOver 標題（FR-04）

    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button endButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private Button menuLeaderboardButton;    // Menu 排行榜按鈕（決策 B=i）
    [SerializeField] private Button leaderboardBackButton;    // LeaderboardScreen 返回主畫面（決策 C=α）
    [SerializeField] private Button leaderboardRestartButton; // LeaderboardScreen 重新開始（決策 C=α）

    [Header("Leaderboard（FR-08~14）")]
    [SerializeField] private LeaderboardAnimator leaderboardAnimator; // 演出元件（ADR-015 決策 5）
    [SerializeField] private GameObject nameModal;            // 名稱輸入 modal（決策 A=a，T6）
    [SerializeField] private TMP_InputField nameInputField;   // TMP Input Field（上限 12 字元）
    [SerializeField] private Button nameConfirmButton;        // 確認（留空＝取消，BC-4）
    [SerializeField] private Button nameCancelButton;         // 取消

    [Header("References")]
    [SerializeField] private GameManager gameManager;

    [Header("Animation（面板滑入／文字滑出）")]
    [SerializeField] private RectTransform menuCanvasRect;      // 滑入目標（null 時由 menuCanvas 自動解析）
    [SerializeField] private RectTransform pauseRect;           // 滑入目標（null 時由 pausePane 自動解析）
    [SerializeField] private RectTransform gameOverPaneRect;    // 滑入目標（null 時由 gameOverPane 自動解析）
    [SerializeField] private Vector2 slideInOffset = new Vector2(0f, -600f); // 面板 off-screen 起點（相對靜止位置）
    [SerializeField] private float slideInDuration = 0.5f;
    [SerializeField] private Vector2 textSlideOffset = new Vector2(0f, -80f); // GameOver 文字滑出偏移
    [SerializeField] private float textSlideDuration = 0.4f;

    private bool _isFirstSync; // 初始同步旗標：OnEnable 首次同步不觸發進場動畫（避免被 GameManager.Start 第二次 fire 秒殺）
    private bool _menuBrowseMode;      // LeaderboardScreen 瀏覽模式（Menu 態，決策 B=i）
    private bool _gameOverQualifyMode; // LeaderboardScreen 上榜模式（GameOver 態，決策 C=α）
    private bool _nameModalActive;     // 名稱 modal 顯示中（Esc 取消用）
    private LeaderboardEntry[] _pendingEntries; // 上榜流程 LoadAll 結果（取消復原用）

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        ResolveAnimationTargets();
        ResolveScoreText();
        WireButtons();
    }

    /// <summary>HUD ScoreText 自動解析：未接線時依名稱精確找 "ScoreText"（避免 FindObjectOfType 抓到
    /// Menu/GameOver 其他 TMP）。顯示職責歸 UI（B1 修復），GameManager 不再握 scoreText。</summary>
    private void ResolveScoreText()
    {
        if (scoreText != null) return;
        var go = GameObject.Find("ScoreText");
        if (go != null) scoreText = go.GetComponent<TextMeshProUGUI>();
    }

    /// <summary>動畫面板 RectTransform 自動解析（T4 可只接 GameObject，或直接指定 RectTransform）。
    /// 注意：Menu 必須用「包裝 Panel（MenuPanel）」而非根 Canvas——根 Screen Space Overlay Canvas 的
    /// anchoredPosition 位移視覺無效，tween 根 Canvas 不會看到滑入。panel 是 child，位移會視覺生效。</summary>
    private void ResolveAnimationTargets()
    {
        if (menuCanvasRect == null && menuCanvas != null)
        {
            // 找 MenuCanvas 下的 MenuPanel 包裝層；若無則退回 menuCanvas（舊結構相容）
            var menuPanel = menuCanvas.transform.Find("MenuPanel");
            menuCanvasRect = (menuPanel != null ? menuPanel : menuCanvas.transform).GetComponent<RectTransform>();
        }
        if (pauseRect == null && pausePane != null)
            pauseRect = pausePane.GetComponent<RectTransform>();
        if (gameOverPaneRect == null && gameOverPane != null)
            gameOverPaneRect = gameOverPane.GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (gameManager != null)
        {
            gameManager.OnStateChanged += OnStateChanged;
            // B1 修復：訂閱 ScoreSession 逐分事件，遊玩中 HUD 即時更新（經 GameManager.Score accessor，S3）
            gameManager.Score.OnScoreChanged += OnScoreChanged;
            // 初始同步：與實際 GameManager state 一致，避免首次畫面錯位。
            // 但初始同步不觸發進場動畫——否則 GameManager.Start() 的第二次 fire 會 Stop 掉剛啟動的 tween（秒殺）。
            _isFirstSync = true;
            OnStateChanged(gameManager.GetGameState());
            _isFirstSync = false;
        }
    }

    private void OnDisable()
    {
        if (gameManager != null)
        {
            gameManager.OnStateChanged -= OnStateChanged;
            gameManager.Score.OnScoreChanged -= OnScoreChanged;
        }
    }

    private void Update()
    {
        // T6：Esc 取消名稱輸入（Input System；專案 activeInputHandler=1，StandaloneInputModule 禁用）
        if (_nameModalActive && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            CancelNameInput();
    }

    /// <summary>逐分事件處理：HUD ScoreText 即時更新為 "Score: N"（B1 修復）。</summary>
    private void OnScoreChanged(int score)
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }

    /// <summary>按鈕接線（Awake 執行一次，避免 OnEnable 重複 AddListener 累積）。</summary>
    private void WireButtons()
    {
        if (startButton != null) startButton.onClick.AddListener(() => gameManager.StartGame());
        if (resumeButton != null) resumeButton.onClick.AddListener(() => gameManager.Resume());
        if (endButton != null) endButton.onClick.AddListener(() => gameManager.EndGame());
        if (restartButton != null) restartButton.onClick.AddListener(() => gameManager.Restart());
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(() => gameManager.BackToMenu());
        // 決策 B=i：Menu 排行榜按鈕 → LeaderboardScreen 瀏覽模式
        if (menuLeaderboardButton != null) menuLeaderboardButton.onClick.AddListener(ShowLeaderboardBrowse);
        // 決策 C=α：LeaderboardScreen 返回主畫面（Menu 態回主面板；GameOver 態 BackToMenu）
        if (leaderboardBackButton != null) leaderboardBackButton.onClick.AddListener(OnLeaderboardBack);
        // 決策 C=α：LeaderboardScreen 重新開始（僅上榜模式顯示）
        if (leaderboardRestartButton != null) leaderboardRestartButton.onClick.AddListener(() => gameManager.Restart());
        // 決策 A=a：名稱 modal 確認/取消
        if (nameConfirmButton != null) nameConfirmButton.onClick.AddListener(ConfirmNameInput);
        if (nameCancelButton != null) nameCancelButton.onClick.AddListener(CancelNameInput);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        // BC-1/BC-2：狀態切換時停止所有進行中的面板/文字動畫並歸位（FR-06）
        StopPanelTweens();
        // ADR-015 決策 7：LeaderboardScreen 隱藏掛 OnStateChanged 開頭（非 GameState 顯示，state 切換時一律收起）
        HideLeaderboardScreen();

        switch (state)
        {
            case GameManager.GameState.Menu:
                SetActive(menuCanvas, true);
                SetActive(overlayCanvas, false);
                // FR-12：High Score 顯示退休——不再更新 menuHighScoreText
                if (!_isFirstSync) PlayPanelSlideIn(menuCanvasRect);
                break;

            case GameManager.GameState.Ready:
            case GameManager.GameState.Playing:
            case GameManager.GameState.Dying:
                SetActive(menuCanvas, false);
                SetActive(overlayCanvas, false);
                // M1 修復：不再於此硬寫 "Score: 0"——此 case 在 Resume（Paused→Playing）也會 fire，
                // 硬寫會把暫停前分數誤重置為 0。新局歸零改由 ScoreSession.Begin() fire OnScoreChanged(0) 驅動
                // （僅 StartGame/Restart 呼叫 Begin，Resume 不呼叫），HUD 訂閱事件自然重置。
                break;

            case GameManager.GameState.Paused:
                SetActive(menuCanvas, false);
                SetActive(overlayCanvas, true);
                SetActive(pausePane, true);
                SetActive(gameOverPane, false);
                if (!_isFirstSync) PlayPanelSlideIn(pauseRect);
                break;

            case GameManager.GameState.GameOver:
                SetActive(menuCanvas, false);
                SetActive(overlayCanvas, true);
                SetActive(pausePane, false);
                SetActive(gameOverPane, true);
                // BC-5：先寫文字再播動畫
                if (gameOverTitleText != null)
                    gameOverTitleText.text = "GameOver";
                if (gameOverScoreText != null)
                    gameOverScoreText.text = "Score: " + gameManager.Score.CurrentScore;
                // 上榜流程（FR-13）：LoadAll → Qualifies → 上榜顯示 LeaderboardScreen（取代 GameOverPane，決策 C=α）
                // 未上榜維持 GameOverPane（High Score 文字已退休，FR-12）。進場動畫在 callback 內依上榜與否播放。
                HandleGameOverQualify();
                break;
        }
    }

    /// <summary>上榜流程（FR-13）：GameOver 態 → LoadAll → Qualifies → 上榜顯示 LeaderboardScreen＋名稱 modal；
    /// 未上榜維持 GameOverPane。callback 內執行顯示動作本身（同步回呼容忍，ADR-015 決策 4）。</summary>
    private void HandleGameOverQualify()
    {
        if (gameManager == null || gameManager.Leaderboard == null) return;
        int score = gameManager.Score.CurrentScore;
        gameManager.Leaderboard.LoadAll(entries =>
        {
            // BC-6 守衛：callback 內以「目前狀態仍為 GameOver」守衛（比照 _isFirstSync 狀態快照語義）
            if (gameManager.GetGameState() != GameManager.GameState.GameOver) return;
            _pendingEntries = entries;
            if (LeaderboardRules.Qualifies(score, entries))
            {
                // 上榜（決策 C=α）：LeaderboardScreen 取代 GameOverPane
                SetActive(gameOverPane, false);
                SetActive(leaderboardScreen, true);
                _gameOverQualifyMode = true;
                SetActive(leaderboardRestartButton != null ? leaderboardRestartButton.gameObject : null, true);
                // 先渲染既有榜單（瀏覽式，Submit 前不加入新條目）
                if (leaderboardAnimator != null)
                    leaderboardAnimator.Render(entries);
                ShowNameModal();
            }
            else
            {
                // 未上榜：既有 GameOverPane（Score/GameOverTitle 保留，無 High Score 文字）
                if (!_isFirstSync)
                {
                    PlayPanelSlideIn(gameOverPaneRect);
                    PlayTextSlideOut();
                }
            }
        });
    }

    /// <summary>Menu 態瀏覽（決策 B=i）：Menu 排行榜按鈕 → 顯示 LeaderboardScreen 瀏覽模式（禁 Submit）。</summary>
    private void ShowLeaderboardBrowse()
    {
        if (gameManager == null || gameManager.Leaderboard == null) return;
        gameManager.Leaderboard.LoadAll(entries =>
        {
            // Menu 態瀏覽：state 維持 Menu（不走 OnStateChanged）
            if (gameManager.GetGameState() != GameManager.GameState.Menu) return;
            _menuBrowseMode = true;
            SetActive(menuCanvas, false);
            SetActive(leaderboardScreen, true);
            // 瀏覽模式：只顯示返回鈕（隱藏重新開始鈕）
            SetActive(leaderboardRestartButton != null ? leaderboardRestartButton.gameObject : null, false);
            if (leaderboardAnimator != null)
                leaderboardAnimator.Render(entries);
        });
    }

    /// <summary>LeaderboardScreen 返回鈕（FR-11）：Menu 態回 MenuCanvas 主面板；GameOver 上榜態 BackToMenu。</summary>
    private void OnLeaderboardBack()
    {
        if (_menuBrowseMode)
            HideLeaderboardBrowse();
        else if (_gameOverQualifyMode)
            gameManager.BackToMenu();
    }

    /// <summary>瀏覽模式返回：隱藏 LeaderboardScreen、回 Menu 主面板（state 維持 Menu）。</summary>
    private void HideLeaderboardBrowse()
    {
        _menuBrowseMode = false;
        if (leaderboardAnimator != null) leaderboardAnimator.StopAll(); // BC-5：離開路徑中斷歸位
        SetActive(leaderboardScreen, false);
        SetActive(menuCanvas, true);
    }

    /// <summary>隱藏 LeaderboardScreen（OnStateChanged 開頭＋離開路徑）。重置顯示旗標與名稱 modal。</summary>
    private void HideLeaderboardScreen()
    {
        _menuBrowseMode = false;
        _gameOverQualifyMode = false;
        _nameModalActive = false;
        SetActive(nameModal, false);
        SetActive(leaderboardScreen, false);
    }

    /// <summary>顯示名稱輸入 modal（決策 A=a，T6）：預填上次輸入（經 gameManager.PlayerName，PlayerNameStore seam）。</summary>
    private void ShowNameModal()
    {
        if (nameModal == null) return;
        // 預填上次輸入（NFR-03：UI 不直接觸碰持久化，名稱一律經 gameManager.PlayerName seam）
        if (nameInputField != null)
            nameInputField.text = gameManager.PlayerName.Load();
        SetActive(nameModal, true);
        _nameModalActive = true;
        if (nameInputField != null) nameInputField.ActivateInputField();
    }

    /// <summary>確認名稱（FR-13）：留空按確認＝取消（BC-4）；非空 → 記憶名稱＋Submit → callback 內 PlayQualify。</summary>
    private void ConfirmNameInput()
    {
        if (!_nameModalActive) return;
        string name = nameInputField != null ? nameInputField.text : string.Empty;
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // 留空按確認＝取消（BC-4）：丟棄不上榜
            CancelNameInput();
            return;
        }
        // 上限 12 字元（PlayerNameStore 內部亦裁剪防護，決策 A=a）
        if (name.Length > 12) name = name.Substring(0, 12);
        // 記憶名稱（下次預填）
        gameManager.PlayerName.Save(name);
        // 提交（FR-13／ADR-015 修訂 6）：Submit 確認後才播 Qualify Animation
        var entry = new LeaderboardEntry { playerName = name, score = gameManager.Score.CurrentScore };
        SetActive(nameModal, false);
        _nameModalActive = false;
        gameManager.Leaderboard.Submit(entry, result =>
        {
            // BC-6 守衛：callback 內以「目前狀態仍為 GameOver」守衛
            if (gameManager.GetGameState() != GameManager.GameState.GameOver) return;
            // Submit callback 內禁再 LoadAll（LB2）：SubmitResult.Entries 已含提交後完整榜單
            if (!result.Qualified) return; // 修 validator m1：伺服器權威 Qualified——未上榜不播上榜動畫
            if (leaderboardAnimator != null)
                leaderboardAnimator.PlayQualify(result);
        });
    }

    /// <summary>取消名稱輸入（BC-4/NFR-06）：不 Submit、不播動畫——隱藏 modal、榜單維持既有條目、留在瀏覽狀態。</summary>
    private void CancelNameInput()
    {
        if (!_nameModalActive) return;
        SetActive(nameModal, false);
        _nameModalActive = false;
        // 榜單復原（維持 _pendingEntries 渲染，無新條目）
        if (leaderboardAnimator != null && _pendingEntries != null)
            leaderboardAnimator.Render(_pendingEntries);
    }

    /// <summary>面板進場滑入（FR-03）：從 off-screen 起點滑到靜止位置，easeOutBack 彈跳。</summary>
    private void PlayPanelSlideIn(RectTransform rect, System.Action onComplete = null)
    {
        if (rect == null || !rect.gameObject.activeSelf) return;
        Vector2 resting = rect.anchoredPosition;
        rect.anchoredPosition = resting + slideInOffset; // 設 off-screen 起點
        UITween.AnchoredPositionTo(rect, resting, slideInDuration, 0f, UITween.EaseOutBack, onComplete);
    }

    /// <summary>GameOver 標題/分數文字滑出（FR-04；High Score 文字已退休，FR-12）。</summary>
    private void PlayTextSlideOut()
    {
        if (gameOverTitleText != null)
            SlideText(gameOverTitleText.rectTransform);
        if (gameOverScoreText != null)
            SlideText(gameOverScoreText.rectTransform);
    }

    private void SlideText(RectTransform rect)
    {
        if (rect == null) return;
        Vector2 resting = rect.anchoredPosition;
        rect.anchoredPosition = resting + textSlideOffset;
        UITween.AnchoredPositionTo(rect, resting, textSlideDuration, 0f, UITween.EaseOutBack);
    }

    /// <summary>停止所有面板/文字 tween 並歸位（FR-06/BC-1/BC-2）。
    /// 排行榜演出中斷歸位（ADR-015 決策 7／LB8）：離開路徑（返回/BackToMenu/Restart/OnStateChanged 開頭）呼叫 StopAll。</summary>
    private void StopPanelTweens()
    {
        if (menuCanvasRect != null) UITween.StopAll(menuCanvasRect);
        if (pauseRect != null) UITween.StopAll(pauseRect);
        if (gameOverPaneRect != null) UITween.StopAll(gameOverPaneRect);
        if (gameOverTitleText != null) UITween.StopAll(gameOverTitleText.rectTransform);
        if (gameOverScoreText != null) UITween.StopAll(gameOverScoreText.rectTransform);
        // FR-12：High Score 退休——不再停止 gameOverHighScoreText tween
        if (leaderboardAnimator != null) leaderboardAnimator.StopAll(); // BC-5：停止榜單全部 tween 並歸位
    }

    private void SetActive(GameObject go, bool active)
    {
        if (go != null)
            go.SetActive(active);
    }
}