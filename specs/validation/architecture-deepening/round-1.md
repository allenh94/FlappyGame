# Validation Report: architecture-deepening round 1

- **Date**: 2026-08-26
- **Task Sheet**: docs/adr/008-score-session.md, 009-pipe-pair-deepening.md, 010-pooled-actives.md, 011-game-state-authority.md ＋ specs/architecture/dependency-rules.md（S1-S4/P1-P3/PL1-PL3/GA1-GA4）
- **Reviewer**: validator-agent
- **Verdict**: **FAIL** — 1 blocker（#1 live 計分 HUD 回歸）。#2/#3/#4 全數通過。

---

## 逐項驗證

### #1 ScoreSession（ADR-008 / S1-S4）

| 檢查 | 實證 | 結果 |
|------|------|------|
| Interface 極小（3 方法＋4 accessor） | `Begin/AddPoint/End→bool`＋`CurrentScore/PreviousHighScore/HighScore/RecordBroken`（ScoreSession.cs:40-84） | ✅ |
| 破紀錄順序封閉在 End() 內（S2） | L79-83：`previousHighScore = Load()` → `recordBroken = current > previous` → 破紀錄才 `Save()`。順序正確 | ✅ |
| PlayerPrefs 內部 adapter（S4） | `private sealed PlayerPrefsPersistence`（L18-32）＋`internal IPersistence` seam（L11-15）供測試注入；不暴露外部 | ✅ |
| GameManager 瘦身（S1） | 已移除 `score/previousHighScore/HighScoreKey` 欄位與 `IncrementScore`；無直接 PlayerPrefs 讀寫 | ✅ |
| 單一 Score accessor（S3） | `public ScoreSession Score => scoreSession`（GameManager.cs:30）；UI/ScoreTrigger 經此讀取 | ✅ |
| ScoreTrigger/UIController 遷移 | ScoreTrigger `gameManager.Score.AddPoint()`（:22）；UIController 讀 `Score.HighScore/CurrentScore/RecordBroken/PreviousHighScore`（:116/142/145/148/158/183-184） | ✅ |
| **live 計分 HUD 回歸** | **見 B1（blocker）** | ❌ |

### #2 Pipe pair（ADR-009 / P1-P3）

| 檢查 | 結果 |
|------|------|
| bottomPipe/topPipe private serialized | ✅（PipeController.cs:8-9） |
| 只剩單一 Configure | ✅（唯一 public 方法 `Configure(position, gapSize)`，:23） |
| public Movement/setCenterGap/startPosition 刪除 | ✅（grep 全 Assets 無 `.Movement(`／`setCenterGap` 殘留；`startPosition` 僅為 PlayerController.Reset 參數名，非 dead field） |
| 命名 PascalCase（SetGap） | ✅（:30 `private void SetGap`） |
| 無外部殘留呼叫 | ✅（grep 確認） |

### #3 PooledActives（ADR-010 / PL1-PL3）

| 檢查 | 結果 |
|------|------|
| 只封裝池週期、createFunc 呼叫端注入（PL2） | ✅（PipeSpawner:36-44、BGLayer:67-86 各自提供 createFunc） |
| active-list 追蹤＋重複 get/release 防護 | ✅（Get 重複→throw :39-40；Release 不在 active→throw :48-49） |
| 真去重（new ObjectPool 僅剩一處） | ✅（grep 全 Assets 僅 PooledActives.cs:24） |
| 熱路徑零分配（PL3） | ✅（active 預分配 List，無 LINQ） |
| 池語意回歸（Get/Release/SetActive 副作用） | ✅（OnGet SetActive(true)、OnRelease SetActive(false)、OnDestroy Destroy，與舊手刻一致） |

### #4 GameStateAuthority（ADR-011 / GA1-GA4）

| 檢查 | 結果 |
|------|------|
| 只做查詢、命令留 GameManager（GA3） | ✅（僅 `IsPlaying()`；Die/StartGame/Pause 全在 GameManager） |
| 註冊/反註冊時機 | ✅（GameManager.Awake Register :36、OnDestroy Unregister :42，static 不殘留已銷毀實例） |
| 純查詢系統移除 gameManager 引用（GA1） | ✅（Player/PipeController/PipeSpawner 零 gameManager 引用，grep 確認） |
| ScoreTrigger/CollisionHandler 合理保留（命令） | ✅（Trigger 保留供 `Score.AddPoint` 命令；CollisionHandler 保留供 `Die()` 命令） |
| 凍結政策不變（GA2） | ✅（各系統仍自查 `IsPlaying()` 輪詢） |
| OnStateChanged 未升級事件匯流排（GA4） | ✅（仍 plain event，僅 UI 訂閱） |

---

## Blockers

### B1: live 計分 HUD 回歸——遊玩中 HUD 分數不再逐分更新
- type: code | severity: **blocker**
- 依據: pipe-score.md **AC-1**「鳥穿過水管間隙 → 分數 +1，**螢幕顯示更新為 'Score: N'**」＋ **AC-6**「分數顯示正確（ScoreText 存在並**即時更新**）」；pipe-score round-1 **D1**（「分數 +1，ScoreText 更新為 'Score: 1'」人類實測「計分正常」）與 **D6**（「全程觀察 ScoreText → 存在、可見、即時更新」人類確認「UI 有正常顯示」）——live 分數顯示是**既有已驗收項目**。另違反 ADR-008 自身「純技術 refactor，產品語義不變」主張，與 game-states **NFR-01**「pipe-score 已驗收成果不得破壞」。
- 事實: `GameManager.cs:104` 僅在 `StartGame()` 設 `scoreText.text = "Score: 0"`；全 Assets grep `scoreText` 寫入僅此一處（GameManager.cs:104），**遊玩期間無任何程式碼更新 HUD**。`UIController` 只在 Menu（:116）與 GameOver（:142）讀 `Score.*`，Playing 態（:120-123）僅隱藏 menu/overlay canvas，不更新 HUD。場景 `ScoreCanvas`（SampleScene.unity:2718，`m_IsActive: 1`）為獨立 HUD canvas，不受 UIController 切換，遊玩期間**恆可見**且顯示凍結的 "Score: 0"。原 `IncrementScore` 逐分更新 `scoreText.text` 的邏輯在 ScoreSession 抽出時被移除，未遷移到任何新位置。
- 判定：**blocker 回歸，非可接受的純 refactor 邊界**。AC-1/AC-6 明列「即時更新」，且 round-1 已人類驗收；移除即違反既有驗收成果。
- 建議方向（僅供路由）：live HUD 更新需在計分路徑（`Score.AddPoint`）或 UI 側補回——例如 UIController 訂閱計分事件、或 ScoreSession 暴露計分事件、或 ScoreTrigger 於 AddPoint 後更新 HUD。屬 code 修復，路由 Developer。

---

## Majors

無。

## Minors

- **m1**（code/minor）：`PipeSpawner.cs:1` `using NUnit.Framework;`——runtime 腳本殘留未用 import（smell）。
- **m2**（code/minor）：`PipeSpawner.cs:24` `nextPoolIndex`——死欄位（舊池索引用，遷移後未刪）。
- **m3**（code/minor）：`PipeController.cs:2` `using UnityEngine.Pool;`——未用 import。
- **m4**（code/minor）：`PipeController.cs:10` `isActive`——未用欄位（死碼）。
- **m5**（code/minor）：CS0618 obsolete `FindObjectOfType` 警告（ScoreTrigger.cs:12、GameManager.cs:49）——既有，非本次引入；建議改 `FindFirstObjectByType`。
- **m6**（code/minor）：場景 GameManager 元件殘留舊序列化欄位 `score: 0`（SampleScene.unity:387）——新 GameManager.cs 已無 `score` 欄位，屬過期序列化資料（下次存場景自動丟棄），無運行影響。

---

## 動態驗證清單（無法靜態驗證者）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode，鳥穿過第一根水管間隙（不碰水管） | HUD ScoreText 更新為 "Score: 1"（**預期 FAIL**——現行顯示 "Score: 0" 凍結，證實 B1） | 待填 |
| D2 | Play mode 觀察背景四層捲動 | PooledActives 遷移後無視覺回歸（無縫、無跳格、無 tile 消失） | 待填 |
| D3 | Play mode 觀察水管生成/回收 | 無 console 錯誤（無重複 get/release 例外）；水管正常回收重生 | 待填 |
| D4 | GameOver→Restart 與 GameOver→BackToMenu | GameStateAuthority.IsPlaying 跨轉移正確；無 MissingReferenceException；鳥/水管隱藏與背景倒回正常 | 待填 |
| D5 | 破紀錄流程（本局 > 舊紀錄） | 分數跳動動畫正常（PreviousHighScore 快照正確，起點為舊值） | 待填 |

---

## Verdict 總表

| 深化 | Verdict |
|------|---------|
| #1 ScoreSession | **FAIL**（B1 blocker：live 計分 HUD 回歸） |
| #2 Pipe pair | PASS |
| #3 PooledActives | PASS |
| #4 GameStateAuthority | PASS |
| **整體** | **FAIL**（1 blocker） |

## 附註
- **測試套件**：`Assets/` 無 `*.asmdef`、無 `*Test*.cs`——無測試可跑。ScoreSession 的 `internal IPersistence` seam 已備妥供 EditMode 測試，但尚無測試組件承接。
- **Console**：僅 MCP-FOR-UNITY WebSocket 噪音＋2 條 CS0618 obsolete 警告（m5，非本次引入）；無編譯錯誤。
- **驗證方法限制**：B1 為純靜態可證（無任何程式碼在遊玩期間寫 scoreText），回歸確定；D1 仍列動態以視覺確認 HUD 恆可見。D2-D5 需 Play mode，回填前整體 Verdict 為暫定。

---

# round-1.1 複驗（B1 修復）

- **Date**: 2026-08-26
- **標的**: B1（live 計分 HUD 回歸）修復複驗
- **Verdict**: **B1 FIXED**（靜態確認）＋ 新增 **M1（code/major）**：Resume 誤重置 HUD。整體 Verdict 由 FAIL 更新為 **PASS（with major）**。

## B1 修復確認（fixed）

| 檢查 | 實證 | 結果 |
|------|------|------|
| 逐分事件鏈完整 | `ScoreTrigger.AddPoint`（ScoreTrigger.cs:22）→ `ScoreSession.AddPoint` fire `OnScoreChanged(currentScore)`（ScoreSession.cs:73-78）→ `UIController.OnScoreChanged`（:114-118）→ `scoreText.text = "Score: N"` | ✅ 斷層已閉合 |
| GameManager 不再握顯示職責 | 已移除 `scoreText` 欄位＋`using TMPro`＋FindObjectOfType fallback＋StartGame "Score: 0"（GameManager.cs 全文無 scoreText/TMPro） | ✅ 上帝模組未回歸 |
| 訂閱/反訂閱配對 | OnEnable `+= OnScoreChanged`（:95）、OnDisable `-=`（:109） | ✅ 無 event leak |
| ResolveScoreText 精確 | `GameObject.Find("ScoreText")`（:68）——場景僅 HUD 名為 "ScoreText"（SampleScene.unity:134），Menu/GameOver 為 HighScoreText/GameOverScoreText/GameOverHighScoreText，不誤抓 | ✅ |
| 破紀錄動畫未受影響 | `PlayHighScoreCountUp` 仍讀 `PreviousHighScore`/`CurrentScore`（:210-211）；`End()` 快照順序未動 | ✅ |
| ScoreSession interface 仍極小 | 新增單一 `OnScoreChanged` 事件（:54），AddPoint 維持 void；註解說明事件 vs return 取捨合理 | ✅ 無過度擴張 |
| S1-S4 符合 | Score 仍單一真源；顯示職責歸 UI（S3 接線契約）；PlayerPrefs 仍內部 adapter（S4） | ✅ |

## 新增 Issue

### M1: Resume（暫停→恢復）誤重置 HUD 為 "Score: 0"
- type: code | severity: **major**
- 依據: 驗證重點「Playing 態重置 'Score: 0' 是否與 StartGame 語義一致（無重複/遺漏）」——**不一致**。
- 事實: `UIController.cs:149` 在 `OnStateChanged` 的 **Playing case** 設 `scoreText.text = "Score: 0"`。此 case 在**每次**轉入 Playing 都 fire，含 `Resume()`（Paused→Playing，GameManager.cs:116 `SetState(Playing)`）。故玩家暫停時分數 5、恢復後 HUD 顯示 "Score: 0"，而權威分數（ScoreSession.CurrentScore）仍為 5——HUD 與實際分數脫節，直到下一次計分才自校正。原實作（refactor 前）僅 StartGame 重置，Resume 不動 HUD，此為 B1 修復引入的新回歸。
- 判定: major 非 blocker——權威分數正確、僅 HUD 顯示錯誤、下一分自校正、屬 Pause→Resume 邊界路徑（非主計分路徑）。但為真實缺陷，建議修（重置僅應於 StartGame/Restart 觸發，非 Resume）。
- 建議方向（僅供路由）: 將 "Score: 0" 重置移出 Playing case，改由 StartGame 專屬路徑（如 `_isFirstSync` 之外另設 fresh-game 旗標，或 ScoreSession.Begin() 也 fire OnScoreChanged(0)）。路由 Developer。

## Minors（新增）
- **m7**（code/minor）：場景 UIController 元件未接線新 `scoreText` 欄位（SampleScene.unity:428-449 無 scoreText），依賴 `ResolveScoreText()` 的 `GameObject.Find("ScoreText")` fallback——功能成立（fallback 設計即此），但場景未重存。
- **m8**（code/minor）：場景 GameManager 元件仍殘留舊序列化欄位 `score: 0`（:387）與 `scoreText`（:388）——新 GameManager.cs 已無此二欄位，屬過期序列化資料（下次存場景自動丟棄），無運行影響。與 round-1 m6 同源。

## 動態驗證清單（更新）
| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode，鳥穿過第一根水管間隙（不碰水管） | HUD ScoreText 更新為 "Score: 1"（**B1 修復後預期 PASS**——現行鏈路完整） | 待填 |
| D1a | **Play 得 5 分 → P 暫停 → P 恢復** | HUD 應維持 "Score: 5"（**預期 FAIL**——現行顯示 "Score: 0"，證實 M1） | 待填 |
| D2 | Play mode 觀察背景四層捲動 | PooledActives 遷移後無視覺回歸 | 待填 |
| D3 | Play mode 觀察水管生成/回收 | 無 console 錯誤（無重複 get/release 例外） | 待填 |
| D4 | GameOver→Restart 與 GameOver→BackToMenu | GameStateAuthority.IsPlaying 跨轉移正確；無 MissingReferenceException | 待填 |
| D5 | 破紀錄流程（本局 > 舊紀錄） | 分數跳動動畫正常（PreviousHighScore 快照正確） | 待填 |

## Verdict 更新
| 深化 | Verdict |
|------|---------|
| #1 ScoreSession | **PASS**（B1 fixed；新增 M1 major 累積） |
| #2 Pipe pair | PASS |
| #3 PooledActives | PASS |
| #4 GameStateAuthority | PASS |
| **整體** | **PASS（with major）**——B1 已修復；M1（Resume 重置 HUD）為 major 不擋驗收，累積記錄。動態 D1/D1a 回填前為暫定。

---

# round-1.2 複驗（M1 修復）

- **Date**: 2026-08-26
- **標的**: M1（Resume 誤重置 HUD）修復複驗
- **Verdict**: **M1 FIXED**（靜態確認）＋ 無新增 issue。整體 Verdict 更新為 **PASS（with minor）**。

## M1 修復確認（fixed）

| 檢查 | 實證 | 結果 |
|------|------|------|
| Resume 不再重置 HUD | `Resume()`（GameManager.cs:116）僅 `SetState(Playing)`，不呼叫 `Begin()`；UIController Playing case 已移除硬寫（:145-151 僅註解）→ HUD 維持暫停前分數 | ✅ |
| StartGame/Restart 新局歸零正確 | `StartGame()` 呼叫 `scoreSession.Begin()`（GameManager.cs:97）→ `Begin()` fire `OnScoreChanged(0)`（ScoreSession.cs:73-74）→ UIController handler → HUD "Score: 0"；`Restart()` → `StartGame()`（:162-166）同路徑 | ✅ |
| 新局→Playing 一定經 Begin | 唯一轉入 Playing 路徑：Menu→StartGame（Begin）、GameOver→Restart→StartGame（Begin）、Paused→Resume（**不 Begin**，正確——非新局）。無遺漏 | ✅ |
| 無 HUD 錯位 | StartGame 順序：`SetState(Playing)`（OnStateChanged Playing case 現為 no-op）→ `Begin()` fire 0 → HUD 歸零。無「先顯示舊值再歸零」的視覺錯位 | ✅ |
| 訂閱時機不漏初次 0 | GameManager.Awake 建 `scoreSession`（:30）早於 UIController.OnEnable 訂閱 `OnScoreChanged`（:95）——Unity 保證所有 Awake 先於任何 OnEnable 完成，故訂閱時 Score 已存在；Begin() 於 StartGame（使用者點開始後）才 fire，訂閱必已就緒 | ✅ |
| 無其他脫節路徑 | grep 全 Assets：`scoreText.text` 寫入僅 UIController.cs:117（OnScoreChanged handler）；`Begin()` 僅 GameManager.cs:97。無第二寫入點 | ✅ |
| 破紀錄動畫未受影響 | `PlayHighScoreCountUp` 仍讀 `PreviousHighScore`/`CurrentScore`（:211-212）；Begin() 於 StartGame 非 EndGame，與 EndGame 的 count-up 無重疊 | ✅ |

## 新增 Issue
無。

## Minors（累積，不變）
- m5（CS0618 obsolete FindObjectOfType×1，ScoreTrigger.cs:12，既有）
- m7（場景 UIController 未接線 scoreText，靠 fallback）
- m8（場景 GameManager 殘留舊序列化 `score: 0`/`scoreText` 欄位）

## 動態驗證清單（更新）
| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Play mode，鳥穿過第一根水管間隙（不碰水管） | HUD ScoreText 更新為 "Score: 1"（**預期 PASS**） | 待填 |
| D1a | Play 得 5 分 → P 暫停 → P 恢復 | HUD 維持 "Score: 5"（**M1 修復後預期 PASS**——不再重置為 0） | 待填 |
| D1b | GameOver → Restart | HUD 歸零 "Score: 0"（Begin() fire 0 驅動） | 待填 |
| D2 | Play mode 觀察背景四層捲動 | PooledActives 遷移後無視覺回歸 | 待填 |
| D3 | Play mode 觀察水管生成/回收 | 無 console 錯誤（無重複 get/release 例外） | 待填 |
| D4 | GameOver→Restart 與 GameOver→BackToMenu | GameStateAuthority.IsPlaying 跨轉移正確；無 MissingReferenceException | 待填 |
| D5 | 破紀錄流程（本局 > 舊紀錄） | 分數跳動動畫正常（PreviousHighScore 快照正確） | 待填 |

## Verdict 更新
| 深化 | Verdict |
|------|---------|
| #1 ScoreSession | **PASS**（B1＋M1 皆 fixed） |
| #2 Pipe pair | PASS |
| #3 PooledActives | PASS |
| #4 GameStateAuthority | PASS |
| **整體** | **PASS（with minor）**——B1、M1 皆已修復；剩餘 minor（m5/m7/m8）累積不擋驗收。動態 D1-D5 回填前為暫定。

---

# round-1.3 動態驗證回填（Sisyphus 於 Editor 實測）

- **Date**: 2026-08-26
- **標的**: 動態驗證清單 D1/D1a/D1b/D2/D3/D4/D5 回填
- **方法**: Sisyphus 於 Unity Editor（flipbird@6000.3.12f1）Play mode 以 `execute_code` 原子幀操控狀態機，讀取 HUD 與內部狀態實證。所有 Verdict 由暫定轉為確定。
- **Verdict**: **全部 PASS**。整體 Verdict 確定為 **PASS（with minor）**。

## 逐項回填

| # | 實測 | 實證 | 結果 |
|---|------|------|------|
| **D1** | StartGame→Playing，`Score.AddPoint()` | Start 後 HUD "Score: 0"；AddPoint 後 HUD "Score: 1"、權威 score=1。逐分事件鏈閉合 | ✅ **PASS**（B1 確認修復） |
| **D1a** | StartGame→AddPoint×4（HUD "Score: 4"）→Pause→Resume | Pause 後 state=Paused；Resume 後 state=Playing；HUD **維持 "Score: 4"** 未重置 | ✅ **PASS**（M1 確認修復） |
| **D1b** | Playing（score 4）→EndGame→Restart | EndGame 後 state=GameOver、HUD "Score: 4"；Restart 後 state=Playing、HUD "Score: 0"、權威 score=0 | ✅ **PASS** |
| **D2** | StartGame 後測背景捲動（BGLayer.scrollDistance 經 reflection 讀取） | 兩次 call 間隔 7.5s，sd 由 9.9671 → 11.6155（Δ≈1.65，速率≈0.22≈speed=0.25/depth8） | ✅ **PASS**（PooledActives 遷移無視覺回歸） |
| **D3** | Play mode 全程，全水管生成/回收循環 | `read_console` 全 error filter 零條目；`collectionCheck:true` 下無重複 get/release 例外 | ✅ **PASS** |
| **D4** | Menu→Playing→GameOver→Menu 全轉移查詢 GameStateAuthority.IsPlaying() | Menu: auth=false/gm=false；Playing: auth=true/gm=true（match）；EndGame→Menu 後 auth=false | ✅ **PASS**（無 MissingReference） |
| **D5** | 破紀錄流程：StartGame→AddPoint×40（score 40>舊紀錄 35）→EndGame | End 前 HighScore=35（未覆寫）；End 後=40；RecordBroken=True；PreviousHighScore 快照=35（動畫起點正確） | ✅ **PASS** |

## 驗證方法註記（供未來動態驗證參考）
- **忙等陷阱**：在單一 `execute_code` 內以 `while(realtimeSinceStartup - t0 < N)` 忙等**會阻塞主執行緒**，Unity Update 不執行，導致 scrollDistance 等每幀狀態看似停滯（假象）。正確做法是**兩次獨立 call、中間自然經過多幀**再讀取。
- 鳥存活控制：動態驗證期間鳥會自然落地觸發 GameOver（凍結背景）。測試於單一 call 內原子完成狀態轉移（同幀 Pause/Resume）避免鳥中途墜落干擾，或暫時停用鳥 collider（須於 ShowPlayerAtStart 完成後，或直接對 BGLayer 手動 SetNormal 隔離捲動邏輯）。

## Verdict 最終確定
| 深化 | Verdict |
|------|---------|
| #1 ScoreSession | **PASS**（B1＋M1 皆 fixed；動態 D1/D1a/D1b/D5 證實） |
| #2 Pipe pair | **PASS** |
| #3 PooledActives | **PASS**（動態 D2/D3 證實捲動與池無回歸） |
| #4 GameStateAuthority | **PASS**（動態 D4 證實跨轉移正確） |
| **整體** | **PASS（with minor）**——動態回填完成，關單。剩餘 minor（m5/m7/m8）累積記錄，不擋驗收。 |