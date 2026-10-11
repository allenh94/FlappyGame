# Validation Report: bg-layer-editor round 3
- **Date**: 2026-08-24
- **Task Sheet**: specs/requirements/bg-layer-editor.md（本輪增量：正式美術素材接入——4 個 tile 模板 prefab）
- **Verdict**: **PASS**（零 blocker；2 major 累積記錄、1 minor）

---

## 驗收範圍與方法

本輪驗收對象為 round-2 PASS 後的美術增量：`TileTemplate_Sky/Mountain/Forest/Foreground.prefab` 四檔新增、BGConfig 四層模板接線、spritesheet_default.png filterMode 改 Point、SampleScene 零修改。

方法：純靜態 YAML/meta 讀取（meta 即真相）＋ fileID/guid 全量交叉解析＋ Console 實查；動態行為採信 Sisyphus 已回填的 Play mode 實測證據（見文末回填狀態節）。Validator 模型無法讀取圖片（look_at 實測確認），截圖目視複核列為人類殘餘項。

## AC 對照表

| # | AC | 結果 | 證據 |
|---|----|------|------|
| 1 | 多層 ≥4 層配置正確、模板引用非 null | ✅ | BGConfig.asset 恰 4 層；凍結值逐項無變動（Sky d8/rO10/tw24、Mountain d4/rO20/tw24、Forest d2/rO30/tw24、Foreground d1/rO40/tw24）；4 個 template guid 與各 prefab .meta 完全一致 |
| 2 | 模板含多個子物件、模板機制未破壞 | ✅ | 子物件數 Sky 8／Mountain 8／Forest 16／Foreground 16；root 皆純 Transform、子物件皆 Transform+SpriteRenderer、全部 z=0；runtime 鏈路完好：BGLayer.createFunc `Instantiate(def.template)` → `Tile.ApplyTemplate(tileWidth, renderOrder)` → 合併 bounds 根縮放＋band+authored 排序 |
| 3 | 分離鐵律（B3）未破壞 | ✅ | depth/renderOrder/tileWidth 數值原封不動；prefab 內零 script（grep `MonoBehaviour\|!u!114` 於 Tiles/*.prefab 零匹配）；代碼端 depth 只進速度（BGLayer L30）、renderOrder 只進 sortingOrder（Tile L36–41、Editor L211） |
| 4 | 鳥可見性風險 | ✅ 設計容許內 | 背景最高帶＝Foreground 40＋authored 最大 2＝**42** ≪ 鳥 MeshRenderer order **1000**（D4/B4 裁定：遮擋僅靠 sortingOrder、全層 z=0）；Sisyphus 實測帶位 Sky 10~12／Mountain 20~23／Forest 30~32／Foreground 40~42 與 YAML authored 值逐項吻合 |
| 5a | 不回歸：HillCluster 模板仍在且未變 | ✅ | root fileID 4370133681625299411、Transform 7390681888296572882、三子 Hill_Back(0.5,-4.1)/Hill_Mid(-6,-3.5)/Hill_Front(3.5,-3.9)、order 1/2/3、sprite guid 2ef5473b…＝HillBlock.png——與 round-2 報告記載逐項一致 |
| 5b | 不回歸：BGManager/BGConfig.cs/Tile.cs 未被修改 | ✅ | 三檔語義與已驗收狀態逐行一致（BGManager null guard/z=0/資料驅動迴圈；BGConfig LayerDef 欄位與 B2/B3 註解；Tile 正規化＋帶位公式） |
| 5c | 不回歸：BGLayer 未被修改 | ⚠️ **前提失真** | 現行檔含 round-2 報告白紙黑字記載「未修」之後新增的 m1/m3 修復代碼——見 Major F1。靜態審查結論：修復健全、鐵律無違（詳見附錄 A） |
| 5d | Console 無錯誤 | ✅ | unityMCP_read_console 獨立實查：0 error / 0 warning |
| 6 | 圖集 filterMode Point | ✅ | spritesheet_default.png.meta `filterMode: 0`（Point）；guid ca99acab… 與全部 48 個子物件 sprite 引用一致；nameFileIdTable 69 筆，48 個子物件 fileID **全數解析成功**（cloud1~8 各不重複、pyramid/pyramidMayan/tower/towerAlt/towerSmallAlt、treePine/tree/treeLong/treeOrange/treeDead、treeSmall_green*/bush*/bushOrange*/bushAlt*/fence） |
| 7 | SampleScene 零修改 | ✅ | 場景對 5 個模板 guid＋圖集 guid 零引用；GameManager.backgroundManager 接線完好（fileID 321862630）；BGManager.config → BGConfig.asset（guid cbb6e7d5…）接線完好；GameManager L48 ResetAll 呼叫在位 |

補充事實：
- 材質 guid 9dfc825aed78fcd4ba02077103263b40 ＝ URP 內建 Sprite-Unlit-Default.mat（Library/PackageCache/com.unity.render-pipelines.universal@fd0c0d56fb49/Runtime/Materials/），非專案資產，破圖風險低。
- 測試檔：Assets/Scripts 共 12 檔、零測試組件——沿用 round-1/2 理由跳過 run_tests（無可跑標的）。
- 派工單稱子物件 sortingOrder「0~3」：實際 Sky/Forest/Foreground authored 為 0~2、僅 Mountain 至 3；描述範圍略寬，無行為影響，不列 issue。

## Blockers

（無）

## Majors

### F1: 驗收前提失真——BGLayer.cs／BGLayerEditorWindow.cs 存在繞過驗收輪的代碼漂移
- type: code | severity: major
- 依據: 本輪派工宣稱「BGManager/BGLayer/BGConfig.cs/Tile.cs 未被修改」；round-2 報告明載 m1/m3「❌ 未修，severity 不變」且「runtime 側 BGLayer placeholder 同型問題未動」
- 事實:
  - 現行 BGLayer.cs 含 `placeholderSprite` 欄位＋OnDestroy 釋放路徑（L23/L49–50/L119–127，註解自標「round-2 m1」），及 `GetVisibleWidth` 改 public static（L90–91，自標「round-2 m3」）
  - 現行 BGLayerEditorWindow.cs BuildPreview 改用 `BGLayer.GetVisibleWidth()` 取代硬編碼 20.53f（L192–195，自標「round-2 m3」）、ClearPreview 增加 placeholder sprite/texture 釋放（L253–261，自標「round-2 m1」）
  - 兩檔修改發生於 round-2 報告落盤之後，未經任何驗收輪即進入已驗證核心；施工側記憶（scene-developer）亦未聲明此改動歸屬
- 判定為 major 非 blocker 的理由: 本 Validator 已代為逐行靜態審查（附錄 A）：修復方向正確、API 選用正確（runtime 用 Destroy／Editor 用 DestroyImmediate）、捲動與排序數學零觸碰、B3/B4/B5/B7/D7 全部複驗通過；且 Sisyphus 的 Play 動態證據是在現行代碼上取得。無行為退化，僅流程追溯性受損。
- 建議方向: Sisyphus 向 gameplay-developer 確認此批修復的派工來源與時間點並補登記；日後派工單的「未被修改」主張須以實際 diff 為準。round-2 累積 minor 帳本中 m1/m3 可標記「已修復（round-3 追認，靜態審查通過）」。

### F2: 規格漂移——requirements 仍載明正式美術接入「不在範圍（P2 另案）」
- type: requirement | severity: major
- 依據: specs/requirements/bg-layer-editor.md「明確不在範圍」區：「正式美術素材接入列 P2 另案」「本期 asset-developer 無任務（placeholder 維持）」
- 事實: 本輪已完成正式美術接入並通過驗收，規格文件未同步修訂；後續 agent 讀取規格將得到與現實相反的結論（共享記憶腐爛風險）
- 建議方向: PM 修訂規格——將 P2 項目標記「已於 round-3 提前實作」或移入範圍區並更新 asset-developer 任務描述；同步更新 shared/project.md 技術事實（由 Sisyphus 於收單時執行）

## Minors

### m1: Mountain 天際線最高點低於規格上限
- type: code | severity: minor
- 事實: 山體最高塔尖 y≈+0.2，規格允許至 +1（scene-developer 施工備註自陳）；純視覺豐富度調校空間，非缺陷

## 附錄 A：漂移代碼靜態審查（F1 證據）

| 項目 | 審查結論 |
|------|---------|
| BGLayer.placeholderSprite 欄位化＋OnDestroy | ✅ runtime 物件用 `Destroy()` 正確；createFunc 引用欄位而非區域變數；template!=null 時欄位為 null、OnDestroy 早退；不觸碰捲動/排序 |
| BGLayer.GetVisibleWidth → public static | ✅ 函數體未變（正交/透視雙分支保留），僅可見性變更；Editor 與 runtime 單一真源，符合 NFR-03/NFR-05 |
| Editor BuildPreview 計數改源 | ✅ `CeilToInt(GetVisibleWidth()/max(tw,0.01))+1` 再 `Max(n,2)`——與 runtime ComputeTileCount 公式完全同構 |
| Editor ClearPreview 釋放 | ✅ Edit mode 物件用 `DestroyImmediate()` 正確；BuildPreview 端 null 檢查保證重建 |
| 鐵律複驗 | ✅ B3 分離（depth→speed L30／renderOrder→band）、B4 z=0、B5 pool 邊界（Instantiate 僅 createFunc、Destroy 僅 onDestroy）、B7 using 白名單、D7 Repeat 取模模型——全部在位 |

## 動態驗證回填狀態（Phase 4.5）

Sisyphus 已於派工時回填核心動態證據：Play mode 四層各 2 tiles、sprite 非 null 且解析至圖集子 sprite（cloud4/pyramid/treePine/treeSmall_green2 等）、實測排序帶 10~12/20~23/30~32/40~42、x 跨度蓋滿可視寬、Console 零錯誤零警告；截圖存證 Assets/Screenshots/play_smoke_4layers.png 及 TileTemplate_{Sky,Mountain_verify-1,Forest,Foreground}_verify.png。

殘餘人類複核項（非阻塞，靜態證據已覆蓋其風險面）：
1. 目視截圖確認四層皆為正式美術、無洋紅破圖、無白色 placeholder 方塊（Validator 模型無法讀圖；sprite 引用已由 fileID→nameFileIdTable 全數靜態解析、材質為 URP 內建、Console 零 shader 錯誤佐證）

## Statistics

- blocker: 0 / major: 2（F1 code、F2 requirement）/ minor: 1
- 路由建議：F1 → Sisyphus 查證派工來源（非修復輪標的）；F2 → PM；m1 → 累積記錄
