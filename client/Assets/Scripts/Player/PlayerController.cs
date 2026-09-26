using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 5f;

    [Header("Dash")]
    public float DashSpeed    = 14f;
    public float DashDuration = 0.15f;
    public float DashCooldown = 1f;

    Rigidbody     _rb;
    BulletShooter _shooter;
    Animator      _anim;
    Vector2       _moveInput;      // X / Z 방향
    bool          _isDashing;
    float         _dashTimer;
    float         _dashCooldownTimer;
    Vector2       _dashDirection;

    // 원격 플레이어 목표 상태 (FixedUpdate에서 MovePosition으로 적용)
    Vector3 _remoteTargetPos;
    float   _remoteTargetRot;
    bool    _hasRemoteTarget;

    // 애니메이터 파라미터 이름 (Animator Controller에서 동일하게 사용)
    static readonly int AnimIsMoving = Animator.StringToHash("isMoving");

    // 원격 플레이어는 false
    public bool IsLocalPlayer = true;
    // 1 = P1 (WASD + 마우스), 2 = P2 (IJKL + 우클릭)
    public int  PlayerIndex   = 1;

    // 대시 쿨다운 비율 (0 = 준비됨, 1 = 방금 사용)
    public float DashCooldownRatio =>
        DashCooldown > 0f ? Mathf.Clamp01(_dashCooldownTimer / DashCooldown) : 0f;

    void Awake()
    {
        _rb      = GetComponent<Rigidbody>();
        _shooter = GetComponent<BulletShooter>();
        _anim    = GetComponentInChildren<Animator>();  // 모델 하위 Animator 자동 탐색

        _rb.useGravity  = false;
        _rb.constraints = RigidbodyConstraints.FreezePositionY
                        | RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationZ;
    }

    void Update()
    {
        if (!IsLocalPlayer) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying())
        {
            _moveInput = Vector2.zero;
            return;
        }

        GatherInput();
        HandleDash();
        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (!IsLocalPlayer)
        {
            ApplyRemoteMovement();
            return;
        }
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying()) return;
        Move();
    }

    void ApplyRemoteMovement()
    {
        if (!_hasRemoteTarget) return;
        _rb.MovePosition(Vector3.Lerp(transform.position, _remoteTargetPos, Time.fixedDeltaTime * 15f));
        _rb.MoveRotation(Quaternion.Lerp(transform.rotation,
            Quaternion.Euler(0f, _remoteTargetRot, 0f), Time.fixedDeltaTime * 15f));
    }

    void GatherInput()
    {
        var kb = Keyboard.current;

        if (PlayerIndex == 2)
            GatherInputP2(kb);
        else
            GatherInputP1(kb);
    }

    void GatherInputP1(Keyboard kb)
    {
        float h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                - (kb.aKey.isPressed || kb.leftArrowKey.isPressed  ? 1f : 0f);
        float v = (kb.wKey.isPressed || kb.upArrowKey.isPressed    ? 1f : 0f)
                - (kb.sKey.isPressed || kb.downArrowKey.isPressed   ? 1f : 0f);
        _moveInput = new Vector2(h, v).normalized;

        // 마우스 방향으로 캐릭터 회전
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        if (groundPlane.Raycast(ray, out float dist))
        {
            Vector3 point = ray.GetPoint(dist);
            Vector3 dir   = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        if (_shooter != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Straight);
            if (kb.digit2Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Spread);
            if (kb.digit3Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Spiral);
            if (kb.digit4Key.wasPressedThisFrame) _shooter.TryActivateSpecial();
        }
    }

    // P2 전용 입력 : IJKL 이동 / 이동 방향으로 조준 / 우클릭 발사
    void GatherInputP2(Keyboard kb)
    {
        float h = (kb.lKey.isPressed ? 1f : 0f) - (kb.jKey.isPressed ? 1f : 0f);
        float v = (kb.iKey.isPressed ? 1f : 0f) - (kb.kKey.isPressed ? 1f : 0f);
        _moveInput = new Vector2(h, v).normalized;

        // 이동 방향으로 캐릭터 회전
        if (_moveInput.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(
                new Vector3(_moveInput.x, 0f, _moveInput.y));

        if (_shooter != null)
        {
            if (kb.numpad1Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Straight);
            if (kb.numpad2Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Spread);
            if (kb.numpad3Key.wasPressedThisFrame) _shooter.SwitchPattern(BulletPatternType.Spiral);
            if (kb.numpad4Key.wasPressedThisFrame) _shooter.TryActivateSpecial();
        }
    }

    void HandleDash()
    {
        _dashCooldownTimer -= Time.deltaTime;

        bool dashPressed = PlayerIndex == 2
            ? Keyboard.current.rightShiftKey.wasPressedThisFrame
            : Keyboard.current.spaceKey.wasPressedThisFrame;

        if (dashPressed
            && _dashCooldownTimer <= 0f && !_isDashing)
        {
            _isDashing         = true;
            _dashTimer         = DashDuration;
            _dashCooldownTimer = DashCooldown;
            _dashDirection     = _moveInput != Vector2.zero
                ? _moveInput
                : new Vector2(transform.forward.x, transform.forward.z);
        }

        if (_isDashing)
        {
            _dashTimer -= Time.deltaTime;
            if (_dashTimer <= 0f) _isDashing = false;
        }
    }

    void UpdateAnimator()
    {
        if (_anim == null) return;
        // 이동 입력이 있거나 대시 중이면 isMoving = true
        bool moving = _moveInput.magnitude > 0.01f || _isDashing;
        _anim.SetBool(AnimIsMoving, moving);
    }

    void Move()
    {
        Vector3 vel = _isDashing
            ? new Vector3(_dashDirection.x, 0f, _dashDirection.y) * DashSpeed
            : new Vector3(_moveInput.x,     0f, _moveInput.y)     * MoveSpeed;

        // Y 속도는 유지 (중력 꺼져 있지만 안전하게)
        _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
    }

    // NetworkManager에서 원격 플레이어 목표 위치를 저장 → FixedUpdate에서 MovePosition 적용
    public void SetRemotePosition(Vector3 position, float rotationY)
    {
        if (IsLocalPlayer) return;
        _remoteTargetPos = position;
        _remoteTargetRot = rotationY;
        _hasRemoteTarget = true;
    }
}
