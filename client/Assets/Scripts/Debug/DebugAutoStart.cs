using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 디버그 전용 - 서버 없이 로컬 테스트할 때 사용
/// Enter  : 게임 시작 (GameState Waiting → Playing)
/// R      : 씬 재시작
/// </summary>
public class DebugAutoStart : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Debug Settings")]
    [Tooltip("활성화 시 Play 누르자마자 자동으로 게임 시작")]
    public bool AutoStartOnPlay = true;

    void Start()
    {
        if (AutoStartOnPlay)
            GameManager.Instance?.StartGame();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Enter → 게임 시작
        if (kb.enterKey.wasPressedThisFrame)
            GameManager.Instance?.StartGame();

        // R → 씬 재시작
        if (kb.rKey.wasPressedThisFrame)
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
#endif
}
