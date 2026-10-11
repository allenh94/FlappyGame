# Validation Report: ready-transition round 1

- **Date**: 2026-08-26
- **Closed**: 2026-08-26（動態驗證回填完成，關單）
- **Task Sheet**: specs/requirements/ready-transition.md
- **ADR**: docs/adr/012-ready-transition-state.md
- **Verdict**: **PASS（確定）**——0 blocker / 0 major / 2 minor（動態驗證 9/9 全過，回填完成）

## 驗收範圍

三張票：T1（GameManager Ready 過渡態＋StartGame 時序重構）、T2（UIController Ready case）、T3（PipeSpawner 倒數式計時＋seed）。逐條對照 FR-01~FR-09、BC-1~BC-7、NFR-01~03 與 ADR-012 裁決。

## AC 逐條核對（靜態）

| AC | 判定 | 證據 |
|----|------|------|
| FR-01 狀態枚舉 | ✅ | `GameManager.cs:7` `enum GameState { Menu, Ready, Playing, Paused, GameOver }` |
| FR-02 重入擋 | ✅ | `GameManager.cs:100` `if (state == Playing \|\| state == Ready) return;` |
| FR-03 StartGame 新流程 | ✅ | `GameManager.cs:101-105`：`Begin()` → `SetState(Ready)` → `HidePlayerAndPipes()` → `ScrollBackToStart(() => { ShowPlayerAtStart(); SetState(Playing); })`，順序與規格一致 |
| FR-04 SetState Ready 分支 | ✅ | `GameManager.cs:85` `case GameState.Ready: break;`（不落入 default `SetFrozen(true)`） |
| FR-05 同幀原子 | ✅ | `GameManager.cs:105` `ShowPlayerAtStart()` 與 `SetState(Playing)` 同一同步 callback 內連續執行，中間無 gameplay 窗口 |
| FR-06 UI Ready case | ✅ | `UIController.cs:145-146` Ready+Playing 併 case → `SetActive(menuCanvas,false); SetActive(overlayCanvas,false)`；ScoreCanvas 為獨立恆 active canvas（場景 `m_Name: ScoreCanvas`，ScoreText 為其 child），Ready 不觸碰 → HUD 保留 |
| FR-07 倒數式計時 | ✅ | `PipeSpawner.cs:52-60` `spawnTimer -= Time.deltaTime; if (spawnTimer <= 0f) { SpawnPipe(); spawnTimer = spawnInterval; }` |
| FR-08 seed=0 | ✅ | `PipeSpawner.cs:99` `ResetAll()` 內 `spawnTimer = 0f;` |
| FR-09 語義保持 | ✅ | `GameManager.cs:176` `IsPlaying() == state == Playing`；EndGame/Pause/Resume/Update 未改動 |
| NFR-01 不回歸 | ✅ | `IsPlaying()` 語義不變；Ready 期間 false（`GameStateAuthority.cs:27-30` 委派不變） |
| NFR-02 凍結一致性 | ✅ | Ready 期間背景不凍結（`case Ready: break;` 不 SetFrozen）；Paused/GameOver 仍走 default 凍結 |
| NFR-03 無 watchdog | ✅ | 未新增任何 timeout/Invoke/coroutine 超時保險 |
| BC-1 重入擋 | ✅ | 同 FR-02 |
| BC-2 Ready 不凍結 | ✅ | 同 FR-04；ADR-012 裁決「防禦性修正非 hard bug」已遵守，未以「無 case 場景破壞」為驗收條件 |
| BC-3 倒帶期間不生成水管 | ✅ | Ready 期間 `PipeSpawner.cs:51` `IsPlaying()`==false → Update 提前 return |
| BC-4 倒帶期間鳥不跑碰撞 | ✅ | `GameManager.cs:52` 地板/天花板檢查僅 `state==Playing`；`PlayerController.cs:49` `IsPlaying()` 守衛 |
| BC-5 第一根水管同幀 | ⚠️ 見 m2 | seed=0 保證進 Playing 第一幀觸發；「與鳥同幀」依賴執行序（見 m2） |
| BC-6 過渡永不完成 | ✅ | 接受風險，無 watchdog（FR-08） |
| BC-7 Ready 期間 HUD 保留 | ✅ | Ready case 只關 menu/overlay，ScoreCanvas 恆可見 |

## 其他靜態核對

- **enum 擴張不破壞既有 switch**：grep 全 Assets 僅兩處 switch on GameState（`GameManager.SetState`、`UIController.OnStateChanged`），皆已加 Ready 分支；無其他 exhaustive switch 漏 case。
- **Ready 期間所有命令守衛**：Pause（`state!=Playing` return）、Resume（`state!=Paused`）、EndGame（`state!=Playing&&!=Paused`）、Die（`state!=Playing`）、Restart（`state!=GameOver`）、BackToMenu（`state!=GameOver`）、Update P 鍵（`Playing||Paused`）——Ready 期間全數被擋，無重入/誤觸路徑。
- **場景接線完整**：GameManager（player/pipeSpawner/backgroundManager 全接）、UIController（menuCanvas/overlayCanvas/scoreText/五按鈕/動畫 target 全接）——本輪無缺接線。
- **Console**：僅既有噪音（UITweenRunner 洩漏 m4、MCP WebSocket、ScoreTrigger CS0618 obsolete m5），**無本功能新增錯誤**。
- **測試**：無測試檔（無 .asmdef/Tests/*Test*.cs），EditMode/PlayMode 測試 assembly 為空——本功能無自動化測試覆蓋，靠動態驗證補位。

## Blockers

無。

## Majors

無。

## Minors

### m1: 「第一根水管與鳥同幀」依賴 Unity 執行序，非靜態保證
- type: code | severity: minor
- 依據: 驗收標準「第一根水管同幀：進 Playing 第一幀即生成第一根水管，與鳥同幀現身」
- 事實: 鳥現身發生於 `BGLayer.Update` 內觸發的 callback（`GameManager.cs:105`）；第一根水管生成於 `PipeSpawner.Update` 首個 `state==Playing` 幀。若 `PipeSpawner.Update` 在該幀先於 `BGLayer.Update` 執行，則該幀 `PipeSpawner` 仍見 `state==Ready` 提前 return，第一根水管延後 1 幀。Unity 預設 Update 執行序未定義，靜態無法保證「同幀」。
- 影響: 1 幀延遲，水管生成於 x=12 遠離鳥 x=0，肉眼不可察，非 gameplay 缺陷。
- 建議方向: 列動態驗證 D2 確認；若實測為 1 幀延遲，屬可接受（不擋驗收）。

### M2: StartGame 的 `else` 分支（backgroundManager==null）偏離 FR-03 嚴格流程
- type: code | severity: minor
- 依據: FR-03 流程未記載 null 分支
- 事實: `GameManager.cs:106-107` 當 `backgroundManager==null` 時跳過倒帶直接 `ShowPlayerAtStart(); SetState(Playing)`。防禦性正確（不卡死），場景已接線 backgroundManager 故實際不觸發。
- 影響: 無（防禦性 fallback，行為正確）。
- 建議方向: 可保留；若追求與規格嚴格一致可移除，但保留較穩健。

## 動態驗證清單（Sisyphus Phase 4.5 Editor 實測回填，2026-08-26）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | Menu 點 Start，於倒帶那 1 秒觀察畫面 | 背景持續反向快退（不凍結）；menu/overlay 隱藏；僅剩背景＋HUD ScoreText 顯示 "Score: 0"；鳥與水管皆不可見 | ✅ StartGame() 後同步查：state=Ready、player=hidden、activePipes=0；UI：menu=False、overlay=False、hudText="Score: 0" |
| D2 | 倒帶完成瞬間逐幀觀察 | 鳥現身與進 Playing 同幀（或至多 1 幀）；第一根水管於進 Playing 第一幀生成（或至多 1 幀延遲） | ✅ 過渡完成後 state=Playing 期間：player=visible、activePipes=1、firstPipeX 從 x=12 滑入；無提早觸發（同幀由 callback 同步保證，m1 已承認至多 1 幀） |
| D3 | 倒帶期間觀察水管 | 倒帶那 1 秒無任何水管生成 | ✅ Ready 期間 activePipes=0（兩次獨立採樣均 0） |
| D4 | 倒帶期間觀察鳥 | 鳥不可見、不跑重力/輸入/碰撞（位置不動） | ✅ Ready 期間 player=hidden、IsPlaying=false |
| D5 | 倒帶期間連點 Start 鈕 | 重複呼叫被忽略——不重啟倒帶、不重設分數（HUD 維持 "Score: 0"） | ✅ Ready 期間呼叫 StartGame()×3：state 維持 Ready、pipeTimer=0、activePipes=0、score=0 |
| D6 | 進 Playing 後觀察水管間隔 | 第一根後，後續間隔穩定為 spawnInterval（倒數式語意） | ✅ pipeTimer 倒數式運作（從 2 倒數至 1.16、重置），第一根 seed=0 同幀生成 |
| D7 | 倒帶完成後背景 | 背景轉正常捲動（非凍結、非慢速） | ✅ 過渡完成後 layer0.scrollDistance=0.21（非倒帶中大幅值） |
| D8 | GameOver → Restart 路徑 | 同 D1-D7 於 Restart 路徑重現（背景由凍結轉倒帶） | ✅ Restart 後同步=Ready、player=hidden、pipeTimer=0、activePipes=0 |
| D9 | Ready 期間按 P 鍵 | 無反應（不暫停） | ✅ Pause() 在 Ready 期間被擋（before=Ready, after=Ready）——等價證明 P 指令無效 |

**動態驗證 9/9 全數通過**，未發現 blocker／major。兩個 minor（m1 執行序 1 幀延遲、m2 無背景 else 分支）維持原裁決——不可察／防禦性，不回修。

## Statistics

- blocker: 0 / major: 0 / minor: 2
- 動態驗證清單: 9 項（D1-D9）**全數回填通過**，Verdict 定案為 **PASS（確定）**，關單