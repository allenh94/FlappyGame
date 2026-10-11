# Leaderboard 系統架構（排行榜）

> 本文件由 tech-lead-agent 維護（ADR-015 產出；ADR-016 擴充線上後端），其他 agent 唯讀。
> 需求真相來源：`specs/requirements/leaderboard.md`（本地版）＋`specs/requirements/leaderboard-lootlocker.md`（線上後端）。本文引用其條目編號作為裁定依據。
> 決策紀錄：`docs/adr/015-leaderboard.md`（本地版）＋`docs/adr/016-leaderboard-lootlocker.md`（線上後端）。依賴規則：`dependency-rules.md` LB1-LB16。

## 0. 模組總覽（Class Diagram）

```
┌─ Leaderboard 持久化層（Assets/Scripts/Leaderboard/，純 C#，零 MonoBehaviour）──┐
│                                                                              │
│  LeaderboardEntry : [Serializable] class                                     │
│    playerName : string / score : int（FR-01）                                 │
│                                                                              │
│  LeaderboardProvider : interface（FR-02，async callback）                     │
│    void LoadAll(Action<LeaderboardEntry[]> callback)                          │
│    void Submit(LeaderboardEntry entry, Action<SubmitResult> callback)         │
│  SubmitResult : readonly struct { bool Qualified; int Rank;                   │
│                                   LeaderboardEntry[] Entries; }               │
│                                                                              │
│  LeaderboardRules : static class（FR-05 純函式，單一事實來源）                  │
│    static bool Qualifies(int score, LeaderboardEntry[] entries)               │
│      · <10 筆：score > 0；=10 筆：score > 第 10 名；與第 10 名同分不算          │
│      · null/空陣列視為空榜單（corrupt 防護 FR-07 配套，LB6）                   │
│                                                                              │
│  LootLockerLeaderboardProvider : LeaderboardProvider（ADR-016；FR-01~FR-08）   │
│    · 單一自足：lazy guest session＋讀寫穿透快取＋待送佇列＋唯一 member_id       │
│    · PlayerPrefs key "flipbird_leaderboard_cache"（上次成功榜，JSON wrapper）   │
│    · PlayerPrefs key "flipbird_leaderboard_pending"（待送佇列，JSON，上限 20）  │
│    · 伺服器權威 SubmitResult：SubmitScore→rank；GetScoreList→Entries；          │
│      Qualified = rank∈top10（決策 5）                                          │
│    · SDK 呼叫全封裝於 internal ILootLockerApi seam（NFR-03；LB9）              │
│    · 純 C#（零 MonoBehaviour）；internal 建構 seam 供 EditMode 注入 fake API   │
│                                                                              │
│  internal ILootLockerApi : interface（SDK 隔離 seam，ADR-016 決策 1）          │
│    void StartGuestSession(string deviceId, Action<bool> cb)                   │
│    void SubmitScore(string memberId, int score, string key, string metadata,  │
│                     Action<bool,int> cb)   // success, rank                   │
│    void GetScoreList(string key, int count, Action<bool,LeaderboardEntry[]> cb)│
│    · concrete LootLockerApi 為唯一引用 LootLocker.* 的類（LB9）                │
│    · 主執行緒斷言：建構捕捉 ManagedThreadId，callback 入口比對（ADR-016 決策 2）│
│                                                                              │
│  PlayerNameStore : class（ADR-015 決策 3，名稱記憶 seam）                      │
│    · PlayerPrefs key "flipbird_player_name"；Load()/Save(string)              │
│    · internal 建構 seam 供 EditMode 測試注入                                   │
└──────────────────────────────────────────────────────────────────────────────┘
   ▲ 依賴方向單向（LB3：Provider 不得反向引用遊戲邏輯/UI；LB9：SDK 呼叫隔離於 ILootLockerApi）

┌─ GameManager（組裝＋accessor，ADR-015 決策 2）────────────────────────┐
│  public LeaderboardProvider Leaderboard { get; }   // Awake new LootLockerLeaderboardProvider() │
│  public PlayerNameStore PlayerName { get; }        // Awake new PlayerNameStore()          │
│  // 比照 ScoreSession 模式（ADR-008 S3）：呼叫端經 accessor，不直接觸碰 PlayerPrefs        │
└──────────────────────────────────────────────────────────────────────┘
   ▲
┌─ UI 層（Assets/Scripts/UI/）─────────────────────────────────────────┐
│  UIController（流程編排）                                              │
│    · OnStateChanged(GameOver)：LoadAll → Qualifies → 上榜流程          │
│    · 顯示旗標：menuBrowseMode / gameOverQualifyMode（LB8）             │
│    · 按鈕接線：排行榜鈕（決策 B=i）、返回/BackToMenu/Restart（決策 C=α） │
│  LeaderboardAnimator（演出執行，ADR-015 決策 5）                        │
│    · Render(entries) / PlayQualify(SubmitResult) / StopAll()           │
│    · 榜單列渲染＋滑入＋滾分＋即時重排＋中斷歸位（B3 教訓：child RectTransform）│
│  UITween（既有原語，零改動）                                            │
│    · AnchoredPositionTo / FloatTo / StopAll / StopFloatTweens          │
└──────────────────────────────────────────────────────────────────────┘
```

## 1. 接口契約（LeaderboardProvider，LB2）

- **`LoadAll(Action<LeaderboardEntry[]> callback)`**：讀取完整榜單（已排序，降冪＋同分 FCFS）。corrupt 時回傳空陣列（FR-07）。
- **`Submit(LeaderboardEntry entry, Action<SubmitResult> callback)`**：提交條目並回傳 `SubmitResult`。`Qualified`＝是否上榜；`Rank`＝1-based 名次（未上榜 -1）；`Entries`＝提交後完整榜單（Qualify Animation 用）。本地實作＝插入→排序→截斷 10 筆→覆寫（原子）；線上實作＝伺服器權威（見 §1.5）。
- **callback 契約**（ADR-015 決策 4；ADR-016 決策 2 具體化）：
  1. **主執行緒保證**——callback 在 Unity 主執行緒呼叫。線上實作（LootLocker 8.2.0）經實證 SDK callback 已於主執行緒（ADR-016 Context），故**不需 dispatcher**；concrete adapter 以 `ManagedThreadId` 斷言防 SDK 行為漂移。
  2. **同步回呼容忍**——本地實作同步立即回呼（A3）；callback 內執行「顯示/渲染」動作本身，不假設顯示已完成（同步回呼時 LeaderboardScreen 尚未顯示）。線上實作為異步（至少 1 幀延遲）。
  3. **Submit callback 內禁再 LoadAll**——`SubmitResult.Entries` 已含完整榜單，再 LoadAll 會重入（同步回呼時）。
- **NFR-05（ADR-016 決策 6 修正）**：介面契約不變；換 LootLocker 改組裝點（`GameManager.cs:20` 欄位型別 `LocalLeaderboardProvider`→`LeaderboardProvider`＋`:41` `new LootLockerLeaderboardProvider()`，共 2 行）；**UI 僅允許既有流程最小硬化（修 m1：`PlayQualify` 前檢 `result.Qualified`）**，不得擴張 UI 職責。

## 1.5 線上後端（LootLocker）（ADR-016）

> 後端實作替換：`LocalLeaderboardProvider`（本地 PlayerPrefs）→ `LootLockerLeaderboardProvider`（線上）。產品 UX 與 `LeaderboardProvider` 介面契約不變（NFR-01/NFR-04）。需求真相：`specs/requirements/leaderboard-lootlocker.md`。

### 1.5.1 Provider 內部結構（單一自足，純 C#）

```
LootLockerLeaderboardProvider : LeaderboardProvider
├─ session 狀態：lazy StartGuestSession(SystemInfo.deviceUniqueIdentifier)＋ready 旗標
├─ 快取：PlayerPrefs "flipbird_leaderboard_cache"（上次成功榜，JSON LeaderboardData）
├─ 待送佇列：PlayerPrefs "flipbird_leaderboard_pending"（JSON wrapper，上限 20，FIFO drop-oldest）
├─ memberId 產生器：每次提交唯一（GUID 衍生；補送沿用）
└─ internal ILootLockerApi seam（SDK 隔離）
     └─ concrete LootLockerApi（唯一引用 LootLocker.*；主執行緒斷言）
```

- **session**（FR-02）：首個需要網路之操作（`LoadAll`／`Submit`）觸發 lazy `StartGuestSession(SystemInfo.deviceUniqueIdentifier, cb)`；成功→ready＋flush 佇列；失敗→走失敗路徑（讀快取／排隊），下次操作再試。`multiUserSessionMode=SingleSession`（config 值 2）——單機單人正確。
- **快取**（FR-05）：`LoadAll` 成功→原樣寫回快取＋回呼；失敗→讀快取回呼（不報錯）＋log；快取空／corrupt→回空榜（不 crash）。
- **佇列**（FR-06/FR-08）：`Submit` 失敗且 `LeaderboardRules.Qualifies(score, cached)`→入列 `{memberId, playerName, score}`；上限 20（FIFO drop-oldest）；session-ready 後 flush（逐項、沿用 memberId、成功移除失敗保留、`isFlushing` 防重入）。
- **memberId**（FR-04/BC-5）：每次提交產生唯一 id（GUID 衍生，無溢位）；Arcade 每筆各自成列。
- **伺服器權威 SubmitResult**（FR-07）：`SubmitScore`→rank；`GetScoreList(key,10)`→Entries（`metadata`→`playerName`）；`Qualified = rank∈top10`；回呼 `SubmitResult{Qualified,Rank,Entries}`。客戶端 `LeaderboardRules.Qualifies` 僅為提交前守衛（決策 5）。
- **永遠回呼**：`Submit` 失敗回 `SubmitResult(false,-1,cached)`；`LoadAll` 失敗回快取／空榜——UI 永不懸置。

### 1.5.2 資料流

**LoadAll（快取穿透）**：
```
LoadAll(cb)
  ├─ session 未 ready → StartGuestSession(deviceId)
  │     ├─ 成功 → GetScoreList(key,10)
  │     │     ├─ 成功 → 寫 "flipbird_leaderboard_cache" → cb(entries)
  │     │     └─ 失敗 → 讀快取 → cb(cache 或 空榜)
  │     └─ 失敗 → 讀快取 → cb(cache 或 空榜)
  └─ session ready → GetScoreList(key,10)（同上）
```

**Submit（伺服器權威）**：
```
Submit(entry, cb)
  ├─ session 未 ready → StartGuestSession → 成功後續；失敗 → 排隊（若過守衛）＋cb(false,-1,cached)
  └─ session ready → SubmitScore(memberId, score, key, metadata)
        ├─ 成功 → GetScoreList(key,10) → Qualified=rank∈top10 → cb(SubmitResult)
        └─ 失敗 → 若 Qualifies(score, cached) → 入佇列 → cb(false,-1,cached)
```

**flush（session-ready 後）**：
```
session ready
  └─ FlushPending()（isFlushing 防重入）
        └─ 逐項 SubmitScore(項.memberId, 項.score, key, 項.playerName)
              ├─ 成功 → 移除該項
              └─ 失敗 → 保留（下次再試）
```

### 1.5.3 依賴方向與 SDK 隔離

```
UI → GameManager.Leaderboard → LootLockerLeaderboardProvider → ILootLockerApi → LootLocker SDK
```

- Provider 不得反向引用 UI／GameManager／ScoreSession（LB3）。
- **只有 concrete `LootLockerApi` 可引用 `LootLocker.*`**；provider 邏輯只引用 `ILootLockerApi`（LB9）。
- **主執行緒**：SDK 8.2.0 callback 在主執行緒（實證，ADR-016 Context）；concrete adapter 以 `ManagedThreadId` 斷言；**無 dispatcher**（ADR-016 決策 2）。

### 1.5.4 持久化 key

| key | 擁有者 | 處置 |
|-----|--------|------|
| `flipbird_high_score` | ScoreSession | 不變 |
| `flipbird_player_name` | PlayerNameStore | 不變 |
| `flipbird_leaderboard_cache` | LootLockerLeaderboardProvider | 新增 |
| `flipbird_leaderboard_pending` | LootLockerLeaderboardProvider | 新增（上限 20） |
| `flipbird_leaderboard` | （舊 LocalLeaderboardProvider） | 退役（不讀／不寫／不遷移） |

### 1.5.5 安全

- `Assets/LootLockerSDK/Resources/Config/LootLockerConfig.asset`（含 `.meta`）**不進版控**（`.gitignore`）。
- 資產在 `Resources/` 會被打包進 build——**build 前必須清空 `adminToken`**（客戶端只需 `apiKey`）。
- SOP：build 前檢查 adminToken 為空、apiKey 正確、資產未 commit。建議 Editor pre-build guard（非阻擋）。

## 2. 資料流（GameOver 上榜流程，FR-13）

```
OnStateChanged(GameOver)（UIController.cs:163 case）
  ├─▶ SetActive(gameOverPane, true)（未上榜路徑預設）
  ├─▶ gameManager.Leaderboard.LoadAll(cb)          // 本地同步回呼
  │     └─▶ cb(entries)
  │           ├─▶ LeaderboardRules.Qualifies(CurrentScore, entries)（LB6）
  │           ├─▶ 上榜（決策 C=α）：
  │           │     SetActive(gameOverPane, false)
  │           │     顯示 LeaderboardScreen（上榜模式）＋名稱 modal（決策 A=a）
  │           │       · gameManager.PlayerName.Load() 預填（LB5）
  │           │       · 上限 12 字元；留空按確認＝取消＝丟棄不上榜（BC-4）
  │           │     → 玩家確認 → gameManager.Leaderboard.Submit(entry, cb2)
  │           │         └─▶ cb2(result)：LeaderboardAnimator.PlayQualify(result)
  │           │               · 榜底滑入＋滾分 0→目標 2s easeOut＋即時重排（FR-14）
  │           │               · gameManager.PlayerName.Save(name)（記憶，下次預填）
  │           └─▶ 未上榜：維持 GameOverPane（High Score 文字已退休，FR-12）
  └─▶ BC-6 守衛：cb/cb2 內 if (GetGameState() != GameOver) return;
```

- **時序注意**：本地同步回呼時，整個上榜流程在 OnStateChanged(GameOver) 處理中完成——SetActive 切換同幀發生，渲染在幀末，玩家看不到 GameOverPane 閃爍。未來異步實作（LootLocker）時 GameOverPane 會先顯示、callback 後才切 LeaderboardScreen——屬產品決策（接受閃現或加「判定中」狀態），非架構阻擋。
- **CurrentScore 已為最終值**：UI 收到 GameOver 時 `Score.End()` 已執行（GameManager.cs:174 先於 SetState(GameOver) fire），Qualify 判定可直接用（規格現況診斷 6 實證）。

## 3. 資料流（Menu 瀏覽，決策 B=i）

```
Menu 態（state==Menu，不走 OnStateChanged）
  MenuCanvas 排行榜按鈕（StartButton 下方）──▶ 顯示 LeaderboardScreen（瀏覽模式，禁 Submit）
  LeaderboardScreen 返回鈕 ──▶ 回 Menu 主面板（隱藏 LeaderboardScreen、顯示 MenuCanvas）
```

- 瀏覽模式禁 Submit：不顯示名稱 modal、不顯示 Restart 鈕（只顯示返回鈕）。
- 顯示/隱藏由 UIController 顯示旗標管理（LB8）——state 維持 Menu，OnStateChanged 不 fire。

## 4. 演出（Qualify Animation，FR-14／ADR-015 決策 5）

- **職責歸屬**：`LeaderboardAnimator`（`Assets/Scripts/UI/LeaderboardAnimator.cs`）獨立演出元件——UIController 只負責「何時觸發」，演出元件負責「怎麼演」。
- **介面**：`Render(LeaderboardEntry[] entries)`（渲染榜單列）、`PlayQualify(SubmitResult result)`（上榜演出）、`StopAll()`（中斷歸位）。
- **演出細節**（沿用 UITween 原語，UITween 零改動）：
  - 榜底滑入：新條目 row 從榜底（+offset）以 `AnchoredPositionTo`（easeOut 系）滑入靜止位置。
  - 滾分：`FloatTo`（0→目標分數，2 秒，easeOut 系）逐格更新該 row 分數 TMP（比照 PlayHighScoreCountUp 模式，但 2 秒＋easeOut）。
  - 即時重排：滾動期間依目前滾動值與既有條目比較，逐列 `AnchoredPositionTo` 到新名次位置（easeOut 系）。
  - **動畫目標一律榜單列 child RectTransform**（B3 教訓：根 Canvas 位移視覺無效，ui-animations round-3 實證）。
- **中斷歸位**（BC-5）：離開榜單畫面（返回鈕/BackToMenu/Restart/OnStateChanged 開頭）→ `StopAll()` 停止該榜單相關全部 tween（逐列 StopAll＋滾分 StopFloatTweens）並歸位——比照 StopPanelTweens 先例（UIController.cs:133/241-253）。

## 5. LeaderboardScreen Canvas 慣例（T3）

- **獨立 Canvas**（非 MenuCanvas/OverlayCanvas 子物件；詞彙表「獨立 Canvas」定案），沿用既有慣例：Screen Space-Overlay、Constant Pixel Size、reference 800×600（shared/project.md）。
- 內容：標題（可沿用 GameOverTitle 樣式）、榜單列容器（Top10，row 可 prefab 或程式產生）、「返回主畫面」（BackToMenu）＋「重新開始」（Restart）兩鈕（決策 C=α）。
- 榜單列 row：TMP 文字（名次＋名字＋分數，Kenney Future SDF）；動畫目標用 row 的 child RectTransform（B3 教訓）。
- 名稱輸入 modal（決策 A=a，T6）：LeaderboardScreen 內（上榜模式顯示）——TMP Input Field 預填 `gameManager.PlayerName.Load()`、上限 12 字元、確認/取消（Esc）；留空按確認＝取消＝丟棄不上榜（BC-4）。
- 按鈕樣式沿用 button_rectangle_flat 9-slice；EventSystem 沿用既有（InputSystemUIInputModule，專案 activeInputHandler=1，StandaloneInputModule 每幀拋例外）。
- MenuCanvas 在 StartButton 下方新增「排行榜」按鈕（決策 B=i）。

## 6. Developer 實作切入點

1. **T1（gameplay-developer）線上後端 provider**：`Assets/Scripts/Leaderboard/LootLockerLeaderboardProvider.cs`（新，單一自足）——`LootLockerLeaderboardProvider : LeaderboardProvider`＋`internal ILootLockerApi` seam＋concrete `LootLockerApi`（唯一引用 `LootLocker.*`）。開工先 `unity_reflect` 驗證 SDK 型別／方法簽章（`StartGuestSession`／`SubmitScore`／`GetScoreList`）與 A1-A4。EditMode 測試注入 fake `ILootLockerApi`：快取穿透、佇列上限 20、唯一 member_id、Qualified 推導、session 失敗、flush。詳見 §1.5。
2. **T2（gameplay-developer）GameManager 換組裝點＋刪本地實作**：`GameManager.cs:20` 欄位型別 `LocalLeaderboardProvider`→`LeaderboardProvider`；`:41` `new LootLockerLeaderboardProvider()`；刪 `LocalLeaderboardProvider.cs` 含測試。accessor（`:32`／`:35`）不變。**不改**：狀態機、SetState、EndGame 守衛、Score accessor。
3. **T3（scene-developer）LeaderboardScreen Canvas**：獨立 Canvas（§5 慣例）＋榜單列結構＋按鈕（返回主畫面＋重新開始，決策 C=α）＋MenuCanvas 排行榜按鈕（決策 B=i）＋接線（UIController 序列化欄位：leaderboardScreen、榜單列容器、返回/重開按鈕、名稱 modal）。
4. **T4（scene-developer）UIController 顯示/上榜流程＋High Score 退休**：OnStateChanged switch——Menu case 移除 menuHighScoreText 更新（:140-141）；GameOver case 移除破紀錄分支（:175-194）與 PlayHighScoreCountUp（:210-219）呼叫，新增上榜流程（§2）；移除欄位 menuHighScoreText（:21）/gameOverHighScoreText（:27）/highScoreCountDuration（:50）；StopPanelTweens 移除對應停止呼叫（:248-252）＋加 `leaderboardAnimator.StopAll()`；顯示旗標管理（LB8）。
5. **T5（scene-developer，ADR-015 決策 6）LeaderboardAnimator**：`Assets/Scripts/UI/LeaderboardAnimator.cs`——Render/PlayQualify/StopAll（§4）。**不改**：UITween.cs（沿用既有原語）。
6. **T6（scene-developer）名稱輸入 modal**：LeaderboardScreen 內 TMP Input Field modal——預填 `gameManager.PlayerName.Load()`、上限 12 字元、確認/取消（Esc）；確認後 `gameManager.PlayerName.Save(name)`（LB5）。**UIController 零 PlayerPrefs**（NFR-03 grep 驗收）。開工確認 TMP Input Field 可用性（A4）。