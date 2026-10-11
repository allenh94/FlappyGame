# Game States／主選單／暫停／結算架構

> 本文件由 tech-lead-agent 維護（Phase 2 產出），其他 agent 唯讀。
> 需求真相來源：`specs/requirements/game-states.md`。本文引用其條目編號作為裁定依據。

## 0. 模組總覽（Class Diagram）

```
┌─ GameManager（Assets/Scripts/GameManager.cs，核心改造）──────────────┐
│  enum GameState { Menu, Ready, Playing, Dying, Paused, GameOver }   │
│  private GameState state;                                           │
│  private void SetState(GameState)   // 唯一狀態變異點，fire 事件      │
│  public event Action<GameState> OnStateChanged;  // UI 訂閱          │
│  public void StartGame() / Pause() / Resume() / EndGame() /         │
│              Restart() / BackToMenu() / Die()                       │
│  public bool IsPlaying() { return state == Playing; }               │
│  public bool IsPaused() { return state == Paused; }                 │
│  public int GetScore() / GetHighScore() / GameState GetState()      │
│  Update(): Playing→查地板/天花板；Playing + Paused→查 P 鍵            │
└─────────────────────────────────────────────────────────────────────┘
   │ 單向呼叫
   ├─▶ PlayerController.Reset()（StartGame）
   ├─▶ PipeSpawner.ResetAll()（StartGame／BackToMenu）
   ├─▶ BGManager.SetNormal()/SetAmbient()/SetFrozen(bool)（SetState 依 GameState）
   ├─▶ BGManager.ScrollBackToStart()（StartGame）／ScrollBackToMenu()（BackToMenu）
   ├─▶ HidePlayerAndPipes()（BackToMenu：鳥 Renderer.enabled 隱藏＋pipeSpawner.ResetAll()）
   └─▶ player.Die()（Die）

┌─ Gameplay 系統（各自查 IsPlaying()）────────────────────────────────┐
│  PlayerController.Update：if (!gm.IsPlaying()) return;              │
│  PipeSpawner.Update：if (!gm.IsPlaying()) return;                   │
│  PipeController.Update：if (!gm.IsPlaying()) return;                │
└─────────────────────────────────────────────────────────────────────┘

┌─ 背景系統（GameManager→BGManager 單向凍結，B8）─────────────────────┐
│  BGManager.SetFrozen(bool) → 各 BGLayer.SetFrozen(bool)             │
│  BGLayer.Update：if (frozen) return;                                │
└─────────────────────────────────────────────────────────────────────┘

┌─ UI 系統（Assets/Scripts/UI/）──────────────────────────────────────┐
│  UIController（掛持久根，訂閱 GameManager.OnStateChanged）           │
│    · 依 GameState 切換 MenuCanvas / OverlayCanvas 顯示              │
│    · 按鈕 → GameManager 公開方法                                    │
│    · LeaderboardScreen 顯示旗標（menuBrowseMode/gameOverQualifyMode）│
│  MenuCanvas（Menu 態：標題/開始/排行榜按鈕——最高分文字已退休，FR-12） │
│  OverlayCanvas（Paused/GameOver 態：單面板雙態）                     │
│  LeaderboardScreen（獨立 Canvas，非 GameState——Menu 瀏覽/GameOver 上榜）│
└─────────────────────────────────────────────────────────────────────┘
```

## 1. 狀態機（D1，Ready 過渡態見 ADR-012；Dying 死亡態見 dying-state）

```
enum GameState { Menu, Ready, Playing, Dying, Paused, GameOver }

轉移（FR-03）：
  Menu ──StartGame()──▶ Ready ──倒帶完成──▶ Playing
  Playing ──Pause()──▶ Paused
  Playing ──Die()──▶ Dying ──淡出完成（OnDeathFadeComplete）──▶ GameOver（EndGame 由 Dying 觸發）
  Paused ──Resume()──▶ Playing
  Paused ──EndGame()──▶ GameOver
  GameOver ──Restart()──▶ Ready ──倒帶完成──▶ Playing（Restart 內部呼叫 StartGame()）
  GameOver ──BackToMenu()──▶ Menu
```

- **SetState(GameState) 為唯一狀態變異點**：所有轉移（含 StartGame/Pause/Resume/EndGame/Restart/BackToMenu/Die）一律經它設欄位＋fire `OnStateChanged`。禁止任何方法直接寫 `state` 欄位。
- **Ready 過渡態（ADR-012）**：`StartGame()` 流程＝`scoreSession.Begin()` → `SetState(Ready)` → `HidePlayerAndPipes()` → `backgroundManager.ScrollBackToStart(onComplete: () => { ShowPlayerAtStart(); SetState(Playing); })`。`ShowPlayerAtStart()` 與 `SetState(Playing)` 同幀連續（FR-05），中間無 gameplay 窗口。重入擋＝`if (state == Playing || state == Ready) return;`（FR-02，防倒帶期間重複呼叫）。
- **Dying 死亡態（dying-state）**：`Die()` 由「`isDying = true`」改 `SetState(Dying)`——死亡瞬間 `IsPlaying()==false`，水管/生成/計分自查守衛自然凍結；背景靠 `case Dying: SetFrozen(true)` 立即凍結。死亡流程事件鏈不變：Die → PlayDissasemble → Dissasemble 播完（state-query）→ 內部啟動淡出 → OnDeathFadeComplete → EndGame。**重入防護由 state 承擔**：`Die()` 首行 `if (state != Playing) return;`——state==Dying 時直接 return（BC-3）。
- **SetState 背景 switch**：`case Playing: SetNormal()`、`case Menu: SetAmbient()`、`case Ready: break;`（**不凍結**，背景處 ScrollingBack 持續倒帶；防禦性修正，見 ADR-012）、`case Dying: SetFrozen(true)`（死亡瞬間背景立即凍結，顯式 case）、`default: SetFrozen(true)`（Paused/GameOver）。
- **EndGame() 是 GameOver 的唯一入口**：`Die()`（撞死）與暫停「結束」按鈕皆收斂到 EndGame()。`Die()` 為薄包裝：守衛（僅 Playing）→ `SetState(Dying)` → `player.Die()` → `PlayDissasemble()`。EndGame() 守衛（**Dying/Paused**）→ SetState(GameOver) → 寫最高分（T5 掛鉤）→ fire 事件。此設計滿足 FR-07「死亡與暫停→結束同路徑」。
- **Update() 結構**（順序關鍵，BC-1 守衛）：
  ```
  if (state == Playing) {
      if (player.transform.position.y <= floorY || >= ceilingY) Die();
  }
  if (state == Playing || state == Paused) {
      if (Keyboard.current.pKey.wasPressedThisFrame) {
          if (state == Playing) Pause(); else Resume();
      }
  }
  ```
  地板/天花板檢查**先於** P 鍵檢查：同幀死亡＋按 P 時，Die() 先置 state=Dying，P 檢查因 state 非 Playing/Paused 而跳過——不誤觸暫停。Ready/Dying 期間兩者皆不執行（Ready/Dying 非 Playing/Paused）。**H1 守衛簡化**：原 `state == Playing && !isDying` 的 `!isDying` 隨旗標移除——`state == Playing` 已排除 Dying，死亡流程期間不會每幀重觸發 Die()。
- **IsPlaying()** 回傳 `state == Playing`（NFR-01 保持，ScoreTrigger/CollisionHandler 相容）。**Ready/Dying 期間 false**——正是本修正的關鍵（死亡期間各系統守衛生效）。**IsPaused()** 回傳 `state == Paused`（dying-state 新增，BlobAnimator 淡出守衛用，語義＝非暫停即可淡出）。

## 2. 凍結機制（D3）

非 Playing 態（Menu/Ready/Dying/Paused/GameOver）凍結全部 gameplay 系統。**不使用 SetActive 停用、不使用 Time.timeScale=0**（理由見 ADR-006 D3）。**Ready 為例外**：gameplay 系統因 `IsPlaying()==false` 自查凍結，但背景**不**凍結（ScrollingBack 持續倒帶，見 ADR-012）。**Dying 為新增凍結態**：gameplay 系統因 `IsPlaying()==false` 自查凍結，背景靠 `case Dying: SetFrozen(true)` 立即凍結（dying-state）。

| 系統 | 凍結方式 | 守衛 |
|------|---------|------|
| PlayerController | 自查 `IsPlaying()` | `Update()` 首行 `if (!GameStateAuthority.IsPlaying()) return;`（含 HandleInput，防暫停/Ready/Dying 時 flap） |
| PipeSpawner | 自查 `IsPlaying()` | `Update()` 首行 return（不生成水管；Ready 期間 seed=0 保留；Dying 期間不生成） |
| PipeController | 自查 `IsPlaying()` | `Update()` 首行 return（不移動；Dying 期間水管凍結） |
| BGLayer | GameManager→BGManager 單向 | `BGManager.SetFrozen(bool)` → 各 `BGLayer.SetFrozen(bool)`；`Update()` 首行 `if (frozen) return;`。**Ready 期間不呼叫 SetFrozen**（`case Ready: break;`），背景處 ScrollingBack motion 持續倒帶（ScrollingBack 優先於 frozen，`BGLayer.cs:143-163`）；**Dying 期間 `case Dying: SetFrozen(true)`**（死亡瞬間背景立即凍結） |

- **背景不得自查 GameManager**（B8：BGManager 不得反向引用 GameManager）。凍結由 GameManager 呼叫 `BGManager.SetFrozen(bool)` 達成，維持單向接線。
- **GameManager 引用模式**：Player/PipeSpawner/PipeController 比照 ScoreTrigger（`[SerializeField] GameManager`＋`Start()` 內 `FindObjectOfType` fallback）。PipeController 為池化物件，於 Awake/Start 解析一次即可。
- **凍結閘語義**：一律用 `IsPlaying()`（= state==Playing）。非 Playing 即凍結 gameplay 系統，涵蓋 Menu/Ready/**Dying**/Paused/GameOver 五態（Ready 期間 gameplay 凍結、背景例外不凍結，見 ADR-012；Dying 期間 gameplay 與背景全凍結），無需另設 IsFrozen()。
- **Menu 隱藏鳥（非凍結，ADR-007）**：回 Menu 隱藏 Bird 是產品行為（隱藏渲染），非凍結機制。用 `GetComponentsInChildren<Renderer>().enabled = false` 隱藏，**不用 SetActive 停用**（G2 維持不放寬）；StartGame 以 `GetComponentsInChildren<Renderer>().enabled = true`＋`player.Reset()` 恢復。水管走 `pipeSpawner.ResetAll()` 池回收。

## 3. UI 子系統（D4-D6）

- **目錄**：`Assets/Scripts/UI/`（新系統目錄，符合 CONTEXT.md「新系統腳本各立子資料夾」慣例）。
- **UIController.cs**：掛**持久根**（GameManager 根或專屬 "UI" 根），**不掛會被停用的 Canvas**。訂閱 `GameManager.OnStateChanged`，依 GameState 切換：
  - Menu → 顯示 MenuCanvas、隱藏 OverlayCanvas
  - Ready → 隱藏兩者（淨空，保留 HUD ScoreText；同 Playing case，ADR-012 FR-06）
  - Playing → 隱藏兩者
  - **Dying → 隱藏兩者**（淨空，保留 HUD ScoreText；同 Ready/Playing case，dying-state FR-09——死亡淡出期間畫面保持乾淨，GameOver 面板延後到 EndGame 才出現）
  - Paused → 顯示 OverlayCanvas（繼續/結束面板）
  - GameOver → 顯示 OverlayCanvas（重新開始/回主選單＋分數；最高分文字已退休，FR-12；上榜時 LeaderboardScreen 取代 GameOverPane，決策 C=α）
- **接線（NFR-05）**：
  - UI→GameManager：公開方法（StartGame/Pause/Resume/EndGame/Restart/BackToMenu/GetScore/GetHighScore）。
  - GameManager→UI：`OnStateChanged` 事件（C# event，`public event Action<GameState>`）。UI 不得直接操作內部狀態。
- **Canvas 分離**（D6）：MenuCanvas（標題/開始/排行榜按鈕——最高分文字已退休，FR-12）與 OverlayCanvas（暫停/終局單面板雙態）**分離**。UI 控制腳本整 Canvas 停用（SetActive 於 Canvas 無物理副作用）。兩者沿用 ScoreCanvas 慣例（Screen Space-Overlay、Constant Pixel Size、reference 800×600）。
- **LeaderboardScreen（非 GameState，ADR-015 決策 7）**：獨立 Canvas（非 MenuCanvas/OverlayCanvas 子物件），於 Menu／GameOver 兩態內顯示，**不新增 GameState**（NFR-01）。顯示/隱藏由 UIController 顯示旗標管理（menuBrowseMode／gameOverQualifyMode），**不經 OnStateChanged 的 state 切換**——Menu 態點排行榜按鈕時 state 維持 Menu（不走 OnStateChanged）。隱藏掛 OnStateChanged 開頭（StopPanelTweens 旁）＋返回鈕事件；離開路徑（返回鈕/BackToMenu/Restart）呼叫 `LeaderboardAnimator.StopAll()` 中斷歸位（BC-5，LB8）。凍結政策相容：Menu/GameOver 態 gameplay 已凍結（IsPlaying()==false），LeaderboardScreen 顯示不影響 IsPlaying() 語義。
- **EventSystem**：場景新增一個 EventSystem（選單按鈕必須；EventSystem 全域，非 per-Canvas）。

## 6. Developer 實作切入點

1. T1 先改 GameManager：enum＋SetState＋OnStateChanged＋公開方法；`Start()` 設 Menu 不呼叫 StartGame；`Die()` 移除 Invoke 並轉呼叫 EndGame()。
2. T2 凍結：Player/PipeSpawner/PipeController 各加 `IsPlaying()` 守衛；BGManager 加 SetFrozen 傳播至 BGLayer。
3. T3/T4 場景：新增 EventSystem＋MenuCanvas＋OverlayCanvas（沿用 ScoreCanvas 慣例）；UIController 掛持久根訂閱事件。
4. T5 最高分：EndGame() 內 PlayerPrefs 寫入（key `flipbird_high_score`）；GetHighScore() 供 UI 讀取。
5. T6 P 鍵：GameManager.Update 內 `Keyboard.current.pKey.wasPressedThisFrame`（已實證存在）。
6. T7/T8 字型與 9-slice 素材：asset-developer 職責，不影響本架構。

## 7. Ready 過渡態實作切入點（ready-transition，ADR-012）

1. **T1（gameplay-developer）GameManager**：enum 加 `Ready`（`{ Menu, Ready, Playing, Paused, GameOver }`）；`StartGame()` 重入擋改 `if (state == Playing || state == Ready) return;`；`StartGame()` 新流程＝`scoreSession.Begin()` → `SetState(Ready)` → `HidePlayerAndPipes()` → `backgroundManager.ScrollBackToStart(onComplete: () => { ShowPlayerAtStart(); SetState(Playing); })`；`SetState` 背景 switch 加 `case GameState.Ready: break;`（不落入 default `SetFrozen(true)`）。
2. **T2（scene-developer）UIController**：`OnStateChanged` switch 加 `case GameManager.GameState.Ready:` → `SetActive(menuCanvas,false); SetActive(overlayCanvas,false)`（同 Playing case）。HUD 由 `OnScoreChanged` 事件驅動（`Begin()` fire 0），不需硬寫。
3. **T3（gameplay-developer）PipeSpawner**：`Update` 計時改倒數式（`spawnTimer -= dt; if (spawnTimer <= 0) { SpawnPipe(); spawnTimer = spawnInterval; }`）；`ResetAll()` seed `spawnTimer = 0`。純計時語意，不與 ADR-010 池化衝突。

## 8. Dying 死亡態實作切入點（dying-state，ADR-013 決策 8 翻轉）

1. **T1（gameplay-developer）GameManager**：enum 加 `Dying`（`{ Menu, Ready, Playing, Dying, Paused, GameOver }`）；`Die()` 由「`isDying = true`」改 `SetState(GameState.Dying)`（`if (state != Playing) return;` 保留為重入防護）；`SetState` 背景 switch 加 `case GameState.Dying: backgroundManager.SetFrozen(true); break;`；`EndGame()` 守衛改 `if (state != GameState.Dying && state != GameState.Paused) return;`；**刪除 `isDying` 欄位與全部使用點**（`:22` 欄位、`:89` 改回純 `state == Playing`、`:138` 刪除重設、`:209-210` 由 SetState(Dying) 取代）；`:87-88` 過期註解更新。**不改**：`IsPlaying()`（嚴格 `state == Playing`）、`StartGame()`/`BackToMenu()`/`Restart()`/`Pause()`/`Resume()` 流程。
2. **T2（gameplay-developer）BlobAnimator＋GameStateAuthority**：`BlobAnimator.cs:144` speed 改 `(lastState == Playing || lastState == Dying || isAssembling) ? 1f : 0f`；`BlobAnimator.cs:152` 淡出守衛改 `if (GameStateAuthority.IsPaused()) return;`；`GameStateAuthority` 新增 `IsPaused()`（`instance != null && instance.IsPaused()`）；`GameManager` 新增 `public bool IsPaused() { return state == GameState.Paused; }`。
3. **T3（scene-developer）UIController**：`OnStateChanged` switch 加 `case GameManager.GameState.Dying:` → `SetActive(menuCanvas,false); SetActive(overlayCanvas,false)`（同 Ready/Playing case；HUD ScoreText 屬獨立 ScoreCanvas 恆可見）。