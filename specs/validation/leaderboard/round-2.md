# Validation Report: leaderboard round 2

- **Date**: 2026-10-05
- **Task Sheet**: `specs/requirements/leaderboard.md`
- **Round 1**: `specs/validation/leaderboard/round-1.md`
- **Verdict**: **PASS（暫定）** — blocker 清零；待互動動態項（D2/D4-D9/D12/D14）回填後定案

> 驗證方法限制聲明：本輪無 git，修復範圍以「檔案行數變化＋逐行核對」確認（非 diff）。測試結果以 `unityMCP_run_tests`（EditMode）＋ `TestResults.xml` 落盤檔交叉核對；Console 以 `unityMCP_read_console` 即時讀取。

---

## 1. Blocker 複驗

### B1（round-1 blocker）→ **FIXED**

- **修復內容**: `Assets/Tests/Editor/LeaderboardTests.cs:220-241`——`FakePersistence_Injection_DoesNotTouchRealPlayerPrefs` 改為 hermetic：開頭 `PlayerPrefs.DeleteKey("flipbird_leaderboard")`、`try` 內保留原斷言（fake 注入不寫真實 key）、`finally` 再 `DeleteKey` 自我清理。
- **獨立複驗證據**:
  - 本輪重新執行 `unityMCP_run_tests`（Mode=EditMode），`TestResults.xml`（start-time 2026-10-04 17:15:05Z）記載：`result="Passed" total="18" passed="18" failed="0" inconclusive="0" skipped="0"`。
  - 目標測試明確 Passed：`<test-case id="1015" name="FakePersistence_Injection_DoesNotTouchRealPlayerPrefs" … result="Passed" …>`（`TestResults.xml:16`）。
  - 全檔 grep `result="Failed"|result="Skipped"|result="Inconclusive"` 於 XML **零命中**。
  - 測試檔 `[Test]` 方法數 = **18**（與 round-1 一致，無新增/刪除測試）。
- **判定**: B1 清零。修復正確——測試不再依賴環境預存狀態，且 `finally` 保證失敗時亦清理。

### B2（round-1 blocker，已由 Phase 4.5 動態推翻）→ **確認關閉＋防禦性硬化無回歸**

- **背景**: round-1 靜態推斷 `GetComponentInChildren<TextMeshProUGUI>()` 會因列 inactive 而回 null；Sisyphus Phase 4.5 實測推翻——Unity 此 API 的 includeInactive 判準為子物件的 **activeSelf**（非 activeInHierarchy），列文字解析正常（browse 實測顯示 `LEADERBOARD`／`1.  ALLEN S`，截圖 `Assets/Screenshots/leaderboard_d1_browse.png`）。**B2 非缺陷，round-1 已關閉。**
- **本輪防禦性變更**: `LeaderboardAnimator.cs:51` 改 `child.GetComponentInChildren<TextMeshProUGUI>(true)`（比照 `PlayerController.cs:58` 先例）。
- **無回歸核對**:
  - 檔案行數 219（與 round-1 相同）；grep `GetComponentInChildren` 僅 :51 一處；其餘 `Render`/`PlayQualify`/`StopAll`/`ReRank`/`MoveRow`/`SetRowText`/`SetRowScore`/`SetRowColor` 逐行核對**與 round-1 完全一致**。
  - 每列僅一個 TMP 子物件（`RowText_N`），`includeInactive=true` 不引入多候選歧義。
  - 行為等價（對 active 物件結果不變；對 inactive 物件由「找不到」變「找到」，正是防禦目的）。
- **判定**: 無回歸，變更安全。

---

## 2. Console 複驗

- `unityMCP_read_console`（error/warning）本輪僅 2 筆：
  1. `Assets/Scripts/ScoreTrigger.cs(12,27): warning CS0618: 'Object.FindObjectOfType<T>()' is obsolete`——**既有**、任務明示可接受。
  2. `MCP-FOR-UNITY [WebSocket] Unexpected receive error`——**MCP 外掛自身噪音**，非遊戲缺陷。
- **零遊戲程式碼 error**。round-1 的 2 筆 NRE（UIController.cs:107/121）本輪**未復現**（該 NRE 為 domain-reload 觸發的既有潛在缺陷，見 M1；非持續性）。

---

## 3. 殘餘累積項（維持記錄、不擋驗收）

| # | type | severity | 摘要 | 本輪狀態 |
|---|------|----------|------|----------|
| M1 | code | major | Console NRE（UIController.cs:107/121）——既有 domain-reload 潛在 null 安全缺陷，非 leaderboard 引入、非 gameplay 影響 | 維持；本輪未復現 |
| M2 | code | major | `Submit` 未呼叫 `LeaderboardRules.Qualifies`（FR-05/LB6 單一事實來源；實際流程等價，score≤0 邊界分歧） | 維持 |
| m1 | code | minor | `PlayQualify` 未檢 `result.Qualified`（Rank=-1 時 clamp 成 0 錯動畫） | 維持 |
| m2 | code | minor | 名稱 modal 全螢幕遮擋返回鈕（NFR-06「返回鈕恆可用」字面不成立） | 維持 |
| m3 | code | minor | `_Recovery/0.unity` 殘留退休 UIController 欄位 | 維持 |
| m4 | code | minor | `LeaderboardRules.Qualifies` 假設 entries 已排序且 ≤10 | 維持 |
| m5 | code | minor | `MoveRow` 每幀重啟 tween 可能視覺抖動 | 維持 |
| m6 | code | minor | `StopAll` 不還原列文字/分數 | 維持 |
| m7 | requirement | minor | 規格「排行榜」字面未同步改英文（文件漂移） | 維持 |

---

## 4. 新引入缺陷檢查

### m8（新，code/minor）: hermetic 測試會清掉 Editor 的真實 `flipbird_leaderboard` 資料

- **type**: code ｜ **severity**: minor
- **事實**: `LeaderboardTests.cs:225/239` 於測試開頭與 `finally` 皆 `PlayerPrefs.DeleteKey("flipbird_leaderboard")`。此使測試 hermetic（正確），但副作用是**每次執行 EditMode 測試即刪除 Editor 的榜單資料**（開發者實機遊玩累積的榜單會被清空）。
- **影響範圍**: 僅 Editor 測試環境（PlayerPrefs 為 Editor 專屬，不影響出貨 build）；不影響本 feature 功能。
- **建議方向**（僅供路由參考）: 改為「先讀原值 → DeleteKey → 斷言 → finally 還原原值」，兼顧 hermetic 與不破壞資料。
- **判定**: 不擋驗收（test-only、Editor-only、無功能影響）。

**其餘新缺陷**: 無。本輪修復僅觸及 2 檔（`LeaderboardTests.cs` 測試本體、`LeaderboardAnimator.cs:51` 一行），未觸及產品邏輯、場景、架構。

---

## 5. 動態驗證殘餘（待互動實測回填）

round-1 Phase 4.5 已回填 D1（PASS，B2 推翻）、D3/D10（程式冒煙 PASS）、D11（部分 PASS）、D13/D15（PASS）。以下仍待互動實測：

| # | 步驟 | 預期觀察 | 狀態 |
|---|------|----------|------|
| D2 | 遊玩得分 >0 → 撞死 → 淡出完成 | GameOver 後顯示 LeaderboardScreen（取代 GameOverPane）＋名稱 modal；預填上次名稱 | 待填 |
| D4 | 名稱 modal → CANCEL 或 Esc | 不 Submit、不播動畫；榜單維持既有條目 | 待填 |
| D5 | 名稱留空 → CONFIRM | 視同取消，不上榜 | 待填 |
| D6 | 第二次上榜 | 名稱欄位預填上次輸入 | 待填 |
| D7 | 輸入 >12 字元 | 欄位限 12 字元 | 待填 |
| D8 | 分數未過 Qualify | 顯示既有 GameOverPane，無任何 High Score 文字 | 待填 |
| D9 | 上榜畫面點 BACK TO MENU / RESTART | 分別回 Menu / 重開新局 | 待填 |
| D12 | 全程 Play mode 觀察 Console | 零遊戲程式碼 error | 待填 |
| D14 | 破紀錄但未上榜（BC-7） | 不顯示任何 High Score 文字；ScoreSession 資料照寫 | 待填 |

> 上述互動項為 UX 路徑，靜態與程式冒煙已覆蓋邏輯面；回填前 Verdict 為**暫定**。

---

## 6. 逐條 AC 複驗（與 round-1 對照）

round-1 靜態全過項（FR-01~14 除 M2、BC-1~7、NFR-01~06 除 m2、LB1-LB8、場景結構/接線/EventSystem/High Score 退休/兩 key 分離/UI 零 PlayerPrefs/UITween 零改動/ScoreSession 零改動）**本輪無變動**——修復未觸及這些檔案，維持 PASS。B1 由 FAIL 轉 PASS；B2 由「靜態推斷 blocker」轉「動態推翻、非缺陷」。

---

## Statistics

- **blocker**: 0（B1 FIXED；B2 動態推翻、非缺陷）
- **major**: 2（M1、M2——維持累積）
- **minor**: 8（m1-m7 維持＋**m8 新增**）
- **動態驗證殘餘**: 9 項（D2/D4/D5/D6/D7/D8/D9/D12/D14）

## 關鍵結論

1. **B1 清零**：EditMode 18/18 Passed（`TestResults.xml` `passed="18" failed="0"`），目標測試 `FakePersistence_Injection_DoesNotTouchRealPlayerPrefs` 明確 Passed。
2. **B2 關閉**：Phase 4.5 動態推翻（列文字解析正常）；本輪防禦性硬化 `includeInactive=true` 無回歸。
3. **Console 零遊戲 error**（僅既有 CS0618 warning＋MCP 噪音）。
4. **無新 blocker/major**；新增 1 minor（m8：測試刪除 Editor 榜單資料，test-only）。
5. **Verdict = PASS（暫定）**——待 D2/D4-D9/D12/D14 互動實測回填後定案。
