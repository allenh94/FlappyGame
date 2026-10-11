# ADR-004: BG Layer Editor／背景授權系統架構

## Status

Accepted

## Context

現有背景為 3 個固定層：`BackgroundLayer.cs` 單類包辦建 tile＋捲動＋縮放，`GameManager.backgroundLayers[3]` 為唯一註冊點；三層遮擋依賴透視相機的 z 投影（z=0/0.1/0.2），tile sortingOrder 全 0。需求（specs/requirements/bg-layer-editor.md）要求升級為動態 Layer、Tile Template、Object Pool、Depth／Render Order 分離的可授權系統，且必須整合既有驗收成果而非重建。

## Decision

1. **TileTemplate ＝ Prefab**（D1）：多子物件層級原生支援，pool 直接 Instantiate。
2. **配置 ＝ BGConfig ScriptableObject**（D2）：Editor Window 唯一寫入、Runtime 唯讀，單向流。
3. **整合 ＝ 新類別承接、BackgroundLayer 退役**（D3）：取模捲動思想移植至 BGLayer 後刪除原類別；GameManager 改呼叫 `BGManager.ResetAll()`，Reset 語義保留、`BackgroundLayer[3]` 依賴移除。
4. **排序 ＝ sortingOrder 帶位接管遮擋**（D4）：全部背景 SpriteRenderer 落 Default Sorting Layer，order 帶 10/20/30…；所有層 z=0，遮擋禁用 z。鳥 MeshRenderer 設 sortingOrder=1000 恆在背景之上（T5 實證 transparent queue 下生效，fallback＝SortingGroup）。水管不納入本系統。
5. **速度公式 ＝ speed = baseSpeed / depth，baseSpeed=2.0**（D5）：恰好重現現狀三點（depth 1/2/4 → 2.0/1.0/0.5 u/s），遷移零手感損失。
6. **目錄 ＝ 慣例目錄隔離、不引入 asmdef**（D6）：Runtime `Assets/Scripts/Background/`、Editor `Assets/Scripts/Editor/Background/`（Unity 特殊資料夾自動排除 build）、配置 `Assets/Config/Background/`、模板 `Assets/Prefabs/Background/Tiles/`。

詳細類別圖／資料流／生命週期見 `specs/architecture/bg-layer-editor.md`。

## Consequences

- 正面：Depth 與 Render Order 完全解耦（FR-04 可對照測試）；Layer 數量資料驅動，新增層零代碼改動；穩態捲動 0 Instantiate/Destroy/GC（比照 PipeSpawner pool 模式）；速度公式與現狀數值無縫銜接。
- 正面：Editor 配置與 Runtime 實例分離，場景不再承載背景邏輯。
- 負面：鳥 MeshRenderer 的 sortingOrder 行為未實證，T5 需驗證並可能觸發 fallback 改造。
- 負面：BackgroundLayer.cs 刪除為不可逆操作，遷移期需以 parallax-background-fix 驗收項護航。
- 被放棄的替代方案：SO 清單式模板（重建 prefab 成本）、JSON 配置（失去引用序列化）、原地改造 BackgroundLayer（職責膨脹）、雙 parallax 系統並存（違反 FR-12）、續用 z 投影排序（違反分離鐵律）、新增 Sorting Layer（無收益徒增維護）。
