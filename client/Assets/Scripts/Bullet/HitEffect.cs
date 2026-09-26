using UnityEngine;

public class HitEffect : MonoBehaviour
{
    public Sprite[] Frames;
    public float    FPS          = 18f;
    public float    Scale        = 3f;
    public int      SortingOrder = 100;

    SpriteRenderer _sr;
    float          _timer;
    int            _frame;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) { Destroy(gameObject); return; }

        _sr.sortingOrder = SortingOrder;
        transform.localScale = Vector3.one * Scale;

        if (Frames == null || Frames.Length == 0 || Frames[0] == null)
        {
            Debug.LogWarning("[HitEffect] Frames가 비어있음 — HitEffect.prefab에 스프라이트가 연결됐는지 확인하세요.");
            Destroy(gameObject);
            return;
        }

        _sr.sprite = Frames[0];
    }

    void LateUpdate()
    {
        // 항상 카메라를 향하는 빌보드
        if (Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;

        if (Frames == null || Frames.Length == 0) return;

        _timer += Time.deltaTime;
        int next = Mathf.FloorToInt(_timer * FPS);

        if (next >= Frames.Length) { Destroy(gameObject); return; }
        if (next != _frame)        { _frame = next; _sr.sprite = Frames[_frame]; }
    }
}
