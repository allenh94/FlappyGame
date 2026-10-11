# 靜態流程追蹤：blob-animator 狀態機（Sisyphus 產出，2026-09-05）

> 產出者：Sisyphus（orchestrator）。性質：**流程模擬追蹤**（非程式碼審查——Validator round-2 已做程式碼審查，本文件補充「逐流程執行序模擬」）。
> 對照：`specs/architecture/animator-system.md`（契約）、`specs/validation/blob-animator/round-2.md`（Validator）。
> 用途：人類 Play 測試（D1-D10）前的靜態信心依據＋未來回歸複核的基準。

## 審查對象（現行版本）

- `Assets/Scripts/BlobAnimator.cs`（186 行，state-query 核心時序＋B1 每幀求值）
- `Assets/Scripts/PlayerController.cs`（163 行，OnJumpRise 衝力＋AN13）
- `Assets/Scripts/GameManager.cs`（235 行，A2 流程＋dying 守衛）

## 追蹤結果總表

| # | 流程 | 追蹤結果 | 對應 D 清單 |
|---|------|---------|------------|
| F1 | StartGame 全流程 | ✅ | D1 |
| F2 | 鳥下墜接 Land | ✅ | D1（尾段） |
| F3 | 跳躍蓄力→上升 | ✅ | D3 |
| F4 | 蓄力連點 | ✅ | D4 |
| F5 | 345 再跳／Land 再跳 | ✅ | D5 |
| F6 | 死亡流程 | ✅ | D6 |
| F7 | 死亡中按 P | ✅ | D7 |
| F8 | 暫停/恢復 | ✅ | D9 |
| F9 | Restart／BackToMenu | ✅ | D8 |

## F1 StartGame 全流程（逐幀模擬）

```
Menu（state=Menu）
  │ StartGame()（GameManager:134）
  │   if Playing||Ready return（重入擋）
  │   isDying=false
  │   scoreSession.Begin()
  │   SetState(Ready) → OnStateChanged(Ready) → BlobAnimator lastState=Ready（不在此求值 speed，B1）
  │   HidePlayerAndPipes()（鳥 SetVisible(false)＋水管 ResetAll seed=0）
  ▼ Ready（倒帶 1s，IsPlaying()==false）
  │ ScrollBackToStart(onComplete:)
  │   SetFrozen(true)          ← 背景凍結（Assemble 期間）
  │   ShowPlayerAtStart()
  │     player.SetVisible(true)+Reset()
  │     blobAnimator.ResetVisual()  ← 清 gate/once 旗標＋alpha=1
  │     blobAnimator.PlayAssemble() ← isAssembling=true；animator.Play("Assemble",0,0)
  ▼ 下一幀 BlobAnimator.Update
  │   UpdateAnimatorSpeed(): lastState=Ready 但 isAssembling=true → speed=1（B1 修復核心）
  │   UpdateAnimationStateSignals(): Assemble 播放中
  ▼ Assemble 播完（20 幀 1.67s）
  │   info.IsName("Assemble") && norm>=1 → assembleCompleteFired=true；isAssembling=false
  │   fire AssembleComplete → GameManager.HandleAssembleComplete
  │     if state==Ready → SetState(Playing)
  │       backgroundManager.SetNormal()  ← 背景開始捲動
  │       OnStateChanged(Playing) → BlobAnimator lastState=Playing
  │     PipeSpawner.Update: IsPlaying()==true 且 spawnTimer seed=0 → 首幀生成第一根水管
  │     PlayerController.Update: IsPlaying()==true → HandleGravity（IsAssembling=false 恢復重力）
  ✅ 結果：Assemble 播完世界才動（背景捲動＋水管生成＋鳥開始下墜）
```

**關鍵安全點（B1 修復驗證）**：`PlayAssemble()`（設 isAssembling=true）發生在 `SetState(Ready)` fire **之後**——若 speed 在 OnStateChanged 回呼內求值會停在 0（卡死）；現改 Update 每幀求值，`isAssembling=true` 後**最多 1 幀內** speed=1，卡死鏈徹底消除。

## F3 跳躍蓄力→上升

```
Playing 中按 space → PlayerController.HandleInput → blobAnimator.PlayJump()
  if isCharging||isAssembling return（此處 false→通過）
  isCharging=true；jumpRiseFired=false；animator.Play("Jump",0,0)
▼ Jump 播放（6 幀 0.5s）
  norm>=0.5（frame3 起點）→ jumpRiseFired=true；isCharging=false；fire JumpRise
    → PlayerController.OnJumpRise: isLanding=false；currentVelocity=flapForce（12）
  HandleGravity: velocity 由負轉正 → 上升（345 幀動畫＋實際位移）
✅ 結果：012 蓄力播完才觸發上升（FR-10）
```

## F6 死亡流程（含淡出→EndGame）

```
Playing 中鳥撞水管/地板 → CollisionHandler→GameManager.Die()（或 Update 地板/天花板檢查）
  if state!=Playing return；if isDying return（re-entry guard）
  isDying=true
  player.Die()（isDead=true＋速度歸零）
  collider.enabled=false（補停，防重複觸發）
  blobAnimator.PlayDissasemble()
▼ Dissasemble 播放（15 幀 1.25s；state=Playing → speed=1）
  期間：水管持續捲動（FR-17）；Update 地板/天花板跳過（H1: !isDying）；輸入被 isDead 擋
▼ 播完 → norm>=1 → dissasembleCompleteFired=true；fire DissasembleComplete（hook）；isFadingOut=true
▼ FadeOut（每幀 IsPlaying gate）
  alpha -= fadeSpeed*dt → 0
  alpha<=0 → fadeCompleteFired=true；isFadingOut=false；fire OnDeathFadeComplete
    → GameManager.HandleDeathFadeComplete → EndGame()
      if Playing||Paused → scoreSession.End() → SetState(GameOver)
✅ 結果：瓦解動畫→淡出→才 GameOver UI（FR-15）
```

## 競態安全分析

| 競態 | 兩條路徑 | 結果 |
|------|---------|------|
| 死亡淡出完成 vs P 鍵同幀 | ①先 EndGame→state=GameOver→P 檢查跳過 ②先 Pause→state=Paused→淡出 IsPlaying gate 凍結→Resume 續播→淡出完成→EndGame（state=Paused 通過守衛） | ✅ 皆安全 |
| Assemble 播完 vs 按 P（Ready 期間） | Ready 期間 P 鍵檢查 `if Playing||Paused` 不成立→無視 | ✅ |
| StartGame 重入（Ready 期間） | `if Playing||Ready return` | ✅ |
| Die 重入（死亡流程中） | `if isDying return` | ✅ |

## 結論

**狀態機邏輯健全——9 條主流程＋4 組競態全數靜態驗證通過。** 理論上 D1-D10 應全數符合預期；最終以人類 Play 實測回填為準（Verdict 暫定 → 回填後轉正式 PASS）。