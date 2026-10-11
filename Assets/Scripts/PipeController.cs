using UnityEngine;
using UnityEngine.Pool;

public class PipeController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    [SerializeField] private GameObject bottomPipe;
    [SerializeField] private GameObject topPipe;

    // FR-06/FR-07（ADR-014）：變體套用元件＋scale 範圍（prefab 接線，T3）
    [SerializeField] private PipeVariant pipeVariant;
   
    private bool isActive;

    private void Update()
    {
        // T2：非 Playing 態（Menu/Paused/GameOver）不移動
        // ADR-011：狀態查詢走 GameStateAuthority（GA1），不再持 GameManager 引用
        if (!GameStateAuthority.IsPlaying()) return;

        // Move left
        transform.position += Vector3.left * moveSpeed * Time.deltaTime;
    }

    /// <summary>單一對外 interface（ADR-009）：設定根位置 + 間隙，一次完成。</summary>
    public void Configure(Vector3 position, float gapSize,float horizontalOffsetRange,float maxScale,float minScale)
    {
        transform.position = position;
        SetOffsetAndGap(gapSize,horizontalOffsetRange);
        // FR-06/BC-4（ADR-014）：Configure 內觸發變體套用（PV2 單向）；null guard 防未接線
        if (pipeVariant != null)
            pipeVariant.ApplyRandom(Random.Range(minScale, maxScale));
    }

    /// <summary>依 gapSize 置中上下管 localPosition（原 setCenterGap 邏輯，PascalCase）。</summary>
    private void SetOffsetAndGap(float gapSize,float horizontalOffsetRange)
    {
        float randomX = Random.Range(-horizontalOffsetRange/2.0f, horizontalOffsetRange/2.0f);
        bottomPipe.transform.localPosition = new Vector3(randomX, -gapSize * 0.5f, 0);
        
        randomX = Random.Range(-horizontalOffsetRange/2.0f, horizontalOffsetRange/2.0f);
        topPipe.transform.localPosition = new Vector3(randomX, gapSize * 0.5f, 0);
    }
    
}
