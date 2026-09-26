using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// TestScene 전용 - 특수무기 지급 / 디버그 단축키
/// F1 : 강화 스파이럴 지급 (30발)
/// F2 : 고속분쇄탄 지급 (25발)
/// F3 : 유도탄 지급 (15발)
/// F5 : Player2 HP 전액 회복
/// F6 : Player1 HP 전액 회복
/// </summary>
public class TestHelper : MonoBehaviour
{
    [Header("References (SceneBuilder 자동 연결)")]
    public BulletShooter LocalShooter;
    public PlayerHealth  LocalHealth;
    public PlayerHealth  RemoteHealth;

    [Header("UI")]
    public TMP_Text HintText;

    void Start()
    {
        // SceneBuilder가 연결 못한 경우 태그로 자동 탐색
        if (LocalShooter == null)
        {
            var p1 = GameObject.FindWithTag("Player1");
            if (p1 != null)
            {
                LocalShooter = p1.GetComponent<BulletShooter>();
                LocalHealth  = p1.GetComponent<PlayerHealth>();
            }
        }
        if (RemoteHealth == null)
        {
            var p2 = GameObject.FindWithTag("Player2");
            if (p2 != null) RemoteHealth = p2.GetComponent<PlayerHealth>();
        }

        if (HintText != null)
            HintText.text =
                "[ TEST MODE ]\n" +
                "F1  강화 스파이럴 ×30\n" +
                "F2  고속분쇄탄 ×25\n" +
                "F3  유도탄 ×15\n" +
                "F5  P2 HP 회복\n" +
                "F6  내 HP 회복";
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f1Key.wasPressedThisFrame) GiveWeapon(SpecialWeaponType.EnhancedSpiral, 30);
        if (kb.f2Key.wasPressedThisFrame) GiveWeapon(SpecialWeaponType.Crusher,        25);
        if (kb.f3Key.wasPressedThisFrame) GiveWeapon(SpecialWeaponType.Homing,         15);
        if (kb.f5Key.wasPressedThisFrame) RemoteHealth?.RestoreFullHP();
        if (kb.f6Key.wasPressedThisFrame) LocalHealth?.RestoreFullHP();
    }

    void GiveWeapon(SpecialWeaponType type, int ammo)
    {
        if (LocalShooter == null) return;
        LocalShooter.AcquireSpecialWeapon(type, ammo);
        Debug.Log($"[TestHelper] {type} ×{ammo} 지급");
    }
}
