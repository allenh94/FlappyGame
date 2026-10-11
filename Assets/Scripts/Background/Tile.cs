using UnityEngine;

/// <summary>
/// Pooled tile 實例——結構複製自 TileTemplate prefab（架構 D1：模板即 prefab）。
/// 建立時由 BGLayer 呼叫 ApplyTemplate 套用世界尺寸與遮擋帶；之後為純被動物件。
/// </summary>
public class Tile : MonoBehaviour
{
    /// <summary>套用 Layer 設定：世界寬正規化＋sortingOrder 遮擋帶。</summary>
    public void ApplyTemplate(float tileWidth, int renderOrderBand)
    {
        NormalizeWorldWidth(tileWidth);
        ApplyRenderOrderBand(renderOrderBand);
    }

    // 依全部子 Renderer 的合併 bounds 換算根縮放，使 tile 世界寬＝tileWidth。
    // （單一 renderer 時等價 legacy ApplyWorldSize；多子物件時以整體外框計）
    private void NormalizeWorldWidth(float tileWidth)
    {
        if (tileWidth <= 0f) return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        if (combined.size.x <= 0f) return;
        float scale = tileWidth / combined.size.x;
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    // §2：層帶＝renderOrder（間隔 10 由配置端保證）；模板內子物件保留相對偏移：
    // 子 renderer 在 prefab 中 authored 的 sortingOrder 視為層內 ±9 微調量，runtime 加上層帶基底。
    private void ApplyRenderOrderBand(int band)
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sortingOrder = band + renderers[i].sortingOrder;
    }
}
