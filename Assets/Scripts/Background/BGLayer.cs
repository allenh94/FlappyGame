using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 單一背景層——持有 LayerDef 資料，移植 Mathf.Repeat 取模捲動數學（無縫無漂移）。
/// 分離鐵律（B3）：depth 只進速度、renderOrder 只進 sortingOrder。
/// Tile 生命週期走共用池化 seam（ADR-010，比照 PipeSpawner 收斂至 PooledActives）：
/// 初始化一次建立、穩態只改位置——零 Instantiate／Destroy／GC。
/// Tile 內容：LayerDef.template 有值時 Instantiate 該 prefab（架構 D1：模板即 prefab），
/// 留空時用 placeholder 並發防呆警告（Editor 端提示點在 T7 視窗）。
/// </summary>
public class BGLayer : MonoBehaviour
{
    public const float BaseSpeed = 2f;

    /// <summary>背景過渡（反向快退）時長（1 秒）。</summary>
    private const float ScrollBackDuration = 1f;
    /// <summary>Menu 慢速捲動倍率（相對正常速度）。</summary>
    private const float AmbientSpeedFactor = 0.15f;

    /// <summary>捲動狀態機：Normal 正常捲動；Ambient Menu 慢速捲動；
    /// ScrollingBack 正在執行反向快退回初始位（優先於 Normal/Ambient，且不受凍結限制）。</summary>
    private enum MotionMode { Normal, Ambient, ScrollingBack }

    private LayerDef def;
    private float scrollDistance;
    private float speed;
    private bool frozen;
    private MotionMode motion = MotionMode.Normal;
    private MotionMode postScrollBackMotion = MotionMode.Normal; // 過渡完成後進入的狀態（回 Menu→Ambient，回 Playing→Normal）
    private float scrollBackStart;  // 過渡起點 scrollDistance
    private float scrollBackTimer;  // 過渡已耗時
    private System.Action scrollBackOnComplete; // 過渡完成回呼（Restart 用：恢復鳥＋重生水管）

    // ADR-010：池週期（active-list 追蹤＋重複 get/release 防護＋SetActive/Destroy wiring）收斂至 PooledActives
    private PooledActives<GameObject> tilePool;
    private Sprite placeholderSprite; // round-2 m1：placeholder 資源持有，供 OnDestroy 釋放

    /// <summary>由 BGManager 注入資料並初始化（runtime 對 def 只讀）。</summary>
    public void Initialize(LayerDef layerDef)
    {
        def = layerDef;
        transform.localScale = Vector3.one; // 防父縮放污染子 tile 世界尺寸
        speed = BaseSpeed / Mathf.Max(def.depth, 0.01f);

        if (def.template == null)
            Debug.LogWarning("[BGLayer] Layer '" + def.name + "' 未指定 Template——使用 placeholder。請於 Editor 指定模板。", this);

        InitializePool();
        int count = ComputeTileCount();
        for (int i = 0; i < count; i++)
            tilePool.Get();

        ApplyScrollPositions();
    }

    // ---- Pool（共用池化 seam，ADR-010）----
    // 模板內容由 Tile 元件於建立時一次性套用；穩態不再触碰內容

    private void InitializePool()
    {
        int count = ComputeTileCount();
        // round-2 m1：placeholder 存進實例欄位（原為區域變數），生命週期由 OnDestroy 管理
        placeholderSprite = def.template == null ? CreatePlaceholderSprite() : null;

        // PL2：建立邏輯留呼叫端——createFunc 建 tile template；池週期由 PooledActives 處理
        tilePool = new PooledActives<GameObject>(
            createFunc: () =>
            {
                GameObject tile;
                if (def.template != null)
                {
                    tile = Instantiate(def.template, transform, false);
                }
                else
                {
                    tile = new GameObject("Tile");
                    tile.transform.SetParent(transform, false);
                    tile.AddComponent<SpriteRenderer>().sprite = placeholderSprite;
                }
                tile.AddComponent<Tile>().ApplyTemplate(def.tileWidth, def.renderOrder);
                return tile;
            },
            capacity: count,
            maxSize: count
        );
    }

    // NFR-03：tile 數量由資料推導——N = ceil(可視寬/tileWidth)+1，至少 2 張保證無縫換行。
    // 可視寬於初始化時以主相機在 z=0 平面計算一次（解析度變更不重算，
    // 承襲 legacy 前提：tileWidth ≥ 該層可視寬，見規格 NFR-05）。
    private int ComputeTileCount()
    {
        float viewWidth = GetVisibleWidth();
        int n = Mathf.CeilToInt(viewWidth / def.tileWidth) + 1;
        return Mathf.Max(n, 2);
    }

    // round-2 m3：改為 public——Editor 預覽（BGLayerEditorWindow）與 runtime 共用此推導，單一真源
    public static float GetVisibleWidth()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[BGLayer] 找不到 MainCamera——使用預設可視寬 20。");
            return 20f;
        }
        if (cam.orthographic)
            return cam.orthographicSize * 2f * cam.aspect;

        float dist = Mathf.Abs(cam.transform.position.z); // 相機固定朝 -z 看 z=0 平面
        float height = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return height * cam.aspect;
    }

    // ---- Placeholder ----

    private static Sprite CreatePlaceholderSprite()
    {
        Texture2D tex = new Texture2D(32, 32);
        Color[] pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100);
    }

    // round-2 m1：placeholder 的 Texture/Sprite 是 runtime 物件（非資產），層銷毀時須手動釋放
    private void OnDestroy()
    {
        if (placeholderSprite == null) return;
        Texture2D tex = placeholderSprite.texture;
        Destroy(placeholderSprite);
        if (tex != null) Destroy(tex);
        placeholderSprite = null;
    }

    private void Update()
    {
        if (def == null || def.tileWidth <= 0f) return;

        // 過渡動畫優先：反向快退 scrollDistance → 0（不受凍結限制）。
        if (motion == MotionMode.ScrollingBack)
        {
            scrollBackTimer += Time.deltaTime;
            float p = Mathf.Clamp01(scrollBackTimer / ScrollBackDuration);
            float eased = UITween.EaseOutCubic(p);
            scrollDistance = Mathf.LerpUnclamped(scrollBackStart, 0f, eased);
            ApplyScrollPositions();
            if (p >= 1f)
            {
                scrollDistance = 0f;
                ApplyScrollPositions();
                motion = postScrollBackMotion;
                if (scrollBackOnComplete != null)
                {
                    var cb = scrollBackOnComplete;
                    scrollBackOnComplete = null;
                    cb();
                }
            }
            return;
        }

        // T2：凍結僅作用於 Normal 態；Ambient（Menu 慢速捲動）不受凍結限制。
        if (motion == MotionMode.Normal && frozen) return;

        float step = speed * Time.deltaTime;
        if (motion == MotionMode.Ambient) step *= AmbientSpeedFactor;
        scrollDistance = Mathf.Repeat(scrollDistance + step, def.tileWidth);
        ApplyScrollPositions();
    }

    /// <summary>T2：設定凍結狀態（GameManager → BGManager → BGLayer 單向）。</summary>
    public void SetFrozen(bool value)
    {
        frozen = value;
    }

    /// <summary>切回 Playing：正常捲動（解凍）。</summary>
    public void SetNormal()
    {
        motion = MotionMode.Normal;
        frozen = false;
    }

    /// <summary>切回 Menu：慢速捲動（環境感，選單仍活著）。</summary>
    public void SetAmbient()
    {
        motion = MotionMode.Ambient;
        frozen = false;
    }

    /// <summary>反向快退到初始位，過渡完成後進入 Playing 正常捲動（Restart/Start 用）。</summary>
    public void ScrollBackToNormal(System.Action onComplete = null)
    {
        BeginScrollBack(MotionMode.Normal, onComplete);
    }

    /// <summary>反向快退到初始位，過渡完成後進入 Menu 慢速捲動（BackToMenu 用）。</summary>
    public void ScrollBackToAmbient()
    {
        BeginScrollBack(MotionMode.Ambient, null);
    }

    private void BeginScrollBack(MotionMode post, System.Action onComplete)
    {
        scrollBackStart = scrollDistance;
        scrollBackTimer = 0f;
        postScrollBackMotion = post;
        scrollBackOnComplete = onComplete;
        motion = MotionMode.ScrollingBack;
    }

    // 由單一累計距離推導全部 tile 位置（非各自獨立累加）：相鄰間距恆等於 tileWidth
    private void ApplyScrollPositions()
    {
        var active = tilePool.Active;
        if (active.Count == 0) return;
        float x = -scrollDistance;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] != null)
                active[i].transform.localPosition = new Vector3(x + i * def.tileWidth, 0f, 0f);
        }
    }

    /// <summary>重置到初始位置。</summary>
    public void Reset()
    {
        scrollDistance = 0f;
        ApplyScrollPositions();
    }
}