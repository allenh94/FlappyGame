# ADR-016: Leaderboard 線上後端（LootLocker）——SDK 隔離 seam、主執行緒 marshal 裁定、快取／佇列持久化擴張

## Status

Accepted

## Context

`specs/requirements/leaderboard-lootlocker.md`（arch-review=yes）把排行榜後端自本地 PlayerPrefs（`LocalLeaderboardProvider`）換成 LootLocker 線上後端。前身 ADR-015（本地版）之 8 決策全部沿用，僅換實作類與組裝點。本 ADR 涵蓋 PM §arch-review 判定 6 點＋安全第 7 點，並補齊實作層約束。

**觸發架構變更的 7 點**：①新外部依賴 LootLocker SDK 8.2.0 與 `internal ILootLockerApi` seam ②網路 I/O 與 guest session 生命週期＋主執行緒 marshal（A1）③離線讀寫穿透快取（新 key）④提交失敗待送佇列（新 key，上限 20）⑤刪除 `LocalLeaderboardProvider` 含測試 ⑥NFR-05 修正 ⑦安全（adminToken）。

**規格事實主張核對**：逐行實讀 `LeaderboardProvider.cs`／`LocalLeaderboardProvider.cs`／`PlayerNameStore.cs`／`GameManager.cs`／`UIController.cs`／`LootLockerConfig.asset`／SDK 原始碼（`LootLockerHTTPClient.cs`／`LootLockerLifecycleManager.cs`／`LootLockerHTTPExecutionQueueItem.cs`／`LeaderboardRequest.cs`／`LootLockerSDKManager.cs`／`LootLockerConfig.cs`）。**發現 2 處規格事實錯誤**（見「對 pm 規格的修訂建議」1/2）。

### 事實更正 1：config 資產路徑

PM 規格「涉及物件」寫 `Assets/Resources/LootLockerConfig.asset`——**實際路徑為 `Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset`**（實查 glob 確認）。該資產在 `Resources/` 下**會被打包進 build**。

### 事實更正 2：T2 不是「一行」

PM 規格 T2 稱「`GameManager.cs:41` 換一行、其餘零改動」。實讀 `GameManager.cs:20` 為 `private LocalLeaderboardProvider leaderboardProvider;`——**欄位型別是具體類**。刪除 `LocalLeaderboardProvider` 後此行無法編譯，**T2 至少改 2 行**：`:20` 型別改 `LeaderboardProvider`（介面）＋`:41` `new LootLockerLeaderboardProvider()`。accessor（`:32`）已是介面型別，不變。

### 事實更正 3（安全現況）

`LootLockerConfig.asset` 現值：`apiKey=dev_…`（已填）、**`adminToken`（Server API key）已填**、`domainKey` 已填、`game_version=1.0.0.1`、`multiUserSessionMode=2`（＝`SingleSession`，單機單人正確）。adminToken 雖僅在 `#if UNITY_EDITOR` 分支被送出（`LootLockerHTTPClient.cs:913-918`），但資產在 `Resources/` 會被**打包進 build**，字串可被提取——build 前必須清空。

### 主執行緒 marshal 的實查證據（A1 裁定依據）

SDK 8.2.0 的 HTTP 路徑**完全由 Unity 主執行緒生命週期驅動**：

1. `LootLockerHTTPClient : MonoBehaviour`（`LootLockerHTTPClient.cs:138`），掛在 `LootLockerLifecycleManager` 建立的 GameObject 上（`LootLockerLifecycleManager.cs:120-132`，`DontDestroyOnLoad`，`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 自動建立）。
2. `ScheduleRequest` → `StartCoroutine(_ScheduleRequest(request))`（`:553`）——coroutine，主執行緒。
3. `Update()`（`:392`）逐幀處理執行佇列：`CreateAndSendRequest` 呼叫 `UnityWebRequest.SendWebRequest()`（`:612`，async operation 每幀輪詢）→ `ProcessOngoingRequest` → `HandleRequestResult` → `CallListenersAndMarkDone`（`:658-720`）。
4. `LateUpdate()`（`:487`）亦呼叫 `CallListenersAndMarkDone`。
5. `CallListenersAndMarkDone`（`:722`）→ `executionItem.RequestData.CallListenersWithResult(response)`（`:763`）——**此處呼叫使用者 callback**。
6. session refresh 為 coroutine（`RefreshSession`，`:766`，`yield return new WaitUntil(...)`）。
7. 全 Runtime 掃描 `Task.Run`／`new Thread`／`ThreadPool`／`async`／`await`：**HTTP／leaderboard 路徑零命中**；唯一 `System.Threading.Tasks` 使用在 `LootLockerPresenceClient.cs`（Presence websocket 功能，config `enablePresence: 0` 已停用，不在 leaderboard 路徑）。
8. `LootLockerResponse.Deserialize`（`LootLockerResponse.cs:63`）在 callback 鏈內同步執行。

**結論：SDK callback 在主執行緒呼叫（8.2.0 實證）。**

## Decision

### 決策 1：`internal ILootLockerApi` seam 足夠隔離 SDK；依賴方向維持單向

**裁定：seam 成立，但形狀明確化為「顯式成功旗標」**（取代 PM 的 `Action<int>` rank<0 sentinel）：

```csharp
internal interface ILootLockerApi
{
    void StartGuestSession(string deviceId, Action<bool> onComplete);
    void SubmitScore(string memberId, int score, string leaderboardKey, string metadata,
                     Action<bool, int> onComplete);                 // success, rank
    void GetScoreList(string leaderboardKey, int count,
                      Action<bool, LeaderboardEntry[]> onComplete); // success, entries
}
```

- **隔離充分性**：seam 只暴露 provider 需要的三個操作（session／submit／list），回傳型別為 `bool`＋`int`＋`LeaderboardEntry[]`（領域型別），**零 LootLocker 型別洩漏**。provider 邏輯（session／快取／佇列／memberId）完全不觸碰 SDK。
- **SDK 命名空間收斂**：**只有 concrete adapter（`LootLockerApi`）可引用 `LootLocker.*`**；provider 只引用 `ILootLockerApi`。adapter 負責 SDK 回應 → 領域型別映射（`metadata`→`playerName`、`items`→`LeaderboardEntry[]`）。
- **依賴方向**：`UI → GameManager.Leaderboard → LootLockerLeaderboardProvider → ILootLockerApi → LootLocker SDK` 單向。Provider 不得引用 UI／GameManager／ScoreSession（LB3 不變）。
- **顯式成功旗標的理由**：A4（SDK 錯誤語義未驗證）——`Action<int>` 以 rank<0 表失敗會與「未上榜 rank=-1」語義混淆；`Action<bool,int>` 讓失敗與未上榜可區分，佇列排隊條件（FR-06）判定明確。

### 決策 2：主執行緒 marshal——**不需 dispatcher；以 SDK 主執行緒保證＋防禦性斷言＋版本鎖定**

**裁定：不引入 dispatcher。** 依據 Context 的實查證據，SDK 8.2.0 callback 在主執行緒呼叫，provider 為純 C# 亦無需自行 marshal。

**為何否決 dispatcher 方案**：

- **GameManager.Update 驅動 main-thread queue**：需在 `GameManager.Update` 加 `leaderboardProvider.Pump()`——違反 NFR-04「GameManager 其餘零改動」（PM 明訂僅組裝點變更）。
- **隱藏 MonoBehaviour runner**：provider 需在執行期建立 GameObject／掛 runner——違反 NFR-07「純 C#／零場景配置」。
- 兩者皆為「SDK 保證已成立」下的多餘複雜度。

**防禦性契約（可實作、明確）**：

1. concrete `LootLockerApi` 於建構時（`GameManager.Awake`，主執行緒）捕捉 `Thread.CurrentThread.ManagedThreadId`。
2. 每個 SDK callback 入口斷言 `Thread.CurrentThread.ManagedThreadId == capturedId`；不符→`Debug.LogError`＋**丟棄該 callback**（絕不在非主執行緒呼叫 UI callback）。
3. **版本鎖定**：SDK 8.2.0。SDK 升級為演進觸發條件——升級後必須重驗主執行緒保證；若保證失效，斷言會 fire，屆時再引入 dispatcher（見「演進觸發條件」）。

> 此裁定使 provider 維持純 C#、GameManager 維持單一組裝點、且對 SDK 行為漂移有可觀測的防線。

### 決策 3：持久化擴張與舊 key 退役

| key | 擁有者 | 處置 |
|-----|--------|------|
| `flipbird_high_score` | ScoreSession | **不變**（NFR-02） |
| `flipbird_player_name` | PlayerNameStore | **不變**（LB5） |
| `flipbird_leaderboard_cache` | LootLockerLeaderboardProvider | **新增**：上次成功榜（JSON，`LeaderboardData` wrapper） |
| `flipbird_leaderboard_pending` | LootLockerLeaderboardProvider | **新增**：待送佇列（JSON wrapper，上限 20） |
| `flipbird_leaderboard` | （舊 LocalLeaderboardProvider） | **退役**：不讀、不寫、不遷移 |

- **舊 key 不遷移的理由**：`flipbird_leaderboard` 是本地未經伺服器驗證的資料；遷移會把本地分數注入線上榜，違反決策 5「伺服器權威」。故**只退役、不遷移**。
- **不主動刪除**：`PlayerPrefs.DeleteKey("flipbird_leaderboard")` 非必要且屬破壞性操作；保留為惰性孤兒資料（無害）。清理列為未來可選。
- **四 key 互不覆寫**：cache／pending 與 high_score／player_name 完全分離（NFR-02）。

### 決策 4：待送佇列生命週期

- **入列**：`Submit` 失敗且 `LeaderboardRules.Qualifies(score, cachedEntries)` 為真（決策 8）。
- **項**：`{memberId, playerName, score}`（`[Serializable]` wrapper，JsonUtility 不支援頂層裸陣列）。
- **上限 20**：**FIFO 丟棄最舊**（drop-oldest）——確定性、有界、保留最近嘗試；佇列項皆已過守衛，丟最舊不損失「更高分」語義（Arcade 每筆獨立）。
- **flush 時機**：
  - **規範觸發**：session-ready 轉移（FR-02/FR-08）。
  - **建議附加**：session ready 且佇列非空時，於**成功 `LoadAll` 之後**機會性 flush（不在 `Submit` callback 內，避免巢狀）。
- **防重入**：`isFlushing` 旗標；flush 逐項序列補送，成功移除、失敗保留（下次再試）。
- **memberId 沿用**：提交當下產生、補送沿用同一 id（Arcade 同 id 更新同列——沿用避免補送產生重複列）。
- **provider 永遠回呼**：`Submit` 失敗仍回 `SubmitResult(false, -1, cachedEntries)`；`LoadAll` 失敗回快取或空榜——**UI 永不懸置**。

### 決策 5：刪除 `LocalLeaderboardProvider` 含測試；契約凍結

- 刪 `LocalLeaderboardProvider.cs`（含 `IPersistence`／`PlayerPrefsPersistence`）＋對應 EditMode 測試。
- **殘留引用檢查**：grep `LocalLeaderboardProvider` 全專案零命中；grep `flipbird_leaderboard`（精確，排除 `_cache`／`_pending`）零命中。
- **`LeaderboardProvider` 契約凍結**：`LeaderboardProvider`／`SubmitResult`／`LeaderboardEntry`／`LeaderboardRules`／`LeaderboardData` 形狀不變（NFR-04）。
- **`GameManager.cs:20` 欄位型別**改 `LeaderboardProvider`（見事實更正 2）。

### 決策 6：NFR-05 修正認可

原「呼叫端零改動」→ **「介面契約不變、組裝點變更；UI 僅允許既有流程最小硬化（修 m1）」**。認可理由：伺服器權威 `Qualified` 需 UI 於 `PlayQualify` 前檢核（T4），此為唯一允許的 UI 改動。`leaderboard-system.md` §1 措辭同步更新。

> 註：因事實更正 2，「組裝點一行」精確為「組裝點 2 行（欄位型別＋new）」。

### 決策 7：安全——config 不進版控、adminToken build 前清空

- **不進版控**：`Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset`（含 `.meta`）加入 `.gitignore`。理由：資產在 `Resources/` 會被**打包進 build**，adminToken 字串可被提取（即使 `#if UNITY_EDITOR` 不送出，字串仍在二進位內）。
- **build 前清空 adminToken**：客戶端只需 `apiKey`；`adminToken` 為 Server key，build 前必須設為空字串。
- **SOP（強制）**：build 前檢查清單——①確認 `adminToken` 為空 ②確認 `apiKey` 為 dev/prod 正確值 ③確認資產未被 commit。
- **自動化（建議、非阻擋）**：可加 Editor build preprocessor（`IPreprocessBuildWithReport`）於 `adminToken` 非空時 fail build。此為新增 Editor 腳本，不在 PM 任務清單，列後續硬化。
- **`multiUserSessionMode=2`（SingleSession）**：單機單人正確，無需變更。

## Consequences

- 正面：SDK 完全隔離於 `ILootLockerApi` seam，provider 純 C#、可注入 fake 測試（T3）；依賴方向單向維持。
- 正面：主執行緒 marshal 以實證裁定「不需 dispatcher」，避免 GameManager 額外改動與隱藏 MonoBehaviour，符合 NFR-04/NFR-07。
- 正面：防禦性斷言＋版本鎖定使 SDK 行為漂移可觀測。
- 正面：快取／佇列使離線讀寫對 UI 無感（NFR-01）；伺服器權威 SubmitResult 修正客戶端守衛競態（BC-6）。
- 負面：新增 2 個 PlayerPrefs key；provider 內部狀態（session／cache／queue／memberId）集中於單一類，行數較本地版多。
- 負面：flush 僅在 session-ready 轉移（＋機會性 LoadAll 後）觸發——session 內提交失敗的項，最壞需等下次 session 建立才補送（已列已知限制）。
- 負面：`flipbird_leaderboard` 成為惰性孤兒資料（無害）。
- 被放棄的替代方案：dispatcher（GameManager.Update pump／隱藏 runner）——見決策 2；`Action<int>` sentinel seam——見決策 1；舊 key 遷移——見決策 3。

## 對 pm 規格的修訂建議

1. **config 路徑更正**：`Assets/Resources/LootLockerConfig.asset` → `Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset`（涉及物件表）。
2. **T2 改 2 行**：`GameManager.cs:20` 欄位型別 `LocalLeaderboardProvider` → `LeaderboardProvider`；`:41` `new LootLockerLeaderboardProvider()`。規格「僅改 :41 一行／其餘零改動」更正為「改 :20＋:41 兩行，其餘零改動」。
3. **ILootLockerApi seam 形狀**：`Action<int>`（rank<0 失敗）→ `Action<bool,int>`（success, rank）；`GetScoreList` → `Action<bool, LeaderboardEntry[]>`；`StartGuestSession` → `Action<bool>`。理由：A4 錯誤語義明確化。
4. **memberId 產生器**：建議 GUID 衍生（`Guid.NewGuid().ToString("N")`）取代「時間戳＋遞增序」——無溢位、跨 session 無碰撞（BC-5）。
5. **佇列上限策略定案**：FIFO 丟棄最舊（drop-oldest）。
6. **flush 時機**：規範＝session-ready 轉移；建議附加＝成功 `LoadAll` 後機會性 flush（防重入）。
7. **provider 永遠回呼**：`Submit` 失敗回 `SubmitResult(false,-1,cached)`；`LoadAll` 失敗回快取／空榜——UI 永不懸置。
8. **安全 SOP**：config 資產（含 .meta）進 `.gitignore`；build 前清空 adminToken；建議 Editor pre-build guard（非阻擋）。
9. **舊 key 退役**：`flipbird_leaderboard` 不讀／不寫／不遷移（不主動刪除）。
10. **`multiUserSessionMode=2`**：SingleSession，單機正確，無需變更（規格可註記）。

## 對既有 ADR 的相容性註記

- **ADR-015**：本 ADR 為其後端實作替換之延伸，**不修改 ADR-015**。決策 1（深模組）、決策 2（程式 new／否決 `[SerializeField] interface`）、決策 3（PlayerNameStore seam）、決策 4（callback 三點）、決策 8（ScoreSession 零改動）全部沿用。ADR-015 決策 4 的「未來異步實作自行 marshal」由本 ADR 決策 2 具體化（實證＝SDK 已主執行緒，故以斷言取代 dispatcher）。
- **ADR-008**：ScoreSession 零改動；`flipbird_high_score` 不變。
- **ADR-011**：Leaderboard 仍走 `GameManager.Leaderboard` accessor，不另立 static seam。
- **ADR-006**：LeaderboardScreen 非 GameState 顯示不變。
