# Validation Report: dying-state round 1

- **Date**: 2026-09-30
- **Task Sheet**: specs/requirements/dying-state.md
- **Verdict**: **PASS（暫定）**——動態驗證 D1-D9 回填前為暫定；回填後方可關單
- **驗證範圍**: T1（GameManager）＋T2（BlobAnimator/GameStateAuthority）＋T3（UIController）＋架構文件同步（game-states.md/animator-system.md/dependency-rules.md/gameplay-architecture.md/ADR-013）
- **驗證方法限制**: ① 無 git 環境——「零改動」類 AC（MUST NOT 清單）以語義級核對＋與既有驗收輪記載版本比對，非 git diff；② 本 session 無 unityMCP 工具（read_console/run_tests 不可達）——Console 錯誤與編譯狀態無法靜態確認，列動態驗證；③ 無測試套件（Assets/ 下無 .asmdef、無 *Test*.cs）

## 逐項 AC 驗證結果（15 項靜態重點）

| # | 驗證項 | 結果 | 證據 |
|---|--------|------|------|
| 1 | enum 完整性（FR-01） | ✅ | `GameManager.cs:7` `{ Menu, Ready, Playing, Dying, Paused, GameOver }`——Dying 插在 Playing 之後，語意「Playing 的死亡子階段」 |
| 2 | isDying 徹底移除（FR-07） | ✅ | grep `isDying` 於 `Assets/` 全 .cs **零命中**；舊使用點全轉換：`:22` 欄位已刪、`:89`→現 `:88` 純 `state == Playing`、`:138` 重設已刪、`:209`→現 `:212` 守衛、`:210`→現 `:213` `SetState(Dying)` |
| 3 | Die() 轉移（FR-02） | ✅ | `:212` `if (state != Playing) return;` 保留（重入防護）；`:213` `SetState(Dying)` 取代 isDying=true；`:214-218` player.Die()→停 collider→PlayDissasemble 順序不變 |
| 4 | 背景凍結（FR-03） | ✅ | `:123` `case Dying: backgroundManager.SetFrozen(true); break;`（顯式 case）；PipeController.cs:16／PipeSpawner.cs:51 因 `IsPlaying()` 嚴格化（Dying 時 false）自然停 |
| 5 | 動畫 speed 接受 Dying（FR-04） | ✅ | `BlobAnimator.cs:146` `(lastState == Playing \|\| lastState == Dying \|\| isAssembling) ? 1f : 0f` |
| 6 | 淡出守衛（FR-05） | ✅ | `BlobAnimator.cs:156` `if (GameStateAuthority.IsPaused()) return;`——「非暫停即可淡出」；Paused 仍凍結 |
| 7 | IsPaused() seam | ✅ | `GameStateAuthority.cs:33-36`（`instance != null && instance.IsPaused()`）＋`GameManager.cs:235-238`（`state == Paused`）；查詢非命令，ADR-011 合規（NFR-05） |
| 8 | EndGame 守衛 | ✅ | `:171` `if (state != Dying && state != Paused) return;`——死亡淡出完成（Dying）與暫停→結束（Paused）兩路徑皆通 |
| 9 | StartGame 重入擋 | ✅ | `:139` `if (state == Playing \|\| state == Ready \|\| state == Dying) return;`——含 Dying（Tech Lead 建議防禦性硬化） |
| 10 | P 鍵排除（BC-1/FR-06） | ✅ | `:98` `state == Playing \|\| state == Paused` 自然排除 Dying——死亡中按 P 無效（舊 bug 消失） |
| 11 | UI Dying case（T3/FR-09） | ✅ | `UIController.cs:145-153` Dying 與 Ready/Playing 共用分支（`SetActive(menuCanvas,false); SetActive(overlayCanvas,false);`）——淨空 menu/overlay、保留 HUD ScoreText |
| 12 | 重入防護安全（BC-3） | ✅ | 三重防護：① `Die()` 首行 `state != Playing` 守衛（state==Dying 直接 return）；② 地板/天花板檢查 `state == Playing` 排除 Dying；③ 鳥 collider 已於 `Die()` 內停用（`:216-217`），CollisionHandler 路徑即使觸發也過不了 ① |
| 13 | 凍結一致性（NFR-02/NFR-04） | ✅ | 四系統全凍結無漏閘：PlayerController.cs:68／PipeSpawner.cs:51／PipeController.cs:16 自查 `IsPlaying()`（Dying 時 false）；背景 `case Dying: SetFrozen(true)` → BGManager → BGLayer.frozen → `BGLayer.cs:166` `if (motion == Normal && frozen) return;` |
| 14 | MUST NOT 遵守 | ✅ | 七檔案語義級核對零改動：PipeController（:16 守衛/Configure/SetGap）、PipeSpawner（:51 守衛/倒數式/ResetAll seed=0）、PlayerController（:68 守衛/isDead 內部旗標保留）、BGLayer（:166 frozen 檢查）、BGManager（SetFrozen/SetNormal/SetAmbient）、ScoreTrigger（:18 IsPlaying 守衛）、CollisionHandler（OnTriggerEnter2D→Die）——與既有驗收輪記載版本一致（方法限制①） |
| 15 | 註解一致性 | ✅ | `:85-88` 已更新（原「Die() 先置 state=GameOver」過期註解移除，改為 BC-1 順序＋H1 state 排除說明）；Die() 頭註解（`:207-209`）FR-16 描述已更新（「維持 Playing」撤銷→Dying 態） |

## 架構文件同步檢查（arch-review 範圍）

| 文件 | 同步狀態 |
|------|---------|
| `specs/architecture/game-states.md` | ✅ enum（:10/:52）、轉移圖（:57）、Dying 死亡態（:66）、SetState 背景 switch（:67）、EndGame 守衛（:68）、Update 結構 H1 簡化（:80）、IsPaused()（:81）、凍結機制（:85/:92/:96）、UI Dying case（:106）、實作切入點（:130-134）全同步 |
| `specs/architecture/animator-system.md` | ✅ 類圖（:27/:43/:59-70）、speed 映射（:156-158）、淡出守衛（:165）、死亡流程（:185-227）全同步 |
| `specs/architecture/dependency-rules.md` | ✅ G1（:25）、G2（:26）、GA1（:60）、AN5-AN8（:73-76）、既有依賴現況（:94-101）全同步 |
| `specs/architecture/gameplay-architecture.md` | ✅ 死亡流（:51-61）同步 |
| `docs/adr/013-blob-animator.md` | ✅ 決策 5 修訂（:57-63）、決策 8 翻轉（:75-93）、H1-H6 裁決翻轉（:84-93）、Consequences（:122）、被放棄方案（:136）全同步 |

## Blockers

無。

## Majors

無。

## Minors

### m1: Die() 內 blobAnimator 無 null guard
- type: code | severity: minor
- 依據: 既有（blob-animator round-1 m1 維持，非本功能引入）
- 事實: `GameManager.cs:218` `blobAnimator.PlayDissasemble();`——blobAnimator 於 OnEnable fallback 解析（`:45-46`），若場景未接線且無 BlobAnimator 元件則為 null → NRE。與其他呼叫點（`:200-204` ShowPlayerAtStart 有 null guard）不一致
- 建議方向: 加 `if (blobAnimator != null)` 守衛（與 ShowPlayerAtStart 一致）

### m2: FadeOut 內 spriteRenderer 無 null guard
- type: code | severity: minor
- 依據: 既有（blob-animator round-1 m3 維持，非本功能引入）
- 事實: `BlobAnimator.cs:158` `Color color = spriteRenderer.color;`——spriteRenderer 於 Awake 解析（`:50-51`），若場景未接線則為 null → NRE
- 建議方向: 加 null guard（ResetVisual 已有 `:183` 同款守衛）

### m3: PlayHidden() 死命令
- type: code | severity: minor
- 依據: 既有（blob-animator round-1 m2 維持，非本功能引入）
- 事實: `BlobAnimator.cs:98` `PlayHidden()` 零呼叫（grep 全 Assets 無呼叫點）——保留為對稱 API
- 建議方向: 保留（對稱 API）或標註死碼

### m4: PlayerController.HandleRotation() 死方法
- type: code | severity: minor
- 依據: 既有（blob-animator round-1 m4 維持，非本功能引入）
- 事實: `PlayerController.cs:126-145` `HandleRotation()` 於 Update 內被註解掉（`:74` `// HandleRotation(); //改用spritesheet animation 不再旋轉`）——死方法＋死欄位（maxUpAngle/maxDownAngle/rotationSpeed）
- 建議方向: 移除或保留為未來旋轉需求（既有裁決：保留）

## 靜態驗證關鍵時序（記錄供動態驗證對照）

1. **死亡瞬間 speed 無 1 幀凍結窗口**：`Die()` → `SetState(Dying)` → OnStateChanged fire（同步）→ `lastState=Dying` 立即生效；`PlayDissasemble()` 呼叫時 speed 為上一幀值（Playing 態 speed=1）→ Dissasemble 立即播放；下一幀 Update 求值 speed=1（lastState==Dying）。B1 每幀求值方案涵蓋此路徑（與 blob-animator round-2 教訓一致）。
2. **背景凍結同步、水管/生成凍結延 1 幀**：背景 `SetFrozen(true)` 在 SetState 內同步執行（死亡幀即凍結）；水管/生成靠下一幀 Update 自查 `IsPlaying()==false`。死亡幀水管移動距離 ≈ moveSpeed×dt ≈ 0.08 單位，視覺不可察——動態 D1/D2 確認。
3. **死亡幀水管生成殘留**：若死亡幀 PipeSpawner.Update 先於 Die() 執行且 spawnTimer<=0，會生成一根水管但立即凍結（Dying 態不再移動）——動態 D2 確認。

## 動態驗證清單（無法靜態驗證者；Sisyphus 於 Editor 實測回填）

| # | 步驟 | 預期觀察 | Sisyphus 回填 |
|---|------|----------|---------------|
| D1 | 進入 Playing，撞水管死亡，觀察死亡動畫期間畫面左側水管 | 水管立即停止移動（不再滑到畫面外）；死亡幀至多殘留 0.08 單位位移 | （待填） |
| D2 | 同上，觀察死亡動畫期間畫面右側 | 無新水管現身（死亡幀殘留生成除外，且生成後立即凍結） | （待填） |
| D3 | 同上，觀察背景（Sky/Mountain/Forest/Foreground 四層） | 背景立即停止捲動（死亡瞬間凍結） | （待填） |
| D4 | 同上，觀察鳥的 Dissasemble 動畫 | 動畫完整播放（speed=1，不凍結第一幀），15 幀 1.25s 播完 | （待填） |
| D5 | 同上，等待淡出 | 淡出完成（alpha→0）後 GameOver 面板才出現（延後出現，非死亡瞬間） | （待填） |
| D6 | 死亡動畫期間按 P | P 無效——不暫停、不卡死、死亡流程繼續走完進 GameOver | （待填） |
| D7 | 死亡→GameOver 後按 Restart 新局 | 一切正常重置：鳥現身（alpha=1）、Assemble 播放、水管/背景恢復、無 isDying 舊旗標殘留副作用 | （待填） |
| D8 | Playing 按 P 暫停 → 按「結束」按鈕 | 正常進 GameOver（EndGame 守衛保留 Paused 路徑驗證） | （待填） |
| D9 | 正常 Playing 期間（未死亡） | 水管照常移動/生成、背景照常捲動（不誤凍結） | （待填） |
| D10 | 死亡動畫期間按 space | 無效（PlayerController 自查 IsPlaying()==false，不 flap） | （待填） |
| D11 | 死亡動畫期間按「結束」按鈕（若 overlay 可見） | 不可達（Dying 態 UI 淨空，pause overlay 不可見）——若可見則為缺陷 | （待填） |
| D12 | Console 檢查 | 死亡流程全程無 error/warning（本 session 無 unityMCP 工具，靜態無法確認編譯/Console） | （待填） |

## Statistics

- blocker: 0 / major: 0 / minor: 4（全為既有，非本功能引入）
- 15 項靜態驗證重點全數通過；架構文件 5 份全同步
- 動態驗證清單 D1-D12 已列，回填前 Verdict 暫定