# ADR-013: Blob Animator 系統（spritesheet Animator 四層分離＋死亡流程重構）

## Status

Accepted

> **2026-09-04 動態驗證修訂**：人類 Play mode 實測發現 2 個真實 bug，本 ADR 決策 3/4/5/8 修訂＋新增決策 11：
> 1. **問題 1**：Assemble 未播完背景/水管就動——採納 **A2**（Assemble 期間維持 Ready 態，AssembleComplete 才 SetState(Playing)）。
> 2. **問題 2**：只能跳一次——採納 **B2**（核心時序改 AnimatorStateInfo 查詢，不依賴 Animation Event；`animator.Play(name, 0, 0f)` 強制從頭播）。
> 完整裁決見決策 3/4/5/8/11 與 `specs/architecture/animator-system.md`。
>
> **2026-09-30 修訂：決策 8 翻轉——新增 GameState.Dying**（人類實測回報死亡期間水管/背景未凍結 bug，旗標方案失敗）：
> 1. **問題**：死亡流程期間 GameState 維持 Playing（FR-16），`isDying` 旗標僅 GameManager 內部自我參考、**從未被廣播**——水管照常移動（PipeController.cs:16）、照常生成（PipeSpawner.cs:51）、背景照常捲動（BGLayer.cs:166），違反 ADR-006 D3 凍結一致性。
> 2. **裁決**：翻轉決策 8——新增 `GameState.Dying` 取代 dying 旗標；水管在死亡期間**必須凍結**（FR-17 撤銷，本功能核心）。完整裁決見決策 5/8 修訂與 `specs/architecture/game-states.md`。

## Context

flipbird 目前鳥是 3D quad（MeshFilter＋MeshRenderer，SampleScene.unity:2262/2270），專案零 Animator 基礎建設（glob `Assets/**/*.anim` 與 `*.controller` 皆零命中）。需求（specs/requirements/blob-animator.md，D1-D9 已定案）要求把鳥換成 spritesheet Unity Animator 動畫：跳躍改「蓄力（012）→ 上升（345）」節奏（按鍵不再直接給衝力）、死亡改「解體→淡出→才 GameOver」流程、暫停時動畫凍結。T1（新 Animator 控制層）、T2（PlayerController 跳躍物理重構）、T3（GameManager 死亡流程重構）觸發架構變更。

## Decision

### 1. 四層分離（D1/FR-02）

- **素材層**：純幀序列（已 slice 的 spritesheet），零邏輯。
- **狀態機層**：`.controller` 狀態機，轉換條件全由程式參數／Event 驅動（無時間條件、無自動轉換）。
- **橋樑層**：Animation Event——Jump 剪輯 frame3 起點掛 `OnJumpRise`；各剪輯末端掛 `OnXxxComplete`。
- **控制層**：`BlobAnimator.cs`（命令＋事件＋暫停＋死亡淡出）。

### 2. 5 態狀態機（D2/FR-03）——裁決：成立

`{ Hidden, Assemble, Land, Jump, Dissasemble }`，**無 Idle、無 Run**。controller 預設態＝Hidden（Menu/Ready 鳥隱藏、Animator 停播）。理由：遊戲中鳥不存在「站立/跑步」語境——非隱藏即誕生（Assemble）、跳躍（Jump）、下墜（Land）、死亡（Dissasemble）；Idle/Run 素材本次不用，加入即 YAGNI。

### 3. 橋樑層（D1/FR-02）——**2026-09-04 修訂：核心時序改 AnimatorStateInfo 查詢（B2），Animation Event 退役**

原裁決：Animation Event 為橋樑層正式機制（時序留在資產單一事實源）。**動態驗證推翻**：Animation Event 在實戰中不可靠（「只能跳一次」——`animator.Play("Jump")` 對已在 Jump 態的重入不重播剪輯，事件不重觸發，isCharging 卡死）。**修訂**：

- **橋樑層＝BlobAnimator.Update 內 AnimatorStateInfo 查詢**：讀 `GetCurrentAnimatorStateInfo(0)`，依 state＋normalizedTime fire C# 事件（once 旗標防重複）。
- **Animation Event 從核心時序退役**：.anim 剪輯移除 OnJumpRise/OnXxxComplete 事件（T5），BlobAnimator 移除對應回呼方法——避免雙重 fire 與「no receiver」警告。
- **未來輔助 hook（音效等非關鍵）**：用**新事件名**（如 `OnJumpSound`）加回 Animation Event，不與核心時序衝突。
- **`animator.Play(name, 0, 0f)`**：命令方法強制從 normalizedTime 0 播——即使已在同態，也可靠重播（雙保險）。
- 原「時序留在資產」論點仍適用於**美術幀序**（素材層不變）；但**觸發時點**由 code 定義（normalizedTime 常數），可程式驗證。

### 4. BlobAnimator 控制層（D8/FR-19）——**2026-09-04 修訂：state-query 取代 Animation Event 回呼**

- 掛 Bird，持有 `Animator`。
- 命令：`PlayHidden` / `PlayAssemble` / `PlayJump` / `PlayLand` / `PlayDissasemble`（對應 .controller 狀態轉換）。**全部 `animator.Play(name, 0, 0f)` 強制從頭播**（B2 雙保險）。
- **C# 事件**（程式訂閱面）：`JumpRise` / `AssembleComplete` / `LandComplete` / `DissasembleComplete` / `OnDeathFadeComplete`。
- **state-query 機制（B2）**：BlobAnimator.Update 讀 `GetCurrentAnimatorStateInfo(0)`——Jump 態且 `normalizedTime >= 0.5`（frame3/6）→ fire `JumpRise`（once 旗標）；Assemble 態且 `normalizedTime >= 1` → fire `AssembleComplete`；Land 態且 `>= 1` → fire `LandComplete`；Dissasemble 態且 `>= 1` → fire `DissasembleComplete`＋內部啟動 FadeOut()。命令方法重置對應 once 旗標（PlayJump→jumpRiseFired=false 等）。
- **命名規則（CS0102 修正，保留）**：C# 事件去 "On" 前綴（`JumpRise` 等）；無衝突事件維持 house style（`OnDeathFadeComplete`）。原「回呼方法保留 OnXxx」因回呼方法已移除而簡化——事件名規則不變。
- 暫停：訂閱 `GameManager.OnStateChanged` → `animator.speed = (state == Playing || isAssembling) ? 1 : 0`（決策 5 修訂）。
- 死亡淡出：alpha→0 邏輯所在，**以 `GameStateAuthority.IsPaused()` 自查 gate**（暫停時淡出也凍結，與 ADR-006 D3 凍結政策一致；2026-09-30 由 `IsPlaying()` 改——Dying 態淡出必須執行，見決策 8 修訂）。淡出由 state-query 偵測 Dissasemble 播完後**內部啟動**——GameManager 不訂閱 `DissasembleComplete`，只訂閱 `OnDeathFadeComplete` → EndGame。
- **DissasembleComplete 事件澄清（M1，保留）**：state-query fire `DissasembleComplete` 事件（hook）＋內部啟動 FadeOut()，兩者並行互不依賴。決策 4 的「GameManager 不訂閱 DissasembleComplete」是**訂閱面**約束，非**發射面**禁令。
- `ResetVisual()`：恢復 alpha=1、取消進行中淡出（冪等）＋**清 gate 旗標與 once 旗標**（`isCharging=false; isAssembling=false; jumpRiseFired=false; assembleCompleteFired=false; landCompleteFired=false; dissasembleCompleteFired=false`）——供 `GameManager.ShowPlayerAtStart()` 呼叫。
- **輸入 gate 旗標（保留）**：`isCharging`（PlayJump→true、state-query fire JumpRise→false）＋`isAssembling`（PlayAssemble→true、state-query fire AssembleComplete→false）。`PlayJump()` 開頭 `if (isCharging || isAssembling) return;`；`PlayLand()` 開頭 `if (isAssembling || isCharging) return;`。
- **IsAssembling 查詢（保留）**：`public bool IsAssembling { get; private set; }`——PlayerController.HandleGravity 開頭 `if (blobAnimator.IsAssembling) return;`。**ADR-011 合規**：GA1 管「遊戲狀態查詢」（IsPlaying）wiring；IsAssembling 是**動畫子狀態查詢**（組件內部狀態），非遊戲狀態查詢，不經 GameStateAuthority 正確。

### 5. animator.speed 映射（FR-14 補完）——**2026-09-04 修訂：A2 配套；2026-09-05 修訂：B1 每幀求值；2026-09-30 修訂：接受 Dying**

`(lastState == Playing || lastState == Dying || isAssembling) ? 1 : 0`——Playing 或 Dying（Dissasemble 播放中）或 Assemble 播放中（Ready 態）→ 1；其餘（Menu/Ready 非 Assemble/Paused/GameOver）→ 0。**A2 配套**：Assemble 在 Ready 態播放，speed 需在 Ready＋isAssembling 時為 1。規格 FR-14 只提 Paused/GameOver→0；此映射涵蓋全部狀態。

**B1 修復（2026-09-05）**：speed **由 Update 每幀求值**（`UpdateAnimatorSpeed()`：`(lastState == Playing || isAssembling) ? 1 : 0`），OnStateChanged 訂閱**只存 `lastState` 快照**、不在事件 fire 時求值。原因：A2 流程下 `PlayAssemble()` 在 Ready 態呼叫（SetState(Ready) fire 之後），若 speed 只在事件 fire 時求值，`isAssembling=true` 的變化不被重新求值 → speed 卡 0 → Assemble 凍結第一幀 → AssembleComplete 永不 fire → SetState(Playing) 永不發生（卡死）。每幀求值使 `isAssembling=true` 後最多 1 幀內 speed=1，與事件 fire 時機無關。

**Dying 接受（2026-09-30）**：`lastState == Dying` 加入映射——Dissasemble 在 Dying 態播放，speed 需為 1（否則 Dissasemble 凍結第 0 幀 → `dissasembleCompleteFired` 永不 fire → 淡出永不啟動 → 卡死）。此為「死亡期間維持 Playing 態」繞過的第一個隱藏依賴（BlobAnimator.cs:144）。

### 6. OnStateChanged 第二訂閱者（ADR-011 GA4 演進條件觸發）——裁決：維持 plain C# event，不升級事件匯流排

BlobAnimator 訂閱 `OnStateChanged` 使訂閱者從「僅 UI」變為「UI＋BlobAnimator」，**觸發** ADR-011 GA4 演進條件。重估結論：**不升級**。事件匯流排的價值在於多對多解耦與動態訂閱管理；目前僅 2 個訂閱者、訂閱關係靜態、事件為單一狀態變更通知，升級為正式匯流排屬過度設計。GA4 條件保留，未來出現第三個事件訂閱者再重估。

與 ADR-011 查詢/命令分離一致：BlobAnimator 用**事件訂閱**（狀態轉變驅動）而非輪詢 `IsPlaying()`——查詢 seam（GameStateAuthority）仍為輪詢系統唯一查詢入口；命令（Die/StartGame 等）仍走 GameManager。BlobAnimator 不新增查詢、不承接命令，符合 GA3。

### 7. Flap 語義改變（D3/FR-09~10）——ADR-002 移動模型保留

按鍵不再直接 `currentVelocity = flapForce`；輸入 → `PlayJump()` → Jump 012 蓄力 → frame3 起點 `OnJumpRise` → 才設 `currentVelocity = flapForce`。ADR-002 移動模型（flap 力度、重力、terminal velocity、旋轉、X 固定）**全部保留**，僅「輸入→衝力」的時序延遲至動畫橋樑。屬 ADR-002 的時序精化，非模型替換。

### 8. 死亡流程重構（D7/FR-15~18）——**2026-09-30 修訂：Dying 態取代 dying 旗標**

- `Die()`：`if Playing` → **`SetState(Dying)`**（不再用 isDying 旗標）→ `player.Die()`（凍結原地＋速度歸零＋停 collider）→ `PlayDissasemble()`。
- `SetState` switch 加 **`case Dying: SetFrozen(true)`**——死亡瞬間背景立即凍結（顯式 case，比照 ADR-012 `case Ready: break;` 的意圖明確原則；不依賴 default 分支）。
- `ShowPlayerAtStart()`（**2026-09-04 修訂：A2**）：`SetVisible(true)` → `player.Reset()` → `blobAnimator.ResetVisual()` → **`blobAnimator.PlayAssemble()`**。**不再同幀 SetState(Playing)**——Assemble 在 Ready 態播放，`AssembleComplete`（state-query 偵測）→ GameManager handler → `SetState(Playing)`（見決策 11）。
- state-query 偵測 Dissasemble 播完（`normalizedTime >= 1`）→ fire `DissasembleComplete`（hook）＋內部啟動淡出（alpha→0）→ fire `OnDeathFadeComplete` → `EndGame()`。淡出啟動在 BlobAnimator 內部，GameManager 只訂閱 `OnDeathFadeComplete`。
- 淡出守衛改 **`IsPaused()`**（見決策 5 修訂）——「非暫停即可淡出」，Dying 態淡出正常執行（此為「維持 Playing 態」繞過的第二個隱藏依賴，BlobAnimator.cs:152）。
- **死亡流程期間進入 Dying 態**（取代原「維持 Playing 態」）；**水管照常捲動的 FR-17 裁定撤銷**——水管在死亡期間必須凍結（本功能核心）。

**dying 守衛漏洞逐一裁決（2026-09-30 翻轉）**：

| # | 漏洞 | 裁決 |
|---|------|------|
| H1 | `GameManager.Update` 地板/天花板檢查（`state==Playing` 時）在死亡流程期間若鳥死於邊界（y<=floorY 或 >=ceilingY）會每幀重觸發 `Die()` | **改**：`!isDying` 移除——`state == Playing` 已排除 Dying，檢查條件簡化（原「加 !isDying 防禦性」裁決隨旗標移除而撤銷） |
| H2 | P 鍵暫停——死亡流程中按 P 會 `Pause()`（state 仍 Playing） | **改**：死亡中按 P 不再誤觸暫停——Dying 不在 `state == Playing \|\| state == Paused`，P 鍵檢查自然排除（原「可接受」裁決推翻：實測 P 鍵卡死，見規格現況診斷 8） |
| H3 | ScoreTrigger 計分——死亡流程期間 state==Playing，`IsPlaying()` gate 不擋 | **維持可接受**：collider 停＋`scored` 旗標＋`IsPlaying()` gate 三重防護；Dying 態 `IsPlaying()==false` 使 gate 更嚴（不需守衛） |
| H4 | PipeSpawner 生成——死亡流程期間 `IsPlaying()==true` 持續生成 | **改**：死亡期間不再生成——`IsPlaying()` 嚴格化（Dying 時 false）後自然停（原「可接受」裁決推翻：實測持續生成，見規格現況診斷 4） |
| H5 | 死亡淡出（alpha→0）若以 Update/協程驅動，暫停時會繼續淡出 | **改**：淡出守衛從 `IsPlaying()` 改 `IsPaused()`——「非暫停即可淡出」；Dying 態淡出正常執行、Paused 態仍凍結（H5 原意「暫停時淡出凍結」保留） |
| H6 | 背景——Playing 態 `SetNormal` 持續捲動 | **改**：死亡期間凍結——`case Dying: SetFrozen(true)`（原「可接受」裁決推翻：實測背景照常捲動，見規格現況診斷 5） |

### 9. Bird 視覺替換（D9/FR-22~24）——ADR-007 相容

MeshFilter＋MeshRenderer → SpriteRenderer；sortingOrder 維持 1000。ADR-007 的 `GetComponentsInChildren<Renderer>().enabled` 對 SpriteRenderer 通用（SpriteRenderer 繼承 Renderer，ADR-007 已實證）——`SetVisible` 不需改動。場景 Bird z=-0.02（3D quad 防 z-fighting 用）可保留或歸零——SpriteRenderer 排序全由 sortingOrder 決定，z 不影響遮擋。

### 10. 資產引用（NFR-04）

Sprite 引用＝**texture guid＋sprite internalID**（meta 內 `spriteID` 對應 `internalID`，如 Jump `_0`＝spriteID `8f75dd1a012453a4386b83831881bdab`／internalID `745265774`）。.anim 剪輯 keyframe 以 `{fileID: <internalID>, guid: <texture guid>, type: 3}` 引用。動畫資產一律 Unity MCP 建，不可手寫 YAML（FR-01/NFR-05）。

### 11. Assemble 期間維持 Ready 態（A2，2026-09-04 動態驗證修訂）

**問題**：原流程 `ShowPlayerAtStart()` 同幀 `SetState(Playing)` → 背景 `SetNormal()` 立即捲動、PipeSpawner seed=0 第一幀生成水管——**Assemble（1.67s）未播完世界就開始動**，違反用戶核心要求「Assemble 播完遊戲就開始動」。

**決策（A2）**：

- `StartGame()` 倒帶完成 onComplete 改：`SetFrozen(true)` → `ShowPlayerAtStart()`（**不 SetState(Playing)**）。Assemble 在 Ready 態播放。
- GameManager 訂閱 `BlobAnimator.AssembleComplete` → handler：`if (state == Ready) SetState(Playing);`（防禦性守衛）。
- 背景：onComplete 內顯式 `SetFrozen(true)`（Assemble 期間凍結）；`SetState(Playing)` 的 `case Playing: SetNormal()` 不變（零耦合）。
- 水管：PipeSpawner **零改動**——Ready 期間 `IsPlaying()==false` 自然擋生成；`ResetAll()` seed=0 保留到進 Playing 第一幀。
- BlobAnimator speed：`(state == Playing || isAssembling) ? 1 : 0`（決策 5 修訂）——Ready＋Assemble 時 speed=1 播放。
- **FR-05（ADR-012）同幀原子放寬**：ShowPlayerAtStart 與 SetState(Playing) 不再同幀——但「中間無 gameplay 窗口」精神保留（間隙期間 `IsPlaying()==false`，無 gameplay 系統運行）。需 PM 追認。
- **Ready 語意擴張**：Ready 從「倒帶窗口」擴張為「倒帶＋Assemble 窗口」——鳥在 Ready 期間可見（Assemble 播放），但遊戲未開始。與 ADR-012「還沒看到東西就先觸發」的病灶精神一致。

**否決 A1**（SetState 的 `case Playing` 依賴 `IsAssembling` 條件凍結＋PipeSpawner 新增 `BeginSpawning()` API）：SetState 耦合動畫子狀態（分層違規）、更多移動件、PipeSpawner 需新 API。

## Consequences

- 正面：動畫時序（蓄力/上升/解體完成點）由 code 定義（normalizedTime 常數），**可程式驗證**（B2）；不再依賴 Animation Event 的脆弱觸發。
- 正面：**Dying 態取代 dying 旗標**（2026-09-30）——死亡瞬間水管/生成/背景全凍結（`IsPlaying()` 嚴格化＋`case Dying: SetFrozen(true)`），凍結一致性（ADR-006 D3）在死亡期間自然生效；狀態機 6 態，ADR-006/011 凍結政策與查詢/命令分離不破壞。
- 正面：ADR-007 的 Renderer.enabled 隱藏機制對 SpriteRenderer 通用，Menu 隱藏鳥零改動。
- 正面：A2 使「Assemble 播完遊戲才開始」成為狀態機語意（Ready 期間遊戲未開始），PipeSpawner 零改動。
- 負面：OnStateChanged 訂閱者從 1 增為 2（UI＋BlobAnimator），GA4 演進條件觸發但裁決不升級（見決策 6）。
- 負面：Animation Event 從核心時序退役——「時序留在資產」的原始論點部分放棄（美術幀序仍在資產，觸發時點在 code）。未來輔助 hook 需用新事件名。
- 負面：`OnLandComplete` 目前無消費者（FR-19 列為事件但無 FR 引用）——保留為 hook（未來落地音效/擠壓動畫用），非缺陷。
- 負面：BlobAnimator 事件名與原回呼方法名不同（C# 事件 `JumpRise` 等）——CS0102 強制，命名規則已記錄（決策 4），避免混淆。
- 負面：輸入 gate 依賴 BlobAnimator 內部旗標（isCharging/isAssembling）——旗標生命週期須與 ResetVisual 同步（Restart 清旗標），否則上一局殘留會誤擋新局輸入。
- 負面：animator.speed 由 Update 每幀求值（B1 修復）——每幀一次 `animator.speed` 寫入（熱路徑極小成本，可接受）；換得「與事件 fire 時機解耦」的健壯性。
- 負面：Dissasemble 拼寫與美術資產（Blue-Blob-Dissasemble.png）一致，非標準英文拼寫（正確為 Disassemble）——域語言優先，維持規格拼寫。
- 被放棄的替代方案：
  - **純 C# frame callback 驅動動畫**（否決）：時序知識（幀率/幀數/採樣率）需在 C# 重複，與資產雙重事實源；美術調整幀序需改 code。state-query 勝出（讀 AnimatorStateInfo 不需知道幀率）。
  - **Animation Event 續用（B1）**（否決）：根因未 100% 定位（`animator.Play` 同態重入不可靠），且 Animation Event 有 culling/component lookup/時序邊界等已知坑；關鍵時序不該依賴脆弱機制。
  - **輪詢 IsPlaying() 控 animator.speed**（否決）：BlobAnimator 是狀態轉變驅動（狀態變才需改 speed），輪詢每幀寫 `animator.speed` 是冗餘寫入；事件訂閱符合「狀態變更通知」語意，且不新增查詢 seam 使用者。
  - **維持 isDying 旗標**（否決，2026-09-30 翻轉）：原裁決「新增 GameState.Dying（否決）——狀態機 5 態已涵蓋；『死亡流程』是 Playing 內部的短暫子階段，用旗標（dying）表達更貼切」已被現實推翻——旗標僅 GameManager 內部自我參考、**從未被廣播**，水管/生成/背景三系統看不到它，死亡期間照常移動（人類實測 bug）；且死亡流程（Dissasemble 1.25s＋淡出）是玩家可見的明確相位，非「短暫子階段」。新增態的成本（SetState switch、UI switch、凍結政策）全部可控制且已有明確改法。
  - **A1（SetState case Playing 依賴 IsAssembling＋PipeSpawner.BeginSpawning）**（否決）：SetState 耦合動畫子狀態（分層違規）、更多移動件、PipeSpawner 需新 API。A2 勝出（見決策 11）。

## 對 pm 規格的修訂建議

- **FR-16 撤銷（2026-09-30）**：死亡流程不再維持 Playing 態，改 **Dying 態**——`Die()` 由 `isDying = true` 改 `SetState(Dying)`；`isDying` 欄位與全部使用點移除。**需 PM 追認**（dying-state 規格已含此修訂）。
- **FR-17 撤銷（2026-09-30）**：水管照常捲動的裁定撤銷——水管在死亡期間**必須凍結**（本功能核心）。**需 PM 追認**。
- **FR-14 補完**：animator.speed 映射應為 `(state == Playing || state == Dying || isAssembling) ? 1 : 0`（含 Menu/Ready 非 Assemble→0、Ready＋Assemble→1、Dying→1）。規格文字「Paused/GameOver 時 animator.speed=0、Playing 時 1」未提 Menu/Ready 與 Assemble 期間。
- **FR-05（規格）Assemble 期間世界凍結**：規格 FR-05 只說「Assemble 播完停最後一幀 → 鳥開始下墜」——動態驗證發現背景/水管在 Assemble 期間已動。A2 修復：Assemble 期間維持 Ready 態（背景凍結、水管不生成）。**需 PM 追認**「Assemble 播完遊戲才開始」為正式需求語意。
- **FR-05（ADR-012）同幀原子放寬**：ShowPlayerAtStart 與 SetState(Playing) 不再同幀（改 AssembleComplete 事件驅動）——「中間無 gameplay 窗口」精神保留。**需 PM 追認**。
- **FR-07 事件機制**：規格 FR-02 明列「橋樑層：Animation Event」——動態驗證推翻，核心時序改 AnimatorStateInfo 查詢（B2）。**需 PM 追認**規格 FR-02 的橋樑層描述修訂。
- **OnLandComplete**：FR-19 列為事件但無消費者——保留為 hook，Validator 不應以「無消費者」為缺陷。
- **FR-19 事件名調整（CS0102）**：規格列「事件 OnJumpRise、OnAssembleComplete、OnLandComplete、OnDissasembleComplete」——C# 禁止 method 與 event 同名。實作：C# 事件去 "On" 前綴（`JumpRise` 等）。規格事件名為概念性描述，實作以本 ADR 決策 4 為準。
- 其餘 D1-D9、FR-01~24、BC-1~9、NFR-01~05 經實證全部正確，無需修訂。