# Validation Report: blob-animator round 1
- **Date**: 2026-09-05
- **Task Sheet**: specs/requirements/blob-animator.md（FR-01~24、NFR、T1-T6）
- **Architecture**: specs/architecture/animator-system.md（最終真相，含 A2/B2 修訂）＋ docs/adr/013-blob-animator.md（含決策 11 A2）
- **Verdict**: **FAIL**——1 blocker（B1：Assemble 在 Ready 態播放時 animator.speed=0，遊戲卡死 Ready）。編譯零錯誤已驗證（Sisyphus bridge 實證）；人類實測回饋待補。

## 審查範圍

正式驗收 round-1，全量審查 T1-T5：
- `Assets/Scripts/BlobAnimator.cs`（T1＋B2 修復）
- `Assets/Scripts/PlayerController.cs`（T2）
- `Assets/Scripts/GameManager.cs`（T3＋A2 修復）
- `Assets/Art/Blob Character Pack/Animations/`（4 .anim＋1 .controller，T5）
- `Assets/Scenes/SampleScene.unity`（T4 場景）
- 對照：`Assets/Scripts/UI/UIController.cs`（Ready case）、`Assets/Scripts/GameStateAuthority.cs`、`Assets/Scripts/CollisionHandler.cs`（不變）

## 逐項結果（對照最新契約）

### T1 — BlobAnimator.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| state-query 核心時序（B2）：Jump≥0.5、其他≥1.0、once 旗標 | ✅ | :83-90，Jump `normalizedTime>=0.5f`、Assemble/Land/Dissasemble `>=1f`、once 旗標防重複 |
| 命令 `Play(name, 0, 0f)`＋重置 once 旗標 | ✅ | :96/:103/:111/:118/:124 全 `Play(name, 0, 0f)`；PlayAssemble→assembleCompleteFired=false、PlayJump→jumpRiseFired=false、PlayLand→landCompleteFired=false、PlayDissasemble→dissasembleCompleteFired=false |
| speed 公式 `(state == Playing \|\| isAssembling) ? 1 : 0`（A2） | ⚠️ **公式正確但求值時機有漏洞** | :134 公式與契約一致，但只在 OnStateChanged fire 時求值——見 B1 |
| gate 旗標（isCharging/isAssembling）＋PlayJump/PlayLand 開頭 gate | ✅ | :108/:116；PlayJump→isCharging=true（:109）、JumpRise fire→false（:84）；PlayAssemble→isAssembling=true（:101）、AssembleComplete fire→false（:86） |
| 5 事件（JumpRise/AssembleComplete/LandComplete/DissasembleComplete/OnDeathFadeComplete） | ✅ | :24-32，命名規則（去 On 前綴）符合 ADR-013 決策 4 |
| FadeOut IsPlaying gate（H5） | ✅ | :142 `if (!GameStateAuthority.IsPlaying()) return;` |
| ResetVisual：恢復 alpha=1＋清 gate/once 旗標 | ✅ | :159-175，清 isFadingOut/fadeCompleteFired/isCharging/isAssembling＋4 once 旗標＋alpha=1 |
| **無 Animation Event 回呼方法殘留（AN3 修訂）** | ✅ | grep 全 Assets：BlobAnimator.cs 無 OnJumpRise/OnAssembleComplete/OnLandComplete/OnDissasembleComplete 方法（僅 PlayerController:89 的 OnJumpRise 訂閱方法） |

### T2 — PlayerController.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| HandleInput→PlayJump（不直接給衝力） | ✅ | :84 `blobAnimator.PlayJump()`；全檔無 Flap()、無輸入路徑直接設衝力 |
| JumpRise 訂閱→設 flapForce | ✅ | :45 訂閱、:89-93 OnJumpRise 設 `currentVelocity = flapForce` |
| isLanding 防重複 PlayLand | ✅ | :119-123，`currentVelocity < 0 && !isLanding`；OnJumpRise 重置 isLanding=false（:91） |
| AN13 HandleGravity IsAssembling gate | ✅ | :99 `if (blobAnimator != null && blobAnimator.IsAssembling) return;` |

### T3 — GameManager.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| A2：StartGame onComplete 改 SetFrozen(true)+ShowPlayerAtStart（不 SetState(Playing)） | ✅ | :143-146，onComplete 內 SetFrozen(true)→ShowPlayerAtStart()，無 SetState(Playing) |
| HandleAssembleComplete：`if (state == Ready) SetState(Playing)` | ✅ | :72-75 防禦性守衛 |
| Die() re-entry guard＋PlayDissasemble＋停 collider | ✅ | :208-210 `if (state != Playing) return; if (isDying) return;`＋:214 collider 停用＋:215 PlayDissasemble |
| OnDeathFadeComplete 訂閱→EndGame（不訂閱 DissasembleComplete） | ✅ | :50 訂閱 OnDeathFadeComplete；grep 證實無 DissasembleComplete 訂閱 |
| H1 !isDying（Update 地板/天花板） | ✅ | :89 `if (state == GameState.Playing && !isDying)` |
| ShowPlayerAtStart：ResetVisual+PlayAssemble | ✅ | :199-200 |
| isDying 重設（StartGame） | ✅ | :138 `isDying = false;` |

### T4 — 場景（SampleScene.unity）

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| Bird 有 SpriteRenderer（無 MeshFilter/MeshRenderer） | ✅ | Bird（fileID 1181836431）8 元件：Transform/PlayerController/Rigidbody2D/CollisionHandler/BoxCollider2D/BlobAnimator/SpriteRenderer/Animator；場景 MeshFilter/MeshRenderer 僅屬 Ceiling (1)/(2)（既有物件） |
| Animator＋BlobAnimator 掛載 | ✅ | Animator（1181836442）m_Controller guid 9cfed30cee548684ab4d9e877c9354f0＝BlobAnimator.controller.meta ✅；BlobAnimator（1181836440）animator/spriteRenderer/gameManager 全接線 |
| sortingOrder 1000 | ✅ | :2386 `m_SortingOrder: 1000` |
| collider 0.64×0.32（2:1） | ✅ | :2321 `m_Size: {x: 0.64, y: 0.32}` |
| 接線完整 | ✅ | GameManager（321862628）→player/pipeSpawner/backgroundManager/blobAnimator 全接線；PlayerController→blobAnimator 接線；BlobAnimator→gameManager 接線 |

### T5 — 動畫資產

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| 4 .anim 幀數 20/15/6/5 | ✅ | Assemble 20 keyframes、Dissasemble 15、Jump 6、Land 5 |
| 零 Animation Event（B2） | ✅ | 4 .anim 全 `m_Events: []` |
| 不 loop（FR-13） | ✅ | 4 .anim 全 `m_LoopTime: 0` |
| 採樣率一致（假設 C） | ✅ | 4 .anim 全 `m_SampleRate: 12` |
| controller 5 態 default Hidden | ✅ | 5 態 {Hidden, Assemble, Land, Jump, Dissasemble}，無 Idle/Run；`m_DefaultState`＝Hidden（234737003346821818）；全 `m_Transitions: []`（無時間條件/自動轉換）；`m_AnimatorParameters: []` |
| controller→.anim GUID 引用 | ✅ | Jump→4ea0b65b…（Jump.anim.meta）、Dissasemble→f2fed5cb…、Land→fe33aabb…、Assemble→1534054c…、Hidden→{fileID: 0} |
| .anim→sprite GUID 引用（NFR-04） | ✅ | Assemble→085c3339…（Blue-Blob-Assemble.png）、Jump→d81ff205…（Blue-Blob-Jump.png）、Land→d64ccfc8…（Blue-Blob-Land.png）、Dissasemble→5812d868…（Blue-Blob-Dissasemble.png）；internalID 與 meta 一致（Jump _0＝745265774 對 ADR-013 例） |

### 跨檔案一致性

| 檢查 | 結果 | 證據 |
|------|------|------|
| 事件名對齊 | ✅ | PlayerController:45 訂 JumpRise；GameManager:50-51 訂 OnDeathFadeComplete/AssembleComplete |
| 命令呼叫對齊 | ✅ | PlayJump（PC:84）、PlayLand（PC:122）、PlayAssemble（GM:200）、PlayDissasemble（GM:215） |
| 無殘留舊 Flap 語義 | ✅ | 全 Assets grep 無 Flap() 殘留 |
| CollisionHandler 不變 | ✅ | 仍 `gameManager.Die()`，符合 NFR-01 |
| UIController Ready case | ✅ | :145-152 Ready 併入 Playing（關 menu/overlay、保留 HUD） |
| pre-review M1（DissasembleComplete fire 對外事件） | ✅ 已解決 | Tech Lead 澄清為文件錯誤；animator-system.md:137 已更新「fire DissasembleComplete（hook）＋內部啟動 FadeOut()」；ADR-013 決策 4 :48 同——實作符合更新後契約 |
| pre-review m2（ShowPlayerAtStart 未收 SetState(Playing)） | ✅ 已解決 | A2 修訂後契約即「不 SetState(Playing)」（animator-system.md:63-64），實作符合 |

## Blockers

### B1: Assemble 在 Ready 態播放時 animator.speed=0——A2 修復的時序漏洞，遊戲卡死 Ready
- type: code | severity: blocker
- 依據: FR-14「Playing 時 1」＋animator-system.md:152-153「A2 配套：Ready＋Assemble 時 speed=1（Assemble 播放）」＋FR-05「Assemble 播完停最後一幀 → 鳥開始下墜」＋FR-08「Dissasemble 播完 → 淡出 → EndGame」的對偶（Assemble 播完 → SetState(Playing)）
- 事實: `animator.speed` 唯一寫入點是 `BlobAnimator.cs:134`（`HandleGameStateChanged`），**只在 `GameManager.OnStateChanged` fire 時求值**。A2 流程：
  1. `GameManager.StartGame():140` `SetState(Ready)` → fire `OnStateChanged(Ready)` → `HandleGameStateChanged(Ready)`：`(Ready==Playing || isAssembling) ? 1 : 0`——此時 `isAssembling=false`（ResetVisual 已清／初始 false）→ **speed=0**
  2. 背景倒帶完成 → onComplete（:143-146）→ `ShowPlayerAtStart():193-202` → `ResetVisual()`（isAssembling=false）→ `PlayAssemble()`（`BlobAnimator.cs:98-104`：isAssembling=true、`animator.Play("Assemble", 0, 0f)`）——**PlayAssemble 不重新求值 speed**
  3. 此後直到 AssembleComplete 前**無任何 OnStateChanged fire**（StartGame 流程中 SetState 僅 :140 一次）→ **speed 維持 0**
  4. Assemble 動畫凍結在第一幀，`normalizedTime` 停在 0 → `UpdateAnimationStateSignals():85-86` 的 `info.IsName("Assemble") && info.normalizedTime >= 1f` 永不滿足 → `AssembleComplete` 永不 fire → `HandleAssembleComplete():72-75` 永不執行 → **`SetState(Playing)` 永不發生 → 遊戲卡死在 Ready 態**（鳥顯示 Assemble 第一幀、世界凍結、Start 重入被擋）
- 佐證: `GameManager.cs:200` 註解「Ready 末段（speed=0）切態停第一幀」直接承認 speed=0 狀態——與契約 A2 配套（Ready＋Assemble 時 speed=1）矛盾，且「停第一幀」正是卡死根因。
- 建議方向: （僅供路由參考）PlayAssemble 內設 `animator.speed = 1f`（或 ShowPlayerAtStart 內），或將 speed 求值移至 Update 每幀執行（`animator.speed = (GameStateAuthority.IsPlaying() || isAssembling) ? 1f : 0f`）——後者同時消除「OnStateChanged 求值時機」脆弱性。

## Majors

無。

## Minors

### m1: GameManager.Die() 對 blobAnimator.PlayDissasemble() 無 null guard
- type: code | severity: minor
- 依據: 一致性（同檔 ShowPlayerAtStart:197、OnEnable:46 皆有 `if (blobAnimator != null)` guard）
- 事實: `GameManager.cs:215` `blobAnimator.PlayDissasemble();` 無 null 檢查。場景已接線（1181836440）＋OnEnable fallback 存在，非現行缺陷。pre-review m1 維持。

### m2: PlayHidden() 命令全碼庫無呼叫點（死命令）
- type: code | severity: minor
- 依據: FR-19 列 PlayHidden 為命令；animator-system.md:15 列為命令
- 事實: `BlobAnimator.cs:96` PlayHidden 定義但 grep 全 Assets 無呼叫。因 .controller 預設態＝Hidden，Menu/Ready 鳥隱藏靠 SetVisible(false)＋speed=0，流程不需顯式回 Hidden。pre-review m3 維持。

### m3: FadeOut() 對 spriteRenderer 無 null guard
- type: code | severity: minor
- 依據: 防禦性一致性（Awake:49-50 有 GetComponent fallback，但 GetComponent 找不到時仍為 null）
- 事實: `BlobAnimator.cs:144` `Color color = spriteRenderer.color;` 無 null 檢查。場景已接線（1181836441），Awake fallback 存在，非現行缺陷。

### m4: PlayerController 殘留旋轉死欄位（HandleRotation 已註解）
- type: code | severity: minor
- 依據: 程式碼整潔
- 事實: `PlayerController.cs:15-17` maxUpAngle/maxDownAngle/rotationSpeed 序列化欄位＋:126-145 HandleRotation 方法被註解（:74 `// HandleRotation(); //改用spritesheet animation 不再旋轉`）；場景 :2219-2221 同欄位殘留。spritesheet 動畫取代旋轉後為死碼。

## 動態驗證清單（無法靜態驗證者；人類實測回饋待補）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode 按 Start，觀察 Assemble 播放 | **B1 驗證**：鳥播 Assemble（20 幀）動畫，播完（約 1.67s）後世界才開始動（背景捲動、水管生成、鳥開始下墜）；**若鳥卡在 Assemble 第一幀不動、世界永不開始＝B1 成立** | 待填 |
| D2 | Assemble 播放期間按 jump | 無視輸入（isAssembling gate）；鳥靜止不墜落（AN13） | 待填 |
| D3 | Assemble 播完後按 jump 一次 | 鳥播 Jump 012 蓄力（不立即上升），012 播完（約 0.25s）才上升（JumpRise） | 待填 |
| D4 | 012 蓄力期間連點 jump | 不重播 012、不跳躍（isCharging gate，BC-4） | 待填 |
| D5 | 345 上升期間按 jump | 回 012 重播（FR-07/FR-12，BC-5） | 待填 |
| D6 | 鳥撞水管/地板死亡 | Die→Dissasemble 播放→淡出（alpha→0）→才 GameOver UI；期間水管持續捲動、維持 Playing（FR-15~17） | 待填 |
| D7 | 死亡流程中按 P 暫停 | 動畫（含淡出）凍結；Resume 續播（H5/BC-3） | 待填 |
| D8 | 死亡淡出完成後 Restart | 鳥恢復 alpha=1 不隱形；新局輸入不被上一局殘留 gate 旗標誤擋（ResetVisual） | 待填 |
| D9 | 暫停/GameOver 時動畫凍結、Playing 恢復 | animator.speed 接線正確（含 Menu/Ready→0、Ready＋Assemble→1） | 待填 |
| D10 | 剪輯播完停最後一幀 | 不 loop（FR-13，BC-7） | 待填 |

## 編譯驗證

- 編譯零錯誤已驗證（Sisyphus bridge 實證，2026-09-05）——本報告不重複執行。

## Statistics
- blocker: 1 / major: 0 / minor: 4

## 備註
- pre-review 的 M1（architecture/major）已由 Tech Lead 澄清為文件錯誤非實作缺陷，契約已更新（animator-system.md:137、ADR-013 決策 4 :48），實作符合更新後契約，本輪不列。
- pre-review 的 m2（ShowPlayerAtStart 未收 SetState(Playing)）已被 A2 修訂自然解決（契約改為「不 SetState(Playing)」），本輪不列。
- 人類實測回饋待補：D1-D10 回填＋B1 修復後方可關單。