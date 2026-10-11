# ADR-014: 水管 sprite 變體（PipeVariant 元件＋渲染遷移＋動態 collider 同步）

## Status

Accepted

## Context

需求（`specs/requirements/pipe-sprite-variants.md`）要求水管從固定 Mesh 渲染改為 Multiple atlas 子 sprite 隨機變體：每根生成的水管隨機選一個子 sprite（上下管同 variant），套用隨機 scale（0.8~1.2 可調），collider 形狀與所選 sprite 外觀一致。觸發三項架構變更：

1. **新元件 PipeVariant**（T1/T2）：新增跨腳本依賴 PipeController → PipeVariant。
2. **MeshRenderer→SpriteRenderer 遷移**（T3）：水管渲染從 Mesh 遷移到 Sprite。
3. **collider 同步策略**（T1）：BoxCollider2D（靜態序列化）→ PolygonCollider2D＋runtime 動態 path 寫入（sprite.GetPhysicsShape / SetPath 雙軌）。

既有約束：ADR-009（PipeController 單一 `Configure(center, gapSize)`，PipeSpawner 零改動硬性要求）、ADR-010（pool 重用陷阱——同一實例重複 Get，ApplyRandom 必須每次完整重置）、ADR-007（`GetComponentsInChildren<Renderer>()` 涵蓋 SpriteRenderer）、ADR-003（碰撞規則＝鳥撞管→死，trigger-based）。

## Decision

### 1. PipeVariant 元件（單一職責，Configure 觸發）

- 新建 `Assets/Scripts/PipeVariant.cs`（MonoBehaviour，掛 Pipe root），單一職責＝「對這根水管套用隨機變體」：隨機選 sprite variant＋同步 collider＋套用 scale。三職責同屬「套用變體」單一操作，拆開增加接線複雜度（collider 同步邏輯若成長再抽）。
- 依賴方向 **PipeController → PipeVariant 單向**：PipeController 持 `[SerializeField] private PipeVariant pipeVariant`，`Configure()` 內 `SetGap` 後觸發 `pipeVariant.ApplyRandom(Random.Range(minScale, maxScale))`（null guard 防未接線）。**PipeVariant 不得引用 PipeController**（或任何遊戲邏輯模組）。
- **ADR-009 相容**：`Configure(center, gapSize)` 公開介面簽名不變，PipeSpawner 零改動；ApplyRandom 觸發是 Configure 內部副作用（spawn 設定的一部分），非公開介面擴張。
- 序列化欄位：`SpriteRenderer[] renderers`（長度 2：0=Bottom、1=Top）、**`PolygonCollider2D[] colliders`**（與 renderers 同序；FR-09 已強制 PolygonCollider2D，用具體型別使接線契約編譯期強制，免 runtime cast）、`Sprite[] variants`。
- **空 variants 防護**：`ApplyRandom` 開頭 `if (variants == null || variants.Length == 0) return;`——`variants[0]` 對空陣列會拋 IndexOutOfRangeException（規格 BC-4 的「Random.Range(0,0) 回傳 0 不拋錯」正確，但後續索引空陣列會拋錯；T2 的 null guard 只涵蓋「PipeVariant 未接線」，不涵蓋「variants 為空」）。
- **pool 重用完整重置（ADR-010）**：ApplyRandom 每次呼叫完整重置——`polygonCollider.pathCount = shapeCount`（或 fallback 時 1）→ 逐 path `SetPath(i, path)`；不得只在 Awake 做一次。

### 2. MeshRenderer→SpriteRenderer 遷移

- BottomPipe/TopPipe 移除 MeshFilter＋MeshRenderer，改掛 SpriteRenderer（sortingOrder 維持 50，與背景帶位一致）。
- 材質改用 **URP Sprite-Unlit-Default**（背景 tile 慣例，guid `9dfc825aed78fcd4ba02077103263b40`）；**不得沿用既有 Pipe.mat**（URP Lit 材質，SpriteRenderer 不適用）。
- **ADR-007 相容**：`GetComponentsInChildren<Renderer>()` 涵蓋 SpriteRenderer（共同基類 Renderer，ADR-007 決策 2 已實證；鳥遷移 ADR-013 決策 9 已先例）——Menu 隱藏機制零改動。

### 3. collider 同步策略（靜態→動態）

- BottomPipe/TopPipe 的 BoxCollider2D 換成 PolygonCollider2D（**IsTrigger=1、tag Pipe 維持**——CollisionHandler.OnTriggerEnter2D → CompareTag("Pipe") → Die() 不變）。
- collider path 由 sprite 資料 runtime 寫入（sprite 驅動）：`sprite.GetPhysicsShapeCount() > 0` → 逐 shape `GetPhysicsShape(i, path)` → `SetPath(i, path)`；否則 → `sprite.bounds` 矩形 path。
- **座標空間一致性**：`GetPhysicsShape` 回傳 sprite 本地空間，`SetPath` 接受 collider 本地空間——collider 與 SpriteRenderer 同 GameObject（BottomPipe/TopPipe），兩空間一致；path 隨 transform 階層縮放（含 BottomAnchor localScale y=10），collider 與視覺同步縮放。
- **ADR-003 相容**：碰撞規則（鳥撞管→死）不變；PolygonCollider2D trigger 語意與 BoxCollider2D 相同。
- **ADR-010 相容**：ApplyRandom 每次完整重置（見決策 1）。

### 4. 雙軌 collider 架構（physics shape 主軌＋bounds fallback）——裁決：成立，不收斂

- 主軌（physics shape）＝精確匹配外觀；fallback（bounds 矩形）＝保證無 physics shape 的 sprite 仍有非空 collider（BC-2）。
- 單軌 physics-shape-only **否決**：variant 缺 physics shape → 空 collider → 鳥穿管（遊戲破壞）。
- 單軌 bounds-only **否決**：不規則造型失去「collider 匹配外觀」。
- 雙軌成本＝ApplyRandom 一個分支，防禦性合理（與專案防禦式風格一致）。
- **開放決策 A（矩形直管 vs 不規則造型）不影響架構成立**：矩形時兩軌結果近似（fallback 實務死碼但無害）；不規則時主軌必要。**不收斂**——雙軌策略本身不需 Human 裁決；開放決策 A 是產品決策（PM/Human 領地），架構兩種結果皆容納。

## Consequences

- 正面：collider 與視覺同步（sprite 驅動），滿足「玩家看到的水管外觀與碰撞體完全吻合」。
- 正面：ADR-009 維持（PipeSpawner 零改動）；ADR-007/003/010 全相容。
- 正面：雙軌防禦——無 physics shape 的 sprite 不會產生空 collider。
- 負面：collider 從靜態序列化改 runtime 動態——prefab 不再自描述碰撞體；未來改 collider 需改 sprite physics shape 或 code。
- 負面：新跨腳本依賴（PipeController → PipeVariant）——已以 PV 規則（dependency-rules）約束單向。
- 負面：A1（`Sprite.GetPhysicsShapeCount`/`GetPhysicsShape`/`PolygonCollider2D.SetPath` 存在性）未驗證——T1 開工 unity_reflect 驗證（規格已列為檢查點，設計不依賴其他未驗證前提）。
- 被放棄的替代方案：
  - **每 variant 一個 prefab／prefab variant**（否決）：variant 數擴張時 prefab 爆炸，接線重複。
  - **單軌 physics shape**（否決）：缺 shape 的 sprite 產生空 collider，鳥穿管。
  - **單軌 bounds 矩形**（否決）：不規則造型 collider 與外觀不符。
  - **collider 同步拆獨立元件**（否決）：三職責同屬「套用變體」操作，拆開增加接線複雜度。

## 對 pm 規格的修訂建議

- **BC-4 修正**：variants 空陣列時 `variants[0]` 拋 IndexOutOfRangeException（非「renderer.sprite=null 視覺為空」）——T1 需加空陣列 guard（決策 1）。
- **FR-04 欄位型別**：`Collider2D[] colliders` 建議改 `PolygonCollider2D[] colliders`（FR-09 已強制 PolygonCollider2D，編譯期強制接線契約，免 runtime cast）。
- **T3 材質**：明確「不得沿用 Pipe.mat（Lit），用 URP Sprite-Unlit-Default」。