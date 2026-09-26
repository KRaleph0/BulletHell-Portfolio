using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    [Header("Stats")]
    public int MaxHP = 100;
    public int CurrentHP { get; private set; }

    [Header("Shield")]
    public bool HasShield { get; private set; }

    public UnityEvent<int, int> OnHPChanged;  // (current, max)
    public UnityEvent OnDied;
    public UnityEvent OnShieldActivated;
    public UnityEvent OnShieldAbsorbed;

    public bool IsDead { get; private set; }

    void Awake()
    {
        CurrentHP = MaxHP;
    }

    public void TakeDamage(int damage)
    {
        if (IsDead) return;

        if (HasShield)
        {
            HasShield = false;
            OnShieldAbsorbed?.Invoke();
            return;
        }

        CurrentHP = Mathf.Max(0, CurrentHP - damage);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        if (GameManager.Instance?.LocalPlayer == this)
            NetworkManager.Instance?.SendHit(damage, CurrentHP, MaxHP);

        if (CurrentHP <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        if (GameManager.Instance?.LocalPlayer == this)
            NetworkManager.Instance?.SendHP(CurrentHP, MaxHP);
    }

    public void ActivateShield()
    {
        HasShield = true;
        OnShieldActivated?.Invoke();
    }

    // 서버에서 HP를 강제 설정할 때 사용 (서버 권위 판정)
    public void SetHP(int hp)
    {
        CurrentHP = Mathf.Clamp(hp, 0, MaxHP);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        if (CurrentHP <= 0 && !IsDead) Die();
    }

    public void RestoreFullHP()
    {
        IsDead    = false;
        CurrentHP = MaxHP;
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
    }

    void Die()
    {
        IsDead = true;
        OnDied?.Invoke();
        GameManager.Instance.OnPlayerDied(this);

        // 로컬 플레이어 사망 시 릴레이에 알림
        bool isLocal = GameManager.Instance.LocalPlayer == this;
        if (isLocal) NetworkManager.Instance?.SendPlayerDied();
    }
}
