using UnityEngine;

/// <summary>
/// 音效播放唯一入口（SFX 基礎建設）——單一 AudioSource 以 PlayOneShot 播放，允許快速連跳時音效重疊。
/// 呼叫端只需學：PlayJump()、PlayGameOver()；clip 由場景接線（scene-developer），未接線時靜默不拋錯。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    [Header("SFX Clips")]
    [SerializeField] private AudioClip jumpSfx;
    [SerializeField] private AudioClip gameOverSfx;

    [Header("Volume")]
    [SerializeField] private float jumpVolume = 1f;
    [SerializeField] private float gameOverVolume = 1f;

    private AudioSource audioSource;

    private void Awake()
    {
        // 防禦性解析（比照 PlayerController.Awake）：GetComponent 取既有，null 才 AddComponent
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // 純 2D 音效：不隨距離衰減、不自動播放、不循環、無隨機音高
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.pitch = 1f;
    }

    /// <summary>跳躍音效（起跳瞬間）——PlayOneShot 允許快速連跳時音效重疊。</summary>
    public void PlayJump()
    {
        if (jumpSfx == null) return; // 未接線靜默（防 NRE）
        audioSource.PlayOneShot(jumpSfx, jumpVolume);
    }

    /// <summary>死亡音效（Die() 死亡瞬間）——PlayOneShot 允許與其他音效重疊。</summary>
    public void PlayGameOver()
    {
        if (gameOverSfx == null) return; // 未接線靜默（防 NRE）
        audioSource.PlayOneShot(gameOverSfx, gameOverVolume);
    }
}
