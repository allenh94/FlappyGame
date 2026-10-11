using UnityEngine;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if bird hit a pipe
        if (other.CompareTag("Pipe"))
        {
            gameManager.Die();
        }
    }
}
