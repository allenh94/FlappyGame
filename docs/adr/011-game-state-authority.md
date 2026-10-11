# ADR-011: GameStateAuthority 權威查詢 seam（收 wiring，不收凍結政策）

## Status

Accepted

> **2026-09-30 擴張註記（dying-state）**：新增 `IsPaused()` 查詢（`instance != null && instance.IsPaused()`；GameManager 對應 `public bool IsPaused() { return state == GameState.Paused; }`）——BlobAnimator 死亡淡出守衛用（語義＝非暫停即可淡出，Dying 態淡出必須執行）。屬本 ADR「只做狀態查詢（IsPlaying 等）」明列可擴的正常擴張：查詢/命令分離原則不變（IsPaused 是查詢非命令）；不觸發事件匯流排演進條件（BlobAnimator 已是 OnStateChanged 第二訂閱者，ADR-013 決策 6 已裁決不升級；IsPaused 為輪詢查詢，不新增訂閱者）。

## Context

5 個 gameplay 系統（Player／PipeController／PipeSpawner／ScoreTrigger／CollisionHandler）各自持有 `[SerializeField] GameManager` ＋ `FindObjectOfType<GameManager>()` fallback ＋ `if(!IsPlaying()) return;`。狀態查詢 wiring 重複 5 份——同一份「解析 GameManager＋查 Playing」邏輯散落各處，改一處漏一處。

（註：CollisionHandler 是**命令呼叫**（`gameManager.Die()`），非狀態查詢，不受本決策影響；其 GameManager 引用保留。）

ADR-006 D3 凍結政策（各系統自查 `IsPlaying()`）**維持不變**——本決策只收 wiring，不收政策。

## Decision

- 收成單一權威查詢 seam：`GameStateAuthority`（static singleton，GameManager 於 Awake 註冊）。
- 五系統共用 `GameStateAuthority.IsPlaying()`，移除各自 GameManager 引用＋FindObjectOfType 查 IsPlaying。
- **命令**（Die／StartGame／Pause 等）與 `OnStateChanged` 事件維持在 GameManager；GameStateAuthority 只做狀態查詢，不承接命令。
- **不變更 ADR-006 D3 凍結政策**：各系統仍自查 `IsPlaying()`（輪詢），只改查詢來源。

**事件匯流排評估（演進觸發條件）**：

- 條件：「出現第二個訂閱 GameManager 狀態的系統（除 UI 外）→ 重估 `OnStateChanged` 是否升級正式事件匯流排」。
- 評估：5 個 gameplay 系統用**輪詢**（每幀 `IsPlaying()`），非事件訂閱 `OnStateChanged`。目前 `OnStateChanged` 仍只有 UI 一個訂閱者。GameStateAuthority 是**查詢 seam**（輪詢），不引入第二個事件訂閱者。→ **不觸發**升級條件。
- 決策：**不升級正式事件匯流排**；`OnStateChanged` 維持現狀（plain event，僅 UI 訂閱）。演進觸發條件保留，未來若出現真正的事件訂閱者再重估。

## Consequences

- 正面：狀態查詢 wiring 收斂為單一 seam，5 系統不再各自 FindObjectOfType。
- 正面：凍結政策不變（D3），無行為回歸。
- 正面：`OnStateChanged` 維持輕量（僅 UI），不引入事件匯流排複雜度。
- 負面：引入 static singleton（可測性略降，但查詢極薄）；GameManager 需註冊。
- 被放棄的替代：升級正式事件匯流排（無第二訂閱者，過度設計）；維持 5 份 wiring（重複持續）。

## 對 pm 規格的修訂建議

純技術 refactor，無產品行為（凍結語義不變），不需開 `specs/requirements/` 規格。