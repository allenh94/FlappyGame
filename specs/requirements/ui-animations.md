# 規格：UI 動畫（按鈕 hover／面板滑入彈跳／GameOver 字樣滑出／破紀錄分數跳動）（ui-animations）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **no**（純 UI 附加動畫；核心僅加最高分快照欄位，無架構變更、無新跨系統依賴） ｜ Created: 2026-08-25
> 前置依賴：**game-states 已 PASS**（UIController.cs、MenuCanvas/OverlayCanvas、OnStateChanged、EventSystem 均已上線）。

## 功能：UI 動畫系統

- **目標**：讓 UI 帶「動畫感」——按鈕 hover 放大、面板進場滑出帶彈跳、GameOver 字樣滑出、破紀錄時最高分整數逐格跳動，全部有彈跳（bounce）感。
- **性質**：**附加動畫層**，疊在既有 game-states UI 之上。不改造 gameplay 核心、不新增第三方套件、不破壞已驗收的 UI 切換邏輯。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script（新） | `Assets/Scripts/UI/UITween.cs` | **新增**（easing 函式庫 + Tween 執行器，純 C# 自建，無第三方套件；含泛用 `FloatTo`/`ValueTween` 供分數跳動） |
| Script（新） | `Assets/Scripts/UI/ButtonHoverScale.cs` | **新增**（按鈕 hover/Select 放大動畫元件，用 UITween） |
| Script | `Assets/Scripts/UI/UIController.cs` | **修改**（在 OnStateChanged 內觸發面板進場動畫；保留既有 SetActive 切換與按鈕接線；GameOver 破紀錄時先瞬間顯示舊最高分，面板滑入後啟動分數跳動） |
| Script | `Assets/Scripts/GameManager.cs` | **修改**（`EndGame()` 在 `SaveHighScore()` 前快照 `previousHighScore`＋`DidBreakRecord` 旗標，供 UI 判定是否播分數跳動） |
| Scene | `Assets/Scenes/SampleScene.unity` | **修改**（按鈕掛 EventTrigger、動畫 reference 接線、面板初始 off-screen 定位） |
| Prefab | `Assets/Prefabs/UI/`（若存在） | **可能修改**（按鈕/面板 prefab 掛動畫元件） |

## 現況診斷（已驗證事實，附出處）

1. **無第三方 tween 套件**：`Assets/` 全目錄 grep `DOTween|LeanTween|iTween|Tween` 零命中（本 session 實測）。動畫必須自建或走 Animator。
2. **UIController 用 SetActive 切換**：`UIController.cs:105-109` `SetActive()` 包裝 `go.SetActive(active)`；`:69-103` `OnStateChanged` 依 GameState 切換 MenuCanvas/OverlayCanvas 與 pausePane/gameOverPane 顯示，並寫分數/最高分文字。
3. **狀態事件已存在**：`UIController.cs:43-51` `OnEnable` 訂閱 `gameManager.OnStateChanged`，`OnDisable` 退訂；初始同步呼叫 `OnStateChanged(gameManager.GetGameState())`。
4. **按鈕接線在 Awake**：`UIController.cs:36-41` `Awake` 呼叫 `WireButtons()`（`onClick.AddListener`），避免 OnEnable 重複累積。
5. **凍結守則**：禁用 `Time.timeScale=0`（game-states FR-08）；UI Canvas 顯示切換仍用 SetActive。**timeScale 恆為 1**，故 UI 動畫用 `deltaTime` 即可正常播放（含 Paused/GameOver 期間）。
6. **Button 內建 transition 不支援 scale**：Button transition 僅 ColorTint/ColorSwap/SpriteSwap/Animation（Unity 內建事實）。scale hover 需自建（EventTrigger + 程式）或走 Animation transition（需 AnimatorController）。

### 未驗證假設（不得視為既定事實）

- **假設 A**：`EventTrigger` 的 `PointerEnter`／`PointerExit` 事件在 InputSystemUIInputModule 下正常觸發。依專案 activeInputHandler=1 應成立，但未在 Editor 實測——開工先驗證。
- **假設 B**：`Select`／`Deselect` 事件（鍵盤導覽）與 `PointerEnter`／`PointerExit` 可同時掛在 EventTrigger 上不衝突。未實測。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 按鈕 hover 有動畫感 | FR-01／FR-02；T2 |
| 選單滑出帶彈跳 | FR-03；T3 |
| GameOver 字樣滑出 | FR-04；T3 |
| 都帶點彈跳感 | FR-02／FR-03／FR-04（easeOutBack 過沖） |
| 破紀錄時最高分由舊值滾動到新值，由快到慢 | FR-07；T5 |

## 功能需求（FR）

- **FR-01 按鈕 hover 放大**：按鈕 hover（滑鼠 PointerEnter 或鍵盤 Select）時 scale 放大（如 1.0 → 1.1），失焦（PointerExit／Deselect）時縮回 1.0。
- **FR-02 hover 帶彈跳**：放大與縮回皆帶輕彈（easeOutBack 過沖），非線性。
- **FR-03 面板進場滑入**：MenuCanvas 進場、pausePane 進場、gameOverPane 進場時，從畫面外側滑入並帶彈跳（easeOutBack）。
- **FR-04 GameOver 字樣滑出**：gameOverPane 進場時，分數／標題文字以滑出動畫呈現（可與面板滑入同播或延遲）。
- **FR-05 動畫不阻塞**：動畫播放期間按鈕仍可點擊（不擋輸入）。
- **FR-06 動畫與狀態機同步**：動畫僅在對應狀態進場時播放一次；狀態切換（如 pause→resume）時停止並歸位，不殘留錯位。
- **FR-07 破紀錄分數跳動**：僅當「本次分數 > 舊最高分」（`DidBreakRecord`）才播分數跳動；未破紀錄瞬間顯示目前最高分，無跳動動畫。GameOver 時 `gameOverHighScoreText` 先瞬間顯示舊最高分（`GetPreviousHighScore()`），等 gameOverPane 面板滑入完成（onComplete）後才啟動整數逐格跳動：1 秒、EaseOutCubic（由快到慢）、定格新值。

## 非功能需求（NFR）

- **NFR-01 不回歸**：game-states 已驗收成果（UIController SetActive 切換、OnStateChanged 訂退訂、五按鈕接線）不得破壞。
- **NFR-02 低耦合**：動畫僅透過 `GameManager.OnStateChanged` 觸發，不直接操作 gameplay 內部狀態（沿用 NFR-05）。
- **NFR-03 無新依賴**：不引入第三方 tween 套件（DOTween 等），純 C# 自建 easing。
- **NFR-04 凍結相容**：動畫在 Paused／GameOver 期間仍播放（UI 動畫不受 gameplay 凍結影響；timeScale 恆為 1）。
- **NFR-05 效能**：動畫僅播放期間執行，結束後停止（不每幀空轉）。
- **NFR-06 值動畫可定向停止**：分數跳動用泛用 float tween（`UITween.FloatTo`＋`ValueTween`，token 定向停止）；狀態切換時 `StopFloatTweens` 中斷並 snap 到新值（FR-06/BC-7）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | UITween 核心（easing 函式庫 + Tween 執行器） | P0 | gameplay-developer | no | 純 C# 自建 tween：easeOutBack（彈跳）、easeOutCubic（滑入）、scale/position 動畫執行器 | `Assets/Scripts/UI/UITween.cs` | 提供 easeOutBack 過沖曲線；tween 可播放/停止/歸位；無第三方套件引用 |
| T2 | 按鈕 hover 放大動畫 | P0 | gameplay-developer | no | ButtonHoverScale 元件：PointerEnter/Select 放大、PointerExit/Deselect 縮回，帶 easeOutBack | `Assets/Scripts/UI/ButtonHoverScale.cs` | hover 放大帶彈跳；失焦縮回；鍵盤 Select 也觸發 |
| T3 | 面板進場滑入＋GameOver 字樣滑出 | P0 | gameplay-developer | no | UIController 在 OnStateChanged 觸發 menu/pause/gameOver 面板滑入（easeOutBack）＋gameOver 文字滑出 | `Assets/Scripts/UI/UIController.cs` | 三面板進場滑入帶彈跳；GameOver 文字滑出；快速切換不殘留 |
| T4 | 場景接線（EventTrigger＋reference＋anchor） | P1 | scene-developer | no | 按鈕掛 EventTrigger、動畫 reference 設定、面板 off-screen 起始定位 | `Assets/Scenes/SampleScene.unity`、按鈕/面板 | 按鈕可 hover；面板起始位置正確；動畫 reference 已接 |
| T5 | 破紀錄分數跳動 | P0 | gameplay-developer | no | GameManager 快照舊最高分＋DidBreakRecord；UIController 破紀錄時先瞬間顯示舊值、面板滑入後啟動 1 秒 EaseOutCubic 整數跳動；`UITween.FloatTo` 泛用 float tween（token 定向停止） | `Assets/Scripts/GameManager.cs`、`Assets/Scripts/UI/UIController.cs`、`Assets/Scripts/UI/UITween.cs` | 僅破紀錄播跳動；面板滑入後啟動；1 秒 EaseOutCubic 定格新值；狀態切換中斷並 snap |

### T1 — UITween 核心（P0, gameplay-developer, arch-review: no）
- **Goal**：純 C# 自建 tween 工具，供 T2/T3 使用。
- **作法**：
  - `UITween` 靜態工具類（或 MonoBehaviour 執行器）：提供 easing 函式（`EaseOutBack`、`EaseOutCubic` 等）與 tween 執行（coroutine 或 Update 驅動，支援 scale/position 動畫、播放/停止/歸位）。
  - **不引入任何第三方套件**（NFR-03）。
  - 用 `deltaTime`（timeScale 恆為 1，NFR-04）。
- **開工前置**：無 Editor API 依賴，但若用 coroutine 需確認 MonoBehaviour 執行環境。
- **驗收**：easeOutBack 產生過沖曲線；tween 可播放、中途停止、歸位；無第三方引用。

### T2 — 按鈕 hover 放大（P0, gameplay-developer, arch-review: no）
- **Goal**：按鈕 hover/Select 時 scale 放大帶彈跳，失焦縮回。
- **作法**：
  - `ButtonHoverScale` MonoBehaviour：掛按鈕，用 `EventTrigger`（PointerEnter/PointerExit/Select/Deselect）＋ UITween 做 scale 動畫。
  - 放大目標（如 1.1）與時長可序列化調整。
  - **同時處理滑鼠與鍵盤**（Select/Deselect，BC-4）。
- **開工前**：用 `unity_reflect` 驗證 `EventTrigger` 與 `EventTrigger.Entry` API 存在性（假設 A/B）。
- **驗收**：hover 放大帶彈跳；移出縮回；鍵盤 Select 也觸發。

### T3 — 面板進場滑入＋GameOver 字樣滑出（P0, gameplay-developer, arch-review: no）
- **Goal**：menu/pause/gameOver 面板進場滑入帶彈跳；GameOver 文字滑出。
- **作法**：
  - 在 `UIController.OnStateChanged` 內，於既有 SetActive 切換後觸發進場動畫（用 UITween）。
  - menuCanvas 進場（Menu 態）、pausePane 進場（Paused 態）、gameOverPane 進場（GameOver 態）各播一次滑入（easeOutBack）。
  - gameOverPane 內分數/標題文字滑出（FR-04）。
  - 狀態切換時停止進行中的 tween 並歸位（FR-06，BC-1/BC-2）。
  - **保留**既有 SetActive 切換與文字更新（NFR-01）；文字先寫再播動畫（BC-6）。
- **驗收**：三面板進場滑入帶彈跳；GameOver 文字滑出；快速狀態切換不殘留錯位。

### T4 — 場景接線（P1, scene-developer, arch-review: no）
- **Goal**：把動畫元件與 EventTrigger 接到場景。
- **作法**：
  - 按鈕掛 `EventTrigger`（PointerEnter/PointerExit/Select/Deselect）與 `ButtonHoverScale`。
  - 面板設定 off-screen 起始 anchor（滑入前位置）。
  - 確認 UIController 動畫 reference 已接。
- **驗收**：按鈕可被 hover；面板起始位置正確；動畫可播放。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 快速狀態切換 | pause→playing 快速切換，動畫未播完即被中斷 | T3 切換時停止 tween 並歸位（FR-06） |
| BC-2 動畫期間面板停用 | 動畫播放中面板被 SetActive(false) | T3 停用時停止 tween（FR-06） |
| BC-3 hover 與點擊衝突 | hover 放大中點擊按鈕 | scale 動畫與 onClick 獨立，不衝突（FR-05） |
| BC-4 鍵盤 Select | 鍵盤導覽也會觸發 Select，需與滑鼠 hover 同處理 | T2 同時處理 PointerEnter/Exit 與 Select/Deselect（FR-01） |
| BC-5 文字更新順序 | gameOver 進場動畫與分數文字更新順序 | T3 先寫文字再播動畫 |
| BC-6 回主選單重播 | BackToMenu 回 Menu 時 menu 重新進場 | 每次 SetActive(true) 播一次進場動畫 |
| BC-7 跳動中狀態切換 | 分數跳動進行中按「重新開始」或「回主選單」 | `StopPanelTweens` 對 `gameOverHighScoreText` 呼叫 `StopFloatTweens`（token 一致）中斷並 snap 到新值（FR-06/FR-07） |

## 驗收標準（整體，Validator 對照用）

- [ ] **hover 放大**：滑鼠 hover 按鈕 → scale 放大帶彈跳；移出 → 縮回
- [ ] **鍵盤 Select**：鍵盤導覽 Select 按鈕 → 同樣觸發 hover 放大
- [ ] **Menu 滑入**：啟動進 Menu 時，主選單滑入帶彈跳
- [ ] **Pause 滑入**：暫停時 pausePane 滑入帶彈跳
- [ ] **GameOver 滑入**：死亡時 gameOverPane 滑入帶彈跳
- [ ] **GameOver 字樣滑出**：gameOverPane 分數/標題文字滑出
- [ ] **破紀錄跳動**：破紀錄死亡時，最高分先瞬間顯示舊值，面板滑入後由舊值整數逐格跳動至本次分數，1 秒 EaseOutCubic，定格新值
- [ ] **未破紀錄不跳動**：未破紀錄死亡時，最高分瞬間顯示目前最高分，無跳動動畫
- [ ] **跳動中斷**：跳動進行中按重新開始/回主選單，跳動立即停止並 snap 到新值，無殘留
- [ ] **不阻塞**：動畫期間按鈕仍可點擊
- [ ] **快速切換不殘留**：pause→resume 快速切換後無錯位殘留
- [ ] **無新依賴**：未引入第三方 tween 套件
- [ ] **不回歸**：game-states 已驗收（SetActive 切換、五按鈕接線、OnStateChanged）保持

## 規格自檢清單

- **事實主張標出處**：無第三方 tween（本 session grep `Assets/` 零命中）；UIController 結構（`UIController.cs:105-109` SetActive、`:43-51` 訂退訂、`:60-67` WireButtons）；Button transition 不支援 scale（Unity 官方 API 事實）；凍結規則（shared/project.md 技術事實節）；分數跳動（`GameManager.cs:119-120` 快照舊最高分於 `SaveHighScore` 前、`UIController.cs:47` duration=1f、`:147-151` 先顯示舊值＋onComplete 延後啟動、`:185-188` FloatTo EaseOutCubic、`UITween.cs:51-55` FloatTo、`:170-181` StopValueTweens snap、`UIController.cs:218-222` StopFloatTweens）。
- **未驗證假設**：假設 A（EventTrigger 在 InputSystemUI 下觸發）、假設 B（Pointer 與 Select 事件共存）明確標「未驗證假設」，T2 已註明「開工先用 unity_reflect 驗證」。
- **一致性**：BC-1/BC-2 與 FR-06/T3 一致（「動畫與狀態機同步」）；BC-4 與 FR-01/T2 一致（「鍵盤 Select」）；BC-6 與 FR-03/T3 一致（「面板進場」）；BC-7 與 FR-06/FR-07/NFR-06/T5 一致（「跳動中斷並 snap」）。
- **Editor API 驗證**：T2 依賴 EventTrigger API，任務單已加「開工先用 unity_reflect 驗證」。

## arch-review 判定

**此功能不觸發架構變更，arch-review = no**，理由：

1. **純附加動畫**：不改造 gameplay 核心邏輯（Player/Pipe/BG 零改動）；GameManager 僅加 `previousHighScore`＋`DidBreakRecord` 快照欄位與 `GetPreviousHighScore()` getter（T5），供 UI 判定分數跳動，屬最小附加、非架構變更。其餘為新增 UI 動畫元件與修改 UIController 的動畫觸發。
2. **無新跨系統依賴**：動畫僅透過既有 `GameManager.OnStateChanged` 觸發（NFR-02），不新增跨腳本依賴。
3. **無新共享核心**：`UITween` 是 UI 專用自包含工具，不與 gameplay 系統共享；若未來擴及 gameplay 系統再另行審查。
4. **C# vs Animator 裁定**（見下）是 UI 內部實作模式選擇，非架構變更。

## 裁定：純 C# easing vs Animator

**裁定：純 C# easing（自建 UITween），不用 Animator／AnimationClip。**

理由：

1. **無第三方套件**：專案無 DOTween/LeanTween（grep 零命中）。Animator 方案仍需自建 AnimationClip＋AnimatorController 資產，且 bounce 曲線要在曲線編輯器手刻，維護成本高。
2. **低耦合（NFR-02）**：純 C# tween 是自包含工具，直接呼叫 easing 函式，與狀態機僅透過 OnStateChanged 觸發，符合 NFR-05 低耦合。
3. **彈跳感易控**：easeOutBack（過沖）是單一數學函式，一行即可，比在 Animator 曲線編輯器手刻 bounce 更精確可控、可測試。
4. **無 Art 資產**：需求不需美術資產，純 C# 不需額外資產導入。
5. **凍結相容**：timeScale 恆為 1（凍結靠各系統自查），純 C# 用 deltaTime 即可在 Paused/GameOver 期間正常播放，無 Animator 的 unscaled 設定需求。
6. **測試性**：easing 函式為純函式，可單元測試；Animator 資產難自動驗證。

**hover scale 作法**：Button 內建 transition 不支援 scale（僅 ColorTint/ColorSwap/SpriteSwap/Animation），故用 `EventTrigger`（PointerEnter/PointerExit/Select/Deselect）＋ `ButtonHoverScale` 元件＋ UITween 自建 scale 動畫。