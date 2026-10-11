# ADR-009: Pipe pair 深化（介面收斂為單一 Configure）

## Status

Accepted

## Context

`PipeController.cs` 30 行卻暴露 `public Movement()`／`setCenterGap()`／`ResetPosition()`／`bottomPipe`／`topPipe` field，且有 dead field `startPosition`。公開介面（4 方法 + 2 公開欄位）與實作（30 行）幾乎等價＝**淺模組**。呼叫端（PipeSpawner）被迫知道 bottomPipe/topPipe 內部結構與 gap 設定細節，且命名不一致（`setCenterGap` 小寫開頭）。

## Decision

- `bottomPipe`／`topPipe` 收成 **private serialized**——呼叫端不需知道內部結構。
- 單一 interface `Configure(Vector3 center, float gapSize)`：設定位置＋間隙，一次完成。
- 移動由 `Update` 自行驅動（內部），刪除 public `Movement`。
- 刪除 public `setCenterGap`（改私有 `SetGap`，PascalCase）與 dead field `startPosition`。
- 命名統一 PascalCase。

**Depth**：公開介面從 4 方法 + 2 欄位收斂為 **1 方法（`Configure`）**。呼叫端只需學「設定位置＋間隙」，移動／間隙計算隱藏於內部。**Leverage**：PipeSpawner 呼叫端介面簡化；**Locality**：移動與間隙邏輯集中在 PipeController 內部。

## Consequences

- 正面：PipeController 變深模組，PipeSpawner 呼叫端介面簡化。
- 正面：dead code（`startPosition`）移除。
- 正面：命名一致（PascalCase）。
- 負面：無（純收斂，行為不變）。
- 被放棄的替代：維持現狀（淺模組，公開結構暴露，呼叫端被迫知道內部）。

## 對 pm 規格的修訂建議

純技術 refactor，無新產品行為（水管移動／間隙語義不變），不需開 `specs/requirements/` 規格。