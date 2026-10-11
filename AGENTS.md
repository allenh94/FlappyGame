# Agents

## Unity MCP
當需要操作 Unity Editor 時，使用 unityMCP 工具。
例如：建立/刪除/修改 GameObject、讀取場景、操作 Prefab。
不要用 Bash 執行 Editor 腳本，用 MCP 工具。

## Game Development Agents

flipbird 使用 Helm/Ekioo 風格的 True Agent 架構：

### 架構圖

```
Sisyphus（Orchestrator）
    │
    ├── OMO Agents（基礎設施）
    │   ├── explore → 搜 codebase
    │   ├── librarian → 搜文件
    │   └── oracle → 架構顧問
    │
    └── Project True Agents（領域專家）
        ├── pm-agent → 需求分析、規格、分配
        ├── tech-lead-agent → 架構設計與審查
        ├── gameplay-developer → 核心機制
        ├── scene-developer → 場景佈局
        ├── asset-developer → 美術資產
        └── validator-agent → 驗收（只挑問題，不修復）
```

### Agent 清單

| Agent | 職責 | 定義位置 |
|-------|------|----------|
| **PM Agent** | 分析需求、建立規格、排優先順序、分配任務 | `.opencode/agents/pm-agent.md` |
| **Tech Lead Agent** | 架構設計與審查、dependency-rules 維護、ADR | `.opencode/agents/tech-lead-agent.md` |
| **Gameplay Developer** | 核心機制、物理、得分、輸入處理 | `.opencode/agents/gameplay-developer.md` |
| **Scene Developer** | 場景佈局、Prefab、物件組織、相機設定 | `.opencode/agents/scene-developer.md` |
| **Asset Developer** | 精靈、材質、音效、視覺效果 | `.opencode/agents/asset-developer.md` |
| **Validator Agent** | 驗收、缺陷報告（永不修復） | `.opencode/agents/validator-agent.md` |

### Workflow（Sisyphus 唯一調度，串行執行，避免檔案衝突）

```
Human: "做翻轉鳥"
    ↓
Phase 1: PM Agent → specs/requirements/<feature>.md
         （任務單帶 arch-review: yes/no 標記）
    ↓
Phase 2: Tech Lead Agent（僅當 arch-review=yes）
         → 更新 specs/architecture/ ＋ docs/adr/ 留決策紀錄
    ↓
Phase 3: Developers 串行實作（gameplay → scene → asset）
         執行中發現需要新跨腳本依賴／動共享核心
         → 停手回報 Sisyphus 轉 Tech Lead，不得自行變通
    ↓
Phase 4: Validator Agent → specs/validation/<feature>/round-N.md
         每個 issue 標籤：type(code|architecture|requirement)
                        × severity(blocker|major|minor)
    ↓
Phase 4.5: 動態驗證（Validator 出清單，Sisyphus 執行）
         Validator 把無法靜態驗證的 AC／UX 路徑列成「動態驗證清單」
         （步驟＋預期觀察）；Sisyphus 於 Editor 實測並回填報告——
         回填完成前 Verdict 為暫定，回填後該輪驗收方可關單
    ↓
Phase 5: Sisyphus 按標籤路由：
         code+blocker → Developer 續修
         architecture → Tech Lead
         requirement  → PM
    ↓
Phase 6: blocker 清零 或 同一 task 修滿 2 輪
         → 回報 Human（附全程紀錄；2 輪未解 = 上報人類裁決）
```

**迴圈終止政策**：只有 `blocker` 觸發下一輪修復；`major`／`minor` 累積記錄、不擋驗收。同一 task 最多 2 輪修復，之後停止迴圈上報人類。

### 派發守則：空白結果 ≠ 派工遺失（2026-08-24 靜默失敗事故增訂）

模型供應端會**間歇性靜默失敗**——subagent session 只留下空白 assistant 訊息（特徵：派發後 <1 秒即建立、零內容），不回話也不報錯。這是傳輸層瞬時故障，不是 agent 抗命，也不是工作遺失。Sisyphus 調度時必須遵守：

1. **判定證據先行**——宣告任何「派工遺失／任務不存在」前必須三證齊備：經過時間、session 實際內容、產物 glob 結果。缺一不可判死。
2. **最短寬限窗**——validator／tech-lead 類審查任務 ≥ 5 分鐘；developer 類任務宣告逾時前先查產物落盤狀態。禁止在派發後 1 分鐘內宣告派工遺失。
3. **恢復順序**——收到空結果或閒置逾時：先 `task(task_id="ses_...")` 續跑原 session（保留上下文，實證可恢復）→ 仍失敗 → 稍候再續跑一次 → 同一工作三次皆敗才允許全新派發，並回報 Human 註記。
4. **TODO hook 不是判決**——`[TODO CONTINUATION]` 提醒只要求檢查狀態；收到時先驗事實（session 是否活躍、產物是否存在），不得直接判定失敗並重派。

事故紀錄：2026-08-23 夜間 pm-agent 三度調度與 validator 兩度調度皆為此症狀（空白 assistant 訊息）；同晚 gameplay-developer 完整跑完，ping 測試兩 agent 皆通——設定檔無罪，屬供應端瞬時故障。

### 能力邊界（硬性強制）

邊界由每個 agent frontmatter 的 `permission` 強制執行——文件只是說明，config 才是法律。規則順序：broad deny 在前、specific allow 在後（最後一條符合的規則勝出）。

**Permission 設定注意事項**（2026-08-21 煙霧測試實證）：

1. **改 agent permission 後必須重啟 opencode 才生效**——agent 定義在程序啟動時載入並快取，熱編輯 frontmatter 不會影響已執行中的程序。
2. **edit pattern 必須雙形式並列**——引擎會把路徑正規化成絕對路徑（如 `H:/Document/Unity/flipbird/...`）再比對，相對形式（`specs/requirements/**`）匹配不到，必須同時保留 `**/` 前綴形式（`**/specs/requirements/**`）。六個 agent 的設定檔均已採用雙形式。
3. **deny 側永遠正常**——`"*": deny` 匹配一切路徑格式；allow 側失效時症狀是「全部寫入被拒」，可用此特徵快速判斷是 allow 匹配壞掉而非 deny 誤設。

| Agent | 可寫路徑 | MCP 工具 |
|-------|---------|----------|
| pm-agent | `specs/requirements/**` | 無 |
| tech-lead-agent | `specs/architecture/**`、`docs/adr/**` | 無 |
| gameplay-developer | `Assets/**` | script / components / gameobject / console / tests / unity_reflect / refresh_unity |
| scene-developer | `Assets/**` | scene / gameobject / prefabs / camera / components / material / asset / scriptable_object / console |
| asset-developer | `Assets/**` | material / generate_image / generate_audio / texture / asset / console |
| validator-agent | `specs/validation/**` | 只讀：read_console、run_tests |

全員共通：`bash: deny`、其餘路徑 `edit: deny`、`task: {"*": deny}`——六個 agent 互相不可調用，指揮鏈由 Sisyphus 獨佔。

角色鐵律：

- Validator 永遠不負責修問題，只負責指出問題
- Developer 永遠不負責重新設計架構，只負責實作架構
- Tech Lead 才有權修改架構（`specs/architecture/` + `docs/adr/`）
- PM 才有權修改產品需求與優先級（`specs/requirements/`）

### 記憶系統

每個 agent 有自己的記憶，共享知識放 `shared/`：

```
.scratch/memory/
├── pm-agent.md           ← PM 的記憶
├── tech-lead-agent.md    ← Tech Lead 的記憶
├── gameplay-developer.md ← Gameplay 的記憶
├── scene-developer.md    ← Scene 的記憶
├── asset-developer.md    ← Asset 的記憶
├── validator-agent.md    ← Validator 的記憶
└── shared/
    └── project.md        ← 所有人共享的知識
```

### 協作規則

1. 只寫自己的 memory.md，不改別人的
2. 每個 agent 開頭先讀所有 memory.md（了解別人做了什麼）
3. 遇到衝突 → 寫入自己的 memory 註明，不直接修改
4. 整合時由 Sisyphus 解決衝突
5. Feature 收單（validator PASS＋動態驗證回填完成）後，由 Sisyphus 更新 `shared/project.md` 的目前進度與技術事實——共享記憶腐爛比沒有記憶更危險
6. Memory 精簡：「最近工作」只保留最近 2～3 個 feature 的條目，更早的壓成一行情境索引（細節已在 specs/ 文件，不在 memory 重複存放）；「經驗／教訓」節的蒸餾知識永久保留
7. Agent 設定檔只放角色、權限、規則與穩態 SOP——不放會腐爛的專案現狀（腳本清單、場景結構、shader 名），現狀一律指向 `shared/project.md` 或派工時即時讀取

### 如何調用 True Agent

```python
# Sisyphus 調用方式
task(subagent_type="pm-agent", prompt="分析翻轉鳥需求")
task(subagent_type="tech-lead-agent", prompt="審核 arch-review 任務：新增得分事件匯流排")
task(subagent_type="gameplay-developer", prompt="實作鳥的飛行物理")
task(subagent_type="scene-developer", prompt="建立遊戲場景")
task(subagent_type="asset-developer", prompt="建立鳥的精靈")
task(subagent_type="validator-agent", prompt="驗收 player-jump，對照 specs/requirements/player-jump.md")
```

### Issue tracker

Issues live as local markdown files under `.scratch/<feature>/` in this repo. See `docs/agents/issue-tracker.md`.

兩種 issue 是不同產物，不要混用：

| 種類 | 位置 | 產出者 | 意義 |
|------|------|--------|------|
| **施工票** | `.scratch/<feature>/issues/` | PM／規劃流程 | 實作前拆好的工作項 |
| **缺陷報告** | `specs/validation/<feature>/round-N.md` | Validator | 實作後挑出的問題，帶 type × severity 標籤 |

### Triage labels

Five canonical roles: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Unity game dev skill

A playable-first Unity workflow lives at `.codex/skills/unity-game-dev/`. It covers feature loops, prototyping, diagnosis, review, and Unity-specific checklists (performance, assets, scenes, builds).

**Skill location per tool:**

| Tool | Default skill path | How to access |
|---|---|---|
| Codex | `.codex/skills/` | Native — just use the skill |
| OpenCode | `.opencode/skills/` or `~/.config/opencode/skills/` | Symlink or copy: `New-Item -ItemType SymbolicLink -Path ".opencode\skills\unity-game-dev" -Target ".codex\skills\unity-game-dev"` |
| Claude Code | `.claude/skills/` | Symlink or copy: same approach |
