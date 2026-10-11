# Dependency Rules

> 本文件由 tech-lead-agent 維護，其他 agent 唯讀。
> Validator 以本文件作為 `type: architecture` 缺陷的判定依據；Developer 新增跨腳本依賴前必須經過 Tech Lead 審核。

## 規則清單

### A. 背景系統（bg-layer-editor，2026-08-21 制定；詳見 bg-layer-editor.md／ADR-004）

| # | 規則 | 理由 |
|---|------|------|
| B1 | 背景捲動全專案**單一實作**：僅 `BGLayer` 的取模推導；`BackgroundLayer.cs` 移植後刪除，不得殘留第二套 parallax | FR-12 單一 Parallax |
| B2 | 配置單向流：Editor Window → BGConfig SO → Runtime 唯讀；Runtime 任何代碼不得寫入 BGConfig | FR-17/FR-18 配置與實例分離 |
| B3 | **Depth／Render Order 分離鐵律**：Depth 只進速度公式（speed=baseSpeed/depth），Render Order 只進 sortingOrder；兩者不得互相讀取或推導 | FR-04 分離鐵律 |
| B4 | 遮擋禁用 z：背景層根與 tile 一律 z=0，遮擋僅經 Default Sorting Layer＋sortingOrder 帶位（10/20/30…）；鳥 order=1000 | FR-04/FR-05；鳥可見性驗收項 |
| B5 | Instantiate/Destroy 僅限池邊界：Tile 只允許 TilePool createFunc/onDestroy 觸碰；穩態捲動熱路徑零分配（禁 LINQ/字串串接/裝箱） | NFR-01～NFR-03 |
| B6 | 目錄邊界：Runtime 核心 `Assets/Scripts/Background/`；Editor 工具僅限 `Assets/Scripts/Editor/Background/`；配置資產 `Assets/Config/Background/`；模板 prefab `Assets/Prefabs/Background/Tiles/`。跨目錄引用需 Tech Lead 審核 | FR-18；D6 目錄隔離 |
| B7 | 外部依賴白名單：背景系統僅允許依賴 `UnityEngine`／`UnityEngine.Pool`／`UnityEditor`（Editor 目錄內）。禁止依賴玩家、水管、計分、輸入等任何遊戲邏輯模組 | NFR-06 低耦合 |
| B8 | GameManager→BGManager **單向接線**：GameManager 僅呼叫 `BGManager.ResetAll()`；BGManager 不得反向引用 GameManager；死亡重開 Reset 語義不得回歸為 foreach 舊型別 | FR-13 不回歸；規格「涉及物件」表 |

### G. 遊戲狀態系統（game-states，2026-08-24 制定；詳見 game-states.md／ADR-006）

| # | 規則 | 理由 |
|---|------|------|
| G1 | GameManager 為唯一狀態權威：`GameState` 變更一律經 `SetState()`（單一變異點）；`IsPlaying()` 語義 = `state == Playing`。enum 含 `Ready` 過渡態與 `Dying` 死亡態（`{ Menu, Ready, Playing, Dying, Paused, GameOver }`，ADR-012／dying-state）；Ready 期間 `IsPlaying()` 為 false；**Dying 期間 `IsPlaying()` 為 false**（死亡瞬間水管/生成/計分自查凍結） | NFR-01 不回歸；狀態機單一真源 |
| G2 | 凍結機制＝各 gameplay 系統自查 `IsPlaying()`；背景經 GameManager→BGManager.SetFrozen 單向凍結（B8）；**禁 SetActive 停用、禁 Time.timeScale=0**。Menu 隱藏玩家＝`Renderer.enabled`（禁 SetActive 停用 gameplay 物件；見 ADR-007）。**Ready 例外**：gameplay 系統因 `IsPlaying()==false` 自查凍結，但背景**不**凍結（`SetState` 的 `case Ready: break;`，背景處 ScrollingBack 持續倒帶；見 ADR-012）。**Dying 為新增凍結態**：gameplay 系統因 `IsPlaying()==false` 自查凍結，背景靠 `SetState` 的 `case Dying: SetFrozen(true)` 立即凍結（見 dying-state） | NFR-02 凍結一致性；SetActive 副作用不可控；timeScale 凍結 UI＋手動重力衝突 |
| G3 | P 鍵暫停歸屬 GameManager（遊戲流程輸入）；PlayerController 只管 flap（space）；兩鍵不互擾 | NFR-04 輸入衝突；狀態機擁有者持流程輸入 |
| G4 | UI 系統目錄 `Assets/Scripts/UI/`；UI 與 GameManager 接線＝公開方法＋`OnStateChanged` 事件；UI 不得直接操作內部狀態 | NFR-05 低耦合；CONTEXT.md 目錄慣例 |
| G5 | UI 控制腳本掛持久根（非被停用 Canvas）；MenuCanvas 與 OverlayCanvas 分離 | 事件訂閱需常駐；兩 Canvas 生命週期/內容不同 |

### S. 得分系統（score-session，2026-08-26 制定；詳見 ADR-008）

| # | 規則 | 理由 |
|---|------|------|
| S1 | 得分邏輯唯一真源：`ScoreSession`（`Assets/Scripts/Score/`）為得分／最高分／破紀錄判定／PlayerPrefs 持久化唯一實作；GameManager 不得再持有 `score`／`previousHighScore`／`HighScoreKey` 欄位或直接讀寫 PlayerPrefs | ADR-008 深模組；上帝模組瘦身 |
| S2 | 破紀錄順序封閉：快照舊最高分 → 計算 recordBroken → 覆寫，一律在 `ScoreSession.End()` 內；GameManager 不得重排該順序 | ADR-008 順序 bug 收斂 |
| S3 | 接線契約：GameManager 暴露唯讀 `Score`（ScoreSession）單一 accessor；UI／ScoreTrigger 經此讀取／計分，不直接觸碰 PlayerPrefs | ADR-008 介面收斂 |
| S4 | 目錄邊界：Score 系統 runtime 僅限 `Assets/Scripts/Score/`；PlayerPrefs 持久化為 ScoreSession 內部 adapter，不暴露外部 | CONTEXT.md 目錄慣例 |

### P. 水管對（pipe-pair，2026-08-26 制定；詳見 ADR-009）

| # | 規則 | 理由 |
|---|------|------|
| P1 | PipeController 公開介面收斂為單一 `Configure(center, gapSize)`；`bottomPipe`／`topPipe` 收為 private serialized；刪除 public `Movement`／`setCenterGap` 與 dead `startPosition` | ADR-009 深模組；呼叫端不需知內部結構 |
| P2 | 移動由 PipeController.Update 自行驅動（內部）；外部不得呼叫移動 | ADR-009 移動隱藏 |
| P3 | 命名 PascalCase：私有 gap 設定方法 `SetGap` | ADR-009 命名一致 |

### PV. 水管變體（pipe-sprite-variants，2026-10-01 制定；詳見 ADR-014）

| # | 規則 | 理由 |
|---|------|------|
| PV1 | PipeVariant 單一職責：隨機選 sprite variant＋同步 collider＋套用 scale；collider 同步為 sprite 驅動（physics shape 主軌＋bounds fallback 雙軌，不收斂） | ADR-014 決策 1/4；collider 與視覺同步 |
| PV2 | 依賴方向 PipeController → PipeVariant **單向**：PipeController 於 `Configure()` 內觸發 `ApplyRandom`（null guard）；PipeVariant 不得引用 PipeController 或任何遊戲邏輯模組 | ADR-014 決策 1；低耦合 |
| PV3 | `ApplyRandom` 每次**完整重置**（`pathCount` 重設＋重 `SetPath`）；variants 空陣列時 return（防 IndexOutOfRange）；不得只在 Awake 做一次 | ADR-010 pool 重用陷阱；ADR-014 決策 1 |

### PL. 共用池化（pooling，2026-08-26 制定；詳見 ADR-010）

| # | 規則 | 理由 |
|---|------|------|
| PL1 | 池週期唯一實作：`PooledActives<T>`（`Assets/Scripts/Pooling/`）為 `UnityEngine.ObjectPool` 唯一薄 adapter，負責 active-list 追蹤＋重複 get/release 防護；PipeSpawner 與 BGLayer 不得再手刻 ObjectPool 四 callback | ADR-010 真 seam 收斂 |
| PL2 | 建立邏輯留呼叫端：createFunc（pipe prefab／tile template）由 PipeSpawner／BGLayer 提供；PooledActives 只處理池週期（get/release/destroy/active-list） | ADR-010 只抽池週期 |
| PL3 | 熱路徑零分配維持：active-list 用預分配 List，禁 LINQ／GC | NFR-01～03 |

### GA. 遊戲狀態權威查詢（game-state-authority，2026-08-26 制定；詳見 ADR-011）

| # | 規則 | 理由 |
|---|------|------|
| GA1 | 狀態查詢唯一 seam：`GameStateAuthority.IsPlaying()`／`IsPaused()` 為 gameplay 系統唯一狀態查詢；Player／PipeController／PipeSpawner／ScoreTrigger 不得各自持有 GameManager 引用＋FindObjectOfType 查 IsPlaying | ADR-011 wiring 收斂；dying-state 擴張（IsPaused 為查詢非命令） |
| GA2 | 凍結政策不變（ADR-006 D3）：各系統仍自查 `IsPlaying()`（輪詢），只改查詢來源；禁 SetActive 停用、禁 timeScale=0 維持 | ADR-011 收 wiring 不收政策 |
| GA3 | 命令仍走 GameManager：Die／StartGame／Pause 等流程命令與 `OnStateChanged` 事件維持在 GameManager；GameStateAuthority 只做狀態查詢，不承接命令 | ADR-011 查詢/命令分離 |
| GA4 | 事件匯流排決策：`OnStateChanged` 維持現狀（僅 UI 訂閱），不升級正式事件匯流排——5 個 gameplay 系統用輪詢（IsPlaying）非事件訂閱，未觸發「第二個訂閱者」演進條件 | ADR-011 評估決策 |

### AN. 動畫系統（blob-animator，2026-09-04 制定；詳見 animator-system.md／ADR-013）

| # | 規則 | 理由 |
|---|------|------|
| AN1 | 動畫資產唯一建置途徑＝Unity MCP；**禁手寫 .anim/.controller YAML** | FR-01/NFR-05；資產序列化由工具保證 |
| AN2 | 狀態機 5 態 `{Hidden, Assemble, Land, Jump, Dissasemble}`，無 Idle/Run；轉換條件全由程式參數／Event 驅動，**禁時間條件/自動轉換** | FR-02/FR-03；時序由 BlobAnimator 命令控制 |
| AN3 | 動畫→程式橋樑唯一機制＝**AnimatorStateInfo 查詢（B2）**：BlobAnimator.Update 讀 `GetCurrentAnimatorStateInfo(0)`，依 state＋normalizedTime fire C# 事件（once 旗標防重複）。**Animation Event 從核心時序退役**——.anim 剪輯無 Animation Event；未來輔助 hook（音效等非關鍵）用新事件名加回 | ADR-013 決策 3 修訂；動態驗證「只能跳一次」 |
| AN4 | BlobAnimator 為控制層唯一持有 Animator 的元件；命令 PlayHidden/PlayAssemble/PlayJump/PlayLand/PlayDissasemble 對應狀態轉換，**全部 `animator.Play(name, 0, 0f)` 強制從頭播** | FR-19；控制層收斂；B2 雙保險 |
| AN5 | 暫停接線：BlobAnimator 訂閱 GameManager.OnStateChanged → `animator.speed = (lastState == Playing \|\| lastState == Dying \|\| isAssembling) ? 1 : 0`（Playing/Dying/Ready+Assemble→1；Menu/Ready 非 Assemble/Paused/GameOver→0）；不碰 timeScale、不改 GameManager | FR-14；ADR-006/011 凍結政策；A2 配套；dying-state（Dying 接受） |
| AN6 | 死亡淡出（alpha→0）以 GameStateAuthority.IsPaused() 自查 gate——暫停時凍結（「非暫停即可淡出」） | H5；ADR-006 D3 凍結一致性；dying-state（原 IsPlaying() 改） |
| AN7 | 死亡流程期間進入 **Dying 態**（`Die()` 由 `isDying = true` 改 `SetState(Dying)`）；**不新增旗標**；水管在死亡期間**凍結**（FR-17 撤銷） | FR-16/FR-18；ADR-006/011；dying-state（翻轉 ADR-013 決策 8） |
| AN8 | GameManager.Update 地板/天花板檢查＝`state == Playing`（**`!isDying` 已移除**——state 本身排除 Dying，死亡流程期間不會每幀重觸發 Die()） | H1；dying-state（原「加 !isDying 防禦性」裁決隨旗標移除而撤銷） |
| AN9 | Sprite 引用＝texture guid＋sprite internalID（`{fileID: <internalID>, guid: <texture guid>, type: 3}`）；勿用檔名 | NFR-04；house style |
| AN10 | 剪輯不 loop，播完停最後一幀直到狀態改變 | FR-13 |
| AN11 | BlobAnimator 命名規則：Animation Event 回呼方法保留 `OnXxx`（.anim 資產契約）；與之衝突的 C# 事件去 "On" 前綴（`JumpRise` 等）；無衝突事件維持 house style（`OnDeathFadeComplete`）。禁 method 與 event 同名（CS0102） | ADR-013 決策 4 |
| AN12 | 輸入 gate 在 BlobAnimator 內部：`isCharging`（PlayJump→true、OnJumpRise→false）＋`isAssembling`（PlayAssemble→true、OnAssembleComplete→false）；PlayJump/PlayLand 開頭 `if (isCharging || isAssembling) return;`。ResetVisual() 清兩旗標 | FR-11/FR-12/BC-4；012 蓄力/Assemble 期間無視輸入 |
| AN13 | Assemble 期間鳥靜止：BlobAnimator 暴露 `public bool IsAssembling { get; private set; }`；PlayerController.HandleGravity 開頭 `if (blobAnimator.IsAssembling) return;`。IsAssembling 是動畫子狀態查詢（組件內部），非遊戲狀態查詢，不經 GameStateAuthority（GA1 不違反） | 用戶核心要求「Assemble 播完才下墜」；ADR-013 決策 4 |
| AN14 | 核心時序 once 旗標：state-query fire 事件用 once 旗標防重複（jumpRiseFired/assembleCompleteFired/landCompleteFired/dissasembleCompleteFired）；命令方法重置對應 once 旗標；ResetVisual 清全部 | ADR-013 決策 4（B2）；防重複 fire |
| AN15 | Assemble 期間維持 Ready 態（A2）：StartGame 倒帶完成 onComplete 改 `SetFrozen(true)→ShowPlayerAtStart()`（不 SetState(Playing)）；GameManager 訂閱 AssembleComplete → `if (state==Ready) SetState(Playing)`；背景 Assemble 期間凍結、水管因 IsPlaying()==false 自然不生成（PipeSpawner 零改動） | ADR-013 決策 11；用戶核心要求「Assemble 播完遊戲才開始」 |

### LB. 排行榜系統（leaderboard，2026-10-04 制定；leaderboard-lootlocker 線上後端，2026-10-05 擴充；詳見 leaderboard-system.md／ADR-015＋ADR-016）

| # | 規則 | 理由 |
|---|------|------|
| LB1 | 榜單持久化唯一真源：`LootLockerLeaderboardProvider`（`Assets/Scripts/Leaderboard/`）為榜單唯一實作（線上 LootLocker 後端；快取 key `flipbird_leaderboard_cache`／佇列 key `flipbird_leaderboard_pending`）；與 `flipbird_high_score`（ScoreSession）／`flipbird_player_name`（PlayerNameStore）分離，互不覆寫；**ScoreSession 零改動**；舊 `flipbird_leaderboard`（LocalLeaderboardProvider）**退役**（不讀／不寫／不遷移） | ADR-016 決策 3；ADR-015 決策 1/8；NFR-02 資料分離 |
| LB2 | 接口契約：`LeaderboardProvider`（`LoadAll`/`Submit` async callback）為呼叫端唯一介面；`SubmitResult` 含 `Qualified`/`Rank`/`Entries`；**callback 保證主執行緒呼叫**；**Submit callback 內禁再 LoadAll**（`Entries` 已含提交後完整榜單） | ADR-015 決策 4；NFR-05 換實作呼叫端零改動 |
| LB3 | 依賴方向 **UI → GameManager.Leaderboard → Provider → ILootLockerApi → LootLocker SDK 單向**：Provider 不得反向引用遊戲邏輯（GameManager/ScoreSession/UI）；Leaderboard 模組依賴 UnityEngine（PlayerPrefs/JsonUtility/SystemInfo）＋System＋（僅經 seam）LootLocker SDK | ADR-016 決策 1；ADR-015 決策 1；B7 精神低耦合 |
| LB4 | 持久化隔離（比照 S4）：PlayerPrefs 讀寫隔離在 Provider 內部 adapter；UI 不得直接觸碰 PlayerPrefs（含名稱記憶——經 PlayerNameStore seam，LB5） | ADR-015 決策 3；NFR-03 grep 驗收（UIController 零 PlayerPrefs） |
| LB5 | 名稱記憶 seam：`PlayerNameStore`（key `flipbird_player_name`）為名稱記憶唯一實作；UI 經 `GameManager.PlayerName` accessor 讀寫；**不得併入 LeaderboardProvider 接口**（名稱非榜單職責） | ADR-015 決策 3；NFR-03 與決策 A=a 衝突化解 |
| LB6 | Qualify 判定單一事實來源：`LeaderboardRules.Qualifies` 純函式（<10 筆須 >0；=10 筆須 >第 10 名；與第 10 名同分不算；**null/空陣列視為空榜單**）；Submit 內部與 UI 顯示判定共用 | ADR-015 決策 1；FR-05/FR-07 corrupt 防護配套 |
| LB7 | 演出職責：Qualify Animation 歸 `LeaderboardAnimator`（`Assets/Scripts/UI/`，獨立演出元件）；動畫目標一律榜單列 **child RectTransform**（B3 教訓：根 Canvas 位移視覺無效）；中斷歸位＝`StopAll()` 全部列＋`StopFloatTweens` 滾分 | ADR-015 決策 5；ui-animations round-3 實證 |
| LB8 | 非 GameState：LeaderboardScreen 於 Menu／GameOver 兩態內顯示，**不新增 GameState**；顯示/隱藏由 UIController 顯示旗標管理（非 OnStateChanged state 切換）；返回/重開走既有 GameManager 公開方法（BackToMenu/Restart）；離開路徑（返回鈕/BackToMenu/Restart/OnStateChanged 開頭）呼叫 `LeaderboardAnimator.StopAll()` | ADR-015 決策 7；NFR-01/ADR-006 凍結政策相容 |
| LB9 | **SDK 呼叫隔離**：LootLocker SDK 呼叫一律封裝於 `internal ILootLockerApi` seam；**只有 concrete `LootLockerApi` 可引用 `LootLocker.*`**；provider 邏輯（session／快取／佇列／memberId）不得直接呼叫 SDK；seam 回傳領域型別（`bool`／`int`／`LeaderboardEntry[]`），零 SDK 型別洩漏 | ADR-016 決策 1；NFR-03；測試可注入 fake |
| LB10 | **主執行緒 marshal 契約**：SDK 8.2.0 callback 保證主執行緒（實證：`LootLockerHTTPClient : MonoBehaviour` 由 Update/LateUpdate/coroutine 驅動）；**無 dispatcher**；concrete adapter 建構捕捉 `ManagedThreadId`、callback 入口斷言，不符則 log＋丟棄；SDK 升級須重驗（見演進觸發條件） | ADR-016 決策 2；ADR-015 決策 4；NFR-04/NFR-07 |
| LB11 | **快取／佇列 key 分離**：`flipbird_leaderboard_cache`（上次成功榜）／`flipbird_leaderboard_pending`（待送佇列）與 `flipbird_high_score`／`flipbird_player_name` 分離、互不覆寫；舊 `flipbird_leaderboard` 退役（不讀／不寫／不遷移、不主動刪除） | ADR-016 決策 3；NFR-02 |
| LB12 | **session 生命週期**：lazy `StartGuestSession(SystemInfo.deviceUniqueIdentifier)`（首個網路操作觸發）；ready 旗標；成功→flush 佇列；失敗→走快取／排隊失敗路徑，不阻塞不 crash，下次操作再試 | ADR-016 決策 4；FR-02/BC-3 |
| LB13 | **待送佇列**：`Submit` 失敗且 `LeaderboardRules.Qualifies(score, cached)` 才入列；項 `{memberId, playerName, score}`；上限 20（**FIFO drop-oldest**）；flush 逐項序列、成功移除失敗保留、`isFlushing` 防重入；memberId 提交當下產生、補送沿用 | ADR-016 決策 4；FR-06/FR-08/BC-8 |
| LB14 | **唯一 member_id**：每次提交產生唯一 id（GUID 衍生，無溢位）；Arcade 每筆各自成列；補送沿用同一 id（避免重複列） | ADR-016 決策 4；FR-04/BC-5 |
| LB15 | **伺服器權威 SubmitResult**：`Qualified` 由伺服器 `rank∈top10` 決定；`Rank` 來自 `SubmitScore`；`Entries` 來自 `GetScoreList(key,10)`；客戶端 `LeaderboardRules.Qualifies` 僅為提交前守衛（不決定 Qualified）；provider 永遠回呼（失敗回 `SubmitResult(false,-1,cached)`／`LoadAll` 回快取或空榜） | ADR-016 決策 4；FR-07/BC-6 |
| LB16 | **安全**：`Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset`（含 `.meta`）不進版控；資產在 `Resources/` 會被打包→**build 前必須清空 `adminToken`**（客戶端只需 `apiKey`）；SOP 檢查；建議 Editor pre-build guard（非阻擋） | ADR-016 決策 7；NFR-06/BC-9 |

### 演進觸發條件

- Editor 代碼膨脹（>3 檔或 >500 行）→ 升級 Editor asmdef（建議修訂 B6）。
- 出現第二個需要配置資產的系統 → 統一 `Assets/Config/` 慣例並回寫本文件。
- 出現執行期動態增刪 Layer 或解析度變更需求（P2）→ 重估 Tile 生命週期模型（D7／ADR-005）。
- 出現第二個訂閱 GameManager 狀態的系統（除 UI 外）→ 重估 `OnStateChanged` 事件是否需升級為正式事件匯流排。（**已觸發一次**：blob-animator 的 BlobAnimator 為第二訂閱者；ADR-013 決策 6 重估結論＝維持 plain event 不升級。條件保留，出現第三個訂閱者再重估。）
- **LootLocker SDK 升級**（>8.2.0）→ 重驗主執行緒 callback 保證（LB10）；若保證失效（斷言 fire），引入 dispatcher（GameManager.Update pump 或 MonoBehaviour runner）並回寫 ADR-016。
- 出現第二個使用 LootLocker SDK 的系統 → 重估 `ILootLockerApi` seam 是否升級為共享服務（現為 leaderboard 專用）。

## 既有依賴現況

- `GameManager` ──呼叫──▶ `BGManager.SetNormal()/SetAmbient()/SetFrozen(bool)`（SetState 依 GameState；`case Ready: break;` 不凍結、`case Dying: SetFrozen(true)` 死亡凍結）＋`ScrollBackToStart()`（StartGame）／`ScrollBackToMenu()`（BackToMenu）
- `GameManager` ──呼叫──▶ `player.Reset()`（StartGame）＋`player.Die()`（Die）＋`HidePlayerAndPipes()`（BackToMenu：鳥 Renderer.enabled 隱藏＋`pipeSpawner.ResetAll()`）
- `GameManager` ──持有──▶ `ScoreSession`（`Score` accessor；StartGame→`Begin()`、EndGame→`End()`）
- `ScoreTrigger` ──呼叫──▶ `gameManager.Score.AddPoint()`（計分）＋`GameStateAuthority.IsPlaying()`（狀態查詢）
- `Player`／`PipeController`／`PipeSpawner` ──查詢──▶ `GameStateAuthority.IsPlaying()`（ADR-011；不再各自持 GameManager 引用）
- `CollisionHandler` ──呼叫──▶ `gameManager.Die()`（命令，維持 GameManager 引用）
- `BlobAnimator` ──訂閱──▶ `GameManager.OnStateChanged`（animator.speed 控制；ADR-013 決策 6：第二訂閱者，GA4 裁決不升級匯流排）
- `BlobAnimator` ──查詢──▶ `GameStateAuthority.IsPaused()`（死亡淡出 gate，AN6；dying-state 由 IsPlaying() 改）＋`Animator.GetCurrentAnimatorStateInfo(0)`（核心時序，AN3/B2）
- `PlayerController` ──訂閱──▶ `BlobAnimator.JumpRise`（設 flapForce）＋──呼叫──▶ `BlobAnimator.PlayJump()/PlayLand()`（T2）＋──查詢──▶ `BlobAnimator.IsAssembling`（HandleGravity gate，AN13）
- `GameManager` ──呼叫──▶ `BlobAnimator.PlayDissasemble()`（Die）＋`BlobAnimator.ResetVisual()`＋`BlobAnimator.PlayAssemble()`（ShowPlayerAtStart）＋──訂閱──▶ `BlobAnimator.OnDeathFadeComplete`（死亡流程）＋`BlobAnimator.AssembleComplete`（A2：→ SetState(Playing)，AN15）
- `PipeSpawner` ──持有──▶ `PooledActives<PipeController>`（createFunc 建 pipe prefab；ADR-010）
- `PipeController` ──呼叫──▶ `PipeVariant.ApplyRandom()`（`Configure()` 內觸發；ADR-014，PV2 單向）
- `BGLayer` ──持有──▶ `PooledActives<GameObject>`（createFunc 建 tile template；ADR-010）
- `BGManager` ──建立／持有──▶ `BGLayer` ×N ──持有──▶ `TilePool`（per template）──▶ `Tile`
- `BGLayerEditorWindow`（Editor）──寫──▶ `BGConfig` SO；Runtime 全體──讀──▶ `BGConfig`
- 背景系統與玩家／水管／計分零依賴（B7）
- `GameManager` ──持有──▶ `LeaderboardProvider`（`Leaderboard` accessor；Awake `new LootLockerLeaderboardProvider()`，欄位型別 `LeaderboardProvider`；比照 ScoreSession 模式；ADR-015 決策 2＋ADR-016 事實更正 2）＋`PlayerNameStore`（`PlayerName` accessor；Awake `new PlayerNameStore()`；ADR-015 決策 3）
- `UIController` ──呼叫──▶ `gameManager.Leaderboard.LoadAll()/Submit()`（上榜流程）＋`gameManager.PlayerName.Load()/Save()`（名稱 modal 預填/記憶）＋`LeaderboardAnimator.Render()/PlayQualify()/StopAll()`（演出；ADR-015 決策 5）
- `LeaderboardAnimator`（UI）──呼叫──▶ `UITween.AnchoredPositionTo/FloatTo/StopAll/StopFloatTweens`（沿用既有原語，UITween 零改動）
- `LootLockerLeaderboardProvider` ──呼叫──▶ `ILootLockerApi`（internal seam）──▶ LootLocker SDK（`LootLockerSDKManager.StartGuestSession/SubmitScore/GetScoreList`；SDK 呼叫隔離，LB9；主執行緒斷言，LB10）
- `LootLockerLeaderboardProvider`／`PlayerNameStore` ──讀寫──▶ PlayerPrefs（key `flipbird_leaderboard_cache`／`flipbird_leaderboard_pending`／`flipbird_player_name`；內部 adapter，LB4/LB5/LB11）；與 ScoreSession 的 `flipbird_high_score` 分離（LB1）；舊 `flipbird_leaderboard` 退役（LB11）
