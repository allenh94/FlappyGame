# Validation Report: blob-animator（T1-T3 純 C# 靜態預審）
- **Date**: 2026-09-04
- **Task Sheet**: specs/requirements/blob-animator.md（FR-01~24、NFR、T1-T3）
- **Architecture**: specs/architecture/animator-system.md（最終真相）＋ docs/adr/013-blob-animator.md
- **Verdict**: **PASS（暫定）**——無 blocker；1 major / 3 minor。編譯與動態驗證待 MCP 恢復後補，回填前為暫定。

## 審查範圍

本次為**正式驗收前的靜態預審**，僅審 T1-T3 純 C# 部分（BlobAnimator.cs / PlayerController.cs / GameManager.cs）與契約（規格＋架構 doc＋ADR-013）的一致性。**不包含**：編譯驗證（Unity MCP 未連線）、動態行為（Animation Event 掛幀觸發、播放時序）、T4 場景改造、T5 動畫資產。這些列入「動態驗證清單」與「編譯驗證待補」節。

審查對象：
- `Assets/Scripts/BlobAnimator.cs`（T1，新建）
- `Assets/Scripts/PlayerController.cs`（T2，改造）
- `Assets/Scripts/GameManager.cs`（T3，改造）
- 對照：`Assets/Scripts/GameStateAuthority.cs`、`Assets/Scripts/CollisionHandler.cs`（不變）

## 逐項結果（對照契約）

### T1 — BlobAnimator.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| 命令 PlayHidden/PlayAssemble/PlayJump/PlayLand/PlayDissasemble | ✅ | :74/:76/:83/:90/:96，狀態名與契約一致 |
| AN12 PlayJump 開頭 gate `if (isCharging \|\| isAssembling) return;` | ✅ | :85 |
| AN12 PlayLand 開頭 gate `if (isAssembling \|\| isCharging) return;` | ✅ | :92 |
| gate 旗標生命週期（PlayJump→isCharging=true、OnJumpRise→false；PlayAssemble→isAssembling=true、OnAssembleComplete→false） | ✅ | :86/:102、:79/:108 |
| AN11 事件命名（衝突去 On 前綴） | ✅ | JumpRise/AssembleComplete/LandComplete/DissasembleComplete（去 On）；OnDeathFadeComplete（無衝突保留）:24-32 |
| Animation Event 回呼方法名與契約掛幀一致 | ✅ | OnJumpRise/OnAssembleComplete/OnLandComplete/OnDissasembleComplete :100/:106/:112/:117，與 animator-system.md:126-129 文字一致 |
| AN5 暫停接線 `speed = (state==Playing)?1:0`（含 Menu/Ready→0） | ✅ | :131，`(state == GameManager.GameState.Playing) ? 1f : 0f` |
| AN6 死亡淡出：IsPlaying() gate、OnDissasembleComplete 內部啟動 | ✅ | FadeOut :139 `if (!GameStateAuthority.IsPlaying()) return;`；OnDissasembleComplete :121 設 isFadingOut=true |
| ResetVisual：清旗標＋恢復 alpha | ✅ | :158-167 清 isFadingOut/fadeCompleteFired/isCharging/isAssembling＋alpha=1 |
| IsAssembling 查詢 | ✅ | :40 `public bool IsAssembling => isAssembling;` |

### T2 — PlayerController.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| HandleInput → PlayJump()（不直接給衝力，Flap 零殘留） | ✅ | :84 `blobAnimator.PlayJump()`；全檔無 Flap()、無 `currentVelocity = flapForce` 於輸入路徑 |
| 訂閱 JumpRise → 設 flapForce | ✅ | :45 訂閱、:89-93 OnJumpRise 設 `currentVelocity = flapForce` |
| 速度<0 且非 dying → PlayLand（isLanding 防重複） | ✅ | :116-124，`if (isDead) return;`（非 dying gate）＋`if (currentVelocity < 0 && !isLanding)` |
| AN13 HandleGravity 開頭 IsAssembling gate | ✅ | :99 `if (blobAnimator != null && blobAnimator.IsAssembling) return;` |

### T3 — GameManager.cs

| 契約要求 | 結果 | 證據 |
|---------|------|------|
| Die()：re-entry guard（isDying）＋PlayDissasemble＋停 collider | ✅ | :192-199，`if (isDying) return;`＋`player.Die()`＋`bodyCollider.enabled=false`＋`blobAnimator.PlayDissasemble()` |
| 訂閱 OnDeathFadeComplete → EndGame（不訂閱 DissasembleComplete） | ✅ | :49 訂閱 OnDeathFadeComplete；grep 證實無 DissasembleComplete 訂閱 |
| H1 守衛：Update 地板/天花板檢查加 !isDying | ✅ | :76 `if (state == GameState.Playing && !isDying)` |
| ShowPlayerAtStart：ResetVisual＋PlayAssemble（缺口 1 修復） | ✅ | :183-184 |
| isDying 重設（StartGame） | ✅ | :125 `isDying = false;` |

### 跨檔案一致性

| 檢查 | 結果 | 證據 |
|------|------|------|
| 事件名對齊（JumpRise/OnDeathFadeComplete） | ✅ | PlayerController:45 訂 JumpRise；GameManager:49 訂 OnDeathFadeComplete |
| 命令呼叫對齊 | ✅ | PlayJump（PC:84）、PlayLand（PC:122）、PlayAssemble（GM:184）、PlayDissasemble（GM:199） |
| 無殘留舊 Flap 語義 | ✅ | 全 Assets grep 無 Flap() 殘留；輸入路徑無直接衝力 |
| CollisionHandler 不變 | ✅ | 仍 `gameManager.Die()`（:12），符合 NFR-01 |

## Blockers

無。

## Majors

### M1: OnDissasembleComplete 回呼 fire 了 DissasembleComplete 事件，與架構契約「不 fire 對外事件」矛盾
- type: architecture | severity: major
- 依據: animator-system.md:129「`OnDissasembleComplete()`（Dissasemble 末端）——**內部啟動 FadeOut()**（不 fire 對外事件）」；ADR-013 決策 4「淡出由 OnDissasembleComplete() 回呼內部啟動——GameManager 不訂閱 DissasembleComplete」
- 事實: `BlobAnimator.cs:119` `DissasembleComplete?.Invoke();` 在 OnDissasembleComplete 內 fire 對外事件，與契約「不 fire 對外事件」字面矛盾。grep 證實 DissasembleComplete 零訂閱者（GameManager 只訂 OnDeathFadeComplete），故**零受害者**——功能無害，但違背明確契約文字。
- 附註: 架構 doc 自身有內部張力——:129 說「不 fire 對外事件」，:136 卻列 DissasembleComplete 為「保留 hook（目前無消費者）」。hook 若要可被訂閱，理應被 fire。此矛盾需 Tech Lead 裁定：要嘛改 :129 為「fire DissasembleComplete 作 hook，但不作為淡出啟動信號」，要嘛移除 :119 的 fire。
- 建議方向: 交 Tech Lead 澄清契約（非修法）。

## Minors

### m1: GameManager.Die() 對 blobAnimator.PlayDissasemble() 無 null guard，與其他呼叫點不一致
- type: code | severity: minor
- 依據: 一致性（同檔 ShowPlayerAtStart:181、OnEnable:46 皆有 `if (blobAnimator != null)` guard）
- 事實: `GameManager.cs:199` `blobAnimator.PlayDissasemble();` 無 null 檢查。T4 未接線時（現況）blobAnimator 為 null → Die() 會 NRE。因 T4 為本 feature 後續任務、且 OnEnable fallback 已存在，屬潛在風險非現行缺陷。建議與其他呼叫點一致補 guard。

### m2: ShowPlayerAtStart() 未將 SetState(Playing) 收在方法內，與架構 doc 方法契約結構不符
- type: code | severity: minor
- 依據: animator-system.md:185-190 顯示 ShowPlayerAtStart 以 `SetState(Playing)` 收尾
- 事實: `GameManager.cs:177-186` ShowPlayerAtStart 只做 SetVisible→Reset→ResetVisual→PlayAssemble；`SetState(Playing)` 在呼叫端 `ScrollBackToStart` callback（:130）內、ShowPlayerAtStart 之後。功能等價（同 callback 同幀原子，PlayAssemble 先於 SetState(Playing) 次序正確），僅方法契約結構與 doc 記載不符。文件漂移風險。

### m3: PlayHidden() 命令全碼庫無呼叫點（死命令）
- type: code | severity: minor
- 依據: FR-19 列 PlayHidden 為命令；animator-system.md:15 列為命令
- 事實: `BlobAnimator.cs:74` PlayHidden 定義但 grep 全 Assets 無呼叫。因 .controller 預設態＝Hidden（ADR-013 決策 2），Menu/Ready 鳥隱藏靠 SetVisible(false)＋speed=0，流程不需顯式回 Hidden，故無功能缺口。屬介面完整性命令，觀察性註記。

## 動態驗證清單（無法靜態驗證者）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode 進 Playing，按 jump 一次 | 鳥播 Jump 012 蓄力（不立即上升），012 播完（frame3 起點）才上升——驗證 OnJumpRise 掛幀觸發（假設 B） | 待填 |
| D2 | 012 蓄力期間連點 jump | 不重播 012、不跳躍（isCharging gate） | 待填 |
| D3 | Assemble 播放期間按 jump | 無視輸入（isAssembling gate）；鳥靜止不墜落（AN13） | 待填 |
| D4 | 345 上升期間按 jump | 回 012 重播（FR-07/FR-12） | 待填 |
| D5 | 鳥撞水管死亡 | Die→Dissasemble 播放→淡出（alpha→0）→才 GameOver UI；期間水管持續捲動、維持 Playing | 待填 |
| D6 | 死亡流程中按 P 暫停 | 動畫（含淡出）凍結；Resume 續播（H5/BC-3） | 待填 |
| D7 | 死亡淡出完成後 Restart | 鳥恢復 alpha=1 不隱形；新局輸入不被上一局殘留 gate 旗標誤擋（ResetVisual） | 待填 |
| D8 | 暫停/GameOver 時動畫凍結、Playing 恢復 | animator.speed 接線正確（含 Menu/Ready→0） | 待填 |
| D9 | 剪輯播完停最後一幀 | 不 loop（FR-13） | 待填 |

## 編譯驗證待補（MCP 恢復後）

- `System.Action`（BlobAnimator:24-32 等）——既有 house style（UIController/ScoreSession/BGManager/BGLayer 皆用），非本次引入，但編譯未驗證。
- `FindFirstObjectByType<GameManager>()`（BlobAnimator:55、GameManager:47）——Unity 2023.1+ API，比對 UIController 模式，編譯未驗證。
- `rb.linearVelocity`（PlayerController:111/:150/:160）——Unity 2023.1+ 更名 API，編譯未驗證。
- `Physics2D.gravity`（PlayerController:102）——2D 物理 API，編譯未驗證。
- 全部 C# 檔編譯（CS 錯誤）——本 session 無 unityMCP，未驗證。

## Statistics
- blocker: 0 / major: 1 / minor: 3

## 備註
- 規格 FR-19 事件名（OnJumpRise 等）與實作（JumpRise 等）差異為 ADR-013 決策 4 已裁決的 CS0102 修正（規格事件名為概念性描述），非缺陷。
- 架構 doc section 0/3 與 section 4 的「淡出啟動位置」不一致（gameplay-developer 記憶已記載）——現行 doc 已更新為「內部啟動」，實作依此，無問題。
- 本預審 Verdict 為**暫定**：動態清單 D1-D9 回填＋編譯驗證通過後方可關單。