# 規格：排行榜（Leaderboard）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（T1-T5 觸發架構變更——新共享元件 LeaderboardProvider 接口＋本地實作、PlayerPrefs 單值→JSON 榜單的持久化擴張、最高分 UI 顯示退休由排行榜取代、Qualify 演出新動畫模式） ｜ Created: 2026-10-04
> （功能 seed：Human 需求——排行榜功能。前 10 名玩家名字與分數的清單，依分數降冪排序；為最高分顯示的唯一來源。領域詞彙已定案於 CONTEXT.md 詞彙表 :30-36——Leaderboard／LeaderboardEntry／Qualify／Leaderboard Screen／LeaderboardProvider／Qualify Animation／High Score 顯示退休，本規格依該定案展開。）

## 功能：排行榜（Leaderboard）

- **目標**：新增前 10 名玩家名字＋分數的榜單，依分數降冪排序（同分先到先得）；以**排行榜畫面**取代既有「High Score」文字顯示（Menu 與 GameOver 兩態皆可顯示，非獨立 GameState）；持久化走統一 **LeaderboardProvider** 接口（本地＝PlayerPrefs JSON，未來可換 LootLocker）。
- **性質**：**新功能＋UI 顯示遷移**。新增 LeaderboardProvider 接口＋LocalLeaderboardProvider（持久化層）＋LeaderboardEntry（資料模型）＋Leaderboard Screen（獨立 Canvas）＋Qualify 上榜流程與演出；**退休**既有 High Score 文字顯示（含破紀錄分數跳動）。**ScoreSession 與 `flipbird_high_score` 資料不動**（最高分資料保留於 ScoreSession，僅顯示退休）。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/Leaderboard/LeaderboardEntry.cs`（新） | **新建**（T1）：資料模型——playerName＋score |
| Script | `Assets/Scripts/Leaderboard/LeaderboardProvider.cs`（新） | **新建**（T1）：統一接口（async callback，契約三點見 FR-02）＋LeaderboardRules 靜態判定 |
| Script | `Assets/Scripts/Leaderboard/LocalLeaderboardProvider.cs`（新） | **新建**（T1）：PlayerPrefs JSON 實作（key `flipbird_leaderboard`）；internal 測試 seam |
| Script | `Assets/Scripts/Leaderboard/PlayerNameStore.cs`（新） | **新建**（T1）：名稱記憶 seam（key `flipbird_player_name`，Load()/Save()）；internal 測試 seam（ADR-015 決策 3） |
| Script | `Assets/Scripts/GameManager.cs` | **修改**（T2）：新增 `Leaderboard`＋`PlayerName` accessor（Awake new；鏡像 `Score` accessor S3 模式，:26；否決 [SerializeField] interface，ADR-015 決策 2） |
| Script | `Assets/Scripts/UI/UIController.cs` | **修改**（T4）：Menu 顯示榜單＋GameOver 上榜流程＋**High Score 顯示退休**（:20-21/:27/:140-141/:175-194/:210-219）；按鈕接線；LeaderboardAnimator 觸發 |
| Script | `Assets/Scripts/UI/LeaderboardAnimator.cs`（新） | **新建**（T5）：獨立演出元件（Render／PlayQualify／StopAll，ADR-015 決策 5） |
| Scene | `Assets/Scenes/SampleScene.unity` | **修改**（T3）：新增 LeaderboardScreen Canvas（獨立）、MenuCanvas 加「排行榜」按鈕、接線 |
| Script | `Assets/Scripts/UI/UITween.cs` | **不變**（T5 沿用既有原語：AnchoredPositionTo／FloatTo／EaseOutBack／EaseOutCubic／StopAll／StopFloatTweens） |
| Script | `Assets/Scripts/Score/ScoreSession.cs` | **不變**（最高分資料保留於此，NFR-02；key `flipbird_high_score`、End() 破紀錄覆寫流程不動） |
| Prefab | `Assets/Prefabs/` | **沿用慣例**（T3 依需求新建榜單列 row prefab 或純程式產生，見 T3 作法） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **排行榜領域詞彙已定案**：`CONTEXT.md:30-36` 詞彙表定義 Leaderboard（前 10 名降冪、最高分顯示唯一來源）、LeaderboardEntry（名字＋分數、同分先到先得）、Qualify（<10 筆須 >0；=10 筆須 >第 10 名；與第 10 名同分不算）、Leaderboard Screen（獨立 Canvas＋返回主畫面鈕、Menu 瀏覽／GameOver 上榜皆可顯示、**非獨立 GameState**）、LeaderboardProvider（統一 async callback 接口、本地＝PlayerPrefs JSON、未來 LootLocker）、Qualify Animation（榜底滑入＋分數滾動 0→目標 2 秒 easeOut＋即時重排）、High Score（資料保留於 ScoreSession、**UI 顯示已退休**由排行榜取代）。
2. **無任何排行榜實作**：grep `leaderboard|Leaderboard|排行榜` 於 `Assets/` 與 specs/ 零命中（唯一命中為 CONTEXT.md 詞彙表）；SampleScene.unity grep `m_Name: Leaderboard` 零命中——無現成榜單物件/腳本。
3. **最高分顯示現況（退休範圍）**：`UIController.cs:20-21` `menuHighScoreText`、`:27` `gameOverHighScoreText`、`:140-141`（Menu 態顯示 "High Score: N"）、`:175-194`（GameOver 態破紀錄分支：先顯示舊最高分再跳動）、`:210-219` `PlayHighScoreCountUp`（破紀錄分數跳動，1 秒 EaseOutCubic，ui-animations FR-07）——**退休範圍即此四處**（顯示層移除；ScoreSession 資料與判定保留）。
4. **ScoreSession 深模組（ADR-008）**：`ScoreSession.cs:11-15` internal `IPersistence` seam、`:18-32` `PlayerPrefsPersistence`（key `flipbird_high_score`）、`:46` `HighScore` accessor、`:57-63` 建構／測試注入、`:87-96` `End()`（快照→判定 recordBroken→覆寫，S2 順序封閉）。**呼叫端不觸碰 PlayerPrefs（S4）**——Leaderboard 持久化必須比照：PlayerPrefs 讀寫隔離在 Provider 內部。
5. **GameManager Score accessor（S3）**：`GameManager.cs:26` 註解「得分模組唯讀 accessor（S3）——UI／ScoreTrigger 經此讀取／計分，不直接觸碰 PlayerPrefs」——Leaderboard accessor 鏡像此模式（T2）。
6. **狀態機與 EndGame 時序**：enum `{Menu, Ready, Playing, Dying, Paused, GameOver}`（dying-state）；`EndGame()` 守衛 Dying/Paused→`SetState(GameOver)`→寫最高分→fire `OnStateChanged`（`GameManager.cs:173` 註解「須在 SetState(GameOver) fire OnStateChanged 前呼叫，UI 才能讀到正確的 RecordBroken/PreviousHighScore」）——**UI 收到 GameOver 時 `CurrentScore` 已是最終值**，Qualify 判定可直接用。
7. **UI 慣例（shared/project.md＋ui-animations）**：MenuCanvas／OverlayCanvas 分離（Screen Space-Overlay、Constant Pixel Size、reference 800×600）；MenuPanel 包裝層（B3 教訓：根 Canvas 位移視覺無效，動畫目標必須 child RectTransform）；按鈕＝button_rectangle_flat 9-slice；字型＝Kenney Future SDF；EventSystem 必須 **InputSystemUIInputModule**（專案 activeInputHandler=1，StandaloneInputModule 每幀拋例外）。
8. **UITween 既有原語（ui-animations）**：`AnchoredPositionTo`／`ScaleTo`／`FloatTo`＋EaseOutBack／EaseOutCubic／EaseOutQuad＋`StopAll`／`StopFloatTweens`（snap 到 to 值）——Qualify Animation 的「滑入＋滾分＋重排」可全數沿用，不需新 tween 引擎。
9. **動畫中斷先例**：`UIController.cs:133` `StopPanelTweens()`（狀態切換時停止所有進行中動畫並歸位，FR-06/BC-1/BC-2）——Qualify Animation 比照：離開榜單畫面（返回/重開/回 Menu）時停止並歸位（BC-5）。

### 架構背景（ADR 出處）

- **ADR-008**（`docs/adr/008-score-session.md`）：ScoreSession 為得分/最高分/PlayerPrefs 唯一真源深模組（S2 順序封閉、S3 accessor、S4 持久化隔離、internal seam 測試注入）——**LeaderboardProvider 比照同一深模組模式**（介面＋本地實作＋internal seam），T1/T2 依此設計。
- **ADR-006**（`docs/adr/006-game-states.md`）：GameState 狀態機＋凍結政策（D3，非 Playing 凍結 gameplay，禁用 SetActive 停用 gameplay 物件、禁用 timeScale=0）；**Leaderboard Screen 不新增 GameState**（詞彙表定案），僅在 Menu／GameOver 兩態內顯示——不觸及凍結政策。
- **ADR-011**（`docs/adr/011-game-state-authority.md`）：查詢/命令分離 seam（GameStateAuthority 僅狀態查詢）；Leaderboard 走 GameManager.Leaderboard accessor（S3 命令/查詢皆經 accessor），不另立 static seam。
- **ADR-015**（`docs/adr/015-leaderboard.md`）：Leaderboard 系統架構裁定——LeaderboardProvider 深模組（比照 ADR-008 S2-S4）；否決 [SerializeField] interface（決策 2）；PlayerNameStore 名稱 seam（決策 3）；async callback 契約三點（決策 4）；LeaderboardAnimator 獨立演出元件（決策 5）；T5 歸 scene-developer（決策 6）；非 GameState 確認（決策 7）；ScoreSession 零改動兩 key 分離（決策 8）。架構細節見 `specs/architecture/leaderboard-system.md`（LB1-LB8）。
- **ui-animations**（`specs/requirements/ui-animations.md`）：FR-07 破紀錄分數跳動（1 秒 EaseOutCubic）**隨 High Score 顯示退休**（FR-12/T4）；UITween 原語與「動畫目標必須 child RectTransform」教訓適用於 Qualify Animation（T5）。
- **game-states**（`specs/requirements/game-states.md`）：MenuCanvas 標題/開始/最高分——「最高分」文字由排行榜取代（FR-12/T4）；OverlayCanvas 單面板雙態（PausePane/GameOverPane）不變。
- **CONTEXT.md 目錄慣例**（:67-71）：新系統腳本一律 `<根目錄>/<系統名>/` 子資料夾隔離——Leaderboard 新系統 → `Assets/Scripts/Leaderboard/`（T1）。

### 未驗證假設

- **A1（JsonUtility 裸陣列）**：Unity `JsonUtility` 不支援頂層裸陣列（需 wrapper class 包 `LeaderboardEntry[]`）——已知 API 行為，未在本專案使用過（grep `JsonUtility` 於 Assets/Scripts 零命中）；T1 開工先 unity_reflect 驗證或直接採 wrapper class 慣例。
- **A2（PlayerPrefs 字串容量）**：10 筆榜單 JSON 遠低於 PlayerPrefs 字串上限（~1MB 級）——安全，不影響；不另做壓縮。
- **A3（async callback 同步回呼）**：LocalLeaderboardProvider 為本地實作，callback 可能**同步**立即回呼（無真實 async）——UI 流程必須容忍同步回呼（不能假設一定有幀延遲）；BC-6 競態防護仍需保留（未來 LootLocker 實作可能真 async）。
- **A4（TMP Input Field 可用性）**：名稱輸入 modal（決策 A=a 定案）需 TMP Input Field 元件——TMP 套件已用（Kenney Future SDF），Input Field 屬同套件；T6 開工確認場景元件可用。

## 已定案決策（A/B/C）

> 三項決策已於先前 grill-with-docs 盤問由 Human 定案，不可再開。

### 決策 A：玩家名稱來源——**定案：方案 a（名稱輸入 modal）**
- **背景**：榜單條目含「玩家名字」（詞彙表定案），名字來源需定案。
- **定案**：上榜時顯示名稱輸入 modal——TMP Input Field **預填上次輸入**（PlayerPrefs key `flipbird_player_name`）、**上限 12 字元**、**確認／取消（Esc）**；首次無記錄時欄位空白；**留空按確認＝取消**（丟棄不上榜）。輸入後記憶，下次沿用。
- **影響**：FR-13 名稱取得＝modal 流程（seam 維持於單一取得點）；T6 確定執行。

### 決策 B：Menu 態 Leaderboard Screen 呈現——**定案：方案 i（排行榜按鈕切換）**
- **背景**：詞彙表定案「主選單（瀏覽）」，Menu 態與 MenuCanvas 的並存方式需定案。
- **定案**：MenuCanvas 在 **StartButton 下方**新增「排行榜」按鈕，點擊切換至 LeaderboardScreen（瀏覽模式，禁 Submit）；LeaderboardScreen「返回」鈕回 Menu 主面板——避免 800×600 畫面並列擁擠，符合「返回主畫面鈕」語意。
- **影響**：FR-09/T3/T4（Menu 瀏覽呈現）；Menu 主面板加一顆按鈕。

### 決策 C：GameOver 上榜流程與返回鈕去向——**定案：方案 α（取代 GameOverPane）**
- **背景**：詞彙表定案「GameOver（上榜）兩狀態皆可顯示」＋LeaderboardScreen「返回主畫面鈕」；上榜時與 GameOverPane 的取代關係、返回鈕去向需定案。
- **定案**：上榜時 LeaderboardScreen **取代** GameOverPane 顯示（上榜演出→名稱 modal→Submit），LeaderboardScreen 提供「返回主畫面」（BackToMenu）與「重新開始」（Restart）兩按鈕——玩家不會因上榜而失去再玩一次的選擇；未上榜顯示既有 GameOverPane。
- **影響**：FR-10/FR-11/T3/T4；LeaderboardScreen 兩顆按鈕（與 GameOverPane 按鈕集一致）。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 前 10 名玩家名字與分數的清單，降冪排序 | FR-01／FR-04；T1 |
| 最高分顯示的唯一來源（High Score 顯示退休） | FR-12；T4 |
| 同分先到先得 | FR-04；T1 |
| 上榜資格判定（<10 須 >0；=10 須 >10th；同分不算） | FR-05；T1 |
| 排行榜畫面（獨立 Canvas、返回主畫面鈕、Menu/GameOver 皆可顯示、非 GameState） | FR-08／FR-09／FR-10／FR-11；T3/T4 |
| Provider 統一接口（async callback、本地 PlayerPrefs JSON、未來 LootLocker） | FR-02／FR-03／FR-07；T1 |
| 上榜演出（榜底滑入＋滾分 2 秒 easeOut＋即時重排） | FR-14；T5 |

## 功能需求（FR）

### A. 資料模型與持久化（LeaderboardProvider 層）

- **FR-01 LeaderboardEntry 資料模型**：新建 `LeaderboardEntry`（可序列化資料類）——欄位 `string playerName`＋`int score`；供 Provider 讀寫與 UI 顯示共用（UI 不得直接觸碰 PlayerPrefs，NFR-03）。
- **FR-02 LeaderboardProvider 統一接口**：新建 `LeaderboardProvider` 接口（async callback 式，詞彙表定案）——`void LoadAll(Action<LeaderboardEntry[]> callback)`（讀取完整榜單，已排序）、`void Submit(LeaderboardEntry entry, Action<SubmitResult> callback)`（提交條目）。`SubmitResult` 含 `bool Qualified`／`int Rank`（1-based，未上榜 -1）／`LeaderboardEntry[] Entries`（提交後完整榜單，Qualify Animation 用）。**呼叫端不直接觸碰 PlayerPrefs**（比照 S4）；未來 LootLocker 實作換實作類即可、呼叫端零改動（NFR-05）。**callback 契約三點（ADR-015 決策 4）**：①**主執行緒保證**——callback 保證在 Unity 主執行緒呼叫；未來異步實作（LootLocker）若在背景執行緒回呼，實作類必須自行 marshal 到主執行緒（Update 佇列/Dispatcher），不得丟給呼叫端 ②**同步回呼容忍**——callback 內執行「顯示/渲染」動作本身，不假設顯示已完成（本地同步回呼時 callback 在 OnStateChanged(GameOver) 處理中執行、LeaderboardScreen 尚未顯示）③**Submit callback 內禁再 LoadAll**——`SubmitResult.Entries` 已含提交後完整榜單，再 LoadAll 於同步回呼時會重入（LB2）。
- **FR-03 LocalLeaderboardProvider（PlayerPrefs JSON）**：新建本地實作——PlayerPrefs key `flipbird_leaderboard`（字串，存 JSON）；10 筆上限（FR-04 截斷）；`internal` 測試 seam（比照 ScoreSession.cs:11-15/60-63，EditMode 注入 fake persistence）。
- **FR-04 Top 10 排序與同分規則**：榜單依分數**降冪**排序；**同分先到先得**——先插入的條目排在上面（詞彙表定案）。寫入後截斷至前 10 筆（超出丟棄第 11 名以後）。
- **FR-05 Qualify 判定（LeaderboardRules）**：`LeaderboardRules.Qualifies(int score, LeaderboardEntry[] entries)` 靜態純函式（可 EditMode 測試，不依賴實體）——**未滿 10 筆：`score > 0`；滿 10 筆：`score > 第 10 名分數`；與第 10 名同分不算上榜**（詞彙表定案）。**entries 為 null/空陣列時視為空榜單**（corrupt 防護 FR-07 回空榜單的配套，ADR-015 修訂 5／LB6）——`score > 0` 即上榜。Submit 內部與 UI 顯示判定共用同一規則（單一事實來源）。
- **FR-06 Submit 原子操作**：`Submit` 內完成「插入→排序（FR-04）→截斷 10 筆→覆寫 PlayerPrefs→回傳 SubmitResult」整個序列（讀取→變更→寫回封閉在一次呼叫內）；回傳結果含上榜與否（`Qualified`）與名次（`Rank`）。
- **FR-07 corrupt 防護**：PlayerPrefs JSON 解析失敗（格式損壞/缺欄位）→ **回傳空榜單且不 crash**（下次 Submit 覆寫重建）；寫入前快取完整榜單於記憶體，避免重複讀取損壞值。

### B. Leaderboard Screen（UI）

- **FR-08 獨立 Canvas**：LeaderboardScreen 為**獨立 Canvas**（非 MenuCanvas/OverlayCanvas 子物件；詞彙表「獨立 Canvas」定案），沿用既有慣例（Screen Space-Overlay、Constant Pixel Size、reference 800×600，shared/project.md）；內容＝榜單列（Top10）＋「返回主畫面」與「重新開始」兩按鈕（決策 C=α 定案）。
- **FR-09 Menu 態瀏覽**：Menu 態可瀏覽榜單（詞彙表「主選單（瀏覽）」定案）；呈現方式（決策 B=i 定案）＝MenuCanvas 在 StartButton 下方新增「排行榜」按鈕，點擊切換至 LeaderboardScreen 瀏覽模式；LeaderboardScreen「返回」鈕回 Menu 主面板。瀏覽模式**禁 Submit**（不寫入）。
- **FR-10 GameOver 態僅上榜時顯示**：GameOver 態下僅當**本局分數通過 Qualify 判定**（FR-05）時顯示 LeaderboardScreen（上榜模式）；未上榜顯示既有 GameOverPane（詞彙表「GameOver（上榜）」定案）。上榜時 LeaderboardScreen **取代** GameOverPane 顯示（決策 C=α 定案）。
- **FR-11 返回鈕雙態行為**：LeaderboardScreen 返回鈕——Menu 態回 MenuCanvas 主面板；GameOver 上榜態提供「返回主畫面」（BackToMenu）與「重新開始」（Restart）兩按鈕（決策 C=α 定案）。**不得新增 GameState**（NFR-01）——一律走既有 GameManager 公開方法（BackToMenu/Restart）。
- **FR-12 最高分 UI 顯示退休**：移除 High Score 文字顯示——`UIController.cs:140-141`（Menu 態 `menuHighScoreText`）、`:175-194`（GameOver 破紀錄分支）、`:210-219` `PlayHighScoreCountUp`（破紀錄跳動）退休；`menuHighScoreText`/`gameOverHighScoreText` 欄位（:20-21/:27）與 `highScoreCountDuration`（:50）移除。**ScoreSession 資料與判定不變**（NFR-02：`flipbird_high_score` 照寫、`RecordBroken` 照算——僅顯示退休，詞彙表定案）。

### C. 上榜流程與演出

- **FR-13 上榜流程接線**：GameOver 態（`OnStateChanged(GameOver)`，UIController.cs:163）→ `Leaderboard.LoadAll` → `LeaderboardRules.Qualifies(CurrentScore, entries)`（FR-05）→ **上榜**：顯示 LeaderboardScreen（上榜模式）＋名稱輸入 modal（決策 A=a 定案）——TMP Input Field **預填上次輸入**（經 `gameManager.PlayerName.Load()`，PlayerNameStore seam，ADR-015 決策 3）、上限 12 字元、確認／取消（Esc）；首次無記錄欄位空白；**留空按確認＝取消＝丟棄不上榜**；取消/Esc＝丟棄不上榜。**時序（ADR-015 修訂 6）**：Submit 確認後才播 Qualify Animation——玩家確認 → `gameManager.PlayerName.Save(name)`（記憶，下次預填）＋ `Leaderboard.Submit(entry, callback)` → callback 內 `LeaderboardAnimator.PlayQualify(result)`（**動畫在 Submit 之後，非 Qualifies 之後**）；**未上榜**：顯示既有 GameOverPane（High Score 文字已退休，不顯示）。`CurrentScore` 於 UI 收到 GameOver 時已為最終值（現況診斷 6）。
- **FR-14 Qualify Animation**：新條目從**榜底以分數滾動（0→目標、2 秒 easeOut）滑入**、名次依滾動值**即時重排**（詞彙表定案；沿用 UITween 原語——AnchoredPositionTo 滑入＋FloatTo 滾分＋逐列 anchoredPosition 重排；動畫目標用榜單列 child RectTransform，B3 教訓）。動畫中離開（返回/重開/回 Menu）→ 停止並歸位（比照 StopPanelTweens，現況診斷 9／BC-5）。

## 非功能需求（NFR）

- **NFR-01 不新增 GameState**：Leaderboard Screen 於 Menu／GameOver 兩態內顯示，**非獨立 GameState**（詞彙表定案）——狀態機（ADR-006）與凍結政策（D3）零變更；不觸及 `SetState`/`IsPlaying`。
- **NFR-02 ScoreSession 資料不變**：最高分資料保留於 ScoreSession（key `flipbird_high_score`、`End()` 破紀錄覆寫流程、`RecordBroken` 判定）**全部不變**——僅 UI 顯示退休（FR-12）；Leaderboard 持久化獨立於 HighScore（新 key `flipbird_leaderboard`），兩者不互相覆寫。
- **NFR-03 UI 不直接觸碰 PlayerPrefs**：榜單與名稱讀寫一律經 `GameManager.Leaderboard`（Provider）／`GameManager.PlayerName`（PlayerNameStore seam，ADR-015 決策 3）——比照 S3/S4（ScoreSession 深模組模式）；UI 不得新增任何 PlayerPrefs 呼叫（grep 驗收：UIController.cs 零 PlayerPrefs）。
- **NFR-04 凍結政策/狀態轉移不變**：LeaderboardScreen 顯示不影響 `IsPlaying()` 語義與各系統自查守衛；返回/重開走既有 GameManager 公開方法（BackToMenu/Restart），狀態轉移路徑零新增。
- **NFR-05 Provider 接口可換實作**：LeaderboardProvider 接口契約穩定（async callback）——未來 LootLocker 實作（詞彙表定案方向）僅需新增實作類，呼叫端（UIController/GameManager）零改動。
- **NFR-06 名稱輸入不阻斷主流程**：名稱輸入 modal（決策 A=a 定案）不得阻斷遊戲主流程——取消/Esc/留空按確認＝丟棄不上榜（BC-4）、返回鈕恆可用、輸入中離開不阻塞。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | LeaderboardEntry＋Provider 接口＋LocalLeaderboardProvider＋PlayerNameStore | P0 | gameplay-developer | **yes** | 資料模型＋統一接口＋PlayerPrefs JSON 實作＋Qualify 規則（含 null/空陣列）＋corrupt 防護＋名稱 seam＋測試 seam | `Assets/Scripts/Leaderboard/`（新目錄，4 檔） | LoadAll/Submit 正確；排序/FCFS/截斷/Qualify（含 null）/corrupt 全通過 EditMode 測試 |
| T2 | GameManager Leaderboard＋PlayerName accessor | P0 | gameplay-developer | **yes** | Awake new 兩實作（否決 [SerializeField] interface，ADR-015 決策 2）；暴露唯讀 accessor（S3） | `Assets/Scripts/GameManager.cs` | `gameManager.Leaderboard` 可 LoadAll/Submit；`gameManager.PlayerName` 可 Load/Save；GameManager 其餘零改動 |
| T3 | LeaderboardScreen Canvas 場景 | P0 | scene-developer | **yes** | 獨立 Canvas＋榜單列結構＋按鈕（返回主畫面＋重新開始，決策 C=α）＋MenuCanvas 排行榜按鈕（決策 B=i）＋接線 | `Assets/Scenes/SampleScene.unity` | Canvas 慣例正確（800×600）；榜單列 Top10 結構；按鈕接線完整 |
| T4 | UIController 顯示/上榜流程＋High Score 退休 | P0 | scene-developer | **yes** | Menu 瀏覽顯示＋GameOver 上榜流程（FR-13）＋High Score 文字退休（FR-12） | `Assets/Scripts/UI/UIController.cs` | 上榜流程完整；未上榜顯示 GameOverPane 無 High Score 文字；UIController 零 PlayerPrefs |
| T5 | LeaderboardAnimator 演出元件 | P0 | scene-developer | no | 新建獨立演出元件（Render/PlayQualify/StopAll）——榜底滑入＋滾分 2 秒 easeOut＋即時重排（ADR-015 決策 5/6） | `Assets/Scripts/UI/LeaderboardAnimator.cs`（新） | 新條目滑入、分數 0→目標滾動、名次隨滾動值即時重排；離開中斷歸位（StopAll） |
| T6 | 名稱輸入 modal | P1 | scene-developer | no | TMP Input Field modal（預填/12 字元/確認取消）＋經 `gameManager.PlayerName`（PlayerNameStore seam，ADR-015 決策 3） | `Assets/Scenes/SampleScene.unity` | 預填記憶；12 字元上限；留空按確認＝取消不上榜（BC-4）；UIController 零 PlayerPrefs |

### T1 — LeaderboardEntry＋Provider 接口＋LocalLeaderboardProvider＋PlayerNameStore（P0, gameplay-developer, arch-review: yes）
- **Goal**：持久化層深模組（比照 ScoreSession 模式）——資料模型、統一接口、本地 PlayerPrefs JSON 實作、Qualify 規則。
- **作法**：
  - 新建 `Assets/Scripts/Leaderboard/` 目錄（CONTEXT.md :67-71 新系統子目錄慣例）。
  - `LeaderboardEntry.cs`：`[Serializable] public class LeaderboardEntry { public string playerName; public int score; }`（FR-01）。
  - `LeaderboardProvider.cs`：`public interface LeaderboardProvider { void LoadAll(Action<LeaderboardEntry[]> callback); void Submit(LeaderboardEntry entry, Action<SubmitResult> callback); }`＋`public readonly struct SubmitResult { public bool Qualified; public int Rank; public LeaderboardEntry[] Entries; }`（FR-02/FR-06）＋`public static class LeaderboardRules { public static bool Qualifies(int score, LeaderboardEntry[] entries); }`（FR-05，純函式）＋JSON wrapper class（`[Serializable] class LeaderboardData { public LeaderboardEntry[] entries; }`，A1 防護）。
  - `LocalLeaderboardProvider.cs`：實作 `LeaderboardProvider`；PlayerPrefs key `flipbird_leaderboard`（FR-03）；讀取→記憶體快照；Submit＝插入→排序（降冪、同分 FCFS）→截斷 10 筆→序列化寫回→回傳 SubmitResult（FR-04/FR-06）；解析失敗回傳空榜單不 crash（FR-07）；`internal` 建構 seam 供 EditMode 注入 fake persistence（比照 ScoreSession.cs:60-63）。
  - `PlayerNameStore.cs`：名稱記憶 seam（ADR-015 決策 3）——key `flipbird_player_name`、`Load()`/`Save(string)`、PlayerPrefs 內部 adapter＋internal 測試 seam（比照 ScoreSession 模式）；**獨立於 LeaderboardProvider**（名稱記憶非榜單職責，不污染接口契約）。
  - **開工先 unity_reflect 驗證**：JsonUtility 裸陣列行為（未驗證假設 A1）——或直接採 wrapper class 慣例。
- **驗收**：EditMode 測試——10 筆上限、降冪排序、同分 FCFS（先插入在上）、Qualify 三規則（<10 須 >0；=10 須 >10th；與 10th 同分不算）＋**null/空陣列視為空榜單（score>0 上榜，ADR-015 修訂 5）**、Submit 原子回傳、corrupt JSON 回空榜單不 crash、fake persistence 注入生效（不碰 PlayerPrefs）、PlayerNameStore Load/Save 正確。

### T2 — GameManager Leaderboard＋PlayerName accessor（P0, gameplay-developer, arch-review: yes）
- **Goal**：GameManager 暴露 Leaderboard Provider 與 PlayerNameStore 唯讀 accessor（鏡像 Score，S3；ADR-015 決策 2）。
- **作法**：
  - Awake 內 `new LocalLeaderboardProvider()`＋`new PlayerNameStore()`（比照 `scoreSession = new ScoreSession()` 既有模式）；暴露 `public LeaderboardProvider Leaderboard { get; }`＋`public PlayerNameStore PlayerName { get; }`。
  - **明確否決 `[SerializeField] LeaderboardProvider`**——Unity 序列化器不支援 interface 型別欄位，會靜默忽略（不報錯、永不賦值、Inspector 顯示空），T2 完成即 NRE（ADR-015 決策 2 裁定）。
  - 加註解（比照 :26 Score accessor 註解——「榜單/名稱模組唯讀 accessor（S3）——UI 經此讀取／提交，不直接觸碰 PlayerPrefs」）。
  - **不改**：狀態機、SetState、EndGame 守衛、Score accessor、其餘公開接口。
- **驗收**：`gameManager.Leaderboard` 可 LoadAll/Submit；`gameManager.PlayerName` 可 Load/Save；編譯乾淨；GameManager 其餘行為零回歸。

### T3 — LeaderboardScreen Canvas 場景（P0, scene-developer, arch-review: yes）
- **Goal**：獨立 LeaderboardScreen Canvas＋榜單列結構＋按鈕，場景接線。
- **作法**：
  - 場景新增 LeaderboardScreen（獨立 Canvas，Screen Space-Overlay、Constant Pixel Size、800×600，比照 ScoreCanvas 慣例）；內容＝標題（可沿用 GameOverTitle 樣式）、榜單列容器（Top10，row 可 prefab 或程式產生，見下）、「返回主畫面」＋「重新開始」兩按鈕（決策 C=α 定案）。
  - 榜單列 row：TMP 文字（名次＋名字＋分數，Kenney Future SDF）；動畫目標用 row 的 child RectTransform（B3 教訓：非根 Canvas）。
  - MenuCanvas 在 StartButton 下方加「排行榜」按鈕（決策 B=i 定案）——接線至 UIController 顯示 LeaderboardScreen。
  - 按鈕樣式沿用 button_rectangle_flat 9-slice；EventSystem 沿用既有（InputSystemUIInputModule，現況診斷 7）。
  - 接線：UIController 序列化欄位（leaderboardScreen、榜單列容器、返回/重開按鈕）。
  - **不改**：MenuCanvas/OverlayCanvas 既有結構（除 MenuCanvas 新增排行榜按鈕）；GameManager/ScoreCanvas。
- **驗收**：LeaderboardScreen 為獨立 Canvas（非 MenuCanvas/OverlayCanvas 子物件）；慣例正確；榜單列 Top10 結構完整；按鈕接線後 UI 事件正確觸發；無 missing script。

### T4 — UIController 顯示/上榜流程＋High Score 退休（P0, scene-developer, arch-review: yes）
- **Goal**：Menu 瀏覽顯示＋GameOver 上榜流程（FR-13）＋High Score 文字退休（FR-12）。
- **作法**：
  - `OnStateChanged` switch：
    - `case Menu:`——移除 `menuHighScoreText` 更新（:140-141）；決策 B=i 定案：Menu 主面板＋排行榜按鈕（StartButton 下方），點擊切換 LeaderboardScreen 瀏覽模式。
    - `case GameOver:`——移除 High Score 分支（:175-194）與 `PlayHighScoreCountUp`（:210-219）呼叫；新增上榜流程（FR-13）：`gameManager.Leaderboard.LoadAll` → `LeaderboardRules.Qualifies` → 上榜顯示 LeaderboardScreen（取代 GameOverPane，決策 C=α 定案）＋名稱 modal（決策 A=a）＋Submit（**確認後才觸發 `leaderboardAnimator.PlayQualify`**，ADR-015 修訂 6）；未上榜顯示既有 GameOverPane（Score/GameOverTitle 文字保留）。
  - 移除欄位：`menuHighScoreText`（:21）、`gameOverHighScoreText`（:27）、`highScoreCountDuration`（:50）；移除 `PlayHighScoreCountUp`（:210-219）與 `StopPanelTweens` 內對應停止呼叫（:248-252）。
  - 按鈕接線：LeaderboardScreen 返回鈕（Menu 態→回 Menu 主面板；GameOver 態→BackToMenu/Restart 兩鈕，決策 C=α）、MenuCanvas 排行榜按鈕（決策 B=i）。
  - 演出觸發：序列化欄位 `leaderboardAnimator`（LeaderboardAnimator，T5 產物）；`StopPanelTweens` 加 `leaderboardAnimator.StopAll()`（中斷歸位觸發點，ADR-015 決策 7）。
  - **不改**：`OnScoreChanged`（HUD 逐分）、Ready/Playing/Dying/Paused case、GameManager 命令呼叫。
- **驗收**：Menu 態榜單可瀏覽、無 High Score 文字；GameOver 上榜流程完整（Qualify→名稱→Submit→演出）；未上榜顯示 GameOverPane 且無 High Score 文字；`grep PlayerPrefs Assets/Scripts/UI/` 零命中（NFR-03）。

### T5 — LeaderboardAnimator 獨立演出元件（P0, scene-developer, arch-review: no；ADR-015 決策 5/6 裁決）
- **Goal**：上榜演出——新條目榜底滑入＋分數滾動 0→目標 2 秒 easeOut＋名次即時重排（詞彙表定案）。
- **作法**：
  - 新建 `Assets/Scripts/UI/LeaderboardAnimator.cs` **獨立演出元件**——`Render(LeaderboardEntry[] entries)`（渲染榜單列）、`PlayQualify(SubmitResult result)`（上榜演出）、`StopAll()`（中斷歸位）；**UIController 只負責「何時觸發」，演出元件負責「怎麼演」**（ADR-015 決策 5：職責分離——UIController 流程編排、LeaderboardAnimator 演出執行；放 `Assets/Scripts/UI/`——演出是 UI 職責，持久化層不得依賴 UI）。
  - 演出細節（沿用 UITween 原語，UITween 零改動）：
    - 依 SubmitResult.Entries 渲染榜單；新條目 row 從榜底（+offset）以 `AnchoredPositionTo`（EaseOutBack 或 easeOut 系）滑入靜止位置。
    - 分數滾動：`FloatTo`（0→目標分數，2 秒，easeOut 系）逐格更新該 row 的分數 TMP（比照 PlayHighScoreCountUp 模式，但 2 秒＋easeOut）。
    - 即時重排：滾動期間依目前滾動值與既有條目比較，逐列 `AnchoredPositionTo` 到新名次位置（EaseOut 系）——動畫目標一律 row child RectTransform（B3 教訓）。
    - 中斷歸位（BC-5）：離開榜單畫面（返回鈕/BackToMenu/Restart/OnStateChanged 開頭）→ `StopAll()` 停止該榜單相關全部 tween（逐列 StopAll＋滾分 StopFloatTweens）並歸位——比照 StopPanelTweens 先例（UIController.cs:133/241-253）；UIController 的 StopPanelTweens 呼叫 `leaderboardAnimator.StopAll()`（ADR-015 決策 7 中斷歸位觸發點）。
  - **不改**：UITween.cs（沿用既有原語）；GameManager/狀態機。
- **驗收**：`LeaderboardAnimator` 三接口（Render/PlayQualify/StopAll）完整；新條目自榜底滑入；分數 0→目標 2 秒 easeOut 滾動；名次隨滾動值即時重排（中段截圖/逐幀採樣確認）；中斷離開無殘留 tween（歸位）。

### T6 — 名稱輸入 modal（P1, scene-developer, arch-review: no；決策 A=a 定案，確定執行）
- **Goal**：上榜名稱輸入 modal——預填記憶、12 字元上限、確認／取消（Esc）。
- **作法**：LeaderboardScreen 新增名稱輸入 modal（上榜模式顯示）——TMP Input Field **預填**上次輸入（經 `gameManager.PlayerName.Load()`，PlayerNameStore seam，ADR-015 決策 3；**UIController 零 PlayerPrefs**，NFR-03 grep 驗收）、**上限 12 字元**、**確認／取消（Esc）** 按鈕；首次無記錄欄位空白；**留空按確認＝取消**（丟棄不上榜）；取消/Esc＝丟棄不上榜；確認後以輸入名稱呼叫 Submit（FR-13）並記憶（`gameManager.PlayerName.Save(name)`）；輸入中離開不阻塞（NFR-06）。
- **驗收**：首次上榜顯示空白欄位；輸入後記憶、下次預填沿用；欄位限 12 字元；留空按確認＝取消不上榜；Esc/取消＝丟棄不上榜；輸入中離開不阻塞返回/重開。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 未滿 10 筆的最低門檻 | 榜單 <10 筆時，`score > 0` 即上榜（含 score=1） | FR-05：Qualify 規則（<10 筆 → >0）；T1 EditMode 測試 |
| BC-2 第 10 名同分 | 榜單滿 10 筆且本局分數＝第 10 名分數——**不算上榜**（詞彙表定案） | FR-05：`> 第 10 名` 嚴格大於；T1 EditMode 測試 |
| BC-3 PlayerPrefs JSON corrupt | 榜單字串損壞（手改/版本遷移）——不得 crash、不得阻塞遊戲 | FR-07：解析失敗回傳空榜單；下次 Submit 覆寫重建 |
| BC-4 名稱空白/取消/Esc | 名稱留空按確認、取消鈕或 Esc——不得以空名上榜 | FR-13/決策 A=a：**留空按確認＝取消＝丟棄不上榜**；取消/Esc＝丟棄不上榜；欄位上限 12 字元；T6 驗收 |
| BC-5 動畫中離開 | Qualify Animation 進行中返回/重開/回 Menu——殘留 tween 干擾下一狀態 | FR-14：離開時停止該榜單全部 tween 並歸位（比照 StopPanelTweens）；T5 驗收 |
| BC-6 async 未完成競態 | LoadAll callback 抵達時 GameOver 畫面已被關閉/狀態已變（未來 LootLocker 真 async 時） | FR-13：callback 內以「目前狀態仍為 GameOver」守衛（比照 _isFirstSync 模式）；本地同步回呼時不觸發（A3）；配合 callback 主執行緒保證契約（ADR-015 決策 4） |
| BC-7 破紀錄但未上榜 | `RecordBroken=true` 但分數未過第 10 名門檻（如個人紀錄 35 < 榜單第 10 名 50） | FR-12：High Score 顯示退休後此情境不顯示任何 High Score 文字（排行榜取代）；ScoreSession 資料照寫不衝突（NFR-02） |

## 驗收標準（整體，Validator 對照用）

- [ ] **榜單資料**：Top 10、分數降冪、同分先到先得（先插入在上）；提交後截斷至 10 筆（FR-04）
- [ ] **Qualify 判定**：<10 筆且 score>0 上榜；=10 筆且 score>第 10 名上榜；與第 10 名同分不上榜；**entries 為 null/空陣列視為空榜單（score>0 即上榜）**（FR-05/BC-1/BC-2/ADR-015 修訂 5）
- [ ] **持久化**：PlayerPrefs JSON（key `flipbird_leaderboard`）讀寫正確；corrupt 回空榜單不 crash；UI 零 PlayerPrefs 呼叫（FR-03/FR-07/NFR-03）
- [ ] **接口**：LeaderboardProvider（async callback）LoadAll/Submit 契約正確；SubmitResult 含 Qualified/Rank/Entries；**callback 主執行緒保證＋同步回呼容忍（callback 內執行顯示動作）＋Submit callback 內不再 LoadAll**；未來換實作呼叫端零改動（FR-02/NFR-05/ADR-015 決策 4）
- [ ] **排行榜畫面**：獨立 Canvas（800×600 慣例）；Menu 態可瀏覽（排行榜按鈕切換，決策 B=i）；GameOver 態僅上榜時顯示（決策 C=α）；返回鈕行為正確；**無新增 GameState**（FR-08~11/NFR-01）
- [ ] **High Score 顯示退休**：Menu/GameOver 均無 "High Score" 文字；`menuHighScoreText`/`gameOverHighScoreText`/`PlayHighScoreCountUp` 已移除；ScoreSession 資料與 `flipbird_high_score` 照寫不變（FR-12/NFR-02）
- [ ] **上榜流程**：GameOver→LoadAll→Qualifies→名稱 modal（決策 A=a）→Submit→**確認後才播演出**完整；未上榜顯示 GameOverPane（Score/標題保留）（FR-13/ADR-015 修訂 6）
- [ ] **上榜按鈕**：LeaderboardScreen 提供「返回主畫面」（BackToMenu）與「重新開始」（Restart）兩按鈕且行為正確（FR-11/決策 C=α）
- [ ] **名稱 modal**：預填上次輸入（經 `gameManager.PlayerName.Load()`，PlayerNameStore seam）、上限 12 字元、確認／取消（Esc）；首次空白、留空按確認＝取消不上榜；**UIController 零 PlayerPrefs（grep 驗收）**（FR-13/決策 A=a/BC-4/ADR-015 決策 3）
- [ ] **Qualify Animation**：新條目榜底滑入＋分數 0→目標 2 秒 easeOut 滾動＋名次即時重排；動畫中離開歸位無殘留（FR-14/BC-5）
- [ ] **不回歸**：狀態機/凍結政策零變更；Ready/Playing/Dying/Paused case 行為不變；HUD 逐分（OnScoreChanged）正常；Restart/BackToMenu 路徑正常（NFR-01/NFR-04）

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（CONTEXT.md:30-36、UIController.cs:20-21/27/50/133/140-141/163/175-194/210-219/241-253、ScoreSession.cs:11-15/18-32/46/57-63/87-96、GameManager.cs:26/173、shared/project.md「遊戲狀態機」「UI 動畫」節、ui-animations FR-07、ADR-008、ADR-006、ADR-011、ADR-015、specs/architecture/leaderboard-system.md（LB1-LB8）、CONTEXT.md:67-71 目錄慣例）。
- **未驗證假設**：A1（JsonUtility 裸陣列——開工 unity_reflect 驗證或採 wrapper class）、A2（PlayerPrefs 字串容量——安全）、A3（async callback 同步回呼——UI 流程容忍）、A4（TMP Input Field 可用性——T6 開工確認）。
- **一致性**：BC-1/BC-2 與 FR-05/T1 一致（Qualify 三規則）；BC-3 與 FR-07/T1 一致（corrupt 防護）；BC-4 與 FR-13/決策 A=a/T6 一致（名稱 modal：留空按確認＝取消不上榜）；BC-5 與 FR-14/T5 一致（動畫中斷歸位）；BC-6 與 FR-13/T4 一致（async 競態守衛）；BC-7 與 FR-12/T4 一致（破紀錄未上榜無顯示）；FR-02 契約三點與 ADR-015 決策 4／LB2 一致；FR-05 null 處理與 ADR-015 修訂 5／LB6 一致；FR-13 PlayerName seam 與 ADR-015 決策 3 一致；T5 歸屬與 ADR-015 決策 5/6 一致。
- **Editor API 驗證**：T1 涉及 runtime API（JsonUtility/PlayerPrefs 既有，無新 API）；A1 僅行為確認非新 API——開工 unity_reflect 驗證 JsonUtility 裸陣列行為即可；本功能無 Editor API 依賴。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**。已由 Tech Lead 完成審查並接受（`docs/adr/015-leaderboard.md`——規格事實主張全數核實正確、零勘誤；6 條修訂建議已依序落實於本規格，見 FR-02/FR-05/FR-13/T1/T2/T5/T6）。範圍：

1. **新共享元件 LeaderboardProvider 接口＋LocalLeaderboardProvider**（T1/T2）：新增跨腳本依賴（UIController→GameManager.Leaderboard→Provider）；新系統目錄 `Assets/Scripts/Leaderboard/`（CONTEXT.md 慣例）；需 Tech Lead 審視接口契約（async callback）、與 ScoreSession 深模組模式的一致性（ADR-008 S2-S4）、dependency-rules 是否新增規則。
2. **持久化模型擴張**（T1）：PlayerPrefs 單值（`flipbird_high_score`）→JSON 榜單（`flipbird_leaderboard`）——最高分資料保留於 ScoreSession、顯示退休由排行榜取代（詞彙表定案）；需 Tech Lead 確認資料模型分離（兩 key 不互相覆寫、ScoreSession 零改動）並更新架構文件。
3. **UI 顯示退休**（T4）：移除 Menu/GameOver High Score 文字顯示（含 ui-animations FR-07 破紀錄跳動退休）——跨規格變更（game-states「最高分」、ui-animations FR-07），需 Tech Lead 更新 `specs/architecture/game-states.md` 與相關文件。
4. **Qualify Animation 新演出模式**（T5）：榜單列動態重排（滾動分數＋即時名次重排）——UITween 既有原語可達、不觸發新架構，但「列重排」為新動畫模式，需 Tech Lead 確認與 ui-animations 的動畫職責劃分（歸 UIController 或獨立演出元件）。
5. **非獨立 GameState 確認**（T3/T4）：Leaderboard Screen 於 Menu/GameOver 兩態內顯示、**不新增 GameState**（詞彙表定案）——與 ADR-006 狀態機/凍結政策相容性確認；決策 B/C（Menu 呈現、GameOver 上榜流程）已定案（方案 i／α），一併納入架構審查。