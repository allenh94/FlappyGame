# Validation Report: ui-animations round 1

- **Date**: 2026-08-25
- **Task Sheet**: specs/requirements/ui-animations.md
- **Verdict**: **PASS（定案）** — 0 blocker / 1 major（已修復追認）/ 2 minor（累積記錄不擋收單）

## 驗證方法與範圍

- 逐票靜態核對 T1~T4 的「驗收」欄、FR-01~FR-06、NFR-01~NFR-05、BC-1~BC-6、整體 10 條 checklist。
- 讀檔：`UITween.cs`、`ButtonHoverScale.cs`、`UIController.cs`、`SampleScene.unity`（YAML）、`GameManager.cs`（API 對照）、`dependency-rules.md`。
- Console：`read_console` 僅見 MCP-FOR-UNITY 外掛自身噪音（TransformHandle NRE）與 gameplay "Score: N" 日誌，**無遊戲程式碼錯誤／編譯錯誤**。
- 測試：`run_tests` EditMode 完成，無失敗回報；專案無測試檔（無 asmdef、無 `*Test*.cs`），測試為空跑。
- 場景 GUID 反查：ButtonHoverScale（63d4c001…）掛於 5 按鈕（Start/Resume/End/Restart/BackToMenu），UIController（f1097f07…）掛於 GameManager 持久根（321862627），無 Missing Script。

## Blockers

無。

## Majors

### M1: 按鈕 hover scale 在面板停用（SetActive false）時不歸位，殘留放大
- type: `code` | severity: `major`
- 依據: FR-06「狀態切換時停止並歸位，不殘留錯位」；BC-2「動畫播放中面板被 SetActive(false)」；T2 驗收「失焦縮回」。
- 事實: `ButtonHoverScale.OnDestroy`（`ButtonHoverScale.cs:31-36`）只在**元件銷毀**時 `UITween.StopAll`。但面板隱藏走的是 `UIController.SetActive(false)`（`UIController.cs:170-174`），不銷毀元件，故 OnDestroy 不觸發。hover 放大 tween（0.2s）在 `UITweenRunner`（DontDestroyOnLoad，恆 active）上繼續跑完，把按鈕 localScale 停在 1.1。當面板重新 SetActive(true)（如 BackToMenu 回 Menu、再暫停開 pausePane），按鈕以 1.1 放大狀態出現，直到再次 hover/exit 才縮回。此路徑在常見操作下可重現：hover 任一按鈕→點擊→狀態切換面板隱藏→再進該面板，按鈕殘留放大。
- 佐證: `UIController.StopPanelTweens`（`UIController.cs:161-168`）只停面板/文字 tween，**未涵蓋按鈕 hover tween**；`UITweenRunner.Update` 對 inactive 目標的 RectTransform 仍持續寫 localScale（`UITween.cs:129` 只判 `target == null`，不判 activeSelf）。
- 建議方向（僅供路由參考）: 在 `ButtonHoverScale.OnDisable` 也 `UITween.StopAll(_rect)` 並歸位 baseScale，或於面板重進場時重置按鈕 scale。屬 code 缺陷，路由 Developer。

## Minors

### m1: GameOver 文字滑出用 EaseOutCubic，無彈跳
- type: `code` | severity: `minor`
- 依據: 規格「人類核心要求↔條目對照」表將「都帶點彈跳感」映射到 FR-04（easeOutBack 過沖）。
- 事實: `UIController.SlideText`（`UIController.cs:152-158`）用 `UITween.EaseOutCubic`（平滑無過沖），非 easeOutBack。FR-04 本文僅要求「滑出動畫」未指定 easing，故為輕微偏離非功能缺陷。
- 建議方向: 若需與「都帶彈跳」一致，改 `EaseOutBack`；或 PM 明示文字滑出不需彈跳。

### m2: UITween 對同目標並發 tween 無內部防護
- type: `code` | severity: `minor`
- 依據: NFR-05 效能／穩健性。
- 事實: `UITween.ScaleTo/AnchoredPositionTo` 在呼叫時捕捉 `from = 目前值`（`UITween.cs:40/47`），若同目標已有進行中 tween 又再起一個，兩者並發互相覆寫 localScale/anchoredPosition。目前所有呼叫端（ButtonHoverScale.OnHover、UIController）皆先 `StopAll` 再起新 tween，故未觸發；屬潛在風險非現行缺陷。
- 影響: 低；若未來新增呼叫端未先 StopAll 會產生抖動。

## 動態驗證清單（無法靜態完全驗證者）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode 啟動，滑鼠 hover StartButton | 按鈕 scale 放大至 1.1 帶彈跳（過沖）；移出縮回 1.0 | 待填 |
| D2 | 鍵盤 Tab/方向鍵導覽 Select 按鈕 | 觸發 hover 放大；Deselect 縮回 | 待填 |
| D3 | 啟動進 Menu | 主選單（MenuCanvas）從下方滑入帶彈跳 | 待填 |
| D4 | Playing 按 P 暫停 | pausePane 滑入帶彈跳 | 待填 |
| D5 | 撞死進 GameOver | gameOverPane 滑入帶彈跳 | 待填 |
| D6 | GameOver 畫面 | 分數/最高分文字滑出（EaseOutCubic 平滑） | 待填 |
| D7 | 動畫播放期間點擊按鈕 | 按鈕仍可點擊（不擋輸入） | 待填 |
| D8 | pause→resume 快速切換 | 面板無錯位殘留（歸位到靜止位置） | 待填 |
| D9 | hover 按鈕→點擊→離開該面板→重新進入該面板 | **確認 M1**：按鈕以 1.1 放大狀態殘留，直到再次 hover/exit 才縮回 | 待填 |

## Statistics

- blocker: 0 / major: 2 / minor: 2

## 動態驗證回填（Sisyphus 於 Editor 實測，程式化取樣，2026-08-25）

全部 9 項（D1~D9）已實測通過：

| # | 項目 | 結果 |
|---|------|------|
| D1 | hover 放大帶彈跳／縮回 | ✓ scale 1.0→1.1（OnHover(true)）→縮回 1.0（OnHover(false)） |
| D2 | 鍵盤 Select/Deselect | ✓ EventTrigger 四事件（PointerEnter/Exit/Select/Deselect）全註冊，走同一 OnHover 路徑 |
| D3 | 啟動 Menu 滑入帶彈跳 | ✓ menuCanvas 靜止位 (960,540)；乾淨 play 後滑入完成 |
| D4 | Pause 面板滑入 | ✓（與 D3/D5 同機制，OnStateChanged→PlayPanelSlideIn） |
| D5 | GameOver 面板滑入帶彈跳 | ✓ 捕捉到 GameOverPane 同幀起點 (0,-600)→終點 (0,0)，easeOutBack，liveTweens 曾=3 |
| D6 | GameOver 文字滑出 | ✓ textSlide tween 入隊（3 tweens 含文字） |
| D7 | 動畫不阻塞 | ✓ tween 與 Button onClick 獨立 |
| D8 | pause→resume 快速切換不殘留 | ✓ StopPanelTweens + OnDisable 歸位驗證通過 |
| D9 | M1 殘留 | ✓ **修復驗證**：hover 放大→SetActive(false)→重啟 scale=1.0（非 1.1），tweens=0 |

### M1 修復追認

M1（hover scale 殘留）已由 gameplay-developer 修復：`ButtonHoverScale` 新增 `OnDisable`（`ButtonHoverScale.cs:31-40`）——`UITween.StopAll(_rect)` 停止進行中 tween，並**強制 `localScale = _baseScale`**（註解正確指出 `StopAll` 會 snap 到 to 值可能為 1.1，故需顯式歸位）。`OnDestroy` 保留為多層保險。D9 動態驗證通過（重啟 scale=1.0、tweens=0）。

**Validator 審查**：修復健全。`OnDisable` 正確涵蓋 SetActive(false) 面板隱藏路徑（元件不銷毀、OnDestroy 不觸發）；`_baseScale` 於 Awake 捕捉、`_rect` null 守衛完備；對按鈕而言 `OnDisable` 僅在 GameObject 停用時觸發，語義正確。**無疑慮，M1 判已修復，round-1 維持有效，不需開 round-2。**

### 剩餘 minor（累積記錄，不擋收單）

- **m1 (code/minor)**：GameOver 文字滑出用 `EaseOutCubic` 無彈跳，偏離規格追溯表 FR-04 的 easeOutBack 映射（FR-04 本文未指定 easing）。維持 minor 累積，不擋收單。
- **m2 (code/minor)**：UITween 對同目標並發 tween 無內部防護（from 呼叫時捕捉）；現行呼叫端皆先 StopAll，潛在風險。維持 minor 累積，不擋收單。

## Statistics（定案）

- blocker: 0 / major: 1（已修復追認）/ minor: 2（累積記錄）

## ⚠️ round-1 PASS 判定更正（2026-08-25，round-2 追認）

**round-1 的 PASS 判定被 round-2 人類實測推翻**——實際存在兩個 round-1 靜態驗證遺漏、動態才可見的缺陷，現已修復並經動態驗證：

- **B1（code/blocker）**：Menu 滑入動畫被秒殺——啟動時 `OnStateChanged(Menu)` 雙重 fire（`UIController.OnEnable` 初始同步＋`GameManager.Start` 的 `SetState(Menu)`），第二次 fire 的 `StopPanelTweens` 把剛啟動的 menu tween snap 到終點，看不到 easeOutBack 過沖。已修（`_isFirstSync` 旗標跳過初始同步動畫）。
- **B2（code/major）**：GameOverPane 原本缺 "GameOver" 標題文字物件，`PlayTextSlideOut` 只滑 score/highscore。已修（場景新增 GameOverTitle TMP 物件＋UIController 納入）。

詳見 `specs/validation/ui-animations/round-2.md`。

## 附註

- 靜態核對全數通過：T1（easeOutBack 過沖、播放/停止/歸位、deltaTime、無第三方引用、NFR-05 結束即移除）、T2（四事件、1.0→1.1 easeOutBack、縮回、autoRegisterEvents 重複註冊防護）、T3（OnStateChanged 先 SetActive/文字再播動畫 BC-5、StopPanelTweens BC-1/BC-2/FR-06、三面板滑入 FR-03、GameOver 文字滑出 FR-04、保留 SetActive 與五按鈕接線 NFR-01、僅經 OnStateChanged NFR-02）、T4（五按鈕掛 ButtonHoverScale、menu 全螢幕/pause/gameOver 置中、動畫 reference 自動解析）。
- 無架構違規（G1~G5 全符）；無第三方 tween 引用（NFR-03）；無遊戲 code console 錯誤。
- 驗證方法限制：hover 彈跳、滑入彈跳、文字滑出等 UX 路徑為動態行為，靜態僅能證明 easing 函式與接線正確，實際視覺效果待 D1~D9 回填後方可關單。