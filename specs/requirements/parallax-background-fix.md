# 規格：視差背景修復（parallax-background-fix）

> Status: done（validator round-1 PASS，2026-08-21）｜ Owner: pm-agent ｜ Date: 2026-08-21
> （本檔由 Sisyphus 代 pm-agent 落盤——pm-agent 當時寫入權限故障，內容為其原文交付物）

## 功能：Parallax Background Fix

- **目標**：玩家在飛行時看到三層以不同速度向左捲動的彩色背景，產生前後深度感，且循環無縫、重開機狀態正確。
- **性質**：修復既有功能（bugfix），非新功能。美術維持 placeholder（純色層），本次不引入新素材。

## 需求分析：什麼叫「修好」

現行 `BackgroundLayer.cs` 採「父物件動態生成 2 個子 tile、交替移動達成無限循環」的設計。**此設計本身保留，只修實作**。修好的定義：

1. **深度感**：三層以不同速度向左捲動（BG_Layer_01 近層 speed=2 > BG_Layer_02 中層 speed=1 > BG_Layer_03 遠層 speed=0.5），肉眼可辨前後層次。
2. **無縫循環**：捲動任意距離（含長時間 ≥60 秒）畫面不出現空隙、跳動或接縫錯位。
3. **渲染順序穩定**：遠層永遠畫在近層之後，無閃爍、無 z-fighting。
4. **無遮擋**：場景中沒有任何物件擋在視差層前面。
5. **重置正確**：死亡重開後三層回到初始相位，無殘留偏移。
6. **接線不變**：`GameManager.backgroundLayers` 陣列與 `Reset()` 呼叫照常運作，公開 API 不變。

## 已確認的 Bug 清單（探索報告）

| # | Bug | 根因 | 影響 |
|---|-----|------|------|
| B1 | tile 尺寸數學錯誤 | `CreateTile` 用 32×32px sprite @100PPU = 0.32 世界單位，乘上 localScale (20,12) 後 tile 實際僅 6.4×3.84 單位，但換行邏輯假設 tile 寬 = `tileWidth`(20) | 捲動 6.4 單位後出現空隙 |
| B2 | 父物件縮放繼承 | 子 tile 建立在 scale=(20,12,1) 的父物件下，localScale 相乘成世界縮放 (400,144,1)；localPosition x=20 被放大成世界 400 單位 | tile 完全錯位，飛出畫面外 |
| B3 | 無深度分離 | 三層同位置 (0,0,0)、同 sortingOrder=0 | 渲染順序不可控，可能閃爍/z-fighting |
| B4 | 遮擋物 | `BackGround1/2/3` 靜態 Quad 位於 z=1，比視差層 z=0 更靠近相機 (z=-10) | 視差層被完全擋住 |
| B5 | 死碼 | `BackgroundScroll.cs`、`ParallaxBackgroundSetup.cs`、`ParallaxBackgroundQuickSetup.cs` 無人引用；場景含 `Background`（停用）、`Quad`（停用，掛 PlayerController）殘留物 | 誤導後續開發與維護 |

## 涉及物件

| 類型 | 路徑 | 處置 |
|------|------|------|
| Script | `Assets/Scripts/BackgroundLayer.cs` | **修改**（B1、B2） |
| Script | `Assets/Scripts/GameManager.cs` | **不改**（接線正確） |
| Script | `Assets/Scripts/BackgroundScroll.cs` | **刪除**（死碼） |
| Script | `Assets/Scripts/ParallaxBackgroundSetup.cs` | **刪除**（死碼） |
| Script | `Assets/Scripts/ParallaxBackgroundQuickSetup.cs` | **刪除**（死碼） |
| Scene | `Assets/Scenes/SampleScene.unity` | **修改**（清垃圾＋配三層） |
| Art | `Assets/Art/Sprites/WhiteSquare.png` | 不變（placeholder 夠用） |

## 死碼處置決策

- **刪除** `BackgroundScroll.cs`、`ParallaxBackgroundSetup.cs`、`ParallaxBackgroundQuickSetup.cs`。理由：三者皆無任何引用（已確認 specs/architecture/ 文件亦未提及），保留只會誤導 agent 與人類貢獻者。刪除前須掃描場景與 Prefab 確認無殘留引用（防 Missing Script）。
- **保留** `BackgroundLayer.SetSprite()`／`SetColor()`。雖然目前無人呼叫，但它們是公開 API 的一部分，是未來接入正式美術的掛點，不屬於死碼。
- **腳本位置不動**：gameplay-developer memory 有「腳本放 Assets/Scripts/Gameplay/」的約定，但現有腳本全平鋪在 `Assets/Scripts/`。bugfix 期間不移動檔案（降低風險），目錄重整另案處理。

---

## 任務單

### Task 1: 修復 BackgroundLayer tile 數學與縮放繼承

- **Priority**: P0
- **Assigned**: gameplay-developer
- **arch-review**: no（維持既有「雙 tile 交替循環」設計，僅修實作，無新增跨腳本依賴）
- **Goal**: 解決 B1（tile 尺寸 vs 換行距離不一致）與 B2（父縮放繼承導致 tile 錯位），使無限循環真正無縫。
- **Unity Objects**: `Assets/Scripts/BackgroundLayer.cs`
- **實作約束（不變式，實作方式由 developer 自行決定）**:
  - tile 的**世界寬度**必須等於 `tileWidth` 參數值（換行距離 = 實際視覺寬度）
  - tile 的**世界高度**必須等於 `tileHeight` 參數值
  - 子 tile 不得受父物件縮放影響而錯位（父 scale 或子 scale 補償，二擇一，developer 決定）
  - 換行位置應由絕對位置推導，避免每幀累加造成浮點誤差漂移
  - 公開 API 簽名完全不變：`speed`、`tileWidth`、`tileHeight`、`layerColor`、`Reset()`、`SetSprite()`、`SetColor()`
  - `GameManager.cs` 零改動
- **Acceptance Criteria**:
  - [ ] Play mode 下任一層連續捲動 ≥60 秒，畫面無空隙、無跳動
  - [ ] Scene view 檢查：兩個子 tile 的世界尺寸 = tileWidth × tileHeight，並排相接
  - [ ] `Reset()` 後兩個 tile 回到初始位置
  - [ ] Console 無錯誤/警告

### Task 2: 刪除死碼腳本

- **Priority**: P1
- **Assigned**: gameplay-developer
- **arch-review**: no（specs/architecture/ 已確認無引用，不需 Tech Lead 同步文件）
- **Goal**: 移除 B5 的三支死碼腳本，消除維護噪音。
- **Unity Objects**: `Assets/Scripts/BackgroundScroll.cs`、`Assets/Scripts/ParallaxBackgroundSetup.cs`、`Assets/Scripts/ParallaxBackgroundQuickSetup.cs`
- **Acceptance Criteria**:
  - [ ] 刪除前已確認 SampleScene 與所有 Prefab 無這三支腳本的引用
  - [ ] 三支腳本及其 .meta 已刪除
  - [ ] 專案重新編譯通過，Console 無 Missing Script 錯誤

### Task 3: 場景清理（移除遮擋與殘留物）

- **Priority**: P0
- **Assigned**: scene-developer
- **arch-review**: no
- **Goal**: 解決 B4 與 B5 場景部分——移除擋住視差層的靜態 Quad 與所有測試殘留物。
- **Unity Objects**: `SampleScene` 中的 `BackGround1`、`BackGround2`、`BackGround3`、`Background`、`Quad`
- **注意事項**:
  - `BackGround1/2/3`（z=1 靜態 Quad）→ 刪除
  - `Background`（停用，掛舊版 BackgroundScroll + MeshCollider）→ 刪除
  - `Quad`（停用，上面掛著 PlayerController！）→ 刪除前**必須**先確認真正的玩家鳥物件另有其物且完好無損；此 Quad 是測試殘留物，不是玩家本體
- **Acceptance Criteria**:
  - [ ] 上述五個物件自場景移除
  - [ ] 玩家鳥、管線、地板等其他遊戲物件不受影響
  - [ ] Hierarchy 無停用的垃圾物件殘留
  - [ ] Play mode 下視差層不再被遮擋

### Task 4: 視差層場景配置修正

- **Priority**: P0
- **Assigned**: scene-developer
- **arch-review**: no
- **依賴**: Task 1 完成（配置正確性依賴修好的 tile 數學）
- **Goal**: 解決 B3——建立穩定的渲染順序，並確保三層完整覆蓋相機視野。
- **Unity Objects**: `SampleScene` 中的 `BG_Layer_01`、`BG_Layer_02`、`BG_Layer_03`
- **配置要求**:
  - sortingOrder 分離且**遠層 < 近層**：BG_Layer_03（speed=0.5，最遠）最低、BG_Layer_02 居中、BG_Layer_01（speed=2，最近）最高（具體數值 developer 決定，如 0/5/10）
  - 可選保險：z 位置微幅分離（SpriteRenderer 以 sortingOrder 為準，z 非必須）
  - 每層世界尺寸 ≥ 相機視野——相機為透視 FOV60° @ z=-10，z=0 平面視野寬 ≈20.53 單位（16:9）；層世界尺寸以 24×14 為基準（含餘量），任何時刻不得露出底色
  - 三層 x、y 對齊一致
  - `layerColor` 維持維持紅/黃/藍 placeholder 配色
- **Acceptance Criteria**:
  - [ ] Play mode：三層同時可見，遠層在近層之後，順序穩定無閃爍
  - [ ] 三層速度分離肉眼可辨（近快遠慢）
  - [ ] 畫面四角與邊緣無露底
  - [ ] 場景已儲存

### 建議執行順序（Sisyphus 調度，串行）

```
Task 1 (gameplay) → Task 2 (gameplay) → Task 3 (scene) → Task 4 (scene)
```

符合 workflow 的 gameplay → scene 串行原則；Task 4 對 Task 1 的依賴自然滿足。

---

## 邊界情況

| 情境 | 風險 | 對策 |
|------|------|------|
| 浮點誤差累積 | 長時間捲動後 tile 換行位置漂移，出現細縫 | 換行基於絕對位置計算（Task 1 不變式） |
| Reset() 只重置移動偏移、漏掉 tile 位置 | 重開後相位錯亂 | Reset 必須同時歸位兩個子 tile（Task 1 驗收項） |
| aspect ratio 不同的裝置 | 層寬不足以覆蓋視野 | placeholder 階段以 24×14 為基準；「tile 寬 ≥ 視野寬」為硬性要求，多解析度支援列 P2 不在本次範圍 |
| `Quad` 上的 PlayerController 是唯一玩家來源 | 誤刪會毀掉玩家 | Task 3 明文要求刪除前確認鳥本體存在 |
| 刪腳本留下 Missing Script | 場景/Prefab 引用斷裂 | Task 2 要求刪除前掃描引用 |

## 驗收標準（整體，Validator 對照用）

- [ ] 三層背景以不同速度向左捲動（2 / 1 / 0.5，近快遠慢），視覺上有前後深度感
- [ ] 背景無縫循環：連續捲動 ≥60 秒不出現空隙或跳動
- [ ] 三層渲染順序穩定（遠在下、近在上，不閃爍、不 z-fighting）
- [ ] 場景無遮擋/干擾視差層的殘留物件（BackGround1/2/3、Background、Quad 全數移除）
- [ ] 死亡重開後三層背景狀態正確重置
- [ ] `GameManager` 既有接線不變且正常運作（零程式碼改動）
- [ ] 死碼腳本（BackgroundScroll、兩個 Setup）已刪除，專案編譯通過，Console 無錯誤
- [ ] 維持 placeholder 美術（純色層），未引入新素材

## 明確不在範圍（P2，另案）

- 正式美術素材接入（`SetSprite()` 掛點已保留）
- 多解析度/多 aspect ratio 適配
- 腳本目錄重整（`Assets/Scripts/Gameplay/` 慣例落地）
