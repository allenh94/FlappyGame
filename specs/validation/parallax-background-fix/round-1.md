# Validation Report: parallax-background-fix round 1
- **Date**: 2026-08-21（靜態驗收輪；Play mode 動態驗證由 Sisyphus 於本報告後補做）
- **Task Sheet**: specs/requirements/parallax-background-fix.md
- **Verdict**: PASS（靜態範圍）——blocker 0。動態項見下方 Unverified 清單，PASS 以「靜態可驗證部分全數通過」為範圍，動態驗收完成前 feature 不算完整關單。

## 驗證方法與證據來源

| 方法 | 內容 |
|------|------|
| 源碼審查 | `Assets/Scripts/BackgroundLayer.cs`（159 行全文）、`Assets/Scripts/GameManager.cs`（72 行全文） |
| 場景 YAML 直讀 | `Assets/Scenes/SampleScene.unity`（1863 行，BG_Layer_01/02/03、Main Camera、Bird、GameManager 區塊逐項核對） |
| GUID 反查 | 收集 7 個專案腳本 .meta 的 GUID，對照場景 13 個 + Pipe.prefab 2 個 `m_Script` 引用，全數有歸屬 |
| 全域 grep | 死碼類名（BackgroundScroll / ParallaxBackgroundSetup / ParallaxBackgroundQuickSetup）掃 Assets 全部 .cs/.unity/.prefab/.asset/.controller：零匹配 |
| Console | unityMCP read_console（error+warning 共 23 條，逐條歸因） |
| 測試 | 專案無 Tests 目錄、無 test assembly（glob 全 Assets 確認），run_tests 不適用 |

## Blockers

（無）

## Majors

（無）

## Minors

### M1: runtime 生成的 Texture2D / Sprite 無釋放路徑
- type: code ｜ severity: minor
- 依據: 邊界情況未處理（資源生命週期）
- 事實: `BackgroundLayer.CreatePlaceholderSprite()`（第 57–68 行）每次 `Start()` 以 `new Texture2D(32,32)` + `Sprite.Create()` 生成 placeholder，類別無 `OnDestroy`，`SetSprite()` 換圖時亦不釋放舊 sprite/texture。Unity 不會自動銷毀 runtime 建立的 Texture2D（非 asset），需靠 GC + `Resources.UnloadUnusedAssets`。
- 影響評估: 目前量級可忽略——三層各 4KB／每次 Play session，且 `SetSprite()` 現階段無任何呼叫者。不擋驗收。
- 建議方向: 未來接入正式美術（P2 的 SetSprite 掛點啟用）時，補 `OnDestroy` 釋放與換圖時的舊資源釋放即可。

### M2: 三層父物件序列化 scale=(20,12) 與執行期值不一致
- type: code ｜ severity: minor
- 依據: 場景衛生／可維護性
- 事實: 場景 YAML 中 BG_Layer_01/02/03 的 Transform `m_LocalScale` 均為 `{x:20, y:12, z:1}`（L713、L1496 及 L01 對應區塊），執行期由 `Start()` 正規化為 `(1,1,1)`。行為正確（這是 B2 修復的防禦設計，兩位 developer 已互相交接），但 Editor Scene view gizmo 與任何直接讀場景的後續 agent 都會看到誤導性的 20×12。
- 建議方向: 下次任何人動場景時順手把三層 scale 存成 (1,1,1)，一行改動消除歧義。非必須。

### M3: tileWidth=24 的覆蓋保證以 aspect ≤ 16:9 為前提
- type: code ｜ severity: minor
- 依據: 邊界情況（規格已明文列 P2 不在範圍，僅記錄前提條件）
- 事實: 相機為透視 FOV60°@z=-10（SampleScene L481–482），16:9 下 z=0 平面視野寬 ≈ 20.53，雙 tile 保證區 [−12,+12] 覆蓋充分（margin ≥1.47）。但若 aspect > ~2.17（如 21:9≈2.33，視野寬 ≈26.9 > 24），邊緣將露底色。
- 影響評估: 規格邊界情況表明文「多解析度支援列 P2 不在本次範圍」，當前 Game view 口徑下 AC 成立。不擋驗收。

## Unverified—需動態測試（Sisyphus 截圖驗收清單）

以下 AC 我以靜態手段（源碼數學證明 + 序列化值核對）給出高信心預判，但本質上需要 Play mode 實測才能關單：

| # | 項目 | 對應 AC | 靜態證據 | 動態待確認 |
|---|------|---------|----------|-----------|
| U1 | 連續捲動 ≥60 秒無空隙/跳動 | T1 AC-1、整體 AC-2 | 換行數學正確：單一累計器 `scrollDistance` 經 `Mathf.Repeat` 夾在 [0,W)，兩 tile 位置由絕對值推導（A=−d、B=−d+W），間距恆等 W，無每幀累加漂移；覆蓋區間任意時刻 ⊇ 視野 | 實際畫面無縫；另注意雙 tile 接縫處理論上有亞像素級 float 舍入差（x+24−12 vs x+12），同色下應不可見，需眼見為憑 |
| U2 | 三層速度分離肉眼可辨、深度感 | 整體 AC-1、T4 AC-2 | speed 序列化值 2/1/0.5（L363/L1572/L789）、方向數學向左正確 | 視覺層次 |
| U3 | 渲染順序動態穩定無閃爍/z-fighting | T4 AC-1、整體 AC-3 | sortingOrder 10/5/0（近>遠，確定性排序）+ z 分離 0/0.1/0.2 雙保險 + 三層父 SpriteRenderer 全數 m_Enabled:0（防幽靈渲染） | 實際無閃爍 |
| U4 | Play mode 下視差層不被遮擋 | T3 AC-4 | BackGround1/2/3、Background、Quad 在場景 YAML 零匹配（grep 證據） | 實際畫面 |
| U5 | 死亡重開後三層歸位 | 整體 AC-5、T1 AC-3 | `Reset()` 設 scrollDistance=0 後走與初始化完全相同的 `ApplyScrollPositions()` 路徑 → 位置與初始位形數學等價；GameManager.Die→Invoke(StartGame)→layer.Reset() 接線完整 | 實際重開畫面 |

## 逐條 AC 核對表

### Task 1（BackgroundLayer 修復）
- [x] 公開 API 簽名完全不變：`speed`(f)、`tileWidth`(f)、`tileHeight`(f)、`layerColor`(Color)、`Reset()`、`SetSprite(Sprite)`、`SetColor(Color)` 全數存在且簽名一致（源碼 L19–26、127–158）
- [x] tile 世界尺寸 = tileWidth × tileHeight：`ApplyWorldSize()` 以 `sprite.bounds.size`（32px@100PPU=0.32）換算 localScale=(W/0.32, H/0.32)，父 scale 已正規化為 1 → 世界尺寸恆等參數值。數學成立
- [x] 換行基於絕對位置：見 U1 靜態證據
- [x] `Reset()` 歸位兩個子 tile：見 U5 靜態證據
- [ ] Play mode ≥60 秒無縫 → **U1 unverified**
- [x] Console 無錯誤/警告（遊戲程式碼口徑）：23 條 error/warning 全部歸因 `com.coplaydev.unity-mcp` 外掛自身（WebSocket 未初始化、TransformHandle 序列化 NRE、deprecated API 提示），零遊戲程式碼錯誤、零編譯錯誤、零 Missing Script。其中「BackgroundLayer String concatenation in Update()」經源碼核實為 MCP lint 誤報（Update 內僅 float 運算，無字串）

### Task 2（死碼刪除）
- [x] 三支腳本及其 .meta 已刪除：Assets/Scripts/ 僅剩 7 組 .cs+.meta（glob 證據）
- [x] 無殘留引用/Missing Script：類名全域 grep 零匹配；GUID 反查 15 個 m_Script 引用（場景 13 + prefab 2）全數對應現存資產——專案腳本 7 個（BackgroundLayer×3、GameManager、PipeSpawner、PlayerController、CollisionHandler 於 Bird、PipeController+ScoreTrigger 於 Pipe.prefab）＋套件腳本（TextMeshProUGUI、UniversalAdditionalCameraData/LightData、URP Volume、UI CanvasScaler）
- [x] 編譯通過：Console 零編譯錯誤

### Task 3（場景清理）
- [x] 五個物件移除：YAML grep `m_Name: BackGround|Background$|Quad$` 零匹配
- [x] 玩家鳥不受影響：Bird（fileID 1181836431）七元件完整——PlayerController/Rigidbody2D/Transform/MeshFilter/MeshRenderer/CollisionHandler（gameManager 接線指向 GameManager 元件，健康）/BoxCollider2D
- [x] Hierarchy 無停用垃圾殘留：五目標不存在；剩餘停用物件 Ceiling(1)(2)、Pipe 為功能物件（管線池/天花板），不在規格清理清單
- [ ] Play mode 不被遮擋 → **U4 unverified**（靜態證據已強：遮擋物不存在）

### Task 4（視差層配置）
- [x] sortingOrder 遠<近：03=0（L765）、02=5（L1548）、01=10（L339）
- [x] 世界尺寸 ≥ 相機視野：24×14 vs 透視視野 20.53×11.55（z=0 平面，含 y=1 相機偏移重算：視野 y∈[−4.77,+6.77] ⊂ 層覆蓋 [−6,+8]）——scene-developer 發現規格「20×12」隱含的正交相機假設不成立並修正為 24×14，方向正確且必要（20 < 20.53 必露縫）。規格硬性要求是「≥視野、不露底」而非鎖死 20×12，故判定合規；建議 PM 下次修訂更新該描述
- [x] 三層 x,y 對齊：均 (0,1)
- [x] layerColor 紅/黃/藍：{1,0,0}/{1,0.92,0.016}/{0,0,1}
- [x] 場景已儲存：上述修改值均在磁碟 YAML 中
- [ ] 三層同時可見、順序穩定、速度可辨 → **U2/U3 unverified**

### 整體 AC
- [x] GameManager 接線語義不變：`backgroundLayers` 陣列（3 個 fileID 接線健康）+ `foreach layer.Reset()` 與規格描述一致。⚠️ 附註：專案非 git repo，無法做 diff 級「零改動」驗證，此結論為語義級核對
- [x] 維持 placeholder 美術：runtime 程式生成純色方塊（非新素材檔案）；Art/ 僅 WhiteSquare.png（仍被三層停用的父 SpriteRenderer 經內嵌 Sprite 引用，非孤兒）；未新增任何素材檔

## 架構檢查

- `specs/architecture/dependency-rules.md` 規則表為空（待 Tech Lead 首次制定），無可違反條目
- 本次實作未新增跨腳本依賴：BackgroundLayer.cs 僅 using UnityEngine，零外部腳本引用；四個任務單 arch-review 均為 no，與實作相符
- type: architecture 問題：0

## Statistics
- blocker: 0 ｜ major: 0 ｜ minor: 3（code×3）
- unverified（動態待測）: 5 項（U1–U5）
- type 分布: code 3 ｜ architecture 0 ｜ requirement 0

## 給 Sisyphus 的路由建議

- 無 blocker → 不觸發修復輪
- M1–M3 累積記錄即可；M1 可在 P2「正式美術接入」案中一併處理
- 請執行 U1–U5 動態截圖驗收（≥60 秒捲動、速度分離、重開歸位、四角無露底）；若動態發現問題，以 round-2 報告追加
- 轉交 PM 參考（非缺陷）：規格中「現行意圖為 20×12」與「確認 orthographic size」的描述基於過時的相機假設，實際為透視 FOV60°，24×14 為必要修正
- 轉交 gameplay-dev 注意（非缺陷）：scene-developer 回報 editor hierarchy 曾出現停用 "Pipe" 根物件（instanceID −1300 負值）但磁碟場景無此物，疑似 editor session runtime 殘留；磁碟真相乾淨，若再次出現建議排查
