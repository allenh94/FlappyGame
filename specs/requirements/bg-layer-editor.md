# 規格：BG Layer Editor／背景授權系統（bg-layer-editor）

> Status: final（Phase 1 交付——pm-agent 已正式接管本檔並完成審閱修訂；待 Phase 2 Tech Lead 架構審查）｜ Owner: pm-agent ｜ Date: 2026-08-21
> （沿革：初稿由 Sisyphus 依 pm-agent 已完成的需求分析代寫落盤——當時 pm-agent 寫入權限故障。2026-08-21 pm-agent 接管，本版修訂：新增「涉及物件」總表與「人類核心要求對照表」、各任務單補 Unity Objects、邊界情況補「鳥 MeshRenderer 共存」與「遷移殘留裝飾 Renderer」兩項、整體驗收補「分離鐵律對照測試」與鳥可見性。內容所有權自本版起歸 pm-agent。）

## 功能：BG Layer Editor / Background Authoring System

- **目標**：將現有 3 個固定背景層升級為「動態 Layer 數量＋Tile Template＋Object Pool／Recycle＋Depth／Render Order 分離」的可授權背景系統，並提供 BG Layer Editor 工具供開發者配置背景。
- **性質**：新功能。**擴充既有系統，非平行重建**——現有 BackgroundLayer 捲動機制與 parallax-background-fix 已驗收成果必須整合保留。

## 需求分析

### 現狀（explore 盤點＋讀檔核實，2026-08-21）

| 面向 | 現況 |
|------|------|
| 捲動核心 | `BackgroundLayer.cs`：雙 tile 交替循環，`Mathf.Repeat(scrollDistance + speed * deltaTime, tileWidth)` 取模推導位置，數學上無縫；tile 由程式碼在 `Start()` 即席生成（placeholder sprite 程式碼生成） |
| Layer 註冊 | `GameManager.backgroundLayers` 固定 `BackgroundLayer[3]` 陣列＝唯一註冊點；`StartGame()` foreach 呼叫 `Reset()` |
| 排序現實 | 三層靠 `transform.position.z`（0 / 0.1 / 0.2）＋**透視相機**排序；runtime tile 的 sortingOrder 全為預設 0；TagManager 僅有 `Default` 一個 Sorting Layer；三層根物件上有停用的裝飾性 SpriteRenderer（sortingOrder 10/5/0，執行時不渲染） |
| 相機 | **透視 FOV60° @ z=-10，靜止不動**（無 follow 腳本）；z=0 平面視野寬 ≈20.53 @16:9 |
| 鳥 | MeshRenderer @ z=0（非 SpriteRenderer），與背景層共用 z=0 平面——排序方案必須處理兩者關係 |
| Pool 先例 | `PipeSpawner.cs` 使用 `UnityEngine.Pool.ObjectPool<PipeController>`（createFunc/onGet/onRelease/onDestroy + collectionCheck）——Tile Pool 可比照此模式 |
| 世界模型 | 世界左移制：玩家 X 恆 0、水管每幀左移 5 u/s、相機不動 |
| 工具與資產 | 專案無任何自製 EditorWindow／MenuItem；無背景 prefab；美術僅 `WhiteSquare.png` placeholder |
| 版本管線 | Unity 6000.3.12f1 ＋ URP 17.3.0（Renderer 為 UniversalRendererData 非 2D Renderer——Sorting Layer / Order in Layer 對 SpriteRenderer 正常可用） |

### 核心設計要求（人類原始需求提煉）

1. **Depth 與 Render Order 必須分離**（最重要）：Depth 只管空間深度與 parallax 速度因子；Render Order 只管 Sprite 遮擋前後。兩者不得互相推導，遮擋不得依賴 Z 軸。
2. **Layer 數量不可寫死**：動態新增／刪除／調整。
3. **Tile-Based**：Layer 由 Tile 沿 X 軸重複排列成無限背景；Tile 離開可視範圍即 Recycle 重用。
4. **Tile Template**：Tile 可含多個子物件，作為可重複使用的模板資產；Layer 只指定 Template 名稱。
5. **Editor 配置與 Runtime 實例分離**：Editor 存「背景配置」，Runtime 才建立 GameObject。
6. **效能**：禁止常態 Instantiate/Destroy/GC Allocation；Tile 數量貼近可視範圍。

### 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| Depth 與 Render Order 分離不耦合 | FR-02／FR-03／FR-04／FR-05；整體驗收「分離鐵律對照測試」 |
| 遮擋不依賴 Z 軸 | FR-04／FR-05；整體驗收「Z 重疊」項 |
| Layer 數量動態 | FR-01；Task 2 驗收「新增第 4、5 層無需改程式碼」 |
| Tile Template 可含多子物件 | FR-08／FR-09／FR-10；Task 4 |
| Object Pool／Recycle，禁常態 Instantiate/Destroy | FR-07；NFR-01～NFR-03；Task 3 |
| Editor 配置與 Runtime 實例分離 | FR-17／FR-18；Task 6～Task 8 |
| 整合而非重建既有系統 | 性質宣告；FR-12／FR-13；Task 5 |

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Script | `Assets/Scripts/BackgroundLayer.cs` | **整合改造**（取模無縫捲動思想保留，掛接新系統） |
| Script | `Assets/Scripts/GameManager.cs` | **可能修改**（唯一註冊點 `BackgroundLayer[3]` 改為資料驅動；死亡重開 Reset 語義不得回歸） |
| Script（新） | BGManager／BGLayer／TilePool／TileTemplate 等——類名、檔案位置由 Task 1 裁定 | **新增** |
| Editor Script（新） | 隔離位置（`Assets/Scripts/Editor/` 或獨立 Editor asmdef）由 Task 1 裁定 | **新增**（BG Layer Editor 工具） |
| 配置資產（新） | 背景配置資料形式（ScriptableObject／JSON 等）由 Task 1 裁定 | **新增** |
| Scene | `Assets/Scenes/SampleScene.unity`（BG_Layer_01/02/03 遷移至新系統） | **修改** |
| Art | `Assets/Art/Sprites/WhiteSquare.png` | 不變（placeholder 夠用） |

---

## 功能需求（FR）

### A. Layer 管理

- **FR-01 動態 Layer**：背景由多個 Layer 組成，數量不寫死，可新增／刪除／調整順序／改名；示範配置至少含 Sky／Mountain／Forest／Foreground 四類。
- **FR-02 Depth 屬性**：每 Layer 有獨立 Depth，只負責空間深度語義與 parallax 速度因子。
- **FR-03 Render Order 屬性**：每 Layer 有獨立 Render Order，只負責 Sprite 遮擋前後（數值越高越晚 render、越容易遮擋他人）。
- **FR-04 分離鐵律**：Depth 與 Render Order 不得互相推導或耦合；Sprite 遮擋不得依賴 `transform.position.z`。
- **FR-05 渲染機制**：遮擋一律透過 Unity 2D Sorting Layer / Order in Layer 機制達成。

### B. Tile 系統

- **FR-06 Tile-Based 無限背景**：Layer 由 Tile 沿 X 軸重複排列，無縫延伸。
- **FR-07 Recycle**：Tile 離開相機可視範圍即回收重用；捲動期間禁止常態 Instantiate/Destroy。
- **FR-08 多子物件 Tile**：單一 Tile 可含多個子物件（Tree/Rock/Bush…），在 Editor 一次配置完成。
- **FR-09 Tile Template**：SkyTile/MountainTile/ForestTile 等可重複使用模板；Layer 只指定 Template，Runtime 依模板建立／Recycle tile。
- **FR-10 Template 內容編輯**：Template 的子物件可在 Editor 增刪與配置。

### C. Parallax 與既有系統整合

- **FR-11 Depth→速度**：Depth 越大捲動越慢（遠慢近快），方向與現有系統一致。
- **FR-12 單一 Parallax**：整合既有 BackgroundLayer 捲動機制，全專案不得出現第二套並行的 parallax 實作。
- **FR-13 不回歸**：parallax-background-fix 已驗收成果（無縫循環、Reset 正確、渲染穩定）不得破壞；死亡重開行為保持。

### D. Editor 工具（BG Layer Editor）

- **FR-14 Layer List UI**：新增／刪除／排序／改名／編輯 Depth／編輯 Render Order／指定 Tile Template。
- **FR-15 Tile 配置**：Tile Width、排列方式於 Editor 指定。
- **FR-16 Scene View 即時預覽**：任何配置修改立即反映在 Scene View。
- **FR-17 配置／實例分離**：Editor 存「背景配置資料」，Runtime 才依配置建立 GameObject。
- **FR-18 Editor/Runtime 隔離**：Editor 功能不得污染 Runtime 核心。

## 非功能需求（NFR）

- **NFR-01 Instantiate/Destroy 約束**：僅允許發生於 Play 進入時的池初始化與場景卸載；穩態捲動期間為 0 次。
- **NFR-02 GC 控制**：穩態捲動每幀 GC Allocation = 0（Profiler 驗證）；熱路徑（Update／recycle）不得有字串串接、裝箱、LINQ、new 分配。
- **NFR-03 Tile 數量上限**：任一 Layer 同時存在的 Tile 數以「覆蓋相機可視寬度＋最小餘量」為上限，不得預鋪全關卡或隨時間增長。
- **NFR-04 效能基準**：連續捲動 ≥60 秒無幀率劣化、無記憶體增長趨勢。
- **NFR-05 相機事實約束**：所有「可視範圍」計算以透視 FOV60° @ z=-10 為準（z=0 平面 ≈20.53 @16:9），**不得假設正交相機**（承襲 validator 實證教訓）。
- **NFR-06 低耦合**：新系統與玩家／水管／計分邏輯零依賴；GameManager 接線語義保持（具體形式可隨架構調整，但死亡重開 Reset 行為不得回歸）。

## 邊界情況

| 情境 | 風險 | 對策 |
|------|------|------|
| 多層 Depth 相同 | 速度 tie-break 未定義 | 同 Depth 同速視為合法；Render Order 仍獨立生效（具體 tie-break 由 Tech Lead 裁定） |
| 多層 Render Order 相同 | 遮擋順序未定義（Unity 行為） | Tech Lead 裁定：Editor 提示或文件註明 |
| 鳥（MeshRenderer @ z=0）與背景層共存 | 新排序方案可能讓前景層蓋住鳥、或鳥意外蓋住全部背景層 | Task 1 必須裁定鳥的渲染策略（如納入同一 Sorting Layer 體系）；Task 5 與整體驗收含鳥可見性檢查 |
| BG_Layer 根物件上停用的裝飾 SpriteRenderer（sortingOrder 10/5/0） | 遷移後殘留，誤導後續除錯 | Task 5 遷移時一併清理或記錄保留理由 |
| 極端 aspect ratio | tileWidth 不足以覆蓋視野 | 承襲 M3 教訓：24 單位覆蓋保證以 aspect ≤16:9 為前提；多解析度適配列 P2 另案 |
| Template 被 Layer 引用中遭刪除 | Runtime 建立失敗 | Editor 防呆提示（Tech Lead 裁定形式） |
| Play mode 中修改配置 | 半套 hot-reload 需求 | **定義為不支援**：配置僅 Edit mode 生效，Play mode 中修改不保證結果 |
| 執行期（Play mode 中）動態增刪 Layer | 範圍蔓延 | 本期範圍限 Edit mode 配置階段；執行期增刪列 P2 另案（見「不在範圍」） |

## 驗收標準（整體，Validator 對照用）

- [ ] **多 Layer**：≥4 層（Sky/Mountain/Forest/Foreground）同時正常顯示
- [ ] **Z 重疊**：多層位於相同或重疊 z 座標時，遮擋順序仍由 Render Order 決定、穩定無閃爍
- [ ] **Render Order**：修改某層 Render Order 後遮擋關係正確反轉
- [ ] **分離鐵律對照測試**：(a) 固定 Depth、只改 Render Order → 僅遮擋變化、速度不變；(b) 固定 Render Order、只改 Depth → 僅速度變化、遮擋不變（直接驗證 FR-04 無耦合）
- [ ] **Parallax**：不同 Depth 的層速度不同（Depth 大者慢），肉眼可辨
- [ ] **無縫延伸**：連續捲動 ≥60 秒無空隙／跳動／接縫
- [ ] **Recycle**：長時間捲動下 tile 物件數量恆定；Profiler 確認捲動期間 0 次 Instantiate/Destroy
- [ ] **Template**：同一 Template 被 ≥2 個 Layer 引用時各自正常運作互不干擾
- [ ] **多子物件**：含多子物件的 Tile 完整顯示並隨 Layer 捲動
- [ ] **動態增刪 Layer**：Edit mode 下新增／刪除 Layer 後遊戲正常運作
- [ ] **Scene View 預覽**：改 Depth／Render Order／Tile 配置後 Scene View 立即反映
- [ ] **鳥可見性**：鳥在新排序方案下正常顯示於預期位置（策略依 Task 1 裁定）
- [ ] **不回歸**：死亡重開 Reset 正確、GameManager 接線運作、Console 無錯誤

---

## 任務單

> arch-review 標記語義：`yes`＝該票實作前需 Tech Lead 產出／更新架構文件（Phase 2）；`no`＝消費既有架構即可。

### Task 1: 背景系統架構設計文件

- **Priority**: P0
- **Assigned**: tech-lead-agent
- **arch-review**: yes（本票即 Phase 2 架構工作本身）
- **Goal**: 產出 Class Diagram（BGManager／BGLayer／Tile／TileTemplate／Editor 關係）、Data Flow（Editor 配置 → Layer Data → Runtime → Pool → Instances）、Rendering Flow（Depth→Parallax；Render Order→Sorting→Final Rendering）、Tile Lifecycle（Create→Spawn→Move→Out of Camera→Recycle→Reuse）；裁定 Depth／RenderOrder 資料模型、TileTemplate 資產形式、Editor 配置儲存方式、與既有 BackgroundLayer/GameManager 的整合策略（含透視相機下的排序方案、鳥 MeshRenderer 的共存策略、Editor 代碼隔離形式）。
- **Unity Objects**: 無（純文件產出：`specs/architecture/`＋`docs/adr/`）
- **Deliverables**: `specs/architecture/bg-layer-editor*.md` 更新＋`docs/adr/` 決策紀錄＋`specs/architecture/dependency-rules.md` 同步。
- **Acceptance Criteria**:
  - [ ] 四份分析（Class Diagram / Data Flow / Rendering Flow / Tile Lifecycle）齊備且責任分離明確
  - [ ] Depth／Render Order／Parallax 三者責任邊界有明文裁定
  - [ ] 鳥 MeshRenderer 共存策略有明文裁定
  - [ ] ADR 記錄關鍵決策（含相機投影與排序方案）

### Task 2: Runtime 核心——動態 Layer 管理＋Depth/RenderOrder 分離

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: yes（新增 runtime 共享核心）
- **依賴**: Task 1
- **Goal**: 依架構文件實作動態 Layer 管理（類名依 T1 裁定）與 Depth／Render Order 分離屬性，接線 Sorting Layer / Order in Layer。
- **Unity Objects**: `Assets/Scripts/BackgroundLayer.cs`（整合）、`Assets/Scripts/GameManager.cs`（註冊點資料驅動化）、新 runtime scripts（類名/位置依 T1）
- **實作約束（不變式）**:
  - Layer 數量執行期由資料驅動，程式碼中無寫死的 3 層假設
  - Depth 唯一決定該層捲動速度因子；Render Order 唯一決定該層 sprite 的排序映射
  - 同 z 或重疊 z 的多層，遮擋順序仍完全由 Render Order 決定
  - 死亡重開 Reset 行為保持
- **Acceptance Criteria**:
  - [ ] 新增第 4、5 層無需改程式碼
  - [ ] 改 Render Order 即時改變遮擋（含 z 重疊情境）
  - [ ] Console 無錯誤

### Task 3: Tile Pool 與 Recycle 機制

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: yes（pool lifecycle 設計）
- **依賴**: Task 1
- **Goal**: 實作 Tile Object Pool：Tile 離開相機可視範圍後回收、移到前方重用；可比照 `PipeSpawner.cs` 的 `UnityEngine.Pool.ObjectPool` 模式。
- **Unity Objects**: 新 Tile Pool scripts（類名/位置依 T1）；參照 `Assets/Scripts/PipeSpawner.cs`
- **實作約束（不變式）**:
  - 穩態捲動期間 0 次 Instantiate/Destroy（NFR-01）
  - 熱路徑零 GC 分配（NFR-02）
  - Tile 數量恆定且貼近可視範圍（NFR-03）
  - 無縫延伸沿用取模推導思想，避免浮點漂移
- **Acceptance Criteria**:
  - [ ] 連續捲動 ≥60 秒無空隙、tile 數量恆定
  - [ ] Profiler 確認捲動期間 0 Instantiate/Destroy、0 GC 分配

### Task 4: Tile Template 系統

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: yes（template 資料模型）
- **依賴**: Task 1
- **Goal**: 實作 Tile Template：定義 Tile 內容（可含多個子物件），Layer 指定 Template 後 Runtime 依模板建立／Recycle tile。
- **Unity Objects**: 新 TileTemplate scripts＋模板資產（形式依 T1）
- **實作約束（不變式）**:
  - Template 資產形式依 T1 裁定；Layer 只引用 Template，不直接持有子物件清單
  - 同一 Template 被 ≥2 個 Layer 引用時互不干擾
  - Template 內容變更後，使用中的 Layer 於下次配置生效（Play mode 中不要求 hot-reload）
- **Acceptance Criteria**:
  - [ ] 含多子物件的 Tile 完整顯示並隨 Layer 捲動
  - [ ] 同 Template 多 Layer 共用正常

### Task 5: 既有系統整合

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: yes（動共享核心：BackgroundLayer/GameManager）
- **依賴**: Task 2、Task 3、Task 4
- **Goal**: 將現有三層遷移至新系統；整合 GameManager 接線與死亡重開；確保全專案單一 parallax 實作。
- **Unity Objects**: `Assets/Scripts/BackgroundLayer.cs`、`Assets/Scripts/GameManager.cs`、`SampleScene`（BG_Layer_01/02/03）
- **注意事項**:
  - 遷移時清理 BG_Layer_01/02/03 根物件上停用的裝飾 SpriteRenderer（sortingOrder 10/5/0），或記錄保留理由
  - 驗收含鳥可見性檢查（渲染策略依 T1 裁定）
- **Acceptance Criteria**:
  - [ ] parallax-background-fix 驗收項全數不回歸
  - [ ] 無第二套 parallax 實作殘留
  - [ ] 鳥正常可見、未被背景層異常遮擋
  - [ ] Console 無錯誤

### Task 6: BG Layer Editor——Layer List UI

- **Priority**: P1
- **Assigned**: gameplay-developer
- **arch-review**: yes（editor↔runtime 資料橋接的新子系統邊界）
- **依賴**: Task 2
- **Goal**: EditorWindow 提供 Layer List：新增／刪除／排序／改名／編輯 Depth／編輯 Render Order／指定 Tile Template。
- **Unity Objects**: Editor scripts（隔離位置依 T1）、背景配置資產
- **Acceptance Criteria**:
  - [ ] 全部 FR-14 操作可用且持久化
  - [ ] Editor 代碼與 Runtime 核心隔離（FR-18）

### Task 7: BG Layer Editor——Tile 配置與 Template 內容編輯

- **Priority**: P1
- **Assigned**: gameplay-developer
- **arch-review**: no（擴充 T6 視窗、消費 T4 模型，無新跨腳本依賴）
- **依賴**: Task 4、Task 6
- **Goal**: 於 Editor 中配置 Tile Width／排列方式，並編輯 Template 子物件內容。
- **Unity Objects**: Task 6 的 EditorWindow 擴充
- **Acceptance Criteria**:
  - [ ] FR-15／FR-10 操作可用
  - [ ] Template 遭刪除時有防呆提示

### Task 8: Scene View 即時預覽

- **Priority**: P1
- **Assigned**: gameplay-developer
- **arch-review**: no（T6 視窗內的預覽功能）
- **依賴**: Task 6、Task 7
- **Goal**: 修改任何配置後 Scene View 立即反映（FR-16）。
- **Unity Objects**: Task 6/7 的 EditorWindow 擴充
- **Acceptance Criteria**:
  - [ ] 改 Depth／Render Order／Tile 配置後 Scene View 即時更新
  - [ ] 離開 Editor 後場景不留髒資料

### Task 9: 示範場景配置（≥4 層）

- **Priority**: P1
- **Assigned**: scene-developer
- **arch-review**: no（使用成品工具做配置）
- **依賴**: Task 5～Task 8
- **Goal**: 以新系統配置 SampleScene 示範場景：Sky/Mountain/Forest/Foreground 至少四層，Depth 與 Render Order 各異，其中至少一層使用含多子物件的 Template。
- **Unity Objects**: `SampleScene`、背景配置資產
- **注意事項**: 執行手段（走 Editor UI 或直接序列化配置）由 scene-developer 依工具能力決定；必要時由人類手動操作 Editor UI 驗收 UX。
- **Acceptance Criteria**:
  - [ ] ≥4 層同時顯示、速度分離肉眼可辨
  - [ ] 遮擋順序符合各層 Render Order
  - [ ] 場景已儲存

### 建議執行順序（Sisyphus 調度，串行）

```
T1 (tech-lead) → T2 → T3 → T4 → T5 (gameplay) → T6 → T7 → T8 (gameplay) → T9 (scene) → Validator
```

本期 asset-developer 無任務：美術維持 placeholder（正式素材接入列 P2 另案）。

---

## 明確不在範圍（P2，另案）

- Play mode 執行期動態增刪 Layer（本期僅 Edit mode 配置階段）
- Y 軸方向的 parallax／tiling（本期僅 X 軸）
- 正式美術素材接入（沿用 placeholder 起步）
- 多解析度／多 aspect ratio 適配
- Play mode 中修改配置的 hot-reload
