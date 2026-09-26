using UnityEngine;

public enum BulletMode { Normal, Crusher, Homing }

public class Bullet : MonoBehaviour
{
    public float  Speed    = 8f;
    public int    Damage   = 10;
    public float  Lifetime = 5f;
    public string OwnerTag;

    Vector3    _direction;
    BulletMode _mode = BulletMode.Normal;

    // ── Crusher ──────────────────────────────────────────────────────────
    float _accelTimer;
    float _accelDelay;
    float _accelSpeed;
    bool  _accelerated;

    // ── Homing ───────────────────────────────────────────────────────────
    Transform _homingTarget;
    float     _homingMaxDist  = 18f;
    float     _homingTurnRate = 180f;

    // ── Curve (이동 중 방향 회전) ─────────────────────────────────────────
    public float      CurveSpeed     = 0f; // 도/초, Init 이후에도 외부에서 설정 가능
    public GameObject HitEffectPrefab;

    // ── Converge (일정 시간 후 발사 지점으로 수렴) ────────────────────────
    bool    _converging;
    float   _convergeDelay;
    float   _convergeTimer;
    float   _convergeTurnRate;
    Vector3 _convergeTarget;

    public void Init(Vector3 direction, float speed, int damage, string ownerTag,
                     BulletMode mode           = BulletMode.Normal,
                     float accelDelay          = 0.7f,
                     float accelSpeed          = 24f,
                     float homingMaxDist       = 18f,
                     float homingTurnRate      = 180f,
                     float convergeDelay       = 0f,
                     float convergeTurnRate    = 0f,
                     float elapsedTime         = 0f,
                     bool  isLocal             = false)
    {
        _direction = new Vector3(direction.x, 0f, direction.z).normalized;
        Speed      = speed;
        Damage     = damage;
        OwnerTag   = ownerTag;
        _mode      = mode;

        _accelDelay       = accelDelay;
        _accelSpeed       = accelSpeed;
        _homingMaxDist    = homingMaxDist;
        _homingTurnRate   = homingTurnRate;
        _convergeDelay    = convergeDelay;
        _convergeTurnRate = convergeTurnRate;
        _convergeTarget   = transform.position;

        if (_mode == BulletMode.Homing)
        {
            string targetTag = ownerTag == "Player1" ? "Player2" : "Player1";
            var t = GameObject.FindWithTag(targetTag);
            if (t != null) _homingTarget = t.transform;
        }

        // Dead reckoning: 네트워크 레이턴시만큼 위치·상태 보정
        if (elapsedTime > 0f)
        {
            Lifetime -= elapsedTime;
            if (Lifetime <= 0f) { Destroy(gameObject); return; }

            transform.position += _direction * DeadReckonDist(speed, elapsedTime);

            if (_mode == BulletMode.Crusher)
            {
                _accelTimer = elapsedTime;
                if (elapsedTime >= _accelDelay)
                {
                    Speed        = _accelSpeed;
                    _accelerated = true;
                }
            }
        }

        Destroy(gameObject, Lifetime);
        ApplyColor(isLocal);
    }

    // 경과 시간에 따른 이동 거리 계산 (모드별 속도 프로파일 반영)
    float DeadReckonDist(float initSpeed, float elapsed)
    {
        if (_mode == BulletMode.Crusher)
        {
            float slow = Mathf.Min(elapsed, _accelDelay) * initSpeed;
            float fast = Mathf.Max(0f, elapsed - _accelDelay) * _accelSpeed;
            return slow + fast;
        }
        return initSpeed * elapsed;
    }

    void SpawnHitEffect()
    {
        if (HitEffectPrefab != null)
            Instantiate(HitEffectPrefab, transform.position, Quaternion.identity);
    }

    void ApplyColor(bool isLocal)
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", isLocal
            ? new Color(0.2f, 1f, 0.3f)   // 내 탄막 — 초록
            : new Color(1f, 0.2f, 0.2f));  // 적 탄막 — 빨강
        r.SetPropertyBlock(mpb);
    }

    void Update()
    {
        switch (_mode)
        {
            case BulletMode.Normal:
                if (CurveSpeed != 0f)
                    _direction = Quaternion.Euler(0f, CurveSpeed * Time.deltaTime, 0f) * _direction;
                HandleConverge();
                transform.Translate(_direction * Speed * Time.deltaTime, Space.World);
                break;

            case BulletMode.Crusher:
                _accelTimer += Time.deltaTime;
                if (!_accelerated && _accelTimer >= _accelDelay)
                {
                    Speed        = _accelSpeed;
                    _accelerated = true;
                }
                transform.Translate(_direction * Speed * Time.deltaTime, Space.World);
                break;

            case BulletMode.Homing:
                if (_homingTarget != null)
                {
                    Vector3 toTarget = _homingTarget.position - transform.position;
                    toTarget.y = 0f;
                    float dist = toTarget.magnitude;
                    float t    = Mathf.Clamp01(dist / _homingMaxDist);
                    float turn = _homingTurnRate * t * Mathf.Deg2Rad * Time.deltaTime;
                    if (dist > 0.01f)
                        _direction = Vector3.RotateTowards(_direction, toTarget.normalized, turn, 0f);
                }
                transform.Translate(_direction * Speed * Time.deltaTime, Space.World);
                break;
        }
    }

    void HandleConverge()
    {
        if (_convergeTurnRate <= 0f) return;

        if (!_converging)
        {
            _convergeTimer += Time.deltaTime;
            if (_convergeTimer >= _convergeDelay)
                _converging = true;
            return;
        }

        // 발사 지점 방향으로 서서히 방향 전환
        Vector3 toCenter = _convergeTarget - transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.01f)
        {
            float turn = _convergeTurnRate * Mathf.Deg2Rad * Time.deltaTime;
            _direction = Vector3.RotateTowards(_direction, toCenter.normalized, turn, 0f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(OwnerTag)) return;

        var ph = other.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            // 피해는 로컬 플레이어에게만 — 원격 플레이어 판정은 상대방 씬에서 처리
            if (GameManager.Instance?.LocalPlayer == ph)
                ph.TakeDamage(Damage);
            SpawnHitEffect();
            Destroy(gameObject);
            return;
        }

        if (other.CompareTag("Wall"))
            Destroy(gameObject);
    }
}
