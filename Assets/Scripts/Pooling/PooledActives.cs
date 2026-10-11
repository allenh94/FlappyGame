using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 共用池化 seam（ADR-010）：UnityEngine.ObjectPool 的薄 adapter。
/// 負責池週期——active-list 追蹤＋重複 get/release 防護＋SetActive/Destroy 標準 wiring。
/// 呼叫端只提供 createFunc（PL2：只抽池週期，不抽 tile/pipe 建立邏輯）。
/// T 為活體型別：Pipe 池用 T=PipeController（Component），Tile 池用 T=GameObject。
/// 熱路徑零分配（PL3）：active-list 用預分配 List，禁 LINQ／GC。
/// </summary>
public class PooledActives<T> where T : UnityEngine.Object
{
    private readonly ObjectPool<T> pool;
    private readonly List<T> active;

    /// <summary>唯讀 active-list（供外部迭代／釋放全部）。</summary>
    public IReadOnlyList<T> Active => active;

    public PooledActives(Func<T> createFunc, int capacity, int maxSize)
    {
        active = new List<T>(capacity);
        pool = new ObjectPool<T>(
            createFunc: createFunc,
            actionOnGet: OnGet,
            actionOnRelease: OnRelease,
            actionOnDestroy: OnDestroy,
            collectionCheck: true,
            defaultCapacity: capacity,
            maxSize: maxSize
        );
    }

    /// <summary>取出活體並加入 active-list；重複 get 防護（同一活體不得重複 get）。</summary>
    public T Get()
    {
        T item = pool.Get();
        if (active.Contains(item))
            throw new InvalidOperationException("[PooledActives] 重複 Get：同一活體已在 active-list。");
        active.Add(item);
        return item;
    }

    /// <summary>釋放活體並移出 active-list；重複 release 防護（同一活體不得重複 release）。</summary>
    public void Release(T item)
    {
        if (!active.Remove(item))
            throw new InvalidOperationException("[PooledActives] 重複 Release：活體不在 active-list。");
        pool.Release(item);
    }

    /// <summary>釋放全部活體並清空 active-list。</summary>
    public void ReleaseAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            pool.Release(active[i]);
        active.Clear();
    }

    // ---- 標準 wiring（SetActive/Destroy）----
    private void OnGet(T item) => GetGameObject(item).SetActive(true);
    private void OnRelease(T item) => GetGameObject(item).SetActive(false);
    private void OnDestroy(T item) => UnityEngine.Object.Destroy(GetGameObject(item));

    private static GameObject GetGameObject(T item)
    {
        if (item is GameObject go) return go;
        if (item is Component comp) return comp.gameObject;
        throw new InvalidOperationException("[PooledActives] 僅支援 GameObject 或 Component 型別。");
    }
}