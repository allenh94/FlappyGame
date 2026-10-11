# Validation Report: ui-animations round 4（破紀錄分數跳動動畫）
- **Date**: 2026-08-25
- **Task Sheet**: specs/requirements/ui-animations.md（＋本次派工：破紀錄分數跳動）
- **Verdict**: **PASS（暫定）**（blocker 0 / major 0 / minor 1；動態回填前暫定）

> 本次改動橫跨 ui-animations（分數跳動）與 game-states（背景過渡＋回 Menu 隱藏鳥/水管），故各自產 round 報告。本檔為 ui-animations 側；背景過渡與隱藏見 `specs/validation/game-states/round-2.md`。

## 驗收標的（本次新增：破紀錄分數跳動）
- 僅當「本次分數 > 舊最高分」才播；未破紀錄瞬間顯示
- `GameManager.EndGame()` 在 `SaveHighScore()` 前快照舊最高分（previousHighScore + DidBreakRecord 旗標）
- `UIController` 於 GameOver 先瞬間顯示舊最高分，等 gameOverPane 面板滑入完成（onComplete）後才啟動整數逐格跳動，1 秒，EaseOutCubic，定格新值
- 用 `UITween.FloatTo`（新增泛用 float 值 tween）

## 逐條核對

| 標的 | 結果 | 證據 |
|------|------|------|
| 快照舊最高分於 SaveHighScore 前 | ✅ | `GameManager.cs:119-120` `previousHighScore = GetHighScore(); DidBreakRecord = score > previousHighScore;` 於 `:122 SetState(GameOver)` 與 `:123 SaveHighScore()` 之前 |
| 未破紀錄不啟動分數動畫 | ✅ | `UIController.cs:155-164` else 分支：`gameOverHighScoreText.text = GetHighScore()` 瞬間顯示，`PlayPanelSlideIn(gameOverPaneRect)` 無 onComplete、`PlayTextSlideOut()` 僅文字滑出——無 count-up 副作用 |
| 先瞬間顯示舊值，面板滑入後才跳動 | ✅ | `UIController.cs:147-151` 先寫 `GetPreviousHighScore()`，`PlayPanelSlideIn(gameOverPaneRect, () => PlayHighScoreCountUp())` 以 onComplete 延後啟動 |
| 1 秒、EaseOutCubic、定格新值 | ✅ | `UIController.cs:47` `highScoreCountDuration = 1f`；`:185-188` `FloatTo(..., EaseOutCubic, () => text = toScore)`；`UITweenRunner.Update` ValueTween 完成時 `t.apply(t.to)` 再 onComplete（`UITween.cs:209-213`） |
| 泛型 float tween | ✅ | `UITween.cs:51-55` `FloatTo(object token, Action<float> apply, ...)`＋`ValueTween` 執行單元 |
| 狀態切換清理 | ✅ | `UIController.cs:218-222` `StopPanelTweens` 對 gameOverHighScoreText 呼叫 `StopFloatTweens`（token 一致）；`StopValueTweens` snap 到 to（`UITween.cs:170-181`） |
| 無第三方套件 | ✅ | `Assets/` grep `DOTween|LeanTween|iTween|DoTween` 零命中 |

## Blockers
無。

## Majors
無。

## Minors
### m1: 分數跳動為新功能，ui-animations 規格未記載
- type: requirement | severity: minor
- 依據: 規格追溯性（ui-animations.md FR-01~FR-06 皆未涵蓋「破紀錄分數逐格跳動」）
- 事實: 本次新增的破紀錄分數跳動（FloatTo/ValueTween/onComplete 觸發）是全新功能，`specs/requirements/ui-animations.md` 無對應 FR/NFR 條目，驗收只能對照派工描述而非規格。規格與實作脫節。
- 建議方向: PM 於 ui-animations.md 補記分數跳動 FR（含「僅破紀錄」「面板滑入後啟動」「1 秒 EaseOutCubic」）。

## 動態驗證清單（無法靜態驗證者）
| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | 破紀錄死亡（本次分數 > 舊最高分） | gameOverPane 滑入後，最高分文字由舊值整數逐格跳動至本次分數，1 秒 EaseOutCubic，定格新值 | 待填 |
| D2 | 未破紀錄死亡 | 最高分瞬間顯示目前最高分，無跳動動畫 | 待填 |
| D3 | 破紀錄跳動進行中按「重新開始」 | 跳動立即停止，無殘留錯位；進入 Playing | 待填 |
| D4 | 破紀錄跳動進行中按「回主選單」 | 跳動立即停止；Menu 最高分顯示新值 | 待填 |

## Statistics
- blocker: 0 / major: 0 / minor: 1