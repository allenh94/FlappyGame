# ADR-008: ScoreSession 深模組抽出（得分／最高分／破紀錄判定）

## Status

Accepted

## Context

`GameManager.cs` 是上帝模組。public interface 13+ 成員，其中 ~9 個與得分／最高分綁死（`score`、`GetScore`、`GetHighScore`、`GetPreviousHighScore`、`SaveHighScore`、`DidBreakRecord`、`HighScoreKey`、`IncrementScore`、`previousHighScore`）。得分邏輯與狀態編排（Menu/Playing/Paused/GameOver）混在同一模組，介面與實作幾乎等價——淺模組，呼叫端（UI、ScoreTrigger）被迫學習大量與自身無關的成員。

破紀錄判定的關鍵順序 bug 風險藏在**跨狀態邏輯交界**：`EndGame()` 內「先快照 `GetPreviousHighScore()` 再 `SaveHighScore()`」——若順序被重排（先 Save 再快照），破紀錄動畫起點會讀到已覆寫的新值。此順序是隱性不變量，散落在 GameManager 的狀態編排中，無單一測試面，任何重構都可能踩到。

## Decision

抽 `Assets/Scripts/Score/ScoreSession.cs` **深模組**（新系統目錄，符合 CONTEXT.md「新系統各立子資料夾」慣例，比照 `Background/`）。

**Interface 極小**（呼叫端需學的全部）：

- `Begin()` — 開始新一局（重置 current score、清除 recordBroken）
- `AddPoint()` — 計分 +1
- `End()` → `bool recordBroken` — 快照舊最高分 → 計算 recordBroken → 破紀錄則覆寫；回傳是否破紀錄
- 唯讀 accessor：`CurrentScore`、`PreviousHighScore`（End 當下快照）、`HighScore`（持久化最高分）、`RecordBroken`

**Implementation 深**：

- PlayerPrefs 持久化做成**內部 adapter**（internal seam，供 EditMode 測試注入 fake persistence）——不暴露到外部，呼叫端不觸碰 PlayerPrefs。
- 破紀錄順序封閉在 `End()` 內：快照 → 計算 → 覆寫。順序 bug 從「跨狀態邏輯交界」收斂為「單一模組單一方法」，可單元測試。

**GameManager 只保留狀態權威＋編排**：得分變一次性呼叫——`Score.Begin()`（StartGame）、`Score.End()`（EndGame）、`Score.AddPoint()`（ScoreTrigger 計分）。GameManager 公開介面從 ~9 個得分成員收斂為**單一唯讀 `Score` accessor**（`public ScoreSession Score => scoreSession;`），UI 經 `gameManager.Score.*` 讀取。

**Depth／Leverage**：呼叫端（GameManager／UI／ScoreTrigger）只需學 1 個 accessor + 3 個方法，換得全部得分／持久化／破紀錄判定行為。**Locality**：破紀錄順序、PlayerPrefs 讀寫、最高分語義集中在 ScoreSession 一處——改一處、修一處、測一處。

## Consequences

- 正面：GameManager 介面縮小（~9 個得分成員 → 1 個 `Score` accessor），上帝模組瘦身，狀態編排與得分邏輯分離。
- 正面：破紀錄順序 bug 封閉在單一方法，可 EditMode 單元測試（注入 fake persistence，不碰 PlayerPrefs）。
- 正面：PlayerPrefs 讀寫隔離，UI／playground 不直接觸碰持久化。
- 負面：新增一個模組＋一個目錄（`Assets/Scripts/Score/`）；GameManager 需持有並注入 ScoreSession。
- 被放棄的替代方案：保留 GameManager 內得分邏輯（上帝模組不變，順序 bug 風險持續）；把得分拆成多個小方法（淺模組，介面與實作等複雜度，無 leverage）。

## 對 pm 規格的修訂建議

純技術 refactor——得分／最高分／破紀錄判定的**產品語義不變**（無新產品行為），不需開 `specs/requirements/` 規格。