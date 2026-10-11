# Gameplay Architecture

> 本文件由 tech-lead-agent 維護，其他 agent 唯讀。
> 核心機制架構——飛行物理、碰撞、得分、輸入、遊戲狀態機的職責劃分與事件流。
> 依賴規則見 `dependency-rules.md`；決策紀錄見 `docs/adr/`（ADR-002/003/006/007/008/009/010/011）。

## 模組職責劃分

| 模組 | 職責 | 介面（呼叫端需學的） | 深度 |
|------|------|----------------------|------|
| **GameManager** | 狀態機權威（Menu/Ready/Playing/Dying/Paused/GameOver）＋編排；流程命令入口；背景單向驅動 | `StartGame/Pause/Resume/EndGame/BackToMenu/Restart/Die`、`IsPlaying/IsPaused/GetGameState`、`OnStateChanged` 事件、`Score` accessor | 深（狀態編排隱藏） |
| **ScoreSession**（`Score/`） | 得分／最高分／破紀錄判定／PlayerPrefs 持久化 | `Begin/AddPoint/End→bool`＋唯讀 accessor | 深（ADR-008） |
| **GameStateAuthority** | 狀態查詢唯一 seam（輪詢） | `IsPlaying()`／`IsPaused()`（static） | 薄 adapter（ADR-011） |
| **PlayerController** | 鳥移動（flap／重力／旋轉）＋可見性 | `Flap`（內部）、`Die`、`Reset`、`SetVisible` | 深 |
| **BlobAnimator**（ADR-013） | 鳥動畫控制層（命令＋事件＋暫停＋死亡淡出） | `PlayAssemble/PlayJump/PlayLand/PlayDissasemble`、`OnJumpRise/OnXxxComplete` | 中（四層分離控制層） |
| **PipeController**（ADR-009） | 水管對移動＋間隙 | `Configure(center, gapSize)` | 深（介面收斂） |
| **PipeVariant**（ADR-014） | 水管變體套用（隨機 sprite＋collider 同步＋scale） | `ApplyRandom(scale)` | 中（sprite 驅動 collider） |
| **PipeSpawner** | 水管生成／回收（池） | `ResetAll` | 中 |
| **ScoreTrigger** | 通過計分（唯一計分點） | 無公開介面（碰撞觸發） | 淺 |
| **CollisionHandler** | 鳥撞管死亡命令 | 無公開介面（碰撞觸發） | 淺 |
| **PooledActives&lt;T&gt;**（ADR-010） | 池週期薄 adapter（active-list＋防護） | `Get/Release/ReleaseAll/Active` | 薄 adapter |
| **LeaderboardProvider**（`Leaderboard/`，ADR-015） | 榜單讀寫統一接口（async callback）＋Qualify 判定純函式 | `LoadAll/Submit`、`LeaderboardRules.Qualifies` | 深（持久化隔離） |
| **LocalLeaderboardProvider**（`Leaderboard/`，ADR-015） | PlayerPrefs JSON 榜單實作（key `flipbird_leaderboard`）＋corrupt 防護 | 無公開介面（實作 LeaderboardProvider） | 深（內部 adapter） |
| **PlayerNameStore**（`Leaderboard/`，ADR-015） | 玩家名稱記憶 seam（key `flipbird_player_name`） | `Load/Save` | 薄 adapter |
| **LeaderboardAnimator**（`UI/`，ADR-015） | 榜單列渲染＋Qualify 演出（滑入/滾分/重排/中斷歸位） | `Render/PlayQualify/StopAll` | 中（演出封閉） |

## 事件流與資料流

### 狀態機（GameManager 唯一真源，ADR-006；Ready 見 ADR-012；Dying 見 dying-state）

```
Menu ──StartGame──▶ Ready ──倒帶完成──▶ Playing ──Pause──▶ Paused ──Resume──▶ Playing
  ▲                    │  │
  │                    │  └──Die──▶ Dying ──淡出完成──▶ GameOver ──Restart──▶ Ready
  └──BackToMenu────────┴───────────────┘        └──BackToMenu──▶ Menu
```

- 所有轉移經私有 `SetState()`（單一變異點）：設欄位 → 同步背景（SetNormal/SetAmbient/SetFrozen）→ fire `OnStateChanged`。
- `OnStateChanged` 目前**僅 UI 訂閱**（ADR-011 決策：不升級事件匯流排；BlobAnimator 為第二訂閱者，ADR-013 決策 6 裁決維持 plain event）。

### 凍結機制（ADR-006 D3，ADR-011 收 wiring）

- **各 gameplay 系統自查**：Player／PipeController／PipeSpawner／ScoreTrigger 於 Update 開頭 `if (!GameStateAuthority.IsPlaying()) return;`（輪詢，非事件訂閱）。**Dying 態 `IsPlaying()==false`**——死亡瞬間水管/生成/計分自然凍結（dying-state）。
- **背景**：GameManager → `BGManager.SetFrozen(bool)` → 各 `BGLayer.SetFrozen(bool)`（B8 單向，BGLayer 不反向引用 GameManager）。**`case Dying: SetFrozen(true)`**——死亡瞬間背景立即凍結。
- 禁 SetActive 停用 gameplay 物件、禁 Time.timeScale=0（G2）。

### 得分流（ADR-008）

```
ScoreTrigger.OnTriggerEnter2D ──▶ gameManager.Score.AddPoint()
GameManager.StartGame ──▶ Score.Begin()
GameManager.EndGame ──▶ Score.End() → bool recordBroken（快照→計算→覆寫，封閉在內）
UI ──▶ gameManager.Score.CurrentScore / HighScore / PreviousHighScore / RecordBroken
```

### 死亡流（ADR-013：Die→Dissasemble→淡出→EndGame；2026-09-30：Dying 態取代旗標）

```
CollisionHandler.OnTriggerEnter2D（Pipe tag）──▶ gameManager.Die()
GameManager.Update（地板/天花板）──▶ Die()（同入口）
  └─▶ if Playing → SetState(Dying) → player.Die()（凍結＋停 collider）
      → blobAnimator.PlayDissasemble()
  └─▶ BlobAnimator.OnDissasembleComplete → FadeOut()（alpha→0，IsPaused gate）
  └─▶ BlobAnimator.OnDeathFadeComplete → EndGame() → Score.End() → SetState(GameOver)
死亡流程期間進入 Dying 態（水管/生成/背景全凍結）；EndGame 由 Dying 觸發（FR-16/FR-17 撤銷）
```

### 池化流（ADR-010）

```
PipeSpawner ──▶ PooledActives<PipeController>.Get()/Release()/ReleaseAll()
BGLayer     ──▶ PooledActives<GameObject>.Get()/Release()/ReleaseAll()
  └─ createFunc 由呼叫端提供（pipe prefab / tile template）；池週期統一在 PooledActives
```

### 水管變體流（ADR-014）

```
PipeSpawner ──▶ PipeController.Configure(position, gapSize)（ADR-009 單一接口，零改動）
  └─▶ PipeVariant.ApplyRandom(Random.Range(minScale, maxScale))（Configure 內觸發，PV2 單向）
      ├─▶ SpriteRenderer.sprite = 隨機 variant（上下管同 variant，FR-02）
      ├─▶ PolygonCollider2D path = sprite physics shape（或 bounds fallback，雙軌）
      └─▶ BottomPipe/TopPipe localScale = scale（不套 root，gap 固定）
  └─ 每次 ApplyRandom 完整重置（pathCount 重設＋重 SetPath；ADR-010 pool 重用，PV3）
```

### 排行榜流（ADR-015）

```
[GameOver 上榜流程]（OnStateChanged(GameOver) 內，本地同步回呼）
UIController ──▶ gameManager.Leaderboard.LoadAll(cb)（FR-13）
  └─▶ cb(entries)：LeaderboardRules.Qualifies(CurrentScore, entries)（LB6 單一事實來源）
      ├─▶ 上榜：顯示 LeaderboardScreen（上榜模式，取代 GameOverPane，決策 C=α）
      │        ＋名稱 modal（決策 A=a：gameManager.PlayerName.Load() 預填）
      │        → 玩家確認 → gameManager.Leaderboard.Submit(entry, cb2)（LB2）
      │            └─▶ cb2(result)：LeaderboardAnimator.PlayQualify(result)（演出）
      └─▶ 未上榜：顯示既有 GameOverPane（High Score 文字已退休，FR-12）

[Menu 瀏覽]（決策 B=i，state 維持 Menu，不走 OnStateChanged）
MenuCanvas 排行榜按鈕 ──▶ 顯示 LeaderboardScreen（瀏覽模式，禁 Submit）
LeaderboardScreen 返回鈕 ──▶ 回 Menu 主面板

[中斷歸位]（BC-5）
返回鈕/BackToMenu/Restart/OnStateChanged 開頭 ──▶ LeaderboardAnimator.StopAll()（LB8）
```

- **依賴方向**：UI → `GameManager.Leaderboard` → Provider 單向（LB3）；Provider 不得反向引用遊戲邏輯。
- **callback 契約**：主執行緒保證；同步回呼容忍（callback 內執行顯示動作，不假設顯示已完成）；Submit callback 內禁再 LoadAll（LB2，SubmitResult.Entries 已含完整榜單）。
- **High Score 顯示退休**：`menuHighScoreText`/`gameOverHighScoreText`/`PlayHighScoreCountUp` 移除（FR-12）；ScoreSession 資料與 `flipbird_high_score` 照寫不變（NFR-02，LB1）。

## 目錄邊界（CONTEXT.md 慣例）

```
Assets/Scripts/
├── GameManager.cs / PlayerController.cs / PipeController.cs / PipeSpawner.cs / ScoreTrigger.cs / CollisionHandler.cs   （既有平鋪，未經決策不搬家）
├── Score/            ScoreSession.cs（ADR-008）
├── Pooling/          PooledActives<T>.cs（ADR-010）
├── Background/       BGConfig / BGLayer / BGManager / Tile
├── Leaderboard/      LeaderboardEntry / LeaderboardProvider / LocalLeaderboardProvider / PlayerNameStore（ADR-015）
└── UI/               UIController / UITween / ButtonHoverScale / LeaderboardAnimator（ADR-015）
```

## 關鍵不變量（回歸防護）

- **破紀錄順序**：快照舊最高分 → 計算 recordBroken → 覆寫，封閉在 `ScoreSession.End()`（S2）。
- **IsPlaying 語義** = `state == Playing`（G1），ScoreTrigger/CollisionHandler 相容。**Dying 期間 false**——死亡流程各系統自查凍結（dying-state）。
- **凍結**：各系統自查（輪詢），背景單向（B8），禁 SetActive/timeScale（G2）。
- **熱路徑零分配**：池週期 active-list 預分配 List，禁 LINQ/GC（PL3）。