# 規格：死亡狀態（Dying state）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（T1/T2/T3 觸發架構變更——**翻轉 ADR-013 決策 8**） ｜ Created: 2026-09-30
> （功能 seed：人類實測回報 bug——死亡流程期間水管照常移動/生成、背景照常捲動。根因：死亡期間 `GameState` 維持 `Playing`（FR-16 設計），`isDying` 旗標從未被廣播給任何移動系統。本規格新增 `Dying` state 取代 `isDying` 旗標。）

## 功能：死亡流程專用狀態（Dying）

- **目標**：死亡瞬間（`Die()`）即進入 `Dying` 態——水管停止移動/生成、背景立即凍結，同時維持「Die → Dissasemble → 淡出 → OnDeathFadeComplete → EndGame」事件鏈完整執行（不卡死）。以全域 state 取代僅 GameManager 內部自我參考的 `isDying` 旗標，讓凍結政策（ADR-006 D3）在死亡期間自然生效。
- **性質**：**bug 修復＋狀態機擴張**。改造既有核心腳本（GameManager）＋修正 BlobAnimator 兩處對「死亡期間 IsPlaying()==true」的隱藏依賴＋UIController 加 case。**翻轉 ADR-013 決策 8**（原裁決否決新增 GameState.Dying，理由「5 態已涵蓋、旗標更貼切」——旗標方案造成實際 bug，見現況診斷 3/4/5）。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/GameManager.cs` | **核心改造**（enum 加 `Dying`；`Die()` 改 `SetState(Dying)`；`SetState` switch 加 `case Dying: SetFrozen(true)`；`EndGame()` 守衛接受 Dying；**移除 `isDying` 欄位與全部使用點**；更新過期註解） |
| Script | `Assets/Scripts/BlobAnimator.cs` | **修改**（兩處隱藏依賴修正：speed 每幀求值接受 Dying；淡出守衛改「非暫停即可淡出」） |
| Script | `Assets/Scripts/UI/UIController.cs` | **修改**（`OnStateChanged` switch 加 `case Dying:`——與 Ready/Playing 同款） |
| Script | `Assets/Scripts/GameStateAuthority.cs` | **修改**（新增 `IsPaused()` 查詢，ADR-011 seam 擴張——「只做狀態查詢（IsPlaying 等）」明列可擴） |
| Script | `Assets/Scripts/PipeController.cs` | **不變**（`IsPlaying()` 自查守衛在 Dying 態自動生效，零改動） |
| Script | `Assets/Scripts/PipeSpawner.cs` | **不變**（同上） |
| Script | `Assets/Scripts/Background/BGLayer.cs` | **不變**（背景凍結靠 SetState switch 的 `case Dying: SetFrozen(true)` 單向驅動，BGLayer 零改動） |
| Script | `Assets/Scripts/PlayerController.cs` | **不變**（`isDead` 為元件內部相位旗標，維持 local flag——見「邊界」節） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **死亡流程期間維持 Playing（FR-16 設計）**：`GameManager.cs:204-205` 註明「死亡流程期間維持 Playing 態（FR-16）；EndGame() 延後到淡出完成（OnDeathFadeComplete）後」。`Die()`（`:206-216`）設 `isDying = true`（`:210`）但**不改 state**。
2. **isDying 僅 GameManager 內部自我參考**：`GameManager.cs:22`（欄位）、`:89`（`&& !isDying`，H1 守衛）、`:138`（StartGame 重設）、`:209`（re-entry guard）、`:210`（賦值）——**從未被廣播給任何移動系統**。
3. **水管照常移動（bug）**：`PipeController.cs:16` `if (!GameStateAuthority.IsPlaying()) return;`——死亡期間 `IsPlaying()==true` → 水管持續左移。
4. **水管持續生成（bug）**：`PipeSpawner.cs:51` 同守衛——死亡期間持續生成。
5. **背景照常捲動（bug）**：`BGLayer.cs:166` `if (motion == MotionMode.Normal && frozen) return;`——背景凍結靠 `GameManager.SetState` 的 `default: SetFrozen(true)`（`:123`），但死亡期間 state==Playing 走 `case Playing: SetNormal()`（`:119`，frozen=false），**從未觸發凍結**。
6. **隱藏依賴 #1（動畫 speed）**：`BlobAnimator.cs:144` `animator.speed = (lastState == Playing || isAssembling) ? 1f : 0f`——若死亡時 state 改掉（非 Playing），Dissasemble 凍結第 0 幀 → `dissasembleCompleteFired` 永不 fire（`:91-92`）→ 淡出永不啟動 → 卡死。**這是 FR-16「維持 Playing」繞過的第一個依賴**。
7. **隱藏依賴 #2（淡出守衛）**：`BlobAnimator.cs:152` `if (!GameStateAuthority.IsPlaying()) return;`（H5 守衛）——若死亡時 state 改掉，淡出凍結 → `OnDeathFadeComplete` 永不 fire → `EndGame()` 永不執行 → 卡死在死亡畫面。**這是 FR-16「維持 Playing」繞過的第二個依賴**。
8. **P 鍵死亡中暫停卡死**：`GameManager.cs:99` P 鍵檢查 `state == Playing || state == Paused`——死亡期間 state==Playing，按 P → `Pause()` → `SetState(Paused)` → BlobAnimator speed=0（`:144`）＋淡出守衛凍結（`:152`）→ 死亡流程中斷，卡在 Paused（鳥半解體/半淡出；玩家不操作則永不進 GameOver）。
9. **過期註解**：`GameManager.cs:87-88` 仍寫「Die() 先置 state=GameOver」——早已不符現況（EndGame 已延後到淡出後，Die() 不再直接置 GameOver）。
10. **isDying 不隨 BackToMenu() 重置**：`GameManager.cs:176-182` 未碰 isDying，只靠 `:138` StartGame 重設——sticky 旗標潛在問題（目前無路徑繞過 StartGame 進 Playing，屬潛在風險非現行 bug）。

### 架構背景（ADR 出處）

- **ADR-013 決策 8**（`docs/adr/013-blob-animator.md:128`）明確否決新增 GameState.Dying：「狀態機 5 態已涵蓋；新增態需改 SetState switch、UI switch、凍結政策，且『死亡流程』是 Playing 內部的短暫子階段，用旗標（dying）表達更貼切」。**本次改動翻轉此裁決**——旗標方案造成水管/背景未凍結的實際 bug（現況診斷 3/4/5），且 `isDying` 從未被廣播（診斷 2）。
- **ADR-013 H2/H4/H6**（`:81/:83/:85`）當時裁決「死亡中按 P 可接受」「水管持續生成可接受」「背景持續捲動可接受」——本次改動**推翻 H2**（P 鍵卡死，診斷 8）與 **H4/H6**（凍結 bug，診斷 3/4/5）。人類實測已確認這些「可接受」實為缺陷。
- **ADR-006 D3 凍結政策**：非 Playing 態凍結 gameplay 系統，各系統自查 `IsPlaying()`＋背景 GameManager→BGManager 單向 `SetFrozen`。Dying 加入後，`IsPlaying()` 語義維持嚴格 `state == Playing`（`GameManager.cs:226-229` 不變）——水管/生成/計分自然停；背景靠 SetState switch 的 `case Dying: SetFrozen(true)`。
- **ADR-011**：GameStateAuthority 為查詢 seam（「只做狀態查詢（IsPlaying 等）」），新增 `IsPaused()` 屬 seam 正常擴張，不觸發事件匯流排演進條件（仍無第二個事件訂閱者）。
- **ADR-012**：Ready 過渡態不受影響——`StartGame()`（Ready→Playing）與 `BackToMenu()` 路徑零改動。

### 未驗證假設

- **無**。本功能所有行為主張（含 P 鍵卡死、兩處隱藏依賴、isDying 移除安全性）皆由靜態代碼追蹤確認，不依賴 Editor 實測。動態驗證清單見驗收標準（Validator 出清單、Sisyphus 於 Editor 實測回填）。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 死亡瞬間水管停止移動/生成 | FR-01／FR-02／FR-03；T1 |
| 死亡瞬間背景停止捲動 | FR-03；T1 |
| 死亡流程（Dissasemble→淡出→EndGame）不卡死 | FR-04／FR-05；T2 |
| 死亡中按 P 不再卡死暫停 | FR-06；T1 |
| 移除 isDying 旗標（單一事實來源） | FR-07；T1 |
| 過期註解更新 | FR-08；T1 |
| 死亡淡出期間 UI 淨空 | FR-09；T3 |

## 功能需求（FR）

### A. 狀態機擴張

- **FR-01 新增 Dying state**：`GameManager.cs:7` enum 改 `{ Menu, Ready, Playing, Dying, Paused, GameOver }`（Dying 插在 Playing 之後，語意為「Playing 的死亡子階段」）。
- **FR-02 Die() 轉移**：`Die()`（`:206-216`）由「`isDying = true`」改為 `SetState(GameState.Dying)`；`EndGame()` 守衛（`:168`）改為 `if (state != GameState.Dying && state != GameState.Paused) return;`（死亡淡出完成時 state 是 Dying；保留 Paused 給「暫停→結束」按鈕路徑）。**死亡流程事件鏈不變**：Die → PlayDissasemble → Dissasemble 播完（state-query）→ 內部啟動淡出 → OnDeathFadeComplete → EndGame。
- **FR-03 背景凍結**：`SetState` switch（`:110-128`）加 `case GameState.Dying: backgroundManager.SetFrozen(true); break;`——死亡瞬間背景立即凍結（顯式 case，比照 ADR-012 `case Ready: break;` 的意圖明確原則；不依賴 default 分支）。

### B. BlobAnimator 兩處隱藏依賴修正

- **FR-04 動畫 speed 接受 Dying**：`BlobAnimator.cs:144` 改 `(lastState == Playing || lastState == Dying || isAssembling) ? 1f : 0f`——Dissasemble 在 Dying 態正常播放（隱藏依賴 #1 修正）。
- **FR-05 淡出守衛接受 Dying**：`BlobAnimator.cs:152` 改 `if (GameStateAuthority.IsPaused()) return;`——語義為「非暫停即可淡出」（H5 守衛原意：暫停時淡出凍結；Dying 態淡出必須執行）。`GameStateAuthority` 新增 `IsPaused()` 查詢（`GameManager` 對應 `public bool IsPaused() { return state == GameState.Paused; }`）。

### C. 附帶修正

- **FR-06 P 鍵排除**：P 鍵檢查（`:99`）`state == Playing || state == Paused` **自然排除 Dying**——死亡中按 P 無效，死亡流程不被中斷（無需改 code，驗證即可）。
- **FR-07 isDying 移除**：刪除 `isDying` 欄位（`:22`）與全部使用點——`:89` 改回純 `state == GameState.Playing`（H1 守衛由 state 承擔）、`:138` 刪除重設、`:209` 刪除 re-entry guard、`:210` 由 `SetState(Dying)` 取代。**重入防護分析見 BC-3**。
- **FR-08 過期註解更新**：`GameManager.cs:87-88` 註解「Die() 先置 state=GameOver」更新為「Die() 先置 state=Dying，P 檢查因 state 非 Playing/Paused 而跳過」。
- **FR-09 UI Dying case**：`UIController.cs:135-195` switch 加 `case GameManager.GameState.Dying:`——與 Ready/Playing 同款（`SetActive(menuCanvas, false); SetActive(overlayCanvas, false);`，保留 HUD ScoreText）——死亡淡出期間畫面保持乾淨，GameOver 面板延後到 EndGame 才出現。

## 非功能需求（NFR）

- **NFR-01 死亡期間凍結不回歸**：水管移動（`PipeController.cs:16`）、水管生成（`PipeSpawner.cs:51`）、背景捲動（`BGLayer.cs:166`）在死亡流程期間（Dying 態）必須全部凍結——本次 bug 修復的核心驗收。
- **NFR-02 IsPlaying() 語義**：`IsPlaying()` 維持嚴格 `state == Playing`（`GameManager.cs:226-229` 不變）；Dying 期間回傳 false——各 gameplay 系統自查守衛自然凍結，**PipeController/PipeSpawner/PlayerController 零改動**。
- **NFR-03 死亡流程不卡死**：Dissasemble 動畫與淡出在 Dying 態必須正常播放（`BlobAnimator.cs:144/152` 兩處依賴修正），OnDeathFadeComplete → EndGame 事件鏈完整執行。
- **NFR-04 凍結一致性**：Dying 態下所有 gameplay 系統同步凍結（Player/PipeSpawner/PipeController 自查 `IsPlaying()`；背景靠 SetState switch `case Dying`），不得有任一系統漏閘。
- **NFR-05 查詢 seam 合規**：新增 `IsPaused()` 走 GameStateAuthority（ADR-011），BlobAnimator 不新增 GameManager 引用、不承接命令（GA3）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | GameManager 新增 Dying state | P0 | gameplay-developer | **yes** | enum 加 Dying；Die() 轉移；背景凍結；EndGame 守衛；isDying 移除；註解更新 | `Assets/Scripts/GameManager.cs` | 死亡瞬間水管/生成/背景全凍結；死亡流程完整走完進 GameOver |
| T2 | BlobAnimator 兩處依賴修正＋IsPaused() | P0 | gameplay-developer | **yes** | speed 接受 Dying；淡出守衛改 IsPaused()；GameStateAuthority 新增 IsPaused() | `Assets/Scripts/BlobAnimator.cs`、`Assets/Scripts/GameStateAuthority.cs` | Dissasemble 正常播放；淡出正常完成；EndGame 執行 |
| T3 | UIController Dying case | P0 | scene-developer | **yes** | OnStateChanged switch 加 Dying case | `Assets/Scripts/UI/UIController.cs` | 死亡淡出期間畫面淨空（無 menu/overlay），HUD 保留 |

### T1 — GameManager 新增 Dying state（P0, gameplay-developer, arch-review: yes）
- **Goal**：以全域 `Dying` state 取代 `isDying` 旗標，死亡瞬間凍結水管/生成/背景。
- **作法**：
  - `:7` enum 加 `Dying`：`{ Menu, Ready, Playing, Dying, Paused, GameOver }`。
  - `Die()`（`:206-216`）：`if (state != GameState.Playing) return;` 保留（**此即重入防護**，見 BC-3）；刪除 `if (isDying) return;`（`:209`）；`isDying = true`（`:210`）改 `SetState(GameState.Dying);`；其餘（`player.Die()`、停 collider、`PlayDissasemble()`）不變。
  - `SetState` switch（`:110-128`）加 `case GameState.Dying: backgroundManager.SetFrozen(true); break;`。
  - `EndGame()` 守衛（`:168`）改 `if (state != GameState.Dying && state != GameState.Paused) return;`。
  - 刪除 `isDying` 欄位（`:22`）；`:89` 改回 `if (state == GameState.Playing)`；`:138` 刪除 `isDying = false;`。
  - `:87-88` 註解更新（FR-08）。
  - **不改**：`IsPlaying()`（`:226-229`）、`StartGame()`/`BackToMenu()`/`Restart()`/`Pause()`/`Resume()` 流程。
- **驗收**：撞死瞬間水管停止移動/生成、背景停止捲動；死亡流程完整走完（Dissasemble→淡出→GameOver）；死亡中按 P 無效。

### T2 — BlobAnimator 兩處依賴修正＋IsPaused()（P0, gameplay-developer, arch-review: yes）
- **Goal**：修正兩處「死亡期間 IsPlaying()==true」的隱藏依賴，使 Dying 態下 Dissasemble 與淡出正常執行。
- **作法**：
  - `BlobAnimator.cs:144`：`animator.speed = (lastState == GameManager.GameState.Playing || lastState == GameManager.GameState.Dying || isAssembling) ? 1f : 0f;`
  - `BlobAnimator.cs:152`：`if (GameStateAuthority.IsPaused()) return;`（取代 `if (!GameStateAuthority.IsPlaying()) return;`）。
  - `GameStateAuthority.cs`：新增 `public static bool IsPaused()`（`instance != null && instance.IsPaused()`）；`GameManager.cs` 新增 `public bool IsPaused() { return state == GameState.Paused; }`。
  - **不改**：state-query 核心時序（`:81-93`）、命令方法、`ResetVisual()`。
- **驗收**：Dissasemble 動畫在 Dying 態正常播放（speed=1）；淡出正常完成；`OnDeathFadeComplete` fire → `EndGame()` 執行；暫停時淡出仍凍結（H5 守衛不退化）。

### T3 — UIController Dying case（P0, scene-developer, arch-review: yes）
- **Goal**：死亡淡出期間 UI 淨空。
- **作法**：`UIController.cs:135-195` switch 加 `case GameManager.GameState.Dying:` → `SetActive(menuCanvas, false); SetActive(overlayCanvas, false);`（與 Ready/Playing case 同款；HUD ScoreText 屬獨立 ScoreCanvas 恆可見）。
- **驗收**：死亡瞬間至 GameOver 面板出現前，畫面無 menu/overlay 彈出，HUD 分數保留。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 P 鍵死亡中暫停 | 死亡流程中按 P：現行（state==Playing）會 `Pause()` → `SetState(Paused)` → speed=0＋淡出守衛凍結 → 死亡流程中斷卡在 Paused | FR-06：Dying 態下 P 鍵檢查（`state == Playing \|\| state == Paused`）自然排除，P 無效；死亡流程不被中斷 |
| BC-2 淡出守衛卡死 | 若 `BlobAnimator.cs:152` 未改（仍 `!IsPlaying()`），Dying 態 `IsPlaying()==false` → 淡出凍結 → `OnDeathFadeComplete` 永不 fire → `EndGame()` 永不執行 → 卡死在死亡畫面 | FR-05：守衛改 `IsPaused()`——「非暫停即可淡出」，Dying 態淡出正常執行 |
| BC-3 isDying 移除後的重入防護 | 移除 `isDying` 後 `Die()` 的重入防護靠什麼？**state 本身**：① `Die()` 首行 `if (state != GameState.Playing) return;`（`:208`）——state==Dying 時直接 return，重入被擋；② 地板/天花板檢查（`:89`）`state == GameState.Playing` 已排除 Dying——死亡流程期間不會每幀重觸發 `Die()`（H1 守衛由 state 承擔）；③ CollisionHandler 撞擊路徑——鳥 collider 已在 `Die()` 內停用（`:213-214`），且即使觸發也過不了 ①。**安全** | FR-07：刪除 `isDying` 欄位與使用點；重入防護由 `state != Playing` 守衛承擔 |
| BC-4 動畫 speed 卡死 | 若 `BlobAnimator.cs:144` 未改（仍 `(lastState == Playing \|\| isAssembling)`），Dying 態 `lastState==Dying` → speed=0 → Dissasemble 凍結第 0 幀 → `dissasembleCompleteFired` 永不 fire → 淡出永不啟動 → 卡死 | FR-04：speed 求值接受 `lastState == Dying`——Dissasemble 正常播放 |
| BC-5 死亡中按「結束」按鈕 | 死亡流程中若 pause overlay 可見（僅 Paused 態），按「結束」→ `EndGame()`——Dying 態下 pause overlay 不可見（FR-09 淨空），此路徑不可達；Paused 態按「結束」仍正常（EndGame 守衛保留 Paused） | FR-02：EndGame 守衛接受 Dying＋Paused；FR-09：Dying 態 UI 淨空 |

## 驗收標準（整體，Validator 對照用）

- [ ] **死亡凍結**：撞死瞬間（Dissasemble 播放期間）水管停止移動、水管停止生成、背景停止捲動——三系統全凍結（NFR-01）
- [ ] **死亡流程完整**：Dissasemble 動畫正常播放（speed=1）→ 淡出正常完成 → `OnDeathFadeComplete` fire → `EndGame()` → GameOver（NFR-03）
- [ ] **P 鍵排除**：死亡流程中按 P 無效，死亡流程不被中斷、不卡在 Paused（FR-06）
- [ ] **isDying 移除**：`GameManager.cs` 無 `isDying` 欄位與使用點；`Die()` 重入防護由 `state != Playing` 守衛承擔（FR-07/BC-3）
- [ ] **背景凍結**：死亡瞬間背景 `SetFrozen(true)` 生效（`BGLayer` 停止捲動）；GameOver 態背景仍凍結（FR-03）
- [ ] **UI 淨空**：死亡淡出期間無 menu/overlay 彈出，HUD 分數保留；GameOver 面板在 EndGame 後正常出現（FR-09）
- [ ] **暫停淡出守衛不退化**：Playing 態按 P 暫停，若淡出進行中（理論上不可達，防禦性）仍凍結（FR-05/H5）
- [ ] **不回歸**：`StartGame()`（Ready→Playing）與 `BackToMenu()` 路徑行為不變；`IsPlaying()` 語義維持嚴格 `state == Playing`（NFR-02）；計分（ScoreTrigger）與碰撞（CollisionHandler）不受影響

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（GameManager.cs:7/22/87-88/89/99/110-128/138/168/176-182/204-216/226-229、BlobAnimator.cs:144/152、UIController.cs:135-195、PipeController.cs:16、PipeSpawner.cs:51、BGLayer.cs:166、GameStateAuthority.cs:27-30、ADR-013:128/81/83/85、ADR-006 D3、ADR-011、ADR-012）。
- **未驗證假設**：**無**——所有行為主張（P 鍵卡死、兩處隱藏依賴、isDying 移除安全性）皆由靜態代碼追蹤確認。動態驗證（Editor 實測）由 Sisyphus 於驗收階段回填。
- **一致性**：BC-1 與 FR-06/T1 一致（「P 鍵死亡中暫停」）；BC-2 與 FR-05/T2 一致（「淡出守衛卡死」）；BC-3 與 FR-07/T1 一致（「isDying 移除重入防護」）；BC-4 與 FR-04/T2 一致（「動畫 speed 卡死」）；BC-5 與 FR-02/FR-09 一致（「死亡中按結束」）。
- **Editor API 驗證**：本功能**不涉及 Editor API**（enum/switch/查詢方法皆 runtime C#），不需 unity_reflect 驗證；無新 Unity API 依賴。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**，範圍：

1. **翻轉 ADR-013 決策 8**（T1/T2/T3）：原裁決否決新增 GameState.Dying——本次以實際 bug（水管/背景未凍結）＋「isDying 從未被廣播」推翻之。需 Tech Lead 更新 ADR-013（決策 8 修訂＋H2/H4/H6 裁決翻轉）與 `specs/architecture/`（game-states.md 狀態機圖、animator-system.md 死亡流程、gameplay-architecture.md:55、dependency-rules.md:76 的 `!isDying` 引用）。
2. **GameStateAuthority 查詢擴張**（T2）：新增 `IsPaused()`——ADR-011 seam 正常擴張（「只做狀態查詢（IsPlaying 等）」），不觸發事件匯流排演進條件；需 Tech Lead 確認。
3. **凍結政策語意**（T1）：Dying 加入後 `IsPlaying()` 語義維持嚴格 `state == Playing`——水管/生成/計分自然停；背景靠 SetState switch `case Dying: SetFrozen(true)`。與 ADR-006 D3 一致，無政策變更，但需 Tech Lead 在架構文件補 Dying 態凍結說明。