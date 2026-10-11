# Validation Report: ui-animations round 3

- **Date**: 2026-08-25
- **Task Sheet**: specs/requirements/ui-animations.md
- **Verdict**: **PASS（定案）** — 0 blocker / 0 major / 1 minor（m2 累積記錄）

## 背景：round-2 未徹底解決

round-2 修復後，人類再次實測回報「Menu 仍瞬間出現、無過渡動畫」。round-2 修了「雙重 fire 秒殺」但未命中真正根因——**動畫目標是根 Canvas，位移視覺無效**。本輪追認 B3 並確認修復。

## Blockers

### B3: Menu 仍瞬間出現，無過渡動畫（根因＝根 Canvas 位移視覺無效）
- type: `code` | severity: `blocker`
- 依據: FR-03「面板進場滑入帶彈跳」；整體驗收「Menu 滑入：啟動進 Menu 時，主選單滑入帶彈跳」。
- 事實: round-1/2 的 tween「有播放、有過沖」（TWEENDBG 逐幀證實 easeOutBack e=1.065 過沖），但動畫目標是 **MenuCanvas 根——Screen Space Overlay 的根 Canvas**。在 Screen Space Overlay 下，**移動根 Canvas 的 anchoredPosition 對視覺渲染無效**（根 canvas 的 child world 座標 ≠ 螢幕顯示位置）。所以 tween 有算、有過沖，但**畫面不動**。
  - 對比：pausePane/gameOverPane 是 **OverlayCanvas 的子物件**（有父），改它們的 anchoredPosition 會視覺移動——所以那兩個滑入看得到，Menu 看不到。
- **修復（已落地）**:
  1. **場景結構**：在 MenuCanvas（根 Screen Space Overlay）下建立「MenuPanel」包裝層（`SampleScene.unity:1625`，透明 Image alpha=0、raycastTarget=0、1920×1080、anchor 0,0、anchoredPos 0,0），把 Title/StartButton/HighScoreText 三個原本直接掛根 Canvas 的內容 reparent 進 MenuPanel（`MenuPanel` children = 431979740/2007211625/1749079642），位置保持。MenuCanvas 現在唯一 child 是 MenuPanel（`SampleScene.unity:3193-3194`）。
  2. **UIController**：`ResolveAnimationTargets` 改為解析 `menuCanvas.transform.Find("MenuPanel")`（若有）當作 menuCanvasRect，退回舊結構（直接 menuCanvas）相容（`UIController.cs:61-66`）。`PlayPanelSlideIn` 現在 tween 的是 MenuPanel（child）的 anchoredPosition，位移會視覺生效。
- **動態驗證（Sisyphus 於 Editor 逐幀採樣，已過）**: MenuPanel.anchoredPosition 完整滑入曲線——frame1 y=-600（off-screen 起點）→ frame2 -493 → frame3 -398 → **frame4 +39（超過 resting 0 = easeOutBack 過沖）** → frame5~15 回落至 0。且驗證過 MenuPanel 位移會讓 child（StartButton）世界座標跟著動（delta 相等），證明視覺有效。
- 建議方向: 已修復，無需再修。

## Minors

### m2（累積）: UITween 對同目標並發 tween 無內部防護
- type: `code` | severity: `minor`
- 事實: `UITween.ScaleTo/AnchoredPositionTo` 在呼叫時捕捉 `from = 目前值`（`UITween.cs:40/47`），若同目標已有進行中 tween 又再起一個，兩者並發互相覆寫。目前所有呼叫端皆先 `StopAll` 再起新 tween，未觸發。維持 minor 累積，不擋收單。

## 動態驗證清單（本輪）

| # | 步驟 | 預期觀察 | 結果 |
|---|------|----------|------|
| D1 | 啟動進 Menu | MenuPanel 從 off-screen (0,-600) 滑入至 (0,0) 帶 easeOutBack 過沖，child（StartButton）世界座標同步移動 | ✓ 完整曲線 frame1 -600→frame4 +39（過沖）→frame5~15 回落 0；child 世界座標 delta 相等 |

## Statistics

- blocker: 1（B3，已修復）/ major: 0 / minor: 1（m2 累積）

## 附註

- **round-2 雖修了雙重 fire 秒殺，但未命中根 Canvas 位移視覺無效的真根因**——Menu 仍需 round-3 修復。round-2 的 `_isFirstSync` 修法仍有效（避免雙重 fire 秒殺），但動畫目標必須是 child RectTransform（包裝 panel）而非根 Canvas。
- **教訓**：Screen Space Overlay 根 Canvas 的 anchoredPosition 位移視覺無效。此缺陷靜態看 easing/接線全正確（tween 有播放、有過沖），只有動態驗證或 Unity 平台知識才能捕獲——「tween 有算但畫面不動」是 UI 動畫驗收的高風險盲區。
- **修復審查**：`ResolveAnimationTargets` 的 `Find("MenuPanel")` 退回舊結構相容設計健全；MenuPanel 透明 Image（alpha=0）＋raycastTarget=0 不擋輸入（FR-05 不阻塞）；anchor 0,0 + 1920×1080 使 child 世界座標位移與 panel 位移一致。無疑慮。
- Console 僅見 MCP-FOR-UNITY 外掛噪音（WebSocket/Inspector），無遊戲 code 錯誤。

## Statistics

- blocker: 0 / major: 0 / minor: 1（m2 累積）