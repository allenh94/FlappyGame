# 規格：Atlas XML 切圖工具（atlas-xml-slicer）

> Status: **draft**（初稿由 Sisyphus 於 2026-08-24 代寫落盤——pm-agent session 三度中斷無法產出，沿 bg-layer-editor 先例代寫，待 pm-agent 複核修訂後正式接管）｜ Owner: pm-agent（待接管）｜ Date: 2026-08-24
> （沿革：人類 2026-08-24 提出需求「已打包好的 atlas texture，png+xml 一組，要讓 Unity 依 xml 正確拆分，寫一個 editor 輸入一張匯入的 sprite 自動抓對應 xml 解析並完成 slice」。上下文盤點由 Sisyphus 完成，規格初稿由 Sisyphus 代寫。內容所有權待 pm-agent 接管後移交。）
> （裁決：2026-08-24 人類裁定 Q1——輸入方式為**拖曳單張 png**、批次不做；Q2／Q3 經白話說明後採規格預設，詳見文末「裁決紀錄」。）

## 功能：Atlas XML Slicer／外部打包圖集自動切圖工具

- **目標**：提供一個 Unity Editor 視窗工具，輸入一張已匯入的圖集 png，工具依檔名自動尋找同資料夾的同名 xml、解析 TexturePacker／Starling 格式的子圖資訊、換算座標後完成 TextureImporter slice，使該貼圖在 Unity 內成為多個正確命名的 Sprite。
- **性質**：新功能。**純 Editor 端工具，零 runtime 改動**——不修改任何場景、prefab、runtime 腳本。

## 需求分析

### 現狀（explore 盤點＋讀檔核實，2026-08-24）

| 面向 | 現況 |
|------|------|
| 圖集來源 | 人類在外部以 TexturePacker／Starling 慣例打包：`sprite_name.png`＋`sprite_name.xml` 成對存在 |
| 實際資產路徑 | **`Assets/Art/Atlases/Background/`**（人類口述「ART/atlas/」與實際路徑不符，本規格以實際路徑為準）；現有 `spritesheet_default.png`＋`spritesheet_default.xml` 一組 |
| 匯入現狀 | png 的 `.meta` 已設 `spriteMode: 2`（Multiple）但 `spriteSheet.sprites: []`——**零切分**，正是本工具要填補的缺口 |
| XML 格式 | `<TextureAtlas imagePath="spritesheet_default.png"><SubTexture name="bush1.png" x="419" y="950" width="120" height="60"/>…`，共 69 張子圖（2026-08-24 實測核實，貼圖 1067×1095）；座標原點在**左上角**（y 向下增長） |
| 既有 SpriteAtlas | 上一層 `Assets/Art/Atlases/` 已有 `Background.spriteatlas`（Unity SpriteAtlas 資產；初稿誤植「同目錄」經 validator round-1 m2 勘正）——slice 完成的 sub-sprite 會被其自動收包，需驗證互動不衝突（見驗收 AC-6） |
| Editor 慣例 | 專案唯一自製 EditorWindow：`Assets\Scripts\Editor\Background\BGLayerEditorWindow.cs`——global namespace、IMGUI（OnGUI＋EditorGUILayout）、`[MenuItem("Window/<Category>/<Name>")]`、繁中 doc comment 引 FR-xx、`SetDirty＋SaveAssets` 模式 |
| 測試基建 | **無**：全 Assets 無 asmdef、無 Tests 資料夾；test-framework 1.6.0 已裝未用；歷史驗證採截圖存證（Assets/Screenshots/） |
| Runtime 消費 | 背景系統經 prefab template（BGConfig → LayerDef.template → 子 SpriteRenderer）間接使用 sprite，無 Resources.Load、無 SO 直接 Sprite 欄位 |

### 核心技術風險（必須在規格層面定調）

1. **Y 軸翻轉**：XML 座標為左上原點，Unity Sprite rect 為左下原點。換算公式：`y_unity = textureHeight − y_xml − height_xml`。x 不變。此公式錯一個符號＝全部 sprite 垂直錯位，驗收必須含視覺抽樣比對。
2. **匯入縮放風險**：XML 座標對應**原始 png 像素尺寸**；若 Unity 匯入時縮放（maxTextureSize 低於原圖、或非 1 scale），rect 全錯。工具必須防呆：比對 XML 極值（max(x+w)、max(y+h)）與匯入後 texture 尺寸，不一致即警告中止。
3. **命名後綴**：SubTexture `name` 屬性帶 `.png` 副檔名（如 `bush1.png`）。sprite 名稱須去除副檔名（→`bush1`），避免 Unity 內出現假副檔名資源名。
4. **slice API 選擇**：候選 (a) `TextureImporter.spritesheet = SpriteMetaData[]`（簡單直接，Unity 6000.3 仍可用）；(b) `ISpriteEditorDataProvider`（2D package 官方路徑，較冗長但與 SpriteAtlas/新版管線相容性佳）。由實作者於 T1 裁定，二擇一即可，不影響架構。

### 核心功能要求（人類原始需求提煉）

1. **輸入一張已匯入的 png**（Editor 視窗欄位選取或拖入）。
2. **依檔名自動配對 xml**：同資料夾、同名、`.xml` 副檔名。
3. **解析 xml 並完成 slice**：結果可在 Unity Sprite Editor／Project 視窗展開看到全部正確尺寸、正確位置的子 sprite。
4. **可重複執行**：對同一張圖重跑結果冪等（完全覆蓋舊 slices，不重複不殘留）。

### 人類核心要求 ↔ 規格條目對照（追溯性）

| 人類核心要求 | 對應條目 |
|--------------|----------|
| png+xml 一組、依檔名配對 | FR-02；邊界情況「xml 缺漏」 |
| 讓 Unity sprite 正確依 xml 拆分 | FR-03～FR-05；AC-1／AC-2 |
| 寫一個 editor、輸入一張匯入的 sprite | FR-01；T1 |
| 自動解析並完成 slice | FR-03／FR-06 |

## 涉及物件

| 類型 | 路徑／名稱 | 處置 |
|------|-----------|------|
| Editor Script（新） | `Assets/Scripts/Editor/AtlasTools/AtlasXmlSlicerWindow.cs`（類名可微調，位置遵循 `Assets/Scripts/Editor/<Feature>/` 慣例） | **新增** |
| 匯入設定 | `Assets/Art/Atlases/Background/spritesheet_default.png`（`.meta` 的 slice 資料） | **修改**（僅經工具寫入 slice；spriteMode 維持 Multiple） |
| 資料檔 | `Assets/Art/Atlases/Background/spritesheet_default.xml` | 只讀（解析來源，不修改） |
| SpriteAtlas | `Assets/Art/Atlases/Background.spriteatlas`（位於 Atlases 層、非 Background 子資料夾；路徑經 validator round-1 m2 勘正） | **不動**（僅驗證 slice 後互動正常） |
| Runtime 腳本／場景／Prefab | 全部 | **零改動** |

---

## 功能需求（FR）

### A. Editor 工具介面

- **FR-01 視窗與輸入**：新增 EditorWindow（選單路徑 `Window/Art/Atlas XML Slicer`），提供單一 Texture2D 欄位；**主要輸入方式為從 Project 視窗拖曳 png 到該欄位**（ObjectField 天然支援拖放）。一次只處理一張（人類裁決 2026-08-24，批次列 P2 不做）。
- **FR-02 xml 自動配對**：點擊執行時，依所選 png 的完整路徑將副檔名替換為 `.xml`，於**同資料夾**尋找配對檔；找不到時 Console 回報明確錯誤（含查找路徑），且不更動該貼圖任何匯入設定。

### B. 解析與切圖

- **FR-03 XML 解析**：解析 TextureAtlas 根元素下的所有 SubTexture 子節點（屬性：name/x/y/width/height）；格式不符或缺必要屬性時逐項回報錯誤並中止。
- **FR-04 座標換算**：套用 `y_unity = textureHeight − y_xml − height_xml` 換算為 Unity rect；x/y/w/h 直接沿用。
- **FR-05 命名規則**：sprite 名稱＝SubTexture name 去除 `.png` 副檔名（如 `bush1.png`→`bush1`）。
- **FR-06 套用 slice**：將結果寫入 TextureImporter（spriteMode 設為 Multiple、slice 清單整體覆蓋），隨後 `ImportAsset`＋`SaveAssets`；完成後 Console 回報成功切分數量。
- **FR-07 冪等重跑**：對同一貼圖重複執行時以最新解析結果**整體覆蓋**舊 slice 清單，不產生重複或殘留。

### C. 防呆與尺寸守門

- **FR-08 匯入尺寸守門**：執行前比對 XML 極值（max(x+width)、max(y+height)）與匯入後 texture 寬高；若 XML 極值超出貼圖尺寸（暗示匯入被縮放或 xml 配錯檔），回報**警告**並中止（初稿「回報錯誤」措辭與邊界情況表不一致，經 validator round-1 m1 勘正統一為警告；實作即取警告）。
- **FR-09 越界守門**：任一 SubTexture rect 落於貼圖邊界外時回報該筆資料錯誤並**中止整批**（預設行為，見開放問題 Q2），不留半套結果。

## 邊界情況

| 情境 | 行為 |
|------|------|
| 所選 png 無同名 xml | Console 錯誤（含查找路徑），不改動任何設定（FR-02） |
| xml 存在但所指 png 不在 | 本工具以使用者所選 png 為準；xml 內 `imagePath` 屬性僅供交叉檢查，不一致時警告但不中止 |
| xml 損壞／非 TextureAtlas 格式／缺屬性 | 逐項回報錯誤、中止，不動匯入設定（FR-03） |
| SubTexture rect 越界 | 回報錯誤、中止整批（FR-09；跳過模式見 Q2） |
| 已切過的圖重跑 | 整體覆蓋，冪等（FR-07） |
| 匯入尺寸與 XML 極值不符 | 警告中止（FR-08） |
| 所輸入的不是 Texture2D／是子 sprite 而非主貼圖 | 取其主 Texture／提示應選主貼圖資產 |
| Play mode 中操作 | 定義為不支援：本工具限 Edit mode 使用 |

## 驗收標準（整體，Validator 對照用）

- [ ] **AC-1 數量與尺寸**：對 `spritesheet_default.png` 執行 → 產生 69 個 sprite（＝XML SubTexture 總數；初稿誤植 70 已勘正），逐一比對每個 sprite rect 的尺寸與位置等於換算後的 XML 對應值
- [ ] **AC-2 座標正確（Y 翻轉驗證）**：視覺抽樣 ≥5 張（涵蓋圖集四角與中央，如 `bush1`、`cloud2`、`tower`、`treeSnow`、`house1`），內容與外部打包軟體預覽一致、無垂直偏移
- [ ] **AC-3 命名**：sprite 名稱無 `.png` 後綴、與 XML 對應項一致
- [ ] **AC-4 冪等**：重跑一次 → 數量與清單不變、無重複 sprite、Console 無錯誤
- [ ] **AC-5 錯誤路徑**：選一張無 xml 的 png → 明確錯誤訊息且該貼圖匯入設定未被改動
- [ ] **AC-6 SpriteAtlas 共存**：slice 完成後 `Background.spriteatlas` 可正常打包（無錯誤、Pack 預覽正常），既有背景系統不受影響
- [ ] **AC-7 零侵入**：全程未修改任何 runtime 腳本、場景、prefab；Console 無殘留錯誤

## 任務單

> arch-review 標記語義：`yes`＝該票實作前需 Tech Lead 產出／更新架構文件（Phase 2）；`no`＝消費既有架構即可。
> 本期全部 arch-review: **no**——單一新 Editor 腳本、不觸共享核心、不加跨腳本依賴、runtime 零改動；slice API 二選一屬實作細節非架構變更；SpriteAtlas 互動僅驗證不修改。

### Task 1: 實作 Atlas XML Slicer Editor 工具

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: no（隔離的新 Editor 腳本，零跨腳本依賴）
- **Goal**: 依 FR-01～FR-09 實作 `Assets/Scripts/Editor/AtlasTools/AtlasXmlSlicerWindow.cs`。
- **Unity Objects**: 新 Editor script 一枚（唯一新增程式檔）
- **實作約束（不變式）**:
  - 遵循 house style（參照 `BGLayerEditorWindow.cs`）：global namespace、IMGUI、`[MenuItem("Window/Art/Atlas XML Slicer")]`、繁中 doc comment 引 FR-xx、`SetDirty`／`SaveAssets`／`ImportAsset` 收尾
  - XML 解析＋座標換算抽出為可獨立呼叫的靜態方法（為 T3 測試鋪路，不強制 asmdef）
  - slice API 於 `TextureImporter.spritesheet` 與 `ISpriteEditorDataProvider` 二擇一，於 doc comment 註明裁定理由
  - **不得順手搬家或重排任何既有腳本**（承襲教訓：Scripts 平鋪現實勿動）
- **Deliverables**: 工具腳本＋Console 操作回饋
- **Acceptance Criteria**:
  - [ ] FR-01～FR-09 全數實作
  - [ ] 對 `spritesheet_default.png` 執行成功（AC-1 前置）

### Task 2: 實測驗收與存證

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: no（使用 T1 成品執行驗證流程）
- **依賴**: Task 1
- **Goal**: 以 `spritesheet_default` 完整走一遍 AC-1～AC-7，截圖存證至 `Assets/Screenshots/`（承襲專案截圖驗證慣例），回報逐項結果。
- **Unity Objects**: `spritesheet_default.png` 匯入設定（經工具）、Screenshots 存證
- **Acceptance Criteria**:
  - [ ] AC-1～AC-7 逐項通過並附截圖／Console 紀錄
  - [ ] 發現偏差時回報 Sisyphus，不得自行改規格繞過

### Task 3: EditMode 測試基建首例（可選）

- **Priority**: P2（可選，人類裁決是否本期做）
- **Assigned**: gameplay-developer
- **arch-review**: no（測試裝配隔離，不觸生產碼）
- **依賴**: Task 1
- **Goal**: 建立專案第一個 EditMode 測試裝配（asmdef＋Tests 資料資料夾），針對 T1 抽出的靜態方法（XML 解析、Y 翻轉換算）寫單元測試。**注意：這是專案首個 asmdef，屬新模式引入**，若人類不想在本期引入測試基建則整票取消。
- **Acceptance Criteria**:
  - [ ] Test Runner 中 EditMode 測試全綠
  - [ ] 生產碼零改動

### 建議執行順序（Sisyphus 調度，串行）

```
T1 (gameplay) → T2 (gameplay) → Validator round-1；T3 視人類裁決插入
```

本期 scene-developer／asset-developer 無任務（美術資產已就位、場景零改動）。

---

## 裁決紀錄（2026-08-24 人類裁決，取代原「開放問題」）

| 問題 | 裁決 |
|------|------|
| Q1 輸入方式 | **拖曳指定、單張處理**——從 Project 視窗拖 png 到工具欄位即可；批次模式確認本期不做（P2 另案） |
| Q2 越界策略 | 人類未表態（原敘述過於技術性，已補白話說明）；**採規格預設：任一 SubTexture rect 越界即中止整批並回報錯誤**——越界通常代表 xml 配錯檔或匯入被縮放，硬切會產出壞資源；日後若要改「跳過壞筆、其餘照切」屬單一條件調整 |
| Q3 sprite 命名 | 人類澄清 `spritesheet_default` 僅為範例檔名；實際議題是 SubTexture 的 name 屬性帶 `.png` 後綴（如 `bush1.png`）→ **採規格預設：切出的 sprite 名稱去除後綴（`bush1`）**，避免 Unity 內出現假副檔名資源名 |

## 明確不在範圍（P2，另案）

- 批次處理整個資料夾（見 Q1）
- 切好的 sprite 如何接進背景系統 prefab／BGConfig（runtime 整合另案；現有 template-prefab 消費方式不受影響）
- 支援非 TexturePacker／Starling 格式（如 Cocos plist、JSON hash/array）
- xml 變更後的自動重新切圖（AssetPostprocessor 監聽）
