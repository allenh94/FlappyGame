# Animator 動畫系統架構（blob-animator）

> 本文件由 tech-lead-agent 維護（Phase 2 產出），其他 agent 唯讀。
> 需求真相來源：`specs/requirements/blob-animator.md`。本文引用其條目編號作為裁定依據。
> 決策紀錄：`docs/adr/013-blob-animator.md`。

## 0. 模組總覽（Class Diagram）

```
┌─ BlobAnimator（Assets/Scripts/BlobAnimator.cs，掛 Bird，控制層）──────┐
│  [SerializeField] Animator animator;                                  │
│  [SerializeField] SpriteRenderer spriteRenderer;  // 死亡淡出目標      │
│                                                                       │
│  命令（對應 .controller 狀態轉換，全部 animator.Play(name, 0, 0f)）：   │
│    PlayHidden() / PlayAssemble() / PlayJump() / PlayLand() /          │
│    PlayDissasemble()                                                  │
│  C# 事件（程式訂閱面）：                                               │
│    JumpRise / AssembleComplete / LandComplete / DissasembleComplete / │
│    OnDeathFadeComplete                                                │
│  state-query（B2，核心時序，Update 內讀 AnimatorStateInfo）：           │
│    Jump 態 normalizedTime>=0.5 → fire JumpRise（once 旗標）            │
│    Assemble 態 >=1 → fire AssembleComplete                            │
│    Land 態 >=1 → fire LandComplete                                    │
│    Dissasemble 態 >=1 → fire DissasembleComplete＋內部啟動 FadeOut()    │
│  暫停：訂閱 GameManager.OnStateChanged 存 lastState 快照；             │
│    Update 每幀求值 animator.speed =                                    │
│    (lastState == Playing \|\| lastState == Dying \|\| isAssembling) ? 1 : 0│
│    （B1 修復；Dying 接受＝2026-09-30，Dissasemble 在 Dying 態播放）      │
│  死亡淡出：state-query 偵測 Dissasemble 播完 → FadeOut() →             │
│    fire OnDeathFadeComplete（FadeOut 自查 IsPaused()，暫停時凍結）      │
│  ResetVisual()：恢復 alpha=1、取消進行中淡出＋清 gate/once 旗標         │
│    （ShowPlayerAtStart 呼叫）                                          │
│  輸入 gate 旗標：isCharging（PlayJump→true、JumpRise fire→false）＋    │
│    isAssembling（PlayAssemble→true、AssembleComplete fire→false）      │
│    PlayJump 開頭 if (isCharging || isAssembling) return;               │
│    PlayLand 開頭 if (isAssembling || isCharging) return;               │
│  IsAssembling 查詢：public bool IsAssembling { get; private set; }     │
│    （PlayerController.HandleGravity 開頭 if (IsAssembling) return;）    │
└──────────────────────────────────────────────────────────────────────┘
   │ 訂閱（事件，非輪詢）
   ├─▶ GameManager.OnStateChanged（animator.speed 控制）
   │ 查詢（淡出 gate）
   ├─▶ GameStateAuthority.IsPaused()（2026-09-30：原 IsPlaying() 改）
   │ 查詢（核心時序，B2）
   ├─▶ Animator.GetCurrentAnimatorStateInfo(0)（state＋normalizedTime）
   │ 命令（程式→動畫）
   └─▶ Animator（.controller 狀態機）

┌─ PlayerController（物理層，T2 改造）──────────────────────────────────┐
│  · 移除按鍵當下直接設衝力（Flap() 不再 currentVelocity = flapForce）    │
│  · 輸入 → BlobAnimator.PlayJump()（012 蓄力；gate 在 BlobAnimator 內） │
│  · 訂閱 BlobAnimator.JumpRise → currentVelocity = flapForce            │
│  · HandleGravity 開頭 if (blobAnimator.IsAssembling) return;           │
│    （Assemble 播放期間鳥靜止，播完才下墜）                              │
│  · 速度 <0 且非 dying → BlobAnimator.PlayLand()                        │
└──────────────────────────────────────────────────────────────────────┘

┌─ GameManager（死亡編排，T3 改造）─────────────────────────────────────┐
│  · Die()：if Playing → SetState(Dying) → player.Die() →               │
│      blobAnimator.PlayDissasemble()（2026-09-30：Dying 態取代旗標）     │
│  · 訂閱 BlobAnimator.OnDeathFadeComplete → EndGame()                  │
│    （淡出由 BlobAnimator 內部啟動，不訂閱 DissasembleComplete）         │
│  · 訂閱 BlobAnimator.AssembleComplete → if (state==Ready)             │
│      SetState(Playing)（A2：Assemble 播完遊戲才開始）                   │
│  · StartGame onComplete：SetFrozen(true) → ShowPlayerAtStart()        │
│    （不 SetState(Playing)——Assemble 在 Ready 態播）                    │
│  · ShowPlayerAtStart()：SetVisible(true) → player.Reset() →           │
│      blobAnimator.ResetVisual() → blobAnimator.PlayAssemble()         │
│  · Update 地板/天花板檢查：state == Playing（H1：!isDying 已移除，      │
│      state 本身排除 Dying）                                            │
└──────────────────────────────────────────────────────────────────────┘

┌─ 動畫資產（T5，Unity MCP 建，不可手寫 YAML）──────────────────────────┐
│  4 個 .anim：Assemble(20) / Dissasemble(15) / Jump(6) / Land(5)       │
│  1 個 .controller：{Hidden, Assemble, Land, Jump, Dissasemble}        │
│    · 預設態 = Hidden                                                   │
│    · 轉換條件全由程式參數／Event 驅動（無時間條件、無自動轉換）           │
│    · 剪輯不 loop（播完停最後一幀，FR-13）                               │
│    · **無 Animation Event**（B2：核心時序由 state-query 驅動；          │
│      未來輔助 hook 用新事件名加回）                                    │
│    · Sprite 引用 = texture guid + sprite internalID（NFR-04）          │
└──────────────────────────────────────────────────────────────────────┘
```

## 1. 四層分離（D1/FR-02）

| 層 | 內容 | 職責 | 變更者 |
|----|------|------|--------|
| 素材層 | 已 slice spritesheet（.png） | 純幀序列，零邏輯 | asset-developer |
| 狀態機層 | .controller | 狀態轉換，條件全由程式參數／Event 驅動 | asset-developer（Unity MCP） |
| 橋樑層 | **AnimatorStateInfo 查詢（B2）** | 動畫→程式（BlobAnimator.Update 讀 state＋normalizedTime fire C# 事件） | gameplay-developer |
| 控制層 | BlobAnimator.cs | 命令＋事件＋暫停＋死亡淡出 | gameplay-developer |

鐵律：

- 狀態機**不得**含時間條件或自動轉換（FR-02）——所有轉換由 BlobAnimator 命令觸發。
- 動畫資產**不可手寫 .anim/.controller YAML**（FR-01/NFR-05）——一律 Unity MCP 建。
- **核心時序＝AnimatorStateInfo 查詢（B2，ADR-013 決策 3 修訂）**：BlobAnimator.Update 讀 `GetCurrentAnimatorStateInfo(0)`，依 state＋normalizedTime fire C# 事件（once 旗標防重複）。**Animation Event 從核心時序退役**——.anim 剪輯無 Animation Event；未來輔助 hook（音效等非關鍵）用新事件名加回。
- **命令一律 `animator.Play(name, 0, 0f)`** 強制從頭播（B2 雙保險——`animator.Play(name)` 對同態重入不可靠）。

## 2. 狀態機（D2/FR-03~08）

```
預設態：Hidden（Menu/Ready 鳥隱藏，Animator 停播）

Hidden ──PlayAssemble()──▶ Assemble（Ready 態播放，A2）──AssembleComplete──▶ SetState(Playing) → 鳥開始下墜
                                                                              │
Assemble 期間無視跳躍鍵（FR-05/FR-11）＋世界凍結（背景 SetFrozen、水管不生成）  ▼
                                                                          Land（下墜中持續顯示）
                                                                              │
Land 期間按 jump ──PlayJump()──▶ Jump（012 蓄力）                              │
                                   │                                          │
Jump 012 播完（normalizedTime>=0.5）──JumpRise──▶ 設 flapForce 上升            │
                                   │                                          │
Jump 345 期間按 jump ──PlayJump()──▶ 回 012 重播（FR-07/FR-12）                 │
                                   │                                          │
上升結束（速度轉負）──PlayLand()──▶ Land ─────────────────────────────────────┘

任何狀態 ──PlayDissasemble()──▶ Dissasemble ──DissasembleComplete──▶ 淡出 ──▶ EndGame()
```

- 5 態 `{Hidden, Assemble, Land, Jump, Dissasemble}`，**無 Idle、無 Run**（ADR-013 決策 2）。
- 全部剪輯不 loop，播完停最後一幀直到狀態改變（FR-13）。
- **Assemble 期間鳥靜止**（用戶核心要求）：PlayerController.HandleGravity 開頭 `if (blobAnimator.IsAssembling) return;`——Assemble 播放期間鳥不累加重力（velocity 凍結 0），播完（AssembleComplete→isAssembling=false）才開始下墜。Assemble 期間 HandleLandAnimation 自然不觸發（velocity=0，`<0` 不成立）；Assemble 結束後重力恢復→velocity<0→PlayLand 自然銜接。
- **Assemble 期間世界凍結（A2，ADR-013 決策 11）**：Assemble 在 **Ready 態**播放——背景 `SetFrozen(true)`（onComplete 顯式呼叫）、水管因 `IsPlaying()==false` 自然不生成、計分不觸發。`AssembleComplete`（state-query 偵測）→ GameManager → `SetState(Playing)` → 世界開始動。
- 輸入規則（FR-12）：012 蓄力期間無視輸入（isCharging gate）；345 上升與 Land 下降期間可再跳（回 012 重播）；Assemble 期間無視輸入（isAssembling gate）；Dissasemble 期間無視輸入（dying 守衛）。

## 3. BlobAnimator 介面（D8/FR-19）

**命令**（程式→動畫，對應 .controller 狀態轉換，**全部 `animator.Play(name, 0, 0f)` 強制從頭播**）：

- `PlayHidden()` / `PlayAssemble()` / `PlayJump()` / `PlayLand()` / `PlayDissasemble()`

**state-query 核心時序**（B2，ADR-013 決策 3 修訂——取代 Animation Event 回呼）：

- BlobAnimator.Update 讀 `GetCurrentAnimatorStateInfo(0)`，依 state＋normalizedTime fire C# 事件（**once 旗標防重複**；命令方法重置對應 once 旗標）：
  - Jump 態且 `normalizedTime >= 0.5`（frame3/6）→ fire `JumpRise`＋`isCharging=false`
  - Assemble 態且 `normalizedTime >= 1` → fire `AssembleComplete`＋`isAssembling=false`
  - Land 態且 `normalizedTime >= 1` → fire `LandComplete`
  - Dissasemble 態且 `normalizedTime >= 1` → fire `DissasembleComplete`（hook）＋內部啟動 FadeOut()
- **Animation Event 已從 .anim 剪輯移除**（T5）——避免雙重 fire 與「no receiver」警告。未來輔助 hook（音效等非關鍵）用**新事件名**（如 `OnJumpSound`）加回。

**C# 事件**（程式訂閱面）：

- `JumpRise`——PlayerController 訂閱，設 flapForce
- `AssembleComplete`——**GameManager 訂閱**（A2：→ SetState(Playing)）＋保留 hook（未來音效）
- `LandComplete`——保留 hook（目前無消費者；未來可接落地音效/擠壓）
- `DissasembleComplete`——保留 hook（目前無消費者；淡出由內部啟動，不需外部訂閱；未來可接死亡音效/解體特效）
- `OnDeathFadeComplete`——GameManager 訂閱，EndGame()

**命名規則（CS0102 修正）**：C# 事件去 "On" 前綴（`JumpRise` 等）；無衝突事件維持 house style（`OnDeathFadeComplete`）。原「回呼方法保留 OnXxx」因回呼方法已移除（B2）而簡化——事件名規則不變。

**暫停接線**（FR-14，ADR-013 決策 5 修訂；**B1 修復 2026-09-05**）：

- **訂閱 `GameManager.OnStateChanged` 只存狀態快照** `lastState = state;`——**不在事件 fire 時求值 speed**。
- **`animator.speed` 由 Update 每幀求值**：`(lastState == Playing || lastState == Dying || isAssembling) ? 1 : 0`（`UpdateAnimatorSpeed()`，於 Update 內、state-query 之前）。
- **B1 教訓（勿重蹈）**：speed 求值**不得**綁在 OnStateChanged fire 時機——A2 流程下 `PlayAssemble()` 在 Ready 態呼叫（`SetState(Ready)` fire 之後），若 speed 只在事件 fire 時求值，`isAssembling=true` 的變化不會被重新求值 → speed 卡 0 → Assemble 凍結第一幀 → AssembleComplete 永不 fire → SetState(Playing) 永不發生（卡死）。每幀求值使 `isAssembling=true` 後**最多 1 幀內** speed=1，與事件 fire 時機無關。
- **Dying 接受（2026-09-30）**：`lastState == Dying` 加入映射——Dissasemble 在 Dying 態播放，speed 需為 1（否則 Dissasemble 凍結第 0 幀 → `dissasembleCompleteFired` 永不 fire → 淡出永不啟動 → 卡死）。
- **A2 配套**：Ready＋Assemble 時 speed=1（Assemble 播放）；其餘非 Playing（Menu/Ready 非 Assemble/Paused/GameOver）→ 0
- 不碰 timeScale、不改 GameManager（與 ADR-006/011 一致）

**死亡淡出**（FR-15）：

- state-query 偵測 Dissasemble 播完（`normalizedTime >= 1`）→ **內部啟動** `FadeOut()`（alpha→0）→ 完成後 fire `OnDeathFadeComplete`
- `FadeOut()` 以 `GameStateAuthority.IsPaused()` 自查 gate——**暫停時淡出凍結**（H5，ADR-013 決策 8；2026-09-30 由 `IsPlaying()` 改 `IsPaused()`——「非暫停即可淡出」，Dying 態淡出必須執行）
- GameManager **不訂閱** `DissasembleComplete`——淡出啟動在 BlobAnimator 內部，GameManager 只訂閱 `OnDeathFadeComplete` → EndGame

**ResetVisual()**（T1 附加介面，ADR-013 決策 4）：

- 恢復 alpha=1、取消進行中淡出（冪等）＋**清 gate 旗標與 once 旗標**（`isCharging=false; isAssembling=false; jumpRiseFired=false; assembleCompleteFired=false; landCompleteFired=false; dissasembleCompleteFired=false`）
- 呼叫點：`GameManager.ShowPlayerAtStart()`（與 `SetVisible(true)`＋`player.Reset()` 同幀）——避免 Restart 後鳥因死亡淡出殘留 alpha=0 而隱形，且避免上一局殘留 gate/once 旗標誤擋新局

**輸入 gate 旗標**（FR-11/FR-12/BC-4，T1 補）：

- `isCharging`：PlayJump→true、state-query fire JumpRise→false
- `isAssembling`：PlayAssemble→true、state-query fire AssembleComplete→false
- `PlayJump()` 開頭 `if (isCharging || isAssembling) return;`——012 蓄力期間不重播 012、Assemble 期間不強制跳入 Jump
- `PlayLand()` 開頭 `if (isAssembling || isCharging) return;`——012 蓄力期間不播 Land 中斷蓄力（012 時鳥在下降 velocity<0，若無 gate 會中斷 012）

**IsAssembling 查詢**（缺口 3，T1 補）：

- `public bool IsAssembling { get; private set; }`——PlayerController.HandleGravity 開頭 `if (blobAnimator.IsAssembling) return;`
- **ADR-011 合規**：GA1 管「遊戲狀態查詢」（IsPlaying）wiring；IsAssembling 是**動畫子狀態查詢**（組件內部狀態，BlobAnimator 自持自清），非遊戲狀態查詢，不經 GameStateAuthority 正確。否決方案 B（PlayerController 訂閱事件複製旗標）——雙重事實源。

## 4. 死亡流程（D7/FR-15~18，ADR-013 決策 8；2026-09-30 修訂：Dying 態取代旗標）

```
CollisionHandler / GameManager.Update（地板/天花板）
  └─▶ GameManager.Die()
        if (state != Playing) return;          // 既有守衛（重入防護：state==Dying 時直接 return，BC-3）
        SetState(Dying);                        // 取代 isDying 旗標（FR-16 撤銷）
        player.Die();                           // 凍結原地＋速度歸零＋停 collider
        blobAnimator.PlayDissasemble();
  └─▶ BlobAnimator state-query 偵測 Dissasemble 播完（normalizedTime>=1）
        → fire DissasembleComplete（hook）＋內部啟動 FadeOut()（alpha→0，IsPaused gate）
  └─▶ GameManager 訂閱 OnDeathFadeComplete
        → EndGame()                             // GameOver UI 延後到淡出後

StartGame()（A2，ADR-013 決策 11）：
  SetState(Ready) → HidePlayerAndPipes()
  → ScrollBackToStart(onComplete: {
      SetFrozen(true);                          // 背景凍結（Assemble 期間不捲動）
      ShowPlayerAtStart();                      // 鳥現身＋Assemble 開始（Ready 態播）
    })
  → BlobAnimator state-query 偵測 Assemble 播完（normalizedTime>=1）
      → fire AssembleComplete
  → GameManager 訂閱 AssembleComplete → if (state==Ready) SetState(Playing)
      → 背景 SetNormal＋水管 seed=0 第一幀生成（世界開始動）

ShowPlayerAtStart()：
  player.SetVisible(true)
  player.Reset(playerStartPosition)
  blobAnimator.ResetVisual()                    // 恢復 alpha=1＋清 gate/once 旗標
  blobAnimator.PlayAssemble()                   // Ready 態（speed=1，因 isAssembling）播 Assemble
```

- 死亡流程期間進入 **Dying 態**（取代原「維持 Playing 態」）；水管在死亡期間**凍結**（FR-17 撤銷，本功能核心）。
- **dying 守衛漏洞裁決**（ADR-013 決策 8，2026-09-30 翻轉）：

| # | 漏洞 | 裁決 |
|---|------|------|
| H1 | GameManager.Update 地板/天花板檢查在死亡流程期間若鳥死於邊界會每幀重觸發 Die() | **改**：`!isDying` 移除——`state == Playing` 已排除 Dying，檢查條件簡化 |
| H2 | P 鍵暫停在死亡流程期間可用 | **改**：死亡中按 P 不再誤觸暫停——Dying 不在 `state == Playing \|\| state == Paused` |
| H3 | ScoreTrigger 計分 | **維持可接受**：collider 停＋scored 旗標＋IsPlaying gate 三重防護；Dying 態 IsPlaying()==false 更嚴 |
| H4 | PipeSpawner 生成 | **改**：死亡期間不再生成——IsPlaying() 嚴格化後自然停 |
| H5 | 死亡淡出暫停時繼續 | **改**：淡出守衛從 IsPlaying() 改 IsPaused()——「非暫停即可淡出」 |
| H6 | 背景持續捲動 | **改**：死亡期間凍結——case Dying: SetFrozen(true) |

## 5. 視覺替換（D9/FR-22~24）

- Bird：MeshFilter＋MeshRenderer → **SpriteRenderer**（Animator 驅動 sprite）。
- sortingOrder 維持 **1000**（FR-23，ADR-004 D4 鳥恆在背景之上）。
- BoxCollider2D 尺寸依新 sprite（64×32＝2:1）調整（FR-24）。
- ADR-007 相容：`SetVisible` 的 `GetComponentsInChildren<Renderer>().enabled` 對 SpriteRenderer 通用（SpriteRenderer 繼承 Renderer），**不需改動**。
- 場景 Bird z=-0.02 可保留或歸零——SpriteRenderer 排序全由 sortingOrder 決定，z 不影響遮擋。

## 6. 資產引用（NFR-04）

- Sprite 引用＝**texture guid＋sprite internalID**：meta 內每幀有 `spriteID`（唯一 ID）與 `internalID`（序列化 fileID）。.anim 剪輯 keyframe 以 `{fileID: <internalID>, guid: <texture guid>, type: 3}` 引用。
- 例：Blue-Blob-Jump.png（guid `d81ff205355812e478b06343e32a6b51`）`_0`＝spriteID `8f75dd1a012453a4386b83831881bdab`／internalID `745265774`。
- 勿用檔名引用（house style）。

## 7. Developer 實作切入點

1. **T5（asset-developer）先行**：Unity MCP 建 4 個 .anim＋1 個 .controller（先驗證動畫 API 工具存在，假設 A；缺工具回報 Sisyphus）。**移除 Animation Event**（B2：核心時序由 state-query 驅動；.anim 剪輯無事件）。剪輯採樣率與幀率一致（假設 C，建議 12fps 或依美術原始幀率）。
2. **T1（gameplay-developer）BlobAnimator.cs**：命令（`animator.Play(name, 0, 0f)`）＋C# 事件＋**state-query 核心時序（once 旗標）**＋OnStateChanged 訂閱（`speed = (lastState == Playing || lastState == Dying || isAssembling) ? 1 : 0`）＋FadeOut（**IsPaused gate**）＋輸入 gate 旗標（isCharging/isAssembling）＋IsAssembling 查詢＋ResetVisual 清 gate/once 旗標。**移除 Animation Event 回呼方法**。開工前 unity_reflect 驗證 Animator API。
3. **T2（gameplay-developer）PlayerController**：移除 Flap() 直接設衝力；輸入→PlayJump()；訂閱 JumpRise→設 flapForce；HandleGravity 開頭 `if (blobAnimator.IsAssembling) return;`；速度<0 非 dying→PlayLand()。**無其他改動**（A2/B2 不影響 PlayerController）。
4. **T3（gameplay-developer）GameManager**：Die() 改 **SetState(Dying)**＋PlayDissasemble；訂閱 OnDeathFadeComplete→EndGame（淡出由 BlobAnimator 內部啟動，不訂閱 DissasembleComplete）；**訂閱 AssembleComplete→if(state==Ready) SetState(Playing)**；**StartGame onComplete 改 SetFrozen(true)→ShowPlayerAtStart()（不 SetState(Playing)）**；ShowPlayerAtStart() 內 SetVisible→Reset→ResetVisual→PlayAssemble；Update 地板/天花板檢查 `state == Playing`（**!isDying 移除**，2026-09-30）。
5. **T4（scene-developer）**：Bird 換 SpriteRenderer＋掛 Animator＋BlobAnimator；sortingOrder 1000；collider 依 2:1 調整。
6. **T6（gameplay-developer）**：驗證 OnStateChanged→animator.speed 接線（Playing/Ready+Assemble→1、Menu/Ready 非 Assemble/Paused/GameOver→0）。