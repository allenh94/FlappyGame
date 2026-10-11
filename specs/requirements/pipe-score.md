# Pipe Score（水管穿過加分）

- **Status**: draft
- **Owner**: pm-agent
- **arch-review**: no
- **Created**: 2026-08-24

## 功能：Pipe Score（水管穿過加分）

- **目標**：玩家操控鳥飛過水管間隙時，得分 +1，並即時更新螢幕分數顯示。

## 涉及物件

| 物件 | 路徑 | 角色 |
|------|------|------|
| ScoreTrigger.cs | `Assets/Scripts/ScoreTrigger.cs` | 計分觸發（水管上的 ScoreZone 子物件） |
| CollisionHandler.cs | `Assets/Scripts/CollisionHandler.cs` | 鳥的碰撞處理（含衝突計分路徑） |
| GameManager.cs | `Assets/Scripts/GameManager.cs` | 分數持有、顯示、重啟 |
| PlayerController.cs | `Assets/Scripts/PlayerController.cs` | 玩家身分（component 偵測用） |
| Pipe.prefab | `Assets/Prefabs/Pipe.prefab` | 水管 prefab（含 ScoreZone 子物件） |
| SampleScene.unity | `Assets/Scenes/SampleScene.unity` | 主場景（Bird、PipeSpawner 設定） |
| TagManager.asset | `ProjectSettings/TagManager.asset` | 標籤定義（現僅 "Pipe"，無 "Player"） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **ScoreTrigger 已掛進水管 prefab，且有 trigger collider**：`Assets/Prefabs/Pipe.prefab` 的 `ScoreZone` 子物件（tag "Untagged"，行 202-207）帶 `BoxCollider2D`（`m_IsTrigger: 1`、`m_Size: {x: 1, y: 1}`，L224-268）與 `ScoreTrigger` MonoBehaviour（L270-281，`gameManager: {fileID: 0}` 為空，靠 `Start()` 的 `FindObjectOfType<GameManager>()` 回退）。
2. **計分條件永遠不成立（核心缺陷）**：`ScoreTrigger.cs:15` 用 `other.CompareTag("Player")` 判斷玩家。但 `ProjectSettings/TagManager.asset` 僅定義 "Pipe" 標籤（L5），**未定義 "Player"**；且 `SampleScene.unity:654` 的 Bird（掛 PlayerController，L670）標籤為 `"Untagged"`。全場景 grep `m_TagString` 無任何 "Player"。故 `CompareTag("Player")` 永不成立 → **計分永不觸發**。
3. **衝突計分路徑（score-on-death 風險）**：`CollisionHandler.cs`（掛在 Bird 上）`OnTriggerEnter2D` 遇 "Pipe" → `Die()`（L10-14）；`OnTriggerExit2D` 遇 "Pipe" → `IncrementScore()`（L16-24）。鳥撞水管即死，水管繼續左移離開死鳥 → `OnTriggerExit2D` 觸發 → **死亡瞬間加分**，且與 ScoreTrigger 構成重複計分路徑。
4. **分數顯示已接線（非缺陷）**：`SampleScene.unity:134-179` 有 `ScoreText`（TextMeshProUGUI，`m_text: 'Score: 0'`）；`GameManager` 的 `scoreText` 已接線（L295）。`GameManager.cs:58-63` `IncrementScore()` 更新 `scoreText` 並 `Debug.Log`。
5. **GameManager 重啟流程**：`GameManager.cs:41-49` `StartGame()` 設 `score=0`、更新顯示、`player.Reset()`、`pipeSpawner.ResetAll()`；`:51-56` `Die()` 設 `isPlaying=false`、`Invoke(nameof(StartGame), 0.1f)`。
6. **場景 PipeSpawner 設定（與腳本預設不同）**：`SampleScene.unity:1001-1008` — `spawnXPosition: 8`、`gapSize: 6`、`minHeight: -4`、`maxHeight: 4`、`poolSize: 7`、`despawnXPosition: -8`。
7. **ScoreZone 過小（可靠性缺陷）**：ScoreZone 為 1×1（世界座標），但場景間隙 `gapSize=6`。鳥（1×1）通過 6 單位間隙時，僅當鳥位在間隙中心 ±0.5 內才會碰到 ScoreZone → 約 5/6 的通過會漏計分。
8. **水管回收機制**：`PipeSpawner.cs:36-63` 用 `ObjectPool<PipeController>`；`actionOnRelease` 執行 `pipe.gameObject.SetActive(false)`（L50-53），`actionOnGet` 執行 `SetActive(true)`（L46-49）。`CheckDespawn()`（L99-109）在 `x < despawnXPosition` 時釋放水管。

### 未驗證假設（不得視為既定事實）

- **假設 A**：鳥（實心 collider，`SampleScene.unity:819` `m_IsTrigger: 0`）進入 ScoreZone（trigger collider）時，`OnTriggerEnter2D` 確實會觸發 ScoreTrigger。依 Unity 2D 物理規則（trigger 與實心 collider 重疊觸發 callback）應成立，但未在 Editor 實測。
- **假設 B**：水管根 `SetActive(false)` 會觸發子物件 ScoreZone 的 `OnDisable()`。依 Unity 行為（停用父物件停用子物件並觸發 `OnDisable`）應成立，但未在 Editor 實測。此假設是 T2 回收重置機制的依賴。

## 任務清單

| # | 任務 | Priority | Assigned | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|------|---------------|------|
| T1 | 修正玩家偵測 | P0 | gameplay-developer | 讓計分條件能命中玩家 | `Assets/Scripts/ScoreTrigger.cs` | 鳥穿過間隙 → 分數 +1 |
| T2 | 防重複計分 | P0 | gameplay-developer | 同一次通過／回收水管不重複計分 | `Assets/Scripts/ScoreTrigger.cs` | 鳥在間隙內上下拍動 → 只 +1 |
| T3 | 移除衝突計分路徑 | P0 | gameplay-developer | 消除死亡時加分與重計分 | `Assets/Scripts/CollisionHandler.cs` | 撞水管死亡 → 不加分 |
| T4 | 調整 ScoreZone 尺寸 | P1 | scene-developer | 讓鳥可靠觸發計分 | `Assets/Prefabs/Pipe.prefab` | 鳥通過間隙 → 穩定 +1 |
| T5 | 重生競態防護 | P1 | gameplay-developer | 防止 Die→StartGame 窗內誤加分 | `Assets/Scripts/GameManager.cs`、`Assets/Scripts/ScoreTrigger.cs` | 重生後分數正確歸 0 |

### T1 — 修正玩家偵測（P0, gameplay-developer）
- **Goal**：`ScoreTrigger.OnTriggerEnter2D` 的玩家判斷改為 component 偵測，繞開未定義的 "Player" 標籤。
- **作法**：`other.CompareTag("Player")` → `other.GetComponent<PlayerController>() != null`。
- **根因**：TagManager.asset 未定義 "Player" 標籤，且 Bird 標籤為 "Untagged"，`CompareTag("Player")` 永不成立。改為 component 偵測可繞開標籤依賴（agents 無權限寫 `ProjectSettings/TagManager.asset`）。
- **驗收**：鳥穿過間隙 → `IncrementScore()` 被呼叫，分數 +1。

### T2 — 改造重複計分（P0, gameplay）
- **Goal**：同一次通過內鳥重複進出 ScoreZone、或回收水管重新觸發，都不重複計分。
- **作法**：ScoreTrigger 加 `private bool scored;`。`OnTriggerEnter2D` 開頭 `if (scored) return;`，計分後 `scored = true;`。加 `private void OnDisable() { scored = false; }`（水管回收時 `SetActive(false)` 觸發，重置旗標）。
- **依賴**：依賴「假設 B」（`SetActive(false)` 觸發子物件 `OnDisable`）。開工時先在 Editor 驗證此行為。
- **驗收**：鳥在間隙內上下拍動（重複進出 ScoreZone）→ 分數只 +1；水管回收重生成後 → 不重複計分。

### T3 — 停用衝突計分路徑（P0, gameplay）
- **Goal**：消除 `CollisionHandler.OnTriggerExit2D` 的死亡時加分與重計分。
- **作法**：移除或停用 `CollisionHandler.cs:16-24` 的 `OnTriggerExit2D` 計分（`IncrementScore()`）。ScoreTrigger 為唯一計分路徑。
- **驗收**：鳥撞水管死亡 → 分數不因死亡瞬間增加。

### T4 — 調整 ScoreZone 尺寸（P1, scene-developer）
- **Goal**：讓鳥可靠觸發 ScoreZone，不漏計分。
- **作法**：`Assets/Prefabs/Pipe.prefab` 的 ScoreZone `BoxCollider2D` 尺寸由 `{x: 1, y: 1}` 改為 `{x: 1, y: 6.5}`（覆蓋場景 `gapSize=6` 的間隙，略高以含邊緣）。
- **注意**：若 `gapSize` 日後變成可調，需改為動態調整（會引入 PipeController→ScoreZone 新依賴，屆時需 arch-review）。
- **驗收**：鳥通過間隙任意高度 → 穩定 +1。

### T5 — 重生競態防護（P1, gameplay-developer）
- **Goal**：防止 `Die()→StartGame()` 的 0.1s 窗內水管重生重觸發誤加分。
- **作法**：`GameManager` 加 `public bool IsPlaying() { return isPlaying; }`；`ScoreTrigger.OnTriggerEnter` 開頭 `if (!gameManager.IsPlaying()) return;`。
- **驗收**：死亡→重生期間，分數不因水管重生而誤增；重生後分數歸 0。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 重複計分（同一次通過） | 鳥在間隙內上下拍動，重複進出 ScoreZone → `OnTriggerEnter2D` 多次觸發 | T2 `scored` 旗標 |
| BC-2 重複計分（回收水管） | 已計分水管回收後重生，若鳥在重生位置 → 重新觸發 | T2 `OnDisable` 重置旗標 |
| BC-3 死亡時加分 | 鳥撞水管死亡，水管左移離開死鳥 → `CollisionHandler.OnTriggerExit2D` 觸發加分 | T3 移除該計分路徑 |
| BC-4 標籤未定義 | TagManager 無 "Player" 標籤，`CompareTag("Player")` 永不命中 | T1 component 偵測 |
| BC-5 ScoreZone 過小 | 1×1 的 ScoreZone 對 6 單位間隙，多數通過漏計分 | T4 調整尺寸 |
| BC-6 重生競態 | `Die()→StartGame()` 0.1s 窗內水管重生重觸發 | T5 `IsPlaying()` 檢查 |
| BC-7 分數文字缺失 | 若 `scoreText` 為 null，`IncrementScore` 僅 `Debug.Log` | 已存在（ScoreText 已接線）；若缺失，`FindObjectOfType` 回退 |

## 驗收標準

| AC | 描述 |
|----|------|
| AC-1 | 鳥穿過水管間隙 → 分數 +1，螢幕顯示更新為 "Score: N" |
| AC-2 | 鳥在間隙內上下拍動（重複/退出 ScoreZone）→ 分數只 +1 |
| AC-3 | 鳥撞水管死亡 → 不因死亡瞬間加分（無 score-on-death） |
| AC-4 | 死亡→重生後 → 分數歸 0，第一根水管正常計分 |
| AC-5 | 水管回收重生成 → 不重複計分 |
| AC-6 | 分數顯示正確（ScoreText 存在並即時更新） |

## 規格自檢清單

- **事實主張標出處**：上述「現況診斷」每條均標注檔案＋行號（ScoreTrigger.cs:15、TagManager.asset、SampleScene.unity:654/1001-1008、Pipe.prefab:224-268、CollisionHandler.cs:16-24、GameManager.cs:41-56、PipeSpawner.cs:36-52）。
- **未驗證假設**：假設 A（trigger 觸發）、假設 B（OnDisable 回收重置）明確標「未驗證假設」，且 T2 依賴假設 B 已註明「開工先驗證」。
- **一致性**：BC-1/BC-2/BC-6 與 AC-2/AC-4/AC-5 措辭一致（「重複計分」「回收水管」「重生競態」）；BC-3 與 AC-3 一致（「死亡時加分」）。
- **Editor API 驗證**：T2 依賴 `OnDisable` 回收行為，任務單已加「開工先用 unity_reflect 驗證」提示。