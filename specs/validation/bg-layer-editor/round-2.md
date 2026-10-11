# Validation Report: bg-layer-editor round 2
- **Date**: 2026-08-23
- **Task Sheet**: specs/requirements/bg-layer-editor.md
- **Architecture**: specs/architecture/bg-layer-editor.md ＋ specs/architecture/dependency-rules.md（B1–B8）＋ docs/adr/005-bg-layer-tile-lifecycle.md
- **Verdict**: **PASS**（blocker = 0）

---

## 一、Round-1 issues 追蹤清單

| ID | type | severity | 摘要 | 本輪狀態 |
|----|------|----------|------|----------|
| B1 | code | blocker | 示範配置零 Template，AC#8/#9 前提不成立 | ✅ resolved（見第二節） |
| M1 | code | major | Editor 預覽逐 renderer 縮放 vs runtime 合併 bounds 根縮放——預覽失真 | ✅ resolved（見第四節） |
| M2 | architecture | major | ObjectPool Release/Get 穩態死代碼 vs 架構 §3 描述不符 | ✅ resolved——ADR-005 裁決取模模型為正式設計（見第五節） |
| m1 | code | minor | placeholder Texture2D/Sprite 無釋放路徑 | ❌ 未修，severity 不變 |
| m2 | code | minor | Persist() 僅 SetDirty 不 SaveAssets | ✅ resolved（見第四節） |
| m3 | code | minor | BuildPreview 硬編碼可視寬 20.53f | ❌ 未修，severity 不變 |
| m4 | code | minor | CreateConfigAsset 對已存在路徑無防呆 | ✅ resolved（見第四節） |

## 二、B1 驗證：TileTemplate_HillCluster.prefab

**結論：B1 已解除（blocker → resolved）**

逐項核對 `Assets/Prefabs/Background/Tiles/TileTemplate_HillCluster.prefab` YAML：

| 檢查項 | 事實 | 判定 |
|--------|------|------|
| 子物件數量 | 根 Transform（fileID 7390681888296572882）`m_Children` 列出 3 個子 Transform；對應 Hill_Back / Hill_Mid / Hill_Front 三個 GameObject，各掛 1 個 SpriteRenderer | ✅ 3 個 SpriteRenderer 子物件 |
| z 座標 | 三個子 Transform 的 `m_LocalPosition.z` 均為 `0`（Hill_Back x0.5 y-4.1、Hill_Mid x-6 y-3.5、Hill_Front x3.5 y-3.9）；根 Transform position (0,0,0)、scale (1,1,1) | ✅ z=0，符合 dependency-rules B4（遮擋禁用 z） |
| 共用 sprite | 三個 SpriteRenderer 的 `m_Sprite` 均為 `{fileID: 21300000, guid: 2ef5473bb7ba2af4cbdd902026d0761e, type: 3}`；grep *.meta 確認該 guid 屬於 `Assets/Art/Background/HillBlock.png.meta` | ✅ 共用 HillBlock sprite |
| 子物件層次 | prefab 內建 authored sortingOrder：Hill_Back=1 / Hill_Mid=2 / Hill_Front=3（子物件間前後關係），runtime 再疊加 layer order band | ✅ 與 Play 實測 [11 12 13] 等帶位一致 |

模板資產存在於架構指定目錄 `Assets/Prefabs/Background/Tiles/`（dependency-rules B6 ✅）。round-1 B1 的四項事實全部反轉：模板資產已建立、目錄已存在、配置已引用、多子物件路徑已被真實資料行使。

## 三、BGConfig 四層 template 指向驗證

**結論：PASS——四層全部引用 TileTemplate_HillCluster**

`Assets/Config/Background/BGConfig.asset` YAML 逐層核對：

| Layer | depth | renderOrder | tileWidth | template |
|-------|-------|-------------|-----------|----------|
| Sky | 8 | 10 | 24 | `{fileID: 4370133681625299411, guid: 7d0f6d846c5007946884cd5b2353dc1f}` |
| Mountain | 4 | 20 | 24 | 同上 |
| Forest | 2 | 30 | 24 | 同上 |
| Foreground | 1 | 40 | 24 | 同上 |

- guid `7d0f6d846c5007946884cd5b2353dc1f` 經 grep *.meta 確認即 `TileTemplate_HillCluster.prefab.meta`；fileID `4370133681625299411` 即該 prefab 根 GameObject——引用精準指向模板根，非 null（round-1 四層皆 `{fileID: 0}` 的狀態已終結）
- **AC#8 前提成立**：同一 Template 被 4 個 Layer 引用（≥2）——共用情境以最強形式行使
- **AC#9 前提成立**：每層 tile 均為含 3 個 SpriteRenderer 子物件的 Template
- round-1 Console 的 4 條 `[BGLayer] 未指定 Template` 警告的前提已消除（見第六節 console 實查）

觀察（不計 severity）：四層外觀全部為同一 HillCluster 模板，Sky 層亦顯示山丘叢集。任務單僅要求「至少一層使用含多子物件的 Template」，未要求各層視覺差異化，故此配置合法達標；視覺多樣性屬後續美術議題，非本輪驗收範圍。

## 四、M1／m2／m4 驗證：BGLayerEditorWindow.cs

### M1: Editor 預覽尺寸語義 —— 已修復
- `FitTileWorldWidth`（L220–231）：先 `Encapsulate` 合併全部子 renderer 的世界 bounds（L224–226），再以 `scale = tileWidth / combined.size.x` 換算單一根縮放賦予 `tile.transform.localScale`（L229–230）
- 與 runtime `Tile.NormalizeWorldWidth` 同語義；代碼註解（L216–219）明確聲明此為 round-1 M1 修復並禁止逐 renderer 各自拉寬
- 呼叫點正確：BuildPreview L207–210 先收集 `GetComponentsInChildren<SpriteRenderer>()` 再整批傳入
- 判定：多子物件模板下預覽外框＝tileWidth，與 Play 一致——FR-16 預覽恢復參考意義。**resolved**

### m2: Persist() 持久化 —— 已修復
- L152–156：`Persist()` = `EditorUtility.SetDirty(config)` ＋ `AssetDatabase.SaveAssets()`，註解標明 round-1 m2
- 所有寫入路徑（欄位變更 L97、刪層 L87、排序 Swap L130、Add Layer L123）皆經 Persist()——變更即時落盤，關閉 Unity 不再遺失。**resolved**

### m4: New 按鈕覆蓋防呆 —— 已修復
- L133–150：`CreateConfigAsset()` 於 CreateAsset 前檢查既有資產（`existing.layers.Count > 0`），彈出 `EditorUtility.DisplayDialog` 二選一確認框（覆蓋／取消），取消即 return
- 生產配置（四層）不會被誤按靜默覆蓋。**resolved**

### m1 / m3: 本輪未修（severity 不變）
- **m1**：`MakePlaceholderSprite`（L233–241）仍每次 new Texture2D＋Sprite.Create 無釋放路徑；runtime 側 BGLayer placeholder 同型問題未動。維持 minor
- **m3**：BuildPreview L192 仍硬編碼 `20.53f` 推導預覽 tile 數（註解聲明 NFR-05 基準值）。非 16:9 Editor 視窗下預覽 tile 數可能與 Play 不一致。維持 minor

## 五、M2 驗證：架構文件一致性（§3＋D7 vs ADR 005）

**結論：PASS——「兩張皮」已由 Tech Lead 裁決消除**

三方核對：

| 文件 | 內容 | 一致性 |
|------|------|--------|
| 架構 §3 Tile Lifecycle | 改寫為固定 N 取模模型：初始化批次 Get×N 後「此後池不再出入貨；onRelease/collectionCheck 穩態閒置」；Wrap＝Mathf.Repeat 歸零瞬間全體 tile 批次右移，「FR-07 語義由取模 wrap 達成，非逐 tile OutView 事件」；裁定理由指向 D7＋ADR-005 | ✅ 不再要求 Release/Get |
| 架構 D7 | 標題即「固定 N 取模整體平移（否決逐 tile Release/Get 動態回收）」；否決理由、ObjectPool 保留職責（B5 結構性執法者）、P2 觸發條件均已記載；引用 ADR-005 | ✅ 與 ADR 同步 |
| docs/adr/005-bg-layer-tile-lifecycle.md | Status: Accepted、2026-08-23、decider tech-lead-agent；Decision＝改文件承認取模模型；Consequences 明載 onRelease 穩態閒置為已知取捨 | ✅ 正式決策紀錄完備 |

輔助檢查：
- 架構 §0／§1 Data Flow 同步更新（「此後池不再出入貨」「回收重用＝wrap 邊界的批次整體右移」），無殘留舊生命週期描述
- grep dependency-rules.md：Release/OutView/回收/lifecycle 零匹配——B5 僅規範「Instantiate/Destroy 僅限池邊界」，與取模模型不衝突，無需修訂且未被違反
- round-1 M2 的兩個路由選項中 Tech Lead 選擇「裁定取模為正式方案並修訂文件」，符合 M2 建議方向；實作無需變更。**resolved**

## 六、Console 檢查

**結論：PASS——零錯誤零警告**

- `unityMCP_read_console` 實查（error＋warning，50 條窗口）：**0 條**
- round-1 的 4 條 `[BGLayer] Layer 'X' 未指定 Template——使用 placeholder` 警告已消失——其前提（四層 template=null）因模板接入而消除，非被隱藏
- log 類型抽查亦為 0 條（console 目前無殘留輸出），工具連通正常（success:true）
- Sisyphus Play 快檢（來源標明：Sisyphus 提供之實測數據）證實 play 期間零錯誤零警告，與本輪靜態檢查一致

## 七、整體 AC 逐條對照

| # | AC | 本輪結果 | 依據 |
|---|----|----------|------|
| 1 | 多 Layer：≥4 層同時正常顯示 | ✅ PASS | BGConfig 四層齊備且全部掛真實模板；Play 快檢：四層 tile worldW 全部正規化至 24.00 |
| 2 | Z 重疊：遮擋由 Render Order 決定、穩定無閃爍 | ✅ PASS（代碼＋配置面） | prefab 三子物件 z=0、BGManager/BGLayer z=0；帶位 [11 12 13]/[21 22 23]/[31 32 33]/[41 42 43] 實測正確；「無閃爍」視覺終審仍依 Play 存證截圖 |
| 3 | Render Order：修改後遮擋正確反轉 | ⚠️ 機制成立／動態操作對照仍無證據 | sortingOrder 直通 renderer 代碼面不變；本輪新增多子物件 order 疊加實測（band+authored）強化機制可信度；「實際改 order 後遮擋反轉」操作實驗仍缺。不擋驗收 |
| 4 | 分離鐵律對照 (a)(b) | ✅ PASS（代碼面） | depth 只進 speed、renderOrder 只進 sortingOrder，零交叉；本輪代碼未觸及此路徑，round-1 靜態結論沿用 |
| 5 | Parallax：Depth 大者慢、肉眼可辨 | ✅ PASS | Foreground/Sky 速度比 8.0 實測（depth 8 vs 1 反比），與 D5 公式吻合 |
| 6 | 無縫延伸：連續捲動 ≥60 秒無空隙／跳動 | ✅ PASS | 取模模型結構性保證：相鄰間距由同一 scrollDistance 推導恆等 tileWidth，數學上不可能空隙/跳動；tileX=-scrollDistance 精準同步實測佐證；placeholder 單色無接縫風險 |
| 7 | Recycle：tile 數量恆定；Profiler 0 Instantiate/Destroy | ✅ PASS（代碼面）／Profiler 實測缺 | 固定 N 取模為 ADR-005 正式設計；熱路徑零分配靜態審查成立；NFR-02 Profiler 實測仍未提供——如實記錄，不擋驗收 |
| 8 | Template：同一 Template 被 ≥2 Layer 引用互不干擾 | ✅ **PASS（round-1 FAIL → 反轉）** | 四層共用 TileTemplate_HillCluster（最強形式）；互不干擾行為證據：四層各自 depth 速度不同、帶位不同、worldW 各自正規化 24.00 |
| 9 | 多子物件：含多子物件 Tile 完整顯示並捲動 | ✅ **PASS（round-1 FAIL → 反轉）** | HillCluster 3 個 SpriteRenderer 子物件被真實行使；sortingOrder 帶 [11 12 13] 等（band 10/20/30/40＋authored 1/2/3）證實子物件 order 偏移正確疊加；worldW=24.00 證實合併 bounds 正規化生效 |
| 10 | 動態增刪 Layer 後遊戲正常運作 | ⚠️ 機制成立／動態操作證據仍缺 | EditorWindow Add/Delete/排序＋BGManager 資料驅動迴圈代碼面不變；「刪除後 Play 正常」動態驗證無證據。不擋驗收 |
| 11 | Scene View 預覽：配置修改立即反映 | ✅ PASS | RebuildPreviewIfOn 機制不變；M1 已修復——預覽 FitTileWorldWidth 與 runtime NormalizeWorldWidth 同語義（合併 bounds 根縮放），多子物件下預覽不再失真 |
| 12 | 鳥可見性 | ✅ PASS（配置面） | 鳥 sortingOrder=1000 ≫ 背景最高帶位 43（40 band＋authored 3）；z=0 一致；視覺終審依 Play 存證 |
| 13 | 不回歸：死亡重開 Reset 正確、接線運作、Console 無錯誤 | ✅ PASS | GameManager→ResetAll 接線未動；本輪 console error/warning 雙零 |

**AC 總計：11 PASS ／ 2 ⚠️（#3、#10 均為動態操作證據缺失，非缺陷；機制面成立且 round-1 即已標注，不構成 blocker）／ 0 FAIL**

## Statistics

- **blocker: 0**（B1 resolved）
- **major: 0**（M1 resolved／M2 resolved via ADR-005）
- **minor: 2**（m1 placeholder texture 未釋放、m3 硬編碼 20.53f——累積記錄，不擋驗收）
- round-2 new: 無（本輪未發現新問題）

## 結論

**PASS**——round-1 全部 blocker 與 major 已解決：

1. **B1（blocker）**：TileTemplate_HillCluster.prefab 已建立於架構指定目錄，3 個 SpriteRenderer 子物件、z=0、共用 HillBlock sprite；BGConfig 四層全部精準引用該模板。AC#8／#9 由 FAIL 反轉為 PASS。
2. **M1（major）**：Editor 預覽改用與 runtime 同語義的合併 bounds 根縮放，多子物件下預覽不再失真。
3. **M2（major, architecture）**：Tech Lead 以 ADR-005 正式裁定固定 N 取模整體平移為設計，§3／D7／ADR 三方一致，「兩張皮」消除。

遺留 minor×2（m1、m3）如實累積記錄，依迴圈終止政策不觸發修復輪、不擋驗收。AC#3／#10 的動態操作證據缺失為驗證方法限制（Validator 唯讀無法執行 Play 操作），機制面成立，留待人工或後續 Play 驗證補強，非缺陷。

bg-layer-editor 功能驗收通過，可結案。
