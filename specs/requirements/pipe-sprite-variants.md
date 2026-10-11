# 規格：水管 sprite 變體（Pipe sprite variants）

> Status: draft ｜ Owner: pm-agent ｜ arch-review: **yes**（T1/T2/T3 觸發架構變更——新共享元件 PipeVariant＋prefab 結構變更：MeshRenderer→SpriteRenderer 遷移、collider 同步策略） ｜ Created: 2026-09-30
> 已依 ADR-014 修訂（2026-10-01）：①BC-4 空 variants 行為修正——`variants[Random.Range(0, variants.Length)]` 空陣列拋 IndexOutOfRangeException（非「renderer.sprite=null 視覺為空」），T1 加 PV3 空 variants guard ②FR-04/T1 colliders 型別 `Collider2D[]`→`PolygonCollider2D[]`（編譯期接線契約）③T3/FR-08 材質明確 URP Sprite-Unlit-Default（guid `9dfc825aed78fcd4ba02077103263b40`），禁沿用 Pipe.mat（URP Lit）
> 2026-10-01：**決策 B 定案（Background_Planets）**——variants 來源 = `Assets/Art/Atlases/Background_Planets.spriteatlasv2`（Multiple mode）20 子 sprite；T4 placeholder 方案撤銷；**M1 解決**（validator round-1 requirement/major：T4 偏離 Multiple mode atlas——換源後符合規格）
> （功能 seed：Human 需求——修改 pipe 的貼圖與 collider。使用指定 sprite（Multiple mode atlas，底下多個子 sprite），PipeSpawner 每次生成隨機選一個子 sprite，並隨機 scale（0.8~1.2 可調）。collider 必須匹配所選子 sprite 的外觀。）

## 功能：水管 sprite 變體（PipeVariant）

- **目標**：每根生成的水管從 Multiple atlas 的子 sprite 中隨機選一個貼圖，並套用隨機 scale（預設 0.8~1.2），collider 形狀與所選子 sprite 的外觀一致——玩家看到的水管外觀與碰撞體完全吻合。
- **性質**：**新功能＋渲染遷移**。新增共享元件 `PipeVariant`（掛 Pipe root），BottomPipe/TopPipe 由 MeshRenderer 換成 SpriteRenderer，collider 由固定 BoxCollider2D 改為依 sprite 動態同步（雙軌：physics shape 優先、bounds 矩形 fallback）。**PipeSpawner 零改動**（維持 ADR-009 單一 Configure 接口）。

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/PipeVariant.cs`（新） | **新建**（T1）：隨機選 sprite variant＋同步 collider＋套用 scale |
| Script | `Assets/Scripts/PipeController.cs` | **修改**（T2）：`Configure()` 內觸發 `PipeVariant.ApplyRandom(scale)`（維持 ADR-009 單一接口） |
| Prefab | `Assets/Prefabs/Pipe.prefab` | **修改**（T3）：BottomPipe/TopPipe 的 MeshFilter+MeshRenderer 換成 SpriteRenderer；掛 PipeVariant 並接線（renderers/colliders/variants） |
| Script | `Assets/Scripts/PipeSpawner.cs` | **不變**（仍只呼叫 `Configure(position, gapSize)`） |
| Material | `Assets/Materials/Pipe.mat` | **不變**（**不沿用**——URP Lit 材質不適用 SpriteRenderer；水管 SpriteRenderer 用 URP Sprite-Unlit-Default，見 T3 作法） |
| Asset | `Assets/Art/Atlases/Background_Planets.spriteatlasv2`（既有） | **沿用**（T4，P1）：Multiple mode atlas（spriteMode=2，guid `de29e1d52cb07b44c824c57a83cbefb6`），packable = `Assets/Art/Sprites/Background/Universe/PixelArt Planet.png`，20 子 sprite `PixelArt Planet_0`~`PixelArt Planet_19`（PPU=100、spriteGenerateFallbackPhysicsShape=1）——variants 來源（決策 B 定案，見開放決策 B） |
| Script | `Assets/Scripts/CollisionHandler.cs` | **不變**（trigger-based：`OnTriggerEnter2D` → `CompareTag("Pipe")` → `Die()`，CollisionHandler.cs:7-14） |

## 現況診斷（已驗證事實，附出處）

以下事實均直接讀取對應檔案確認，非推測：

1. **Pipe.prefab 結構**（`Assets/Prefabs/Pipe.prefab`）：Pipe(root, tag Pipe, m_IsActive:0, :14-19) + PipeController（moveSpeed=5、bottomPipe/topPipe 引用，:47-52）→ 子物件 BottomAnchor（localScale y=10，:467-481）→ BottomPipe（localPosition y=-0.5，:72-86）、TopAnchor（localScale y=10，:298-313）→ TopPipe（localPosition y=0.5，:333-347）、ScoreZone（BoxCollider2D 1×6.5 + ScoreTrigger，:223-281）。
2. **BottomPipe/TopPipe 目前是 Mesh 渲染**：MeshFilter（內建 cube mesh fileID 10210，:87-94/:397-404）＋ MeshRenderer（sortingOrder 50、材質 guid f0342ec7a8c7a504996465e2230d0d2e，:95-143/:348-396）＋ BoxCollider2D（size 1×1、IsTrigger=1、tag Pipe，:144-189/:405-450）。
3. **Pipe.mat 為純綠色無貼圖**（`Assets/Materials/Pipe.mat`）：`_BaseColor` (0.51, 0.97, 0.21)（:124-125）、`_BaseMap` m_Texture fileID 0（:31-34）——無任何貼圖。
4. **PipeSpawner 池化生成**（`Assets/Scripts/PipeSpawner.cs`）：`PooledActives<PipeController>`（ADR-010，:26/:36-45）；`SpawnPipe()` 隨機 gapCenter → `pipeController.Configure(spawnPosition, gapSize)`（:66-78）。**場景序列化值**（`Assets/Scenes/SampleScene.unity:2587-2593`）：spawnInterval=2、spawnXPosition=8、gapSize=6、minHeight=-4、maxHeight=4、poolSize=7、despawnXPosition=-8（以場景覆寫值為準，非腳本預設）。
5. **PipeController 單一接口**（`Assets/Scripts/PipeController.cs`）：`Configure(Vector3 position, float gapSize)`（:23-27）→ `SetGap`（bottomPipe.localPosition y=-gapSize*0.5、topPipe y=+gapSize*0.5，:30-34）；Update 左移 moveSpeed=5（:12-20）；**無任何 sprite 邏輯**。
6. **碰撞為 trigger-based**（`Assets/Scripts/CollisionHandler.cs:7-14`）：`OnTriggerEnter2D` → `CompareTag("Pipe")` → `gameManager.Die()`——水管 collider 維持 IsTrigger=1 即可，不需改碰撞模式。
7. **專案內沒有任何 pipe sprite**：`Assets/Art/Sprites/` 僅 WhiteSquare.png（空）；`Assets/Art/Atlases/Background/spritesheet_default.png`（70 子 sprite，TexturePacker XML，grep "pipe|Pipe" 於 Art/ 零命中）——**無現成 pipe 圖**（決策 B 定案：variants 改用既有 `Background_Planets.spriteatlasv2` 行星子 sprite，見開放決策 B）。
8. **既有圖集切片流程**（`Assets/Scripts/Editor/AtlasTools/AtlasXmlSlicerWindow.cs`）：TexturePacker XML + `ISpriteEditorDataProvider`（UnityEditor.U2D.Sprites）切片，spriteMode=2（Multiple）；**Unity 6000.3 已移除 TextureImporter.spritesheet**（:15-20 註解）——T4 沿用此工具或同 API 路徑。
9. **無既有 physics shape 使用**：grep `GetPhysicsShape|PolygonCollider2D` 於 Assets/Scripts 零命中（僅 SpriteRenderer 用於 Bird/Tile/BGLayer）——雙軌 collider 策略為全新程式碼。

### 架構背景（ADR 出處）

- **ADR-009**（`docs/adr/009-pipe-pair-deepening.md`）：PipeController 公開介面收斂為單一 `Configure(center, gapSize)`；`bottomPipe`/`topPipe` 收 private serialized。**本功能維持此接口**——PipeSpawner 零改動，`Configure()` 內部觸發 PipeVariant（決策 3）。
- **ADR-010**（`docs/adr/010-pooled-actives.md`）：池週期由 `PooledActives<T>` 處理；**pool 重用陷阱**（決策 6）——同一 PipeController 實例會被重複 Get/Release，`ApplyRandom` 必須每次完整重置，不能只在 Awake 做一次。
- **dependency-rules P1-P3**（`specs/architecture/dependency-rules.md:44-46`）：PipeController 單一 Configure、移動內部驅動、PascalCase——本功能不違反。
- **ADR-007**（`docs/adr/007-menu-hide-renderer.md`）：`GetComponentsInChildren<Renderer>()` 涵蓋 SpriteRenderer（共同基類 Renderer）——MeshRenderer→SpriteRenderer 遷移不影響 Menu 隱藏邏輯（PlayerController.cs:55 註解亦確認）。

### 未驗證假設

- **A1（API 存在性）**：`Sprite.GetPhysicsShapeCount()` / `Sprite.GetPhysicsShape(int, List<Vector2>)` 與 `PolygonCollider2D.SetPath(int, Vector2[])` 在 Unity 6000.3.12f1 存在且行為如預期——**開工先用 unity_reflect 驗證**（T1 任務單已列）。
- **A2（physics shape 資料源）**：Sprite Editor 的 Physics Shape 點資料可經 `Sprite.GetPhysicsShape` 讀取（含 Multiple mode 子 sprite）——依 Unity 官方 API 設計，但未在本專案實證。
- **A3（spriteGenerateFallbackPhysicsShape）**：無自訂 physics shape 的 sprite 是否自動生成 fallback shape（`spritesheet_default.png.meta:53` 有 `spriteGenerateFallbackPhysicsShape: 1`）——若為 true，雙軌的「無 physics shape」分支可能永不觸發；不影響正確性（fallback 分支仍保留為防禦）。
- **A4（scale 對 gap 的影響）**：scale 套在 BottomPipe/TopPipe 兩段 transform 時，gap（anchor localPosition ±gapSize×0.5）保持固定——依 Transform 階層語意成立，但需動態驗證確認視覺間隙不變。

## 開放決策（A/B/C）

### 決策 A：pipe 變體造型（矩形直管 vs 不規則）——已定案（2026-10-01）
- **背景**：pipe 變體為矩形直管 vs 不規則造型，決定 collider 方案主軌。
- **定案**：背景行星為**不規則圓形**——collider 雙軌的 **physics shape 主軌為預期路徑**：`spriteGenerateFallbackPhysicsShape=1` 使 Unity 自動生成 fallback shape，`GetPhysicsShapeCount()>0` 主軌恆觸發；**bounds fallback 保留為防禦**（雙軌架構不變，FR-04）。

### 決策 B：pipe sprite atlas 來源——已定案（2026-10-01，Human 確認）
- **定案**：使用 `Assets/Art/Atlases/Background_Planets.spriteatlasv2`——Multiple mode（spriteMode=2）、guid `de29e1d52cb07b44c824c57a83cbefb6`、packable = `Assets/Art/Sprites/Background/Universe/PixelArt Planet.png`、20 個子 sprite `PixelArt Planet_0`~`PixelArt Planet_19`（PPU=100、spriteGenerateFallbackPhysicsShape=1）。
- **影響**：variants 來源 = 上述 20 子 sprite；T4 placeholder 方案撤銷（T4 改為接線既有 atlas 子 sprite，不再匯入切片）。

### 決策 C：scale 範圍——已定案
- **定案**：0.8~1.2（serialized 可調，FR-03/FR-07）。

## 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| 使用指定 sprite（Multiple atlas 子 sprite） | FR-01／FR-02；T1／T4 |
| 每次生成隨機選一個子 sprite | FR-01；T1 |
| 隨機 scale（0.8~1.2 可調） | FR-03；T1 |
| collider 匹配所選子 sprite 的外觀 | FR-04／FR-05；T1 |

## 功能需求（FR）

### A. PipeVariant 元件（新）

- **FR-01 隨機選 sprite variant**：`PipeVariant.ApplyRandom(float scale)` 從 `Sprite[] variants` 隨機選一個 sprite，套用到 BottomPipe/TopPipe 兩段的 SpriteRenderer（`SpriteRenderer[] renderers`，長度 2，索引 0=Bottom、1=Top）。**空 variants 防護（PV3）**：`ApplyRandom` 開頭必須 `if (variants == null || variants.Length == 0) return;`——variants 空陣列時索引會拋 IndexOutOfRangeException（見 BC-4）。
- **FR-02 上下管同 variant**：同一根 pipe 的 BottomPipe 與 TopPipe 使用**同一個**隨機選出的 sprite（視覺對稱）；不同 pipe 之間各自獨立隨機。
- **FR-03 隨機 scale**：`ApplyRandom(float scale)` 接收的 scale 由呼叫端（PipeController）以 `Random.Range(minScale, maxScale)` 產生；scale 套在 BottomPipe/TopPipe 兩段 transform 的 localScale（**不套 root**——避免 gap 跟著縮，見決策 7）。`minScale`/`maxScale` 為 serialized 欄位，預設 0.8/1.2（開放決策 C）。
- **FR-04 collider 雙軌匹配**：對每個 renderer 對應的 collider（`PolygonCollider2D[] colliders`，與 renderers 同序；FR-09 已強制 PolygonCollider2D，具體型別使接線契約編譯期強制、免 runtime cast）：
  - **主軌（physics shape）**：`sprite.GetPhysicsShapeCount() > 0` → 逐 shape 讀 `sprite.GetPhysicsShape(i, path)`，寫入 `PolygonCollider2D`（`SetPath(i, path.ToArray())`）。
  - **fallback（bounds 矩形）**：無 physics shape → 用 `sprite.bounds` 生成矩形 path（4 點）寫入 PolygonCollider2D。
  - 兩軌共用同一 PolygonCollider2D 元件（T3 由 BoxCollider2D 換成 PolygonCollider2D）。
- **FR-05 pool 重用完整重置**：`ApplyRandom` 每次呼叫必須完整重置——設 sprite、`polygonCollider.pathCount` 歸零（或覆蓋全部 path）、重 `SetPath`；**不得只在 Awake 做一次**（ADR-010 pool 重用陷阱，決策 6）。

### B. PipeController 觸發

- **FR-06 Configure 內觸發**：`PipeController.Configure(position, gapSize)`（PipeController.cs:23-27）在 `SetGap` 後觸發 `pipeVariant.ApplyRandom(Random.Range(minScale, maxScale))`——維持 ADR-009 單一接口，PipeSpawner 零改動。`PipeVariant` 以 `[SerializeField] private PipeVariant pipeVariant;` 注入（prefab 接線，T3）。
- **FR-07 scale 範圍 serialized**：`PipeController` 新增 `[SerializeField] private float minScale = 0.8f; [SerializeField] private float maxScale = 1.2f;`（可調，開放決策 C）。

### C. Prefab 遷移

- **FR-08 MeshRenderer→SpriteRenderer**：BottomPipe/TopPipe 移除 MeshFilter+MeshRenderer，改掛 SpriteRenderer（sortingOrder 維持 50，與背景帶位一致——shared/project.md「水管 50」）；材質用 **URP Sprite-Unlit-Default**（guid `9dfc825aed78fcd4ba02077103263b40`，背景 tile 慣例——shared/project.md「材質 URP Sprite-Unlit-Default」）；**明確禁止沿用 `Assets/Materials/Pipe.mat`**（URP Lit 材質，不適用 SpriteRenderer，見 T3 作法）。
- **FR-09 BoxCollider2D→PolygonCollider2D**：BottomPipe/TopPipe 的 BoxCollider2D 換成 PolygonCollider2D（IsTrigger=1、tag Pipe 維持——CollisionHandler.cs:10 依 tag 判定死亡）。

## 非功能需求（NFR）

- **NFR-01 PipeSpawner 零改動**：`PipeSpawner.cs` 不變（仍只呼叫 `Configure`）——ADR-009 接口維持。
- **NFR-02 計分不受影響**：ScoreZone（BoxCollider2D 1×6.5 + ScoreTrigger）不變；計分路徑（ScoreTrigger 唯一入口）不受 sprite 變體影響。
- **NFR-03 碰撞語意不變**：水管 collider 維持 IsTrigger=1（trigger-based 死亡，CollisionHandler.cs:7-14）；PolygonCollider2D 的 trigger 語意與 BoxCollider2D 相同。
- **NFR-04 熱路徑零分配**：`ApplyRandom` 在生成路徑（非每幀）執行，允許少量分配；但 `GetPhysicsShape` 的 List 緩衝應重複使用（避免每根 pipe 新建 List），比照 PL3 精神。
- **NFR-05 視覺間隙不變**：scale 套在兩段 transform 後，gap（anchor localPosition）保持固定——玩家可通過的間隙寬度不因 scale 改變（決策 7）。

## 任務清單

| # | 任務 | Priority | Assigned | arch-review | Goal | Unity Objects | 驗收 |
|---|------|----------|----------|-------------|------|---------------|------|
| T1 | 新建 PipeVariant 元件 | P0 | gameplay-developer | **yes** | 隨機選 sprite variant＋雙軌 collider 同步＋scale 套用＋pool 重用重置 | `Assets/Scripts/PipeVariant.cs`（新） | ApplyRandom 每次完整重置；physics shape 與 bounds fallback 兩軌皆正確 |
| T2 | PipeController 觸發 ApplyRandom | P0 | gameplay-developer | **yes** | Configure 內觸發 ApplyRandom＋scale 範圍 serialized | `Assets/Scripts/PipeController.cs` | 每次 Configure 觸發一次 ApplyRandom；scale 落在 [minScale, maxScale] |
| T3 | Pipe.prefab 遷移 | P0 | scene-developer | **yes** | BottomPipe/TopPipe 換 SpriteRenderer＋PolygonCollider2D；掛 PipeVariant 並接線 | `Assets/Prefabs/Pipe.prefab` | prefab 結構正確；renderers/colliders/variants 接線完整；sortingOrder 50 維持 |
| T4 | variants 來源（Background_Planets 子 sprite） | P1 | asset-developer | no | 確認 variants 來源 = Background_Planets（Multiple mode）20 子 sprite（既有 atlas，無需匯入切片） | `Assets/Art/Atlases/Background_Planets.spriteatlasv2`（既有） | variants 來源為 Background_Planets（Multiple mode）子 sprite，筆數 = 20 |

### T1 — 新建 PipeVariant 元件（P0, gameplay-developer, arch-review: yes）
- **Goal**：單一職責元件——隨機選 sprite variant＋同步 collider＋套用 scale。
- **作法**：
  - 新建 `Assets/Scripts/PipeVariant.cs`（MonoBehaviour，掛 Pipe root）。
  - Serialized 欄位：`SpriteRenderer[] renderers`（長度 2：0=BottomPipe、1=TopPipe）、`PolygonCollider2D[] colliders`（與 renderers 同序；FR-09 已強制 PolygonCollider2D，具體型別使接線契約編譯期強制）、`Sprite[] variants`（Multiple atlas 子 sprite 清單）。
  - `public void ApplyRandom(float scale)`：
    0. **空 variants 防護（PV3）**：開頭 `if (variants == null || variants.Length == 0) return;`——variants 空陣列時 `variants[Random.Range(0, variants.Length)]` 拋 IndexOutOfRangeException（BC-4）。
    1. 隨機選 `Sprite sprite = variants[Random.Range(0, variants.Length)]`。
    2. 對每個 renderer：`renderer.sprite = sprite`。
    3. 對每個 collider（PolygonCollider2D）：**完整重置**——`polygonCollider.pathCount = 0`（或先清全部 path）；`sprite.GetPhysicsShapeCount() > 0` → 逐 shape `GetPhysicsShape(i, path)` → `SetPath(i, path.ToArray())`；否則 → 用 `sprite.bounds` 生成矩形 path（4 點）→ `SetPath(0, rect)`。
    4. 對每個 renderer 對應 transform：`localScale = new Vector3(scale, scale, 1)`（**不套 root**）。
  - **開工先用 unity_reflect 驗證**：`Sprite.GetPhysicsShapeCount` / `Sprite.GetPhysicsShape` / `PolygonCollider2D.SetPath` 存在性（未驗證假設 A1）。
- **驗收**：呼叫 `ApplyRandom(1f)` 後 renderer.sprite 為 variants 中之一；collider path 與 sprite 外觀匹配（physics shape 或 bounds 矩形）；連續呼叫兩次（模擬 pool 重用）第二次結果完整覆蓋第一次（無殘留 path）；variants 為空（或 null）時呼叫 `ApplyRandom` 不拋錯（PV3 guard）。

### T2 — PipeController 觸發 ApplyRandom（P0, gameplay-developer, arch-review: yes）
- **Goal**：維持 ADR-009 單一接口，`Configure()` 內觸發變體套用。
- **作法**：
  - `PipeController.cs` 新增 `[SerializeField] private PipeVariant pipeVariant;`、`[SerializeField] private float minScale = 0.8f;`、`[SerializeField] private float maxScale = 1.2f;`。
  - `Configure(position, gapSize)`（:23-27）在 `SetGap(gapSize)` 後加：`if (pipeVariant != null) pipeVariant.ApplyRandom(Random.Range(minScale, maxScale));`（null guard 防未接線）。
  - **不改**：Update 移動（:12-20）、SetGap（:30-34）、公開接口簽名。
- **驗收**：每次 `Configure` 觸發一次 `ApplyRandom`；scale 落在 [minScale, maxScale]；未接線時（pipeVariant null）不拋錯。

### T3 — Pipe.prefab 遷移（P0, scene-developer, arch-review: yes）
- **Goal**：prefab 結構遷移——Mesh→Sprite、Box→Polygon、掛 PipeVariant 接線。
- **作法**：
  - BottomPipe/TopPipe：移除 MeshFilter（fileID 10210）+ MeshRenderer；改掛 SpriteRenderer（sortingOrder 50 維持；材質用 **URP Sprite-Unlit-Default**（guid `9dfc825aed78fcd4ba02077103263b40`，背景 tile 慣例——shared/project.md「材質 URP Sprite-Unlit-Default」）；**明確禁止沿用 `Assets/Materials/Pipe.mat`**——該材質是 URP Lit，不適用 SpriteRenderer）。
  - BottomPipe/TopPipe：BoxCollider2D（size 1×1、IsTrigger=1、tag Pipe）換成 PolygonCollider2D（IsTrigger=1、tag Pipe 維持）。
  - Pipe root：掛 PipeVariant；接線 renderers[0]=BottomPipe 的 SpriteRenderer、renderers[1]=TopPipe 的 SpriteRenderer、colliders 同序、variants=atlas 子 sprite 清單（T4 產物；T4 未完成前可留空，T1 的 null guard 涵蓋）。
  - PipeController：接線 pipeVariant 引用。
  - **不改**：ScoreZone（BoxCollider2D 1×6.5 + ScoreTrigger）、anchor 結構（BottomAnchor/TopAnchor localScale y=10）、root tag/active 狀態。
- **驗收**：prefab 無 MeshFilter/MeshRenderer 殘留；SpriteRenderer sortingOrder=50；PolygonCollider2D IsTrigger=1、tag Pipe；PipeVariant 四欄位接線完整。

### T4 — variants 來源（Background_Planets 子 sprite）（P1, asset-developer, arch-review: no）
- **Goal**：variants 來源 = Background_Planets（Multiple mode）20 子 sprite（既有 atlas，無需匯入切片；T4 placeholder 方案撤銷）。
- **作法**：
  - 依開放決策 B（已定案）：variants 來源 = `Assets/Art/Atlases/Background_Planets.spriteatlasv2` 的 20 個子 sprite `PixelArt Planet_0`~`PixelArt Planet_19`（Multiple mode，spriteMode=2，guid `de29e1d52cb07b44c824c57a83cbefb6`，packable = `Assets/Art/Sprites/Background/Universe/PixelArt Planet.png`，PPU=100、spriteGenerateFallbackPhysicsShape=1）。
  - **不再匯入切片**：atlas 已存在，T4 不做匯入/切片——確認 atlas 元資料（Multiple mode、20 子 sprite）並確保 variants 陣列引用正確（prefab 換源由 scene-developer 於 T3 執行）。
  - 子 sprite 命名：沿用既有 `PixelArt Planet_*` 名稱（既有資產，不強制改為小寫底線）。
- **驗收**：variants 來源為 Background_Planets（Multiple mode）子 sprite，筆數 = 20（`PixelArt Planet_0`~`PixelArt Planet_19`）；T3 接線後 variants 清單非空。

## 邊界情況

| 邊界 | 描述 | 防護 |
|------|------|------|
| BC-1 pool 重用殘留 | 同一 PipeController 實例被池重複 Get（ADR-010），第二次 ApplyRandom 若不重置，collider path 殘留第一次的 shape（尤其 shape 數不同時） | FR-05：每次 ApplyRandom 完整重置（pathCount 歸零＋重 SetPath） |
| BC-2 無 physics shape 的 sprite | 子 sprite 未在 Sprite Editor 定義 Physics Shape 時，GetPhysicsShapeCount()==0 | FR-04 fallback：用 sprite.bounds 生成矩形 path——矩形/不規則變體皆可用 |
| BC-3 scale 影響 gap | 若 scale 套在 root，BottomPipe/TopPipe 的 anchor localPosition 跟著縮放，gap 視覺變窄 | 決策 7：scale 只套 BottomPipe/TopPipe 兩段 transform，anchor 不動 |
| BC-4 未接線 PipeVariant／空 variants | prefab 尚未掛 PipeVariant 或 variants 為空時呼叫 Configure | T2 null guard（`if (pipeVariant != null)`）涵蓋「未接線」；**variants 空陣列時 `variants[Random.Range(0, variants.Length)]` 拋 IndexOutOfRangeException**（Random.Range(0,0) 回傳 0 不拋錯，但後續索引空陣列拋錯）——T1 空 variants guard（PV3：`if (variants == null \|\| variants.Length == 0) return;`）涵蓋；T3/T4 接線順序保證 |
| BC-5 上下管不同步 | 若 Bottom/Top 各選不同 sprite，視覺不對稱 | FR-02：同一 pipe 共用同一個隨機 sprite |
| BC-6 計分區不受影響 | ScoreZone 尺寸 1×6.5 對應 gapSize=6（pipe-score 既有），sprite 變體不應改變 gap 語意 | NFR-02/NFR-05：ScoreZone 不動；scale 不套 root 故 gap 固定 |
| BC-7 碰撞語意退化 | PolygonCollider2D 若誤設 IsTrigger=0，鳥會物理彈開而非穿過觸發死亡 | FR-09：IsTrigger=1 維持；驗收檢查 |

## 驗收標準（整體，Validator 對照用）

- [ ] **隨機變體**：每次生成的水管從 variants 隨機選一個 sprite；同一 pipe 的 Bottom/Top 使用相同 sprite（FR-01/FR-02）
- [ ] **隨機 scale**：scale 落在 [minScale, maxScale]（預設 0.8~1.2），套在 BottomPipe/TopPipe 兩段 transform，不套 root（FR-03/決策 7）
- [ ] **collider 匹配**：collider 形狀與所選 sprite 外觀一致——有 physics shape 用 shape 點、無 shape 用 bounds 矩形（FR-04）
- [ ] **pool 重用重置**：同一 pipe 實例被池重用後，第二次 ApplyRandom 完整覆蓋第一次（無殘留 path）（FR-05/BC-1）
- [ ] **PipeSpawner 零改動**：`PipeSpawner.cs` 無任何修改（NFR-01）
- [ ] **計分與碰撞不回歸**：ScoreZone 計分正常；水管 collider 維持 IsTrigger=1、tag Pipe，撞管死亡正常（NFR-02/NFR-03/BC-7）
- [ ] **視覺間隙不變**：scale 變化下 gap 視覺寬度保持固定（NFR-05/BC-3）
- [ ] **prefab 遷移完整**：無 MeshFilter/MeshRenderer 殘留；SpriteRenderer sortingOrder=50、材質 URP Sprite-Unlit-Default（禁 Pipe.mat）；PolygonCollider2D 接線正確（FR-08/FR-09）

## 規格自檢清單

- **事實主張標出處**：現況診斷每條標檔案＋行號（Pipe.prefab:14-19/47-52/72-86/87-94/95-143/144-189/223-281/298-313/333-347/348-396/397-404/405-450/467-481、Pipe.mat:31-34/124-125、PipeSpawner.cs:26/36-45/66-78、SampleScene.unity:2587-2593、PipeController.cs:12-20/23-27/30-34、CollisionHandler.cs:7-14、AtlasXmlSlicerWindow.cs:15-20、spritesheet_default.png.meta:53、`Background_Planets.spriteatlasv2` 元資料（Multiple mode spriteMode=2、guid de29e1d52cb07b44c824c57a83cbefb6、packable、20 子 sprite、PPU=100、spriteGenerateFallbackPhysicsShape=1——決策 B 定案依據）、ADR-009、ADR-010、ADR-014、dependency-rules.md:44-46/48-54、ADR-007、PlayerController.cs:55）。
- **未驗證假設**：A1（GetPhysicsShape/SetPath API 存在性——開工 unity_reflect 驗證）、A2（physics shape 資料源）、A3（spriteGenerateFallbackPhysicsShape 影響）、A4（scale 不影響 gap 的動態確認）。
- **一致性**：BC-1 與 FR-05/T1 一致（「pool 重用殘留」）；BC-2 與 FR-04/T1 一致（「無 physics shape fallback」）；BC-3 與 FR-03/決策 7 一致（「scale 影響 gap」）；BC-4 與 T2 null guard＋T1 空 variants guard（PV3）一致（「未接線 PipeVariant／空 variants」）；BC-5 與 FR-02 一致（「上下管不同步」）；BC-7 與 FR-09 一致（「碰撞語意退化」）。
- **Editor API 驗證**：T1 涉及 runtime API（Sprite.GetPhysicsShape/PolygonCollider2D.SetPath）——**開工先用 unity_reflect 驗證存在性**（未驗證假設 A1）；T4 不再涉及 Editor API 切片（決策 B 定案後 atlas 既有，無匯入/切片工作，見 T4 作法）。

## arch-review 判定

**此功能觸發架構變更，arch-review = yes**，範圍：

1. **新共享元件 PipeVariant**（T1/T2）：新增跨腳本依賴（PipeController → PipeVariant），需 Tech Lead 審視元件職責邊界與 dependency-rules 是否新增規則（如 PV1-PV3）。
2. **MeshRenderer→SpriteRenderer 遷移**（T3）：水管渲染從 Mesh 遷移到 Sprite——需確認與 ADR-007（Renderer.enabled 隱藏）、sortingOrder 帶位（水管 50）、URP Sprite-Unlit-Default 材質慣例相容；是否需 ADR 記錄遷移決策。
3. **collider 同步策略**（T1）：BoxCollider2D→PolygonCollider2D＋動態 path 寫入——碰撞體從靜態序列化改為 runtime 動態，需 Tech Lead 審視與 ADR-003（collision rules）相容性。
4. **開放決策 A 影響**：pipe 變體為矩形直管 vs 不規則造型決定 collider 方案主軌——**已定案（2026-10-01）：背景行星為不規則圓形，physics shape 主軌為預期路徑**（spriteGenerateFallbackPhysicsShape=1 使主軌恆觸發，bounds fallback 保留為防禦）；程式已雙軌並存不阻擋，但 Tech Lead 需確認雙軌策略的架構合理性。