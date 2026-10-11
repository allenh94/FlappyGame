# ADR-002: Movement System

## Status

Accepted

## Context

鳥的移動是手感的核心。需要決定移動模型和參數。

## Decision

使用經典 Flappy Bird 移動模型：
- 按下空白鍵：施加向上的 flap 力度
- 鬆開：受重力向下加速
- 有 terminal velocity（最大下墜速度）
- 旋轉跟隨速度方向（上衝時鳥頭朝上，下墜時鳥頭朝下）
- 鳥只在 Y 軸移動，X 軸固定

## Consequences

- 玩家永遠知道自己的鳥在哪裡（水平位置不變）
- 碰撞判定更簡單可預測
- 經典手感，玩家已經熟悉
- 參數可以在 Inspector 調整測試
