using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class RoomListUI : MonoBehaviour
{
    [Header("방 목록")]
    public Transform  RoomListContent;
    public GameObject RoomItemPrefab;
    public Button     RefreshButton;

    [Header("방 만들기")]
    public Button CreateRoomButton;

    [Header("방 만들기 팝업")]
    public GameObject     CreateRoomPanel;
    public TMP_InputField RoomNameInput;
    public Button         ConfirmCreateButton;
    public Button         CancelCreateButton;

    [Header("하단")]
    public Button   BackButton;
    public TMP_Text StatusText;
    public TMP_Text PlayerIdLabel;

    readonly List<GameObject> _items = new();
    bool _isLoading;

    void Start()
    {
        PlayerIdLabel.text = $"접속 중: {ApiClient.Instance?.PlayerId ?? "-"}";
        CreateRoomPanel.SetActive(false);

        RefreshButton.onClick.AddListener(LoadRooms);
        CreateRoomButton.onClick.AddListener(() => CreateRoomPanel.SetActive(true));
        ConfirmCreateButton.onClick.AddListener(OnConfirmCreate);
        CancelCreateButton.onClick.AddListener(() => CreateRoomPanel.SetActive(false));
        BackButton.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

        LoadRooms();
    }

    // ─── 방 목록 ──────────────────────────────────────────────────────────

    void LoadRooms()
    {
        if (_isLoading) return;
        _isLoading = true;
        SetStatus("방 목록 불러오는 중...", Color.yellow);
        RefreshButton.interactable = false;

        ApiClient.Instance.GetRooms(OnRoomsLoaded, OnError);
    }

    void OnRoomsLoaded(List<RoomInfo> rooms)
    {
        _isLoading = false;
        RefreshButton.interactable = true;

        foreach (var item in _items) Destroy(item);
        _items.Clear();

        if (rooms == null || rooms.Count == 0)
        {
            SetStatus("열려 있는 방이 없습니다.", Color.gray);
            return;
        }

        foreach (var room in rooms)
        {
            var obj  = Instantiate(RoomItemPrefab, RoomListContent);
            var item = obj.GetComponent<RoomItemUI>();
            item.Bind(room, OnJoinRoom);
            _items.Add(obj);
        }

        SetStatus($"방 {rooms.Count}개", Color.white);
    }

    // ─── 방 입장 ──────────────────────────────────────────────────────────

    void OnJoinRoom(RoomInfo room)
    {
        SetStatus($"입장 중... {room.roomName}", Color.yellow);
        RefreshButton.interactable    = false;
        CreateRoomButton.interactable = false;

        ApiClient.Instance.JoinRoom(room.roomId, res =>
        {
            PlayerPrefs.SetString("RoomId",    res.roomId);
            PlayerPrefs.SetString("RelayHost", res.relayHost);
            PlayerPrefs.SetInt("TcpPort",      res.tcpPort);
            PlayerPrefs.SetInt("UdpPort",      res.udpPort);
            PlayerPrefs.Save();

            // 씬 로드 전에 WS 연결 선제 시작
            if (NetworkManager.Instance == null)
            {
                var go = new GameObject("NetworkManager");
                go.AddComponent<NetworkManager>();
            }
            else
            {
                NetworkManager.Instance.Disconnect();
            }
            NetworkManager.Instance.EarlyConnect(
                res.relayHost, res.tcpPort, res.udpPort,
                res.roomId, ApiClient.Instance.AuthToken);

            SceneManager.LoadScene("GameScene");
        }, OnError);
    }

    // ─── 방 만들기 ────────────────────────────────────────────────────────

    void OnConfirmCreate()
    {
        string name = RoomNameInput.text.Trim();
        if (string.IsNullOrEmpty(name)) { SetStatus("방 이름을 입력하세요.", Color.red); return; }

        CreateRoomPanel.SetActive(false);
        SetStatus("방 생성 중...", Color.yellow);

        ApiClient.Instance.CreateRoom(name, room => OnJoinRoom(room), OnError);
    }

    // ─── 공통 ─────────────────────────────────────────────────────────────

    void OnError(string err)
    {
        _isLoading = false;
        RefreshButton.interactable    = true;
        CreateRoomButton.interactable = true;
        SetStatus($"오류: {err}", Color.red);
        Debug.LogWarning($"[RoomList] {err}");
    }

    void SetStatus(string msg, Color color)
    {
        StatusText.text  = msg;
        StatusText.color = color;
    }
}
