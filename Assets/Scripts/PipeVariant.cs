using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 水管變體元件（ADR-014／PV1-PV3）：對這根水管套用隨機變體——
/// 隨機選 sprite variant＋同步 collider＋套用 scale。掛 Pipe root。
/// 由 PipeController.Configure() 觸發（PV2 單向依賴，本元件不得引用 PipeController）。
/// </summary>
public class PipeVariant : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] renderers; // 長度 2：索引 0=BottomPipe、1=TopPipe
    [SerializeField] private PolygonCollider2D[] colliders; // 與 renderers 同序（具體型別＝編譯期接線契約，FR-09）
    [SerializeField] private Sprite[] variants; // Multiple atlas 子 sprite 清單

    // NFR-04：GetPhysicsShape 的 List 緩衝重複使用（避免每根 pipe 新建 List）
    private readonly List<Vector2> shapeBuffer = new List<Vector2>();

    /// <summary>套用隨機變體：隨機選 sprite→套到兩段 renderer→同步 collider→套用 scale。每次完整重置（FR-05/BC-1）。</summary>
    public void ApplyRandom(float scale)
    {
        // PV3：variants 空陣列時 return（防 IndexOutOfRange；T3/T4 接線順序保證非空，此為防禦）
        if (variants == null || variants.Length == 0) return;


        // FR-01：套用到 BottomPipe/TopPipe 兩段 renderer
        for (int i = 0; i < renderers.Length; i++)
        {
            // FR-01/FR-02：隨機選一個 sprite，上下管共用同一個（視覺對稱） (先不照文件共用一個視覺對稱)
            Sprite sprite = variants[Random.Range(0, variants.Length)];
            renderers[i].sprite = sprite;
            // FR-04 雙軌：physics shape 主軌／bounds 矩形 fallback
            SyncColliders(sprite, i);
        }


        // FR-03/決策 7：scale 只套 BottomPipe/TopPipe 兩段 transform，不套 root（gap 不跟著縮）
        Vector3 variantScale = new Vector3(scale, scale, 1f);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].transform.localScale = variantScale;
        }
    }

    /// <summary>FR-04：collider 與所選 sprite 外觀一致——有 physics shape 用 shape 點，無則用 bounds 矩形（BC-2）。</summary>
    private void SyncColliders(Sprite sprite, int index)
    {
        int shapeCount = sprite.GetPhysicsShapeCount();

        PolygonCollider2D collider = colliders[index];

        if (shapeCount > 0)
        {
            // 主軌：pathCount 設為 shapeCount（截斷舊 path＋開新 slot＝完整重置，FR-05），逐 shape 寫入
            collider.pathCount = shapeCount;
            for (int p = 0; p < shapeCount; p++)
            {
                shapeBuffer.Clear();
                sprite.GetPhysicsShape(p, shapeBuffer);
                collider.SetPath(p, shapeBuffer.ToArray());
            }
        }
        else
        {
            // fallback（BC-2）：無 physics shape → 用 sprite.bounds 生成矩形 path（4 點：min/max 組合）
            Vector2 min = sprite.bounds.min;
            Vector2 max = sprite.bounds.max;
            Vector2[] rect = new Vector2[]
            {
                new Vector2(min.x, min.y),
                new Vector2(max.x, min.y),
                new Vector2(max.x, max.y),
                new Vector2(min.x, max.y),
            };
            collider.pathCount = 1;
            collider.SetPath(0, rect);
        }
    }
}