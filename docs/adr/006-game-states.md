# ADR-006: GameState 狀態機＋凍結機制＋UI 子系統

## Status

Accepted（D1 狀態機已由 ADR-012 擴張：enum 加 `Ready` 過渡態、Menu/GameOver→Playing 轉移改經 Ready。本 ADR 的 SetState 單一變異點／IsPlaying 語義／凍結政策原則不變。）

> **2026-09-30 擴張註記（dying-state）**：D1 狀態機再擴張——enum 加 `Dying` 死亡態（`{ Menu, Ready, Playing, Dying, Paused, GameOver }`，翻轉 ADR-013 決策 8 的「否決新增 GameState.Dying」）。D3 凍結政策加註：**Dying 為新增凍結態**——gameplay 系統因 `IsPlaying()==false` 自查凍結，背景靠 `SetState` 的 `case Dying: SetFrozen(true)` 立即凍結。SetState 單一變異點／IsPlaying 語義（嚴格 `state == Playing`）／凍結政策原則（各系統自查＋背景單向）不變。詳見 `specs/architecture/game-states.md` §1/§2/§8 與 ADR-013 決策 8 修訂。

## Context

## Context

現行 `GameManager.cs` 以單一 `isPlaying` bool 為唯一狀態源：`Start()` 直入 `StartGame()`、`Die()` 用 `Invoke(nameof(StartGame), 0.1f)` 自動重生、`Update()` 僅在 `isPlaying` 時檢查地板/天花板。需求（specs/requirements/game-states.md）要求升級為完整狀態機（Menu/Playing/Paused/GameOver）＋主選單／暫停／終局三種 UI＋最高分持久化，且必須整合既有驗收成果（pipe-score 的 ScoreTrigger 唯一計分、CollisionHandler 死亡、背景 BGManager）而非重建。T1（狀態機）、T2（凍結機制）、T3/T4（UI 子系統）觸發架構變更。

## Decision

1. **GameState 狀態機**（D1）：`enum GameState { Menu, Playing, Paused, GameOver }` 取代 `isPlaying`。所有轉移一律經私有 `SetState(GameState)`——單一狀態變異點，負責設欄位＋fire `OnStateChanged` 事件。`IsPlaying()` 回傳 `state == Playing`（NFR-01 語義保持，ScoreTrigger/CollisionHandler 相容）。`Update()` 結構：僅 Playing 態查地板/天花板；Playing＋Paused 態查 P 鍵；**地板/天花板檢查先於 P 鍵檢查**（同幀死亡＋按 P 時，Die() 先將 state 置 GameOver，P 檢查因 state 非 Playing/Paused 而跳過——BC-1 守衛）。

2. **P 鍵歸屬 GameManager**（D2）：暫停是遊戲流程輸入，屬狀態機擁有者，非鳥移動輸入。`GameManager.Update()` 在 Playing（→Pause）與 Paused（→Resume）態偵測 `Keyboard.current.pKey.wasPressedThisFrame`（已 unity_reflect 實證 `pKey` 存在）；Menu/GameOver 忽略。PlayerController 只管 flap（space），且非 Playing 態跳過 HandleInput（T2），兩鍵互不干擾（NFR-04）。

3. **凍結機制＝各系統自查＋背景單向**（D3）：非 Playing 態凍結全部 gameplay 系統。
   - Player/PipeSpawner/PipeController：`Update()` 開頭 `if (!gameManager.IsPlaying()) return;`（各系統自查，比照 ScoreTrigger 的 GameManager 引用模式）。
   - 背景：**不得**由 BGLayer 自查 GameManager（違反 B8「BGManager 不得反向引用 GameManager」）。改由 GameManager → `BGManager.SetFrozen(bool)` → 各 `BGLayer.SetFrozen(bool)`，BGLayer.Update 首行 `if (frozen) return;`。維持 B8 單向接線。
   - **否決 SetActive(false) 統一停用**：副作用不可控——停用 PipeSpawner 會連帶停用其子水管（觸發 ScoreTrigger.OnDisable 重置 scored、PipeController 等）；停用 Player 會隱形鳥＋停用 Collider；停用 BG 會停用全部 tile。SetActive 是「停用物件」而非「凍結模擬」，語意不符。
   - **否決 Time.timeScale=0**：手動重力（PlayerController 手動積分）與 timeScale 硬碰硬；且 timeScale=0 會連 UI 一起凍結（暫停選單必須可互動）。規格「Invoke 不受 timeScale 控制」說法不精確（Invoke 用 Time.time 受 timeScale 影響），但 FR-04 已移除 Invoke，此點無實際影響。

4. **UI 子系統**（D4-D6）：`Assets/Scripts/UI/`（新系統目錄，符合 CONTEXT.md 慣例）。
   - D4：UI 控制腳本 `UIController.cs` 掛**持久根**（GameManager 根或專屬 "UI" 根），**不掛會被停用的 Canvas**；訂閱 `GameManager.OnStateChanged`，依 GameState 切換 MenuCanvas／OverlayCanvas 顯示。
   - D5：接線＝GameManager 公開方法（UI→GameManager：StartGame/Pause/Resume/EndGame/Restart/BackToMenu/GetScore/GetHighScore）＋ `OnStateChanged` 事件（GameManager→UI）。UI 不得觸碰內部狀態（NFR-05 成立）。
   - D6：MenuCanvas 與 OverlayCanvas **分離**（否決併入單一 Canvas）。Menu 是獨立畫面（標題/開始/最高分）；Overlay 是暫停/終局單面板雙態。分離使 UI 控制腳本可整 Canvas 停用（SetActive 於 Canvas 無物理副作用），且兩者生命週期/內容不同。

## Consequences

- 正面：狀態機單一變更點（SetState）＋單一通知點（OnStateChanged），UI 與 gameplay 皆以 GameManager 公開 API 接線，低耦合（NFR-05）。
- 正面：凍結機制分散於各 gameplay 系統自查，無 SetActive 副作用；背景以 B8 單向凍結，不破壞背景低耦合。
- 正面：IsPlaying() 語義保持，pipe-score 驗收成果不回歸（NFR-01）。
- 負面：各 gameplay 系統新增 GameManager 引用（Player/PipeSpawner/PipeController 各一），耦合成本可接受（ScoreTrigger/CollisionHandler 已同模式）。
- 負面：背景新增 SetFrozen 介面（GameManager→BGManager），屬既有 B8 單向接線的自然延伸。
- 被放棄的替代方案：SetActive 統一停用（副作用不可控）、Time.timeScale=0（凍結 UI＋手動重力衝突）、BGLayer 自查 GameManager（違反 B8）、UI 併入單一 Canvas（生命週期/內容混雜）、UI 輪詢 GameManager state（每幀浪費，事件更乾淨）。

## 對 pm 規格的修訂建議

見回報清單——不直接改 `specs/requirements/`（pm 領地）。