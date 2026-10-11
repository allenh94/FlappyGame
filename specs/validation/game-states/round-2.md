# Validation Report: game-states round 2（背景過渡動畫＋回 Menu 隱藏鳥/水管）
- **Date**: 2026-08-25
- **Task Sheet**: specs/requirements/game-states.md、specs/architecture/game-states.md、specs/architecture/dependency-rules.md
- **Verdict**: **PASS（暫定）**（blocker 0 / major 1 / minor 4；動態回填前暫定）

> 本次改動橫跨 ui-animations（分數跳動）與 game-states（背景過渡＋回 Menu 隱藏鳥/水管），故各自產 round 報告。本檔為 game-states 側；分數跳動見 `specs/validation/ui-animations/round-4.md`。

## 驗收標的（本次新增）
1. **背景過渡動畫（1 秒）**：`BackToMenu()`（GameOver→Menu）、`StartGame()`/`Restart()`（→Playing）觸發背景反向快退（`BGLayer.MotionMode.ScrollingBack`），從當前 scrollDistance 平滑回 0，1 秒 EaseOutCubic；完成後回 Menu→Ambient 慢速捲動、回 Playing→正常捲動。取代原本瞬間 `ResetAll()`/瞬間凍結。
2. **回 Menu 隱藏 Bird 與水管**：`BackToMenu()` 呼叫 `HidePlayerAndPipes()`（Bird SetActive(false)、水管 `pipeSpawner.ResetAll()`）；Menu 只留背景倒回動畫；`StartGame()` 補 `player.SetActive(true)` 恢復。

## 逐條核對

| 標的 | 結果 | 證據 |
|------|------|------|
| 過渡 1 秒、EaseOutCubic | ✅ | `BGLayer.cs:18` `ScrollBackDuration = 1f`；`:152` `EaseOutCubic(p)`；`:153` `LerpUnclamped(scrollBackStart, 0f, eased)` |
| 過渡完成後回 Menu→Ambient | ✅ | `BGLayer.cs:200-203` `ScrollBackToAmbient`→`BeginScrollBack(Ambient)`；`:159` 完成後 `motion = postScrollBackMotion`（Ambient） |
| 過渡完成後回 Playing→Normal | ✅ | `BGLayer.cs:194-197` `ScrollBackToNormal`→`BeginScrollBack(Normal)` |
| Ambient 慢速捲動（選單活著） | ✅ | `BGLayer.cs:20` `AmbientSpeedFactor = 0.15f`；`:168` Ambient 態 step×0.15 |
| 過渡不受凍結限制 | ✅ | `BGLayer.cs:148-162` ScrollingBack 分支先於 frozen 檢查，`return` 前不查 frozen |
| 過渡中再次 StartGame 不卡 ScrollingBack | ✅ | `BeginScrollBack`（`:205-211`）重設 `scrollBackStart=scrollDistance`、`scrollBackTimer=0`——重新錨定當前位置續跑，非卡死 |
| 過渡中 Pause 不卡 | ✅ | Pause→SetFrozen(true)，過渡仍跑完→motion=Normal→frozen 生效停止（見 m2） |
| 回 Menu 隱藏 Bird | ✅ | `GameManager.cs:135-139` `HidePlayerAndPipes` → `player.gameObject.SetActive(false)` |
| 回 Menu 釋放水管 | ✅ | `GameManager.cs:138` `pipeSpawner.ResetAll()` |
| StartGame 恢復鳥顯示 | ✅ | `GameManager.cs:94` `player.gameObject.SetActive(true)`；`:95` `player.Reset(playerStartPosition)` 完整重置（`PlayerController.cs:105-112` 重置 isDead/位置/速度/旋轉） |
| StartGame 水管重生 | ✅ | `GameManager.cs:96` `pipeSpawner.ResetAll()`；Playing 態 `PipeSpawner.Update` 重新生成 |
| 背景單向流（B8） | ✅ | GameManager→BGManager 僅呼叫 `SetNormal/SetAmbient/SetFrozen/ScrollBackToStart/ScrollBackToMenu`；BGManager 無反向引用 GameManager |
| 無第三方套件 | ✅ | `Assets/` grep 零命中 |
| Console 無遊戲錯誤 | ✅ | 僅 MCP 噪音＋既有 UITweenRunner 清理警告（見 m4）；測試 0 案 Passed |

## Blockers
無。

## Majors
### M1: `HidePlayerAndPipes` 用 `player.gameObject.SetActive(false)` 停用 gameplay 物件，與 G2「禁 SetActive 停用」衝突（灰色地帶）
- type: architecture | severity: major
- 依據: dependency-rules.md G2「凍結機制＝各系統自查 IsPlaying()；背景經 GameManager→BGManager.SetFrozen 單向凍結（B8）；**禁 SetActive 停用、禁 Time.timeScale=0**」（理由：SetActive 副作用不可控）
- 事實: `GameManager.cs:137` `player.gameObject.SetActive(false)` 直接以 SetActive 停用玩家物件。此為「回 Menu 隱藏鳥」的產品行為（派工明列），非凍結機制本身（凍結仍靠 IsPlaying()/SetFrozen 正確運作），故**非 G2 凍結機制違規**；但規則文字絕對（「禁 SetActive 停用」），且玩家確以 SetActive 被停用，屬規則灰色地帶。功能上安全：`StartGame()` 以 `SetActive(true)`＋`player.Reset()` 完整重置（isDead/velocity/rotation 全歸位），無殘留狀態。
- 建議方向: Tech Lead 裁定——G2 是否放寬允許「Menu 隱藏」用 SetActive（並註明 Reset 契約），或改以非停用機制（如 renderer 隱藏）達成。非 blocker：功能安全、派工明列、凍結機制未破壞。

## Minors
### m1: `BGManager.ResetAll()` 成死碼＋架構文件漂移
- type: code | severity: minor
- 事實: grep 確認 `BGManager.ResetAll()`（`BGManager.cs:43-49`）無任何呼叫者（StartGame/BackToMenu 改用 ScrollBackToStart/ScrollBackToMenu）；`specs/architecture/game-states.md` 第 1 節類別圖仍載「GameManager→BGManager.ResetAll()（StartGame）」——文件與實作脫節。
- 建議方向: 移除死碼或保留；Tech Lead 更新架構文件為 ScrollBackToStart/ScrollBackToMenu。

### m2: 過渡進行中暫停，背景仍持續捲回（不受凍結）
- type: code | severity: minor
- 事實: StartGame 後 1 秒過渡內按 P→Pause→SetFrozen(true)，但 ScrollingBack 分支忽略 frozen（`BGLayer.cs:148-150`），背景在 Paused 期間繼續捲回至完成。屬派工「過渡不受凍結限制」之設計後果，但造成「暫停時背景仍動」的短暫視覺不一致（<1 秒）。非卡死（完成後 motion=Normal＋frozen 生效）。

### m3: `PipeSpawner.spawnTimer` 未於 ResetAll 重置（既有）
- type: code | severity: minor
- 事實: `PipeSpawner.ResetAll()`（`PipeSpawner.cs:119-127`）只釋放水管不清 `spawnTimer`；回 Menu→StartGame 後首根水管生成時機取決於上一局殘留計時。既有行為（非本次引入），但 Menu→Start 路徑使其更常被觸及。

### m4: UITweenRunner DontDestroyOnLoad 場景關閉洩漏（既有）
- type: code | severity: minor
- 事實: Console 警告「Some objects were not cleaned up when closing the scene… UITweenRunner」。runner 為 DontDestroyOnLoad 且永不銷毀，關場景時 Unity 警告。既有（ui-animations 引入），非本次回歸。

## 動態驗證清單（無法靜態驗證者）
| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | 開始遊戲（Menu→Playing） | 背景由當前位置反向快退至 0，1 秒 EaseOutCubic；完成後正常捲動 | 待填 |
| D2 | 死亡→回主選單（GameOver→Menu） | 背景反向快退至 0，1 秒；完成後 Menu 慢速捲動（Ambient）；鳥與水管隱藏，僅見背景倒回 | 待填 |
| D3 | 回 Menu 過渡中按「開始」 | 過渡不卡死，重新錨定當前位置續跑至 0，接正常捲動；鳥恢復顯示、水管重生 | 待填 |
| D4 | 開始過渡中按 P 暫停 | 背景在 Pause 期間仍捲動至完成（m2 現象），恢復後正常 | 待填 |
| D5 | 回 Menu 後鳥/水管不可見；開始後可見 | 鳥 SetActive(false) 隱藏；水管釋放回池；StartGame 後鳥顯示、水管重生 | 待填 |
| D6 | 回 Menu 後 Menu 最高分顯示新值 | 與 GameOver 結算一致（NFR-03） | 待填 |

## Statistics
- blocker: 0 / major: 1 / minor: 4