# Validation Report: bg-layer-editor round 1
- **Date**: 2026-08-23
- **Task Sheet**: specs/requirements/bg-layer-editor.md
- **Architecture**: specs/architecture/bg-layer-editor.md ＋ specs/architecture/dependency-rules.md（B1–B8）
- **Verdict**: **FAIL**（blocker = 1）

---

## 一、整體驗收標準逐條對照（13 條）

| # | AC | 結果 | 依據 |
|---|----|------|------|
| 1 | 多 Layer：≥4 層同時正常顯示 | ✅ PASS | BGConfig.asset 四層齊備；BGManager.Awake 迴圈資料驅動建立；Play 證據：Background 根＋4 層子物件、Sky 2 tiles 符合 N=CeilToInt(20.53/24)+1 公式。視覺面有截圖存證（Assets/Screenshots/t9_play_verification_clean.png）但 Validator 無法看圖，「正常顯示」的最終視覺判定依賴該截圖 |
| 2 | Z 重疊：遮擋由 Render Order 決定、穩定無閃爍 | ⚠️ 代碼面成立／視覺面無法驗證 | 場景 YAML＋代碼證實全層 z=0（BGManager localPosition=Vector3.zero、BGLayer tile localPosition z=0），四層本就完全重疊 z=0，遮擋僅靠 order 10/20/30/40。「穩定無閃爍」需動態畫面判讀，Validator 無法看圖 |
| 3 | Render Order：修改後遮擋正確反轉 | ❓ 無法驗證 | 機制上 sortingOrder 直通 renderer（Tile.ApplyRenderOrderBand）代碼面成立，但「實際修改某層 order 後遮擋反轉」的操作性對照測試無任何證據（證據包未含、Validator 工具唯讀無法執行 Play 操作） |
| 4 | 分離鐵律對照測試 (a)(b) | ⚠️ 代碼面成立／動態對照未執行 | 靜態審查：depth 只進 speed（BGLayer.Initialize L29 `speed = BaseSpeed / Max(depth, 0.01f)`）、renderOrder 只進 sortingOrder（Tile.ApplyRenderOrderBand L36–41），兩者零交叉零讀取。(a)(b) 兩組動態對照實驗無執行證據 |
| 5 | Parallax：Depth 大者慢、肉眼可辨 | ✅ PASS | Play 實測 Sky x=-0.23（0.25 u/s）vs Foreground x=-1.85（2.0 u/s），恰為 depth 反比 8 倍速差；公式與 D5 裁定（baseSpeed=2.0）吻合 |
| 6 | 無縫延伸：連續捲動 ≥60 秒無空隙／跳動 | ⚠️ 數學面成立／60s 時長未佐證 | 取模推導數學保證無縫（Mathf.Repeat＋單一 scrollDistance 推導全 tile 位置，相鄰間距恆等 tileWidth）；placeholder 單色更不可能出現接縫。但本次新系統「連續 ≥60s」的明確時長證據未提供（舊系統 98.6s 紀錄不可沿用） |
| 7 | Recycle：tile 數量恆定；Profiler 0 Instantiate/Destroy | ⚠️ 代碼面成立／Profiler 實測無證據 | activeTiles 固定 N 張、Update/ApplyScrollPositions 路徑零 Instantiate/Destroy/new class 分配（Vector3 為 struct 棧分配）——靜態審查可等價推定。NFR-02 要求的 Profiler 實測未提供 |
| 8 | Template：同一 Template 被 ≥2 Layer 引用互不干擾 | ❌ **FAIL** | **專案中不存在任何模板資產**（見 B1），此 AC 前提不成立 |
| 9 | 多子物件：含多子物件 Tile 完整顯示並捲動 | ❌ **FAIL** | 同 B1 根因：零模板資產，多子物件路徑從未被真實資料行使 |
| 10 | 動態增刪 Layer 後遊戲正常運作 | ⚠️ 代碼面成立／動態操作無證據 | EditorWindow 支援 Add/Delete/排序；BGManager 純資料驅動迴圈，新增第 5 層確實無需改碼（程式碼中無寫死 3 層假設）。「刪除後 Play 正常」的動態驗證無證據 |
| 11 | Scene View 預覽：配置修改立即反映 | ⚠️ 機制存在／發現缺陷 M1 | RebuildPreviewIfOn 於每次變更重建預覽 ✓；但預覽尺寸語義與 runtime 不一致（見 M1），多子物件情境下預覽失真 |
| 12 | 鳥可見性 | ⚠️ 配置面成立／視覺面無法驗證 | 場景 YAML 證實鳥 MeshRenderer sortingOrder=1000、Default Sorting Layer、z=0；背景帶位 max 40＜1000 ✓。實際渲染可見性需看截圖，Validator 無法看圖 |
| 13 | 不回歸：死亡重開 Reset 正確、接線運作、Console 無錯誤 | ✅ PASS | GameManager L48 呼叫 backgroundManager.ResetAll()（場景 YAML 證實 backgroundManager 引用存在）；Console 實查零錯誤、僅 4 條設計內防呆警告 |

## 二、架構鐵律 B1–B8 對照

| # | 規則 | 結果 |
|---|------|------|
| B1 | 單一 parallax 實作 | ✅ Mathf.Repeat 僅存於 BGLayer.cs；BackgroundLayer.cs 已刪除（Assets/Scripts 全域 glob 確認不存在）；GameManager.backgroundLayers 欄位已移除 |
| B2 | 配置單向流 | ✅ Runtime 四支腳本無任何寫入 BGConfig 代碼；EditorWindow 為唯一寫入者 |
| B3 | Depth／Render Order 分離 | ✅ 兩條資料流零交叉（詳 AC#4） |
| B4 | 遮擋禁用 z | ✅ 代碼與場景雙重證實 z=0；帶位 10/20/30/40＋鳥 1000 |
| B5 | Instantiate/Destroy 僅限池邊界 | ⚠️ 形式遵守（Instantiate 僅 createFunc、Destroy 僅 onDestroy、熱路徑零分配），但 pool 的 Release/Get 重用路徑穩態為死代碼——見 M2 |
| B6 | 目錄邊界 | ⚠️ 三個目錄（Scripts/Editor/Config Background）皆符合；**模板目錄 Assets/Prefabs/Background/Tiles/ 不存在**（併入 B1） |
| B7 | 外部依賴白名單 | ✅ using 僅 UnityEngine/System/System.Collections.Generic/UnityEngine.Pool；EditorWindow 另用 UnityEditor（於 Editor 目錄內合法）；零遊戲邏輯依賴 |
| B8 | GameManager→BGManager 單向接線 | ✅ GameManager 僅呼叫 ResetAll()；BGManager 無反向引用 |

---

## Blockers

### B1: 示範配置零 Template——Task 9 規格要求未達成，兩條整體 AC 前提不成立
- type: code　severity: blocker
- 依據:
  - 任務單 Task 9 Goal：「Sky/Mountain/Forest/Foreground 至少四層……**其中至少一層使用含多子物件的 Template**」
  - 整體驗收 AC 第 8 條（Template ≥2 Layer 共用）、第 9 條（多子物件 Tile 完整顯示）
  - dependency-rules B6：模板 prefab 應位於 `Assets/Prefabs/Background/Tiles/`
- 事實:
  1. `Assets/Config/Background/BGConfig.asset` 四層 `template` 欄位全部為 `{fileID: 0}`（null）
  2. `Assets/Prefabs/Background/` 目錄整個不存在（glob 無結果）——架構指定的模板目錄從未建立
  3. Console 殘留 4 條 `[BGLayer] Layer 'X' 未指定 Template——使用 placeholder` 警告，四層全數走 placeholder 路徑
  4. 因此 FR-08／FR-09／FR-10 的 runtime 行使路徑（template instantiate、多子物件 bounds 合併、子物件 order 帶偏移）只有代碼、從未被真實資產驗證過
- 影響: 整體驗收 13 條中 2 條直接 FAIL；T9 的 Goal 承諾未兌現（其三條 AC 字面雖過，但「示範場景」未展示系統的核心賣點——模板化 tile）
- 建議方向: （僅供路由）補建至少一個多子物件模板 prefab 於 Tiles/ 目錄、至少一層（建議 Forest）指定之；另建第二層引用同一模板以滿足 AC#8 的共用情境，再跑一輪 Play 驗證

## Majors

### M1: Editor 預覽與 runtime 的多子物件尺寸語義不一致——預覽會失真
- type: code　severity: major
- 依據: FR-16（Scene View 即時預覽應反映配置結果）；T8 AC「改 Tile 配置後 Scene View 即時更新」
- 事實:
  - Editor 預覽 `BuildPreview.FitWorldWidth`（BGLayerEditorWindow.cs L206–211）：**逐個 renderer 各自**縮放到 tileWidth——多子物件 template 下每個子物件都被獨立拉到全寬，整體外框可能達 N×tileWidth
  - Runtime `Tile.NormalizeWorldWidth`（Tile.cs L18–32）：合併全部子 renderer bounds 後換算**單一根縮放**，整體外框＝tileWidth
  - 兩者僅在單 renderer（現狀 placeholder）下等價；一旦 B1 補上多子物件模板，預覽與 Play 畫面立即分歧，FR-16 的「預覽」失去參考意義
- 建議方向: （僅供路由）preview 改用與 Tile.NormalizeWorldWidth 相同的合併 bounds 根縮放算法

### M2: ObjectPool 的 Release/Get 重用路徑穩態為死代碼——與架構 §3 Tile Lifecycle 描述不符
- type: architecture　severity: major
- 依據: 架構文件 §3（OutView → pool.Release → onRelease 停用 → 前方缺位 pool.Get 移至最右）；dependency-rules B5 精神
- 事實:
  - BGLayer.Update／ApplyScrollPositions 對固定 N 張 activeTiles 做整體取模平移，**從不呼叫 tilePool.Release()／Get()**；actionOnRelease（SetActive(false)）與 collectionCheck 在穩態永不觸發
  - 「回收重用」以取模跳回等價達成：tile 離開視野後仍在序列中，scrollDistance 歸零時整體回到初始排布——視覺與效能目標（NFR-01/02/03、FR-06/07）全部達成
  - 但架構 §3 描述的 per-tile OutView 判定＋Release/Reuse 流程未按圖施工；ObjectPool 在此實作中退化為「一次性物件容器」
- 影響: 不擋驗收（行為等價且更簡單），但架構文件與實作出現永久性分歧，後續維護者將被誤導
- 建議方向: （僅供路由 Tech Lead）二擇一：裁定「取模整體平移」為正式方案並修訂 §3／§0 描述；或要求 Developer 對齊 §3 lifecycle。不應長期兩張皮

## Minors

### m1: placeholder Texture2D/Sprite 無釋放路無釋放路徑（慣犯模式重演）
- type: code　severity: minor
- 事實: BGLayer.CreatePlaceholderSprite 每層各建一份 32×32 runtime texture＋Sprite（共 4 份重複），EditorWindow.MakePlaceholderSprite 再一份；均無 OnDestroy／釋放。與 parallax-background-fix round-1 M1（gameplay-developer 資源生命週期弱點）同型。量極小不擋驗收

### m2: Persist() 僅 SetDirty 不 SaveAssets——「持久化」依賴使用者手動存檔
- type: code　severity: minor
- 事實: BGLayerEditorWindow.Persist 只呼叫 EditorUtility.SetDirty；若使用者改完配置不存檔即關閉 Unity，變更遺失。FR-14 AC「可用且持久化」在嚴格解讀下未自動完成（Unity 慣例內可接受，故 minor）

### m3: BuildPreview 硬編碼可視寬 20.53f
- type: code　severity: minor
- 事實: BGLayerEditorWindow.cs L180 以常數 20.53 推導預覽 tile 數；runtime 用相機即時計算（GetVisibleWidth）。非 16:9 的 Editor 視窗比例下預覽 tile 數可能與 Play 不一致。註解已聲明為 NFR-05 基準值，屬已知近似

### m4: New 按鈕對已存在路徑無防呆，可直接覆蓋生產配置
- type: code　severity: minor
- 事實: CreateConfigAsset 直接 AssetDatabase.CreateAsset 至 DefaultConfigPath；該路徑已有四層配置資產，誤按 New 將靜默覆蓋。工具防呆缺失

---

## 環境觀察（依 Sisyphus 指示不計入 severity）

- **mid-play assembly reload 清空非 serialized 狀態**：BGLayer.def／activeTiles、BGManager.layers 均為非 serialized 欄位，Play 中觸發 script compilation 會導致 tile 凍結。反射注入可恢復、乾淨 session 正常。判定為 Editor 工作流風險（Play 中勿編譯），非 runtime 缺陷。若日後要根治，方向是 serialized 狀態或 Initialize 重入——屬 Tech Lead／Developer 議題，Validator 僅留痕
- **Die() → Invoke(StartGame, 0.1f) 即死即重生**為既有遊戲設計，每次重開 ResetAll() 歸零捲動符合 FR-13，非缺陷

## 驗證方法限制聲明

1. 本輪為靜態審查（規格 vs 代碼 vs 場景/prefab YAML vs Console）＋採信 Sisyphus 提供的 Play 量測證據（速度映射、tile 排布、同步追蹤）
2. Validator 無法查看截圖（t9_play_verification_clean.png），凡涉「肉眼可辨／正常顯示／無閃爍」的視覺終審均標註依賴該存證
3. 專案無任何測試檔（glob 全域確認），run_tests 無意義故未執行
4. Console 檢查結果：Error 0、Warning 4（全為 template=null 防呆警告，屬 T4 規格要求的設計內路徑）

## Statistics

- **blocker: 1**（B1 示範配置零 Template）
- **major: 2**（M1 預覽失真 / M2 pool 死代碼 vs 架構 §3）
- **minor: 4**（m1 texture 洩漏 / m2 持久化 / m3 硬編碼 / m4 New 覆蓋防呆）

## 結論

**FAIL**——blocker 1 條（B1）。runtime 核心（BGLayer/BGManager/Tile/BGConfig）與架構鐵律 B1–B4、B7、B8 全部合格，Play 量測證據紮實；但示範配置未包含任何 Tile Template，導致整體驗收的 Template 共用與多子物件兩條 AC 無法成立，Task 9 Goal 未兌現。請路由 scene-developer/gameplay-developer 補模板資產與配置後進 round-2；M1 應與 B1 同輪處理（補模板前先修預覽算法，否則新模板一掛上預覽即失真）。
