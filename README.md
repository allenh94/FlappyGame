# FlappyGame
> 以 Unity 6 / URP 打造的橫向捲軸網頁遊戲，內建線上排行榜
[▶ Play Demo](https://allenh94.itch.io/flappygame) · Unity 6000.3.12f1 · URP 17.3 · C#

## Overview

FlappyGame 是一款以 Unity 6 與 URP 開發、可直接在瀏覽器遊玩的橫向捲軸遊戲。專案聚焦於手感調校、熱路徑效能與線上排行榜整合，並以一套角色分權的多代理流程完成開發與驗收。

## Tech Stack

| 項目 | 內容 |
|---|---|
| 引擎 | Unity 6000.3.12f1 |
| 渲染 | Universal Render Pipeline (URP) 17.3.0 |
| 輸入 | Input System 1.19.0 |
| 物理 | Physics2D |
| 後端 | LootLocker（線上排行榜） |
| 語言 | C# |

## Technical Highlights

**物件池（Object Pooling）**
水管與背景 tile 全部收斂到泛型池 `PooledActives<T>`。穩態只移動、不 `Instantiate`／`Destroy`，避免記憶體反覆分配與 GC 尖峰，並內含重複 get／release 防護。

**手感設計（Game Feel）**
按下不直接給速度，而是先播「蓄力」動畫；程式等動畫到 `normalizedTime ≥ 0.1` 才觸發 `JumpRise`、施加衝力。衝力因此對上真正的起跳點，蓄力期間也擋掉第二下輸入。

**遊戲狀態機（Game State Machine）**
六態 `Menu / Ready / Playing / Dying / Paused / GameOver`。`Ready`、`Dying` 是為修正「開局太突兀」「死亡瞬間消失」而加的過渡態；系統改以 `GameStateAuthority` 查詢狀態，不直接持有 `GameManager`，降低耦合。

**資料驅動視差背景（Parallax Background）**
`BGConfig`（ScriptableObject）驅動任意層數；深度只管速度、renderOrder 只管 sortingOrder。以 `Mathf.Repeat` 取模無縫捲動，所有 tile 位置由同一個累計距離推導，因此不會漂移，另附自製 Editor 即時預覽視窗。

**零依賴自製 tween 引擎（Tweening）**
UI 動畫由自製 `UITween` 驅動，不引入 DOTween：手寫 easing、`LerpUnclamped` 保留過衝、token 定向停止且停止即歸位。

**手寫物理與終端速度（Terminal Velocity）**
Rigidbody2D `gravityScale=0`、連續碰撞偵測，由程式每幀手動積分重力並鉗制到終端速度，以精準控制「終端速度 × 跳躍衝力」這個手感核心。

**Editor 工具：Atlas XML 自動切圖（Sprite Slicing）**
讀取素材包的 XML 自動換算座標、命名並整批寫入切片。關鍵在於使用官方 `ISpriteEditorDataProvider`，因為舊的 `TextureImporter.spritesheet` 在 Unity 6000.3 已被移除；任一片越界即中止整批，不留半套結果。

**線上排行榜（Online Leaderboard）**
LootLocker 後端以 `ILeaderboardProvider` 隔離。讀取失敗回退快取；送出失敗進待送佇列（上限 20、丟最舊），恢復後補送，分數不會消失，排名以伺服器為準。附 EditMode 單元測試（注入假件）。

## Engineering Process

本專案採多代理角色分權：需求、架構、實作、驗收四種角色分離，權限以設定檔 YAML 硬性強制。驗收者只能寫報告、不能改程式，因為驗收者若能改 code，就會把不合格的東西「就地修成合格」，獨立性便蕩然無存。

流程真的抓到錯：動畫速度被設為 0 導致卡死；UI 動畫靜態驗收 PASS 卻被實測推翻兩次；一個實測 bug 讓架構決定翻案重改。

| ADR | 需求規格 | 架構文件 | 驗收報告 | EditMode 測試 |
|---|---|---|---|---|
| 16 份 | 12 份 | 8 份 | 24 份 | 3 檔 / ~21 案例 |

## My Role

本專案採 **AI-assisted development**，但我把它當成一門**工程方法**經營，而非「請 AI 代筆」：

- **我設計並操作一套多代理工作流**：需求、架構、實作、驗收四種角色分離，各自權限以設定檔**硬性強制**——例如驗收者被禁止修改程式（能被就地修的產出就失去獨立性）。AI 的輸出因此**可被審計、被驗收、被抓錯**。
- **我負責**：需求與規格定義、架構決策、流程與權限設計、實測與驗收、問題診斷。
- **AI 負責**：依規格產出實作。
- **佐證（流程真的抓到錯）**：動畫速度被設為 0 導致卡死 → 診斷出；UI 動畫靜態驗收 PASS → 被實測推翻兩次；一個 runtime bug → 架構決策翻案重改。
- 我對提交的程式碼負最終責任，並能逐項說明其設計與取捨。

## Quick Facts

- 引擎與版本：Unity 6000.3.12f1 / URP 17.3 / Input System 1.19
- 規模：約 29 支腳本、~3,100 行
- 自製系統：物件池、tween、狀態機、背景系統皆自製
- 發佈：WebGL（Brotli 壓縮）
- 開發期：2026-09 ~ 2026-10

## Setup

- 以 **Unity 6000.3.12f1** 開啟本專案。
- 本專案使用 LootLocker 線上排行榜；clone 後需自備 LootLocker Game API Key 才能啟用（設定檔不隨版控提供）。
