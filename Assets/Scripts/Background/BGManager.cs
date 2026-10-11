using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背景系統總管——由 BGConfig 資料驅動建立／重置所有背景層。
/// 單向流（B2）：只讀 BGConfig，不回寫。
/// </summary>
public class BGManager : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private BGConfig config;

    private readonly List<BGLayer> layers = new List<BGLayer>();

    private void Awake()
    {
        if (config == null)
        {
            Debug.LogError("[BGManager] BGConfig 未指定——背景系統停用。", this);
            return;
        }

        // TRANSIENT-T5：T5 遷移時決定是否改引用場景預置根物件
        GameObject root = new GameObject("Background");
        root.transform.position = Vector3.zero;

        for (int i = 0; i < config.layers.Count; i++)
        {
            LayerDef def = config.layers[i];
            if (def == null || string.IsNullOrEmpty(def.name)) continue;

            GameObject go = new GameObject(def.name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.zero; // 全層 z=0

            BGLayer layer = go.AddComponent<BGLayer>();
            layer.Initialize(def);
            layers.Add(layer);
        }
    }

    /// <summary>重置所有層到初始位置（死亡重開用）。</summary>
    public void ResetAll()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].Reset();
        }
    }

    /// <summary>T2 凍結機制（B8 單向）：GameManager → BGManager → 各 BGLayer。</summary>
    public void SetFrozen(bool frozen)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].SetFrozen(frozen);
        }
    }

    /// <summary>進入 Playing：所有層正常捲動（解凍）。</summary>
    public void SetNormal()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].SetNormal();
        }
    }

    /// <summary>進入 Menu：所有層慢速捲動（環境感）。</summary>
    public void SetAmbient()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].SetAmbient();
        }
    }

    /// <summary>反向快退到初始位，過渡完成後接 Playing 正常捲動（Restart/Start 用）。
    /// onComplete 在所有層過渡完成後觸發一次（恢復鳥＋重生水管）。</summary>
    public void ScrollBackToStart(System.Action onComplete = null)
    {
        int pending = 0;
        for (int i = 0; i < layers.Count; i++)
            if (layers[i] != null) pending++;

        if (pending == 0)
        {
            if (onComplete != null) onComplete();
            return;
        }

        int remaining = pending;
        System.Action done = () =>
        {
            if (--remaining <= 0 && onComplete != null) onComplete();
        };
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].ScrollBackToNormal(done);
        }
    }

    /// <summary>反向快退到初始位，過渡完成後接 Menu 慢速捲動（BackToMenu 用）。</summary>
    public void ScrollBackToMenu()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] != null) layers[i].ScrollBackToAmbient();
        }
    }
}
