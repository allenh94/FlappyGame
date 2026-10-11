# Validation Report: game-states round 1
- **Date**: 2026-08-24
- **Task Sheet**: specs/requirements/game-states.md
- **Verdict**: **PASS** — 靜態審查全數通過（0 blocker / 0 major / 0 minor）＋動態驗證 14 項由 Sisyphus 回填完成（見文末「動態驗證回填」節），verdict 轉正式。

## 驗證範圍與方法

本輪為**靜態審查**（Validator 職責），動態驗證（Play mode 14 項）由 Sisyphus 於 Phase 4.5a 完成。靜態審查覆蓋：
1. `Assets/Scripts/GameManager.cs`（T1/T5/T6 核心）
2. `Assets/Scripts/UI/UIController.cs`（T3/T4 UI 子系統）
3. `Assets/Scenes/SampleScene.unity`（T3/T4/T7/T8 場景與素材）
4. 凍結守衛抽查：`PlayerController.cs`、`PipeSpawner.cs`、`PipeController.cs`、`Background/BGLayer.cs`、`Background/BGManager.cs`（T2）

對照錨點：`specs/requirements/game-states.md`（AC/FR/BC）＋`specs/architecture/game-states.md`（D1-D6）＋`specs/architecture/dependency-rules.md`（G1-G5）。

## 逐票驗證表

| # | 主張 | 實證 | 結果 |
|---|------|------|------|
| T1 | GameManager 導入 GameState 狀態機 | `GameManager.cs:8` enum{Menu,Playing,Paused,GameOver}；`:37` Start→SetState(Menu) 不呼叫 StartGame；`:65-73` SetState 唯一變異點＋fire OnStateChanged；`:110-115` Die() 薄包裝（守衛→player.Die()→EndGame()），**無 Invoke 自動重生**；`:132-135` IsPlaying()=state==Playing | ✅ PASS |
| T2 | 暫停凍結機制 | PlayerController.cs:41、PipeSpawner.cs:77、PipeController.cs:24 皆 `if (!gm.IsPlaying()) return;`；BGLayer.cs:133 `if (frozen) return;`＋BGManager.cs:52-56 SetFrozen 傳播；GameManager.cs:70 SetState 呼叫 SetFrozen。**無 SetActive 停用、無 Time.timeScale** | ✅ PASS |
| T3 | 主選單 UI（EventSystem＋Canvas＋按鈕） | `SampleScene.unity:272` EventSystem、`:331` InputSystemUIInputModule（非 Standalone）；MenuCanvas 存在；開始按鈕 m_Sprite→button_rectangle_flat（Grey guid 756c0a7c…） | ✅ PASS |
| T4 | Overlay 面板雙態 | `UIController.cs:85-101` Paused→pausePane、GameOver→gameOverPane 切換；`:62-66` 五按鈕接線（StartGame/Resume/EndGame/Restart/BackToMenu） | ✅ PASS |
| T5 | 最高分 PlayerPrefs | `GameManager.cs:10` key `flipbird_high_score`；`:161-169` SaveHighScore 僅 `score > savedBest` 才 SetInt；`:155-158` GetHighScore 單一真源；EndGame()（:102-107）為唯一寫入入口 | ✅ PASS |
| T6 | P 鍵暫停 | `GameManager.cs:54-61` 僅 Playing/Paused 偵測 `Keyboard.current.pKey.wasPressedThisFrame`；`:44-51` 地板/天花板檢查**先於** P 鍵（BC-1 守衛） | ✅ PASS |
| T7 | Kenney Future 字型 | `SampleScene.unity` 10 處 `m_fontAsset` → guid `75f5b1e824558a846a0a5ce424190607` → `Assets/Fonts/Kenney Future SDF.asset.meta` | ✅ PASS |
| T8 | 按鈕 9-slice 素材 | 5 處按鈕 `m_Sprite` → `button_rectangle_flat.png`（Grey guid 756c5a7c…×4、Blue guid f033fe7a…×1）；Image `m_Type: 1`（Sliced）＝9-slice | ✅ PASS |

## 架構規則對照（dependency-rules G1-G5）

| 規則 | 實證 | 結果 |
|------|------|------|
| G1 唯一狀態權威 | SetState 唯一變異點；IsPlaying()=state==Playing | ✅ |
| G2 凍結機制 | 各系統自查 IsPlaying()＋BG 單向 SetFrozen；無 SetActive/timeScale | ✅ |
| G3 P 鍵歸屬 GameManager | P 鍵在 GameManager.Update；PlayerController 只管 flap | ✅ |
| G4 UI 目錄＋公開方法接線 | `Assets/Scripts/UI/`；UI→GameManager 全公開方法＋OnStateChanged 事件 | ✅ |
| G5 UI 掛持久根＋Canvas 分離 | UIController 掛持久根（OnEnable 訂閱/OnDisable 退訂）；MenuCanvas/OverlayCanvas 分離 | ✅ |

## Issues

### Blockers
無。

### Majors
無。

### Minors
無。

## 動態驗證清單
Sisyphus 已於 Phase 4.5a 執行 Play mode 全 14 項（初始 Menu、Start/Pause/Resume/EndGame/Restart/Die/BackToMenu 轉移、Canvas/Pane 切換、PlayerPrefs 破紀錄寫入、P 鍵守衛、按鈕接線、凍結行為級證明），全數通過。

### 動態驗證回填（Phase 4.5，Sisyphus）

- **日期**：2026-08-24；**執行者**：Sisyphus（Play mode 內 execute_code 逐項實測，log 前綴 `[GAME-STATES-DV]`）。
- **DV 結果（14 項全過）**：
  - INIT：啟動 state=Menu、playing=False、menuCanvas.active=True、overlayCanvas.active=False、最高分顯示（11）✅
  - START：StartGame()→Playing、雙 Canvas 隱藏 ✅
  - PAUSE：Pause()→Paused、overlay 顯示、pausePane 顯示；**4 個 BGLayer 全數 frozen=True** ✅
  - RESUME：Resume()→Playing、全層解凍、Canvas 隱藏 ✅
  - ENDGAME：EndGame()→GameOver、gameOverPane 顯示、pausePane 隱藏 ✅
  - HIGHSCORE：灌分 12>11 後 EndGame→`afterWrite=12`（破紀錄覆寫成功）；已還原 11 避免污染存檔 ✅
  - RESTART：Restart()→Playing、score 歸 0 ✅
  - DIE：Die()（Playing）→GameOver、gameOverPane 顯示（薄包裝路徑）✅
  - BACKTOMENU：BackToMenu()→Menu、menuCanvas 顯示、`menuHighScoreText='High Score: 11'` ✅
  - PKEY-GUARD：Menu/GameOver 態 Pause()/Resume() 皆被守衛擋下（state 不變）✅
  - BUTTON-WIRE：五顆按鈕 onClick.Invoke 全接到正確方法（restart→Playing→end→GameOver→backToMenu→Menu→start→Playing→end2→GameOver→resume(守衛)→GameOver）✅
  - GAMEOVER-TEXT：`gameOverScoreText='Score: 5'`、`gameOverHighScoreText='High Score: 11'` ✅
  - FREEZE-SNAP/RE：Paused 態快照→2 秒後重讀，layerScroll/birdX/activePipes 分毫未動（鳥/水管/背景全凍結，行為級證明）✅
- **結論**：AC-1~AC-11 全數通過；BC-1（死亡+按P 競態）守衛成立。Verdict 由暫定轉正式 **PASS**。
- **遺留**：validator 前三次 session 遭供應端中止（Aborted），第四次全新派發成功落盤；其間 Sisyphus 已獨立完成 DV，兩者互為佐證。

## 驗證方法限制與備註
- **9-slice 判定欄位**：任務提示以 `textureType:8` 為 9-slice 指標，但該欄位不存在於場景 YAML。實際 9-slice 指標為 Image 元件 `m_Type: 1`（Sliced）＋`m_Sprite` 指向 button_rectangle_flat。已以 `m_Type: 1` 為準驗證，非缺陷。
- **按鈕素材名稱**：場景以 GUID 引用 sprite（非字面 `button_rectangle`），已反查 `.meta` 確認 GUID 對應 `button_rectangle_flat.png`。
- **動態行為**（凍結時鳥不動、按鈕點擊、破紀錄覆寫）屬行為級，靜態僅能確認守衛存在，實際行為以 Sisyphus DV 為準。

## Statistics
- blocker: 0 / major: 0 / minor: 0