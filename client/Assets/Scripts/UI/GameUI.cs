using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    // ── HP ───────────────────────────────────────────────────────────────
    [Header("Player 1 (Local)")]
    public Slider   Player1HPBar;
    public TMP_Text Player1HPText;

    [Header("Player 2 (Remote)")]
    public Slider   Player2HPBar;
    public TMP_Text Player2HPText;

    [Header("Dash Cooldown")]
    public Slider DashBar;

    // ── 퀵슬롯 ──────────────────────────────────────────────────────────
    [Header("Quickslot")]
    public Image[]  SlotBorders;  // 4개 – 테두리 색으로 선택 표시
    public Image    Slot4Dim;     // 4번 슬롯 어둠 오버레이 (무기 없을 때)
    public TMP_Text Slot4Label;   // 특수무기 이름
    public TMP_Text Slot4Ammo;    // 탄수

    // ── 게임오버 ─────────────────────────────────────────────────────────
    [Header("Game Over")]
    public GameObject GameOverPanel;
    public TMP_Text   ResultText;
    public Button     RestartButton;

    // ── 색상 상수 ─────────────────────────────────────────────────────────
    static readonly Color ActiveBorder   = new Color(1.00f, 1.00f, 1.00f, 0.95f);
    static readonly Color InactiveBorder = new Color(0.40f, 0.40f, 0.40f, 0.60f);

    BulletShooter    _localShooter;
    PlayerController _localCtrl;
    bool             _positionsSwapped;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (GameOverPanel != null) GameOverPanel.SetActive(false);
    }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm == null) { Debug.LogWarning("[GameUI] GameManager 없음"); return; }

        gm.OnGameOver.AddListener(ShowGameOver);

        if (gm.LocalPlayer != null)
        {
            gm.LocalPlayer.OnHPChanged.AddListener(UpdatePlayer1HP);
            UpdatePlayer1HP(gm.LocalPlayer.CurrentHP, gm.LocalPlayer.MaxHP);
        }
        if (gm.RemotePlayer != null)
        {
            gm.RemotePlayer.OnHPChanged.AddListener(UpdatePlayer2HP);
            UpdatePlayer2HP(gm.RemotePlayer.CurrentHP, gm.RemotePlayer.MaxHP);
        }

        _localShooter = gm.LocalPlayer?.GetComponent<BulletShooter>();
        _localCtrl    = gm.LocalPlayer?.GetComponent<PlayerController>();
        RestartButton?.onClick.AddListener(OnRestartClicked);
    }

    void Update()
    {
        UpdateDashBar();
        UpdateQuickslot();
    }

    // ── 대시 바 ───────────────────────────────────────────────────────────
    void UpdateDashBar()
    {
        if (DashBar != null && _localCtrl != null)
            DashBar.value = 1f - _localCtrl.DashCooldownRatio;
    }

    // ── 퀵슬롯 ───────────────────────────────────────────────────────────
    void UpdateQuickslot()
    {
        if (_localShooter == null || SlotBorders == null || SlotBorders.Length < 4) return;

        // 활성 슬롯 인덱스: 0=Straight 1=Spread 2=Spiral 3=Special
        int activeIdx = _localShooter.IsSpecialActive
            ? 3
            : (int)_localShooter.CurrentPattern;

        for (int i = 0; i < 4; i++)
        {
            if (SlotBorders[i] != null)
                SlotBorders[i].color = i == activeIdx ? ActiveBorder : InactiveBorder;
        }

        bool hasSpecial = _localShooter.ActiveSpecial != SpecialWeaponType.None;

        if (Slot4Dim   != null) Slot4Dim.enabled = !hasSpecial;
        if (Slot4Label != null) Slot4Label.text   = hasSpecial ? SpecialLabel(_localShooter.ActiveSpecial) : "---";
        if (Slot4Ammo  != null) Slot4Ammo.text    = hasSpecial ? $"\u00d7{_localShooter.SpecialAmmo}" : "";
    }

    static string SpecialLabel(SpecialWeaponType t) => t switch
    {
        SpecialWeaponType.EnhancedSpiral => "스파이럴+",
        SpecialWeaponType.Crusher        => "분쇄탄",
        SpecialWeaponType.Homing         => "유도탄",
        _                                => "---"
    };

    // ── HP ───────────────────────────────────────────────────────────────
    void UpdatePlayer1HP(int cur, int max)
    {
        if (Player1HPBar  != null) Player1HPBar.value = (float)cur / max;
        if (Player1HPText != null) Player1HPText.text = $"{cur} / {max}";
    }

    void UpdatePlayer2HP(int cur, int max)
    {
        if (Player2HPBar  != null) Player2HPBar.value = (float)cur / max;
        if (Player2HPText != null) Player2HPText.text = $"{cur} / {max}";
    }

    // P2 역할 확정 후 NetworkManager에서 호출 — 캐시·HP 이벤트·UI 위치 갱신
    public void RefreshPlayerRefs()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // 기존 리스너 전부 해제 (어느 플레이어에 붙었든)
        gm.LocalPlayer?.OnHPChanged.RemoveListener(UpdatePlayer1HP);
        gm.LocalPlayer?.OnHPChanged.RemoveListener(UpdatePlayer2HP);
        gm.RemotePlayer?.OnHPChanged.RemoveListener(UpdatePlayer1HP);
        gm.RemotePlayer?.OnHPChanged.RemoveListener(UpdatePlayer2HP);

        // 새 역할로 재구독
        if (gm.LocalPlayer != null)
        {
            gm.LocalPlayer.OnHPChanged.AddListener(UpdatePlayer1HP);
            UpdatePlayer1HP(gm.LocalPlayer.CurrentHP, gm.LocalPlayer.MaxHP);
        }
        if (gm.RemotePlayer != null)
        {
            gm.RemotePlayer.OnHPChanged.AddListener(UpdatePlayer2HP);
            UpdatePlayer2HP(gm.RemotePlayer.CurrentHP, gm.RemotePlayer.MaxHP);
        }

        _localShooter = gm.LocalPlayer?.GetComponent<BulletShooter>();
        _localCtrl    = gm.LocalPlayer?.GetComponent<PlayerController>();

        // P1 위치와 P2 위치 교환 — 로컬 플레이어 HP바가 항상 같은 위치에 오도록
        if (!_positionsSwapped)
        {
            SwapHPBarPositions();
            _positionsSwapped = true;
        }
    }

    void SwapHPBarPositions()
    {
        if (Player1HPBar == null || Player2HPBar == null) return;

        var rt1 = Player1HPBar.GetComponent<RectTransform>();
        var rt2 = Player2HPBar.GetComponent<RectTransform>();
        (rt1.anchoredPosition, rt2.anchoredPosition) = (rt2.anchoredPosition, rt1.anchoredPosition);
        (rt1.sizeDelta,        rt2.sizeDelta)        = (rt2.sizeDelta,        rt1.sizeDelta);

        if (Player1HPText != null && Player2HPText != null)
        {
            var t1 = Player1HPText.GetComponent<RectTransform>();
            var t2 = Player2HPText.GetComponent<RectTransform>();
            (t1.anchoredPosition, t2.anchoredPosition) = (t2.anchoredPosition, t1.anchoredPosition);
        }
    }

    // ── 게임오버 ─────────────────────────────────────────────────────────
    void ShowGameOver(bool localWon)
    {
        if (GameOverPanel != null) GameOverPanel.SetActive(true);
        if (ResultText    != null) ResultText.text = localWon ? "YOU WIN!" : "YOU LOSE...";
    }

    void OnRestartClicked()
    {
        NetworkManager.Instance?.Disconnect();
        UnityEngine.SceneManagement.SceneManager.LoadScene("RoomList");
    }
}
