using UnityEngine;

public class ScoreTrigger : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    private bool scored;

    private void Start()
    {
        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // ADR-011：狀態查詢走 GameStateAuthority（GA1）；gameManager 僅保留供計分命令（Score.AddPoint）
        if (!GameStateAuthority.IsPlaying()) return;
        if (scored) return;
        if (other.GetComponent<PlayerController>() != null)
        {
            gameManager.Score.AddPoint();
            scored = true;
        }
    }

    private void OnDisable()
    {
        scored = false;
    }
}
