using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// 방 목록의 개별 아이템 한 줄
/// </summary>
public class RoomItemUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text RoomNameText;
    public TMP_Text PlayerCountText;
    public TMP_Text StatusText;
    public Button   JoinButton;

    RoomInfo _room;
    Action<RoomInfo> _onJoin;

    static readonly Color ColorWaiting = new Color(0.3f, 0.9f, 0.4f);
    static readonly Color ColorPlaying = new Color(0.9f, 0.4f, 0.3f);

    public void Bind(RoomInfo room, Action<RoomInfo> onJoin)
    {
        _room   = room;
        _onJoin = onJoin;

        RoomNameText.text    = room.roomName;
        PlayerCountText.text = $"{room.playerCount} / {room.maxPlayers}";

        bool waiting = room.status == "waiting";
        StatusText.text  = waiting ? "대기중" : "게임중";
        StatusText.color = waiting ? ColorWaiting : ColorPlaying;

        // 꽉 찼거나 이미 시작한 방은 입장 불가
        bool canJoin = waiting && room.playerCount < room.maxPlayers;
        JoinButton.interactable = canJoin;
        JoinButton.onClick.RemoveAllListeners();
        JoinButton.onClick.AddListener(() => _onJoin?.Invoke(_room));
    }
}
