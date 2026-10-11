# ADR-007: Menu 隱藏玩家用 Renderer.enabled，G2 維持不放寬

## Status

Accepted

> **2026-10-01 相容性確認（pipe-sprite-variants）**：水管 BottomPipe/TopPipe 由 MeshRenderer 遷移 SpriteRenderer（ADR-014），`GetComponentsInChildren<Renderer>()` 涵蓋 SpriteRenderer 的實證再次適用（鳥遷移 ADR-013 決策 9 已先例）——Menu 隱藏機制零改動。

## Context

`BackToMenu()`（GameOver→Menu）需隱藏 Bird 與水管，Menu 只留背景倒回動畫。Validator 在 `specs/validation/game-states/round-2.md` 標 M1：`GameManager.cs` 的 `HidePlayerAndPipes()` 以 `player.gameObject.SetActive(false)` 停用玩家物件，與 `dependency-rules.md` G2「禁 SetActive 停用」衝突（灰色地帶）。

G2 的禁令針對**凍結機制**：非 Playing 態凍結 gameplay 系統時，不得用 SetActive 停用（ADR-006 D3 否決理由＝副作用不可控——停用 PipeSpawner 連帶停用子水管→觸發 ScoreTrigger.OnDisable 重置 scored；停用 Player 隱形鳥＋停用 Collider；停用 BG 停用全部 tile）。SetActive 是「停用物件」而非「凍結模擬」，語意不符。

「回 Menu 隱藏鳥」是**產品行為**（派工明列），非凍結機制本身（凍結仍靠 `IsPlaying()`／`SetFrozen` 正確運作）。但規則文字絕對（「禁 SetActive 停用」），且玩家確以 SetActive 被停用，屬規則灰色地帶。

## Decision

**不放寬 G2。** 改用 `Renderer.enabled` 隱藏鳥，取代 `player.gameObject.SetActive(false)`。

- 隱藏：`GetComponentsInChildren<Renderer>().enabled = false`（只隱藏渲染，不碰 collider/physics/stat）。
- 恢復：`StartGame()` 內 `GetComponentsInChildren<Renderer>().enabled = true`（取代 `SetActive(true)`）＋既有 `player.Reset()` 完整重置。
- 水管維持 `pipeSpawner.ResetAll()`（池回收 SetActive，非凍結機制，不違 G2）。

**合規性理由**：

1. G2 禁令針對「凍結機制用 SetActive 停用 gameplay 物件」；`Renderer.enabled` 只改渲染，不觸發 OnDisable/OnEnable、不停用 collider、不影響 physics/stat——無 ADR-006 D3 擔憂的副作用。
2. `MeshRenderer.enabled` 與 `SpriteRenderer.enabled` 皆由共同基類 `UnityEngine.Renderer` 宣告（已實證：declaredBy=Renderer，兩者皆 inherit Renderer）——對未來 spritesheet 動畫（多子 SpriteRenderer）通用，`GetComponentsInChildren<Renderer>()` 一次涵蓋。
3. 水管走池回收（ResetAll）本不違規——池回收的 SetActive 是生命週期管理，非凍結機制。

## Consequences

- 正面：G2 維持不放寬，凍結機制規則語意純淨（SetActive 僅保留於池回收等生命週期用途）。
- 正面：Menu 隱藏鳥不觸發 collider 副作用（ScoreTrigger/CollisionHandler 等不受影響）。
- 正面：`Renderer.enabled` 對未來 spritesheet 動畫（多子 SpriteRenderer）通用。
- 負面：GameManager 需以 `GetComponentsInChildren<Renderer>()` 隱藏/恢復鳥（一次子物件遍歷，僅狀態切換時呼叫，成本可忽略）。
- 被放棄的替代方案：放寬 G2 允許 Menu 隱藏用 SetActive（規則文字被稀釋，且 SetActive 副作用仍存在）。

## 對 pm 規格的修訂建議

見回報清單——不直接改 `specs/requirements/`（pm 領地）。