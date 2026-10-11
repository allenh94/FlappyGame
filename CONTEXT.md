# Context

## Project

flipbird — Flappy Bird clone built in Unity 6 with URP.

## Tech stack

- Unity 6 (6000.3.12f1)
- Universal Render Pipeline (URP) 17.3.0
- Input System 1.19.0
- Physics2D
- C#

## Glossary

| Term | Definition |
|---|---|
| **Flap** | 玩家按下跳躍鍵（空白鍵）的**輸入動作**——觸發 Crouch 蓄力，不直接給衝力 |
| **Crouch** | Jump 動畫的 012 蓄力段——期間無視跳躍輸入，蓄力播完才觸發 Rise |
| **Rise** | Crouch 播完後鳥獲得向上衝力的實際位置上升 |
| **Assemble** | 遊戲開始的誕生動畫——鳥現身那一刻播放，播完遊戲開始動 |
| **Dissasemble** | 死亡瓦解動畫——碰撞後播放，播完淡出才進入 GameOver |
| **Terminal velocity** | 鳥下墜的最大速度，避免無限加速 |
| **Gap** | 兩根管子之間的垂直間隙，鳥必須通過 |
| **Pipe pair** | 一根上方管子 + 一根下方管子，構成一個障礙 |
| **Scroll speed** | 背景和管子向左移動的速度 |
| **Collision body** | 鳥和管子的 Physics2D 碰撞體 |
| **Placeholder** | Prototype 階段用的暫時美術（色塊、簡單形狀） |
| **排行榜（Leaderboard）** | 前 10 名玩家名字與分數的清單，依分數降冪排序；為最高分顯示的唯一來源 |
| **榜單條目（LeaderboardEntry）** | 排行榜單一列＝玩家名字＋分數；同分時先插入的在上面（先到先得） |
| **上榜（Qualify）** | 本局分數通過資格判定而可插入榜單：未滿 10 筆須 > 0；滿 10 筆須 > 第 10 名分數；與第 10 名同分不算 |
| **排行榜畫面（Leaderboard Screen）** | 顯示榜單＋返回主畫面鈕的獨立 Canvas，主選單（瀏覽）與 GameOver（上榜）兩狀態皆可顯示，非獨立 GameState |
| **排行榜提供者（LeaderboardProvider）** | 榜單讀寫的統一接口（async callback 式）；本地端實作＝LocalLeaderboardProvider（PlayerPrefs JSON），未來 LootLocker 實作 |
| **上榜演出（Qualify Animation）** | 新條目從榜底以分數滾動（0→目標、2 秒 easeOut）滑入、名次依滾動值即時重排的動畫 |
| **最高分（High Score）** | 個人歷史最高分，資料保留於 ScoreSession（PlayerPrefs）；UI 顯示已退休，由排行榜畫面取代 |

## Architecture

- 純 2D 遊戲，使用 Physics2D
- 鳥只在垂直方向移動（Y 軸），水平位置固定
- 管子和背景從右向左滾動，產生前進感
- 死亡判定：地板、天花板、管子（含邊緣碰撞）
- 死後立刻重置所有物件狀態

## Directory Structure & Conventions

> 維護責任：Tech Lead——目錄慣例隨架構決策（ADR）連動更新。本節只定義「該放哪」與「為何這樣放」，不做檔案清單快照；具體檔案以即時查詢（glob / manage_asset search）為準。

```
Assets/
├── Scripts/
│   ├── *.cs                  舊核心 gameplay 腳本（平鋪，未經決策不搬家——移動會斷場景 GUID 引用）
│   ├── <System>/             新系統 runtime 腳本各立子資料夾（現有：Background/＝BGConfig、BGLayer、BGManager、Tile）
│   └── Editor/<System>/      Editor-only 工具按系統分目錄（現有：AtlasTools/、Background/）
├── Art/
│   ├── Atlases/              圖集＋切片定義（.spriteatlas、spritesheet .png/.xml）
│   ├── Sprites/              單體精靈／placeholder（WhiteSquare.png）
│   └── Background/           背景散件素材
├── Prefabs/<System>/         prefab（現有：Background/Tiles/＝Tile 模板）
├── Config/<System>/          ScriptableObject 配置（現有：Background/BGConfig.asset）
├── Materials/<System>/       材質
├── Scenes/                   場景（SampleScene）
└── Screenshots/              驗收截圖存證（人類目視複核用，agent 不以此自判）
```

慣例（源自 ADR-004 D6「慣例目錄隔離」）：

- 新系統的腳本／prefab／配置／材質一律放 `<對應根目錄>/<系統名>/` 子資料夾隔離，不與既有內容混放
- 既有平鋪腳本未經架構決策不得移動（移動 = GUID 變更風險，會斷場景引用）
- Editor 工具一律放 `Assets/Scripts/Editor/<系統名>/`
