using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class WaitingRoomUI : MonoBehaviour
{
    [Header("Panel")]
    public GameObject Panel;
    public TMP_FontAsset ChatFont;

    [Header("Player List")]
    public TMP_Text P1NameText;
    public TMP_Text P1ReadyText;
    public TMP_Text P2NameText;
    public TMP_Text P2ReadyText;

    [Header("Chat")]
    public ScrollRect  ChatScrollRect;
    public RectTransform ChatContent;
    public TMP_InputField ChatInput;
    public Button         SendButton;

    [Header("Controls")]
    public Button   ReadyButton;
    public TMP_Text ReadyButtonLabel;
    public Button   StartButton;
    public TMP_Text CountdownText;

    bool _isReady;
    bool _isHost;

    static readonly Color ColorReady   = new Color(0.18f, 0.65f, 0.28f);
    static readonly Color ColorNotReady= new Color(0.30f, 0.30f, 0.48f);

    void Start()
    {
        Panel.SetActive(true);
        CountdownText.gameObject.SetActive(false);
        StartButton.gameObject.SetActive(false);

        ReadyButton.onClick.AddListener(OnReadyClicked);
        SendButton.onClick.AddListener(OnSendClicked);
        StartButton.onClick.AddListener(OnStartClicked);
        ChatInput.onSubmit.AddListener(_ => OnSendClicked());

        RefreshReadyButton();
        SetPlayerSlot(P1NameText, P1ReadyText, "대기 중...", false);
        SetPlayerSlot(P2NameText, P2ReadyText, "대기 중...", false);
    }

    // ─── 외부 API ────────────────────────────────────────────────────────────

    public void SetAsHost(bool isHost)
    {
        _isHost = isHost;
        StartButton.gameObject.SetActive(isHost);
    }

    public void UpdatePlayers(
        string p1Name, bool p1Ready, bool p1Host,
        string p2Name, bool p2Ready, bool p2Host)
    {
        string host1 = p1Host ? " [방장]" : "";
        string host2 = p2Host ? " [방장]" : "";
        SetPlayerSlot(P1NameText, P1ReadyText, string.IsNullOrEmpty(p1Name) ? "접속 중..." : p1Name + host1, p1Ready);
        SetPlayerSlot(P2NameText, P2ReadyText, string.IsNullOrEmpty(p2Name) ? "대기 중..." : p2Name + host2, p2Ready);

        if (_isHost)
            StartButton.interactable = p1Ready && p2Ready;
    }

    public void AddChatMessage(string username, string text)
    {
        var go  = new GameObject("ChatMsg");
        go.transform.SetParent(ChatContent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = $"<color=#88ffaa>{username}</color>: {text}";
        tmp.fontSize = 18;
        tmp.color    = Color.white;
        if (ChatFont != null) tmp.font = ChatFont;

        // 최신 메시지로 스크롤
        Canvas.ForceUpdateCanvases();
        ChatScrollRect.verticalNormalizedPosition = 0f;
    }

    public void StartCountdown() => StartCoroutine(CountdownCoroutine());

    IEnumerator CountdownCoroutine()
    {
        ReadyButton.interactable  = false;
        StartButton.interactable  = false;
        ChatInput.interactable    = false;
        CountdownText.gameObject.SetActive(true);

        for (int i = 3; i > 0; i--)
        {
            CountdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }
        CountdownText.text = "시작!";
    }

    public void Hide()
    {
        Panel.SetActive(false);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    // ─── 버튼 이벤트 ─────────────────────────────────────────────────────────

    void OnReadyClicked()
    {
        _isReady = !_isReady;
        RefreshReadyButton();
        NetworkManager.Instance?.SendReadyToggle();
    }

    void OnSendClicked()
    {
        string text = ChatInput.text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ChatInput.text = "";
        ChatInput.ActivateInputField();
        NetworkManager.Instance?.SendChat(text);
    }

    void OnStartClicked()
    {
        StartButton.interactable = false;
        NetworkManager.Instance?.SendStartGame();
    }

    // ─── 내부 헬퍼 ───────────────────────────────────────────────────────────

    void RefreshReadyButton()
    {
        ReadyButtonLabel.text = _isReady ? "준비 취소" : "준  비";
        ReadyButton.GetComponent<Image>().color = _isReady ? ColorReady : ColorNotReady;
    }

    void SetPlayerSlot(TMP_Text nameText, TMP_Text readyText, string name, bool ready)
    {
        nameText.text  = name;
        readyText.text  = ready ? "준비 완료" : "대기 중";
        readyText.color = ready ? new Color(0.3f, 1f, 0.5f) : new Color(0.5f, 0.5f, 0.5f);
    }

    void OnLeaveClicked()
    {
        NetworkManager.Instance?.Disconnect();
        SceneManager.LoadScene("RoomList");
    }
}
