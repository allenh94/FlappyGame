# Validation Report: ui-animations round 2

- **Date**: 2026-08-25
- **Task Sheet**: specs/requirements/ui-animations.md
- **Verdict**: **PASS（定案）** — 0 blocker / 0 major / 1 minor（m2 累積記錄）

## 背景：round-1 誤判更正

round-1 標 PASS 後，人類於 Editor 實測回報兩個缺陷，皆為 **round-1 靜態驗證遺漏、動態才可見**。本輪追認並確認修復。**round-1 的 PASS 判定被推翻**——實際存在 B1(blocker)/B2(major) 兩缺陷，現已修復並經動態驗證。

## Blockers

### B1: Menu 滑入動畫被秒殺，看不到 easeOutBack 過沖
- type: `code` | severity: `blocker`
- 依據: FR-03「面板進場滑入帶彈跳」；整體驗收「Menu 滑入：啟動進 Menu 時，主選單滑入帶彈跳」。
- 事實: 啟動時 `OnStateChanged(Menu)` 被觸發**兩次**——(1) `UIController.OnEnable` 初始同步呼叫（`UIController.cs:67-78`），(2) `GameManager.Start` 的 `SetState(GameState.Menu)`（`GameManager.cs:37`）再 fire 一次。第二次 fire 開頭的 `StopPanelTweens()` 呼叫 `UITween.StopAll(menuRect)`，而 `StopAll`（`UITween.cs:51-55`）snap 到 to 值（540）——把第一次 `PlayPanelSlideIn` 剛啟動的 menu tween 立即取消並歸位到終點，動畫被秒殺。人類實測看不到彈跳，Menu 瞬間出現。
- 修復（已落地）: `UIController` 新增 `_isFirstSync` 旗標（`UIController.cs:46`）——OnEnable 初始同步呼叫 `OnStateChanged(GetGameState())` 時設 `_isFirstSync=true` 跳過進場動畫（`UIController.cs:74/108/121/136`），`GameManager.Start` 的 `SetState(Menu)` 才是唯一觸發真實動畫的點。保留初始同步的必要性（畫面與實際 state 對齊），只是不重播動畫。
- **動態驗證（Sisyphus 於 Editor 逐幀採樣，已過）**: 在 `UITweenRunner.Update` 加臨時 log，捕捉到完整 easeOutBack 曲線——frame=1 p=0.04 y=47、frame=2 y=142、frame=3 p=0.747 **e=1.065 y=579（超過 resting 540 = 過沖出現）**、frame=4+ e 遞減回 1.000、y 回落 540。**過沖 579>540 真實存在**，動畫完整播放不被秒殺。臨時 log 已移除。
- 建議方向: 已修復，無需再修。

## Majors

### B2: GameOver 字樣沒出現
- type: `code` | severity: `major`
- 依據: FR-04「GameOver 字樣滑出」；整體驗收「GameOver 字樣滑出：gameOverPane 分數/標題文字滑出」。
- 事實: GameOverPane 場景中**原本沒有 "GameOver" 標題文字物件**，只有 GameOverScoreText、GameOverHighScoreText 與 Restart/BackToMenu 按鈕。`PlayTextSlideOut()`（修復前 `UIController.cs:154-163`）原本只滑動 score/highscore 兩個文字，沒有 GameOver 標題。人類死亡進 GameOver 看不到「GameOver」字樣。
- **修復（已落地）**: (1) 場景在 GameOverPane 下新增「GameOverTitle」TMP 文字物件（`SampleScene.unity:3449`，text="GameOver"、fontSize=48、黃色、Kenney Future 字型 guid 75f5b1e8、anchoredPos (0,180)）；(2) `UIController` 新增 `[SerializeField] TextMeshProUGUI gameOverTitleText`（`UIController.cs:25`），GameOver 分支先寫 "GameOver"（`UIController.cs:130-131`）再納入 `PlayTextSlideOut`（`UIController.cs:157-158`）與 `StopPanelTweens`（`UIController.cs:179`）。
- **動態驗證（Sisyphus 已過）**: 進 GameOver 態後讀取，`GameOverTitle` 存在、text="GameOver"、動畫完成後 anchoredPos (0,100)。
- 建議方向: 已修復，無需再修。

## Minors

### m1（已修復，round-2 追認）: GameOver 文字滑出用 EaseOutCubic 無彈跳
- type: `code` | severity: `minor`（round-1 記載，本輪已修）
- 事實: round-1 記載 `SlideText` 用 `EaseOutCubic`。修復後 `UIController.cs:170` 已改為 `UITween.EaseOutBack`——文字滑出現帶彈跳，與規格追溯表 FR-04 的 easeOutBack 映射一致。**m1 已解決。**

### m2（累積）: UITween 對同目標並發 tween 無內部防護
- type: `code` | severity: `minor`
- 事實: `UITween.ScaleTo/AnchoredPositionTo` 在呼叫時捕捉 `from = 目前值`（`UITween.cs:40/47`），若同目標已有進行中 tween 又再起一個，兩者並發互相覆寫。目前所有呼叫端皆先 `StopAll` 再起新 tween，未觸發。維持 minor 累積，不擋收單。

## 動態驗證清單（本輪）

| # | 步驟 | 預期觀察 | 結果 |
|---|------|----------|------|
| D1 | 啟動進 Menu | Menu 滑入帶 easeOutBack 過沖（y 超過 resting 540 再回落） | ✓ 過沖 579>540 真實存在，動畫完整播放 |
| D2 | 死亡進 GameOver | GameOverTitle 顯示 "GameOver" 並滑出 | ✓ 存在、text="GameOver"、動畫完成 anchoredPos (0,100) |

## Statistics

- blocker: 1（B1，已修復）/ major: 1（B2，已修復）/ minor: 2（m1 已修復、m2 累積）

## 附註

- **round-1 PASS 判定被 round-2 人類實測推翻**：B1（啟動雙重 fire 秒殺動畫）與 B2（GameOverPane 缺標題文字）皆為動態才可見的缺陷，round-1 靜態驗證無法捕捉。教訓：UI 動畫類驗收的「啟動時序」與「場景物件完整性」需動態驗證補位，靜態 PASS 不可視為定案。
- **`_isFirstSync` 修法架構審查**：修法依賴「`GameManager.Start` 的 `SetState(Menu)` 在 `UIController.OnEnable` 之後 fire」的時序。Unity 保證 OnEnable 先於 Start 執行，故現行成立。**殘留風險（minor）**：此為隱性時序耦合——若未來 GameManager 改在 Awake 設初始 state、或 UIController 掛到 enable 晚於 GameManager.Start 的物件上，動畫可能被跳過或雙播。建議 Tech Lead 於未來重構時考慮以「初始 state 是否已 fire 過」取代「旗標＋時序依賴」，但現行不擋收單。
- Console 僅見 UITweenRunner DontDestroyOnLoad 物件於場景關閉時的 Unity 標準警告（持久 singleton 預期行為，非遊戲缺陷）；無遊戲 code 錯誤。

## ⚠️ round-2 PASS 判定更正（2026-08-25，round-3 追認）

**round-2 雖修了「雙重 fire 秒殺」的 B1，但未命中真正的根因**——人類再次實測發現 Menu 仍瞬間出現、無過渡動畫。round-2 的 `_isFirstSync` 修法仍有效（避免雙重 fire），但動畫目標是 **MenuCanvas 根（Screen Space Overlay 根 Canvas）**，其 anchoredPosition 位移對視覺渲染無效——tween 有播放、有過沖，但畫面不動。

- **B3（code/blocker）**：Menu 滑入動畫視覺無效，因動畫目標是根 Canvas 而非 child RectTransform。已修（場景新增 MenuPanel 包裝層＋UIController 解析 MenuPanel 為動畫目標）。

詳見 `specs/validation/ui-animations/round-3.md`。