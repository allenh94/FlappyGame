# BG Layer Editor／背景授權系統架構

> 本文件由 tech-lead-agent 維護（Task 1 / Phase 2 產出），其他 agent 唯讀。
> 需求真相來源：`specs/requirements/bg-layer-editor.md`。本文引用其條目編號作為裁定依據。

## 0. 模組總覽（Class Diagram）

```
┌─ Editor（Assets/Scripts/Editor/Background/，不進 build）──────────┐
│  BGLayerEditorWindow : EditorWindow                               │
│    · 讀寫 BGConfig（唯一寫入者）                                    │
│    · Layer List UI（FR-14）／Tile 配置（FR-15）／Template 編輯(FR-10)│
│    · Scene View 即時預覽（FR-16，Edit mode only）                   │
└──────────────┬────────────────────────────────────────────────────┘
               │ 寫入（單向）
┌──────────────▼────────────────────────────────────────────────────┐
│  BGConfig : ScriptableObject（Assets/Config/Background/）          │
│    layers : List<LayerDef>                                        │
│      LayerDef { name, depth, renderOrder, template:TileTemplate,   │
│                 tileWidth }                                       │
└──────────────┬────────────────────────────────────────────────────┘
               │ 唯讀（Runtime 啟動時載入一次）
┌──────────────▼────────────────────────────────────────────────────┐
│  Runtime（Assets/Scripts/Background/）                             │
│                                                                   │
│  BGManager : MonoBehaviour（場景單例，掛 SampleScene 根物件）        │
│    · Awake()：讀 BGConfig → 逐 LayerDef 建立 BGLayer                │
│    · 持有 TilePool 字典（key = template）                           │
│    · ResetAll()：供 GameManager 死亡重開呼叫（單向接線）             │
│                                                                   │
│  BGLayer : MonoBehaviour（每 Layer 一個，由 BGManager 建立於        │
│    "Background" 根下；全部 z=0）                                   │
│    · LayerDef 資料（depth/renderOrder/template/tileWidth）          │
│    · Update()：沿襲 Mathf.Repeat(scrollDistance, tileWidth) 取模     │
│      推導位移（無縫無漂移，承襲 BackgroundLayer 思想）               │
│    · 設定子 tile 的 SpriteRenderer.sortingOrder = renderOrder       │
│                                                                   │
│  TilePool（per-template UnityEngine.Pool.ObjectPool<Tile>，         │
│    比照 PipeSpawner 模式：createFunc/onGet/onRelease/onDestroy）    │
│                                                                   │
│  Tile : MonoBehaviour（pooled instance，結構 = TileTemplate prefab  │
│    的 clone；子物件為純 SpriteRenderer，無腳本）                     │
└───────────────────────────────────────────────────────────────────┘

TileTemplate ＝ Prefab 資產（Assets/Prefabs/Background/Tiles/）
GameManager ──呼叫──▶ BGManager.ResetAll()   （唯一跨系統介面）
```

## 1. Data Flow（配置 → 實例）

```
[Edit mode]
BGLayerEditorWindow ──寫──▶ BGConfig.asset（唯一配置真相來源）
                                  │
[Play 進入]                        ▼
BGManager.Awake() ──讀──▶ BGConfig.layers[]
     │ 逐條建立
     ▼
BGLayer(depth, renderOrder, template, tileWidth)
     │ 計算需求 tile 數 N = ceil(可視寬/tileWidth)+餘量（NFR-03/NFR-05）
     ▼
TilePool.Get() ×N ──▶ activeTiles 固定序列（全生命週期唯一 Instantiate 批次，
     │ 此後池不再出入貨）
     ▼
穩態捲動：Update 以單一 scrollDistance 取模推導全部 tile 位置
     （0 Instantiate/Destroy，NFR-01；熱路徑零 GC，NFR-02；
       「回收重用」＝wrap 邊界的批次整體右移，見 §3/D7）
```

規則：Editor 只寫 SO、Runtime 只讀 SO；反向流禁止。Play mode 中修改配置定義為不支援（規格「邊界情況」已裁定）。

## 2. Rendering Flow

```
Depth ──▶ BGLayer.Update() 速度因子：speed = baseSpeed / depth（§5 D5）
              │ 只影響 transform.x 位移量
              ▼
Render Order ──▶ Tile 子 SpriteRenderer.sortingOrder（Default Sorting Layer）
              │ 只影響遮擋前後；與 z 無關（FR-04/FR-05）
              ▼
鳥（MeshRenderer @z=0）：renderer.sortingOrder = 1000（＞全部背景層帶）
              │ T5 實證：MeshRenderer 於 Transparent queue 下 sortingOrder
              │ 生效；若失效 fallback＝鳥根節點加 SortingGroup 或改 Sprite
              │ Renderer（T5 責任，二擇一，不得回頭用 z 排序）
              ▼
水管：不納入本系統（維持 z=0 平面既有行為；鳥↔水管遮擋非背景系統管轄）

sortingOrder 帶位分配：背景層 10/20/30…（每層間隔 10，層內子物件 ±9 微調）；
鳥 ≥1000。全部位於 Default Sorting Layer（不新增 Sorting Layer）。
```

鐵律：所有背景層根與 tile 一律 z=0；遮擋禁用 z（FR-04）。相機透視投影不再是排序機制，僅是既有的視野事實（NFR-05）。

## 3. Tile Lifecycle

```
Create   ：Play 進入時 TilePool 初始化，Instantiate(template)×N
           （NFR-01 允許的唯一常態 Instantiate 點）
Checkout ：初始化批次 tilePool.Get() ×N → activeTiles 固定序列
           （此後池不再出入貨；onRelease/collectionCheck 穩態閒置）
Move     ：隨所屬 BGLayer 以 speed=baseSpeed/depth 左移
Wrap     ：scrollDistance 以 Mathf.Repeat(…, tileWidth) 歸零瞬間，
           全體 tile 整體右移一個 tileWidth——等價於「最左 tile 跳到
           最右」的批次回收重用；每張 tile 週期性回到前緣
           （FR-07 語義由取模 wrap 達成，非逐 tile OutView 事件）
Reset    ：死亡重開 scrollDistance=0 → 全體回到初始排布
Destroy  ：場景卸載時隨層級銷毀（NFR-01 允許的唯一 Destroy 點）
```

模型裁定理由（為何否決逐 tile Release/Get）見 §4 D7 與 `docs/adr/005-bg-layer-tile-lifecycle.md`。

可視寬計算以透視 FOV60°@z=-10、z=0 平面 ≈20.53@16:9 為基準（NFR-05），餘量常數 ≥1 tileWidth。

## 4. 決策裁定（D1–D7）

### D1 TileTemplate 資產形式 → **Prefab**
Prefab 原生支援多子物件層級（FR-08）、Scene 內直觀編輯（FR-10）、pool createFunc 直接 `Instantiate(prefab)` 零轉換成本。
否決 SO 清單式模板：需自寫子物件序列化＋runtime 重建邏輯，等於重造 prefab 且無法在場景中直觀編輯。

### D2 配置儲存與 Runtime 橋接 → **BGConfig ScriptableObject，單向流**
SO 原生序列化 prefab 引用（D1 配套）、Inspector 可檢查、YAML 版控友善。Editor Window 是唯一寫入者，Runtime 唯讀（FR-17/FR-18）。
否決 JSON：失去引用序列化、需自維護 schema；否決場景內嵌配置：場景即實例，違反配置/實例分離精神。

### D3 整合策略 → **新類別承接＋BackgroundLayer 退役；GameManager 過渡接線**
新 BGManager/BGLayer/TilePool 承接全部職責；`Mathf.Repeat` 取模捲動思想移植後刪除 BackgroundLayer.cs（單類包辦三職責是現狀病灶，且固定 `[3]` 陣列無法承載動態 Layer，FR-12/FR-01）。GameManager 移除 `backgroundLayers` 欄位，改持 BGManager 引用，死亡重開呼叫 `ResetAll()`——Reset 語義保留、舊型別依賴不回歸（FR-13、規格「涉及物件」表）。
否決原地改造 BackgroundLayer：職責膨脹不可逆；否決雙系統並存：直接違反 FR-12。

### D4 排序方案 → **sortingOrder 帶位接管遮擋；鳥 order=1000 納入同體系**
背景全 SpriteRenderer 落 Default Sorting Layer，order 帶 10/20/30…（FR-05）；全部 z=0，透視相機退出排序職責（FR-04）。鳥 MeshRenderer 設 sortingOrder=1000 保證恆在背景之上（規格「鳥可見性」驗收項）；MeshRenderer+transparent queue 行為由 T5 實證，fallback 已備（§2）。水管不納入。
否決續用 z 投影：違反分離鐵律；否決新增 Sorting Layer：單 Default＋帶位已足夠，新增徒增 TagManager 維護成本。

### D5 Depth→速度映射 → **speed = baseSpeed / depth；baseSpeed=2.0**
`speed = 2.0 / depth`（depth≥0.01 clamp 防除零）恰好重現現狀三點：depth 1→2.0、2→1.0、4→0.5 u/s，遷移零手感損失（FR-11/FR-13）。示範四層 depth：Sky=8/Mountain=4/Forest=2/Foreground=1。同 Depth 同速合法（規格邊界情況已定）；同 Render Order 由 Unity 行為決定，Editor 文件註明即可。
否決線性插值表／指數曲線：配置面積大、調參不直覺，無對應收益。

### D6 目錄策略與 Editor 隔離 → **慣例目錄隔離，本期不引入 asmdef**
Runtime：`Assets/Scripts/Background/`；Editor：`Assets/Scripts/Editor/Background/`（Unity 特殊資料夾自動排除於 build，零成本滿足 FR-18）；配置：`Assets/Config/Background/`；模板：`Assets/Prefabs/Background/Tiles/`。
否決立即引入 Editor asmdef：專案現狀無任何 asmdef，單獨為 Editor 引入製造不一致；代碼膨脹後再升級（記入 dependency-rules 演進觸發條件）。

### D7 Tile 生命週期模型 → **固定 N 取模整體平移（否決逐 tile Release/Get 動態回收）**
初始化一次 Get×N 後凍結序列，穩態以單一 scrollDistance 取模推導全部位置；「回收重用」於 wrap 邊界批次發生（每張 tile 每 tileWidth 週期回到前緣），行為等價且結構性無漂移——相鄰間距由同一 scrollDistance 推導，恆等 tileWidth。
否決補齊 per-tile OutView→Release→Reuse：對已實測通過的核心做最高風險重寫、零當前收益，且逐 tile 重定位引入累積浮點誤差與排序邊界案例。否決移除 ObjectPool 直接 Instantiate：喪失 B5「Instantiate/Destroy 僅限池邊界」的結構性執法與 template/placeholder 分支集中點。
執行期動態增刪 Layer／多解析度適配列 P2 另案，屆時依 dependency-rules 演進觸發條件重估本決策。完整裁定紀錄：`docs/adr/005-bg-layer-tile-lifecycle.md`。

## 5. Developer 實作切入點

1. T2 先建 BGConfig SO＋BGManager/BGLayer 骨架（不含 pool），打通 GameManager→ResetAll。
2. T3 比照 PipeSpawner 的 ObjectPool 四 callback 模式實作 TilePool；熱路徑禁 LINQ/字串串接/裝箱（NFR-02）。
3. T4 TileTemplate 即 prefab；LayerDef.template 用直接物件引用（null 即 Editor 防呆提示點）。
4. T5 遷移三層→四層示範配置、刪 BackgroundLayer.cs、清理停用裝飾 SpriteRenderer、實證鳥 sortingOrder。
5. T6–T8 EditorWindow 讀寫 SO；預覽僅 Edit mode 操作場景物件，離開前復原（不留髒資料）。
