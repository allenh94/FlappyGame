using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 背景層配置編輯器（FR-14／FR-15／FR-10／FR-16）——BGConfig 的唯一寫入者。
/// 單向流：本視窗寫 SO，Runtime 唯讀；Play mode 中禁用編輯。
/// 預覽僅在 Edit mode 於場景建立暫存物件（HideFlags.DontSave），
/// 關閉預覽／關閉視窗時全部 DestroyImmediate——不留髒資料。
/// </summary>
public class BGLayerEditorWindow : EditorWindow
{
    private const string DefaultConfigPath = "Assets/Config/Background/BGConfig.asset";

    private BGConfig config;
    private Vector2 scrollPos;
    private bool previewOn;
    private readonly List<GameObject> previewRoots = new List<GameObject>();
    private Sprite previewSprite;

    [MenuItem("Window/Background/BG Layer Editor")]
    private static void Open()
    {
        GetWindow<BGLayerEditorWindow>("BG Layer Editor");
    }

    private void OnEnable()
    {
        if (config == null)
            config = AssetDatabase.LoadAssetAtPath<BGConfig>(DefaultConfigPath);
    }

    private void OnDisable()
    {
        ClearPreview();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            config = (BGConfig)EditorGUILayout.ObjectField("Config", config, typeof(BGConfig), false);
            if (GUILayout.Button("New", GUILayout.Width(48)))
                CreateConfigAsset();
        }

        if (config == null)
        {
            EditorGUILayout.HelpBox("未選擇 BGConfig——請指定既有資產或按 New 建立。", MessageType.Info);
            return;
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play mode 中修改配置定義為不支援，請停止播放後編輯。", MessageType.Warning);
            return;
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        DrawLayerList();
        EditorGUILayout.EndScrollView();

        DrawPreviewBar();
    }

    // ---- T6：Layer List（FR-14）----

    private void DrawLayerList()
    {
        EditorGUILayout.LabelField($"Layers ({config.layers.Count})", EditorStyles.boldLabel);

        for (int i = 0; i < config.layers.Count; i++)
        {
            LayerDef def = config.layers[i];
            if (def == null) continue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    def.name = EditorGUILayout.TextField(def.name);
                    if (GUILayout.Button("▲", GUILayout.Width(24)) && i > 0)
                    { Swap(i, i - 1); return; }
                    if (GUILayout.Button("▼", GUILayout.Width(24)) && i < config.layers.Count - 1)
                    { Swap(i, i + 1); return; }
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    { config.layers.RemoveAt(i); Persist(); RebuildPreviewIfOn(); return; }
                }

                EditorGUI.BeginChangeCheck();
                def.depth = Mathf.Max(0.01f, EditorGUILayout.FloatField("Depth", def.depth));
                def.renderOrder = EditorGUILayout.IntField("Render Order", def.renderOrder);
                def.tileWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField("Tile Width", def.tileWidth));
                def.template = (GameObject)EditorGUILayout.ObjectField("Template", def.template, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Persist();
                    RebuildPreviewIfOn();
                }

                // T7 防呆：未指定或參考遺失（含模板資產遭刪除）
                if (def.template == null)
                    EditorGUILayout.HelpBox("Template 未指定或已遺失——Runtime 將使用 placeholder 並顯示警告。", MessageType.Warning);

                // FR-10：Template 內容編輯——以 Prefab Mode 開啟
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (def.template != null && GUILayout.Button("Edit Template"))
                    {
                        Selection.activeObject = def.template;
                        EditorGUIUtility.PingObject(def.template);
                        AssetDatabase.OpenAsset(def.template);
                    }
                    EditorGUILayout.LabelField(string.Empty, GUILayout.ExpandWidth(true));
                }
            }
        }

        if (GUILayout.Button("+ Add Layer"))
        {
            var def = new LayerDef { name = "New Layer", depth = 1f, renderOrder = (config.layers.Count + 1) * 10, tileWidth = 24f };
            config.layers.Add(def);
            Persist(); RebuildPreviewIfOn();
        }
    }

    private void Swap(int a, int b)
    {
        (config.layers[a], config.layers[b]) = (config.layers[b], config.layers[a]);
        Persist(); RebuildPreviewIfOn();
    }

    private void CreateConfigAsset()
    {
        // round-1 m4 防呆：預設路徑已有含層資料的配置時先確認，避免誤按 New 靜默覆蓋生產配置
        var existing = AssetDatabase.LoadAssetAtPath<BGConfig>(DefaultConfigPath);
        if (existing != null && existing.layers != null && existing.layers.Count > 0)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Overwrite BGConfig?",
                DefaultConfigPath + " 已包含 " + existing.layers.Count + " 個 Layer。\n確定要建立新配置並覆蓋嗎？此操作無法復原。",
                "覆蓋", "取消");
            if (!confirmed) return;
        }

        var asset = CreateInstance<BGConfig>();
        AssetDatabase.CreateAsset(asset, DefaultConfigPath);
        AssetDatabase.SaveAssets();
        config = asset;
    }

    private void Persist()
    {
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets(); // round-1 m2：立即落盤，避免使用者未手動存檔即關閉 Unity 而遺失變更
    }

    // ---- T8：Scene View 即時預覽（FR-16，Edit mode only）----

    private void DrawPreviewBar()
    {
        EditorGUI.BeginChangeCheck();
        previewOn = GUILayout.Toggle(previewOn, "Scene Preview (Edit mode)", "Button");
        if (EditorGUI.EndChangeCheck())
        {
            if (previewOn) BuildPreview(); else ClearPreview();
        }
        if (previewOn)
            EditorGUILayout.HelpBox("預覽為暫存物件，不會存入場景；關閉預覽或視窗時自動清除。", MessageType.None);
    }

    private void RebuildPreviewIfOn()
    {
        if (!previewOn) return;
        ClearPreview();
        BuildPreview();
    }

    private void BuildPreview()
    {
        if (config == null || Application.isPlaying) return;
        if (previewSprite == null) previewSprite = MakePlaceholderSprite();

        for (int li = 0; li < config.layers.Count; li++)
        {
            LayerDef def = config.layers[li];
            if (def == null || string.IsNullOrEmpty(def.name)) continue;

            var root = new GameObject("BGLayerPreview_" + def.name) { hideFlags = HideFlags.DontSave };
            root.transform.position = Vector3.zero;

            // round-2 m3：可視寬改用 runtime 同源推導（BGLayer.GetVisibleWidth），
            // 取代硬編碼 20.53f——預覽 tile 數與 Play 模式 NFR-03 公式完全一致
            int count = Mathf.CeilToInt(BGLayer.GetVisibleWidth() / Mathf.Max(def.tileWidth, 0.01f)) + 1;
            count = Mathf.Max(count, 2);
            for (int t = 0; t < count; t++)
            {
                GameObject tile;
                if (def.template != null)
                    tile = (GameObject)PrefabUtility.InstantiatePrefab(def.template);
                else
                {
                    tile = new GameObject("Tile");
                    tile.AddComponent<SpriteRenderer>().sprite = previewSprite;
                }
                tile.transform.SetParent(root.transform, false);
                tile.transform.localPosition = new Vector3(t * def.tileWidth, 0f, 0f);

                SpriteRenderer[] renderers = tile.GetComponentsInChildren<SpriteRenderer>();
                for (int r = 0; r < renderers.Length; r++)
                    renderers[r].sortingOrder = def.renderOrder + renderers[r].sortingOrder;
                FitTileWorldWidth(tile, renderers, def.tileWidth);
            }
            previewRoots.Add(root);
        }
    }

    // 與 runtime Tile.NormalizeWorldWidth 同語義（round-1 M1 修復）：
    // 合併 tile 全部子 renderer 的世界 bounds，以合併寬換算單一根縮放，
    // 使 tile 整體外框世界寬＝tileWidth。禁止逐 renderer 各自拉寬——
    // 多子物件模板下那會把每個子物件都放大到全寬，整體外框達 N×tileWidth。
    private static void FitTileWorldWidth(GameObject tile, SpriteRenderer[] renderers, float tileWidth)
    {
        if (tileWidth <= 0f || renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        if (combined.size.x <= 0f) return;
        float scale = tileWidth / combined.size.x;
        tile.transform.localScale = new Vector3(scale, scale, 1f); // 覆寫根縮放，與 runtime 行為一致
    }

    private static Sprite MakePlaceholderSprite()
    {
        var tex = new Texture2D(32, 32);
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100);
    }

    private void ClearPreview()
    {
        for (int i = 0; i < previewRoots.Count; i++)
        {
            if (previewRoots[i] != null) DestroyImmediate(previewRoots[i]);
        }
        previewRoots.Clear();

        // round-2 m1：連帶釋放 placeholder sprite／texture（Edit mode 物件用 DestroyImmediate）；
        // BuildPreview 端有 null 檢查，下次開啟預覽會自動重建
        if (previewSprite != null)
        {
            Texture2D tex = previewSprite.texture;
            DestroyImmediate(previewSprite);
            if (tex != null) DestroyImmediate(tex);
            previewSprite = null;
        }
    }
}
