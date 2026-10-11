using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// 按鈕 hover/Select 放大動畫元件（FR-01/FR-02）。
/// 掛在按鈕上，用 EventTrigger（PointerEnter/PointerExit/Select/Deselect）＋ UITween 做 scale 動畫。
/// 同時處理滑鼠（PointerEnter/Exit）與鍵盤（Select/Deselect，BC-4）。
/// 預設 autoRegisterEvents=true 會程式化 self-register EventTrigger；若 scene 已手動接線，
/// 設 false 避免重複註冊（AddEntry 亦會跳過已存在的 eventID）。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ButtonHoverScale : MonoBehaviour
{
    [SerializeField] private float hoverScale = 1.1f;   // 放大目標（FR-01）
    [SerializeField] private float duration = 0.2f;     // 動畫時長
    [SerializeField] private bool autoRegisterEvents = true; // 程式化 self-register EventTrigger

    private RectTransform _rect;
    private Vector3 _baseScale;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _baseScale = _rect.localScale;
        if (autoRegisterEvents)
            RegisterEvents();
    }

    private void OnDisable()
    {
        // M1：面板 SetActive(false) 隱藏時元件不銷毀（OnDestroy 不觸發），
        // 進行中的 hover 放大 tween 會跑完殘留 1.1。此處停止 tween 並強制歸位到基礎 scale。
        if (_rect != null)
        {
            UITween.StopAll(_rect); // 停止進行中的 scale 動畫
            _rect.localScale = _baseScale; // 強制歸位（StopAll 會 snap 到 to 值，可能為 1.1）
        }
    }

    private void OnDestroy()
    {
        // 停止進行中的 scale 動畫並歸位（多一層保險）
        if (_rect != null)
            UITween.StopAll(_rect);
    }

    /// <summary>程式化掛載 EventTrigger 並註冊 PointerEnter/Exit/Select/Deselect。</summary>
    private void RegisterEvents()
    {
        var trigger = GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = gameObject.AddComponent<EventTrigger>();

        AddEvent(trigger, EventTriggerType.PointerEnter, _ => OnHover(true));
        AddEvent(trigger, EventTriggerType.PointerExit, _ => OnHover(false));
        AddEvent(trigger, EventTriggerType.Select, _ => OnHover(true));
        AddEvent(trigger, EventTriggerType.Deselect, _ => OnHover(false));
    }

    /// <summary>新增事件；若該 eventID 已存在（scene 已接線）則跳過，避免重複註冊。</summary>
    private static void AddEvent(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> action)
    {
        foreach (var existing in trigger.triggers)
            if (existing.eventID == type) return;

        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    private void OnHover(bool hovered)
    {
        if (_rect == null) return;
        UITween.StopAll(_rect); // 中斷進行中的 scale 動畫
        Vector3 to = hovered ? _baseScale * hoverScale : _baseScale;
        UITween.ScaleTo(_rect, to, duration, 0f, UITween.EaseOutBack);
    }
}