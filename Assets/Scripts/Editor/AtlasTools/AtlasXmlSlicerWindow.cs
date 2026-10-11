using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Atlas XML Slicer（FR-01～FR-09）——外部打包圖集自動切圖工具。
/// 輸入一張已匯入的圖集 png，依同資料夾同名 .xml（TexturePacker／Starling 格式）
/// 解析 SubTexture 清單，換算座標後整體覆蓋寫入 TextureImporter 的 slice 資料，
/// 使該貼圖在 Unity 內成為多個正確命名的 Sprite。
///
/// 【slice API 裁定】採官方 ISpriteEditorDataProvider（UnityEditor.U2D.Sprites）。
/// 原候選 legacy TextureImporter.spritesheet 在本專案 Unity 6000.3.12f1 已被移除——
/// CS0618 明示「Support for accessing sprite meta data through spritesheet has been
/// removed」，且 TextureImporter.spriteMode 屬性不存在（正名為 spriteImportMode）。
/// 故依規格備案走 2D package 官方路徑：SpriteDataProviderFactories 取得 provider、
/// SetSpriteRects 全量覆蓋後 Apply＋SaveAndReimport。
///
/// Edit mode 專用（Play mode 中禁用）；零 runtime 改動。
/// </summary>
public class AtlasXmlSlicerWindow : EditorWindow
{
    /// <summary>SubTexture 的純資料載體——供靜態解析／換算方法與未來單元測試使用。</summary>
    public struct SubTextureData
    {
        public string Name;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    private const string LogPrefix = "[AtlasXmlSlicer] ";
    private const string PngSuffix = ".png";
    private const string XmlExtension = ".xml";

    /// <summary>FR-01：單一 Texture2D 輸入欄位（ObjectField 天然支援 Project 視窗拖放）。</summary>
    private Texture2D atlasTexture;

    [MenuItem("Window/Art/Atlas XML Slicer")]
    private static void Open()
    {
        GetWindow<AtlasXmlSlicerWindow>("Atlas XML Slicer");
    }

    private void OnGUI()
    {
        atlasTexture = (Texture2D)EditorGUILayout.ObjectField(
            "Atlas Texture", atlasTexture, typeof(Texture2D), false);

        if (atlasTexture == null)
        {
            EditorGUILayout.HelpBox("請從 Project 視窗拖曳一張圖集 png 到上方欄位（一次處理一張）。", MessageType.Info);
            return;
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play mode 中操作定義為不支援，請停止播放後使用。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            $"將讀取同資料夾的「{Path.ChangeExtension(atlasTexture.name, XmlExtension)}」並完成切圖。", MessageType.None);

        if (GUILayout.Button("Slice from XML"))
            ExecuteSlice();
    }

    // ---- 主流程 ----

    private void ExecuteSlice()
    {
        string texturePath = AssetDatabase.GetAssetPath(atlasTexture);
        if (string.IsNullOrEmpty(texturePath))
        {
            Debug.LogError(LogPrefix + "無法取得所選貼圖的資產路徑，已中止。");
            return;
        }

        // FR-02：同資料夾、副檔名替換為 .xml 自動配對；找不到即中止且不動任何匯入設定
        string xmlPath = Path.ChangeExtension(texturePath, XmlExtension);
        if (!File.Exists(xmlPath))
        {
            Debug.LogError(LogPrefix +
                $"找不到配對 xml（查找路徑：{xmlPath}）。請確認同資料夾存在同名 .xml 檔；匯入設定未被更動。（FR-02）");
            return;
        }

        string xmlText = File.ReadAllText(xmlPath);

        // FR-03：解析 SubTexture；逐項回報錯誤並中止，不動匯入設定
        List<SubTextureData> entries;
        string imagePath;
        List<string> errors;
        if (!TryParseSubTextures(xmlText, out entries, out imagePath, out errors))
        {
            for (int i = 0; i < errors.Count; i++)
                Debug.LogError(LogPrefix + errors[i]);
            Debug.LogError(LogPrefix + "xml 解析失敗，已中止切圖；匯入設定未被更動。（FR-03）");
            return;
        }

        // 邊界情況：xml imagePath 與所選檔名交叉檢查——不一致僅警告不中止
        CrossCheckImagePath(imagePath, texturePath);

        // FR-08：XML 極值 vs 匯入後貼圖尺寸守門（偵測匯入縮放或 xml 配錯檔）
        int maxRight;
        int maxBottom;
        ComputeExtents(entries, out maxRight, out maxBottom);
        if (maxRight > atlasTexture.width || maxBottom > atlasTexture.height)
        {
            Debug.LogWarning(LogPrefix +
                $"xml 座標極值（{maxRight}×{maxBottom}）超出匯入後貼圖尺寸（{atlasTexture.width}×{atlasTexture.height}）" +
                "——疑似匯入被縮放（maxTextureSize 低於原圖）或 xml 配錯檔，已中止切圖；匯入設定未被更動。（FR-08）");
            return;
        }

        // FR-09：任一 rect 不合法或越界即中止整批，不留半套結果
        for (int i = 0; i < entries.Count; i++)
        {
            SubTextureData entry = entries[i];
            bool inBounds = entry.X >= 0 && entry.Y >= 0 &&
                            entry.Width > 0 && entry.Height > 0 &&
                            entry.X + entry.Width <= atlasTexture.width &&
                            entry.Y + entry.Height <= atlasTexture.height;
            if (!inBounds)
            {
                Debug.LogError(LogPrefix +
                    $"SubTexture「{entry.Name}」rect（x={entry.X}, y={entry.Y}, w={entry.Width}, h={entry.Height}）" +
                    "不合法或落於貼圖邊界外，已中止整批切圖；匯入設定未被更動。（FR-09）");
                return;
            }
        }

        // FR-04＋FR-05：座標換算（Y 翻轉）與命名（去 .png 後綴）
        SpriteRect[] slices = BuildSpriteRects(entries, atlasTexture.height);

        // FR-06／FR-07：sprite 匯入模式設 Multiple、slice 清單整體覆蓋（冪等重跑）、重匯入落盤
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError(LogPrefix + $"{texturePath} 的 asset importer 不是 TextureImporter，已中止。");
            return;
        }

        importer.spriteImportMode = SpriteImportMode.Multiple;

        // 官方 slice 寫入路徑（見類別註解裁定）：provider 全量覆蓋 → Apply → 重匯入
        SpriteDataProviderFactories factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (dataProvider == null)
        {
            Debug.LogError(LogPrefix + $"無法取得 {texturePath} 的 ISpriteEditorDataProvider，已中止。");
            return;
        }

        dataProvider.InitSpriteEditorDataProvider();
        dataProvider.SetSpriteRects(slices);
        dataProvider.Apply();

        importer.SaveAndReimport();
        AssetDatabase.SaveAssets();

        Debug.Log(LogPrefix +
            $"成功切分 {slices.Length} 個 sprite（來源 xml：{Path.GetFileName(xmlPath)}）。（FR-06／FR-07）");
    }

    /// <summary>組出 SpriteRect 清單：套用 FR-04 座標換算與 FR-05 命名規則；每片配發新 spriteID。</summary>
    private static SpriteRect[] BuildSpriteRects(List<SubTextureData> entries, int textureHeight)
    {
        SpriteRect[] slices = new SpriteRect[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            slices[i] = new SpriteRect
            {
                name = StripSpriteSuffix(entries[i].Name),
                spriteID = GUID.Generate(),
                rect = ConvertRect(entries[i], textureHeight),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero
            };
        }
        return slices;
    }

    /// <summary>邊界情況：xml imagePath 屬性與所選貼圖檔名交叉檢查，不一致時警告但不中止。</summary>
    private static void CrossCheckImagePath(string imagePath, string texturePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return;

        string selectedFileName = Path.GetFileName(texturePath);
        if (!string.Equals(imagePath, selectedFileName, System.StringComparison.OrdinalIgnoreCase))
            Debug.LogWarning(LogPrefix +
                $"xml imagePath「{imagePath}」與所選貼圖檔名「{selectedFileName}」不一致——以所選貼圖為準繼續執行。");
    }

    // ---- 靜態純方法（T3 單元測試目標；除回傳型別外不依賴 Unity API）----

    /// <summary>
    /// FR-03：解析 TextureAtlas 根元素下所有 SubTexture 子節點（屬性：name/x/y/width/height）。
    /// 任何格式不符或缺必要屬性皆逐項記入 errors 並回傳 false；全部通過才回傳 true。
    /// imagePath 屬性缺漏不算錯誤（僅供交叉檢查，見邊界情況表）。
    /// </summary>
    public static bool TryParseSubTextures(
        string xmlText, out List<SubTextureData> entries, out string imagePath, out List<string> errors)
    {
        entries = new List<SubTextureData>();
        imagePath = string.Empty;
        errors = new List<string>();

        XmlDocument document = new XmlDocument();
        try
        {
            document.LoadXml(xmlText);
        }
        catch (XmlException exception)
        {
            errors.Add($"xml 格式損壞無法解析：{exception.Message}");
            return false;
        }

        XmlElement root = document.DocumentElement;
        if (root == null || root.Name != "TextureAtlas")
        {
            string actual = root != null ? root.Name : "&lt;無根元素&gt;";
            errors.Add($"根元素為「{actual}」，預期 TextureAtlas（非 TexturePacker／Starling 格式）。");
            return false;
        }

        imagePath = root.GetAttribute("imagePath");

        int index = 0;
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element)
                continue;
            index++;

            XmlElement element = (XmlElement)node;
            if (element.Name != "SubTexture")
            {
                errors.Add($"第 {index} 個子節點為「{element.Name}」，預期 SubTexture。");
                continue;
            }

            SubTextureData entry = default(SubTextureData);
            entry.Name = element.GetAttribute("name");
            bool valid = true;

            if (string.IsNullOrEmpty(entry.Name))
            {
                errors.Add($"第 {index} 個 SubTexture 缺少 name 屬性。");
                valid = false;
            }

            // 刻意用非短路 &：四個屬性全部檢查，一次收集所有錯誤（逐項回報）
            valid &= TryReadInt(element, "x", index, errors, out entry.X);
            valid &= TryReadInt(element, "y", index, errors, out entry.Y);
            valid &= TryReadInt(element, "width", index, errors, out entry.Width);
            valid &= TryReadInt(element, "height", index, errors, out entry.Height);

            if (valid)
                entries.Add(entry);
        }

        if (entries.Count == 0 && errors.Count == 0)
            errors.Add("TextureAtlas 內沒有任何 SubTexture 子節點。");

        return errors.Count == 0;
    }

    /// <summary>讀取整數屬性；缺漏或非整數皆記入 errors（訊息含節點序號與屬性名）。</summary>
    private static bool TryReadInt(XmlElement element, string attributeName, int nodeIndex, List<string> errors, out int value)
    {
        value = 0;
        string raw = element.GetAttribute(attributeName);
        if (raw.Length == 0)
        {
            errors.Add($"第 {nodeIndex} 個 SubTexture 缺少 {attributeName} 屬性。");
            return false;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            errors.Add($"第 {nodeIndex} 個 SubTexture 的 {attributeName} 屬性「{raw}」不是有效整數。");
            return false;
        }
        return true;
    }

    /// <summary>
    /// FR-04：左上原點像素座標 → Unity 左下原點 rect。
    /// 公式：y_unity = textureHeight − y_xml − height_xml；x 不變。
    /// </summary>
    public static Rect ConvertRect(SubTextureData data, int textureHeight)
    {
        float yUnity = textureHeight - data.Y - data.Height;
        return new Rect(data.X, yUnity, data.Width, data.Height);
    }

    /// <summary>FR-05：去除 name 屬性的 .png 後綴（bush1.png → bush1）；無後綴時原樣返回。</summary>
    public static string StripSpriteSuffix(string name)
    {
        if (!string.IsNullOrEmpty(name) &&
            name.Length > PngSuffix.Length &&
            name.EndsWith(PngSuffix, System.StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(0, name.Length - PngSuffix.Length);
        }
        return name;
    }

    /// <summary>FR-08 用：計算所有 SubTexture 的極值 max(x+width)、max(y+height)（左上原點座標系）。</summary>
    public static void ComputeExtents(List<SubTextureData> entries, out int maxRight, out int maxBottom)
    {
        maxRight = 0;
        maxBottom = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            int right = entries[i].X + entries[i].Width;
            int bottom = entries[i].Y + entries[i].Height;
            if (right > maxRight) maxRight = right;
            if (bottom > maxBottom) maxBottom = bottom;
        }
    }
}
