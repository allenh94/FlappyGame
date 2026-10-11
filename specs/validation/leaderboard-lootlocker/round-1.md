# Validation Report: leaderboard-lootlocker round 1

- **Date**: 2026-10-05 11:35
- **Task Sheet**: `specs/requirements/leaderboard-lootlocker.md`
- **Architecture**: `specs/architecture/leaderboard-system.md`（§1.5 線上後端）、`specs/architecture/dependency-rules.md`（LB1、LB3、LB9-LB16）、`docs/adr/016-leaderboard-lootlocker.md`（決策 1-7）
- **前身缺陷**: `specs/validation/leaderboard/round-1.md` m1（PlayQualify 未檢 Qualified）
- **Verdict**: **PASS（暫定）** — 0 blocker / 2 major / 5 minor；動態驗證 D1-D15 待 Sisyphus Phase 4.5 回填（回填前暫定）

> **驗證方法限制聲明**：本輪無 git（repo 根 `.git/` 為空目錄，非有效版控），所有「零改動」主張以**語義級核對**（逐行讀檔比對行為）＋ grep 完成，非 diff。Console 以 `unityMCP_read_console` 即時讀取；測試以 `unityMCP_run_tests`（EditMode）＋ `TestResults.xml` 落盤檔交叉核對。場景結構未重驗（本 feature 為後端替換，場景/UI 除 T4 最小硬化外不變，前輪已驗）。

---

## Blockers

**無。**

- EditMode 測試全綠：`TestResults.xml`（`C:\Users\Allen\AppData\LocalLow\Allenh94\FlappyGame\TestResults.xml`）記載 `testcasecount=23 passed=23 failed=0 inconclusive=0 skipped=0`（start-time 2026-10-05 03:19:14Z）；`Editor.log:3548/3570` 證實本輪 run 完成並落盤。三個 fixture：`LeaderboardRulesTests` 3/3、`LootLockerLeaderboardProviderTests` 16/16、`PlayerNameStoreTests` 4/4。
- Console 零遊戲程式碼 error：`read_console`（error+warning）19 筆全為 warning——MCP WebSocket 噪音、測試誘發的 `[LootLockerLeaderboardProvider] GetScoreList failed`（fake `ListSuccess=false` 案例）、既有 `CS0618`（ScoreTrigger.cs:12）。無 error。

---

## Majors

### M1: `SubmitScore` 成功但 `GetScoreList` 失敗時，`SubmitResult.Entries` 退回快取（不含新條目），UI `PlayQualify` 以 rank 索引會對錯誤條目播動畫

- **type**: code ｜ **severity**: major
- **依據**: FR-07「`Entries` 來自 `GetScoreList(key,10)`」；`LeaderboardProvider.cs:23` 契約「`Entries`＝提交後完整榜單（Qualify Animation 用）」；LB15
- **事實**:
  - `LootLockerLeaderboardProvider.SubmitToServer`（:113-125）：`SubmitScore` 成功後呼叫 `GetScoreList`；若 `listSuccess==false`，`entries = LoadCacheOrEmpty()`（:122）——**退回提交前的快取**（不含剛提交的新條目），但 `Qualified` 仍由伺服器 `rank` 決定（:124），故回呼 `SubmitResult(true, rank, 提交前快取)`。
  - UI `UIController.ConfirmNameInput`（:322-330）在 `result.Qualified==true` 時呼叫 `leaderboardAnimator.PlayQualify(result)`。
  - `LeaderboardAnimator.PlayQualify`（:88-89）以 `newIndex = Clamp(result.Rank - 1, 0, count-1)` 取新條目、`newScore = _entries[newIndex].score`——若 `Entries` 為提交前快取，`_entries[newIndex]` 是**別的玩家**，動畫會對錯誤列滾分／高亮。
  - 測試 `Submit_Success_ListFailure_FallsBackToCache`（LootLockerLeaderboardProviderTests.cs:204-216）只斷言 `Qualified/Rank/Entries.Length`，未覆蓋動畫正確性。
- **影響**: 邊界情況（SubmitScore 成功 + GetScoreList 失敗，兩次獨立 HTTP 呼叫間網路抖動）下，玩家實際已上榜但動畫顯示錯誤條目；下次 `LoadAll` 會自校正。不擋驗收（伺服器狀態正確、UI 不 crash、可自校正）。
- **建議方向**（僅供路由參考）: provider 於 `GetScoreList` 失敗時，可將提交條目插入快取副本後回傳（維持「Entries 含新條目」契約），或 UI 於 `PlayQualify` 前驗證 `Entries[rank-1]` 與提交條目一致。

### M2: NFR-06/BC-9 安全——`adminToken`（Server key）現值非空且資產在 `Resources/` 會被打包；ADR-016 決策 7 的 `.gitignore` 條目未落實、無 pre-build guard

- **type**: architecture ｜ **severity**: major
- **依據**: NFR-06「`LootLockerConfig.asset` 不進版控；該資產在 Resources 會被打包→`adminToken`（Server key）build 前必須清空」；BC-9；ADR-016 決策 7「加入 `.gitignore`」；LB16
- **事實**:
  - `Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset:16` `adminToken: ede2afa5…`（**非空**）；`:15` `apiKey: dev_…`。資產位於 `Resources/` 下 → 會被打包進 build（與 ADR-016 事實更正 1 一致）。
  - **全 repo 無根 `.gitignore`**（glob `**/.gitignore` 僅命中 `.opencode/.gitignore`、`.idea/.../.gitignore`；grep `LootLockerConfig` 於所有 `.gitignore` 零命中）→ ADR-016 決策 7 要求的「加入 `.gitignore`」**未落實**。
  - **無 pre-build guard**：grep `IPreprocessBuildWithReport` 於 `Assets/` 零命中（ADR-016 決策 7 標為「建議、非阻擋」，故未實作屬預期，但 SOP 僅文件化、無自動防線）。
  - 環境事實：repo 根 `.git/` 為**空目錄**（非有效版控）→「不進版控」目前**無受害者**（沒有任何東西被 commit）；且本輪未執行 build → `adminToken` 尚未洩漏。
- **影響**: 零受害者（無 git、未 build）→ 不擋驗收。但若未來初始化版控或執行 build 而忘記清空，Server key 會隨資產進版控／被打包進二進位（字串可提取）。屬真實安全隱患，需 SOP 落實。
- **建議方向**（僅供路由參考）: 補根 `.gitignore` 條目（`Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset*`）；build 前清空 `adminToken`；可選加 Editor pre-build guard（ADR-016 決策 7 建議）。

---

## Minors

### m1: session 失敗路徑未 log（FR-05/BC-3 要求 log）

- **type**: code ｜ **severity**: minor
- **事實**: `LoadAll` session 失敗分支（`LootLockerLeaderboardProvider.cs:52`）與 `Submit` session 失敗分支（:91-95）直接回呼快取／入佇列，**未 `Debug.Log`**；僅 `GetScoreList` 失敗分支有 log（:69）。FR-05「失敗→讀快取回呼（不報錯）＋log」、BC-3「走失敗路徑…；log」要求 session 失敗亦留痕。功能正確，屬可觀測性缺口。

### m2: EditMode 測試 SetUp/TearDown 刪除真實 `flipbird_leaderboard_cache`/`flipbird_leaderboard_pending`

- **type**: code ｜ **severity**: minor
- **事實**: `LootLockerLeaderboardProviderTests.cs:18-30` 於 `[SetUp]`/`[TearDown]` `PlayerPrefs.DeleteKey` 兩個**真實** key（provider 無 persistence seam，測試用真 PlayerPrefs）。在 Editor 執行測試會清掉 Editor 的真實線上快取與待送佇列（test-only/Editor-only，不影響 build；比照 round-2 m8 同類）。建議改「讀原值→刪→斷言→finally 還原」或測試專屬 key。

### m3: `LeaderboardProvider.cs` 介面註解仍描述本地實作語義（文件漂移）

- **type**: code ｜ **severity**: minor
- **事實**: 契約檔凍結不變，但註解已與線上實作不符——`:8`「未來異步實作若在背景執行緒回呼，實作類必須自行 marshal」（線上實作為 SDK 主執行緒保證＋斷言，非 marshal，ADR-016 決策 2）；`:9`「本地實作同步立即回呼」（本地實作已刪）；`:17`「插入→排序→截斷 10 筆→覆寫→回傳 SubmitResult（原子，FR-06）」（線上為伺服器權威，非本地插入排序）。屬文件漂移，不影響行為。

### m4: `LeaderboardRules.Qualifies` 假設 entries 已排序且長度 ≤10（round-1 m4 遺留）

- **type**: code ｜ **severity**: minor
- **事實**: `LeaderboardProvider.cs:51` 以 `entries[entries.Length - 1].score` 取「第 10 名」。線上實作下 `entries` 來自伺服器 `GetScoreList`（已排序、≤10），現行呼叫端保證成立；若伺服器回傳未排序或 >10 筆則判定會錯。屬契約外防禦缺口。

### m5: `FlushNext`/`RemovePending` 每項重讀 PlayerPrefs（O(n²)，n≤20 可忽略）

- **type**: code ｜ **severity**: minor
- **事實**: `FlushNext`（:181-195）逐項補送，每成功一項呼叫 `RemovePending`（:197-202）重新 `LoadPending()`＋`SavePending()`（全量 JSON 讀寫）。n≤20 時成本可忽略，屬效能風格。

---

## 逐條對照（AC／FR／NFR／BC／LB）

### 功能需求（FR）

| 條目 | 結果 | 證據 |
|------|------|------|
| FR-01 LootLocker 實作取代本地、自足 | PASS | `LootLockerLeaderboardProvider.cs:14`（`: LeaderboardProvider`）；session/cache/queue/memberId 全內部（:24-30） |
| FR-02 Guest Session lazy＋成功 flush | PASS | `EnsureSession`（:134-157）；`SystemInfo.deviceUniqueIdentifier`（:146）；成功→`FlushPending()`（:155）；失敗→失敗路徑（:52/:91-95） |
| FR-03 上榜守衛（僅過守衛才提交） | PASS | UI `LeaderboardRules.Qualifies`（`UIController.cs:220`）先於 modal/Submit；未過不呼叫 Submit |
| FR-04 Generic 唯一 member_id | PASS | `Guid.NewGuid().ToString("N")`（:87）；測試 `Submit_Consecutive_GeneratesUniqueMemberIds` 過 |
| FR-05 讀寫穿透快取 | PASS | `FetchAndCache`（:56-73）；cache key（:19）；成功寫快取（:63）；失敗讀快取（:70）；corrupt→空（:218-221）；測試 4 項過 |
| FR-06 提交失敗待送佇列（上限 20） | PASS | `EnqueueIfQualifies`（:161-169）；pending key（:20）；`while(pending.Count>MaxPending) RemoveAt(0)`（:167，FIFO drop-oldest）；排隊條件 `Qualifies(score, cached)`（:163） |
| FR-07 伺服器權威 SubmitResult | PASS | `SubmitToServer`（:101-128）；`Qualified = rank>=1 && rank<=10`（:124）；`Entries` 來自 `GetScoreList`（:113）；測試 `Submit_Success_DerivesQualifiedRankEntries`／`RankOutsideTop10` 過 |
| FR-08 flush 佇列 | PASS | `FlushPending`（:171-195）；`isFlushing` 防重入（:173）；逐項、沿用 memberId（:190）、成功移除失敗保留（:192）；測試 `SessionSuccess_FlushesPending_ReusesMemberId_RemovesOnSuccess`／`FlushFailure_KeepsPending` 過 |
| FR-09 組裝點 | PASS | `GameManager.cs:20` 型別 `LeaderboardProvider`；`:41` `new LootLockerLeaderboardProvider()`；accessor `:32`/`:35` 不變 |
| FR-10 刪除本地實作 | PASS | `LocalLeaderboardProvider` 全 Assets 零命中；`Assets/Scripts/Leaderboard/` 無該檔／.meta；舊 `LeaderboardTests.cs` 已移除（Tests/Editor 僅 3 檔） |
| FR-11 UI 最小硬化（修 m1） | PASS | `UIController.cs:327` `if (!result.Qualified) return;`（Submit callback 內、`PlayQualify` 前） |

### 非功能需求（NFR）

| 條目 | 結果 | 證據 |
|------|------|------|
| NFR-01 產品 UX 不變、失敗無感 | PASS（靜態） | UI 流程形狀不變（僅 :327 guard）；失敗走快取／佇列／空榜（:52/:70/:94/:108）；session 失敗缺 log（m1） |
| NFR-02 ScoreSession 零改動 | PASS | `ScoreSession.cs` 無 LootLocker 引用；`flipbird_high_score` 不變（:20）；Leaderboard 用 cache/pending 新 key |
| NFR-03 依賴方向單向 | PASS | provider using 僅 `System`/`System.Collections.Generic`/`UnityEngine`（:1-3）；grep `GameManager\|ScoreSession\|UIController` 於 Leaderboard/ 僅註解命中；SDK 僅 `LootLockerApi.cs` 引用 |
| NFR-04 介面契約不變＋callback 三點 | PASS | `LeaderboardProvider`/`SubmitResult`/`LeaderboardEntry`/`LeaderboardRules` 形狀不變；主執行緒（SDK 保證＋斷言，LB10）；同步回呼容忍（fake 同步測試全過）；Submit callback 內用 `GetScoreList` 非 `LoadAll`（:113） |
| NFR-05 換實作＝契約不變、組裝點、UI 最小硬化 | PASS | 介面不變；組裝點 2 行（:20/:41）；UI 僅 :327 |
| NFR-06 安全 | **PARTIAL（M2）** | `adminToken` 非空（config:16）；無根 `.gitignore`；無 pre-build guard |
| NFR-07 純 C#／零場景配置／不拋例外 | PASS | provider 無 MonoBehaviour；`LootLockerApi` 三方法皆 try/catch＋log（:23-35/:40-53/:58-75） |

### 邊界情況（BC）

| 條目 | 結果 | 證據 |
|------|------|------|
| BC-1 離線 LoadAll | PASS | session 失敗→`LoadCacheOrEmpty()`（:52）；GetScoreList 失敗→快取（:70）；皆敗→空（:210/:220）；測試 `LoadAll_NetworkFailure_ReadsCache`／`NetworkAndCacheFail`／`CorruptCache` 過 |
| BC-2 提交失敗排隊 | PASS | `EnqueueIfQualifies`（:107/:163）；測試 `Submit_Failure_Qualifies_Enqueues` 過 |
| BC-3 session 建立失敗 | PASS | 不阻塞不 crash（:52/:91-95）；lazy 下次再試（`sessionReady` 未設）；缺 log（m1） |
| BC-4 metadata 空字串 | PASS | UI 留空＝取消不 Submit（`UIController.cs:308-313`）；provider 若收到空 metadata 仍提交（測試 `Submit_EmptyMetadata_StillSubmits` 過） |
| BC-5 唯一 member_id 溢位 | PASS | GUID 衍生（:87），無溢位；測試連續相異過 |
| BC-6 客戶端/伺服器守衛競態 | PASS | `Qualified` 由伺服器 rank 決定（:124）；UI `:327` 檢 `result.Qualified` |
| BC-7 後端未設定 | PASS（靜態） | try/catch＋失敗路徑（:52/:70）；動態 D8 |
| BC-8 佇列滿 20 | PASS | FIFO drop-oldest（:167）；測試 `Submit_Failure_QueueCapsAt20_DropsOldest` 過（P6-P25） |
| BC-9 adminToken 洩漏 | **PARTIAL（M2）** | 見 M2 |

### 依賴規則（LB9-LB16）

| 條目 | 結果 | 證據 |
|------|------|------|
| LB9 SDK 呼叫隔離 | PASS | grep `LootLocker\.`（含點）於 `Assets/`：程式碼僅 `LootLockerApi.cs:3`（`using LootLocker.Requests`）＋註解；provider 只引用 `ILootLockerApi`（:24/:257）；seam 回傳 `bool`/`int`/`LeaderboardEntry[]`，零 SDK 型別洩漏 |
| LB10 主執行緒 marshal 契約 | PASS | `LootLockerApi` 建構捕捉 `ManagedThreadId`（:18）；`OnMainThread` 斷言（:79-84），不符→`LogError`＋丟棄 callback；無 dispatcher（符合 ADR-016 決策 2） |
| LB11 快取／佇列 key 分離 | PASS | cache（:19）／pending（:20）與 `flipbird_high_score`／`flipbird_player_name` 分離；舊 `flipbird_leaderboard` 退役（精確 grep `flipbird_leaderboard"` 零命中） |
| LB12 session 生命週期 | PASS | lazy（:134-157）；ready 旗標（:27/:149）；成功→flush（:155）；失敗→快取／排隊、下次再試 |
| LB13 待送佇列 | PASS | 入列條件（:163）；項 `{memberId,playerName,score}`（:166）；上限 20 FIFO（:167）；`isFlushing`（:173）；memberId 沿用（:190） |
| LB14 唯一 member_id | PASS | GUID（:87）；Arcade 每筆各自成列；補送沿用（:190） |
| LB15 伺服器權威 SubmitResult | PASS | `Qualified` 由 rank∈top10（:124）；`Rank` 來自 `SubmitScore`（:103）；`Entries` 來自 `GetScoreList`（:113）；永遠回呼（:83/:94/:108/:125） |
| LB16 安全 | **PARTIAL（M2）** | 見 M2 |

### 驗收標準（整體）

| 驗收標準 | 結果 |
|----------|------|
| 實作替換（FR-01/FR-10） | PASS |
| Guest Session（FR-02/BC-3） | PASS |
| 上榜守衛（FR-03） | PASS |
| Generic 唯一 member_id（FR-04/BC-5） | PASS |
| 讀寫穿透快取（FR-05/BC-1） | PASS |
| 待送佇列（FR-06/FR-08/BC-2/BC-8） | PASS |
| 伺服器權威 SubmitResult（FR-07/BC-6） | PASS（邊界 M1） |
| 組裝點（FR-09/NFR-04） | PASS |
| UI 最小硬化（FR-11/T4） | PASS |
| 契約三點（NFR-04/A1） | PASS |
| 依賴方向（NFR-03） | PASS |
| 契約不變（NFR-04） | PASS |
| ScoreSession 零改動（NFR-02） | PASS |
| 安全（NFR-06/BC-9） | **PARTIAL（M2）** |
| 測試（T3） | PASS（23/23） |

### 契約凍結核對

| 契約 | 結果 | 證據 |
|------|------|------|
| `LeaderboardProvider`/`SubmitResult`/`LeaderboardRules`/`LeaderboardData` | 不變 | `LeaderboardProvider.cs` 行號與 round-1 記載一致（:12-19/:25-37/:44-53/:59-63） |
| `LeaderboardEntry` | 不變 | `LeaderboardEntry.cs:7-12` |
| `PlayerNameStore` | 不變 | `PlayerNameStore.cs`（key `flipbird_player_name`、12 字元、null→空） |
| `LeaderboardAnimator` | 形狀不變 | `Render`/`PlayQualify`/`StopAll`（:57/:81/:133）；:51 `(true)` 為 round-2 B2 防禦硬化（已驗） |
| `ScoreSession` 零改動 | PASS | 無 LootLocker 引用；`flipbird_high_score` 照寫；`End()`/`RecordBroken` 不變 |
| `UITween` 零改動 | PASS | 未觸碰 |

### 前身缺陷 m1 修復確認

| 項 | 結果 | 證據 |
|----|------|------|
| `UIController.cs:327` guard 存在 | PASS | `if (!result.Qualified) return;`（Submit callback 內、`PlayQualify` 前） |
| 僅此一處 UI 改動 | PASS（語義級） | `ConfirmNameInput`（:303-331）其餘流程與 round-1 記載一致；無其他新增職責（方法限制：無 git diff） |

---

## 動態驗證清單（無法靜態驗證者）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | **前置**：Human 提供真 leaderboard key，替換 `LootLockerLeaderboardProvider.cs:17` `LeaderboardKey="flipbird"` 佔位 | 佔位值為已知前置依賴；未替換前線上榜單不可用（伺服器回 leaderboard not found → 走快取/空榜，不 crash） | 待填 |
| D2 | Play mode → Menu 點 LEADERBOARD（真 key＋後台已建榜） | Guest session 建立成功；`GetScoreList` 回真榜；畫面顯示伺服器榜單（非空） | 待填 |
| D3 | 遊玩得分 >0 → 撞死 → 上榜 → 輸入名稱 → CONFIRM | `SubmitScore` 回伺服器 `rank`；`GetScoreList` 回 `Entries`；`Qualified=rank∈top10`；動畫對**正確**條目滾分 | 待填 |
| D4 | 斷網 → Menu 點 LEADERBOARD | `LoadAll` 不報錯、不空畫面；讀 `flipbird_leaderboard_cache` 回上次成功榜；Console 有 `GetScoreList failed` warning | 待填 |
| D5 | 斷網 → 上榜提交 | 提交失敗且過守衛 → 寫入 `flipbird_leaderboard_pending`；UI 不懸置（回未上榜路徑） | 待填 |
| D6 | 恢復網路 → 觸發 LoadAll／重啟 session | session 成功後 flush 佇列；補送沿用原 memberId；成功後 pending 清空 | 待填 |
| D7 | 提交名稱（含 12 字元長名）→ 重新 LoadAll | metadata 名字往返正確顯示；12 字元未被拒（A2 metadata 長度上限） | 待填 |
| D8 | 重啟遊戲／重裝（同裝置） | `SystemInfo.deviceUniqueIdentifier` 保留身分（A3）；同一玩家 | 待填 |
| D9 | 清空 `LootLockerConfig.asset` `apiKey` → 觸發 LoadAll/Submit | 不 crash；走快取／空榜；Console 有 SDK 錯誤 log（BC-7） | 待填 |
| D10 | 全程觀察 Console | 無 `[LootLockerApi] … off the main thread; discarding`（LB10 斷言不 fire）；無遊戲 error | 待填 |
| D11 | 驗證伺服器 rank 語義 | 第一名 `rank==1`（1-based）；確認 `Qualified=rank>=1 && rank<=10` 正確 | 待填 |
| D12 | 驗證 `GetScoreList` 回傳順序 | `items` 依 rank 升冪（provider 未自行排序，依賴伺服器） | 待填 |
| D13 | **build 前**：檢查 `adminToken` 為空、`apiKey` 正確、資產未進版控 | NFR-06/BC-9 SOP 落實（M2） | 待填 |
| D14 | 提交失敗後**同一 session 內**再提交成功 | 佇列項最壞需等下次 session 建立才補送（ADR-016 已知限制）；確認無重複列 | 待填 |
| D15 | 佇列滿 20 後再失敗提交 | FIFO drop-oldest；不無限增長（BC-8） | 待填 |

---

## Statistics

- **blocker**: 0
- **major**: 2（M1 SubmitScore 成功/GetScoreList 失敗時 Entries 退回快取致動畫錯列；M2 安全 SOP／.gitignore 未落實）
- **minor**: 5（m1 session 失敗缺 log、m2 測試清真實 cache/pending、m3 介面註解漂移、m4 Qualifies 排序假設、m5 flush O(n²)）
- **動態驗證清單**: 15 項（D1-D15）

---

## 關鍵發現摘要

1. **測試全綠**：EditMode 23/23 passed（3 rules + 16 provider + 4 name），`TestResults.xml` 與 `Editor.log` 交叉證實；Console 零遊戲 error。**無 blocker**。
2. **LB9 SDK 隔離成立**：grep `LootLocker\.` 於 `Assets/` 程式碼僅 `LootLockerApi.cs` 命中；provider 只依賴 `ILootLockerApi` seam，零 SDK 型別洩漏。SDK 方法簽章（`StartGuestSession`/`SubmitScore`/`GetScoreList`）與回應欄位（`rank`/`metadata`/`items`）實查 SDK 原始碼吻合。
3. **契約凍結與刪除乾淨**：`LocalLeaderboardProvider` 全 Assets 零命中（含 .meta）；舊 key `flipbird_leaderboard` 精確零命中；`LeaderboardProvider`/`SubmitResult`/`LeaderboardEntry`/`LeaderboardRules`/`PlayerNameStore`/`LeaderboardAnimator` 形狀不變；`ScoreSession` 零改動。
4. **m1 修復確認**：`UIController.cs:327` guard 存在且為唯一 UI 改動（語義級核對）。
5. **M1（major）**：`SubmitScore` 成功但 `GetScoreList` 失敗時，`SubmitResult.Entries` 退回提交前快取（不含新條目），UI `PlayQualify` 以 rank 索引會對錯誤條目播動畫——契約「Entries＝提交後完整榜單」在邊界情況被違反（可自校正、不 crash）。
6. **M2（major）**：`adminToken`（Server key）現值非空且資產在 `Resources/` 會被打包；ADR-016 決策 7 的 `.gitignore` 條目未落實（全 repo 無根 `.gitignore`）、無 pre-build guard。零受害者（無 git、未 build）→ 不擋驗收，但屬真實安全隱患。
7. **動態前置依賴**：`LeaderboardKey="flipbird"` 為 Human 尚未提供真 key 的佔位（`LootLockerLeaderboardProvider.cs:16-17` TODO）——線上 LoadAll/Submit 在真 key 提供前無法驗證（D1）。
