# 規格：鳥的 spritesheet Animator 動畫系統（blob-animator）

> Status: final（追認完成） ｜ Owner: pm-agent ｜ arch-review: **yes**（T1/T2/T3 觸發架構變更） ｜ Created: 2026-09-04 ｜ Revised: 2026-09-05
> 設計拷問已完成（Sisyphus＋人類達成共識），本規格完整記錄 D1-D9 決定。
> **2026-09-05 修訂註記（A2/B2/B1 追認）**：實作過程動態驗證發現 bug，Tech Lead 裁決設計變更（ADR-013 決策 3/4/5/8/11 修訂、specs/architecture/animator-system.md A2/B2 修訂），PM 追認如下：FR-02 橋樑層改 AnimatorStateInfo 查詢（B2，Animation Event 退役）；FR-05 Assemble 在 Ready 態播放、AssembleComplete 才 SetState(Playing)（A2，ADR-012 同幀原子放寬）；FR-14 speed 改 Update 每幀求值（B1）；FR-19 事件名定案（CS0102）＋IsAssembling/ResetVisual/once 旗標。詳見各 FR 條文。動態驗證 D1-D10 待人類實測回填（Validator Verdict 暫定）。

## 功能：把鳥從 3D quad 換成 spritesheet Animator 動畫

- **目標**：Bird 物件由目前的 3D quad（MeshFilter＋MeshRenderer）替換為 **SpriteRenderer＋Unity Animator**（.anim 剪輯＋.controller 狀態機）驅動的 spritesheet 動畫。跳躍改為「蓄力（012）→ 上升（345）」的動畫節奏（按鍵不再直接給衝力）；死亡改為「解體（Dissasemble）→ 淡出 → 才 GameOver」的流程。暫停時動畫凍結。
- **性質**：新動畫系統（專案首個 Animator 基礎建設）＋既有核心腳本（PlayerController 跳躍物理、GameManager 死亡流程）重構。不新增 GameState、不改 timeScale、不碰計分/碰撞/背景架構。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/BlobAnimator.cs` | **新建**（控制層：命令＋事件＋暫停＋死亡淡出） |
| Script | `Assets/Scripts/PlayerController.cs` | **核心改造**（跳躍物理重構：衝力改 OnJumpRise 觸發、速度轉負→PlayLand、輸入 gate） |
| Script | `Assets/Scripts/GameManager.cs` | **核心改造**（死亡流程重構：Die→Dissasemble→淡出→EndGame、dying 守衛、re-entry） |
| Script | `Assets/Scripts/CollisionHandler.cs` | **不變**（OnTriggerEnter2D→gameManager.Die() 保持） |
| Scene | `Assets/Scenes/SampleScene.unity` | **改造**（Bird 換 SpriteRenderer＋掛 Animator＋BlobAnimator、collider 尺寸調整） |
| Asset | `Assets/Art/Blob Character Pack/Blue-Blob-sheets/Blue-Blob-Assemble.png` | 素材（20 幀，已 slice） |
| Asset | `Assets/Art/Blob Character Pack/Blue-Blob-sheets/Blue-Blob-Dissasemble.png` | 素材（15 幀，已 slice） |
| Asset | `Assets/Art/Blob Character Pack/Blue-Blob-sheets/Blue-Blob-Jump.png` | 素材（6 幀，已 slice） |
| Asset | `Assets/Art/Blob Character Pack/Blue-Blob-sheets/Blue-Blob-Land.png` | 素材（5 幀，已 slice） |
| Asset | 4 個 `.anim` 剪輯＋1 個 `.controller` | **新建**（T5，Unity MCP 建，不可手寫 YAML） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **Bird 目前是 3D quad**：`SampleScene.unity:2179-2376`（fileID 1181836431）＝`Transform(0,0,-0.02)`（:2255）＋`PlayerController`（:2212）＋`Rigidbody2D`（:2220）＋`MeshFilter`（:2262，Mesh 10210）＋`MeshRenderer`（:2270，material guid `55dcad50e05ccf148ab926bba32e21c3` :2294、sortingOrder 1000 :2315）＋`CollisionHandler`（:2319）＋`BoxCollider2D`（:2332，`m_Size {x:1, y:1}` :2375）。**沒有 SpriteRenderer**。
2. **專案零 Animator 基礎建設**：glob `Assets/**/*.anim` 與 `Assets/**/*.controller` 皆零命中。
3. **PlayerController.cs**（2026-09-05 追認後現況）：手動重力（`rb.gravityScale = 0f`，:36）；`HandleInput` 在 `Keyboard.current.spaceKey.wasPressedThisFrame` → `blobAnimator.PlayJump()`（:77-86，**不再直接給衝力**，FR-09）；訂閱 `BlobAnimator.JumpRise`（:45）→ `OnJumpRise` 設 `currentVelocity = flapForce`（:89-93，FR-10）；`HandleGravity` 開頭 `if (blobAnimator.IsAssembling) return;`（:99，AN13——Assemble 期間鳥靜止）；`HandleLandAnimation` 速度<0 且非 dying → `PlayLand()`（:116-124，isLanding 防重複）；`HandleRotation` 已註解（:74，改用 spritesheet 動畫不再旋轉）；`Die()`＝`isDead=true`＋速度歸零（:147-152）；`SetVisible(bool)`＝`Renderer.enabled`＋`collider.enabled`（:56-62，ADR-007）；`Update` 由 `GameStateAuthority.IsPlaying()` gate（:68，ADR-011）。
4. **GameManager.cs**（2026-09-05 追認後現況）：`GameState` enum＝`{Menu, Ready, Playing, Paused, GameOver}`（:7，ADR-006/012）；`StartGame()`＝重入擋（:137）→`scoreSession.Begin()`（:139）→`SetState(Ready)`（:140）→`HidePlayerAndPipes()`（:141）→`background.ScrollBackToStart(onComplete: {SetFrozen(true); ShowPlayerAtStart();})`（:143-146，**不 SetState(Playing)**——A2：Assemble 在 Ready 態播，`AssembleComplete`→`if(Ready) SetState(Playing)`，:72-75）；`Die()`＝`if Playing→dying 守衛→player.Die()→停 collider→PlayDissasemble()`（:206-216）；`EndGame()`＝`if Playing/Paused→scoreSession.End()→SetState(GameOver)`（:166-173）；`OnStateChanged` 事件存在（:25）；`IsPlaying()`＝`state==Playing`（:226-229）。
5. **CollisionHandler.cs**：`OnTriggerEnter2D` 對 `"Pipe"` tag→`gameManager.Die()`（:7-14）。
6. **美術素材已 slice 好**（64×32 grid，`spriteMode: 2`）：`Blue-Blob-Assemble.png.meta` 20 幀、`Blue-Blob-Dissasemble.png.meta` 15 幀、`Blue-Blob-Jump.png.meta` 6 幀、`Blue-Blob-Land.png.meta` 5 幀（grep meta `nameFileIdTable` 計數）；另有 `Blue-Blob-Idle.png.meta` 6 幀、`Blue-Blob-Run.png.meta` 8 幀（**本次不用**）。每幀有獨立 `spriteID` GUID（如 Jump `_0`＝`8f75dd1a012453a4386b83831881bdab`，`Blue-Blob-Jump.png.meta:115`）。
7. **house style**：暫停機制＝timeScale 恆 1＋各系統自查 `GameStateAuthority.IsPlaying()`＋背景 `SetFrozen` 單向下發（ADR-006/011）；UI 動畫純 C# 自建 tween（UITween）先例；動畫資產**一律用 Unity MCP 建**（asset/scene-developer 的工具），不可手寫 .anim/.controller YAML。

### 未驗證假設（不得視為既定事實）

- **假設 A**：Unity MCP 伺服器提供完整的動畫 API（`create_animation_clip` / `add_animation_keyframe` / `create_animator_controller` / `add_animator_state` 等；`add_animation_event` 因 B2 退役不再需要）。**T5 開工前必須驗證工具存在**；若缺某工具，回報 Sisyphus 而非自行變通。
- **假設 B**（**已由 B2 解決，2026-09-05**）：Animation Event 掛幀位置觸發——動態驗證推翻（「只能跳一次」：`animator.Play("Jump")` 對同態重入不重播剪輯、事件不重觸發），核心時序改 AnimatorStateInfo 查詢（FR-02 B2），Animation Event 退役，本假設不再適用。
- **假設 C**：剪輯採樣率與幀率需一致（建議 12fps 或依美術原始幀率）。「美術原始幀率為何」需開工確認。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 鳥從 3D quad 換成 spritesheet Animator 動畫 | FR-01／FR-02／FR-22／FR-23／FR-24；T1／T4／T5 |
| 跳躍有「蓄力→上升」的動畫節奏（按鍵不直接給衝力） | FR-09／FR-10／FR-11／FR-12；T2 |
| 死亡有「解體→淡出→才 GameOver」的流程 | FR-15／FR-16／FR-17／FR-18；T3 |
| 暫停時動畫凍結 | FR-14；T1／T6 |
| Assemble 播完遊戲才開始（世界在 Assemble 期間完全凍結） | FR-05（A2）；T1／T3 |
| 動畫資產用 Unity MCP 建、GUID 引用 | FR-01；T5 |

## 功能需求（FR）

### D1 技術路線：Unity Animator 四層分離

- **FR-01 技術路線**：採用 Unity Animator（`.anim` 剪輯＋`.controller` 狀態機）。動畫資產一律用 Unity MCP 建（asset/scene-developer 的工具），**不可手寫 .anim/.controller YAML**。
- **FR-02 四層分離**：
  - **素材層**：純幀序列（已 slice 的 spritesheet），零邏輯。
  - **狀態機層**：`.controller` 狀態機，轉換條件全由程式參數／Event 驅動（無時間條件、無自動轉換）。
  - **橋樑層**：**AnimatorStateInfo 查詢（state-query，B2 修訂）**——BlobAnimator.Update 讀 `GetCurrentAnimatorStateInfo(0)`，依 state＋normalizedTime fire C# 事件（once 旗標防重複）。**Animation Event 從核心時序退役**——.anim 剪輯零 Animation Event；未來輔助 hook（音效等非關鍵）用新事件名（如 `OnJumpSound`）加回。
  - **控制層**：`BlobAnimator.cs`（命令＋事件＋暫停）。

### D2 狀態機（5 狀態，無 Idle、無 Run）

- **FR-03 狀態集**：`{ Hidden, Assemble, Land, Jump, Dissasemble }`。**無 Idle、無 Run**（Idle 6 幀、Run 8 幀素材本次不用）。
- **FR-04 Hidden**：Menu 期間鳥 `SetVisible(false)`、Animator 停播。→ `ShowPlayerAtStart` 現身（**Ready 態**，A2）→ `Assemble`（Ready 態播放，鳥可見但遊戲未開始）。
- **FR-05 Assemble**（20 幀，**Ready 態播放，A2 修訂**）：`ShowPlayerAtStart` 現身後在 Ready 態播 Assemble；播完（state-query 偵測 `normalizedTime>=1`）fire `AssembleComplete` → `SetState(Playing)` → 鳥開始下墜（速度轉負）→ `Land`。Assemble 播放期間**無視跳躍鍵**＋**世界完全凍結**（背景 `SetFrozen(true)`、水管不生成、計分不觸發——`IsPlaying()==false`）。
  - **ADR-012 同幀原子放寬（A2 追認）**：`ShowPlayerAtStart` 與 `SetState(Playing)` 由「同幀原子」改為「事件驅動」（`AssembleComplete` → `SetState(Playing)`）。「無 gameplay 窗口」精神保留——Ready 期間 `IsPlaying()==false`，無 gameplay 系統運行。
- **FR-06 Land**（5 幀）：播完停最後一幀（下墜中持續顯示）；Land 期間按 jump → `Jump`。
- **FR-07 Jump**（012 蓄力→345 上升）：012 播完（state-query 偵測 `normalizedTime>=0.5`，frame3/6）fire `JumpRise` → 鳥設 flapForce 上升；上升結束（速度轉負）→ `Land`；345 期間按 jump → 回 012 重播。
- **FR-08 Dissasemble**（15 幀）：任何時刻 `Die()` → `Dissasemble` → 播完 → 淡出 → `EndGame()`。

### D3 Jump 物理

- **FR-09 輸入不直接給衝力**：按 jump **不**直接設 `currentVelocity = flapForce`。輸入 → 播 Jump 012。
- **FR-10 JumpRise 觸發衝力**：012 播完（frame3 起點，state-query 偵測）fire `JumpRise` → `PlayerController` 的 `OnJumpRise` handler 才設 `currentVelocity = flapForce`。
- **FR-11 蓄力期間無視跳躍鍵**：012 蓄力期間無視跳躍鍵；Assemble 播放期間也無視跳躍鍵。

### D4 輸入規則

- **FR-12 輸入規則總表**：
  - 012 蓄力期間：**無視輸入**。
  - 345 上升與 Land 下降期間：可再跳 → 回 012 重播。
  - Assemble 期間：**無視輸入**。

### D5 Loop

- **FR-13 全部剪輯不 loop**：播完停最後一幀，直到狀態改變。

### D6 暫停

- **FR-14 animator.speed 控制**：`animator.speed` 由 **Update 每幀求值**（B1 修訂）：`(lastState == Playing || isAssembling) ? 1 : 0`——Playing 或 Assemble 播放中（Ready 態）→ 1；其餘（Menu／Ready 非 Assemble／Paused／GameOver）→ 0。`BlobAnimator` 訂閱 `GameManager.OnStateChanged` **只存 `lastState` 快照**、不在事件 fire 時求值（B1 教訓：A2 下 `PlayAssemble()` 在 Ready 態呼叫，事件 fire 時求值會卡 speed=0 → Assemble 凍結第一幀 → AssembleComplete 永不 fire → 卡死）。**不改 GameManager、不碰 timeScale**（與 ADR-006/011 一致）。

### D7 死亡流程

- **FR-15 死亡流程**：`Die()` → 鳥凍結原地（既有 `isDead`＋速度歸零＋**停 collider**）→ 播 `Dissasemble` → 播完 → 淡出（alpha→0）→ 才 `EndGame()`（GameOver UI 延後）。
- **FR-16 dying 守衛**：死亡流程期間**維持 Playing 態**＋dying 守衛（不新增 GameState，與 ADR-006/011 一致）。
- **FR-17 水管持續捲動**：死亡流程期間水管照常捲動（不因鳥死亡而停）。
- **FR-18 re-entry guard**：`Die()` 加 re-entry guard（防止死亡流程期間重複觸發）。

### D8 架構分工

- **FR-19 BlobAnimator.cs**（Bird 上，控制層）：持有 Animator；命令 `PlayHidden` / `PlayAssemble` / `PlayJump` / `PlayLand` / `PlayDissasemble`（**全部 `animator.Play(name, 0, 0f)` 強制從頭播**，B2 雙保險）；C# 事件 `JumpRise`、`AssembleComplete`、`LandComplete`、`DissasembleComplete`、`OnDeathFadeComplete`（**CS0102 修正：事件去 "On" 前綴**；無衝突事件維持 house style）；**state-query 核心時序**（Update 讀 `GetCurrentAnimatorStateInfo(0)`＋normalizedTime＋once 旗標，B2）；訂閱 `OnStateChanged` 存 `lastState` 快照、Update 每幀求值 `animator.speed`（B1）；死亡淡出邏輯所在（`FadeOut` 自查 `IsPlaying()`，H5）；**`IsAssembling` 查詢**（PlayerController.HandleGravity gate，AN13）；**`ResetVisual()`**（恢復 alpha=1＋清 gate/once 旗標，ShowPlayerAtStart 呼叫）；輸入 gate 旗標（`isCharging`/`isAssembling`）。
- **FR-20 PlayerController.cs**（物理層）：移除按鍵當下直接設衝力；訂閱 `JumpRise`（handler `OnJumpRise`）才設 `currentVelocity = flapForce`；速度 <0 且非 dying 時呼叫 `PlayLand()`；輸入觸發改為呼叫 `PlayJump()`（或經 BlobAnimator gate）。
- **FR-21 GameManager.cs**（死亡編排）：`Die()` → `PlayDissasemble` →（BlobAnimator 內部 state-query 偵測播完→啟動淡出）→ `OnDeathFadeComplete` → `EndGame()`。GameManager **不訂閱** `DissasembleComplete`（淡出啟動在 BlobAnimator 內部，ADR-013 決策 4）。

### D9 Bird 視覺替換

- **FR-22 SpriteRenderer 替換**：Bird 的 `MeshFilter`＋`MeshRenderer`（3D quad）→ **`SpriteRenderer`**（Animator 驅動 sprite）。
- **FR-23 sortingOrder 維持**：sortingOrder 維持 **1000**（鳥遮擋層級不變）。
- **FR-24 collider 尺寸調整**：`BoxCollider2D` 尺寸需依新 sprite（64×32＝2:1）調整（現為 1×1，:2375）。

## 非功能需求（NFR）

- **NFR-01 不回歸**：`IsPlaying()` 語義保持（`state == Playing`）；ScoreTrigger 唯一計分、CollisionHandler 碰撞死亡路徑不變；ready-transition 已驗收成果（Ready 過渡態、倒數式水管計時）不得破壞。**PipeSpawner 零改動（A2 確認）**：倒數式計時＋`ResetAll()` seed=0 不變——Ready 期間 `IsPlaying()==false` 下 `spawnTimer -= dt` 不跑，seed 全程保留到 Assemble 播完進 Playing 第一幀才消耗（「進 Playing 第一幀生成」語意保持，與 ready-transition FR-07 一致）。
- **NFR-02 凍結一致性**：暫停/GameOver 時動畫凍結（`animator.speed = 0`）；Playing 時 `1`；Ready＋Assemble 時 `1`（Update 每幀求值，FR-14 B1）。不引入 timeScale 變更（ADR-006/011）。
- **NFR-03 不新增 GameState**：死亡流程用 dying 守衛，不新增 GameState（ADR-006/011）。
- **NFR-04 資產引用用 GUID**：SpriteRenderer／AnimationClip 皆走 GUID 引用（spriteID），勿用檔名（house style，:115 例）。
- **NFR-05 動畫資產 Unity MCP 建**：不可手寫 .anim/.controller YAML（FR-01）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | BlobAnimator.cs 控制元件 | P0 | gameplay-developer | **yes** | 控制層：命令＋事件＋暫停＋死亡淡出 | `Assets/Scripts/BlobAnimator.cs` | 命令可觸發對應剪輯；animator.speed 每幀求值（Paused/GameOver=0、Playing=1、Ready＋Assemble=1）；淡出邏輯在位 |
| T2 | PlayerController 跳躍物理重構 | P0 | gameplay-developer | **yes** | 衝力改 OnJumpRise 觸發、速度轉負→PlayLand、輸入 gate | `Assets/Scripts/PlayerController.cs` | 按鍵不直接給衝力；OnJumpRise 才設 flapForce；速度<0 非 dying 呼叫 PlayLand |
| T3 | GameManager 死亡流程重構 | P0 | gameplay-developer | **yes** | Die→Dissasemble→淡出→EndGame、dying 守衛、re-entry | `Assets/Scripts/GameManager.cs` | 死亡流程期間維持 Playing；水管持續捲動；EndGame 延後到淡出後 |
| T4 | 場景改造：Bird 換 SpriteRenderer＋掛 Animator＋BlobAnimator、collider 尺寸 | P0 | scene-developer | no | Bird 視覺替換為 Animator 驅動 sprite | `Assets/Scenes/SampleScene.unity` | Bird 有 SpriteRenderer＋Animator＋BlobAnimator；sortingOrder 1000；collider 依 2:1 調整 |
| T5 | 動畫資產：4 個 .anim＋1 個 .controller | P0 | asset-developer | no | 建立剪輯與狀態機（Unity MCP 建） | 4 `.anim`＋1 `.controller` | 剪輯幀數正確；無 Animation Event（B2）；GUID 引用 |
| T6 | 暫停時 Animator 凍結的正確接線驗證 | P1 | gameplay-developer | no | 驗證 OnStateChanged→animator.speed 接線 | `Assets/Scripts/BlobAnimator.cs` | Paused/GameOver 時 speed=0、Playing 時 1、Ready＋Assemble 時 1（可能併入 T1） |

### T1 — BlobAnimator.cs 控制元件（P0, gameplay-developer, arch-review: yes）
- **Goal**：建立控制層——持有 Animator，提供命令（PlayHidden/PlayAssemble/PlayJump/PlayLand/PlayDissasemble）、事件（JumpRise/AssembleComplete/LandComplete/DissasembleComplete/OnDeathFadeComplete）、暫停（訂閱 OnStateChanged 存快照＋Update 每幀求值 animator.speed）、死亡淡出邏輯。
- **作法**：
  - 掛 Bird 上，持有 `Animator` 引用（FR-19）。
  - 命令方法對應 `.controller` 狀態轉換（FR-03~08）。
  - 訂閱 `GameManager.OnStateChanged` **只存 `lastState` 快照**；`animator.speed` 由 Update 每幀求值 `(lastState == Playing || isAssembling) ? 1 : 0`（FR-14 B1，不改 GameManager、不碰 timeScale）。
  - 死亡淡出邏輯（alpha→0）所在（FR-15）。
- **開工前置**：先用 `unity_reflect` 驗證 `Animator` 相關 API 存在性（`Animator` component、`Play`/`speed` 欄位、`GetCurrentAnimatorStateInfo`）。確認 `.controller` 狀態名與命令對應（T5 產物）。
- **驗收**：命令可觸發對應剪輯；animator.speed 每幀求值（Paused/GameOver=0、Playing=1、Ready＋Assemble=1）；淡出邏輯在位。

### T2 — PlayerController 跳躍物理重構（P0, gameplay-developer, arch-review: yes）
- **Goal**：改變既有 Flap 語義——按鍵不再直接給衝力，改由 OnJumpRise 觸發；速度轉負且非 dying 時呼叫 PlayLand；輸入 gate。
- **作法**：
  - 移除 `Flap()` 直接 `currentVelocity = flapForce`（:65-68）——按鍵當下不再給衝力（FR-09）。
  - 訂閱 `JumpRise`（handler `OnJumpRise`）：才設 `currentVelocity = flapForce`（FR-10）。
  - 速度 <0 且非 dying 時呼叫 `PlayLand()`（FR-20）。
  - 輸入觸發改為呼叫 `PlayJump()`（或經 BlobAnimator gate）（FR-12）。
- **開工前置**：確認 `HandleInput`（:57-63）與 `Flap`（:65-68）現況；確認 `JumpRise` 事件由 BlobAnimator 提供（T1 前提）。
- **驗收**：按 jump 不直接給衝力；OnJumpRise 才設 flapForce；速度轉負且非 dying 呼叫 PlayLand；012/Assemble 期間無視輸入。

### T3 — GameManager 死亡流程重構（P0, gameplay-developer, arch-review: yes）
- **Goal**：改變 ADR-006 死亡路徑——Die() 不再立即 EndGame()，改為 Die→PlayDissasemble→（BlobAnimator 內部淡出）→OnDeathFadeComplete→EndGame()；dying 守衛；re-entry guard。
- **作法**：
  - `Die()`（:159-164）改為：`if Playing` → dying 守衛（re-entry guard，FR-18）→ `player.Die()`（凍結原地＋停 collider）→ `PlayDissasemble()`（FR-15）。
  - 淡出由 BlobAnimator 內部啟動（state-query 偵測 Dissasemble 播完）；`OnDeathFadeComplete` → 才 `EndGame()`（FR-15，GameOver UI 延後）。GameManager 不訂閱 `DissasembleComplete`。
  - 死亡流程期間**維持 Playing 態**（FR-16，不新增 GameState）；水管照常捲動（FR-17）。
- **開工前置**：確認 `Die()`（:159-164）與 `EndGame()`（:125-132）現況；確認 `OnDeathFadeComplete` 事件由 BlobAnimator 提供（T1 前提）。
- **驗收**：死亡流程期間維持 Playing；水管持續捲動；EndGame 延後到淡出後；Die() 重入被擋。

### T4 — 場景改造：Bird 換 SpriteRenderer＋掛 Animator＋BlobAnimator、collider 尺寸（P0, scene-developer, arch-review: no）
- **Goal**：Bird 視覺替換為 Animator 驅動的 sprite。
- **作法**：
  - 移除 Bird 的 `MeshFilter`（:2262）＋`MeshRenderer`（:2270），改加 **`SpriteRenderer`**（FR-22）。
  - 掛 `Animator`（.controller）＋`BlobAnimator`（FR-19）。
  - sortingOrder 維持 **1000**（FR-23）。
  - `BoxCollider2D` 尺寸依新 sprite（64×32＝2:1）調整（FR-24，現 1×1 :2375）。
- **開工前置**：確認 Bird 物件結構（SampleScene.unity:2179-2376）；確認 T5 產物（.anim/.controller）已存在。
- **驗收**：Bird 有 SpriteRenderer＋Animator＋BlobAnimator；sortingOrder 1000；collider 依 2:1 調整。

### T5 — 動畫資產：4 個 .anim＋1 個 .controller（P0, asset-developer, arch-review: no）
- **Goal**：建立剪輯與狀態機（Unity MCP 建）。
- **作法**：
  - 4 個 `.anim` 剪輯：Assemble（20 幀）、Dissasemble（15 幀）、Jump（6 幀）、Land（5 幀）（FR-03~08）。
  - 1 個 `.controller` 狀態機：`{Hidden, Assemble, Land, Jump, Dissasemble}`，轉換條件全由程式參數／Event 驅動（FR-02）。
  - **無 Animation Event**（B2：核心時序由 state-query 驅動；.anim 剪輯零事件，避免雙重 fire 與「no receiver」警告）。
  - 剪輯採樣率與幀率需一致（建議 12fps 或依美術原始幀率，假設 C）。
  - Sprite slice 引用用 GUID（spriteID），勿用檔名（NFR-04）。
- **開工前置**：**先用 Unity MCP 驗證動畫 API 工具存在**（`create_animation_clip` / `add_animation_keyframe` / `create_animator_controller` / `add_animator_state` 等，假設 A；`add_animation_event` 因 B2 退役不再需要）；若缺某工具，**回報 Sisyphus 而非自行變通**。
- **驗收**：剪輯幀數正確（20/15/6/5）；**無 Animation Event**（B2）；GUID 引用；狀態機 5 態無 Idle/Run。

### T6 — 暫停時 Animator 凍結的正確接線驗證（P1, gameplay-developer, arch-review: no）
- **Goal**：驗證 OnStateChanged→animator.speed 接線正確（可能併入 T1）。
- **作法**：確認 Paused/GameOver 時 `animator.speed = 0`、Playing 時 `1`、Ready＋Assemble 時 `1`（FR-14 B1 每幀求值）。
- **開工前置**：T1 完成後驗證。
- **驗收**：暫停/GameOver 時動畫凍結；Playing 時恢復；Ready＋Assemble 時正常播放。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 死亡流程期間重複 Die | 死亡流程（Dissasemble 播放中）再次觸發 Die（如水管碰撞）會重入 | FR-18 `Die()` re-entry guard |
| BC-2 死亡流程期間按 jump | Dissasemble 播放中按 jump 不應觸發跳躍 | FR-12 輸入規則（Dissasemble 期間無視輸入）＋FR-16 dying 守衛 |
| BC-3 死亡流程期間暫停 | 死亡流程中按 P 暫停，動畫應凍結 | FR-14 animator.speed=0（Paused） |
| BC-4 蓄力 012 期間連點 jump | 012 蓄力期間連點 jump 不應重播或跳躍 | FR-11 蓄力期間無視跳躍鍵 |
| BC-5 上升 345 期間再跳 | 345 上升期間按 jump 應回 012 重播 | FR-07／FR-12 |
| BC-6 淡出期間狀態 | 淡出（alpha→0）期間維持 Playing 態，水管持續捲動 | FR-16／FR-17 |
| BC-7 剪輯播完停最後一幀 | 剪輯不 loop，播完停最後一幀直到狀態改變 | FR-13 |
| BC-8 動畫 API 工具缺失 | Unity MCP 缺某動畫 API 工具 | 假設 A：回報 Sisyphus 而非自行變通 |
| BC-9 暫停時動畫未凍結 | 若 animator.speed 接線錯誤，暫停時動畫仍播 | FR-14／T6 驗證 |
| BC-10 Assemble 卡死（B1 教訓） | 若 speed 求值綁 OnStateChanged fire 時機，A2 下 PlayAssemble 在 Ready 態呼叫後 isAssembling 變化不被重新求值 → speed 卡 0 → Assemble 凍結第一幀 → AssembleComplete 永不 fire → SetState(Playing) 永不發生（卡死） | FR-14（B1：Update 每幀求值） |
| BC-11 Assemble 期間世界未凍結 | 若 Assemble 期間進 Playing（舊同幀流程），背景/水管在 Assemble 播完前就動，違反「Assemble 播完遊戲才開始」 | FR-05（A2：Ready 態播＋AssembleComplete 才 SetState(Playing)） |

## 驗收標準（整體，Validator 對照用）

- [ ] **技術路線**：Bird 使用 SpriteRenderer＋Unity Animator（.anim＋.controller），非手寫 YAML
- [ ] **狀態機 5 態**：`{Hidden, Assemble, Land, Jump, Dissasemble}`，無 Idle、無 Run
- [ ] **橋樑層 state-query（B2）**：核心時序由 AnimatorStateInfo 查詢驅動（Update 讀 state＋normalizedTime＋once 旗標）；.anim 剪輯零 Animation Event
- [ ] **Assemble 播完遊戲才開始（A2）**：Assemble 在 Ready 態播放；播完（AssembleComplete）才 SetState(Playing)——Assemble 期間背景凍結、水管不生成、鳥靜止
- [ ] **跳躍蓄力**：按 jump 不直接給衝力；012 蓄力播完（frame3 起點，state-query 偵測 normalizedTime>=0.5）fire JumpRise 才設 flapForce
- [ ] **蓄力無視輸入**：012 蓄力期間與 Assemble 期間無視跳躍鍵
- [ ] **上升/下降可再跳**：345 上升與 Land 下降期間可再跳→回 012 重播
- [ ] **剪輯不 loop**：全部剪輯播完停最後一幀直到狀態改變
- [ ] **暫停凍結**：animator.speed 由 Update 每幀求值——Paused/GameOver 時 0、Playing 時 1、Ready＋Assemble 時 1（不碰 timeScale）
- [ ] **死亡流程**：Die→Dissasemble→淡出→才 EndGame()；死亡流程期間維持 Playing、水管持續捲動
- [ ] **dying 守衛**：死亡流程期間重複 Die 被擋（re-entry guard）
- [ ] **視覺替換**：Bird 有 SpriteRenderer（無 MeshFilter/MeshRenderer）；sortingOrder 1000；collider 依 2:1 調整
- [ ] **GUID 引用**：SpriteRenderer／AnimationClip 走 GUID（spriteID）引用，非檔名
- [ ] **不回歸**：`IsPlaying()` 語義保持；ScoreTrigger 唯一計分、CollisionHandler 碰撞死亡路徑不變；ready-transition 成果不破壞

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（SampleScene.unity:2179-2376/2255/2262/2270/2294/2315/2332/2375、PlayerController.cs:36/45/56-62/68/74/77-86/89-93/99/116-124/147-152、GameManager.cs:7/25/72-75/137-146/166-173/206-216/226-229、BlobAnimator.cs:24-32/69-75/81-93/98-127/131-145/149-164/169-185、CollisionHandler.cs:7-14、Blue-Blob-Jump.png.meta:115、glob 零命中）。
- **未驗證假設**：假設 A（Unity MCP 動畫 API 存在）、假設 C（剪輯採樣率/幀率一致）明確標「未驗證假設」，T5 已註明開工驗證；假設 B（Animation Event 觸發）**已由 B2 解決**（動態驗證推翻，核心時序改 state-query，2026-09-05）。
- **一致性**：BC-1 與 FR-18/T3 一致（re-entry guard）；BC-2 與 FR-12/FR-16/T2/T3 一致（輸入規則＋dying 守衛）；BC-3 與 FR-14/T1/T6 一致（暫停凍結）；BC-4 與 FR-11/T2 一致（蓄力無視輸入）；BC-5 與 FR-07/FR-12/T2 一致（345 可再跳）；BC-6 與 FR-16/FR-17/T3 一致（淡出期間維持 Playing）；BC-7 與 FR-13/T5 一致（不 loop）；BC-8 與假設 A/T5 一致（API 工具缺失回報）；BC-9 與 FR-14/T6 一致（暫停凍結驗證）；BC-10 與 FR-14（B1）一致（每幀求值）；BC-11 與 FR-05（A2）一致（Assemble 期間世界凍結）。
- **Editor API 驗證**：T1 已加「開工先用 unity_reflect 驗證 Animator API 存在性」；T5 已加「先用 Unity MCP 驗證動畫 API 工具存在」提示。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**，範圍：

1. **新動畫系統架構**（T1）：專案首個 Unity Animator 基礎建設（.anim＋.controller），引入新的四層分離架構（素材/狀態機/橋樑/控制）與 BlobAnimator.cs 控制元件。屬新架構導入。
2. **PlayerController 跳躍物理重構**（T2）：改變既有 `Flap()` 語義（按鍵當下直接給衝力 → `JumpRise` 事件觸發），影響輸入→物理的耦合。
3. **GameManager 死亡流程重構**（T3）：改變 ADR-006 死亡路徑（Die 立即 EndGame → Die→Dissasemble→淡出→EndGame），引入 dying 守衛（不新增 GameState，需 Tech Lead 確認與 ADR-006/011 一致）。

T4（場景改造）與 T5（動畫資產）為自包含改動（視覺替換＋資產建立），**arch-review = no**。

**2026-09-05 追認註記**：架構經實作動態驗證後由 Tech Lead 修訂（ADR-013 決策 3/4/5/8/11、specs/architecture/animator-system.md A2/B2/B1）——橋樑層改 state-query（B2）、Assemble 在 Ready 態播放（A2）、speed 每幀求值（B1）。本規格 FR-02/05/14/19 已同步追認。