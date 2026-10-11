# ADR-005: BG Tile 生命週期採固定 N 取模整體平移（否決動態 Release/Get 回收）

- **Status**: Accepted
- **Date**: 2026-08-23
- **Deciders**: tech-lead-agent（裁決）、Sisyphus（代為落盤）
- **關聯**: bg-layer-editor 驗收 round-1 issue M2（type=architecture, major）；架構文件 §3／D7；dependency-rules B5

## Context

Validator round-1 發現文件與實作「兩張皮」：

- 架構 §3 原描述逐 tile 動態回收生命週期：`OutView → pool.Release → 前方缺位 pool.Get 重用`。
- 實際實作為**固定 N 取模整體平移**：Initialize 批次 `Get()×N` 後序列凍結，穩態 Update 以單一 `scrollDistance = Mathf.Repeat(…, tileWidth)` 推導全部 tile 位置，從不呼叫 Release/Get。

行為等價已實測確認：NFR-01（穩態 0 Instantiate/Destroy）、NFR-02（熱路徑零 GC）、NFR-03（tile 數貼近可視範圍）、FR-06（無縫）、tile x 與 `-scrollDistance` 精準同步、四層 8 倍速差驗證。需裁定改碼對齊文件，或改文件承認現行模型。

## Options Considered

1. **改碼補齊 per-tile Release/Get**——對齊原 §3 文件。
2. **改文件承認固定 N 取模模型**（採納）。
3. 移除 ObjectPool、直接 Instantiate——順帶簡化。

## Decision

採選項 2：**固定 N 取模整體平移為正式設計**。§3 已改寫，新增 D7；TilePool 角色降級記載如下。

### 理由

1. 行為等價且已驗證通過；改碼是對已驗證核心的最高風險變更，零當前收益。
2. 取模模型結構性優越：全部位置由單一 scrollDistance 推導 → 相鄰間距恆等 tileWidth、永不累積漂移；per-tile 重定位有浮點誤差累積與排序邊界案例風險。
3. FR-07「回收重用」語義在取模模型中同樣成立：每張 tile 每 tileWidth 週期回到前緣，只是批次發生於 wrap 邊界而非逐 tile 事件。
4. YAGNI：執行期動態增刪 Layer／多解析度適配均列 P2 另案；屆時依 dependency-rules 演進觸發條件重估本決策。
5. ObjectPool 保留職責：建立邊界（createFunc/onDestroy 是全系統唯一 Instantiate/Destroy 點，B5 的結構性執法者）＋template/placeholder 分支集中處。onRelease/collectionCheck 穩態閒置屬已知取捨。

### 被放棄方案

- 選項 1（改碼）：高風險零收益，見理由 1-2。
- 選項 3（移除池）：失去 B5 結構保障與分支集中點，見理由 5。

## Consequences

- 正面：文件與實作一致，維護者不再被誤導；零回歸風險；穩態路徑極小（一次取模＋N 次賦值）易審計。
- 負面：ObjectPool 的 onRelease/collectionCheck/maxSize 穩態為閒置參數（已於架構 §0/§3 明示角色降級）。
- 負面：未來 P2 若需執行期變更 tile 數量，須重開本決策（觸發條件已登記 dependency-rules）。
