# ADR-015: Leaderboard 系統（LeaderboardProvider 深模組＋LeaderboardScreen 非 GameState 顯示＋High Score 顯示退休）

## Status

Accepted

## Context

排行榜功能（`specs/requirements/leaderboard.md`，arch-review=yes）觸發 5 項架構變更：

1. **新共享元件 LeaderboardProvider 接口＋LocalLeaderboardProvider**（T1/T2）：新系統目錄 `Assets/Scripts/Leaderboard/`；新跨腳本依賴 UIController → GameManager.Leaderboard → Provider；需比照 ScoreSession 深模組模式（ADR-008 S2-S4）。
2. **持久化模型擴張**（T1）：PlayerPrefs 單值 `flipbird_high_score` → JSON 榜單 `flipbird_leaderboard`；兩 key 不得互相覆寫；ScoreSession 零改動。
3. **High Score UI 顯示退休**（T4）：移除 Menu/GameOver 的 High Score 文字與破紀錄跳動（ui-animations FR-07）；需同步 `game-states.md` 等文件。
4. **Qualify Animation 新演出模式**（T5）：榜底滑入＋滾分 2s easeOut＋名次即時重排；裁決動畫職責歸屬。
5. **非獨立 GameState 確認**（T3/T4）：LeaderboardScreen 於 Menu／GameOver 兩態內顯示；與 ADR-006 凍結政策相容；已定案決策 B=i／C=α 一併納入。

已定案產品決策（不可再開，架構須容納）：**A=a**（名稱輸入 modal，預填 `flipbird_player_name`、上限 12 字元、確認/取消 Esc、留空＝丟棄不上榜）、**B=i**（MenuCanvas 排行榜按鈕切換瀏覽、返回鈕回 Menu 主面板）、**C=α**（上榜時 LeaderboardScreen 取代 GameOverPane，提供 BackToMenu＋Restart 兩鈕）。

**規格事實主張核對**：本審查逐行實讀 `UIController.cs`（:20-21/27/50/133/140-141/163/175-194/210-219/241-253）、`GameManager.cs`（:26/173）、`ScoreSession.cs`（:11-15/18-32/46/57-63/87-96）、`UITween.cs`、`ScoreTrigger.cs`、`PooledActives.cs`——**規格全部事實主張（含行號與行為）經實證正確**，無需勘誤。唯一注意：規格標 `UIController.cs:20-21` 為 `menuHighScoreText` 欄位範圍，實際 :20 為 `[Header("Menu")]`、:21 為欄位——屬可接受範圍標記，非錯誤。

## Decision

### 決策 1：LeaderboardProvider 深模組（比照 ADR-008 S2-S4）

`Assets/Scripts/Leaderboard/` 新系統目錄（CONTEXT.md :67-71 慣例），四檔：

- `LeaderboardEntry.cs`：`[Serializable] public class LeaderboardEntry { public string playerName; public int score; }`（FR-01）。
- `LeaderboardProvider.cs`：`public interface LeaderboardProvider { void LoadAll(Action<LeaderboardEntry[]> callback); void Submit(LeaderboardEntry entry, Action<SubmitResult> callback); }`＋`public readonly struct SubmitResult { public bool Qualified; public int Rank; public LeaderboardEntry[] Entries; }`（FR-02/FR-06）＋`public static class LeaderboardRules { public static bool Qualifies(int score, LeaderboardEntry[] entries); }`（FR-05 純函式）＋JSON wrapper class（`[Serializable] class LeaderboardData { public LeaderboardEntry[] entries; }`，A1 防護）。
- `LocalLeaderboardProvider.cs`：實作 `LeaderboardProvider`；PlayerPrefs key `flipbird_leaderboard`（FR-03）；讀取→記憶體快照；Submit＝插入→排序（降冪、同分 FCFS）→截斷 10 筆→序列化寫回→回傳 SubmitResult（FR-04/FR-06）；解析失敗回傳空榜單不 crash（FR-07）；`internal` 建構 seam 供 EditMode 注入 fake persistence（比照 ScoreSession.cs:60-63）。
- `PlayerNameStore.cs`：名稱記憶 seam（見決策 3）。

**GameManager 只保留組裝＋accessor**：`public LeaderboardProvider Leaderboard { get; }`（Awake `new LocalLeaderboardProvider()`，比照 `scoreSession = new ScoreSession()` 於 Awake 的既有模式）＋`public PlayerNameStore PlayerName { get; }`（Awake `new PlayerNameStore()`）。呼叫端（UIController）經 `gameManager.Leaderboard.*`／`gameManager.PlayerName.*` 讀寫，不直接觸碰 PlayerPrefs（S4 精神）。

### 決策 2：介面序列化機制——**程式碼 new LocalLeaderboardProvider，否決 [SerializeField] interface**

**Unity 序列化器不支援 interface 型別欄位**——`[SerializeField] LeaderboardProvider provider` 會被序列化器靜默忽略（不報錯、永不賦值、Inspector 顯示空）。這是 Unity 平台事實，規格若如此寫必踩坑。

**裁定：GameManager 內部 `new LocalLeaderboardProvider()`（Awake），暴露唯讀 interface accessor**。勝出理由：

1. **與 ADR-008 ScoreSession 模式完全一致**：ScoreSession 即純 C# 類、GameManager.Awake `new`、internal 測試 seam、呼叫端經 accessor——LeaderboardProvider 比照同一模式，架構一致性最高，Developer 不需學新模式。
2. **本輪零配置可用**：無需場景接線、無 Inspector 指派，T2 完成即 `gameManager.Leaderboard` 可用。
3. **NFR-05 換實作呼叫端零改動**：UIController 只依賴 `LeaderboardProvider` interface（經 GameManager.Leaderboard accessor）。換 LootLocker 時只改 GameManager 組裝點一行（`new LocalLeaderboardProvider()` → `new LootLockerLeaderboardProvider()`），UIController 零改動——「呼叫端零改動」的正確語義＝組裝點（composition root）是唯一知道具體實作的地方。
4. **測試性**：internal 建構 seam（比照 ScoreSession.cs:60-63）供 EditMode 注入 fake persistence；Provider 為純 C# 類，EditMode 測試不需 new GameObject。

**被否決**（防重提）：

- **`[SerializeField] LeaderboardProvider provider`（interface 直接序列化）**：Unity 序列化器不支援 interface 欄位——靜默忽略、永不賦值，T2 完成即 NRE。此方案不可行。
- **抽象 MonoBehaviour 基底**（`abstract class LeaderboardProviderBase : MonoBehaviour`，LocalLeaderboardProvider 繼承）：LocalLeaderboardProvider 是純資料邏輯（PlayerPrefs JSON 讀寫＋排序），不需要 Unity 生命週期；MonoBehaviour 化使 EditMode 測試需 new GameObject、與 ScoreSession 純 C# 模式不一致、場景接線成本高（違反零配置可用）。
- **[SerializeReference]**：Unity 6 支援 interface 的 SerializeReference，但複雜度最高——與 internal 測試注入 seam 衝突（序列化需無參數建構）、Inspector 指派需手動、本輪零配置可用需 fallback 邏輯；且對「換實作」沒有比決策 2 更好（組裝點仍要改）。

### 決策 3：名稱記憶 seam——**PlayerNameStore 獨立 seam，否決 UI 直接 PlayerPrefs**

**NFR-03 與決策 A 的衝突**：NFR-03 要求 UI 不得直接觸碰 PlayerPrefs（grep 驗收：UIController.cs 零 PlayerPrefs），但決策 A=a 要求名稱記憶（`flipbird_player_name`）——若 UIController 直接 `PlayerPrefs.GetString/SetString`，grep 驗收必失敗。

**裁定：`PlayerNameStore` 獨立 seam**（`Assets/Scripts/Leaderboard/PlayerNameStore.cs`，純 C# 類，PlayerPrefs 內部 adapter＋internal 測試 seam，比照 ScoreSession 模式）。GameManager 暴露 `public PlayerNameStore PlayerName { get; }`（Awake `new PlayerNameStore()`）。UI 經 `gameManager.PlayerName.Load()/Save(name)`，零 PlayerPrefs。

**為何不併入 LeaderboardProvider 接口**：名稱記憶不是「榜單」職責——把 `LoadPlayerName/SavePlayerName` 加進 LeaderboardProvider 會污染接口契約（榜單讀寫 vs 玩家名稱是兩個獨立關注點），且未來 LootLocker 實作不需承接本地名稱記憶。獨立 seam 保持 LeaderboardProvider 接口純淨（僅 LoadAll/Submit），同時滿足 NFR-03 grep 驗收。

### 決策 4：async callback 契約——**主執行緒保證＋同步回呼容忍＋Submit callback 禁再 LoadAll**

**A3（本地同步回呼）與 BC-6（async 競態）裁定**：

1. **callback 主執行緒保證**：接口契約聲明「callback 保證在 Unity 主執行緒呼叫」——UI 操作（SetActive、TMP 文字）只能在主執行緒做。本地實作同步回呼天然滿足；未來 LootLocker 若在背景執行緒回呼，實作類必須自行 marshal 到主執行緒（Update 佇列/Dispatcher），不得把執行緒問題丟給呼叫端。
2. **同步回呼容忍**：呼叫端不得假設有幀延遲（本地同步回呼），也不得假設無幀延遲（未來異步）——**callback 內應執行「顯示/渲染」動作本身，而非假設顯示已完成**。同步回呼時 callback 在 OnStateChanged(GameOver) 處理中執行，LeaderboardScreen 尚未顯示——callback 內做顯示動作是安全的；但若 callback 內假設「LeaderboardScreen 已 active」就會錯。
3. **BC-6 守衛寫法**：callback 內 `if (gameManager.GetGameState() != GameManager.GameState.GameOver) return;` **足夠**——對同步回呼成立（呼叫時 state==GameOver，同步回呼時仍 GameOver，守衛通過）；對未來異步回呼也成立（期間狀態已變則擋下）。比照 `_isFirstSync` 模式的「狀態快照守衛」語義。
4. **Submit callback 內禁再 LoadAll**：`SubmitResult.Entries` 已含提交後完整榜單（Qualify Animation 用）——UI 不得在 Submit callback 內再呼叫 LoadAll（同步回呼時會重入，異步時多餘）。契約明列。

### 決策 5：Qualify Animation 職責——**獨立 LeaderboardAnimator 元件（Assets/Scripts/UI/）**

**裁定：抽獨立演出元件 `LeaderboardAnimator`**（`Assets/Scripts/UI/LeaderboardAnimator.cs`，UI 領域非持久化層）。勝出理由：

1. **職責分離**：UIController 是「流程編排」（狀態切換、按鈕接線、上榜流程），LeaderboardAnimator 是「演出執行」（榜單列渲染、滑入、滾分、重排、中斷歸位）。兩者變更頻率不同（流程改 vs 演出改）。
2. **模組深度**：演出元件持有「榜單列容器＋逐列 RectTransform＋tween 管理」的內部知識，UIController 不需知道列如何排——呼叫端只需 `Render(entries)`／`PlayQualify(SubmitResult result)`／`StopAll()`。
3. **B3 教訓**：動畫目標必須 child RectTransform——演出元件內部持有逐列 RectTransform，自然遵守（根 Canvas 位移視覺無效，ui-animations round-3 實證）。
4. **中斷歸位**：演出元件統一管理「該榜單相關全部 tween」（StopAll 逐列＋StopFloatTweens 滾分），UIController 的 StopPanelTweens 呼叫 `leaderboardAnimator.StopAll()` 即可——比 UIController 自己追蹤每列乾淨。
5. **可測性**：演出元件可獨立 EditMode 測試（注入 fake 列容器）。

**被否決**（防重提）：

- **全放 UIController**：UIController 現 260 行，加 Qualify Animation（~80-120 行）膨脹至 ~380 行；流程與演出耦合；中斷歸位需 UIController 追蹤每列 tween（StopPanelTweens 爆炸）。
- **放 Leaderboard 持久化層（Assets/Scripts/Leaderboard/）**：演出是 UI 職責，持久化層不得依賴 UI（依賴方向錯誤——Provider 不得反向引用遊戲邏輯，更不得引用 UI）。

### 決策 6：T5 歸屬——**scene-developer（UI），非 gameplay-developer**

**裁定：T5 應歸 scene-developer**。理由：

1. Qualify Animation 是 UI 演出（榜單列渲染＋tween），非 gameplay 邏輯。既有 UI 演出（面板滑入、文字滑出、破紀錄跳動）全在 UIController（scene-developer 的 T4 範圍）。
2. 依決策 5，演出元件放 `Assets/Scripts/UI/`——UI 目錄是 scene-developer 的領域（G4）。
3. T5 依賴 T3（LeaderboardScreen Canvas 場景結構）與 T4（上榜流程觸發）——T3/T4 皆 scene-developer，T5 歸 scene-developer 使同一 developer 連續完成 UI 相關任務，減少交接。
4. gameplay-developer 的 T1/T2 是持久化層（純邏輯）——與 UI 演出無關。純邏輯（LeaderboardRules.Qualifies）已在 T1 歸 gameplay。

### 決策 7：非 GameState 確認（NFR-01／ADR-006 相容）

LeaderboardScreen 於 Menu／GameOver 兩態內顯示，**不新增 GameState**（詞彙表定案）。相容性確認：

- **凍結政策（D3）**：Menu 態顯示 LeaderboardScreen（瀏覽）時 state==Menu，gameplay 已凍結（各系統自查 IsPlaying()==false）；GameOver 態顯示 LeaderboardScreen（上榜）時 state==GameOver，gameplay 已凍結。LeaderboardScreen 顯示不影響 `IsPlaying()` 語義與各系統自查守衛。
- **狀態轉移路徑零新增**：返回/重開走既有 GameManager 公開方法（BackToMenu/Restart），不觸及 SetState/IsPlaying。
- **顯示控制**：LeaderboardScreen 的顯示/隱藏由 UIController 的「顯示旗標」管理（menuBrowseMode／gameOverQualifyMode），**不經 OnStateChanged 的 state 切換**——Menu 態點排行榜按鈕時 state 維持 Menu（不走 OnStateChanged），故 LeaderboardScreen 顯示/隱藏是 UIController 的按鈕事件＋上榜流程驅動；隱藏掛在 OnStateChanged 開頭（StopPanelTweens 旁）＋返回鈕事件。
- **中斷歸位觸發點**：LeaderboardScreen 顯示/隱藏不走 OnStateChanged 的 StopPanelTweens（state 不變時不 fire），故 LeaderboardAnimator.StopAll() 必須掛在離開路徑（返回鈕、BackToMenu、Restart、OnStateChanged 開頭）——BC-5 比照 StopPanelTweens 先例。

### 決策 8：ScoreSession 零改動與兩 key 分離（NFR-02）

- **ScoreSession 零改動**：Leaderboard 持久化用新 key `flipbird_leaderboard`，與 `flipbird_high_score`（ScoreSession 內部 PlayerPrefsPersistence）分離。兩 key 不互相覆寫。
- **依賴方向**：LocalLeaderboardProvider 不得引用 ScoreSession（Leaderboard 模組獨立）；Leaderboard 模組只依賴 UnityEngine（PlayerPrefs/JsonUtility）＋System（Action/List），不依賴 GameManager/ScoreSession/UI（B7 精神）。
- **High Score 顯示退休**：`menuHighScoreText`（:21）、`gameOverHighScoreText`（:27）、`highScoreCountDuration`（:50）欄位移除；`:140-141`（Menu 顯示）、`:175-194`（GameOver 破紀錄分支）、`:210-219`（PlayHighScoreCountUp）退休；`StopPanelTweens` 內對應停止呼叫（:248-252）移除。ScoreSession 資料與 `flipbird_high_score` 照寫不變（NFR-02）。

## Consequences

- 正面：Leaderboard 持久化比照 ScoreSession 深模組模式（S2-S4），PlayerPrefs 讀寫全隔離在 Provider/PlayerNameStore 內部，UI 零 PlayerPrefs（NFR-03 grep 驗收可達）。
- 正面：介面序列化問題在設計階段裁定（Unity 序列化器不支援 interface 欄位），避免 T2 實作踩 NRE。
- 正面：名稱記憶 seam 化解 NFR-03 與決策 A 的衝突，UI 只呼叫 interface。
- 正面：async callback 契約（主執行緒保證＋同步容忍＋Submit 禁再 LoadAll）使本地與未來 LootLocker 實作共用同一呼叫端契約（NFR-05）。
- 正面：LeaderboardAnimator 獨立演出元件使 UIController 維持流程編排職責，演出細節封閉在單一元件（B3 教訓內建）。
- 正面：非 GameState 確認——狀態機/凍結政策零變更，ADR-006/011 相容。
- 負面：新增 1 個系統目錄（`Assets/Scripts/Leaderboard/`）＋1 個 UI 演出元件（`Assets/Scripts/UI/LeaderboardAnimator.cs`）；GameManager 新增 2 個 accessor（Leaderboard/PlayerName）。
- 負面：LeaderboardScreen 顯示控制脫離 OnStateChanged 統一入口（按鈕事件＋上榜流程雙驅動），UIController 需維護顯示旗標——已以決策 7 的「隱藏掛 OnStateChanged 開頭＋離開路徑 StopAll」收斂。
- 被放棄的替代方案：見各決策「被否決」節。

## 對 pm 規格的修訂建議

1. **T6（名稱輸入 modal）**：作法「＋`flipbird_player_name` PlayerPrefs」改為「經 `gameManager.PlayerName`（PlayerNameStore seam）讀寫」——UIController 零 PlayerPrefs（NFR-03 grep 驗收）。T6 驗收「預填記憶」不變。
2. **T2（GameManager.Leaderboard accessor）**：作法明確為「Awake `new LocalLeaderboardProvider()`（比照 ScoreSession），暴露 `public LeaderboardProvider Leaderboard { get; }`」——**否決 `[SerializeField] LeaderboardProvider`**（Unity 序列化器不支援 interface 欄位，靜默忽略）。加 `public PlayerNameStore PlayerName { get; }`（Awake `new PlayerNameStore()`）。
3. **T5（Qualify Animation）**：Assigned 由 gameplay-developer 改 **scene-developer**（UI 演出非 gameplay 邏輯）；作法明確為「新建 `Assets/Scripts/UI/LeaderboardAnimator.cs` 獨立演出元件（Render/PlayQualify/StopAll），UIController 只負責觸發」。
4. **LeaderboardProvider 接口契約補三點**（FR-02）：①callback 保證主執行緒呼叫 ②callback 內執行顯示動作（同步回呼時 LeaderboardScreen 尚未顯示）③Submit callback 內禁再 LoadAll（SubmitResult.Entries 已含完整榜單）。
5. **LeaderboardRules.Qualifies 對 null/空陣列**（FR-05）：entries 為 null（corrupt 防護 FR-07 回空榜單）時視為空榜單——`score > 0` 即上榜。T1 EditMode 測試補 null 案例。
6. **名稱 modal 顯示時機**（FR-13）：Qualifies 通過後顯示 LeaderboardScreen（上榜模式）＋名稱 modal；Submit 確認後才播 Qualify Animation——規格已寫，架構確認此順序（動畫在 Submit 之後，非 Qualifies 之後）。

## 對既有 ADR 的相容性註記

- **ADR-008**：LeaderboardProvider 比照 ScoreSession 深模組模式（S2-S4），但為獨立模組——ScoreSession 零改動（NFR-02）。
- **ADR-006**：LeaderboardScreen 非 GameState，於 Menu/GameOver 兩態內顯示——狀態機/凍結政策零變更。
- **ADR-011**：Leaderboard 走 GameManager.Leaderboard accessor（S3 命令/查詢皆經 accessor），不另立 static seam——查詢/命令分離原則不變。
- **ADR-007**：High Score 顯示退休不影響 Renderer.enabled 隱藏機制（Menu 隱藏鳥維持）。
- **ADR-003**：碰撞規則不受影響（Leaderboard 純 UI＋持久化）。