# specs/

本目錄是 flipbird 多 agent 協作的**規格真相來源**，按角色劃分寫入權（由 OpenCode permission 硬性強制）：

| 目錄 | 擁有者（唯一可寫） | 內容 |
|------|------|------|
| `requirements/` | pm-agent | 產品需求與任務單，一個功能一個檔案 |
| `architecture/` | tech-lead-agent | 系統／遊戲玩法／場景架構規則、依賴規則 |
| `validation/` | validator-agent | 驗收缺陷報告，按功能開子資料夾（`<feature>/round-N.md`） |

其他所有人都只有讀取權。決策紀錄（ADR）在 `docs/adr/`，同樣由 tech-lead-agent 維護。

注意：`.scratch/<feature>/issues/` 是**施工票**（實作前拆好的工作項），與 `specs/validation/` 的**缺陷報告**（實作後挑出的問題）是兩種不同產物，不要混用。
