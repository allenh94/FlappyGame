# ADR-001: Prototype Scope

## Status

Accepted

## Context

flipbird 需要一個可玩的 Prototype 來驗證核心手感。範圍需要夠小可以快速完成，但夠完整可以測試操作體驗。

## Decision

Prototype 只包含：
- 鳥的垂直移動（flap + 重力）
- 管子生成和碰撞
- 背景滾動
- 分數計算（不顯示）
- 死後立刻重來

不包含：
- 分數 UI
- 音效
- 日夜變化
- 難度遞增
- Game Over 畫面

## Consequences

- 可以在 1-2 個工作天內完成
- 核心手感可以完整測試
- 美術用 placeholder，之後替換
- 分數系統已經在背景運作，加 UI 很簡單
