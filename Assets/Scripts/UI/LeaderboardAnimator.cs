using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 排行榜演出元件（ADR-015 決策 5／LB7）——獨立演出執行，UIController 只負責「何時觸發」。
/// Render(entries)：渲染榜單列；PlayQualify(result)：上榜演出（榜底滑入＋滾分 2s easeOut＋即時重排）；
/// StopAll()：中斷歸位（比照 StopPanelTweens 語義，BC-5）。
/// 動畫目標一律榜單列 child RectTransform（B3 教訓：根 Canvas 位移視覺無效）。
/// 沿用 UITween 原語（UITween.cs 零改動）。
/// </summary>
public class LeaderboardAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform rowsContainer;   // 榜單列容器（Top10；row＝其直接子物件）
    [SerializeField] private TextMeshProUGUI emptyText;     // 0 筆時「暫無記錄」佔位

    [Header("Row Layout")]
    [SerializeField] private float firstRowY = 110f;        // 第一列中心 y（容器內）
    [SerializeField] private float rowSpacing = 30f;        // 列間距
    [SerializeField] private Vector2 slideInOffset = new Vector2(0f, -60f); // 新條目榜底起點偏移（FR-14）

    [Header("Qualify Animation（FR-14）")]
    [SerializeField] private float scoreCountDuration = 2f; // 滾分總時長（2 秒）
    [SerializeField] private float reRankDuration = 0.3f;   // 重排移動時長（easeOut 系）

    private sealed class RowData
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
    }

    private readonly List<RowData> _rows = new List<RowData>();
    private LeaderboardEntry[] _entries = new LeaderboardEntry[0];
    private bool _isQualifying;
    private int _lastNewSlot = -1;

    private void Awake()
    {
        // 解析 rowsContainer 下所有直接子物件為列（Row_N，各含一個 TMP 子文字）
        if (rowsContainer == null) return;
        for (int i = 0; i < rowsContainer.childCount; i++)
        {
            Transform child = rowsContainer.GetChild(i);
            var rect = child as RectTransform;
            if (rect == null) continue;
            _rows.Add(new RowData
            {
                rect = rect,
                text = child.GetComponentInChildren<TextMeshProUGUI>(true) // B2：includeInactive=true（比照 PlayerController.cs:58 先例，防禦 activeSelf=false）
            });
        }
    }

    /// <summary>渲染榜單列（只渲染實際筆數；0 筆顯示「暫無記錄」佔位）。</summary>
    public void Render(LeaderboardEntry[] entries)
    {
        StopAll();
        _entries = entries ?? new LeaderboardEntry[0];
        int count = _entries.Length;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool active = i < count;
            _rows[i].rect.gameObject.SetActive(active);
            if (!active) continue;
            _rows[i].rect.anchoredPosition = SlotPosition(i);
            SetRowText(_rows[i], i, _entries[i]);
            SetRowColor(_rows[i], i);
        }
        if (emptyText != null)
            emptyText.gameObject.SetActive(count == 0);
    }

    /// <summary>
    /// 上榜演出（FR-14）：新條目自榜底（第 N+1 位，+offset）以 AnchoredPositionTo 滑入；
    /// 分數 FloatTo 0→目標、2 秒 easeOut（越接近越慢）；名次即時重排——滾動期間依目前滾動值
    /// 與既有列比較，逐列 AnchoredPositionTo 到新名次位置（同分 FCFS：新條目在既有同分之下）。
    /// 動畫目標一律 row child RectTransform（B3 教訓）。
    /// </summary>
    public void PlayQualify(SubmitResult result)
    {
        StopAll();
        _entries = result.Entries ?? new LeaderboardEntry[0];
        int count = _entries.Length;
        if (count == 0) return;

        int newIndex = Mathf.Clamp(result.Rank - 1, 0, count - 1);
        int newScore = _entries[newIndex].score;
        int bottomIndex = count - 1;

        // 渲染全部列：既有列置於「插入前」位置，新條目置於榜底 + offset（名字已於確認時填入）
        for (int i = 0; i < _rows.Count; i++)
        {
            bool active = i < count;
            _rows[i].rect.gameObject.SetActive(active);
            if (!active) continue;
            SetRowText(_rows[i], i, _entries[i]);
            SetRowColor(_rows[i], i);
            _rows[i].rect.anchoredPosition = (i == newIndex)
                ? SlotPosition(bottomIndex) + slideInOffset
                : SlotPosition(PreInsertionSlot(i, newIndex));
        }
        if (emptyText != null)
            emptyText.gameObject.SetActive(false);

        // 新條目分數從 0 開始滾動
        SetRowScore(_rows[newIndex], newIndex, 0);

        _isQualifying = true;
        _lastNewSlot = -1; // 強制第一次 ReRank 移動（自榜底 + offset 滑入）
        UITween.FloatTo(this, v =>
        {
            if (!_isQualifying) return;
            int counted = Mathf.RoundToInt(v);
            SetRowScore(_rows[newIndex], newIndex, counted);
            ReRank(newIndex, counted);
        }, 0f, newScore, scoreCountDuration, UITween.EaseOutCubic, () =>
        {
            if (!_isQualifying) return;
            _isQualifying = false;
            SetRowScore(_rows[newIndex], newIndex, newScore);
            // 最終歸位
            for (int i = 0; i < count; i++)
            {
                UITween.StopAll(_rows[i].rect);
                _rows[i].rect.anchoredPosition = SlotPosition(i);
            }
        });
    }

    /// <summary>中斷歸位（BC-5）：停止該榜單相關全部 tween（逐列 StopAll＋滾分 StopFloatTweens）並歸位。</summary>
    public void StopAll()
    {
        _isQualifying = false;
        UITween.StopFloatTweens(this);
        for (int i = 0; i < _rows.Count; i++)
        {
            UITween.StopAll(_rows[i].rect);
            if (i < _entries.Length)
                _rows[i].rect.anchoredPosition = SlotPosition(i);
        }
    }

    /// <summary>即時重排：依目前滾動值與既有列比較，逐列 AnchoredPositionTo 到新名次位置（同分 FCFS）。</summary>
    private void ReRank(int newIndex, int countedScore)
    {
        int count = _entries.Length;
        // 新條目目前名次（1-based）＝1 + 既有列中 score >= 目前滾動值的數量（同分 FCFS：新條目在既有同分之下）
        int currentRank = 1;
        for (int i = 0; i < count; i++)
        {
            if (i == newIndex) continue;
            if (_entries[i].score >= countedScore) currentRank++;
        }
        int currentSlot = currentRank - 1;

        // 新條目：名次改變時移動（初始滑入由第一次 ReRank 處理）
        if (currentSlot != _lastNewSlot)
        {
            _lastNewSlot = currentSlot;
            MoveRow(_rows[newIndex], currentSlot);
        }

        // 既有列：score >= 滾動值（尚未被超越）→ 維持插入前位置；score < 滾動值（已被超越）→ 最終位置
        for (int i = 0; i < count; i++)
        {
            if (i == newIndex) continue;
            int targetSlot = _entries[i].score >= countedScore
                ? PreInsertionSlot(i, newIndex)
                : i; // 最終位置（i 即最終 slot）
            MoveRow(_rows[i], targetSlot);
        }
    }

    /// <summary>既有列在「新條目插入前」的位置：新條目最終 slot 之後的列，插入前位置 = 最終位置 - 1。</summary>
    private int PreInsertionSlot(int entryIndex, int newIndex)
    {
        return entryIndex > newIndex ? entryIndex - 1 : entryIndex;
    }

    private void MoveRow(RowData row, int slot)
    {
        Vector2 target = SlotPosition(slot);
        if (row.rect.anchoredPosition == target) return;
        UITween.StopAll(row.rect); // 避免同目標 tween 累積（m2 防護）
        UITween.AnchoredPositionTo(row.rect, target, reRankDuration, 0f, UITween.EaseOutQuad);
    }

    private Vector2 SlotPosition(int index)
    {
        return new Vector2(0f, firstRowY - index * rowSpacing);
    }

    private void SetRowText(RowData row, int index, LeaderboardEntry entry)
    {
        if (row.text == null) return;
        row.text.text = string.Format("{0}.  {1}  {2}", index + 1, entry.playerName, entry.score);
    }

    private void SetRowScore(RowData row, int index, int score)
    {
        if (row.text == null) return;
        LeaderboardEntry entry = _entries[index];
        row.text.text = string.Format("{0}.  {1}  {2}", index + 1, entry.playerName, score);
    }

    private void SetRowColor(RowData row, int index)
    {
        if (row.text == null) return;
        switch (index)
        {
            case 0: row.text.color = new Color(1f, 0.85f, 0.2f); break;    // 金（FR-08 前三名慣例）
            case 1: row.text.color = new Color(0.85f, 0.85f, 0.9f); break; // 銀
            case 2: row.text.color = new Color(0.8f, 0.5f, 0.2f); break;   // 銅
            default: row.text.color = Color.white; break;
        }
    }
}