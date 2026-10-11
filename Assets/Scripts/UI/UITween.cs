using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 純 C# 自建 tween 工具（NFR-03，無第三方套件）。
/// 提供 easing 函式（easeOutBack 過沖彈跳、easeOutCubic 滑入）與
/// scale / anchoredPosition 動畫執行器（播放／中途停止／歸位）。
/// 由 UITweenRunner（Update 驅動）執行；timeScale 恆為 1，用 Time.deltaTime（NFR-04）。
/// 自包含，不依賴 gameplay 核心。
/// </summary>
public static class UITween
{
    /// <summary>easeOutBack：過沖彈跳（FR-01/FR-02/FR-03/FR-04）。</summary>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>easeOutCubic：滑入。</summary>
    public static float EaseOutCubic(float t)
    {
        float u = t - 1f;
        return u * u * u + 1f;
    }

    /// <summary>easeOutQuad：簡易滑入。</summary>
    public static float EaseOutQuad(float t)
    {
        return t * (2f - t);
    }

    /// <summary>對 RectTransform 做 scale 動畫（目前值 → to）。</summary>
    public static void ScaleTo(RectTransform target, Vector3 to, float duration, float delay, Func<float, float> ease, Action onComplete = null)
    {
        if (target == null) return;
        Runner.StartTween(new Tween(target, TweenType.Scale, target.localScale, to, duration, delay, ease, onComplete));
    }

    /// <summary>對 RectTransform 做 anchoredPosition 動畫（目前值 → to）。</summary>
    public static void AnchoredPositionTo(RectTransform target, Vector2 to, float duration, float delay, Func<float, float> ease, Action onComplete = null)
    {
        if (target == null) return;
        Runner.StartTween(new Tween(target, TweenType.AnchoredPosition, target.anchoredPosition, to, duration, delay, ease, onComplete));
    }

    /// <summary>泛用 float 值動畫（目前值 → to）——供整數分數跳動等用途，每次 tick 呼叫 apply(current)。</summary>
    public static void FloatTo(object token, Action<float> apply, float from, float to, float duration, Func<float, float> ease, Action onComplete = null)
    {
        if (apply == null) return;
        Runner.StartValueTween(new ValueTween(token, apply, from, to, duration, 0f, ease, onComplete));
    }

    /// <summary>停止指定 token 上的所有 float 值動畫。</summary>
    public static void StopFloatTweens(object token)
    {
        Runner.StopValueTweens(token);
    }

    /// <summary>停止目標上所有 tween 並歸位（snap 到 to 值，FR-06/BC-1/BC-2）。</summary>
    public static void StopAll(RectTransform target)
    {
        if (target == null) return;
        Runner.StopAll(target);
    }

    private static UITweenRunner Runner
    {
        get
        {
            if (_runner == null)
            {
                var go = new GameObject("UITweenRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<UITweenRunner>();
            }
            return _runner;
        }
    }
    private static UITweenRunner _runner;
}

internal enum TweenType { Scale, AnchoredPosition }

/// <summary>單一 tween 執行單元。</summary>
internal sealed class Tween
{
    public readonly RectTransform target;
    public readonly TweenType type;
    public readonly Vector3 from;
    public readonly Vector3 to;
    public readonly float duration;
    public readonly float delay;
    public readonly Func<float, float> ease;
    public readonly Action onComplete;
    public float elapsed;

    public Tween(RectTransform target, TweenType type, Vector3 from, Vector3 to, float duration, float delay, Func<float, float> ease, Action onComplete)
    {
        this.target = target;
        this.type = type;
        this.from = from;
        this.to = to;
        this.duration = duration;
        this.delay = delay;
        this.ease = ease;
        this.onComplete = onComplete;
    }
}

/// <summary>泛用 float 值動畫執行單元（token 供定向停止）。</summary>
internal sealed class ValueTween
{
    public readonly object token;
    public readonly Action<float> apply;
    public readonly float from;
    public readonly float to;
    public readonly float duration;
    public readonly float delay;
    public readonly Func<float, float> ease;
    public readonly Action onComplete;
    public float elapsed;

    public ValueTween(object token, Action<float> apply, float from, float to, float duration, float delay, Func<float, float> ease, Action onComplete)
    {
        this.token = token;
        this.apply = apply;
        this.from = from;
        this.to = to;
        this.duration = duration;
        this.delay = delay;
        this.ease = ease;
        this.onComplete = onComplete;
    }
}

/// <summary>Update 驅動的 tween 執行器（NFR-05：僅播放期間執行，結束即移除）。</summary>
public class UITweenRunner : MonoBehaviour
{
    private readonly List<Tween> _tweens = new List<Tween>();
    private readonly List<ValueTween> _valueTweens = new List<ValueTween>();

    internal void StartTween(Tween tween)
    {
        if (tween == null || tween.target == null) return;
        _tweens.Add(tween);
    }

    internal void StartValueTween(ValueTween tween)
    {
        if (tween == null) return;
        _valueTweens.Add(tween);
    }

    public void StopAll(RectTransform target)
    {
        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            if (_tweens[i].target == target)
            {
                Apply(_tweens[i], _tweens[i].to); // 歸位：snap 到 to
                _tweens.RemoveAt(i);
            }
        }
    }

    public void StopValueTweens(object token)
    {
        for (int i = _valueTweens.Count - 1; i >= 0; i--)
        {
            if (_valueTweens[i].token == token)
            {
                ValueTween t = _valueTweens[i];
                t.apply(t.to); // 歸位：snap 到 to
                _valueTweens.RemoveAt(i);
            }
        }
    }

    private void Update()
    {
        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            Tween t = _tweens[i];
            if (t.target == null) { _tweens.RemoveAt(i); continue; } // 目標已銷毀
            t.elapsed += Time.deltaTime;
            if (t.elapsed < t.delay) continue;
            float p = Mathf.Clamp01((t.elapsed - t.delay) / Mathf.Max(t.duration, 0.0001f));
            float e = t.ease != null ? t.ease(p) : p;
            Apply(t, Vector3.LerpUnclamped(t.from, t.to, e));
            if (p >= 1f)
            {
                _tweens.RemoveAt(i);
                if (t.onComplete != null) t.onComplete();
            }
        }

        for (int i = _valueTweens.Count - 1; i >= 0; i--)
        {
            ValueTween t = _valueTweens[i];
            t.elapsed += Time.deltaTime;
            if (t.elapsed < t.delay) continue;
            float p = Mathf.Clamp01((t.elapsed - t.delay) / Mathf.Max(t.duration, 0.0001f));
            float e = t.ease != null ? t.ease(p) : p;
            t.apply(Mathf.LerpUnclamped(t.from, t.to, e));
            if (p >= 1f)
            {
                t.apply(t.to);
                _valueTweens.RemoveAt(i);
                if (t.onComplete != null) t.onComplete();
            }
        }
    }

    private static void Apply(Tween t, Vector3 v)
    {
        if (t.target == null) return;
        switch (t.type)
        {
            case TweenType.Scale: t.target.localScale = v; break;
            case TweenType.AnchoredPosition: t.target.anchoredPosition = v; break;
        }
    }
}