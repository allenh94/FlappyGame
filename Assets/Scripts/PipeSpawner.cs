using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;


public class PipeSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private GameObject pipePrefab;
    [SerializeField] private float spawnInterval = 2f;
    [SerializeField] private float spawnXPosition = 12f;

    [Header("Pipe Settings")]
    [SerializeField] private float horizontalOffsetRange = 2f;
    [SerializeField] private float gapSize = 4f;
    [SerializeField] private float minHeight = -2f;
    [SerializeField] private float maxHeight = 2f;
    [SerializeField] private float minScale = 0.8f;
    [SerializeField] private float maxScale = 1.2f;

    [Header("Pool Settings")]
    [SerializeField] private int poolSize = 5;
    [SerializeField] private float despawnXPosition = -12f;

    // private GameObject[] pipePool;
    private float spawnTimer;
    private int nextPoolIndex;
    // ADR-010：池週期（active-list 追蹤＋重複 get/release 防護＋SetActive/Destroy wiring）收斂至 PooledActives
    private PooledActives<PipeController> pipePool;
    
    private void Awake()
    {
        InitializePool();
    }

    private void InitializePool()
    {
        // PL2：建立邏輯留呼叫端——createFunc 建 pipe prefab；池週期由 PooledActives 處理
        pipePool = new PooledActives<PipeController>(
            createFunc: () =>
            {
                GameObject pipe = Instantiate(pipePrefab,transform);
                return pipe.GetComponent<PipeController>();
            },
            capacity: poolSize,
            maxSize: poolSize
        );
    }

    private void Update()
    {
        // T2：非 Playing 態（Menu/Paused/GameOver）不生成水管
        // ADR-011：狀態查詢走 GameStateAuthority（GA1），不再持 GameManager 引用
        if (!GameStateAuthority.IsPlaying()) return;

        // FR-07：倒數式計時——spawnTimer = 還剩多久出下一根，從 interval 往下減到 0 觸發
        spawnTimer -= Time.deltaTime;

        if (spawnTimer <= 0f)
        {
            SpawnPipe();
            spawnTimer = spawnInterval;
        }

        CheckDespawn();

    }

    private void SpawnPipe()
    {
        PipeController pipeController = pipePool.Get();
        // Random gap position in middle 60% of screen
        float gapCenter = Random.Range(minHeight, maxHeight);
        float gapBottom = gapCenter - gapSize / 2f;
        float gapTop = gapCenter + gapSize / 2f;

        // Position pipe (centered on gap)
        Vector3 spawnPosition = new Vector3(spawnXPosition, gapCenter, 0f);
        pipeController.Configure(spawnPosition,gapSize,horizontalOffsetRange,maxScale,minScale);

    }

    private void CheckDespawn()
    {
        var active = pipePool.Active;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            PipeController pipe = active[i];
            if (pipe.transform.position.x < despawnXPosition)
            {
                pipePool.Release(pipe);
            }
        }
    }
    
    
    public void ResetAll()
    {
        pipePool.ReleaseAll();
        // FR-07：seed=0——進 Playing 第一幀 -dt <= 0 立刻生成第一根，與 player 同幀現身。
        // Ready 期間 IsPlaying()==false，Update 的 spawnTimer -= dt 不執行，seed=0 全程保留到進 Playing。
        spawnTimer = 0f;
    }
}