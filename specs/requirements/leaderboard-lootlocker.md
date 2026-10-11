# 規格：排行榜 LootLocker 線上後端（Leaderboard LootLocker）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（新外部依賴 LootLocker SDK、網路 I/O、guest session 生命週期、離線讀寫穿透快取、提交失敗待送佇列、刪除 LocalLeaderboardProvider 含其測試、**NFR-05 修正**） ｜ Created: 2026-10-05
> 前身：`specs/requirements/leaderboard.md`（本地 PlayerPrefs 版，已完成）。本規格僅換後端實作，**產品 UX 與 `LeaderboardProvider` 介面契約不變**。
> 架構前身：`specs/architecture/leaderboard-system.md`（LB1-LB8）、`docs/adr/015-leaderboard.md`（決策 1-8）。

## 功能：排行榜 LootLocker 線上後端

- **目標**：把排行榜後端自本地 PlayerPrefs（`LocalLeaderboardProvider`）換成 **LootLocker 線上後端**——玩家以 **匿名 Guest Login** 建立 session；榜單採 **Generic（arcade 用法，每筆提交各自成列，同一玩家可多列）**；新增**離線讀寫穿透快取＋提交失敗待送佇列**，使網路不可用時 UI 對失敗無感。
- **性質**：**後端實作替換**（不新增產品功能、不改 UI 流程形狀）。新增單一自足 `LootLockerLeaderboardProvider`（session＋快取＋佇列＋SDK 包在 internal seam）；**刪除** `LocalLeaderboardProvider.cs` 含其測試；`GameManager.Awake` 換組裝點一行。**UI 僅允許既有流程最小硬化（修 validator m1：PlayQualify 前檢 `result.Qualified`）**。
- **不變**：`LeaderboardEntry` / `LeaderboardProvider` / `SubmitResult` / `LeaderboardRules` 形狀、`LeaderboardAnimator`、`PlayerNameStore`、`ScoreSession`（零改動）、`UITween`、`UIController` 除 T4 最小硬化外之流程。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/Leaderboard/LootLockerLeaderboardProvider.cs`（新） | **新建**（T1）：實作 `LeaderboardProvider`；內部 lazy guest session＋讀寫穿透快取＋提交失敗待送佇列＋唯一 member_id 產生器；SDK 呼叫封裝於 `internal ILootLockerApi` seam |
| Script | `Assets/Scripts/Leaderboard/LocalLeaderboardProvider.cs` | **刪除**（T2）：已被 LootLocker 實作取代（含 `IPersistence` seam 與 `PlayerPrefsPersistence`） |
| Script | `Assets/Tests/**/LocalLeaderboardProvider*Tests*.cs`（既有測試檔） | **刪除**（T3）：對應 `LocalLeaderboardProvider` 的 EditMode 測試（隨實作刪除） |
| Script | `Assets/Tests/**/LootLockerLeaderboardProviderTests.cs`（新） | **新建**（T3）：EditMode 測試——注入 fake `ILootLockerApi`，驗證快取穿透／待送佇列／唯一 member_id／Qualified 推導／session 失敗 |
| Script | `Assets/Scripts/GameManager.cs`（:37-46 Awake） | **修改**（T2）：組裝點一行 `new LocalLeaderboardProvider()` → `new LootLockerLeaderboardProvider()`（accessor 形狀不變） |
| Script | `Assets/Scripts/UI/UIController.cs` | **修改**（T4）：`PlayQualify` 前檢 `result.Qualified`（修 validator m1）；**僅最小硬化，其餘流程不變** |
| Script | `Assets/Scripts/Leaderboard/LeaderboardProvider.cs` | **不變**：`LeaderboardProvider`／`SubmitResult`／`LeaderboardRules`／`LeaderboardData` 形狀不變（介面契約凍結） |
| Script | `Assets/Scripts/Leaderboard/LeaderboardEntry.cs` | **不變**：`playerName`＋`score` 形狀不變 |
| Script | `Assets/Scripts/Leaderboard/PlayerNameStore.cs` | **不變**：名稱記憶 seam 照舊（名稱改由 metadata 帶出，見決策 3） |
| Script | `Assets/Scripts/UI/LeaderboardAnimator.cs` | **不變**：`Render`/`PlayQualify`/`StopAll` 契約不變 |
| Script | `Assets/Scripts/Score/ScoreSession.cs` | **不變**（零改動；`flipbird_high_score` 照寫，NFR-02） |
| Asset | `Assets/Resources/LootLockerConfig.asset` | **不進版控**（安全，BC-9）：`apiKey` 供客戶端；**`adminToken`（Server key）build 前必須清空** |
| Package | `lootlocker.lootlocker.sdk.runtime`（8.2.0，OpenUPM） | **沿用**：autoReferenced；Assets 無 asmdef，不需新增 |

## 現況診斷（已驗證事實，附出處）

1. **介面契約現況**：`LeaderboardProvider.cs:12-19` 介面＝`LoadAll(Action<LeaderboardEntry[]> cb)`＋`Submit(LeaderboardEntry entry, Action<SubmitResult> cb)`；`SubmitResult`（:25-37）＝`Qualified`／`Rank`（1-based，未上榜 -1）／`Entries`；`LeaderboardRules.Qualifies`（:44-53）純函式（<10 筆 → `score>0`；=10 筆 → `score>第10名`；同分不算；null/空陣列視為空榜單）；`LeaderboardData`（:59-63）JsonUtility wrapper。**本規格不動此形狀**。
2. **本地實作現況（刪除對象）**：`LocalLeaderboardProvider.cs:12` 實作介面；`:14-15` `MaxEntries=10`＋key `flipbird_leaderboard`；`:18-37` internal `IPersistence` seam＋`PlayerPrefsPersistence`；`:43-49` 建構 seam（正式 new PlayerPrefsPersistence／測試注入）；`:61-87` Submit（插入→`OrderByDescending` 穩定排序→`Take(10)`→寫回→算 rank）；`:90-107` LoadEntries（corrupt 回空陣列）。**刪除即移除本地持久化路徑**。
3. **組裝點現況**：`GameManager.cs:37-46` Awake——`:39` `scoreSession = new ScoreSession()`；`:41` `leaderboardProvider = new LocalLeaderboardProvider()`（ADR-015 決策 2：程式 new，否決 `[SerializeField] interface`）；`:43` `playerNameStore = new PlayerNameStore()`；`:45` `GameStateAuthority.Register(this)`。accessor：`:32` `Leaderboard => leaderboardProvider`、`:35` `PlayerName => playerNameStore`。**T2 僅改 :41 一行**。
4. **架構依賴方向（LB3）**：`specs/architecture/leaderboard-system.md:37` 依賴方向單向——Provider 不得反向引用遊戲邏輯/UI；`:104`（ADR-015 決策 8）Leaderboard 模組只依賴 UnityEngine＋System。**新 Provider 額外依賴 LootLocker SDK，仍不得引用 UI/GameManager/ScoreSession**。
5. **介面契約三點（ADR-015 決策 4／leaderboard-system.md §1:62-65）**：①callback 主執行緒保證（異步實作須自 marsh）②同步回呼容忍（callback 內執行顯示動作本身）③Submit callback 內禁再 LoadAll（`SubmitResult.Entries` 已含完整榜單）。**LootLocker 為 callback 式非同步——新實作必須自行滿足 ①（marshal）**。
6. **Qualify 判定現用點**：`leaderboard.md` FR-13／架構 §2:71-87——UI `OnStateChanged(GameOver)` → `LoadAll` → `LeaderboardRules.Qualifies(CurrentScore, entries)` → 上榜流程 → `Submit` → callback 動畫。**新後端下 `LeaderboardRules.Qualifies` 降為客戶端啟發式守衛**（伺服器權威，決策 5）。
7. **m1 缺陷（validator 既有報告）**：`UIController.PlayQualify` 呼叫前未檢 `result.Qualified`——在伺服器權威結果下（客戶端守衛通過但伺服器未上榜）必須先檢 `Qualified` 才播動畫（T4 修）。
8. **SDK 現況**：LootLocker SDK 8.2.0 已裝（OpenUPM），`lootlocker.lootlockersdk.runtime` autoReferenced；**Assets 無 asmdef，不需新增**；SDK static、callback 式、非 async/await；provider 可純 C#（不依賴 MonoBehaviour）。硬前置 Human 已完成：`LootLockerConfig.asset` 已填 `apiKey=dev_…`；後台已建 Generic leaderboard（metadata on、Game API writes on）、guest login 預設開。
9. **ScoreSession 深模組（ADR-008）**：`flipbird_high_score` 與 `flipbird_leaderboard` 兩 key 分離、互不覆寫（ADR-015 決策 8）；本功能**不觸碰 ScoreSession**（NFR-02）。

### 架構背景（ADR/架構出處）

- **ADR-015**（`docs/adr/015-leaderboard.md`）：決策 1（Provider 深模組）、決策 2（程式 new、否決 `[SerializeField] interface`）、決策 4（callback 三點）、決策 8（ScoreSession 零改動、兩 key 分離）——**本功能全部沿用，僅換實作類與組裝點**。
- **leaderboard-system.md**：LB1-LB8——LB2（介面契約三點）、LB3（依賴方向單向）、LB4（持久化隔離）、LB5（名稱 seam）——本功能不變更架構規則，新增「線上後端」實作細節（快取/佇列/session 生命週期）**需 Tech Lead 於 arch-review 階段審視並更新** `specs/architecture/leaderboard-system.md`（不在本 PM 規格範圍）。
- **NFR-05 修正**：原 `leaderboard.md:120`「未來 LootLocker 實作僅需新增實作類，呼叫端零改動」。**修正為**：介面契約不變、組裝點一行；**UI 僅允許既有流程最小硬化**——即 T4 修 m1（`PlayQualify` 前檢 `result.Qualified`），不得擴張 UI 職責。

### 未驗證假設（寫進規格，供 Developer 開工驗證）

- **A1（回呼執行緒）**：SDK callback 是否在主執行緒——**未知**。ADR-015 決策 4 要求實作自行 marshal；T1 須以「回呼佇列＋主執行緒 drain」防護（即使假設成立亦無害）。
- **A2（metadata 長度上限）**：官方未記載 metadata 長度上限；名稱上限 12 字元（沿用），T1 開工以實際 submit 驗證是否被拒。
- **A3（deviceUniqueIdentifier 穩定性）**：`SystemInfo.deviceUniqueIdentifier` 跨平台穩定性未在專案內驗證——重裝保留身分之目標依此假設；T1/T5 實測。
- **A4（SDK 錯誤語義）**：`SubmitScore`／`GetScoreList` 失敗回呼的成功旗標與錯誤欄位語義——T1 開工以 `unity_reflect`／實跑確認，佇列排隊條件依此判定。

## 已定案決策（Human 拍板，不可改）

> 以下九項由 Human 定案，本規格僅展開，不得新增/改動。

1. **讀寫穿透快取**：provider 內部 PlayerPrefs 快取上次成功榜；`LoadAll` 成功原樣寫回快取、失敗讀快取回呼（不報錯）；UI 對失敗無感（快取／空榜＋log）。
2. **`StartGuestSession(SystemInfo.deviceUniqueIdentifier, cb)`**（重裝保留身分）。
3. **沿用名稱 modal＋`PlayerNameStore`**；名字走 `SubmitScore(memberId, score, key, metadata, cb)` 的 **metadata** 參數（**非** `SetPlayerName`；generic 榜 player 為 null，metadata 是唯一名字來源）。
4. **Generic／Arcade**：每筆各自成列，同一玩家可多列。
5. **`LeaderboardRules.Qualifies` 降為客戶端啟發式守衛**；`SubmitResult{Qualified,Rank,Entries}` 由伺服器權威（`SubmitScore` 回 `rank`；再 `GetScoreList(key,10)` 取 `Entries`；`Qualified = rank∈top10`）。
6. **只有通過 top-10 守衛的「上榜局」才提交**（非每個 GameOver 都送）。
7. **新增單一自足 `LootLockerLeaderboardProvider : LeaderboardProvider`**（`Assets/Scripts/Leaderboard/`），內部 lazy guest session＋快取＋佇列＋SDK 包在 `internal ILootLockerApi` seam；**刪除 `LocalLeaderboardProvider.cs` 含其測試**。
8. **快取 key `flipbird_leaderboard_cache`；待送佇列 key `flipbird_leaderboard_pending`（JSON，上限 20）**；排隊條件＝提交失敗且 `Qualifies(score, cached)`；session 建立成功後 flush；佇列項 `{memberId, playerName, score}`（memberId 提交當下產生、補送沿用）。
9. **完整流程 PM→Tech Lead→Developer→Validator→Phase 4.5**。

## SDK 事實（寫進規格）

- LootLocker SDK 8.2.0 已裝（OpenUPM）；`lootlocker.lootlockersdk.runtime` autoReferenced；**Assets 無 asmdef，不需新增**。
- Arcade 每筆提交需**唯一 member_id**（沿用同一 id 只更新同一列）→ provider 需唯一 member_id 產生器（決策 7）。
- `SubmitScore→{member_id,rank,score}`；`GetScoreList(key,count,[after],cb)` items＝`LootLockerLeaderboardMember{member_id,rank,score,metadata,player}`；`GetMemberRank` 可用。
- SDK static、callback 式、非 async/await；provider 可純 C#。
- 未驗證假設：回呼是否主執行緒（ADR-015 決策 4 要求實作自行 marshal，A1）；metadata 長度上限官方未記載（A2）；`deviceUniqueIdentifier` 平台穩定性（A3）。
- 硬前置 Human 已完成：`LootLockerConfig.asset` 已填 `apiKey=dev_…`；後台已建 Generic leaderboard（metadata on、Game API writes on）、guest login 預設開。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 後端自 PlayerPrefs 換 LootLocker 線上 | FR-01／FR-09；T1/T2 |
| 匿名 Guest Login（重裝保留身分） | FR-02；T1 |
| Generic／Arcade（每筆各自成列、可多列） | FR-04／FR-07；T1 |
| 讀寫穿透快取（UI 對失敗無感） | FR-05／FR-06；T1 |
| 提交失敗待送佇列（上限 20、session 成功後 flush） | FR-06／FR-08；T1 |
| 伺服器權威 SubmitResult（Qualified/Rank/Entries） | FR-07；T1 |
| 只有上榜局才提交（top-10 守衛） | FR-03／FR-07；T1/T4 |
| 產品 UX 與介面契約不變 | NFR-01／NFR-03；T1/T2/T4 |
| `LeaderboardProvider` 介面契約不變、組裝點一行 | NFR-04；T2 |
| 刪除 LocalLeaderboardProvider 含測試 | FR-10；T2/T3 |
| 安全：adminToken 不進版控、build 前清空 | NFR-06／BC-9；T1（規格約束） |

## 功能需求（FR）

### A. Provider 與 Session（FR-01~FR-03）

- **FR-01 LootLocker 實作取代本地**：新建 `LootLockerLeaderboardProvider : LeaderboardProvider`（`Assets/Scripts/Leaderboard/`），行為語義與本地版一致（`LoadAll` 回已排序榜、`Submit` 回 `SubmitResult`）。**實作自足**：內部持有 session／快取／佇列／member_id 產生器，不拆多類（決策 7）。
- **FR-02 Guest Session**：首次需要網路時（`LoadAll`／`Submit` 觸發）**lazy** `StartGuestSession(SystemInfo.deviceUniqueIdentifier, cb)`；成功後標記 session ready；失敗→進入失敗路徑（見 FR-05／FR-06）。session 建立成功後 **flush 待送佇列**（FR-08）。重裝保留身分（A3）。
- **FR-03 上榜守衛**：**只有通過 top-10 守衛的「上榜局」才提交**（決策 6）——UI 以 `LeaderboardRules.Qualifies(CurrentScore, cachedEntries)` 判斷（客戶端啟發式守衛，決策 5）；未過守衛不呼叫 `Submit`。

### B. 快取與佇列（FR-04~FR-08）

- **FR-04 Generic 提交語義**：每次提交產生**唯一 member_id**（Arcade 每筆各自成列；沿用同一 id 只更新同一列，決策 4／SDK 事實）——member_id 產生器須保證唯一（時間戳／遞增序／GUID 衍生，見 BC-5）。
- **FR-05 讀寫穿透快取**：provider 內部以 PlayerPrefs key `flipbird_leaderboard_cache` 快取**上次成功榜**（JSON，`LeaderboardData` wrapper 慣例）。`LoadAll`：成功→原樣寫回快取＋回呼；失敗→讀快取回呼（不報錯）＋log；快取亦失敗/空→回**空榜單**（不 crash）。UI 對失敗無感（決策 1）。
- **FR-06 提交失敗待送佇列**：`Submit` 失敗且 `LeaderboardRules.Qualifies(score, cachedEntries)` 為真時，將 `{memberId, playerName, score}` 入佇列（PlayerPrefs key `flipbird_leaderboard_pending`，JSON）——**上限 20**（滿則丟棄最舊或拒絕新項，T1 定案並註明）；排隊條件（決策 8）與 memberId 沿用（補送沿用提交當下產生的 id）。
- **FR-07 伺服器權威 SubmitResult**：`Submit` 成功後——①`SubmitScore` 回 `rank`；②`GetScoreList(key, 10)` 取 `Entries`（轉 `LeaderboardEntry[]`：`playerName` 取 metadata）；③`Qualified = rank ∈ top10`；回呼 `SubmitResult{Qualified, Rank, Entries}`（決策 5）。**客戶端 `LeaderboardRules.Qualifies` 僅為提交前守衛，不決定 Qualified**。
- **FR-08 flush 佇列**：**session 建立成功後 flush**（FR-02）——逐項補送佇列（沿用項內 memberId）；成功移除、失敗保留（下次再試）；flush 期間避免重入（T1 以旗標防護）。

### C. 組裝、刪除與 UI 最小硬化（FR-09~FR-11）

- **FR-09 組裝點一行**：`GameManager.Awake`（:41）`new LocalLeaderboardProvider()` → `new LootLockerLeaderboardProvider()`；accessor 形狀不變（`:32`／`:35`）；`GameManager` 其餘零改動（NFR-04）。
- **FR-10 刪除本地實作**：刪除 `LocalLeaderboardProvider.cs`（含 `IPersistence` seam 與 `PlayerPrefsPersistence`）**及對應 EditMode 測試**（決策 7）。`LeaderboardProvider.cs`（介面／SubmitResult／Rules／Data）與 `LeaderboardEntry.cs`／`PlayerNameStore.cs` **保留不變**。
- **FR-11 UI 最小硬化（修 m1）**：`UIController.PlayQualify` 呼叫前檢 `result.Qualified`——false 則不播上榜動畫、維持既有顯示（未上榜路徑）。**僅此一處最小硬化，UI 其餘流程與職責不變**（NFR-05 修正）。

## 非功能需求（NFR）

- **NFR-01 產品 UX 不變**：使用者可見流程（GameOver→守衛→名稱 modal→提交→上榜動畫／未上榜）形狀不變；失敗（離線／session 失敗／提交失敗）對玩家無感（快取／佇列／空榜＋log）。
- **NFR-02 ScoreSession 零改動**：`ScoreSession`／`flipbird_high_score`／`End()`／`RecordBroken` 全部不變；Leaderboard 新 key（cache/pending）與 `flipbird_high_score` 分離、互不覆寫（ADR-015 決策 8）。
- **NFR-03 依賴方向單向**：UI → `GameManager.Leaderboard` → `LootLockerLeaderboardProvider` →（`internal ILootLockerApi`）→ LootLocker SDK。**Provider 不得反向引用 UI／GameManager／ScoreSession**（leaderboard-system.md LB3／ADR-015 決策 8）。SDK 呼叫一律封裝於 `ILootLockerApi` seam（測試可注入 fake）。
- **NFR-04 介面契約不變**：`LeaderboardProvider`／`SubmitResult`／`LeaderboardEntry`／`LeaderboardRules` 形狀凍結；callback 三點契約（ADR-015 決策 4）照舊——新實作**須自行 marshal 到主執行緒**（A1）、容忍同步回呼、Submit callback 內禁再 LoadAll。
- **NFR-05（修正）換實作＝介面契約不變、組裝點一行；UI 僅允許既有流程最小硬化（修 m1）**：取代原「呼叫端零改動」——因伺服器權威 `Qualified` 需 UI 於 PlayQualify 前檢核（T4），此為唯一允許的 UI 改動，不得擴張。
- **NFR-06 安全**：`LootLockerConfig.asset` **不進版控**；該資產在 Resources 會被打包→**`adminToken`（Server key）build 前必須清空**，客戶端只需 `apiKey`。
- **NFR-07 純 C#／零場景配置**：Provider 為純 C#（不依賴 MonoBehaviour），開工零場景接線（比照 ADR-015 決策 2 精神）；SDK 呼叫失敗不得拋例外中斷遊戲（try/catch＋log）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | LootLockerLeaderboardProvider（session＋ILootLockerApi seam＋快取＋佇列＋唯一 member_id） | P0 | gameplay-developer | **yes** | 實作 `LeaderboardProvider`，語義與本地版一致 | `Assets/Scripts/Leaderboard/LootLockerLeaderboardProvider.cs`（新） | 快取穿透／佇列／唯一 member_id／伺服器權威 SubmitResult／session lazy＋flush 全通過；契約三點滿足 |
| T2 | GameManager 換組裝點＋刪 LocalLeaderboardProvider | P0 | gameplay-developer | **yes** | Awake :41 換 `new LootLockerLeaderboardProvider()`；刪本地實作 | `Assets/Scripts/GameManager.cs`、`Assets/Scripts/Leaderboard/LocalLeaderboardProvider.cs`（刪） | `gameManager.Leaderboard` 可 LoadAll/Submit；GameManager 其餘零改動；本地檔與殘留引用零命中 |
| T3 | EditMode 測試（fake ILootLockerApi）＋刪舊測試 | P0 | gameplay-developer | no | 驗證快取／佇列／member_id／Qualified 推導／session 失敗；刪除 Local 測試 | `Assets/Tests/**`（新＋刪） | 新測試全綠；Local 測試檔移除；無孤立測試引用 |
| T4 | UIController.PlayQualify 前檢 result.Qualified（修 m1） | P0 | scene-developer | no | 伺服器權威結果下僅 Qualified 才播動畫 | `Assets/Scripts/UI/UIController.cs` | Qualified=false 不播上榜動畫、走未上榜路徑；其餘流程零改動 |
| T5 | 驗收＋Phase 4.5 動態驗證清單 | P0 | validator | no | 對照本規格挑缺陷＋列動態驗證清單 | `specs/validation/leaderboard-lootlocker/round-N.md` | 缺陷標 type×severity；動態清單含步驟＋預期觀察 |

### T1 — LootLockerLeaderboardProvider（P0, gameplay-developer, arch-review: yes）
- **Goal**：單一自足實作類——lazy guest session＋讀寫穿透快取＋提交失敗待送佇列＋唯一 member_id；SDK 包在 `internal ILootLockerApi` seam（決策 7）。
- **作法**：
  - 新建 `Assets/Scripts/Leaderboard/LootLockerLeaderboardProvider.cs`：`public class LootLockerLeaderboardProvider : LeaderboardProvider`。
  - `internal interface ILootLockerApi`（seam，NFR-03）：暴露 `StartGuestSession(string deviceId, Action<bool> cb)`／`SubmitScore(string memberId, int score, string key, string metadata, Action<int> cb)`（cb 帶 rank；失敗 rank<0）／`GetScoreList(string key, int count, Action<LeaderboardEntry[]> cb)` 等；正式實作包 SDK static 呼叫。
  - **session**（FR-02）：lazy——首個需要網路之操作觸發 `StartGuestSession(SystemInfo.deviceUniqueIdentifier, cb)`；ready 旗標；成功→`FlushPending()`（FR-08）。
  - **快取**（FR-05）：PlayerPrefs key `flipbird_leaderboard_cache`；`LoadAll` 成功寫回、失敗讀取、皆敗回空榜（不 crash）。
  - **佇列**（FR-06）：PlayerPrefs key `flipbird_leaderboard_pending`（JSON，上限 20）；項 `{memberId, playerName, score}`；排隊條件＝提交失敗且 `LeaderboardRules.Qualifies(score, cached)`；補送沿用 memberId。
  - **member_id 產生器**（FR-04/BC-5）：唯一性保證（如時間戳＋遞增序；避免溢位）。
  - **伺服器權威 SubmitResult**（FR-07）：`SubmitScore` 取 rank → `GetScoreList(key,10)` 取 Entries → `Qualified = rank∈top10` → 回呼。
  - **marshal**（NFR-04/A1）：若 SDK 回呼非主執行緒，以回呼佇列＋主執行緒 drain（本類為純 C#——drain 須由呼叫端每幀驅動或 SDK 保證主執行緒；T1 先 `unity_reflect`／實測確認，並註明選型）。
  - **開工先驗證** A1/A2/A3/A4（未驗證假設）——`unity_reflect` 查 SDK 型別／實跑一支 submit。
- **驗收**：`LoadAll` 成功回榜＋寫快取；失敗回快取（不報錯）；快取空回空榜。`Submit` 成功回伺服器權威結果；失敗且過守衛→入佇列（上限 20）；session 成功→flush。唯一 member_id（連續提交產生不同 id）。契約三點滿足（主執行緒／同步容忍／Submit callback 禁再 LoadAll 之使用者）。`internal ILootLockerApi` 可注入 fake（T3 用）。

### T2 — GameManager 換組裝點＋刪 LocalLeaderboardProvider（P0, gameplay-developer, arch-review: yes）
- **Goal**：組裝點一行替換；刪除本地實作（不留下任何引用）。
- **作法**：
  - `GameManager.cs:41` 改 `leaderboardProvider = new LootLockerLeaderboardProvider();`；accessor（:32）與其餘 Awake 行（:39/:43/:45）**零改動**。
  - 刪 `Assets/Scripts/Leaderboard/LocalLeaderboardProvider.cs`。
  - grep 殘留引用（`LocalLeaderboardProvider`、`flipbird_leaderboard`）確認零命中（`flipbird_leaderboard_cache`／`flipbird_leaderboard_pending` 為新 key，不誤判）。
- **驗收**：編譯乾淨；`gameManager.Leaderboard` 可 LoadAll/Submit；`GameManager` 其餘行為零回歸；`LocalLeaderboardProvider` 全專案零命中。

### T3 — EditMode 測試（fake ILootLockerApi）＋刪舊測試（P0, gameplay-developer, arch-review: no）
- **Goal**：以 fake `ILootLockerApi` 覆蓋新 provider 邊界；刪除 Local 測試。
- **作法**：注入 fake（成功／失敗／空榜／corrupt 快取）；測試：`LoadAll` 成功寫快取、失敗讀快取、皆敗回空榜；`Submit` 成功 Qualified/Rank/Entries 推導（rank∈top10）；`Submit` 失敗＋過守衛→入佇列、上限 20；session 成功→flush（memberId 沿用）；唯一 member_id 連續產生相異；session 失敗路徑；metadata 空字串行為（BC-4）。刪除 `LocalLeaderboardProvider*Tests*.cs`。
- **驗收**：新測試全綠、涵蓋上述案例；Local 測試檔移除、無孤立引用；測試不碰真網路（純 fake）。

### T4 — UIController.PlayQualify 前檢 result.Qualified（P0, scene-developer, arch-review: no）
- **Goal**：修 validator m1——伺服器權威結果下僅 `Qualified` 才播上榜動畫。
- **作法**：`Submit` callback 內呼叫 `LeaderboardAnimator.PlayQualify(result)` **前**加 `if (!result.Qualified) { /* 未上榜路徑，維持既有顯示 */ return; }`。**僅此最小硬化**；不新增 UI 職責、不改流程形狀。
- **驗收**：`Qualified=true` 正常播上榜動畫；`Qualified=false` 不播、維持未上榜顯示；其餘 UI 流程零改動。

### T5 — 驗收＋Phase 4.5 動態驗證清單（P0, validator, arch-review: no）
- **Goal**：對照本規格靜態挑缺陷，並列無法靜態驗證之動態清單。
- **作法**：對照 FR/NFR/BC 逐條；缺陷標 `type(code|architecture|requirement) × severity(blocker|major|minor)`；**靜態無法驗證者列「動態驗證清單」**（步驟＋預期觀察）——離線 LoadAll、線上 Submit 上榜、提交失敗排隊與後續 flush、session 建立失敗、metadata 空字串（BC-4）。
- **驗收**：缺陷報告落 `specs/validation/leaderboard-lootlocker/round-N.md`；動態清單可執行；回填前 Verdict 暫定。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 離線 LoadAll | 無網路／session 失敗——`LoadAll` 不得報錯、不得空畫面 | FR-05：讀快取回呼；快取空回空榜＋log；UI 無感（決策 1） |
| BC-2 提交失敗排隊 | `Submit` 失敗且分數過守衛——不得丟失玩家上榜機會 | FR-06：入佇列（上限 20）；session 成功後 flush（FR-08） |
| BC-3 session 建立失敗 | `StartGuestSession` 失敗——不得阻塞 UI／不得 crash | FR-02/FR-05/FR-06：走失敗路徑（讀快取／排隊）；log；下次操作再試（lazy） |
| BC-4 metadata 空字串 | 名稱留空（modal 留空＝取消，沿用 BC-4 上游）——metadata 空字串不得以空名上榜／不得 crash | `leaderboard.md` BC-4：**留空按確認＝取消＝丟棄不上榜**（上游已擋，不呼叫 Submit）；若仍送空 metadata，T1 以預設名或拒絕處理（T1 定案註明） |
| BC-5 唯一 member_id 溢位 | member_id 產生器長期執行／溢位——不得重複（重複會更新同一列而非新增） | FR-04：產生器唯一性保證（時間戳＋遞增序／GUID 衍生；T1 防溢位設計）；EditMode 測試連續產生相異 |
| BC-6 客戶端/伺服器守衛競態 | 客戶端 `Qualifies` 通過但伺服器回 `Qualified=false`（榜單已變） | FR-07：**伺服器權威**——`Qualified` 由伺服器 rank 決定；T4：`PlayQualify` 前檢 `result.Qualified`（修 m1） |
| BC-7 後端未設定 | `LootLockerConfig` apiKey 未填／後台 leaderboard 未建——不得 crash | NFR-07：SDK 例外 try/catch＋log；走失敗路徑（讀快取／空榜）；硬前置 Human 已完成（apiKey／後台） |
| BC-8 佇列滿 20 | 連續失敗累積超過 20 項 | FR-06：上限 20（滿則丟棄最舊或拒絕新項，T1 定案）；不無限增長 |
| BC-9 adminToken 洩漏 | `LootLockerConfig.asset` 進版控／含 Server key 被打包 | NFR-06：資產不進版控；**build 前清空 adminToken**；客戶端只需 apiKey |

## 驗收標準（整體，Validator 對照用）

- [ ] **實作替換**：`LootLockerLeaderboardProvider : LeaderboardProvider` 存在且語義與本地版一致；`LocalLeaderboardProvider` 及其測試已刪、全專案零殘留引用（FR-01/FR-10）
- [ ] **Guest Session**：lazy `StartGuestSession(SystemInfo.deviceUniqueIdentifier, cb)`；成功後 flush 佇列；失敗不阻塞不 crash（FR-02/BC-3）
- [ ] **上榜守衛**：僅通過 `LeaderboardRules.Qualifies` 的局才提交（FR-03）
- [ ] **Generic 唯一 member_id**：每次提交產生唯一 id（連續相異）；Arcade 每筆各自成列（FR-04/BC-5）
- [ ] **讀寫穿透快取**：成功寫 `flipbird_leaderboard_cache`＋回榜；失敗讀快取（不報錯）；皆敗回空榜；UI 無感（FR-05/BC-1）
- [ ] **待送佇列**：失敗且過守衛入 `flipbird_leaderboard_pending`（上限 20）；session 成功後 flush；memberId 沿用（FR-06/FR-08/BC-2/BC-8）
- [ ] **伺服器權威 SubmitResult**：`Qualified` 由伺服器 rank∈top10；`Rank` 來自 `SubmitScore`；`Entries` 來自 `GetScoreList(key,10)`（FR-07/BC-6）
- [ ] **組裝點**：`GameManager.Awake` 一行替換；accessor 形狀不變；其餘零改動（FR-09/NFR-04）
- [ ] **UI 最小硬化**：`PlayQualify` 前檢 `result.Qualified`（修 m1）；`Qualified=false` 不播動畫（FR-11/T4）
- [ ] **契約三點**：主執行緒保證（marshal）／同步回呼容忍／Submit callback 禁再 LoadAll（NFR-04/A1）
- [ ] **依賴方向**：UI→GameManager.Leaderboard→Provider→ILootLockerApi→SDK 單向；Provider 不引用 UI/GameManager/ScoreSession（NFR-03）
- [ ] **契約不變**：`LeaderboardProvider`/`SubmitResult`/`LeaderboardEntry`/`LeaderboardRules`/`PlayerNameStore`/`LeaderboardAnimator` 形狀不變（NFR-04）
- [ ] **ScoreSession 零改動**：`flipbird_high_score` 照寫；新 key 分離互不覆寫（NFR-02）
- [ ] **安全**：`LootLockerConfig.asset` 不進版控；adminToken build 前清空（NFR-06/BC-9）
- [ ] **測試**：新 EditMode 測試全綠（fake ILootLockerApi）；Local 測試移除（T3）

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（LeaderboardProvider.cs:12-19/25-37/44-53/59-63、LocalLeaderboardProvider.cs:12/14-15/18-37/43-49/61-87/90-107、GameManager.cs:32/35/37-46/:41、leaderboard-system.md:37/62-65/104、ADR-015 決策 1/2/4/8、leaderboard.md:120（NFR-05 原版）、SDK 事實）。
- **未驗證假設**：A1（回呼執行緒——marshal 防護）、A2（metadata 上限——name ≤12 實測）、A3（deviceUniqueIdentifier 穩定性——實測）、A4（SDK 錯誤語義——T1 確認）。
- **一致性**：BC-1↔FR-05；BC-2/BC-8↔FR-06/FR-08；BC-3↔FR-02；BC-4↔leaderboard.md BC-4（上游擋）；BC-5↔FR-04；BC-6↔FR-07/T4；BC-7↔NFR-07；BC-9↔NFR-06；決策 1-9 全數落實於 FR/NFR/T 表。
- **邊界涵蓋**：離線 LoadAll（BC-1）、提交失敗排隊（BC-2）、session 失敗（BC-3）、客戶端/伺服器守衛競態（BC-6）、唯一 member_id 溢位（BC-5）、metadata 空字串（BC-4）、後端未設定（BC-7）——全數對應防護。
- **Editor API 驗證**：T1 開工 `unity_reflect` 驗證 LootLocker SDK 型別／方法簽章（`SubmitScore`／`GetScoreList`／`StartGuestSession`）與 A1-A4；無 Editor API 依賴。
- **禁區檢查**：本規格未改 `specs/architecture/**`、`docs/adr/**`、`Assets/**`、`specs/requirements/leaderboard.md`；架構更新留待 arch-review 階段由 Tech Lead 執行。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**（待 Tech Lead 完成審查）。範圍：

1. **新外部依賴**：LootLocker SDK 8.2.0（`lootlocker.lootlockersdk.runtime`，autoReferenced）——Provider 新增對外部 SDK 的依賴；需 Tech Lead 審視 `internal ILootLockerApi` seam 是否足夠隔離（NFR-03）、依賴方向是否維持單向（UI→GameManager.Leaderboard→Provider→ILootLockerApi→SDK）。
2. **網路 I/O 與 session 生命週期**：guest session lazy 建立、重裝保留身分（deviceUniqueIdentifier）、失敗路徑——需確認與 callback 三點契約（ADR-015 決策 4）相容，尤其主執行緒 marshal（A1）。
3. **離線讀寫穿透快取**：新 key `flipbird_leaderboard_cache`——需確認持久化模型擴張與 `flipbird_high_score`／`flipbird_leaderboard` 分離（ADR-015 決策 8）。
4. **提交失敗待送佇列**：新 key `flipbird_leaderboard_pending`（上限 20）——需確認佇列生命週期、flush 時機（session 成功）、上限策略。
5. **刪除 LocalLeaderboardProvider 含測試**：移除本地持久化路徑——需確認無殘留引用、`LeaderboardProvider` 契約仍凍結。
6. **NFR-05 修正**：原「呼叫端零改動」修正為「介面契約不變、組裝點一行；UI 僅允許既有流程最小硬化（修 m1）」——需 Tech Lead 認可並更新 `specs/architecture/leaderboard-system.md` 與相關 ADR。
