# 規格：遊戲狀態機／主選單／暫停／結算／最高分（game-states）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（T1/T2/T3/T4 觸發架構變更） ｜ Created: 2026-08-24
> （功能 seed：`main-menu`。因實際範圍涵蓋狀態機＋主選單＋暫停＋結算＋最高分＋字型＋UI 骨架，命名為 `game-states` 更貼切。）

## 功能：主狀態機與選單系統

- **目標**：將現行「啟動即開局、撞死 0.1 秒自動重生」的單一遊玩流程，升級為完整狀態機（Menu／Playing／Paused／GameOver），並提供主選單、暫停選單、終局結算三種 UI 畫面，加上最高分持久化。
- **性質**：新功能。**改造既有核心腳本（GameManager）＋新增 UI 子系統**，非平行重建——現有計分（ScoreTrigger）、碰撞（CollisionHandler）、水管池（PipeSpawner）、背景（BGManager）行為必須整合保留。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/GameManager.cs` | **核心改造**（`isPlaying` bool → `GameState` enum；啟動停 Menu；死亡停 GameOver；新增 Pause/Resume/EndGame/Restart/BackToMenu；**新增 `HidePlayerAndPipes()`／`ShowPlayerAtStart()` 重生隱藏流程**） |
| Script | `Assets/Scripts/PlayerController.cs` | **可能修改**（Paused/Menu/GameOver 態 Update 跳過；P 鍵輸入歸屬裁定；**新增 `SetVisible(bool)`**——Renderer+collider 隱藏，非 SetActive） |
| Script | `Assets/Scripts/PipeSpawner.cs` | **可能修改**（非 Playing 態不生成水管；**`ResetAll()` 釋放水管回池**） |
| Script | `Assets/Scripts/PipeController.cs` | **可能修改**（非 Playing 態不移動） |
| Script | `Assets/Scripts/Background/BGLayer.cs` | **可能修改**（非 Playing 態不捲動；**新增 `ScrollBackToNormal(onComplete)` 反向快退＋過渡完成回呼**） |
| Script | `Assets/Scripts/Background/BGManager.cs` | **可能修改**（**新增 `ScrollBackToStart(onComplete)`／`ScrollBackToMenu()` 聚合各層反向快退**） |
| Script | `Assets/Scripts/ScoreTrigger.cs` | **不變**（依賴 `IsPlaying()`，狀態機導入後語義保持） |
| Script | `Assets/Scripts/CollisionHandler.cs` | **不變**（呼叫 `Die()`，語義改為 Playing→GameOver） |
| Script（新） | UI 控制腳本（類名／位置由 arch-review 裁定） | **新增**（overlay 面板狀態切換、按鈕接線） |
| Scene | `Assets/Scenes/SampleScene.unity` | **修改**（新增 EventSystem、MenuCanvas、OverlayCanvas） |
| Art | `Assets/Art/UI/kenney_ui-pack/Font/Kenney Future.ttf` | **已匯入**（.meta 存在）；建立 TMP Font Asset 供 ScoreText／UI 文字使用 |
| Art | `Assets/Art/UI/kenney_ui-pack/PNG/.../button_rectangle_*.png` | **使用**（9-slice 按鈕素材） |
| Art | `Assets/Art/UI/kenney_ui-pack/PNG/Extra/.../icon_play_*.png`、`icon_repeat_*.png` | **使用**（按鈕圖示，可選） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **GameManager 啟動即玩、死亡自動重生**：`GameManager.cs:27` `Start()` 直接呼叫 `StartGame()`；`:53` `Die()` 用 `Invoke(nameof(StartGame), 0.1f)` 自動重生。`isPlaying` bool（L20）為唯一狀態。
2. **GameManager.Update 只在 Playing 檢查地板/天花板**：`GameManager.cs:30-39` `if (!isPlaying) return;` 後檢查 `player.transform.position.y <= floorY || >= ceilingY` → `Die()`。
3. **計分與碰撞已收斂**：`ScoreTrigger.cs:17` 用 `gameManager.IsPlaying()` 守衛＋`scored` 旗標；`CollisionHandler.cs:10-14` 撞 "Pipe" → `Die()`。兩者皆依賴 GameManager 的 `IsPlaying()`（`GameManager.cs:58`）。
4. **水管與背景持續更新**：`PipeSpawner.cs:70-82` `Update()` 每幀計時生成水管；`PipeController.cs:18-34` `Update()` 左移水管；`BGLayer.cs:129-134` `Update()` 捲動背景。三者皆無暫停守衛。
5. **場景無 EventSystem**：`SampleScene.unity` 全檔 grep `EventSystem` 零命中。選單要按鈕必須新增。
6. **ScoreCanvas 慣例**：`SampleScene.unity:1037` `ScoreCanvas` — Canvas RenderMode 0（Screen Space-Overlay，L1092）、CanvasScaler `m_UiScaleMode: 0`（Constant Pixel Size，L1072）、`m_ReferenceResolution: {x: 800, y: 600}`（L1075）、含 GraphicRaycaster（L1054）。新 UI 沿用此慣例。
7. **最高分零基礎**：全專案 grep `PlayerPrefs|highScore|bestScore|HighScore|BestScore` 僅命中 `Library/PackageCache/`（第三方套件），專案 `Assets/` 內零命中——最高分從零建。
8. **字型已匯入**：`Assets/Art/UI/kenney_ui-pack/Font/Kenney Future.ttf` 與 `.meta` 皆存在（已匯入）。ScoreText 現為 TextMeshProUGUI（`SampleScene.unity:134`）。
9. **素材盤點**：包內有 `button_rectangle`（10 變體 × 5 色＋Extra）、`icon_play`／`icon_repeat`（僅 Extra/）、`divider`（Extra/）；**無 0-9 數字 sprite、無 panel/board 背景框、無 pause/quit/home 圖示**。按鈕為九宮切片形態。

### 未驗證假設（不得視為既定事實）

- **假設 A**：Input System 的 `Keyboard.current.pKey.wasPressedThisFrame` 可用於暫停按鍵。依現行 `PlayerController.cs:44` 的 `spaceKey` 用法應成立，但未在 Editor 實測。
- **假設 B**：暫停時僅靠「各系統 Update 檢查 GameState」即可凍結，無需 `Time.timeScale`。此為設計定案（手動重力硬碰硬，timeScale 干擾 Update 時序），但「誰持有狀態閘」的具體形式（各系統自查 vs GameManager 統一停用）由 arch-review 裁定。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 啟動停主選單，不直入遊戲 | FR-01／FR-02；T1 |
| 撞死不再自動重生，停在終局 | FR-01／FR-04；T1 |
| 暫停／終局共用同一 overlay 面板 | FR-05／FR-06；T4 |
| 最高分持久化（PlayerPrefs） | FR-07；T5 |
| 暫停用 GameState 守衛，不用 timeScale | FR-08；T2 |
| 分數維持 TMP，字型換 Kenney Future | FR-09；T7 |
| UI 沿用 ScoreCanvas 慣例＋新增 EventSystem | FR-10／FR-11；T3 |
| 按鈕用 button_rectangle 九宮切片 | FR-12；T8 |
| 重新開始不誤觸殘留水管碰撞 | FR-13；T1（StartGame 重生隱藏流程） |

## 功能需求（FR）

### A. 狀態機

- **FR-01 狀態枚舉**：GameManager 新增 `enum GameState { Menu, Playing, Paused, GameOver }`，取代 `isPlaying` bool。
- **FR-02 啟動停 Menu**：`Start()` 設 `state = Menu`，**不呼叫 `StartGame()`**。
- **FR-03 轉移**：Menu ─開始→ Playing；Playing ─P→ Paused；Playing ─撞死→ GameOver；Paused ─繼續→ Playing；Paused ─結束→ GameOver；GameOver ─重新開始→ Playing；GameOver ─回主選單→ Menu。**Menu→Playing 與 GameOver→Playing 兩條轉移皆走 FR-13 重生隱藏流程**（非直接顯示鳥）。
- **FR-04 死亡停 GameOver**：`Die()` 由 Playing → GameOver，**移除 `Invoke(nameof(StartGame), 0.1f)`**，玩家按按鈕才起身。
- **FR-05 暫停選單**：Paused 態 overlay 顯示「繼續／結束」。
- **FR-06 終局結算**：GameOver 態 overlay 顯示「重新開始／回主選單」＋本局分數＋最高分。
- **FR-07 最高分**：本局正式結束（死亡 與 暫停→結束 同路徑）一律寫入；僅當 `score > savedBest` 才覆寫；主選單載入與結算畫面皆顯示。
- **FR-08 暫停凍結**：Paused／Menu／GameOver 態下，玩家、水管、背景全部凍結；**不使用 `Time.timeScale = 0`**。
- **FR-09 字型**：ScoreText 與新 UI 文字改用 `Kenney Future` TMP Font Asset，維持 TMP（非圖片數字）。
- **FR-10 UI 慣例**：新 UI Canvas 沿用 ScoreCanvas 慣例（Screen Space-Overlay、Constant Pixel Size、reference 800×600）。
- **FR-11 EventSystem**：場景新增 EventSystem（選單要按鈕必須）。
- **FR-12 按鈕素材**：按鈕用 `button_rectangle` 九宮切片（Image 9-slice）。
- **FR-13 重生隱藏流程（Restart/Start）**：`StartGame()`（Menu/GameOver → Playing）不得直接顯示鳥，須依序：① 進 Playing 後**立即隱藏 Bird＋釋放水管**（`HidePlayerAndPipes()`＝`player.SetVisible(false)`＋`pipeSpawner.ResetAll()`）；② 畫面只留**背景反向倒回動畫**（1 秒過渡，`backgroundManager.ScrollBackToStart(onComplete)`）；③ **過渡完成後**才恢復（`ShowPlayerAtStart()`＝`player.SetVisible(true)`＋`player.Reset(playerStartPosition)`），水管隨 Playing 態重新生成。目的：鳥在倒回期間不可見不可碰，恢復時水管已重生、鳥回定位，不誤觸碰撞。鳥隱藏用 `PlayerController.SetVisible(bool)`（`GetComponentsInChildren<Renderer>().enabled`＋`collider.enabled`），**非 SetActive 停用**（ADR-007，G2 禁 SetActive 停用 gameplay 物件）。`BackToMenu()` 亦用 `SetVisible(false)`（取代先前 `SetActive(false)`）。

## 非功能需求（NFR）

- **NFR-01 不回歸**：pipe-score 已驗收成果（ScoreTrigger 唯一計分、`IsPlaying()` 守衛、`scored` 旗標）不得破壞；`IsPlaying()` 語義保持（回傳 `state == Playing`）。
- **NFR-02 凍結一致性**：暫停時所有 gameplay 系統同步凍結，不得有任一系統漏閘（水管繼續生成／背景繼續捲動）。
- **NFR-03 最高分一致性**：Menu 與 GameOver 兩處顯示的最高分必須一致（同源 PlayerPrefs，讀取時機正確）。
- **NFR-04 輸入衝突**：P 鍵與空白鍵（flap）不得互相干擾；Menu／GameOver 態 P 鍵無效。
- **NFR-05 低耦合**：UI 系統與 gameplay 核心以 GameManager 公開方法為唯一接線點，不得直接操作內部狀態。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | GameManager 導入 GameState 狀態機 | P0 | gameplay-developer | **yes** | 狀態機取代 isPlaying，啟動停 Menu，死亡停 GameOver | `Assets/Scripts/GameManager.cs` | 啟動停主選單；死亡停終結局不自動重生 |
| T2 | 暫停凍結機制 | P0 | gameplay-developer | **yes** | 非 Playing 態 Player/Pipe/BG 全凍結 | `PlayerController.cs`、`PipeSpawner.cs`、`PipeController.cs`、`BGLayer.cs` | 暫停時鳥不動、水管不生成/不移動、背景不捲動 |
| T3 | 主選單 UI（EventSystem＋Canvas＋按鈕） | P0 | scene-developer | **yes** | 主選單畫面＋開始按鈕＋最高分 | `SampleScene.unity`、button_rectangle 9-slice | 啟動停在主選單；按開始→Playing；最高分顯示 |
| T4 | Overlay 面板雙態（暫停/終局） | P0 | scene-developer | **yes** | 同一面板依 GameState 切換內容 | `SampleScene.unity`、新 UI 控制腳本 | Paused 顯示繼續/結束；GameOver 顯示重新開始/回主選單＋分數 |
| T5 | 最高分 PlayerPrefs | P0 | gameplay-developer | no | 最高分持久化 | `Assets/Scripts/GameManager.cs` | 終局寫入；僅破紀錄覆寫；Menu/結算顯示一致 |
| T6 | P 鍵暫停 | P0 | gameplay-developer | no | P 切換 Playing/Paused | `GameManager.cs`（或 PlayerController） | Playing 按 P→暫停；Paused 按 P→恢復；Menu/GameOver 無效 |
| T7 | Kenney Future 字型 | P1 | asset-developer | no | 分數/UI 文字換 Kenney Future | `Kenney Future.ttf`、ScoreText | ScoreText/UI 文字以 Kenney Future 渲染 |
| T8 | 按鈕 9-slice 素材 | P1 | asset-developer | no | button_rectangle 9-slice 供 UI 使用 | `button_rectangle_*.png` | 按鈕拉伸不變形 |

### T1 — GameManager 導入 GameState 狀態機（P0, gameplay-developer, arch-review: yes）
- **Goal**：把 `isPlaying` bool 換成 `GameState` enum，啟動停 Menu，死亡停 GameOver。
- **作法**：
  - 新增 `enum GameState { Menu, Playing, Paused, GameOver }`。
  - `Start()` → `state = Menu`（不呼叫 `StartGame()`）。
  - `StartGame()`：Menu/GameOver → Playing，重置 score；**走 FR-13 重生隱藏流程**——`HidePlayerAndPipes()`（`player.SetVisible(false)`＋`pipeSpawner.ResetAll()`）→ `backgroundManager.ScrollBackToStart(ShowPlayerAtStart)`（1 秒背景倒回，過渡完成後 `ShowPlayerAtStart()`＝`player.SetVisible(true)`＋`player.Reset(playerStartPosition)`）。
  - `Pause()`：Playing → Paused；`Resume()`：Paused → Playing。
  - `EndGame()`：Playing/Paused → GameOver（死亡與暫停→結束共用），並掛鉤 T5 寫最高分。
  - `Restart()`：GameOver → Playing（等同 StartGame）；`BackToMenu()`：GameOver → Menu（`HidePlayerAndPipes()`＋`backgroundManager.ScrollBackToMenu()`）。
  - `Die()`：Playing → GameOver，**移除 `Invoke(nameof(StartGame), 0.1f)`**。
  - `Update()`：僅 Playing 態檢查地板/天花板；Playing＋Paused 態偵測 P 鍵（見 T6）。
  - 保留 `IsPlaying()`（回傳 `state == Playing`）與 `GetScore()`，維持 ScoreTrigger 相容。
- **驗收**：啟動停在 Menu；死亡停在 GameOver 不自動重生；各狀態轉移正確。

### T2 — 暫停凍結機制（P0, gameplay-developer, arch-review: yes）
- **Goal**：Paused／Menu／GameOver 態下，Player、Pipe、Background 全部凍結。
- **作法**：各系統 `Update()` 開頭檢查 GameManager 狀態，非 Playing 即 return。**不使用 `Time.timeScale = 0`**（手動重力硬碰硬，timeScale 干擾 Update 時序）。
  - `PlayerController.Update()`：非 Playing 態跳過（含 `HandleInput`，避免暫停時空白鍵 flap）。
  - `PipeSpawner.Update()`：非 Playing 態不生成水管。
  - `PipeController.Update()`：非 Playing 態不移動。
  - `BGLayer.Update()`：非 Playing 態不捲動。
- **架構裁定點**：「各系統自查 GameManager state」vs「GameManager 統一停用」由 arch-review 裁定（影響各腳本是否新增 GameManager 引用）。
- **驗收**：暫停時鳥不動、水管不生成/不移動、背景不捲動；恢復後續跑。

### T3 — 主選單 UI（P0, scene-developer, arch-review: yes）
- **Goal**：建立主選單畫面（標題、開始按鈕、最高分顯示）。
- **作法**：
  - 場景新增 **EventSystem**（目前無；選單要按鈕必須）。
  - 新增 MenuCanvas，沿用 ScoreCanvas 慣例（Screen Space-Overlay、Constant Pixel Size、reference 800×600）。
  - 開始按鈕用 `button_rectangle` 9-slice（Image 9-slice），點擊 → `GameManager.StartGame()`。
  - 最高分顯示（T5 讀取）。
- **驗收**：啟動停在主選單；按開始 → Playing；最高分顯示正確。

### T4 — Overlay 面板雙態（P0, scene-developer, arch-review: yes）
- **Goal**：同一 overlay 面板依 GameState 切換內容。
- **作法**：
  - 新增 OverlayCanvas（或併入 MenuCanvas 的 overlay 層），沿用 ScoreCanvas 慣例。
  - Paused 態顯示「繼續／結束」按鈕；GameOver 態顯示「重新開始／回主選單」＋本局分數＋最高分。
  - 按鈕接線：繼續 → `Resume()`；結束 → `EndGame()`；重新開始 → `Restart()`；回主選單 → `BackToMenu()`。
  - 新增 UI 控制腳本（類名/位置由 arch-review 裁定）負責依 GameState 切換面板內容。
- **驗收**：暫停 → 顯示繼續/結束；死亡 → 顯示重新開始/回主選單＋結算分數。

### T5 — 最高分 PlayerPrefs（P0, gameplay-developer, arch-review: no）
- **Goal**：最高分持久化。
- **作法**：
  - key：`flipbird_high_score`。
  - 寫入：`EndGame()` 正式結束時，若 `score > savedBest` 才 `PlayerPrefs.SetInt`。
  - 讀取：主選單載入時顯示；結算畫面顯示（EndGame 寫入後讀取）。
- **驗收**：死亡/暫停結束 → 寫入；僅當局 > 舊紀錄才覆寫；Menu 與 GameOver 顯示一致。

### T6 — P 鍵暫停（P0, gameplay-developer, arch-review: no）
- **Goal**：P 鍵切換 Playing/Paused。
- **作法**：Input System `Keyboard.current.pKey.wasPressedThisFrame`；僅 Playing→Pause、Paused→Resume；Menu/GameOver 忽略（守衛防「死亡同時按 P」競態）。
- **驗收**：Playing 按 P → 暫停；Paused 按 P → 恢復；Menu/GameOver 按 P 無效。

### T7 — Kenney Future 字型（P1, asset-developer, arch-review: no）
- **Goal**：分數與 UI 文字改用 Kenney Future。
- **作法**：由 `Assets/Art/UI/kenney_ui-pack/Font/Kenney Future.ttf`（已匯入）建立 TMP Font Asset；ScoreText 與新 UI 文字套用。
- **驗收**：ScoreText/UI 文字以 Kenney Future 渲染。

### T8 — 按鈕 9-slice 素材（P1, asset-developer, arch-review: no）
- **Goal**：`button_rectangle` 9-slice 供 UI 按鈕使用。
- **作法**：設定 `button_rectangle_*.png` 的 9-slice border，供 T3/T4 的 Image 使用。
- **驗收**：按鈕拉伸不變形。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 死亡同時按 P | 撞死（→GameOver）同一幀按 P，若 P 處理未守衛會誤觸暫停 | T6 守衛：P 僅在 Playing/Paused 有效 |
| BC-2 暫停時管道/背景未凍結 | 若只凍結 Player 而漏 Pipe/BG，暫停時水管仍生成/移動、背景仍捲動 | T2 全系統守衛（NFR-02） |
| BC-3 最高分兩處不一致 | Menu 與 GameOver 顯示不同值 | T5 同讀 PlayerPrefs、讀取時機正確（NFR-03） |
| BC-4 P 鍵與空白鍵衝突 | P 與 space 同時觸發 flap＋暫停 | T2 暫停時 PlayerController 跳過 HandleInput；P 與 space 不同鍵 |
| BC-5 暫停時空白鍵 flap | Paused 態 PlayerController 若仍處理輸入，空白鍵會 flap | T2 PlayerController.Update 非 Playing 即 return |
| BC-6 啟動即 Menu 但水管仍生成 | Menu 態 PipeSpawner 若未守衛，水管持續生成 | T2 PipeSpawner 非 Playing 不生成 |
| BC-7 最高分寫入時機 | 若在非正式結束（如中途退出）寫入會誤存 | T5 僅 `EndGame()` 正式結束寫入 |
| BC-8 字型缺失 | 若 TMP Font Asset 未建立，ScoreText 回退預設字型 | T7 建立 Font Asset；開工先用 unity_reflect 驗證 TMP API |
| BC-9 重生瞬間碰撞 | GameOver→Restart 時，鳥可能與殘留水管重疊，若直接顯示鳥會重生瞬間觸發碰撞 → 又一次 GameOver | FR-13：倒回期間鳥隱藏（`SetVisible(false)` 停用 collider）不可碰；過渡完成後水管已重生、鳥回定位才恢復 |

## 驗收標準（整體，Validator 對照用）

- [ ] **啟動**：遊戲啟動停在主選單，不直入遊戲
- [ ] **開始**：主選單按開始 → 進入 Playing，鳥可操控
- [ ] **暫停**：Playing 按 P → 暫停，鳥/水管/背景全凍結；overlay 顯示「繼續/結束」
- [ ] **恢復**：暫停按繼續（或 P）→ 恢復 Playing，續跑
- [ ] **死亡**：撞死 → GameOver，不自動重生；overlay 顯示「重新開始/回主選單」＋分數＋最高分
- [ ] **暫停結束**：暫停按結束 → GameOver 結算，與死亡同路徑
- [ ] **重新開始**：GameOver 按重新開始 → Playing，分數歸 0；**鳥與水管先隱藏、只留背景倒回（1 秒過渡），過渡完成後鳥回到起始點、水管重生，無瞬間碰撞死亡**
- [ ] **回主選單**：GameOver 按回主選單 → Menu
- [ ] **最高分**：死亡/結束後寫入；僅當局 > 舊紀錄才覆寫；Menu 與 GameOver 顯示一致
- [ ] **字型**：ScoreText 與 UI 文字以 Kenney Future 渲染
- [ ] **計分不回歸**：ScoreTrigger 仍為唯一計分路徑，`IsPlaying()` 守衛保持

## 規格自檢清單

- **事實主張標出處**：上述「現況診斷」每條均標注檔案＋行號（GameManager.cs:27/53/40-39/58、ScoreTrigger.cs:22、CollisionHandler.cs:10-14、PipeSpawner.cs:70-82、PipeController.cs:18-34、BGLayer.cs:18-34、SampleScene.unity:1043-1105/134）。
- **未驗證假設**：假設 A（`pKey.wasPressedThisFrame` 可用）、假設 B（各系統自查 state 即可凍結）明確標「未驗證假設」，且 T2 依賴假設 B 已註明「開工先驗證」。
- **一致性**：BC-1 與 T6 一致（「死亡同時按 P」）；BC-2 與 T2/NFR-02 一致（「凍結一致性」）；BC-3 與 T5/NFR-03 一致（「最高分一致性」）；BC-4/BC-5 與 T2/NFR-04 一致（「輸入衝突」）；**BC-9 與 FR-13/T1 一致（「重生隱藏流程」）**。
- **Editor API 驗證**：T6 依賴 `pKey` 輸入、T7 依賴 TMP Font Asset API，任務單已加「開工先用 unity_reflect 驗證」提示。
- **FR-13 事實出處**：`GameManager.cs:89-100`（StartGame 流程）、`:138-149`（HidePlayerAndPipes/ShowPlayerAtStart）、`:128-134`（BackToMenu）；`PlayerController.cs:42-48`（SetVisible，Renderer+collider 非 SetActive）；`BGManager.cs:80-101`（ScrollBackToStart 聚合 onComplete）、`:104-110`（ScrollBackToMenu）；`BGLayer.cs:17-18`（ScrollBackDuration=1f）、`:201-219`（ScrollBackToNormal/BeginScrollBack）、`:144-169`（ScrollingBack 過渡＋onComplete）；`PipeSpawner.cs:119-127`（ResetAll 釋放回池）；ADR-007（G2 禁 SetActive 停用，改 Renderer.enabled）。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**，範圍：

1. **GameManager 改 GameState**（T1）：既有核心腳本（GameManager）從 bool 狀態改為 enum 狀態機，影響計分守衛（`IsPlaying()`）、碰撞（`Die()`）、水管/背景更新閘。屬核心架構改動。
2. **暫停凍結機制**（T2）：跨 Player/Pipe/BG 四系統的狀態閘設計（各系統自查 vs GameManager 統一停用）是新架構模式，需 Tech Lead 裁定。
3. **UI 系統目錄**（T3/T4）：新增 EventSystem＋MenuCanvas＋OverlayCanvas＋UI 控制腳本，屬新 UI 子系統，需裁定腳本位置與接線方式。

T5/T6/T7/T8 為自包含改動（PlayerPrefs 存讀、單鍵輸入、字型、素材），**arch-review = no**。