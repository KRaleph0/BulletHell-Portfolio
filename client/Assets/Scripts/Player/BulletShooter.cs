using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BulletPatternType  { Straight, Spread, Spiral }
public enum SpecialWeaponType  { None, EnhancedSpiral, Crusher, Homing }

public class BulletShooter : MonoBehaviour
{
    [Header("References")]
    public GameObject BulletPrefab;
    public Transform  FirePoint;

    [Header("Pattern")]
    public BulletPatternType CurrentPattern = BulletPatternType.Straight;

    [Header("Stats")]
    public float  FireRate     = 0.25f;
    public float  BulletSpeed  = 8f;
    public int    BulletDamage = 10;
    public string OwnerTag     = "Player1";

    [Header("Burst (Spread 패턴)")]
    public int   BurstCount    = 3;
    public float BurstDelay    = 0.1f;
    public float BurstCooldown = 0.6f;
    public float SpreadAngle   = 20f;

    [Header("8방향 (Spiral 패턴)")]
    public float OmniFireRate = 1.0f;

    // ── 특수무기 ──────────────────────────────────────────────────────────
    [Header("Special Weapon")]
    public SpecialWeaponType ActiveSpecial   = SpecialWeaponType.None;
    public int               SpecialAmmo     = 0;
    public bool              IsSpecialActive = false;

    // 강화 스파이럴 설정
    [Header("Special – Enhanced Spiral")]
    public int   ESpiralArms       = 8;     // 8방향
    public float ESpiralStep       = 22.5f; // 매 발마다 패턴 회전 각도
    public float ESpiralFireRate   = 0.38f;
    public float ESpiralCurveSpeed = 45f;   // 탄환 곡선 속도 (도/초, 낮을수록 완만하게 퍼짐)

    // 고속분쇄탄 설정
    [Header("Special – Crusher")]
    public float CrusherFireRate   = 0.55f;  // 공속 감소 (느리게)
    public float CrusherInitSpeed  = 4f;
    public float CrusherAccelDelay = 0.65f;
    public float CrusherAccelSpeed = 38f;    // 2차 발진속도 증가

    // 유도탄 설정
    [Header("Special – Homing")]
    public float HomingFireRate  = 0.55f;
    public float HomingSpeed     = 4.5f;
    public float HomingMaxDist   = 18f;
    public float HomingTurnRate  = 190f;

    public bool IsEnhanced { get; private set; }
    float _enhancedTimer;

    float _fireTimer;
    bool  _isBursting;
    float _eSpiralAngle; // 강화 스파이럴 현재 회전 각도

    Animator _anim;
    static readonly int AnimIsAttacking = Animator.StringToHash("isAttacking");

    public bool IsLocalPlayer = true;
    // 1 = P1 (좌클릭 + 마우스 조준), 2 = P2 (우클릭 + 전방향)
    public int  PlayerIndex   = 1;

    void Awake() => _anim = GetComponentInChildren<Animator>();

    void Update()
    {
        if (!IsLocalPlayer) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying()) return;

        _fireTimer -= Time.deltaTime;

        if (IsEnhanced)
        {
            _enhancedTimer -= Time.deltaTime;
            if (_enhancedTimer <= 0f) IsEnhanced = false;
        }

        bool    isFiring = PlayerIndex == 2
            ? Mouse.current.rightButton.isPressed
            : Mouse.current.leftButton.isPressed;
        Vector3 spawnPos = FirePoint != null ? FirePoint.position : transform.position;
        Vector3 aimDir   = GetAimDirection();

        if (isFiring && _fireTimer <= 0f && !_isBursting)
        {
            if (IsSpecialActive && SpecialAmmo > 0)
            {
                FireSpecial(spawnPos, aimDir);
                _fireTimer = SpecialFireRate();
                SpecialAmmo--;
                if (SpecialAmmo <= 0) DeactivateSpecial();
            }
            else if (!IsSpecialActive)
            {
                if (CurrentPattern == BulletPatternType.Spread)
                {
                    StartCoroutine(BurstCoroutine(spawnPos, aimDir));
                    _fireTimer = BurstCooldown + BurstDelay * (IsEnhanced ? BurstCount + 1 : BurstCount);
                }
                else
                {
                    Fire(spawnPos, aimDir);
                    _fireTimer = CurrentPattern == BulletPatternType.Spiral ? OmniFireRate : FireRate;
                    NetworkManager.Instance?.SendBulletFire(spawnPos, aimDir, CurrentPattern, BulletSpeed);
                }
            }
        }

        _anim?.SetBool(AnimIsAttacking, isFiring);
    }

    // ─── 일반 발사 ────────────────────────────────────────────────────────

    void Fire(Vector3 origin, Vector3 aimDir)
    {
        switch (CurrentPattern)
        {
            case BulletPatternType.Straight:
                Spawn(origin, aimDir);
                if (IsEnhanced) Spawn(origin, Rotate(aimDir, 10f));
                break;

            case BulletPatternType.Spread:
                Spawn(origin, aimDir); // BurstCoroutine에서 처리, 여기선 단발
                break;

            case BulletPatternType.Spiral:
                for (int i = 0; i < 8; i++)
                    Spawn(origin, Rotate(Vector3.forward, i * 45f));
                if (IsEnhanced)
                    for (int i = 0; i < 8; i++)
                        Spawn(origin, Rotate(Vector3.forward, i * 45f + 22.5f));
                break;
        }
    }

    // ─── 점사 코루틴 ──────────────────────────────────────────────────────

    IEnumerator BurstCoroutine(Vector3 origin, Vector3 aimDir)
    {
        _isBursting = true;
        int   shots    = IsEnhanced ? BurstCount + 1 : BurstCount;
        int   fanCount = IsEnhanced ? 5 : 3;
        float halfSpan = SpreadAngle * (fanCount - 1) / 2f;

        for (int i = 0; i < shots; i++)
        {
            for (int j = 0; j < fanCount; j++)
                Spawn(origin, Rotate(aimDir, -halfSpan + SpreadAngle * j));
            NetworkManager.Instance?.SendBulletFire(origin, aimDir, BulletPatternType.Spread,
                BulletSpeed, fanCount: fanCount, spreadAngle: SpreadAngle);
            yield return new WaitForSeconds(BurstDelay);
        }

        _isBursting = false;
    }

    // ─── 특수무기 발사 ────────────────────────────────────────────────────

    void FireSpecial(Vector3 origin, Vector3 aimDir)
    {
        switch (ActiveSpecial)
        {
            // 강화 스파이럴: N개 팔을 동시 발사 + 매 발마다 전체 회전
            case SpecialWeaponType.EnhancedSpiral:
                float armAngle = 360f / ESpiralArms;
                for (int i = 0; i < ESpiralArms; i++)
                    SpawnCurve(origin, Rotate(Vector3.forward, _eSpiralAngle + armAngle * i),
                               ESpiralCurveSpeed);
                NetworkManager.Instance?.SendBulletFire(origin, aimDir, BulletPatternType.Spiral,
                    BulletSpeed, curveSpeed: ESpiralCurveSpeed);
                _eSpiralAngle += ESpiralStep;
                break;

            // 고속분쇄탄: 느리게 출발 → 일정 시간 후 급가속
            case SpecialWeaponType.Crusher:
                SpawnSpecial(origin, aimDir, BulletMode.Crusher,
                             CrusherInitSpeed, CrusherAccelDelay, CrusherAccelSpeed);
                NetworkManager.Instance?.SendBulletFire(origin, aimDir, BulletPatternType.Straight,
                    CrusherInitSpeed, BulletMode.Crusher, CrusherAccelDelay, CrusherAccelSpeed);
                break;

            // 유도탄: 거리에 반비례하는 유도 (멀수록 강함)
            case SpecialWeaponType.Homing:
                SpawnSpecial(origin, aimDir, BulletMode.Homing,
                             HomingSpeed, homingMaxDist: HomingMaxDist,
                             homingTurnRate: HomingTurnRate);
                NetworkManager.Instance?.SendBulletFire(origin, aimDir, BulletPatternType.Straight,
                    HomingSpeed, BulletMode.Homing);
                break;
        }
    }

    float SpecialFireRate() => ActiveSpecial switch
    {
        SpecialWeaponType.EnhancedSpiral => ESpiralFireRate,
        SpecialWeaponType.Crusher        => CrusherFireRate,
        SpecialWeaponType.Homing         => HomingFireRate,
        _                                => FireRate
    };

    // ─── Spawn 헬퍼 ───────────────────────────────────────────────────────

    void Spawn(Vector3 pos, Vector3 dir)
    {
        if (BulletPrefab == null) return;
        var obj = Instantiate(BulletPrefab, pos, Quaternion.identity);
        obj.GetComponent<Bullet>()?.Init(dir, BulletSpeed, BulletDamage, OwnerTag,
            isLocal: IsLocalPlayer);
    }

    void SpawnCurve(Vector3 pos, Vector3 dir, float curveSpeed)
    {
        if (BulletPrefab == null) return;
        var obj = Instantiate(BulletPrefab, pos, Quaternion.identity);
        var b   = obj.GetComponent<Bullet>();
        if (b == null) return;
        b.Init(dir, BulletSpeed, BulletDamage, OwnerTag, isLocal: IsLocalPlayer);
        b.CurveSpeed = curveSpeed;
    }

    void SpawnSpecial(Vector3 pos, Vector3 dir, BulletMode mode,
                      float speed,
                      float accelDelay     = 0.7f,
                      float accelSpeed     = 24f,
                      float homingMaxDist  = 18f,
                      float homingTurnRate = 180f)
    {
        if (BulletPrefab == null) return;
        var obj = Instantiate(BulletPrefab, pos, Quaternion.identity);
        obj.GetComponent<Bullet>()?.Init(dir, speed, BulletDamage, OwnerTag,
                                         mode, accelDelay, accelSpeed,
                                         homingMaxDist, homingTurnRate,
                                         isLocal: IsLocalPlayer);
    }

    // ─── 유틸 ─────────────────────────────────────────────────────────────

    Vector3 GetAimDirection()
    {
        // P2는 현재 facing 방향으로 발사
        if (PlayerIndex == 2) return transform.forward;

        Ray   ray         = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        if (groundPlane.Raycast(ray, out float dist))
        {
            Vector3 point = ray.GetPoint(dist);
            Vector3 dir   = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) return dir.normalized;
        }
        return transform.forward;
    }

    static Vector3 Rotate(Vector3 dir, float degrees)
        => Quaternion.Euler(0f, degrees, 0f) * dir;

    // ─── 원격 발사 (Dead Reckoning) ───────────────────────────────────────

    public void FireRemote(Vector3 position, Vector3 direction, BulletPatternType pattern,
                           float latency     = 0f,
                           float speed       = 8f,
                           BulletMode mode   = BulletMode.Normal,
                           float accelDelay  = 0.7f,
                           float accelSpeed  = 24f,
                           float curveSpeed  = 0f,
                           int   fanCount    = 1,
                           float spreadAngle = 0f)
    {
        if (BulletPrefab == null) return;
        float t = Mathf.Clamp(latency, 0f, 0.5f); // 500ms 초과는 버림

        switch (pattern)
        {
            case BulletPatternType.Straight:
                SpawnLatent(position, direction, speed, mode, accelDelay, accelSpeed, t, curveSpeed);
                break;

            case BulletPatternType.Spread:
                if (fanCount <= 1 || spreadAngle == 0f)
                {
                    SpawnLatent(position, direction, speed, mode, accelDelay, accelSpeed, t, curveSpeed);
                }
                else
                {
                    float halfSpan = spreadAngle * (fanCount - 1) / 2f;
                    for (int j = 0; j < fanCount; j++)
                        SpawnLatent(position, Rotate(direction, -halfSpan + spreadAngle * j),
                                    speed, mode, accelDelay, accelSpeed, t, curveSpeed);
                }
                break;

            case BulletPatternType.Spiral:
                for (int i = 0; i < 8; i++)
                    SpawnLatent(position, Rotate(Vector3.forward, i * 45f),
                                speed, mode, accelDelay, accelSpeed, t, curveSpeed);
                break;
        }
    }

    // latency만큼 이미 경과한 것으로 Bullet.Init에 전달 → 위치·상태 자동 보정
    void SpawnLatent(Vector3 pos, Vector3 dir, float speed, BulletMode mode,
                     float accelDelay, float accelSpeed, float elapsedTime,
                     float curveSpeed = 0f)
    {
        if (BulletPrefab == null) return;
        var obj = Instantiate(BulletPrefab, pos, Quaternion.identity);
        var b   = obj.GetComponent<Bullet>();
        if (b == null) return;
        b.Init(dir, speed, BulletDamage, OwnerTag,
            mode, accelDelay, accelSpeed,
            HomingMaxDist, HomingTurnRate,
            elapsedTime: elapsedTime,
            isLocal: IsLocalPlayer);
        if (curveSpeed != 0f) b.CurveSpeed = curveSpeed;
    }

    // ─── 외부 API ─────────────────────────────────────────────────────────

    public void SwitchPattern(BulletPatternType pattern)
    {
        CurrentPattern  = pattern;
        IsSpecialActive = false;
    }

    // 특수무기 획득 (몬스터 드롭 시 호출)
    public void AcquireSpecialWeapon(SpecialWeaponType type, int ammo)
    {
        ActiveSpecial = type;
        SpecialAmmo   = ammo;
        // 자동 활성화하지 않음 — 플레이어가 4키로 선택
    }

    public void TryActivateSpecial()
    {
        if (ActiveSpecial != SpecialWeaponType.None && SpecialAmmo > 0)
            IsSpecialActive = true;
    }

    void DeactivateSpecial()
    {
        IsSpecialActive = false;
        ActiveSpecial   = SpecialWeaponType.None;
        SpecialAmmo     = 0;
        _eSpiralAngle   = 0f;
    }

    public void ActivateEnhancement(float duration = 10f)
    {
        IsEnhanced     = true;
        _enhancedTimer = duration;
    }
}
