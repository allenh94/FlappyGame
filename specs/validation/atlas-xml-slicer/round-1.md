# Validation Report: atlas-xml-slicer round 1
- **Date**: 2026-08-24
- **Task Sheet**: specs/requirements/atlas-xml-slicer.md（含裁決紀錄 Q1–Q3）
- **Architecture**: specs/architecture/dependency-rules.md（B1–B8，均屬背景系統範疇）——本期 arch-review: no，無新架構文件
- **審查標的**: Assets\Scripts\Editor\AtlasTools\AtlasXmlSlicerWindow.cs（332 行，Task 1 唯一交付物）
- **Verdict**: **PASS**（blocker = 0｜major = 1｜minor = 2）

---

## 一、驗證方法與證據來源

| 證據 | 來源 | 性質 |
|------|------|------|
| 動態全量審計（69/69 rect 比對、冪等雙跑、FR-02 錯誤路徑、console 軌跡） | Sisyphus 2026-08-24 以工具自身 ExecuteSlice 管線行使（reflection 驅動） | 採信為已驗證輸入，未重跑 |
| png.meta 全文（1704 行） | 本輪直接讀檔 | 獨立核實 |
| spritesheet_default.xml 全文（69 筆） | 本輪直接讀檔 | 獨立核實 |
| Background.spriteatlas 全文 | 本輪直接讀檔 | 獨立核實 |
| 引用掃描（圖集 GUID＋新腳本類名 × *.unity/*.prefab/*.asset/*.cs） | 本輪 grep | 獨立核實 |
| Console 現況（error/warning 50 條窗口＝0；all 10 條逐一歸因） | unityMCP_read_console | 獨立執行 |
| EditMode 測試套件 | unityMCP_run_tests（job 5b88e012…） | 獨立執行 |
| Assets 檔案異動盤點（mtime 排序 glob） | 本輪 glob | 方法限制：無 git，僅能語義級核對 |

## 二、FR-01～FR-09 逐條對照（靜態審查）

| FR | 判定 | 依據（行號指 AtlasXmlSlicerWindow.cs） |
|----|------|------|
| FR-01 視窗與輸入 | ✅ | `[MenuItem("Window/Art/Atlas XML Slicer")]` L43 與規格逐字一致；單一 Texture2D ObjectField L51（typeof(Texture2D) 天然支援 Project 拖放）；HelpBox 引導拖曳、一次一張（Q1 裁決） |
| FR-02 xml 自動配對 | ✅ | `Path.ChangeExtension(texturePath, ".xml")`＋`File.Exists` L85–91；錯誤訊息含查找路徑；`return` 發生在任何 importer 存取之前——「不改動匯入設定」結構性保證。WhiteSquare.png 實測吻合 |
| FR-03 XML 解析 | ✅ | TryParseSubTextures L211–277：根元素必為 TextureAtlas、逐節點檢查 name/x/y/width/height、刻意非短路 `&` 一次收集全部錯誤（L264–267）、XmlException 捕捉、零 SubTexture 防呆 L273–274；任何錯誤 → ExecuteSlice 逐項 LogError 後中止 L100–105 |
| FR-04 座標換算 | ✅ | ConvertRect L301–305：`y_unity = textureHeight − y_xml − height_xml`、x 沿用。本輪獨立手算抽樣 8 筆 meta vs xml 全中（見第三節） |
| FR-05 命名規則 | ✅ | StripSpriteSuffix L308–317 去 `.png`（大小寫不敏感、無後綴原樣返回、恰等於 `.png` 的退化名不誤削成空字串） |
| FR-06 套用 slice | ✅ | `spriteImportMode = Multiple` L150、SetSpriteRects 全量覆蓋 L163、Apply＋SaveAndReimport＋SaveAssets L164–167、成功回報切分數量 L169–170。任務單要求的 ImportAsset 收尾語義由 SaveAndReimport 涵蓋（API 裁定理由已註記於類別註解 L15–20） |
| FR-07 冪等重跑 | ✅（字面） | SetSpriteRects 整體覆蓋即「不重複不殘留」；雙跑實測 69→69、名稱穩定、console 乾淨。**持續身分輪替問題另立 M1（不違反本條字面）** |
| FR-08 匯入尺寸守門 | ✅ | ComputeExtents max(x+w)/max(y+h) L320–331 vs 匯入後貼圖寬高 L114；超出 → LogWarning 中止、不動設定。實際資料極值 1067×1095 恰等於貼圖尺寸（towerSmallAlt 右緣、bush2 下緣頂格），守門通過。措辭不一致見 m1 |
| FR-09 越界守門 | ✅ | 六條件檢查（x≥0、y≥0、w>0、h>0、x+w≤W、y+h≤H）L126–129；任一失敗 → LogError 中止整批、不留半套（Q2 裁決預設）。與 FR-08 互補：FR-08 抓極值溢出、FR-09 另抓負座標與非正尺寸 |

### 邊界情況表逐行對照（8/8）

| 情境 | 實作 | 判定 |
|------|------|------|
| png 無同名 xml | FR-02 路徑：錯誤含查找路徑、零改動（WhiteSquare 實測） | ✅ |
| xml imagePath 交叉檢查 | CrossCheckImagePath L193–202：不一致僅警告不中止；屬性缺漏則跳過檢查 | ✅ |
| xml 損壞／非 TextureAtlas／缺屬性 | FR-03 路徑：逐項錯誤、中止、不動匯入設定 | ✅ |
| SubTexture rect 越界 | FR-09：錯誤、中止整批 | ✅ |
| 已切過的圖重跑 | 整體覆蓋冪等（持續身分議題見 M1） | ✅ |
| 匯入尺寸與 XML 極值不符 | FR-08：警告中止 | ✅ |
| 非 Texture2D／選到子 sprite | ObjectField `typeof(Texture2D)` 結構性拒收 Sprite 子資產；importer 型別檢查 L143–148 兜底（RenderTexture 等無 TextureImporter 者報錯中止）。表格允許「取主 Texture／提示應選主貼圖」二擇一，UI 排除屬第三種合法達成 | ✅ |
| Play mode 操作 | `Application.isPlaying` 攔截按鈕 L60–64 | ✅ |

### 任務單實作約束對照

- global namespace ✅／IMGUI（OnGUI＋EditorGUILayout）✅／MenuItem 路徑逐字一致 ✅／繁中 doc comment 引 FR-xx ✅
- XML 解析＋座標換算抽出 public static（TryParseSubTextures／ConvertRect／StripSpriteSuffix／ComputeExtents）✅——T3 測試鋪路就緒
- slice API 二擇一並註明裁定理由 ✅：ISpriteEditorDataProvider；legacy `TextureImporter.spritesheet` 於 Unity 6000.3.12f1 已移除（CS0618）、`.spriteMode` 正名 `spriteImportMode`——規格「legacy 在 6000.3 仍可用」前提失效，開發者裁決與類別註解、memory 三方一致
- 未順手搬家重排既有腳本 ✅（語義級：全專案 14 支 .cs 清單無增減，唯一新檔即本工具）

### 架構規則檢查

- dependency-rules B1–B8 均規範背景系統；本工具非背景系統成員且零交叉引用（using 僅 System.*／System.IO／System.Xml／UnityEditor／UnityEditor.U2D.Sprites／UnityEngine，無任何遊戲邏輯模組依賴）→ **無違規**
- 目錄 `Assets/Scripts/Editor/AtlasTools/` 符合規格涉及物件表自訂的 `Assets/Scripts/Editor/<Feature>/` 慣例
- 無 asmdef 引入（B6 演進觸發條件未觸發；Task 3 為 P2 可選未施作，符合預期）

## 三、AC 逐條判定（明示 PASS 清單，無静默跳過）

| # | AC | 判定 | 依據 |
|---|----|------|------|
| 1 | 數量與尺寸：69 個 sprite、rect 逐一等於換算值 | ✅ **PASS** | Sisyphus 全量審計：69/69 與 ConvertRect 公式輸出精確一致、0 多 0 少。本輪獨立手算抽樣 8 筆 meta vs xml 全中：bush1(1095−950−60=85)、bush2(=0)、cloud2(=939)、tower(=362)、treeSnow(=363)、house1(=145)、towerSmallAlt(=1005)、treeSmall_green1(=1)；meta `nameFileIdTable` 恰 69 鍵 |
| 2 | 座標正確（Y 翻轉視覺抽樣 ≥5 張含四角與中央） | ⚠️ 機制成立／視覺終審留人類 | 截圖存證在 `Assets/Screenshots/atlas_xml_slicer_ac2_sampling.png`（bush1/cloud2/tower/treeSnow/house1 五張抽樣，涵蓋四角與中央）。Agent 無法閱圖；數學面已由 AC-1 全量覆蓋（同一公式、同一座標系）。非缺陷，人類複核項 |
| 3 | 命名：無 .png 後綴、與 XML 一致 | ✅ **PASS** | meta 69 名稱全部無後綴；Sisyphus 實測零殘留零重複；StripSpriteSuffix 邏輯審查吻合（Q3 裁決） |
| 4 | 冪等：重跑數量清單不變、無重複、console 無錯誤 | ✅ **PASS（字面）** | 雙跑 69→69、名稱穩定、console 乾淨。持續身分輪替為 FR-07 字面未涵蓋的潛伏缺陷，另立 M1 追蹤 |
| 5 | 錯誤路徑：無 xml 的 png → 明確錯誤、設定未動 | ✅ **PASS** | WhiteSquare.png 實測：FR-02 錯誤含查找路徑；程式序上 return 先於任何 importer 存取，「設定未動」有結構保證 |
| 6 | SpriteAtlas 共存：可正常打包、背景系統不受影響 | ✅ **PASS（結構面）** | `Background.spriteatlas` packable＝`{fileID: 2800000, guid: ca99acab…}`——**主貼圖級引用**，子 sprite 身分輪替不影響打包關係；slice 後兩次 SaveAndReimport 連帶的自動 repack 零錯誤（console 軌跡僅 2 成功＋1 FR-02）；背景系統消費鏈（HillBlock.png→template prefab）與本圖集零交集（GUID 全專案零引用）。Pack 預覽視覺確認留人類（Validator 唯讀方法限制） |
| 7 | 零侵入：未動 runtime/場景/prefab、console 無殘留 | ✅ **PASS（語義級）** | 新檔僅 `AtlasTools/`（腳本＋資料夾 meta）＋截圖一組；*.unity/*.prefab/*.asset 對新腳本類名與圖集 GUID 雙零引用；runtime .cs 清單無增減；console 現況 0 錯誤 0 警告。方法限制：無 git 無法 diff，mtime 排序僅能佐證（最上層異動全是本功能合法新檔） |

**AC 總計：5 PASS ／ 1 PASS（字面）＋M1 附註 ／ 1 ⚠️ 人類複核項（#2）／ 0 FAIL**

## 四、Issues

### M1: 子 sprite 持久身分每輪重切即輪替（spriteID 恒配新 GUID）
- type: **code**｜severity: **major**
- 依據: FR-07 冪等的工程意涵（重跑結果應持續可用）；AC-4 字面未被違反故不列 blocker
- 事實:
  - 代碼面：BuildSpriteRects L182 對每個 entry 每次執行配發 `GUID.Generate()`——不讀取既有 slice 狀態、無同名沿用邏輯
  - 資產面：png.meta 實證身分鏈為 `spriteID → internalID → nameFileIdTable(name→internalID)`，外部序列化引用以 `{guid: 貼圖, fileID: <internalID>}` 定位子 sprite（如 bush1＝131024129486150166）。重切時新 GUID ⇒ 新 internalID ⇒ nameFileIdTable 重寫 ⇒ 舊 fileID 引用懸空
  - 緩解現狀：全專案 *.unity/*.prefab/*.asset 對該圖集 GUID **零引用**——今日受害者為零；SpriteAtlas packable 為主貼圖級引用亦不受影響。故本輪不擋驗收
  - 風險窗口：P2 整合（sprite 接入 prefab／BGConfig）之後，任何一次重切——本工具的核心重複使用情境——將靜默打斷全部引用，且 Console 無任何警示
  - 證據張力：Sisyphus 觀察「instance IDs 不變」與上述代碼事實存在矛盾；無 git 無法留存跨跑 meta 快照，該觀察無法在持久化層面復核（session 內 InstanceID 行為不等於持久 fileID 穩定性）
- 建議方向（供路由參考）：覆蓋前先經 dataProvider.GetSpriteRects() 讀取現有清單，同名 entry 沿用舊 spriteID、僅新增項配新 GUID（TexturePacker 官方 Unity importer 同款做法）；修復時以 meta 前後快照留下可持久化證據

### m1: FR-08 主體「回報錯誤並中止」vs 邊界情況表「警告中止（FR-08）」措辭不一
- type: **requirement**｜severity: **minor**
- 事實: 規格 FR-08 條文寫「回報錯誤」，同文件邊界情況表同行寫「警告中止」。實作採 LogWarning（跟隨更具體的表格列），FR-09 則用 LogError——兩種守門的 Console 層級區分反而清晰
- 影響: 行為核心（中止＋不動匯入設定）兩種讀法下完全一致，僅日誌層級歸屬待統一
- 建議方向: PM 勘正 FR-08 條文為「警告」或「錯錯誤」擇一

### m2: 規格宣稱 Background.spriteatlas 位於「同目錄」，實際在上一層
- type: **requirement**｜severity: **minor**
- 事實: 規格現狀表寫「同目錄已有 Background.spriteatlas」（意指 Assets/Art/Atlases/Background/）；實際位置 `Assets/Art/Atlases/Background.spriteatlas`（png 的上一層）
- 影響: 零功能影響——packable 是明確物件引用而非資料夾式收包；但路徑記載錯誤會誤導後續 agent 盤點（本輪即先撰錯路徑才定位到真檔）
- 建議方向: PM 勘正現狀表路徑

## 五、觀察（不計 severity，含人類複核交接）

1. **公開 UX 路徑未經人工實點**（已知觀察 #3 的評估）：拖放＋Slice 按鈕路徑僅由 reflection 進入 ExecuteSlice 行使過。風險評估為極低——OnGUI→ExecuteSlice 接線僅 3 行靜態可證（L69–70）、MenuItem 為宣告式註冊（編譯通過即生效）、完整業務管線已被端到端行使。**列人類複核項**：開 Window/Art/Atlas XML Slicer、拖入 spritesheet_default.png、點 Slice，確認成功訊息即可閉環
2. **AC-2 截圖與 AC-6 Pack 預覽的視覺終審留人類**：agent 無閱圖能力，數學面與結構面已分別由 AC-1 全量比對與 packable 引用分析覆蓋
3. **xml.meta／Background.spriteatlas.meta 及若干資料夾 meta 的 mtime 有更新**：內容語義完整性已核實（xml 與規格引述值逐字一致、spriteatlas packables 完好指向主貼圖），判斷為 Unity 重匯入連帶改寫，非人為修改。無 git 環境下此類接觸無法定案，如實記錄
4. **Task 3（EditMode 測試基建）未施作**：P2 可選、待人類裁決，非本輪驗收範圍；靜態方法已按要求抽出，鋪路完成
5. **Edit 測試套件結果**：專案零 asmdef 零測試（預期）→「無失敗」成立；測試初始化觸發的強制編譯＋domain reload 成功、零 CS 錯誤，構成本輪第二條編譯健康訊號

## 六、Console 檢查

- `unityMCP_read_console`（error＋warning，50 條窗口）：**0 條**
- all 類型 10 條逐一歸因：全數為 MCP-FOR-UNITY 外掛自身外掛自身訊息（啟動、HTTP server、TestRunner NoThrottle 套用/還原）與 test runner 例行輸出（TestResults.xml 落盤）——零遊戲程式碼錯誤（歸因方法沿襲 validator memory 慣例）
- Sisyphus 提供的操作軌跡（2×成功＋1×FR-02 錯誤、無編譯錯誤）與現況一致

## Statistics

- **blocker: 0**
- **major: 1**（M1 子 sprite 持久身分輪替——累積記錄，不擋驗收）
- **minor: 2**（m1 FR-08 措辭不一、m2 spriteatlas 路徑勘正——均 type: requirement）

## 結論

**PASS**——FR-01～FR-09 全數實作且與裁決紀錄 Q1–Q3 一致；邊界情況表 8/8 覆蓋；AC-1/3/4/5/6/7 通過（AC-4 字面通過、附 M1 潛伏缺陷追蹤），AC-2 數學面成立、視覺終審留人類。依迴圈終止政策，零 blocker 不觸發修復輪；M1/m1/m2 累積記錄。

**交辦人類的兩個複核項**（非缺陷，閉環用）：
1. 實點一次公開 UX 路徑（拖 png → Slice → 成功訊息）
2. 目視 `Assets/Screenshots/atlas_xml_slicer_ac2_sampling.png` 五張抽樣無垂直偏移

**給下一輪的提醒**：若人類裁決進行 P2 runtime 整合（sprite 接入 prefab），M1 必須在整合前修復——否則第一次重切就會靜默打斷全部引用。
