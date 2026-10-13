using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BlobAnimator blobAnimator; // 動畫控制層（T4 場景接線；Awake GetComponent fallback）
    [SerializeField] private AudioManager audioManager; // 音效層（場景接線；Awake GetComponent/FindFirstObjectByType fallback）

    [Header("Movement")]
    [SerializeField] private float flapForce = 12f;
    [SerializeField] private float gravityScale = 3f;
    [SerializeField] private float terminalVelocity = -20f;

    [Header("Rotation")]
    [SerializeField] private float maxUpAngle = 30f;
    [SerializeField] private float maxDownAngle = -60f;
    [SerializeField] private float rotationSpeed = 5f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private float currentVelocity;
    private bool isDead;
    private bool isLanding; // 防重複 PlayLand：進 Land 時 true、OnJumpRise 時 false

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        bodyCollider = GetComponent<Collider2D>();
        if (blobAnimator == null)
            blobAnimator = GetComponent<BlobAnimator>(); // T4 場景接線前防禦性解析
        if (audioManager == null)
            audioManager = GetComponent<AudioManager>(); // 場景接線前防禦性解析
        if (audioManager == null)
            audioManager = FindFirstObjectByType<AudioManager>(); // 跨物件 fallback（比照 blobAnimator 模式）

        rb.gravityScale = 0f; // We handle gravity manually for terminal velocity
        rb.freezeRotation = true; // We handle rotation manually
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private void OnEnable()
    {
        // FR-10：訂閱 JumpRise——012 蓄力播完（frame3 起點）才設衝力
        if (blobAnimator != null)
            blobAnimator.JumpRise += OnJumpRise;
    }

    private void OnDisable()
    {
        if (blobAnimator != null)
            blobAnimator.JumpRise -= OnJumpRise;
    }

    /// <summary>Menu/GameOver 隱藏鳥（ADR-007）：Renderer.enabled 隱藏渲染＋停用 collider 防碰撞。
    /// GetComponentsInChildren 涵蓋未來 spritesheet 動畫多子 SpriteRenderer；零 SetActive 停用 gameplay 物件。</summary>
    public void SetVisible(bool visible)
    {
        Renderer[] renders = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renders.Length; i++)
            renders[i].enabled = visible;
        if (bodyCollider != null) bodyCollider.enabled = visible;
    }

    private void Update()
    {
        // T2：非 Playing 態（Menu/Paused/GameOver）凍結——含 HandleInput，防暫停時輸入觸發跳躍
        // ADR-011：狀態查詢走 GameStateAuthority（GA1），不再持 GameManager 引用
        if (!GameStateAuthority.IsPlaying()) return;
        if (isDead) return;

        HandleInput();
        HandleGravity();
        HandleLandAnimation();
        // HandleRotation(); //改用spritesheet animation 不再旋轉
    }

    private void HandleInput()
    {
        
        
        bool spacePressed =
            Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame;

        bool mouseClicked =
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame;

        bool touchPressed =
            Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
        if (spacePressed || mouseClicked||touchPressed)
        {
            // FR-09/FR-20：輸入不再直接給衝力——改為觸發 Jump 動畫（012 蓄力）。
            // 衝力由 OnJumpRise（JumpRise 事件）在 012 播完後給（FR-10）。
            // FR-12 輸入 gate（012/Assemble 期間無視輸入）由 BlobAnimator.PlayJump() 內部處理。
            if (blobAnimator != null) blobAnimator.PlayJump();
        }
    }

    /// <summary>FR-10：Jump 剪輯 012 蓄力播完（frame3 起點，BlobAnimator.JumpRise 事件）→ 才設衝力。</summary>
    private void OnJumpRise()
    {
        isLanding = false; // 離開 Land 狀態（Land 中按 jump → 012 → OnJumpRise 上升）
        currentVelocity = flapForce;
        if (audioManager != null) audioManager.PlayJump(); // 起跳瞬間音效（每次跳躍一次）
    }

    private void HandleGravity()
    {
        // AN13（Tech Lead 裁決）：Assemble 播放期間鳥靜止——velocity 保持 0，鳥不墜落。
        // Assemble 結束後（IsAssembling=false）重力恢復，velocity<0 自然觸發 PlayLand。
        if (blobAnimator != null && blobAnimator.IsAssembling) return;

        // Apply gravity
        currentVelocity += Physics2D.gravity.y * gravityScale * Time.deltaTime;

        // Clamp to terminal velocity
        if (currentVelocity < terminalVelocity)
        {
            currentVelocity = terminalVelocity;
        }

        // Apply movement
        rb.linearVelocity = new Vector2(0f, currentVelocity);
    }

    /// <summary>FR-20：速度轉負且非 dying → PlayLand（下降時播 Land 動畫）。
    /// isLanding 防護：下降期間每幀速度都 &lt;0，若每幀呼叫 PlayLand 會每幀重播 Land 剪輯（跳回第一幀）。</summary>
    private void HandleLandAnimation()
    {
        if (isDead) return; // 非 dying gate
        if (currentVelocity < 0 && !isLanding)
        {
            isLanding = true;
            if (blobAnimator != null) blobAnimator.PlayLand();
        }
    }

    private void HandleRotation()
    {
        // Map velocity to rotation angle
        float targetAngle;
        if (currentVelocity > 0)
        {
            // Moving up - tilt up
            targetAngle = Mathf.Lerp(0f, maxUpAngle, currentVelocity / flapForce);
        }
        else
        {
            // Moving down - tilt down
            targetAngle = Mathf.Lerp(0f, maxDownAngle, Mathf.Abs(currentVelocity) / Mathf.Abs(terminalVelocity));
        }

        // Smooth rotation
        float currentAngle = transform.eulerAngles.z;
        float newAngle = Mathf.Lerp(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, newAngle);
    }

    public void Die()
    {
        isDead = true;
        rb.linearVelocity = Vector2.zero;
        currentVelocity = 0f;
    }

    public void Reset(Vector3 startPosition)
    {
        isDead = false;
        isLanding = false; // 新局重設 Land 旗標
        transform.position = startPosition;
        currentVelocity = 0f;
        rb.linearVelocity = Vector2.zero;
        transform.rotation = Quaternion.identity;
    }
}