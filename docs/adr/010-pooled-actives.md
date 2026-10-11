# ADR-010: 共用池化 seam（PooledActives&lt;T&gt;）

## Status

Accepted

## Context

`PipeSpawner` 與 `BGLayer` 各手刻同一份 `ObjectPool` 四 callback wiring（createFunc／actionOnGet／actionOnRelease／actionOnDestroy）＋ active-list 追蹤。`BGLayer` 註解明寫「比照 PipeSpawner」＝**兩處重複**。依 codebase-design：**兩個 adapter 才是真 seam**——重複的池週期邏輯（SetActive get/release、Destroy、active-list 增刪、重複 get/release 防護）散落兩處，改一處漏一處，無單一測試面。

## Decision

抽共用 `Assets/Scripts/Pooling/PooledActives<T>`（新共用工具目錄）當 `UnityEngine.ObjectPool` 的**薄 adapter**：

- 負責**池週期**：active-list 追蹤＋重複 get/release 防護＋SetActive/Destroy 標準 wiring。
- 呼叫端只提供 **createFunc**（PipeSpawner 建 pipe prefab、BGLayer 建 tile template）——「**只抽池週期，不抽 tile/pipe 建立邏輯**」。
- Interface：`Get()`、`Release(T)`、`ReleaseAll()`、`Active`（唯讀 active-list）。

**Depth／Leverage**：呼叫端只需提供 createFunc 並呼叫 Get/Release，池週期（active-list、防護、SetActive/Destroy）隱藏。**Locality**：池週期邏輯集中一處，兩呼叫端共用——改一處、修兩處。

## Consequences

- 正面：消除兩處重複（真 seam 收斂）。
- 正面：重複 get/release 防護統一（`collectionCheck` + active-list 防護）。
- 正面：熱路徑零分配維持（List 預分配，NFR-01～03）。
- 負面：新增共用工具目錄（`Assets/Scripts/Pooling/`）；兩呼叫端需遷移。
- 被放棄的替代：維持兩份手刻（重複持續）；抽完整池含建立邏輯（過度抽象——tile/pipe 建立差異大，create 留在呼叫端）。

## 對 pm 規格的修訂建議

純技術 refactor，無產品行為（池週期語義不變），不需開 `specs/requirements/` 規格。