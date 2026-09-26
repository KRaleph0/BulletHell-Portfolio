using UnityEngine;

/// <summary>
/// 로컬 플레이어(Player1)를 중심으로 카메라가 따라다님.
/// 30° 비스듬 탑뷰 유지, 부드러운 Lerp 추적.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("추적 대상")]
    public Transform Target;          // SceneBuilder 또는 Start에서 자동 할당

    [Header("카메라 오프셋 (30° 뷰)")]
    public Vector3 Offset = new Vector3(0f, 18f, -31f);

    [Header("부드러움 (1 = 즉시, 0 = 안 따라옴)")]
    [Range(0.01f, 1f)]
    public float SmoothSpeed = 0.15f;

    void Start()
    {
        // Target이 Inspector에서 미설정이면 "Player1" 태그로 자동 검색
        if (Target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player1");
            if (p != null) Target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (Target == null) return;

        // 플레이어 XZ를 따라가되 Y는 오프셋으로 고정
        Vector3 desired = new Vector3(Target.position.x, 0f, Target.position.z) + Offset;
        transform.position = Vector3.Lerp(transform.position, desired, SmoothSpeed);
    }
}
