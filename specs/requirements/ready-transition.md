# 規格：Ready 過渡態＋水管倒數式生成（ready-transition）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（T1/T2 觸發架構變更） ｜ Created: 2026-08-26
> 設計拷問已完成（Sisyphus＋人類達成共識），本規格完整記錄 D1-D4 決定，不可自行更改。

## 功能：修正「還沒看到東西就先觸發」的開始時序病灶

- **目標**：消除 `StartGame()` 在背景倒帶那 1 秒期間就進入 Playing 的瑕疵——水管提前生成、隱藏鳥跑重力/輸入/碰撞。新增 `Ready` 過渡態，讓「倒帶完成、鳥現身」與「進入 Playing」同幀原子發生；水管計時改倒數式並在進 Playing 第一幀與鳥同幀現身。
- **性質**：既有核心腳本（GameManager）狀態機修正＋PipeSpawner 計時重構。非新功能，不改既有計分/碰撞/背景架構。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/GameManager.cs` | **核心改造**（enum 加 `Ready`；`StartGame()` 時序重構；`SetState` switch 加 Ready 分支） |
| Script | `Assets/Scripts/UI/UIController.cs` | **修改**（`OnStateChanged` 加 `case Ready`） |
| Script | `Assets/Scripts/PipeSpawner.cs` | **修改**（計時改倒數式；`ResetAll()` seed `spawnTimer=0`） |
| Script | `Assets/Scripts/GameStateAuthority.cs` | **不變**（`IsPlaying()` 語義保持：Ready 期間回 false，正是本修正的關鍵） |
| Script | `Assets/Scripts/PlayerController.cs` | **不變**（`SetVisible`/`Reset` 已存在，供 `ShowPlayerAtStart` 使用） |
| Script | `Assets/Scripts/Background/BGManager.cs` | **不變**（`ScrollBackToStart(onComplete)` 已存在） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **GameState enum 現為四態**：`GameManager.cs:7` `enum GameState { Menu, Playing, Paused, GameOver }`。
2. **StartGame 過早進 Playing**：`GameManager.cs:93-103` 目前流程：`if (state == Playing) return;` → `SetState(GameState.Playing)`（立刻進 Playing）→ `scoreSession.Begin()` → `HidePlayerAndPipes()` → `backgroundManager.ScrollBackToStart(ShowPlayerAtStart)`（1 秒倒帶，完成才回呼）。**瑕疵：`SetState(Playing)` 在倒帶前就執行**，倒帶那 1 秒 `IsPlaying()` 已為 true。
3. **倒帶期間的錯誤觸發**：`PipeSpawner.cs:51` `if (!GameStateAuthority.IsPlaying()) return;`——倒帶期間 IsPlaying()==true，`Update` 會跑計時並可能生成水管（`spawnTimer` 跨局不重置）；`GameManager.cs:52-59` 對隱藏鳥跑地板/天花板碰撞檢查；`PlayerController` 對隱藏鳥跑重力/輸入/碰撞。
4. **SetState 背景 switch 的 default 會凍結**：`GameManager.cs:80-86` `switch(newState)`：Playing→`SetNormal()`、Menu→`SetAmbient()`、**default→`SetFrozen(true)`**（Paused/GameOver）。**若 Ready 落入 default 會被凍結，倒帶動畫會停住**——必須加顯式 `case Ready:` 不凍結。
5. **IsPlaying 語義**：`GameManager.cs:169-172` `IsPlaying()` 回傳 `state == Playing`；`GameStateAuthority.cs:27-30` 委派給 `instance.IsPlaying()`。Ready 期間兩者皆 false——正是本修正的關鍵（倒帶期間各系統守衛生效）。
6. **PipeSpawner 累積式計時**：`PipeSpawner.cs:53-59` `spawnTimer += Time.deltaTime; if (spawnTimer >= spawnInterval) { SpawnPipe(); spawnTimer = 0f; }`。
7. **ResetAll 只釋放池、不重置計時**：`PipeSpawner.cs:93-96` `ResetAll()` 僅 `pipePool.ReleaseAll()`，`spawnTimer` 跨局殘留。
8. **UIController 無 Ready case**：`UIController.cs:135-193` `OnStateChanged` switch 僅 Menu/Playing/Paused/GameOver；Playing case（145-151）`SetActive(menuCanvas,false); SetActive(overlayCanvas,false)`。

### 未驗證假設（不得視為既定事實）

- **假設 A**：Ready 期間背景層的 `ScrollingBack` motion 在 `SetState(Ready)` 不觸發 `SetFrozen` 的前提下能持續完成（即 BGLayer 的倒帶過渡不受其他狀態影響）。此為設計定案（D1），但「Ready 期間背景確實持續倒帶」需動態驗證。
- **假設 B**：`ResetAll()` seed `spawnTimer=0` 後，因 Ready 期間 `IsPlaying()==false`（`PipeSpawner.cs:51` 守衛），`spawnTimer -= dt` 不執行，seed=0 全程保留到進 Playing 第一幀。邏輯上成立，但「seed 後到 Playing 前無任何路徑改寫 spawnTimer」需開工確認。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 「還沒看到東西就先觸發」的病灶 | FR-01／FR-02／FR-03；T1 |
| 倒帶期間水管不得提前生成 | FR-03／FR-07；T1／T3 |
| 倒帶期間隱藏鳥不得跑重力/碰撞 | FR-03；T1 |
| 進 Playing 第一幀水管與鳥同幀現身 | FR-07／FR-08；T3 |
| Ready 期間 UI 淨空但保留 HUD | FR-06；T2 |
| 不加 timeout watchdog | FR-09；T1 |

## 功能需求（FR）

### D1 新增 Ready 過渡態

- **FR-01 狀態枚舉**：`GameManager.GameState` enum 新增 `Ready`，成為 `{ Menu, Ready, Playing, Paused, GameOver }`。
- **FR-02 重入擋**：`StartGame()` 開頭改為 `if (state == Playing || state == Ready) return;`（防止 Ready 過渡期間重複呼叫）。
- **FR-03 StartGame 新流程**：
  ```
  StartGame():
    if (state == Playing || state == Ready) return;
    scoreSession.Begin();                    // 重置本局分數／破紀錄旗標（fire OnScoreChanged(0)）
    SetState(Ready);                         // 先進過渡態（IsPlaying() 立即 false）
    HidePlayerAndPipes();                    // 隱鳥 + ResetAll 水管
    backgroundManager.ScrollBackToStart(onComplete: () => {
        ShowPlayerAtStart();                 // 恢復並 Reset 鳥
        SetState(Playing);                  // 倒帶完成才進 Playing
    });
  ```
- **FR-04 SetState(Ready) 背景分支**：`SetState` 的 background switch 新增 `case GameState.Ready:` **不凍結**（`break`，不落入 default `SetFrozen(true)`）。Ready 期間背景層處 `ScrollingBack` motion，不受凍結控制；倒帶完成後 `SetState(Playing)` 才 `SetNormal()`。
- **FR-05 同幀原子**：`ShowPlayerAtStart()` 與 `SetState(Playing)` 在同一同步 callback 內、同幀連續執行，中間無 gameplay 窗口（鳥可見與進 Playing 同幀）。

### FR-06 UI 認可 Ready → 淨空

- `UIController.OnStateChanged` 新增 `case GameManager.GameState.Ready:`。
- Ready 行為：`SetActive(menuCanvas, false); SetActive(overlayCanvas, false)`（同 Playing case）。
- 保留 HUD：ScoreText 持續可見（`scoreSession.Begin()` 已 fire `OnScoreChanged(0)`，HUD 顯示 "Score: 0"）。

### FR-07 PipeSpawner 改倒數式計時

- `PipeSpawner.Update` 由累積式改倒數式：
  - 原本：`spawnTimer += dt; if (spawnTimer >= spawnInterval) { SpawnPipe(); spawnTimer = 0; }`
  - 改成：`spawnTimer -= dt; if (spawnTimer <= 0) { SpawnPipe(); spawnTimer = spawnInterval; }`
- 語意：`spawnTimer` =「還剩多久出下一根」，從 interval 往下減到 0 觸發。
- `ResetAll()`（或 StartGame 入口）把 `spawnTimer` seed 成 0，使進 Playing 第一幀 `-dt <= 0` 立刻生成第一根，與 player 同幀現身。
- 注意：Ready 期間 `IsPlaying()==false`，Update 的 `spawnTimer -= dt` 不會跑，seed=0 全程保留到進 Playing。

### FR-08 不加 timeout watchdog

- 接受「某背景層過渡永不完成 → callback 不執行 → 鳥永不顯示、遊戲卡死」的風險，靠開發期 Console 錯誤抓取。
- **不要加任何 watchdog/Invoke/coroutine 超時保險。**

## 非功能需求（NFR）

- **NFR-01 不回歸**：`IsPlaying()` 語義保持（回傳 `state == Playing`）；Ready 期間 false。ScoreTrigger/CollisionHandler 相容（依賴 `IsPlaying()` 守衛）。pipe-score 已驗收成果不得破壞。
- **NFR-02 凍結一致性**：Ready 期間背景**不**凍結（ScrollingBack 持續倒帶）；Paused/GameOver 仍凍結（default 分支不變）。
- **NFR-03 無 watchdog**：不引入任何超時保險機制（FR-08）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | GameManager 加 Ready 過渡態＋StartGame 時序重構 | P0 | gameplay-developer | **yes** | 倒帶期間 IsPlaying()==false，鳥不可見不可碰 | `Assets/Scripts/GameManager.cs` | 倒帶期間水管不生成、鳥不跑碰撞；倒帶完成才進 Playing |
| T2 | UIController 加 Ready case | P0 | scene-developer | **yes** | Ready 期間 UI 淨空但保留 HUD | `Assets/Scripts/UI/UIController.cs` | Ready 期間 menu/overlay 隱藏、ScoreText 顯示 "Score: 0" |
| T3 | PipeSpawner 倒數式計時＋seed | P0 | gameplay-developer | no | 進 Playing 第一幀生成第一根，與鳥同幀 | `Assets/Scripts/PipeSpawner.cs` | 進 Playing 第一幀出第一根水管；間隔穩定 |

### T1 — GameManager 加 Ready 過渡態＋StartGame 時序重構（P0, gameplay-developer, arch-review: yes）
- **Goal**：消除「倒帶期間 IsPlaying()==true」的病灶——倒帶期間水管不生成、隱藏鳥不跑重力/碰撞；倒帶完成才進 Playing。
- **作法**：
  - enum 加 `Ready`：`{ Menu, Ready, Playing, Paused, GameOver }`。
  - `StartGame()` 重入擋改 `if (state == Playing || state == Ready) return;`。
  - `StartGame()` 新流程（FR-03）：`scoreSession.Begin()` → `SetState(Ready)` → `HidePlayerAndPipes()` → `backgroundManager.ScrollBackToStart(onComplete: () => { ShowPlayerAtStart(); SetState(Playing); })`。
  - `SetState` background switch 加 `case GameState.Ready: break;`（不落入 default `SetFrozen(true)`，FR-04）。
  - `ShowPlayerAtStart()` 與 `SetState(Playing)` 同幀連續（FR-05）。
- **開工前置**：先用 `unity_reflect` 驗證 `GameState` enum 與 `SetState` 現況（無新增 Editor API，但確認 enum 擴張不破壞既有 `switch` 的 exhaustive 檢查）。
- **驗收**：倒帶期間 `IsPlaying()`==false（水管不生成、鳥不跑碰撞）；倒帶完成才進 Playing；Ready 期間背景持續倒帶不凍結。

### T2 — UIController 加 Ready case（P0, scene-developer, arch-review: yes）
- **Goal**：Ready 期間 UI 淨空（menu/overlay 隱藏）但保留 HUD ScoreText。
- **作法**：`OnStateChanged` switch 加 `case GameManager.GameState.Ready:` → `SetActive(menuCanvas, false); SetActive(overlayCanvas, false);`（同 Playing case，FR-06）。HUD 由 `OnScoreChanged` 事件驅動（`Begin()` fire 0），不需在此硬寫。
- **開工前置**：無 Editor API 依賴；確認 `OnStateChanged` 現有 switch 結構（`UIController.cs:135-193`）。
- **驗收**：Ready 期間 menuCanvas/overlayCanvas 隱藏；ScoreText 顯示 "Score: 0"。

### T3 — PipeSpawner 倒數式計時＋ResetAll seed（P0, gameplay-developer, arch-review: no）
- **Goal**：進 Playing 第一幀生成第一根水管，與鳥同幀現身；計時語意改倒數式。
- **作法**：
  - `Update` 計時改倒數式（FR-07）：`spawnTimer -= Time.deltaTime; if (spawnTimer <= 0f) { SpawnPipe(); spawnTimer = spawnInterval; }`。
  - `ResetAll()` 加 `spawnTimer = 0f;`（seed，FR-07）。
- **開工前置**：確認 `ResetAll()` 呼叫時點（`GameManager.HidePlayerAndPipes` 於 `StartGame` 內、`SetState(Ready)` 之後呼叫）與 `Update` 的 `IsPlaying()` 守衛（`PipeSpawner.cs:51`）——seed=0 在 Ready 期間不被 `spawnTimer -= dt` 覆寫（假設 B）。
- **驗收**：進 Playing 第一幀生成第一根水管；後續間隔穩定為 `spawnInterval`；Ready 期間不生成。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 Ready 期間重複 StartGame | 倒帶期間再次呼叫 StartGame（如連點開始鈕）會重入 | FR-02 重入擋 `if (state == Playing \|\| state == Ready) return;` |
| BC-2 Ready 落入 SetState default 被凍結 | 若 Ready 未加 case，落入 default `SetFrozen(true)`，倒帶動畫停住 | FR-04 顯式 `case Ready: break;` |
| BC-3 倒帶期間水管提前生成 | 舊流程 SetState(Playing) 過早，倒帶期間 IsPlaying()==true 水管生成 | FR-03 先 SetState(Ready)（IsPlaying()==false）再倒帶 |
| BC-4 倒帶期間隱藏鳥跑碰撞 | 舊流程隱藏鳥仍跑重力/地板天花板碰撞 | FR-03 Ready 期間 IsPlaying()==false，GameManager.Update 地板/天花板檢查（`GameManager.cs:52`）不執行 |
| BC-5 進 Playing 第一根水管延遲 | 若 spawnTimer 未 seed 0，進 Playing 後要等 interval 才出第一根 | FR-07 `ResetAll()` seed `spawnTimer=0` |
| BC-6 背景過渡永不完成 | 某層過渡卡住 → callback 不執行 → 鳥永不顯示、卡死 | FR-08 接受風險，不加 watchdog，靠 Console 錯誤抓取 |
| BC-7 Ready 期間 HUD 消失 | 若 Ready case 誤關 HUD，倒帶期間看不到分數 | FR-06 Ready 只關 menu/overlay，保留 ScoreText |

## 驗收標準（整體，Validator 對照用）

- [ ] **重入擋**：Ready 過渡期間重複呼叫 `StartGame()` 被忽略（不重啟倒帶、不重設分數）
- [ ] **倒帶期間不觸發**：背景倒帶那 1 秒，`IsPlaying()` 為 false——水管不生成、隱藏鳥不跑重力/碰撞
- [ ] **倒帶期間背景持續**：Ready 期間背景層持續倒帶（不凍結），倒帶完成後轉正常捲動
- [ ] **同幀原子**：倒帶完成後，鳥顯示＋進 Playing 同幀發生，中間無 gameplay 窗口
- [ ] **第一根水管同幀**：進 Playing 第一幀即生成第一根水管，與鳥同幀現身
- [ ] **計時倒數式**：後續水管間隔穩定為 `spawnInterval`（倒數式語意）
- [ ] **UI 淨空**：Ready 期間 menuCanvas/overlayCanvas 隱藏；HUD ScoreText 顯示 "Score: 0"
- [ ] **無 watchdog**：未新增任何 timeout/Invoke/coroutine 超時保險
- [ ] **不回歸**：`IsPlaying()` 語義保持；ScoreTrigger 唯一計分、碰撞死亡路徑不變

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（GameManager.cs:7/93-103/80-86/169-172、GameStateAuthority.cs:27-30、PipeSpawner.cs:51/53-59/93-96、UIController.cs:135-193）。
- **未驗證假設**：假設 A（Ready 期間背景持續倒帶）、假設 B（seed=0 在 Ready 期間不被覆寫）明確標「未驗證假設」，T1/T3 已註明開工驗證。
- **一致性**：BC-1 與 FR-02/T1 一致（重入擋）；BC-2 與 FR-04/T1 一致（Ready 不凍結）；BC-3/BC-4 與 FR-03/T1 一致（倒帶期間 IsPlaying()==false）；BC-5 與 FR-07/T3 一致（seed=0）；BC-6 與 FR-08/T1 一致（無 watchdog）；BC-7 與 FR-06/T2 一致（UI 淨空）。
- **Editor API 驗證**：本功能無新增 Editor API 依賴（enum 擴張、switch 加 case、計時改寫皆純 C#）；T1 已加「開工先用 unity_reflect 驗證 enum/switch 現況」提示。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**，範圍：

1. **GameState enum 擴張**（T1）：核心狀態機（ADR-006）新增 `Ready` 過渡態，改變 Menu/GameOver→Playing 轉移的時序語義。屬核心架構改動。
2. **StartGame 時序重構**（T1）：`SetState(Playing)` 從「先執行」改為「倒帶完成後、與 ShowPlayerAtStart 同幀」，影響狀態轉移與背景過渡的耦合。
3. **SetState 背景 switch 與 Ready 的互動**（T1）：Ready 必須不落入 default `SetFrozen(true)`（否則倒帶被凍結）——這是狀態→背景耦合（ADR-006 B8）的微妙互動，需 Tech Lead 審查確認不破壞既有凍結政策。

T3（PipeSpawner 倒數式計時）為自包含改動（計時語意重構＋seed），**arch-review = no**。