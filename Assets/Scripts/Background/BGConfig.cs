using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背景配置資產——背景系統唯一配置真相來源。
/// 單向流（B2）：Editor Window 寫入（T6），Runtime 唯讀；
/// Runtime 代碼不得寫入本資產任何欄位。
/// </summary>
[CreateAssetMenu(fileName = "BGConfig", menuName = "Background/BG Config")]
public class BGConfig : ScriptableObject
{
    [Header("Layers")]
    public List<LayerDef> layers = new List<LayerDef>();
}

/// <summary>
/// 單一背景層定義（純資料，Editor 配置、Runtime 唯讀）。
/// 分離鐵律（B3）：depth 只進速度公式（speed = baseSpeed / depth），
/// renderOrder 只進 SpriteRenderer.sortingOrder；兩者互不讀取或推導。
/// </summary>
[Serializable]
public class LayerDef
{
    public string name = "New Layer";   // 同時作為場景中 Layer GameObject 名稱
    public float depth = 1f;            // 越大捲動越慢（只影響速度，與遮擋無關）
    public int renderOrder;             // 越大越晚渲染、遮擋他人（只影響遮擋，與速度無關）
    public GameObject template;         // tile 模板 prefab（T4 啟用；留空時 runtime 用 placeholder）
    public float tileWidth = 24f;       // tile 世界寬度＝換行距離（須 ≥ 該層可視寬，見 NFR-05）
}
