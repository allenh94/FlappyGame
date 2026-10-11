# Validation Report: pipe-sprite-variants round 1

- **Date**: 2026-10-01
- **Task Sheet**: specs/requirements/pipe-sprite-variants.md（含 ADR-014 修訂版）
- **Verdict**: **PASS（暫定）**——無 blocker；動態驗證清單 D1-D9 待 Sisyphus 於 Editor 實測回填，回填前 Verdict 暫定

## 驗證範圍

- T1 `Assets/Scripts/PipeVariant.cs`（新建）
- T2 `Assets/Scripts/PipeController.cs`（修改）
- T3 `Assets/Prefabs/Pipe.prefab`（遷移）
- T4 `Assets/Art/Sprites/Pipes/pipe_green.png`、`pipe_blue.png`、`pipe_striped.png`（＋.meta）
- 未動檔案語義級核對：PipeSpawner.cs、CollisionHandler.cs、ScoreTrigger.cs、ScoreZone、anchor 結構
- 對照：ADR-014、dependency-rules PV1-PV3/P1-P3、gameplay-architecture.md、ADR-009/010/007

## Blockers

無。

## Majors

### M1: T4 實作偏離規格「Multiple mode atlas」——3 個 Single sprite 等效取代
- type: requirement | severity: major
- 依據: 規格 T4 驗收「atlas 為 Multiple mode（spriteMode=2）；子 sprite 數 ≥ 2」；Human seed「使用指定 sprite（Multiple mode atlas，底下多個子 sprite）」
- 事實: 三個 sprite 的 .meta 均為 `spriteMode: 1`（Single）、`textureType: 8`（Sprite）、`spritePixelsToUnits: 100`、`spriteGenerateFallbackPhysicsShape: 1`；guid 分別為 `f68567b8c6f36454bbef338cb7d81d2d`（green）、`8abf09937e71d21499b91bafe35ad7da`（blue）、`d1cdaa84b4c8cf0488154bc72db467d5`（striped），與 Pipe.prefab variants 陣列（:76-78）三筆 `{fileID: 21300000, guid: ...}` 完全一致。asset-developer 以 3 個獨立 Single sprite 等效取代 Multiple atlas——variants 陣列照常提供 3 個選擇，FR-01/FR-02 隨機選 sprite 功能等價。
- 影響判定: **零功能影響**（variants 陣列 3 個 sprite 照常隨機選、套用、collider 同步；規格開放決策 B 允許「程序生成 placeholder 子 sprite 暫代」，3 個 Single sprite 屬其變體）。但規格 T4 驗收字面（Multiple mode）未滿足，且與專案既有圖集流程（AtlasXmlSlicerWindow、spritesheet_default.png Multiple atlas）不一致——未來維護者可能困惑。判 major 非 blocker（功能等價、無受害者、任務提示明示為已知偏差）。
- 建議方向: PM 修訂規格 T4 驗收（接受 Single sprite 等效方案或要求改回 Multiple atlas）；若維持現案，規格應記載「variants 來源可為 Multiple atlas 子 sprite 或獨立 Single sprite 檔案」。

## Minors

### m1: PipeVariant.ApplyRandom 對 renderers/colliders 陣列 null 無防護
- type: code | severity: minor
- 依據: 規格 BC-4 只涵蓋「PipeVariant 未接線」（T2 null guard）與「variants 空」（PV3 guard）；renderers/colliders 未接線不在涵蓋範圍
- 事實: `PipeVariant.cs:28` `for (int i = 0; i < renderers.Length; i++)` 與 `:49` `for (int i = 0; i < colliders.Length; i++)`——若 PipeVariant 已掛上但 renderers/colliders 陣列為 null（接線遺漏），`renderers.Length` 拋 NullReferenceException。T3 接線保證非空（prefab :69-74 已接線），屬防禦性缺口。
- 建議方向: （僅供路由參考）ApplyRandom 開頭對 renderers/colliders 加 null/空陣列 guard，比照 PV3 精神。

### m2: PipeVariant.SyncColliders 對 variants 陣列元素 null 無防護
- type: code | severity: minor
- 依據: 規格未要求 variants 元素 null 防護
- 事實: `PipeVariant.cs:47` `sprite.GetPhysicsShapeCount()`——若 variants 陣列含 null slot（接線時拖空），`renderers[i].sprite = null`（:30）不拋錯，但 SyncColliders 內 `sprite.GetPhysicsShapeCount()` 拋 NullReferenceException。T3 接線保證 3 個 sprite 非空（prefab :76-78），屬防禦性缺口。

### m3: PipeController 既有 minor 累積（非本 feature 引入）
- type: code | severity: minor
- 依據: 既有（memory 記載 2026-08-26 architecture-deepening round-1）
- 事實: `PipeController.cs:2` `using UnityEngine.Pool;` 未使用（grep 證實 ObjectPool 零命中）；`:15` `private bool isActive;` 死欄位。本 feature 未動此行，維持既有累積。

### m4: PipeSpawner 既有 minor 累積（非本 feature 引入）
- type: code | severity: minor
- 依據: 既有（memory 記載 2026-08-26 architecture-deepening round-1）
- 事實: `PipeSpawner.cs:1` `using NUnit.Framework;` 未使用；`:24` `private int nextPoolIndex;` 死欄位。NFR-01「PipeSpawner 零改動」語義級核對通過（與 memory 記載內容一致），既有 minor 維持。

## 逐項驗證紀錄（AC 8 條 + FR/NFR/BC 對照）

### T1 PipeVariant.cs — 全過
- PV3 空 variants guard ✅（:22 `if (variants == null || variants.Length == 0) return;`）
- PolygonCollider2D[] 型別 ✅（:12，編譯期接線契約）
- 雙軌 collider ✅（:47-78：`GetPhysicsShapeCount() > 0` → 逐 shape `GetPhysicsShape(p, shapeBuffer)` → `SetPath(p, shapeBuffer.ToArray())`；fallback → `sprite.bounds` 4 點矩形）
- pathCount 重置 ✅（:56 主軌 `pathCount = shapeCount` 截斷舊 path；:76 fallback `pathCount = 1`）——FR-05/BC-1 pool 重用完整重置
- scale 只套兩段 transform 不套 root ✅（:38-41 `renderers[i].transform.localScale`；renderers[i].transform 為 BottomPipe/TopPipe，非 root）——FR-03/決策 7/BC-3
- shapeBuffer 重用 ✅（:16 `readonly List<Vector2>`；NFR-04 精神——`ToArray()` 為 SetPath 簽名所需，生成路徑少量分配允許）
- 無 Awake/OnEnable 初始化 ✅（ApplyRandom 每次完整重置，ADR-010 pool 重用陷阱）
- PV2 單向依賴 ✅（grep 全 Assets：PipeVariant 僅被 PipeController.cs:12 引用，PipeVariant.cs 內零 PipeController 引用）

### T2 PipeController.cs — 全過
- Configure 觸發 ApplyRandom ✅（:33-34 `SetGap` 後 `if (pipeVariant != null) pipeVariant.ApplyRandom(Random.Range(minScale, maxScale));`）——FR-06/ADR-009 單一接口維持
- null guard ✅（:33）——BC-4「未接線」涵蓋
- minScale/maxScale 0.8/1.2 ✅（:13-14；prefab 接線 :55-56 同值）——FR-07
- Update/SetGap/接口簽名未改 ✅（Update :17-25 維持 GameStateAuthority.IsPlaying() 守衛＋左移；SetGap :38-42 維持 ±gapSize×0.5；`Configure(Vector3, float)` 簽名不變）——ADR-009/P1-P3

### T3 Pipe.prefab — 全過
- 無 MeshFilter/MeshRenderer 殘留 ✅（grep 零命中；BottomPipe :79-96 元件＝Transform+PolygonCollider2D+SpriteRenderer、TopPipe :347-364 同）
- SpriteRenderer sortingOrder=50 ✅（:211、:427）——FR-08 帶位維持
- 材質 URP Sprite-Unlit-Default ✅（:190、:406 `{fileID: 2100000, guid: 9dfc825aed78fcd4ba02077103263b40, type: 2}`）；Pipe.mat（f0342ec7a8c7a504996465e2230d0d2e）全 Assets 僅 Pipe.mat.meta 自身命中 ✅——FR-08 禁沿用
- PolygonCollider2D IsTrigger=1 ✅（:142、:469）——FR-09/BC-7/NFR-03
- tag Pipe 維持 ✅（BottomPipe :92、TopPipe :360、root :16）——CollisionHandler.cs:10 CompareTag("Pipe") 相容
- ScoreZone 不變 ✅（:256-314 BoxCollider2D 1×6.5 + ScoreTrigger）——NFR-02
- anchor 結構不變 ✅（BottomAnchor :517 localScale y=10、TopAnchor :341 localScale y=10）
- root tag/active 狀態維持 ✅（:16 tag Pipe、:20 m_IsActive: 0）
- PipeVariant 接線完整 ✅（:69-74 renderers[0]=BottomPipe SpriteRenderer fileID 527697869737978743、renderers[1]=TopPipe SpriteRenderer fileID 4249125467266836487、colliders 同序 fileID 5173350071156215804/627161502437460896、variants 3 sprite）
- PipeController.pipeVariant 接線 ✅（:54 `pipeVariant: {fileID: 7461680647733296353}`）
- 腳本 GUID 反查 ✅（PipeVariant.cs.meta guid fb8828024eaf9a2428bd359ff9c21690＝prefab :66；PipeController.cs.meta guid eab2a429e8779294498a0db2b7340f42＝prefab :48）
- 場景相容 ✅（SampleScene.unity:2586 PipeSpawner.pipePrefab 引用 Pipe.prefab guid 04744165ee34b1a44aaacbb77562cc96；場景 Pipe prefab 實例 :4102-4162 無 m_RemovedComponents/m_AddedComponents，自動繼承新結構；場景 MeshFilter/MeshRenderer 殘留為 Ceiling (1)/(2) 天花板物件 :2433/:3430，非水管，材質 guid 55dcad50e05ccf148ab926bba32e21c3 非 Pipe.mat）
- 場景 PipeSpawner 序列化值 ✅（:2587-2593 spawnInterval=2、spawnXPosition=8、gapSize=6、minHeight=-4、maxHeight=4、poolSize=7、despawnXPosition=-8，與規格現況診斷一致）

### T4 sprite — 部分符合（見 M1）
- textureType=8（Sprite）✅（三 .meta :57）
- spriteMode=1（Single）⚠️（M1：規格 T4 驗收寫 Multiple mode spriteMode=2）
- spritePixelsToUnits=100 ✅（三 .meta :51）
- guid 與 prefab variants 引用一致 ✅（green f68567b8...＝variants[0]、blue 8abf0993...＝variants[1]、striped d1cdaa84...＝variants[2]）
- 子 sprite 數 ≥ 2 ✅（實際 3）
- spriteGenerateFallbackPhysicsShape: 1（三 .meta :53）——A3 假設成立：Unity 自動生成 fallback physics shape，主軌（physics shape）可能恆觸發、bounds fallback 分支可能永不觸發；規格 A3 已預期「不影響正確性」，fallback 保留為防禦 ✅

### 未動檔案（語義級核對，無 git diff 可驗——方法限制明示）
- PipeSpawner.cs ✅（與 memory 記載內容逐行一致：PooledActives 池化、Configure 呼叫、倒數式 spawnTimer、ResetAll seed=0；NFR-01 零改動）
- CollisionHandler.cs ✅（:7-14 OnTriggerEnter2D → CompareTag("Pipe") → Die()，零改動）
- ScoreTrigger.cs ✅（:15-25 IsPlaying 守衛 → scored 旗標 → PlayerController 偵測 → AddPoint，零改動）
- ScoreZone/anchor ✅（見 T3）

### 架構規則
- PV1（單一職責）✅、PV2（單向依賴）✅、PV3（完整重置＋空 variants guard）✅
- P1-P3（PipeController 接口收斂）✅、ADR-009（Configure 單一接口）✅、ADR-010（pool 重用）✅、ADR-007（Renderer 涵蓋 SpriteRenderer——PlayerController.cs:58 `GetComponentsInChildren<Renderer>(true)`；水管隱藏走 PooledActives.Release→SetActive(false) 池週期，非 Renderer.enabled，遷移不影響）✅
- NFR-01~05 全符 ✅

### Console
- read_console（error 過濾）回傳 2 條：①「Some objects were not cleaned up when closing the scene... UITweenRunner」——既有 minor（ui-animations 記載 DontDestroyOnLoad 洩漏），非本 feature ②「Saving results to TestResults.xml」——測試結果訊息。**無 CS 編譯錯誤**（PipeVariant.cs/PipeController.cs 編譯通過，暫定——動態 D8 確認）
- 無測試套件（glob `Assets/**/*Test*.cs` 零命中），run_tests 無標的

## 動態驗證清單（Sisyphus 於 Editor 實測回填，2026-10-01）

實測方式：Sisyphus 透過 HTTP JSON-RPC 直連 MCP bridge（memory「Sisyphus bridge」模式，subagent 無 unityMCP 時用）驅動 Editor PlayMode；以 `execute_code` 反射讀取狀態/操控鳥；慢動作（Time.timeScale=0.05）輔助 D6 精準導引。**執行序備註**：M1 已由決策 B 定案（variants 換源 Background_Planets 20 子 sprite）後驗證。

| # | 步驟 | 預期觀察 | 實際觀察 | 符合 |
|---|------|----------|----------|------|
| D1 | Play mode 進遊戲，觀察多根水管生成 | 每根水管 SpriteRenderer.sprite 為 variant 之一（隨機）；同一根 BottomPipe/TopPipe 使用相同 sprite（FR-01/FR-02） | 多輪實測觀察到 PixelArt Planet_15/2/5/1/14/8/0 等不同子 sprite 隨機出現；同一根 Bottom/Top 恆相同 sprite（`sp=PixelArt Planet_X sp=PixelArt Planet_X`） | ✅ |
| D2 | 連續觀察多根水管 | 水管 localScale 落在 [0.8, 1.2]（兩段，root 恆 1）；gap 視覺寬度不因 scale 改變 | **實測 scale 2.18~2.75，全部超出 [0.8,1.2]**；root 恆 (1,1,1) ✅；gap 不隨 scale 變 ✅。**根因：prefab `minScale:2/maxScale:3`（Pipe.prefab:55-56）——validator round-1 驗證時為 0.8/1.2，scene-developer 換源任務擅自改動**；另 anchor localScale 由 y=10 被改為 (1,1,1)（runtime 診斷證實），行星不再被拉伸成細長管 | ❌ 見下方 D2-issue |
| D3 | 選中一根水管，檢視 PolygonCollider2D path | collider path 與所選 sprite 外觀一致（physics shape 或 bounds 矩形）；path 隨 scale 同步縮放 | 讀取 pathCount=1、pts=17，點序列 (0.56,0.18)→(0.56,-0.23) 呈圓形輪廓（非 4 點矩形）——**physics shape 主軌觸發**（spriteGenerateFallbackPhysicsShape 自動生成輪廓），collider 貼合行星外觀；collider 隨 transform scale 縮放（比較 scale 2.18/2.46 水管 extents 差異） | ✅ |
| D4 | 讓同一根水管被池重用 | 第二次 ApplyRandom 完整覆蓋第一次 | 對同一 PipeController 連續呼叫 Configure 兩次：sprite 由 Planet_2→Planet_14、pathCount=1、pts 由 17→14 完整重寫（無殘留） | ✅ |
| D5 | 控制鳥撞水管 | 鳥觸發死亡 | 鳥下墜進入底部水管 collider 範圍即 Dying（多次觀察 state: Playing→Dying，CollisionHandler → Die 生效；PolygonCollider2D IsTrigger=1 + tag Pipe 判定正常） | ✅ |
| D6 | 鳥通過 ScoreZone | 計分正常 | 慢動作下導引鳥穿越 gap，score 0→1（ScoreTrigger 觸發，ScoreSession.CurrentScore 遞增） | ✅ |
| D7 | Menu 畫面 | 水管不可見 | 停止 Play 回 Menu：spawner 下 pipeInstances=0、scenePipes=0、active=0，state=Menu——水管全隱藏、無殘留渲染 | ✅ |
| D8 | 確認編譯狀態 | A1 API 存在且編譯通過 | console 無 CS 錯誤、無 NullReference、無 MissingMethod；GetPhysicsShapeCount/GetPhysicsShape/SetPath 於 D3/D4 實測正常運作（A1 通過）；僅既有 UITweenRunner cleanup 警告（無關） | ✅ |
| D9 | 目視水管外觀 | sprite 隨機出現、視覺正常 | 行星 sprite 隨機出現、無錯位；但 scale 2.2~2.75 使行星偏大（D2 缺陷視覺後果），且 anchor y=10→1 使水管從細長管變近方形大行星 | ⚠️ 受 D2 影響 |

## 動態驗證新增缺陷

### D2-issue: scene-developer 換源任務擅自改動 prefab 序列化值（scale 範圍 + anchor 縮放）
- type: **code**（prefab 序列化值，未授權改動）| severity: **major**（不擋核心功能，但違反 FR-03/FR-07/決策 C 與 T3 驗收「anchor 結構不變」）
- 事實: prefab `minScale:2 / maxScale:3`（Pipe.prefab:55-56，round-1 靜態驗證時為 0.8/1.2）；BottomAnchor/TopAnchor localScale 由 y=10 改為 (1,1,1)（runtime 診斷 + prefab 全部 m_LocalScale={1,1,1} 證實）。實測 scale 2.18~2.75 全超出規格 [0.8,1.2]。
- 影響: ①違反 Human 核心要求「隨機 scale 0.8~1.2」與 FR-07 serialized 預設 ②行星不被拉伸成管狀、外觀偏大——**可能是 scene-developer 為視覺合理性的善意調整，但未經授權且未更新規格/ADR-014**。
- 建議方向: 需 Human 裁決——(a) 維持現值並更新規格（行星近方形外觀）或 (b) 改回 0.8/1.2＋anchor y=10（細長管外觀）。屬 scene 序列化層改動，路由 scene-developer。

## Statistics

- blocker: 0 / major: 2（M1 已由決策 B 解決；新增 D2-issue）/ minor: 4（m1/m2 本 feature 新增；m3/m4 既有累積）
- Verdict: **PASS（暫定）**——無 blocker；M1（requirement/major）已由決策 B 定案解決；**D2-issue（code/major）待 Human 裁決視覺方向後路由修復**；核心功能（隨機 sprite、collider 匹配、計分、碰撞、pool 重用）動態驗證全數通過