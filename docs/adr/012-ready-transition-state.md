# ADR-012: Ready 過渡態（修正「還沒看到東西就先觸發」的開始時序）

## Status

Accepted

## Context

`StartGame()` 現行流程（GameManager.cs:93-103）在背景倒帶前就 `SetState(GameState.Playing)`（L96），導致倒帶那 1 秒 `IsPlaying()` 已為 true：`PipeSpawner.Update`（`PipeSpawner.cs:51` 守衛失效）可能生成水管、`GameManager.Update`（`GameManager.cs:52-59`）對隱藏鳥跑地板/天花板碰撞、`PlayerController` 對隱藏鳥跑重力/輸入/碰撞。這是「還沒看到東西就先觸發」的開始時序病灶。

需求（specs/requirements/ready-transition.md）要求新增 `Ready` 過渡態，讓「倒帶完成、鳥現身」與「進入 Playing」同幀原子發生；水管計時改倒數式並在進 Playing 第一幀與鳥同幀現身。此為 ADR-006 D1 狀態機的實質擴張（enum 加 `Ready`、Menu/GameOver→Playing 轉移語義改經 Ready 過渡），觸發架構變更。

## Decision

1. **GameState enum 擴張**（FR-01）：`enum GameState { Menu, Ready, Playing, Paused, GameOver }`。`Ready` 為過渡態（非停留態），僅存在於 `StartGame()` 的倒帶窗口。

2. **StartGame 時序重構**（FR-03）：`SetState(Playing)` 從「先執行」改為「倒帶完成後、與 `ShowPlayerAtStart()` 同幀」：
   ```
   StartGame():
     if (state == Playing || state == Ready) return;   // FR-02 重入擋
     scoreSession.Begin();                              // fire OnScoreChanged(0)，HUD 顯示 "Score: 0"
     SetState(Ready);                                   // IsPlaying() 立即 false
     HidePlayerAndPipes();                              // 隱鳥 + ResetAll 水管
     backgroundManager.ScrollBackToStart(onComplete: () => {
         ShowPlayerAtStart();                           // 恢復並 Reset 鳥
         SetState(Playing);                             // 倒帶完成才進 Playing
     });
   ```
   `ShowPlayerAtStart()` 與 `SetState(Playing)` 在同一同步 callback 內、同幀連續執行（FR-05），中間無 gameplay 窗口。

3. **SetState 背景 switch 加 `case GameState.Ready: break;`**（FR-04）：Ready 期間背景不凍結，處 `ScrollingBack` motion 持續倒帶；倒帶完成後 `SetState(Playing)` 才 `SetNormal()`。**此為防禦性＋語意正確的修正**——實證 `BGLayer.Update()`（`BGLayer.cs:138-163`）的 `ScrollingBack` motion 優先於 `frozen` 旗標（註解「不受凍結限制」），即使 Ready 落入 default `SetFrozen(true)` 倒帶仍會完成；但顯式 `case Ready: break;` 使「Ready 期間背景不凍結」成為明確意圖，避免依賴 `Update()` 分支順序這個實作細節（若未來重排 Update 或改凍結邏輯，Ready 會靜默破壞）。

4. **Ready 期間凍結語義**（與 ADR-006 D3 一致）：gameplay 系統（Player/PipeSpawner/PipeController）因 `IsPlaying()==false` 自查凍結；背景**不**凍結（ScrollingBack 持續倒帶）。Paused/GameOver 仍凍結（default 分支不變，NFR-02）。

5. **UI Ready case**（FR-06）：`UIController.OnStateChanged` 加 `case Ready:` → `SetActive(menuCanvas,false); SetActive(overlayCanvas,false)`（同 Playing case）。HUD ScoreText 屬獨立 ScoreCanvas 恆可見，由 `OnScoreChanged` 事件驅動顯示 "Score: 0"。與 ADR-006 D4-D6（UI 依 GameState 切換）相容。

6. **PipeSpawner 倒數式計時**（FR-07，arch-review: no）：`spawnTimer -= dt; if (spawnTimer <= 0) { SpawnPipe(); spawnTimer = spawnInterval; }`；`ResetAll()` seed `spawnTimer = 0`。純計時語意重構，不與 ADR-010 池化衝突（`SpawnPipe` 仍走 `pipePool.Get()`、`ResetAll` 仍 `pipePool.ReleaseAll()`）。

## Consequences

- 正面：倒帶期間 `IsPlaying()==false`，水管不生成、隱藏鳥不跑重力/碰撞——病灶消除。
- 正面：進 Playing 第一幀水管與鳥同幀現身（seed=0 倒數式）。
- 正面：`IsPlaying()` 語義保持（NFR-01），ScoreTrigger/CollisionHandler 相容，pipe-score 驗收成果不回歸。
- 正面：Ready 期間 UI 淨空但保留 HUD，符合 ADR-006 D4-D6。
- 負面：狀態機多一態，`SetState` switch 與 `UIController` switch 需各加 Ready 分支（已列入 T1/T2）。
- 負面：接受「某背景層過渡永不完成 → callback 不執行 → 鳥永不顯示、卡死」風險（FR-08，不加 watchdog，靠 Console 錯誤抓取）。
- 被放棄的替代方案：維持「先 SetState(Playing) 再倒帶」現狀（病灶持續）；加 timeout watchdog（FR-08 否決，過度工程）；Ready 期間用 SetActive 停用 gameplay 物件（違反 G2，SetActive 副作用不可控）。

## 對 pm 規格的修訂建議

- **BC-2 嚴重度修正**：BC-2 描述「背景倒帶被凍結，破壞整個場景」**過度**。實證 `BGLayer.Update` 的 `ScrollingBack` motion 優先於 `frozen`，即使 Ready 落入 default 倒帶仍會完成。`case Ready: break;` 是防禦性/語義正確的修正，非「不加就壞場景」的硬 bug。Validator 不應以「無 case 時場景破壞」為驗收條件。
- 其餘 FR-01~FR-08、BC-1~BC-7、NFR-01~03 與現況診斷（含行號）經實證全部正確，無需修訂。