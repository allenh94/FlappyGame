# Validation Report: blob-animator round 2
- **Date**: 2026-09-05
- **Task Sheet**: specs/requirements/blob-animator.md（FR-01~24、NFR、T1-T6）
- **Architecture**: specs/architecture/animator-system.md（最終真相，含 A2/B2 修訂）＋ docs/adr/013-blob-animator.md
- **Verdict**: **PASS（暫定）**——B1（round-1 blocker）修復正確，無新 blocker、無新 major。動態驗證 D1-D10 待人類實測回填，回填前 Verdict 為暫定。

## 複驗範圍

round-2 為 B1 修復複驗（round-1 唯一 blocker）＋無新回歸確認：
- `Assets/Scripts/BlobAnimator.cs`（B1 修復：speed 改 Update 每幀求值）
- `Assets/Scripts/GameManager.cs`、`Assets/Scripts/PlayerController.cs`（不變確認）
- `Assets/Scenes/SampleScene.unity`（場景接線不變確認）
- 對照：`specs/architecture/animator-system.md`（契約）、`specs/validation/blob-animator/round-1.md`（B1 指控）

## B1 修復複驗（round-1 blocker）

### 修復內容
- `HandleGameStateChanged(GameManager.GameState state)`（:131-137）改為**只存狀態快照** `lastState = state;`——不再在事件 fire 時求值 speed。
- 新增 `UpdateAnimatorSpeed()`（:139-145），於 `Update()`（:71）**每幀求值**：`animator.speed = (lastState == GameManager.GameState.Playing || isAssembling) ? 1f : 0f;`
- 新增欄位 `lastState`（:40）——最近一次 OnStateChanged 快照。

### 逐項驗證

| 檢查點 | 結果 | 證據 |
|--------|------|------|
| 每幀求值公式與契約 A2 一致 | ✅ | :144 `(lastState == Playing \|\| isAssembling) ? 1f : 0f` 與 animator-system.md:152-153「Ready＋Assemble 時 speed=1；其餘非 Playing→0」完全一致 |
| `lastState` 初始化安全 | ✅ | C# enum 預設值＝`Menu`（0）→ 初始 speed=0，Menu 態正確；`OnEnable`（:54-61）訂閱早於 `GameManager.Start`（:81 SetState(Menu)）首次 fire（Unity 執行序 Awake→OnEnable→Start 保證） |
| 訂閱時機安全 | ✅ | 場景已接線 gameManager（SampleScene.unity:2337 fileID 321862628）；OnEnable fallback `FindFirstObjectByType`（:58）存在；GameManager.Awake 先於 BlobAnimator.OnEnable（Unity 保證） |
| **五態語意** | ✅ | 見下表 |
| speed 唯一寫入點 | ✅ | grep 全 Assets `animator.speed` 僅 BlobAnimator.cs:144 一處（:7/:56 為註解） |
| 執行序健壯性 | ✅ | 無論 `ScrollBackToStart` onComplete callback 與 BlobAnimator.Update 的執行序，`isAssembling=true` 後**最多 1 幀內** speed=1——isAssembling 為持續狀態，每幀 Update 重求值，不依賴「事件 fire 當下求值」 |

### 五態語意驗證

| 狀態情境 | lastState | isAssembling | speed | 正確性 |
|----------|-----------|--------------|-------|--------|
| Ready 倒帶中（SetState(Ready) fire 後、ShowPlayerAtStart 前） | Ready | false | 0 | ✅ 鳥隱藏、Animator 停播（FR-04） |
| **Ready＋Assemble（B1 修復目標）** | Ready | true（PlayAssemble 設） | **1** | ✅ Assemble 正常播放，`normalizedTime` 推進 → AssembleComplete fire → SetState(Playing)（卡死解除） |
| Playing | Playing | false | 1 | ✅ 動畫正常播放 |
| Paused / GameOver | Paused / GameOver | false | 0 | ✅ 動畫凍結（FR-14/NFR-02） |
| 死亡流程（Playing 態 Dissasemble） | Playing | false | 1 | ✅ Die() 僅 Playing 態可入（GameManager.cs:208 守衛），Dissasemble 播放（FR-15） |

**B1 修復結論：正確。** 原卡死鏈（SetState(Ready) fire 時 isAssembling=false → speed=0 → PlayAssemble 不重新求值 → Assemble 凍結第一幀 → AssembleComplete 永不 fire → SetState(Playing) 永不發生）已被「每幀求值」徹底消除——`isAssembling=true` 後任何一幀 Update 都會把 speed 設為 1，與 OnStateChanged fire 時機無關。

## 無新回歸確認（B1 修復未波及）

| 檢查點 | 結果 | 證據 |
|--------|------|------|
| state-query 核心時序（Jump≥0.5、其他≥1.0、once 旗標） | ✅ | :85-92 與 round-1 完全一致；Update 新增 `UpdateAnimatorSpeed()`（:71）在 `UpdateAnimationStateSignals()`（:72）之前，不影響 state-query 邏輯 |
| 命令 `Play(name, 0, 0f)`＋重置 once 旗標 | ✅ | :98/:105/:113/:120/:126 全 `Play(name, 0, 0f)`；PlayAssemble→assembleCompleteFired=false、PlayJump→jumpRiseFired=false、PlayLand→landCompleteFired=false、PlayDissasemble→dissasembleCompleteFired=false |
| gate 旗標（isCharging/isAssembling）＋PlayJump/PlayLand 開頭 gate | ✅ | :102/:110/:118 不變 |
| 5 事件（JumpRise/AssembleComplete/LandComplete/DissasembleComplete/OnDeathFadeComplete） | ✅ | :24-32 不變 |
| FadeOut IsPlaying gate（H5） | ✅ | :152 `if (!GameStateAuthority.IsPlaying()) return;` 不變 |
| ResetVisual（alpha=1＋清 gate/once 旗標） | ✅ | :169-185 不變 |
| 場景接線 | ✅ | SampleScene.unity:2334-2338 BlobAnimator 元件 animator(1181836442)/spriteRenderer(1181836441)/gameManager(321862628)/fadeSpeed(1) 全接線；collider 0.64×0.32（:2321/:2393）、sortingOrder 1000（:2386）、Animator controller guid 9cfed30c…（:2408）——與 round-1 一致 |

## GameManager / PlayerController 不變確認

| 檢查點 | 結果 | 證據 |
|--------|------|------|
| A2：StartGame onComplete 改 SetFrozen(true)+ShowPlayerAtStart（不 SetState(Playing)） | ✅ | GameManager.cs:143-146 不變 |
| HandleAssembleComplete：`if (state == Ready) SetState(Playing)` | ✅ | :72-75 不變 |
| Die() re-entry guard＋PlayDissasemble＋停 collider | ✅ | :208-210 `if (state != Playing) return; if (isDying) return;`＋:213-214 停 collider＋:215 PlayDissasemble 不變 |
| H1 !isDying（Update 地板/天花板） | ✅ | :89 不變 |
| AN13 HandleGravity IsAssembling gate | ✅ | PlayerController.cs:99 不變 |
| T2 其餘（HandleInput→PlayJump、JumpRise 訂閱→flapForce、isLanding 防重複） | ✅ | PlayerController.cs:84/:45/:89-93/:119-123 不變 |

## round-1 minor 複核

| # | 內容 | 現況 | 裁決 |
|---|------|------|------|
| m1 | GameManager.Die():215 `blobAnimator.PlayDissasemble()` 無 null guard | **仍存在**（:215 無 guard；OnEnable:46-47 fallback 存在、場景已接線，非現行缺陷） | 維持 minor |
| m2 | PlayHidden() 死命令 | **仍存在**（grep 全 Assets 僅 BlobAnimator.cs:98 定義、零呼叫） | 維持 minor |
| m3 | FadeOut():154 `spriteRenderer.color` 無 null guard | **仍存在**（:154 無 guard；Awake:50-51 fallback 存在、場景已接線，非現行缺陷） | 維持 minor |
| m4 | PlayerController 旋轉死欄位（HandleRotation 已註解） | **仍存在**（:14-17 欄位＋:126-145 HandleRotation 被註解 :74；場景 SampleScene.unity:2219-2221 同欄位殘留） | 維持 minor |

## 新發現

### m5: GameManager.cs:200 過時註解「Ready 末段（speed=0）切態停第一幀」
- type: code | severity: minor
- 依據: 註解正確性（B1 修復後描述的行為已不存在）
- 事實: `GameManager.cs:200` `blobAnimator.PlayAssemble();  // Ready 末段（speed=0）切態停第一幀`——此註解描述的是 **B1 修復前**的行為（round-1 曾以此作為 B1 佐證）。B1 修復後 Ready＋Assemble 時 speed=1（每幀求值），Assemble 正常播放、不會「停第一幀」。註解現為誤導（讀者會以為 speed=0 是預期行為）。
- 建議方向: 更新註解為「Ready 態播 Assemble（speed=1，B1 修復後每幀求值）」。

### m6: animator-system.md 暫停接線節未反映 B1 修復的「每幀求值」時機
- type: architecture | severity: minor
- 依據: 文件漂移風險（round-1 教訓：speed 求值時機是 B1 根因）
- 事實: animator-system.md:25-26（類圖）與 :152-153（暫停接線節）字面描述「訂閱 `GameManager.OnStateChanged` → `animator.speed = (state == Playing || isAssembling) ? 1 : 0`」——事件驅動語義。實作已改為「訂閱存快照＋Update 每幀求值」。**行為等價**（每幀求值更嚴格保證公式成立，契約語義完全滿足），非違規；但文件未記錄求值時機，未來開發者照文件實作會重蹈 B1（事件驅動漏掉同態內旗標變化）。
- 建議方向: Tech Lead 視需要在文件補一句「speed 由 Update 每幀求值（B1 修復，2026-09-05）」。

## 動態驗證清單（無法靜態驗證者；人類實測回填前 Verdict 為暫定）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode 按 Start，觀察 Assemble 播放 | **B1 修復驗證**：鳥播 Assemble（20 幀）動畫，播完（約 1.67s）後世界才開始動（背景捲動、水管生成、鳥開始下墜）；**若鳥卡在 Assemble 第一幀不動、世界永不開始＝B1 未修復** | 待填 |
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

- 本環境無 unityMCP 工具（validator 唯讀工具不可用），編譯驗證依賴 Sisyphus bridge。**Sisyphus 已於 2026-09-05 以 bridge（HTTP JSON-RPC 直連 Unity Editor）執行 `refresh_unity(mode=force, scope=scripts, compile=request)`＋`read_console`，實證編譯零錯誤**（B1 修復後再驗一次亦零錯誤）——本節已回填，編譯關卡 PASS。
- 無測試套件（glob `Assets/**/*Test*.cs` 零命中，與 round-1 一致）。

## Statistics
- blocker: 0 / major: 0 / minor: 6（round-1 維持 4：m1-m4；新增 2：m5、m6）

## 備註
- B1 修復採「每幀求值」方案（round-1 建議方向之二），同時消除「OnStateChanged 求值時機」脆弱性——比「PlayAssemble 內設 speed=1」方案更健壯（涵蓋未來任何同態內旗標變化）。
- Verdict 為 PASS（暫定）：0 blocker 不觸發修復輪；D1-D10 人類實測回填後方可關單（D1 為 B1 修復的動態終證）。
- **補充（2026-09-05）**：Sisyphus 另產出 `static-flow-trace.md`（9 條主流程＋4 組競態的逐幀模擬追蹤，全數靜態通過）——作為 D1-D10 人類實測前的靜態信心依據與回歸複核基準；編譯驗證已由 Sisyphus bridge 實證零錯誤（見上節）。