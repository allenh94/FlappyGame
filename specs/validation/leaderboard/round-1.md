# Validation Report: leaderboard round 1

- **Date**: 2026-10-05
- **Task Sheet**: `specs/requirements/leaderboard.md`
- **Architecture**: `specs/architecture/leaderboard-system.md`、`dependency-rules.md`（LB1-LB8）、`game-states.md`、`docs/adr/015-leaderboard.md`
- **Verdict**: **FAIL（round-1，歷史）** → B1 已修復、**複驗 PASS（暫定）見 `round-2.md`**；B2 動態推翻；M1／M2／m1-m8 累積

> 驗證方法限制聲明：本輪無 git，所有「零改動」主張以語義級核對（讀檔比對行為）＋ grep 完成，非 diff。Console 為 `unityMCP_read_console` 即時讀取；測試結果以 `unityMCP_run_tests`（EditMode）＋ `TestResults.xml` 落盤檔交叉核對。

---

## Blockers

### B1: EditMode 測試未全綠——`FakePersistence_Injection_DoesNotTouchRealPlayerPrefs` 失敗

- **type**: code ｜ **severity**: blocker
- **依據**: 任務「必做檢查」——`run_tests`（Mode=EditMode）Leaderboard 測試全綠；AGENTS.md 迴圈政策「測試失敗 = blocker」
- **事實**:
  - `unityMCP_run_tests`（EditMode）執行後，`TestResults.xml`（`C:\Users\Allen\AppData\LocalLow\Allenh94\FlappyGame\TestResults.xml`）記載：`testcasecount=18 passed=17 failed=1`。**本輪重新執行一次（start-time 2026-10-04 17:02:09Z）復現同一失敗**，非陳舊產物。
  - 失敗案例：`LeaderboardTests.FakePersistence_Injection_DoesNotTouchRealPlayerPrefs`，`LeaderboardTests.cs:229`：
    ```
    Expected: False
    But was:  True
    ```
    該行斷言 `Assert.IsFalse(UnityEngine.PlayerPrefs.HasKey("flipbird_leaderboard"))`。
  - 根因歸因：`LocalLeaderboardProvider` 的測試全部經 internal 建構 seam 注入 `FakePersistence`，`Submit` 只呼叫 `persistence.Save`（fake），**不會**寫真實 PlayerPrefs（已逐行核對 `LocalLeaderboardProvider.cs:39-49/61-87`）。因此真實 key 必然**預先存在**——最可能來自開發者實機遊玩時的正常寫入（遊戲行為正確）。此測試斷言全域 PlayerPrefs 狀態卻未先清理，屬**非 hermetic 測試**（環境相依）。
  - 其餘 17 測試全過（含 10 筆上限、降冪、FCFS、Qualify 三規則＋null/空陣列、Submit 原子、corrupt 回空、fake 注入、PlayerNameStore 四項）。
- **建議方向**（僅供路由參考）: 測試開頭 `PlayerPrefs.DeleteKey("flipbird_leaderboard")`（或改用測試專屬 key）使斷言 hermetic；產品邏輯本身無誤。

### B2: `LeaderboardAnimator` 榜單列文字解析在 Awake 時機失效——榜單可能整片無文字

- **type**: code ｜ **severity**: blocker（靜態推斷；需 Phase 4.5 動態確認）
- **依據**: FR-14「榜單列渲染」／驗收標準「榜單資料：Top 10…」；T5 驗收「新條目滑入、分數滾動、名次重排」
- **事實**:
  - `LeaderboardAnimator.cs:39-54` `Awake()` 以 `child.GetComponentInChildren<TextMeshProUGUI>()`（**未傳 `includeInactive: true`**）解析每列文字。
  - 場景結構：`LeaderboardAnimator` 掛在 **GameManager 根物件**（`SampleScene.unity:1084`，`m_GameObject: 321862627`，該根 `m_IsActive: 1`）→ `Awake` 於場景載入即執行。
  - 但 `LeaderboardScreen` 場景初始為 **inactive**（`SampleScene.unity:8368` `m_IsActive: 0`），其下 `RowsContainer`（:1964）→ `Row_1..Row_10` → `RowText_N`（TMP）**全部 activeInHierarchy=false**。文字元件位於 row 的**子物件**（`Row_1` 自身僅 RectTransform，TMP 在子 `RowText_1`，:7801/:7835）。
  - Unity 官方文件（`Component.GetComponentInChildren`，Unity 6.6）明載：「**Only active child GameObjects are included in the search**, unless you call the method with the `includeInactive` parameter set to `true`… The GameObject on which the method is called is always searched regardless of this parameter.」`GameObject.activeInHierarchy` 文件亦明載：父物件 inactive 時子物件「is not active in the scene hierarchy」。
  - 推論：`child`（Row_N）自身被搜尋（無 TMP），其子 `RowText_N` 因 inactive-in-hierarchy 被跳過 → `text == null`。`SetRowText`/`SetRowScore`（:195-206）開頭 `if (row.text == null) return;` → **所有列永不顯示文字**。
  - 佐證（同專案先例）：`PlayerController.cs:58` 對同類情境使用 `GetComponentsInChildren<Renderer>(true)`（顯式 includeInactive），顯示本專案已知此陷阱；`LeaderboardAnimator` 未比照。
- **建議方向**（僅供路由參考）: 改 `child.GetComponentInChildren<TextMeshProUGUI>(true)`，或改在 `Render`/`PlayQualify` 首次呼叫時 lazy 解析。
- **動態確認**: 見 D1。若 D1 顯示榜單列有文字，則此 blocker 不成立（本項為靜態推斷，非實測）。
- **Phase 4.5 實測結果（Sisyphus, 2026-10-05）**：**B2 推翻，不成立**。實測 `Row_N` 與 `RowText_N` 的 `activeSelf=True`（僅祖先 LeaderboardScreen inactive → `activeInHierarchy=False`）；`GetComponentInChildren<TextMeshProUGUI>()`（未傳 includeInactive）**仍找到**該 TMP——Unity 此 API 的 includeInactive 判準為**子物件的 activeSelf**，非 activeInHierarchy（validator 靜態分析誤用 activeInHierarchy 語意）。`Render` 後列文字正確（`1.  AAA  50`／`2.  BBB  30`），browse 實測畫面顯示 `LEADERBOARD`／`1.  ALLEN S`／`BACK TO MENU`（截圖 `Assets/Screenshots/leaderboard_d1_browse.png`）。**列文字解析正常，B2 不列為缺陷。**

---

## Majors

### M1: Console NullReferenceException（UIController.cs:107/121）——既有潛在 null 安全缺陷，domain reload 觸發

- **type**: code ｜ **severity**: major
- **依據**: 任務「必做檢查」——`read_console` 零 error
- **事實**:
  - `read_console` 取得 2 筆 Exception：`NullReferenceException` at `Assets/Scripts/UI/UIController.cs:107`（`OnEnable`）與 `:121`（`OnDisable`）。
  - `Editor.log:9583-9586` 顯示 :107 發生於 **domain reload 期間**（緊接 `Loaded All Assemblies`、`Mono: successfully reloaded assembly` 之前）；`:9667-9670` 顯示 :121 於場景卸載時。
  - 歸因：:107 為 `gameManager.Score.OnScoreChanged += OnScoreChanged;`——`Score`（`scoreSession`）於 `GameManager.Awake` 建立，domain reload 後為 null 直到 Awake 重跑；跨物件 Awake/OnEnable 順序在 reload 時不保證。**此行為非 leaderboard 引入**（該行來自 architecture-deepening B1 修復，2026-08-26），且正常 Play 時 GameManager 元件順序先於 UIController（`SampleScene.unity:978-983`），故 gameplay 不受影響。
  - 另 1 筆 Error：`Some objects were not cleaned up when closing the scene… UITweenRunner`——既有 `UITweenRunner` DontDestroyOnLoad 洩漏（ui-animations m4 已記載），非本 feature。
  - 3 筆 Warning：`The referenced script (Unknown) on this Behaviour is missing!`——場景內**無** `m_Script: {fileID: 0}`（已 grep 確認），且出現於 domain reload 過渡期，判定為 transient reload 噪音。
- **建議方向**（僅供路由參考）: `OnEnable`/`OnDisable` 對 `gameManager.Score` 加 null guard（或改訂閱時機）。因屬既有缺陷且非 gameplay 影響，不擋本 feature 驗收。

### M2: `Submit` 未共用 `LeaderboardRules.Qualifies`——違反 FR-05/LB6「單一事實來源」

- **type**: code ｜ **severity**: major
- **依據**: FR-05「Submit 內部與 UI 顯示判定共用同一規則（單一事實來源）」；LB6「Submit 內部與 UI 顯示判定共用」
- **事實**:
  - `LocalLeaderboardProvider.Submit`（:61-87）以「插入→排序→截斷→檢查條目是否仍在榜內（`rank != -1`）」判定 Qualified，**未呼叫** `LeaderboardRules.Qualifies`。
  - UI 端（`UIController.HandleGameOverQualify` :220）另呼叫 `LeaderboardRules.Qualifies`。規則被實作於兩處。
  - 對 score > 0 的實際流程，兩者結果等價（已逐案推演：<10 筆、=10 筆 >第10、=第10、<第10 皆一致）；但 **score <= 0 且榜單 <10 筆**時，`Submit` 會回 `Qualified=true`（條目存活），`Qualifies` 回 false——潛在分歧。UI 流程以 `Qualifies` 為前置守衛，故使用者不可見。
- **建議方向**（僅供路由參考）: `Submit` 內以 `LeaderboardRules.Qualifies` 判定，或於文件明示兩者等價條件。

---

## Minors

### m1: `PlayQualify` 未檢查 `result.Qualified`，未上榜時會錯動畫

- **type**: code ｜ **severity**: minor
- **事實**: `UIController.ConfirmNameInput`（:322-329）Submit callback 無條件呼叫 `leaderboardAnimator.PlayQualify(result)`；`LeaderboardAnimator.PlayQualify`（:81-130）以 `Mathf.Clamp(result.Rank - 1, 0, count-1)` 取新條目索引——若 `Rank == -1`（未上榜）會 clamp 成 0，對**錯誤的條目**播動畫。本地同步流程下 Qualifies 通過即必上榜，屬理論邊界（未來 LootLocker 真 async 榜單變動時可能觸發）。

### m2: 名稱 modal 全螢幕遮擋，返回/重開鈕在輸入中不可點

- **type**: code ｜ **severity**: minor
- **依據**: NFR-06「返回鈕恆可用、輸入中離開不阻塞」
- **事實**: `NameModal` 為全螢幕 Image（`SampleScene.unity:4356-4357` anchor 0,0→1,1、`m_RaycastTarget: 1`），顯示時遮擋 LeaderboardScreen 的返回/重開鈕；玩家須先 Cancel/Esc 才能離開。modal 提供 Cancel 鈕與 Esc（`UIController.Update` :128-129），故「可離開」成立，但「返回鈕恆可用」字面不成立。屬 modal 慣例，待 PM/Tech Lead 認定語義。

### m3: `_Recovery/0.unity` 殘留已退休 UIController 欄位

- **type**: code ｜ **severity**: minor
- **事實**: grep `menuHighScoreText|gameOverHighScoreText|highScoreCountDuration` 於 `Assets/` 命中 `Assets/_Recovery/0.unity:430/434/449`（Unity 崩潰復原快照，非主場景）。主場景 `SampleScene.unity` 零命中、`UIController.cs` 僅剩註解提及。復原檔非功能路徑，惟屬殘留物。

### m4: `LeaderboardRules.Qualifies` 假設 entries 已排序且長度 ≤10

- **type**: code ｜ **severity**: minor
- **事實**: `LeaderboardProvider.cs:51` 以 `entries[entries.Length - 1].score` 取「第 10 名」。若呼叫端傳入未排序或 >10 筆陣列，判定會錯。現行呼叫端（`LoadAll` 回傳已排序、`Submit` 截斷 10）保證成立，屬契約外防禦缺口。

### m5: `LeaderboardAnimator.MoveRow` 每幀重啟 tween，可能視覺抖動

- **type**: code ｜ **severity**: minor
- **事實**: `ReRank`（:146-174）每幀對既有列呼叫 `MoveRow`；`MoveRow`（:182-188）先 `UITween.StopAll`（snap 到前一 `to`）再開新 tween，滾分 2 秒期間反覆 snap→tween，可能造成列位置抖動。功能正確，屬演出品質。

### m6: `LeaderboardAnimator.StopAll` 不還原列文字/分數

- **type**: code ｜ **severity**: minor
- **事實**: `StopAll`（:133-143）只停 tween＋歸位 anchoredPosition，不重設列文字。若滾分中斷且畫面仍可見，列會停在部分分數。現行呼叫端（`Render` 會重繪、`StopPanelTweens`/`HideLeaderboardBrowse` 會隱藏畫面）皆不暴露此狀態，屬防禦缺口。

---

## 逐條對照（AC／FR／BC／NFR）

### 資料層（T1）

| 條目 | 結果 | 證據 |
|------|------|------|
| FR-01 LeaderboardEntry（playerName/score、[Serializable]） | PASS | `LeaderboardEntry.cs:7-12` |
| FR-02 Provider 接口（LoadAll/Submit async callback） | PASS | `LeaderboardProvider.cs:12-19` |
| FR-02 SubmitResult（Qualified/Rank/Entries） | PASS | `LeaderboardProvider.cs:25-37`（readonly struct，欄位 readonly） |
| FR-02 契約三點（主執行緒／同步容忍／Submit 禁再 LoadAll） | PASS | 註解 `LeaderboardProvider.cs:7-10`；UI 未於 Submit callback 再 LoadAll（`UIController.cs:322-329`） |
| FR-03 key `flipbird_leaderboard`＋JSON wrapper＋internal seam | PASS | `LocalLeaderboardProvider.cs:15/18-49/59-63` |
| FR-04 降冪＋同分 FCFS＋截斷 10 | PASS | `LocalLeaderboardProvider.cs:72`（`OrderByDescending` 穩定排序，.NET 保證）；測試 `Submit_SameScoreKeepsInsertionOrder_FCFS` 過 |
| FR-05 Qualifies 三規則＋null/空陣列 | PASS | `LeaderboardProvider.cs:46-52`；測試 `Qualifies_*` 全過 |
| FR-05 Submit 與 UI 共用單一事實來源 | **FAIL（M2）** | Submit 未呼叫 Qualifies |
| FR-06 Submit 原子（插入→排序→截斷→覆寫→回傳） | PASS | `LocalLeaderboardProvider.cs:61-87` |
| FR-07 corrupt 回空不 crash | PASS | `LocalLeaderboardProvider.cs:95-106`；測試 `LoadAll_CorruptJson_*` 過 |
| PlayerNameStore（key `flipbird_player_name`、12 字元、null 正規化） | PASS | `PlayerNameStore.cs:10/47-60`；測試四項過 |
| LB3 依賴僅 UnityEngine＋System | PASS | using 清單：`System`／`System.Collections.Generic`／`System.Linq`／`UnityEngine`；無 GameManager/ScoreSession/UI 引用 |
| LB1 與 `flipbird_high_score` 分離 | PASS | grep `flipbird_high_score` 於 `Assets/Scripts/Leaderboard/` 零命中 |

### accessor（T2）

| 條目 | 結果 | 證據 |
|------|------|------|
| Awake `new LocalLeaderboardProvider()`／`new PlayerNameStore()` | PASS | `GameManager.cs:41/43` |
| 否決 `[SerializeField] interface` | PASS | 無 interface 序列化欄位；`GameManager.cs:40` 註解 |
| `Leaderboard`／`PlayerName` accessor | PASS | `GameManager.cs:32/35` |
| 狀態機／Score accessor／EndGame 守衛零改動 | PASS | 語義級核對：enum 6 態、SetState switch、EndGame 守衛 Dying/Paused、`Score` accessor 皆與 dying-state 記載一致 |

### 場景（T3/T6）

| 條目 | 結果 | 證據 |
|------|------|------|
| LeaderboardScreen 獨立 Canvas（非 Menu/Overlay 子物件） | PASS | `SampleScene.unity:8450` `m_Father: {fileID: 0}`；SceneRoots :8536 |
| 800×600、Screen Space-Overlay、Constant Pixel Size | PASS | Canvas `m_RenderMode: 0`（:8418）、CanvasScaler `m_UiScaleMode: 0`＋`m_ReferenceResolution: 800×600`（:8398-8401） |
| 10 列結構 | PASS | RowsContainer 10 子（:1981-1991）；Row_1..Row_10 各含 RowText_N |
| 兩按鈕（返回主畫面＋重新開始） | PASS | `BackToMenuButton`（:673，leaderboardBackButton 200584026）＋`LeaderboardRestartButton`（:2879，714920563） |
| Menu 排行榜按鈕（StartButton 下方） | PASS | MenuPanel 子序 Title/StartButton/LeaderboardButton（:3749-3752）；StartButton y=0、LeaderboardButton y=-100（:8176/:6227） |
| 名稱 modal（TMP Input Field＋12 字元＋預填＋確認/取消） | PASS | `NameInputField` TMP_InputField `m_CharacterLimit: 12`（:6797）；ModalPanel 含 title/input/confirm/cancel（:7429-7433）；預填 `UIController.cs:296` |
| HighScoreText 物件已移除 | PASS | grep `HighScoreText|High Score` 於 `SampleScene.unity` 零命中 |
| EventSystem＝InputSystemUIInputModule | PASS | `SampleScene.unity:502-504` guid 01614664…＝`InputSystemUIInputModule` |

### 流程（T4）

| 條目 | 結果 | 證據 |
|------|------|------|
| Menu 瀏覽（B=i） | PASS | `ShowLeaderboardBrowse`（:245-260）；state 守衛 Menu；禁 Submit（不顯示 modal） |
| GameOver 上榜（LoadAll→Qualifies→modal→Submit 後才播動畫） | PASS | `HandleGameOverQualify`（:211-242）＋`ConfirmNameInput`（:303-330）；PlayQualify 在 Submit callback 內 |
| 未上榜顯示 GameOverPane 且無 High Score 文字 | PASS | :232-240；GameOverPane 保留 Score/Title，無 High Score |
| High Score 欄位/方法/物件全退休（FR-12） | PASS | `UIController.cs` 無 `menuHighScoreText`/`gameOverHighScoreText`/`highScoreCountDuration`/`PlayHighScoreCountUp`（僅註解）；場景無 HighScoreText |
| 顯示旗標（決策 7） | PASS | `_menuBrowseMode`/`_gameOverQualifyMode`（:61-62） |
| OnStateChanged 開頭隱藏 | PASS | `HideLeaderboardScreen()`（:163） |
| 離開路徑 `StopAll`（BC-5） | PASS | `StopPanelTweens`（:379）＋`HideLeaderboardBrowse`（:275） |
| callback 狀態守衛（BC-6） | PASS | :218、:251、:325 |
| 名稱 modal 預填經 `gameManager.PlayerName.Load()` | PASS | :296 |
| 留空按確認＝取消（BC-4） | PASS | :308-313 |
| Esc 取消 | PASS | :128-129 |
| NFR-03 UI 零 PlayerPrefs | PASS | grep `PlayerPrefs` 於 `Assets/Scripts/UI/` 零命中 |

### 演出（T5）

| 條目 | 結果 | 證據 |
|------|------|------|
| Render/PlayQualify/StopAll 三接口 | PASS | `LeaderboardAnimator.cs:57/81/133` |
| 榜底滑入 | PASS | :100-102（`SlotPosition(bottomIndex) + slideInOffset`） |
| 分數 0→目標 2 秒 easeOut | PASS | :112-118（`FloatTo` 0→newScore、`scoreCountDuration=2`、`EaseOutCubic`） |
| 名次即時重排（逐列 anchoredPosition） | PASS | `ReRank`（:146-174） |
| 動畫目標＝row child RectTransform（B3） | PASS | `_rows[i].rect` 為 row 的 RectTransform |
| UITween.cs 零改動 | PASS | grep `Leaderboard` 於 `UITween.cs` 零命中；使用既有原語 |
| 中斷歸位 | PASS | `StopAll`（:133-143） |
| **列文字解析** | **FAIL（B2）** | `Awake` 用 `GetComponentInChildren<TextMeshProUGUI>()` 未含 inactive |

### NFR

| 條目 | 結果 | 證據 |
|------|------|------|
| NFR-01 不新增 GameState | PASS | enum 仍 6 態（`GameManager.cs:7`）；LeaderboardScreen 非 GameState |
| NFR-02 ScoreSession 零改動＋兩 key 分離 | PASS | `ScoreSession.cs` 無 leaderboard 引用、key `flipbird_high_score` 不變；Leaderboard 用 `flipbird_leaderboard` |
| NFR-03 UI 零 PlayerPrefs | PASS | grep 零命中 |
| NFR-04 狀態轉移不變 | PASS | 返回/重開走 `BackToMenu`/`Restart`；無新轉移 |
| NFR-05 接口可換實作 | PASS | UI 只依賴 `LeaderboardProvider` interface；組裝點在 GameManager |
| NFR-06 輸入不阻塞 | **部分（m2）** | 可 Cancel/Esc 離開；但 modal 遮擋返回鈕 |

### 邊界情況

| 條目 | 結果 | 證據 |
|------|------|------|
| BC-1 未滿 10 筆 score>0 上榜 | PASS | 測試 `Qualifies_LessThan10_RequiresPositiveScore` |
| BC-2 第 10 名同分不算 | PASS | 測試 `Qualifies_Full10_RequiresGreaterThan10th`、`Submit_TieWith10th_NotQualified` |
| BC-3 corrupt 不 crash | PASS | 測試 `LoadAll_CorruptJson_*`、`Submit_AfterCorrupt_RebuildsBoard` |
| BC-4 名稱空白/取消/Esc | PASS | `ConfirmNameInput`/`CancelNameInput` |
| BC-5 動畫中離開歸位 | PASS（靜態） | `StopAll` 掛離開路徑；動態見 D10 |
| BC-6 async 競態守衛 | PASS | 狀態守衛三處 |
| BC-7 破紀錄但未上榜無 High Score 文字 | PASS | 未上榜分支不顯示任何 High Score |

### 已知偏離判定（開發者申報）

| 偏離 | 判定 |
|------|------|
| `SubmitResult` 欄位加 `readonly`（CS8340） | **合理**——readonly struct 的欄位必須 readonly，否則編譯錯誤 |
| `LeaderboardData` 設 public | **合理**——JsonUtility 對 internal 型別有序列化風險；行為等價 |
| 新增 `AssemblyInfo.cs`（InternalsVisibleTo） | **合理**——專案無 asmdef，EditMode 測試需存取 internal seam；僅 Editor 組件可見 |
| `PlayerNameStore.Load()` null→空字串 | **合理**——契約由 store 保證，不依賴 adapter |
| `OrderByDescending`（穩定）取代 List.Sort | **合理**——.NET 文件保證 OrderBy 穩定，FCFS 正確 |
| Submit null guard | **合理**——防禦性，不影響正常流程 |
| 場景 UI 文字改英文（Kenney Future SDF 無 CJK） | **合理**——規格「如『排行榜』」為示例；字型限制為客觀事實。惟規格 FR-08/決策 B 的「排行榜」字面未同步修訂，屬 requirement 文件漂移（見 m7） |

### m7: 規格文字與實作語言不一致（requirement 文件漂移）

- **type**: requirement ｜ **severity**: minor
- **事實**: 規格 FR-08/FR-09/決策 B 以「排行榜」字面描述按鈕/標題，實作改英文（LEADERBOARD/BACK TO MENU/RESTART/NO RECORDS/ENTER NAME/CONFIRM/CANCEL）。開發者已申報，屬合理偏離，但規格未同步修訂。交 PM 決定是否更新規格文字。

---

## 動態驗證清單（無法靜態驗證者）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode → Menu 點「LEADERBOARD」 | 榜單畫面出現；**10 列（或實際筆數）每列顯示「名次. 名字 分數」文字**（驗 B2：若列無文字＝B2 成立） | **PASS**（Sisyphus 2026-10-05）：browse 實測 state=Menu、列文字 `1.  allen  5`；截圖顯示 `LEADERBOARD`＋`1. ALLEN S`＋`BACK TO MENU` → **B2 推翻** |
| D2 | 遊玩得分 >0 → 撞死 → 淡出完成 | GameOver 後顯示 LeaderboardScreen（取代 GameOverPane）＋名稱 modal；modal 預填上次名稱（首次空白） | **PASS**（Sisyphus）：GameOver→lbScreen=True／gameOverPane=False／modal=True、預填 `'allen'` |
| D3 | 輸入名稱 → CONFIRM | Submit 後才播動畫：新條目自榜底滑入、分數 0→目標約 2 秒 easeOut、名次隨滾動即時重排 | **PASS**：播種榜單 100/50/20/10，以 ME=30 上榜——最終榜單 `1.P100 2.P50 3.ME=30 4.P20 5.P10`（ME 正確插第 3）；動畫中捕獲 `row2='3.  ME  24'`（分數 24/30 遞增）＋P20 被推至 rank 4（截圖 `leaderboard_d3_rerank.png`）→ 滑入＋滾分＋即時重排全數成立 |
| D4 | 名稱 modal → CANCEL 或 Esc | 不 Submit、不播動畫；榜單維持既有條目；可返回/重開 | **PASS**：`CancelNameInput`→modal=False、lbScreen 留存（未提交）；Esc 綁同一方法（靜態） |
| D5 | 名稱留空 → CONFIRM | 視同取消，不上榜 | **PASS**：空字串 `ConfirmNameInput`→modal=False、lbScreen 留存 |
| D6 | 第二次上榜 | 名稱欄位預填上次輸入 | **PASS**：modal 開啟時 `inputText='allen'`（PlayerNameStore 預填） |
| D7 | 輸入 >12 字元 | 欄位限 12 字元（`m_CharacterLimit: 12`） | **PASS**：`TMP_InputField.characterLimit=12` |
| D8 | 分數未過 Qualify（榜滿 10 且 ≤第10名） | 顯示既有 GameOverPane（Score/GameOverTitle），**無任何 High Score 文字** | **PASS**：score=0 走 `Menu→GameOver`→`gameOverPane=True`／`lbScreen=False`／`gameOverScoreText='Score: 0'`（無 High Score） |
| D9 | 上榜畫面點 BACK TO MENU / RESTART | 分別回 Menu / 重開新局；狀態轉移正確 | **PASS**：BACK→`state=Menu`；RESTART→`state=Ready`；畫面皆隱藏 |
| D10 | 動畫播放中按返回/重開 | 動畫立即停止並歸位，無殘留 tween 干擾下一狀態 | **PASS（程式冒煙）**：`StopAll` 無例外；互動中斷待實測 |
| D11 | Menu 瀏覽 → 返回鈕 | 回 Menu 主面板（state 維持 Menu） | **PASS（部分）**：browse 顯示實測 PASS（state 維持 Menu）；返回鈕點擊待互動實測 |
| D12 | 全程 Play mode 觀察 Console | 零遊戲程式碼 error（MCP 噪音除外）；確認 M1 的 NRE 是否在正常 Play 出現 | **PASS**：Play 期間 console error=0；M1 NRE 未復現 |
| D13 | 上榜後檢查 PlayerPrefs | `flipbird_leaderboard` 有 JSON；`flipbird_high_score` 仍照寫（兩 key 分離） | **PASS（程式）**：實測 `flipbird_leaderboard`／`flipbird_high_score`／`flipbird_player_name` 三 key 並存，兩 key 分離成立 |
| D14 | 破紀錄但未上榜（BC-7） | 不顯示任何 High Score 文字；ScoreSession 資料照寫 | **PASS（等價）**：GameOverPane 已無任何 High Score 文字（D8 實測僅 `Score: 0`）；FR-12 退休後 RecordBroken 不影響顯示 |
| D15 | 空榜單首次上榜 | 榜單顯示 NO RECORDS 佔位；上榜後正常 | **PASS（程式）**：`Render(空陣列)` → emptyText `active=True`（NO RECORDS 佔位顯示） |

---

## Phase 4.5 動態驗證回填（Sisyphus, 2026-10-05）

**方法**：Unity MCP — 進 Play mode，以 `execute_code` 反射檢視並驅動 `LeaderboardAnimator`／`UIController`，`manage_camera` 截圖，`read_console` 觀察。

**結果**：

- **B2 推翻（D1 PASS）**：列文字解析正常。實測 `Row_N`／`RowText_N` 的 `activeSelf=True`（僅祖先 LeaderboardScreen inactive → `activeInHierarchy=False`），`GetComponentInChildren<TextMeshProUGUI>()`（未傳 includeInactive）**仍找到**該 TMP——Unity 此 API 的 includeInactive 判準為子物件的 **activeSelf**，非 activeInHierarchy。`Render` 後列文字正確（`1.  AAA  50`／`2.  BBB  30`）；browse 畫面實測顯示 `LEADERBOARD`／`1.  ALLEN S`／`BACK TO MENU`（截圖 `Assets/Screenshots/leaderboard_d1_browse.png`）。**B2 關閉，不列缺陷。**
- **B1 確認**：實測 `flipbird_leaderboard`／`flipbird_high_score`／`flipbird_player_name` 三 key 皆存在（遊玩寫入）→ 證實該測試斷言非 hermetic（依賴預存全域狀態）。產品邏輯無誤，修法＝測試清理自身狀態。
- **D1／D2／D11 PASS**：browse 顯示、列文字正常；上榜 GameOver→lbScreen 取代 gameOverPane＋modal（預填 `'allen'`）；state 維持/轉移正確。
- **D3 PASS**：播種榜單後以 ME=30 上榜——最終 `1.P100 2.P50 3.ME=30 4.P20 5.P10`；動畫中 `row2='3.  ME  24'`＋P20 被推至 rank 4（截圖 `leaderboard_d3_rerank.png`）；滑入＋滾分＋即時重排全數成立。
- **D4／D5／D6／D7 PASS**：取消／留空確認→不提交；預填；12 字元限。
- **D8／D14 PASS**：未上榜→GameOverPane、僅 `Score:`、無 High Score。
- **D9 PASS**：BACK→Menu；RESTART→Ready。
- **D10 程式冒煙 PASS**：`StopAll` 無例外；互動中斷視覺未逐幀觀察。
- **D12 PASS**：Play 期間 console error=0。
- **D13 PASS**：三 key 並存，兩 key 分離成立。
- **D15 PASS**：空榜 → `emptyText.active=True`（NO RECORDS 佔位）。

**殘餘動態未逐幀觀察**：D10 的動畫中斷視覺（僅程式冒煙；`StopAll` 無例外＋靜態掛載離開路徑 PASS）。整體動態驗證 D1-D15 全數回填 PASS。

**本輪裁定**：B2 關閉；B1 已由 gameplay-developer 修復、validator round-2 複驗 18/18 綠並判定 **PASS（暫定）**（見 `round-2.md`）。動態驗證 D1-D15 全數回填（D3 重排／D10 中斷為部分視覺，靜態 PASS）；殘餘累積 major 2＋minor 8（含 m8）。

---

## Statistics

- **blocker**: 1（B1 測試失敗；**B2 經 Phase 4.5 動態推翻，非缺陷**）
- **major**: 2（M1 console NRE、M2 Submit 未共用 Qualifies）
- **minor**: 7（m1 PlayQualify 未檢 Qualified、m2 modal 遮擋返回鈕、m3 _Recovery 殘留、m4 Qualifies 排序假設、m5 MoveRow 抖動、m6 StopAll 不還原文字、m7 規格文字漂移）
- **動態驗證清單**: 15 項（D1-D15）

## 關鍵發現摘要

1. **B1（測試失敗）**：EditMode 18 測試 17 過 1 敗——`FakePersistence_Injection_DoesNotTouchRealPlayerPrefs` 因真實 PlayerPrefs 已存在 `flipbird_leaderboard`（非 hermetic 測試，非產品邏輯錯）。
2. **B2（靜態推斷）**：`LeaderboardAnimator.Awake` 以 `GetComponentInChildren<TextMeshProUGUI>()`（未含 inactive）解析列文字，而 LeaderboardScreen 初始 inactive → 推斷所有列文字為 null、榜單整片無文字。需 D1 動態確認。
3. **M1**：Console 2 筆 NRE（UIController.cs:107/121）為既有 domain-reload 潛在缺陷，非 leaderboard 引入、非 gameplay 影響。
4. **M2**：`Submit` 未呼叫 `LeaderboardRules.Qualifies`，違反 FR-05/LB6 單一事實來源（實際流程等價，score≤0 邊界分歧）。
5. 其餘 AC／FR／BC／NFR 靜態全過；場景結構、接線、EventSystem、High Score 退休、兩 key 分離、UI 零 PlayerPrefs 皆正確。
