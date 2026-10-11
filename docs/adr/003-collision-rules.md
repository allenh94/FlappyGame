# ADR-003: Collision Rules

## Status

Accepted

> **2026-10-01 相容性確認（pipe-sprite-variants）**：水管 collider 由 BoxCollider2D 改 PolygonCollider2D（ADR-014），trigger 語意（IsTrigger=1、tag Pipe）不變——碰撞規則（鳥撞管→死）不受影響。

## Context

碰撞判定決定遊戲難度和公平性。

## Decision

使用完整懲罰規則：
- 鳥撞地板 → 死
- 鳥撞天花板 → 死
- 鳥撞管子（任何部分，含邊緣擦到）→ 死

死亡後立刻重置所有物件，無縫繼續。

## Consequences

- 難度較高（擦到管子邊緣就死）
- 公平明確（沒有灰色地帶）
- 玩家需要精確控制
- 死後立刻重來，節奏流暢
