using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 바닥에 놓인 특수무기 아이템.
/// 플레이어가 PickupRadius 안에 들어오면 E키 아이콘이 뜨고,
/// E키를 누르면 무기를 지급하고 아이템이 사라진다.
/// </summary>
public class WeaponPickup : MonoBehaviour
{
    [Header("Weapon")]
    public SpecialWeaponType WeaponType;
    public int               Ammo = 30;

    [Header("Interaction")]
    public Texture2D InteractIconTexture; // e.png
    public float     PickupRadius = 2.5f;

    [Header("Respawn (0 = 삭제)")]
    public float RespawnDelay = 0f; // 0이면 획득 후 완전 삭제, >0이면 대기 후 재생성

    [Header("Bob")]
    public float BobHeight = 0.18f;
    public float BobSpeed  = 2.2f;
    public float RotateSpeed = 60f;

    SpriteRenderer _prompt;
    Transform      _modelRoot;
    Transform      _playerTr;
    Vector3        _basePos;

    void Start()
    {
        _basePos = transform.position;

        // ── 모델 자식 찾기 (SceneBuilder가 "Model" 이름으로 생성) ─────────
        var modelChild = transform.Find("Model");
        if (modelChild != null) _modelRoot = modelChild;

        // ── E키 아이콘 (SpriteRenderer Billboard) ────────────────────────
        var iconGO = new GameObject("InteractPrompt");
        iconGO.transform.SetParent(transform);
        iconGO.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        iconGO.transform.localScale    = Vector3.one * 0.6f;

        _prompt = iconGO.AddComponent<SpriteRenderer>();
        _prompt.sortingOrder = 10;
        if (InteractIconTexture != null)
            _prompt.sprite = Sprite.Create(
                InteractIconTexture,
                new Rect(0, 0, InteractIconTexture.width, InteractIconTexture.height),
                new Vector2(0.5f, 0.5f), 100f);
        _prompt.enabled = false;

        // ── Player1 참조 ──────────────────────────────────────────────────
        var p1 = GameObject.FindWithTag("Player1");
        if (p1 != null) _playerTr = p1.transform;
    }

    void Update()
    {
        // Player1 지연 탐색 (Start 시점에 없을 수 있음)
        if (_playerTr == null)
        {
            var p1 = GameObject.FindWithTag("Player1");
            if (p1 != null) _playerTr = p1.transform;
            return;
        }

        // ── 둥실 떠오르는 애니메이션 ──────────────────────────────────────
        float bobY = Mathf.Sin(Time.time * BobSpeed) * BobHeight;
        transform.position = _basePos + new Vector3(0f, bobY, 0f);

        if (_modelRoot != null)
            _modelRoot.Rotate(0f, RotateSpeed * Time.deltaTime, 0f, Space.World);

        // ── 거리 판정 ─────────────────────────────────────────────────────
        float dist    = Vector3.Distance(transform.position, _playerTr.position);
        bool  inRange = dist <= PickupRadius;

        // E키 아이콘 Billboard (카메라 방향 맞춤)
        if (_prompt != null)
        {
            _prompt.enabled = inRange;
            if (inRange && Camera.main != null)
                _prompt.transform.rotation = Camera.main.transform.rotation;
        }

        // ── E키 상호작용 ──────────────────────────────────────────────────
        if (inRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            var shooter = _playerTr.GetComponent<BulletShooter>();
            if (shooter != null)
            {
                shooter.AcquireSpecialWeapon(WeaponType, Ammo);
                if (RespawnDelay > 0f)
                    StartCoroutine(RespawnRoutine());
                else
                    Destroy(gameObject);
            }
        }
    }

    System.Collections.IEnumerator RespawnRoutine()
    {
        // 모델·프롬프트 숨기기
        if (_modelRoot != null) _modelRoot.gameObject.SetActive(false);
        if (_prompt    != null) _prompt.enabled = false;

        yield return new WaitForSeconds(RespawnDelay);

        // 위치 리셋 후 다시 보이기
        transform.position = _basePos;
        if (_modelRoot != null) _modelRoot.gameObject.SetActive(true);
    }
}
