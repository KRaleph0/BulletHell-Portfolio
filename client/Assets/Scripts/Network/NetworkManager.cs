using UnityEngine;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

/// <summary>
/// WebSocket (TCP 3000) : 게임 이벤트 — JSON 메시지
/// UDP         (UDP 7777) : 위치·탄막 실시간 동기화 — JSON
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Relay (Lightsail)")]
    public string RelayHost = "";
    public int    WsPort    = 3000;
    public int    UdpPort   = 7777;

    public string PlayerId  { get; private set; }  // 로그인 ID
    public string SessionId { get; private set; }  // 릴레이 배정 세션 UUID
    public string RoomId    { get; private set; }
    public int    PlayerIndex { get; private set; } // 1 or 2

    // WebSocket
    ClientWebSocket _ws;
    Thread          _wsThread;
    CancellationTokenSource _cts;

    // UDP
    UdpClient _udpClient;
    Thread    _udpThread;

    ConcurrentQueue<string> _wsQueue  = new ConcurrentQueue<string>();
    ConcurrentQueue<string> _udpQueue = new ConcurrentQueue<string>();

    bool   _connected;
    bool   _rolesConfigured;
    uint   _posSendSeq    = 1;
    uint   _bulletSendSeq = 1;
    uint   _hpSendSeq     = 1;
    uint   _hitSendSeq    = 1;
    uint   _lastPosSeq;
    uint   _lastBulletSeq;
    uint   _lastHpSeq;
    uint   _lastHitSeq;
    string _authToken;

    PlayerController _localCtrl;

    [Header("Remote Player")]
    public PlayerController RemotePlayerController;
    public BulletShooter    RemoteBulletShooter;
    public PlayerHealth     RemotePlayerHealth;

    [Header("Waiting Room")]
    public WaitingRoomUI WaitingUI;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // 기존 싱글톤으로 Inspector 참조 이전
            Instance.RemotePlayerController = RemotePlayerController;
            Instance.RemoteBulletShooter    = RemoteBulletShooter;
            Instance.RemotePlayerHealth     = RemotePlayerHealth;
            Instance.WaitingUI              = WaitingUI;
            // gameObject 전체가 아닌 컴포넌트만 제거 — 같은 GameObject의 GameManager 보호
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Awake에서 Destroy된 중복 인스턴스는 Start 진입 즉시 종료 (유령 WS 연결 방지)
        if (Instance != this) return;

        // EarlyConnect로 시작한 스레드가 살아있으면 Skip, 실패했으면 재시도
        if (_wsThread != null && _wsThread.IsAlive) return;

        RelayHost   = PlayerPrefs.GetString("RelayHost", RelayHost);
        PlayerId    = PlayerPrefs.GetString("PlayerId",  "");
        RoomId      = PlayerPrefs.GetString("RoomId",    "");
        _authToken  = PlayerPrefs.GetString("AuthToken", "");
        WsPort      = PlayerPrefs.GetInt("TcpPort",  WsPort);
        UdpPort     = PlayerPrefs.GetInt("UdpPort",  UdpPort);

        if (!string.IsNullOrEmpty(RelayHost) && !string.IsNullOrEmpty(PlayerId))
            Connect();
    }

    // GAME_START 수신 시 호출 — PlayerIndex 확정 후 P2의 로컬/원격 역할을 뒤집음
    public void ConfigurePlayerRoles(GameManager gm)
    {
        // P1은 Inspector 기본값 그대로 사용; 이중 호출 방지
        if (PlayerIndex != 2 || _rolesConfigured) return;

        if (RemotePlayerController == null)
        {
            Debug.LogError("[Net] RemotePlayerController가 null — Inspector에서 원격 플레이어를 연결하세요.");
            return;
        }

        // 씬 내 두 PlayerController 확정
        var allControllers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        PlayerController localCtrl  = null;
        PlayerController remoteCtrl = RemotePlayerController;

        foreach (var c in allControllers)
        {
            if (c != remoteCtrl) { localCtrl = c; break; }
        }
        if (localCtrl == null)
        {
            Debug.LogError("[Net] 두 번째 PlayerController를 찾을 수 없습니다.");
            return;
        }

        // P2 클라이언트: Inspector의 "Remote"(원래 P1용 원격) → 이 클라이언트의 로컬
        remoteCtrl.IsLocalPlayer = true;
        remoteCtrl.PlayerIndex   = 1;   // WASD + 마우스
        localCtrl.IsLocalPlayer  = false;

        // BulletShooter 동기화
        var remoteBs = remoteCtrl.GetComponent<BulletShooter>();
        var localBs  = localCtrl.GetComponent<BulletShooter>();
        if (remoteBs != null) { remoteBs.IsLocalPlayer = true;  remoteBs.PlayerIndex = 1; }
        if (localBs  != null) { localBs.IsLocalPlayer  = false; }

        // NetworkManager 원격 참조 교체 (이제 P1 위저드가 원격)
        RemotePlayerController = localCtrl;
        RemoteBulletShooter    = localCtrl.GetComponent<BulletShooter>();
        RemotePlayerHealth     = localCtrl.GetComponent<PlayerHealth>();

        // GameManager 로컬/원격 교체
        (gm.LocalPlayer, gm.RemotePlayer) = (gm.RemotePlayer, gm.LocalPlayer);

        // _localCtrl 캐시 초기화 — 다음 SendPosition에서 새 LocalPlayer로 재캐시
        _localCtrl = null;

        // 카메라 타겟을 P2 로컬 위저드로 교체
        var cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.Target = remoteCtrl.transform;

        _rolesConfigured = true;
        GameUI.Instance?.RefreshPlayerRefs();
        Debug.Log("[Net] P2 역할 재구성 완료");
    }

    // 씬 전환 전에 RoomListUI에서 호출 — 씬 로드 중 WS 연결을 완료시킴
    public void EarlyConnect(string relayHost, int wsPort, int udpPort, string roomId, string authToken)
    {
        RelayHost  = relayHost;
        WsPort     = wsPort;
        UdpPort    = udpPort;
        RoomId     = roomId;
        _authToken = authToken;
        PlayerId   = PlayerPrefs.GetString("PlayerId", "");
        Connect();
    }

    // ─── 연결 ──────────────────────────────────────────────────────────────

    public void Connect()
    {
        _connected = true;
        _cts = new CancellationTokenSource();

        _wsThread = new Thread(WsLoop) { IsBackground = true };
        _wsThread.Start();

        ConnectUdp();
        InvokeRepeating(nameof(SendPosition), 0f, 0.05f);
        Debug.Log($"[Net] 연결 시작: ws://{RelayHost}:{WsPort}  UDP:{UdpPort}");
    }

    public void Disconnect()
    {
        _connected       = false;
        _rolesConfigured = false;
        _posSendSeq      = 1;
        _bulletSendSeq   = 1;
        _hpSendSeq       = 1;
        _hitSendSeq      = 1;
        _lastPosSeq      = 0;
        _lastBulletSeq   = 0;
        _lastHpSeq       = 0;
        _lastHitSeq      = 0;
        _localCtrl       = null;
        CancelInvoke(nameof(SendPosition));
        _cts?.Cancel();
        _udpClient?.Close();
    }

    // GAME_START 수신 후 GameManager가 준비될 때까지 대기 후 게임 시작
    IEnumerator ApplyGameStart()
    {
        float waited = 0f;
        while (GameManager.Instance == null)
        {
            waited += Time.unscaledDeltaTime;
            if (waited > 10f)
            {
                Debug.LogError("[Net] GameManager를 10초간 기다렸지만 없음 — GameScene에 GameManager 오브젝트가 있는지 확인하세요.");
                yield break;
            }
            yield return null;
        }

        if (waited > 0.01f)
            Debug.LogWarning($"[Net] GameManager 대기 {waited:F2}s — GameScene에 GameManager가 없거나 씬 로드가 지연됨");

        ConfigurePlayerRoles(GameManager.Instance);
        GameManager.Instance.StartGame();
    }

    // ─── WebSocket 루프 (백그라운드) ───────────────────────────────────────

    void WsLoop()
    {
        try
        {
            _ws = new ClientWebSocket();
            var wsUrl = $"ws://{RelayHost}:{WsPort}/ws/{RoomId}";
            Debug.Log($"[WS] 연결 시도: {wsUrl}");
            _ws.ConnectAsync(new Uri(wsUrl), _cts.Token).Wait();

            // 입장 요청
            WsSend(new WsJoin { type = "JOIN", token = _authToken, roomId = RoomId });

            var buf = new byte[65536];
            while (_connected && _ws.State == WebSocketState.Open)
            {
                var seg    = new ArraySegment<byte>(buf);
                var result = _ws.ReceiveAsync(seg, _cts.Token).Result;
                if (result.MessageType == WebSocketMessageType.Close) break;

                string msg = Encoding.UTF8.GetString(buf, 0, result.Count);
                if (!string.IsNullOrEmpty(msg)) _wsQueue.Enqueue(msg);
            }
        }
        catch (Exception e)
        {
            if (_connected) Debug.LogWarning($"[WS] {e.Message}");
        }
    }

    void WsSend(object data)
    {
        try
        {
            string json  = JsonUtility.ToJson(data);
            var    bytes = Encoding.UTF8.GetBytes(json);
            _ws?.SendAsync(new ArraySegment<byte>(bytes),
                           WebSocketMessageType.Text, true, _cts.Token).Wait();
        }
        catch (Exception e) { Debug.LogWarning($"[WS] Send: {e.Message}"); }
    }

    // ─── UDP ───────────────────────────────────────────────────────────────

    void ConnectUdp()
    {
        _udpClient = new UdpClient();
        _udpClient.Connect(RelayHost, UdpPort);
        _udpThread = new Thread(UdpLoop) { IsBackground = true };
        _udpThread.Start();
    }

    void UdpLoop()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_connected)
        {
            try
            {
                var data = _udpClient.Receive(ref ep);
                if (_udpQueue.Count < 120)
                    _udpQueue.Enqueue(Encoding.UTF8.GetString(data));
            }
            catch (SocketException)
            {
                if (!_connected) break;
                Thread.Sleep(1000);
                try { RecreateUdpClient(); }
                catch { break; }
            }
            catch { break; }
        }
    }

    void RecreateUdpClient()
    {
        var next = new UdpClient();
        next.Connect(RelayHost, UdpPort);
        var old = _udpClient;
        _udpClient = next;
        old?.Close();
        Debug.Log("[UDP] 재연결 완료");
    }

    void SendPosition()
    {
        if (!_connected || string.IsNullOrEmpty(SessionId)) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying()) return;

        if (_localCtrl == null)
            _localCtrl = GameManager.Instance.LocalPlayer?.GetComponent<PlayerController>();
        if (_localCtrl == null) return;

        Vector3 pos = _localCtrl.transform.position;
        float   rot = _localCtrl.transform.eulerAngles.y;
        UdpSend(new UdpPos
        {
            type     = "POS",
            seq      = _posSendSeq++,
            playerId = SessionId,
            roomId   = RoomId,
            x = pos.x, z = pos.z, rot = rot
        });
    }

    public void SendBulletFire(Vector3 pos, Vector3 dir, BulletPatternType pattern,
                               float speed      = 8f,
                               BulletMode mode  = BulletMode.Normal,
                               float accelDelay = 0.7f,
                               float accelSpeed = 24f,
                               float curveSpeed = 0f,
                               int   fanCount   = 1,
                               float spreadAngle = 0f)
    {
        if (!_connected || string.IsNullOrEmpty(SessionId)) return;
        UdpSend(new UdpBullet
        {
            type        = "BULLET",
            seq         = _bulletSendSeq++,
            playerId    = SessionId,
            roomId      = RoomId,
            x = pos.x, y = pos.y, z = pos.z,
            dx = dir.x, dz = dir.z,
            pattern     = (int)pattern,
            timestamp   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            speed       = speed,
            mode        = (int)mode,
            accelDelay  = accelDelay,
            accelSpeed  = accelSpeed,
            curveSpeed  = curveSpeed,
            fanCount    = fanCount,
            spreadAngle = spreadAngle,
        });
    }

    // 피격 이벤트 + HP 권위값을 하나의 패킷으로 — damage는 시각 효과용, hp는 보정용
    public void SendHit(int damage, int hp, int maxHp)
    {
        if (!_connected || string.IsNullOrEmpty(SessionId)) return;
        UdpSend(new UdpHit
        {
            type     = "HIT",
            seq      = _hitSendSeq++,
            playerId = SessionId,
            roomId   = RoomId,
            damage   = damage,
            hp       = hp,
            maxHp    = maxHp
        });
    }

    public void SendHP(int hp, int maxHp)
    {
        if (!_connected || string.IsNullOrEmpty(SessionId)) return;
        UdpSend(new UdpHp
        {
            type     = "HP",
            seq      = _hpSendSeq++,
            playerId = SessionId,
            roomId   = RoomId,
            hp       = hp,
            maxHp    = maxHp
        });
    }

    void UdpSend(object data)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
            _udpClient?.Send(bytes, bytes.Length);
        }
        catch (Exception e) { Debug.LogWarning($"[UDP] {e.Message}"); }
    }

    // ─── 메인 스레드 처리 ─────────────────────────────────────────────────

    void Update()
    {
        while (_wsQueue.TryDequeue(out string msg))  HandleWs(msg);
        while (_udpQueue.TryDequeue(out string msg)) HandleUdp(msg);
    }

    void HandleWs(string json)
    {
        var base_ = JsonUtility.FromJson<WsBase>(json);
        switch (base_.type)
        {
            case "CONNECTED":
                break;

            case "JOINED":
                var joined = JsonUtility.FromJson<WsJoined>(json);
                SessionId   = joined.playerId;
                PlayerIndex = joined.playerIndex;
                WaitingUI?.SetAsHost(joined.isHost);
                Debug.Log($"[Net] JOINED — SessionId={SessionId} PlayerIndex={PlayerIndex} isHost={joined.isHost}");
                break;

            case "PLAYER_STATE":
                var ps = JsonUtility.FromJson<WsPlayerState>(json);
                if (ps.players == null) break;
                var p1 = ps.players.Length > 0 ? ps.players[0] : null;
                var p2 = ps.players.Length > 1 ? ps.players[1] : null;
                WaitingUI?.UpdatePlayers(
                    p1?.username ?? "", p1?.isReady ?? false, p1?.isHost ?? false,
                    p2?.username ?? "", p2?.isReady ?? false, p2?.isHost ?? false);
                break;

            case "CHAT":
                var chat = JsonUtility.FromJson<WsChatMsg>(json);
                WaitingUI?.AddChatMessage(chat.username, chat.text);
                break;

            case "GAME_STARTING":
                WaitingUI?.StartCountdown();
                break;

            case "GAME_START":
                Debug.Log($"[Net] GAME_START — PlayerIndex={PlayerIndex} SessionId={SessionId}");
                WaitingUI?.Hide();
                StartCoroutine(ApplyGameStart());
                break;

            case "GAME_OVER":
                var go = JsonUtility.FromJson<WsGameOver>(json);
                GameManager.Instance?.OnGameOver?.Invoke(go.winnerId == SessionId);
                break;

            case "OPPONENT_DISCONNECTED":
                Debug.Log("[WS] 상대방 연결 끊김");
                break;

            case "ERROR":
                var err = JsonUtility.FromJson<WsError>(json);
                Debug.LogWarning($"[WS] 오류: {err.code}");
                break;
        }
    }

    void HandleUdp(string json)
    {
        var base_ = JsonUtility.FromJson<WsBase>(json);
        switch (base_.type)
        {
            case "POS":
                var pos = JsonUtility.FromJson<UdpPos>(json);
                if (pos.playerId == SessionId) return;
                if (pos.seq <= _lastPosSeq) return;
                _lastPosSeq = pos.seq;
                if (RemotePlayerController != null)
                    RemotePlayerController.SetRemotePosition(
                        new Vector3(pos.x, RemotePlayerController.transform.position.y, pos.z), pos.rot);
                break;

            case "BULLET":
                var b = JsonUtility.FromJson<UdpBullet>(json);
                if (b.playerId == SessionId) return;
                if (b.seq <= _lastBulletSeq) return;
                _lastBulletSeq = b.seq;
                float latency = b.timestamp > 0
                    ? (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - b.timestamp) / 1000f
                    : 0f;
                RemoteBulletShooter?.FireRemote(
                    new Vector3(b.x, b.y, b.z),
                    new Vector3(b.dx, 0f, b.dz),
                    (BulletPatternType)b.pattern,
                    latency, b.speed,
                    (BulletMode)b.mode,
                    b.accelDelay, b.accelSpeed,
                    b.curveSpeed,
                    b.fanCount, b.spreadAngle);
                break;

            case "HIT":
                var hit = JsonUtility.FromJson<UdpHit>(json);
                if (hit.playerId == SessionId) return;
                if (hit.seq <= _lastHitSeq) return;
                _lastHitSeq = hit.seq;
                RemotePlayerHealth?.SetHP(hit.hp);   // 권위값으로 보정
                break;

            case "HP":
                var hpMsg = JsonUtility.FromJson<UdpHp>(json);
                if (hpMsg.playerId == SessionId) return;
                if (hpMsg.seq <= _lastHpSeq) return;
                _lastHpSeq = hpMsg.seq;
                RemotePlayerHealth?.SetHP(hpMsg.hp);
                break;
        }
    }

    public void SendReadyToggle() => WsSend(new WsBase { type = "READY_TOGGLE" });
    public void SendChat(string text) => WsSend(new WsChat { type = "CHAT", text = text });
    public void SendStartGame()       => WsSend(new WsBase { type = "START_GAME" });

    // 로컬 플레이어 사망 시 릴레이에 알림 (PlayerHealth에서 호출)
    public void SendPlayerDied()
    {
        WsSend(new WsBase { type = "PLAYER_DIED" });
    }

    void OnDestroy() => Disconnect();

    // ─── 메시지 직렬화 구조체 ─────────────────────────────────────────────

    [Serializable] class WsBase       { public string type; }
    [Serializable] class WsJoin       { public string type; public string token; public string roomId; }
    [Serializable] class WsChat       { public string type; public string text; }
    [Serializable] class WsJoined     { public string type; public string playerId; public int playerIndex; public bool isHost; }
    [Serializable] class WsGameOver   { public string type; public string winnerId; public string loserId; }
    [Serializable] class WsError      { public string type; public string code; }
    [Serializable] class WsChatMsg    { public string type; public string username; public string text; }
    [Serializable] class WsPlayerInfo { public string playerId; public string username; public bool isReady; public bool isHost; public int index; }
    [Serializable] class WsPlayerState { public string type; public WsPlayerInfo[] players; }

    [Serializable] class UdpPos    { public string type; public uint seq; public string playerId; public string roomId; public float x, z, rot; }
    [Serializable] class UdpBullet { public string type; public uint seq; public string playerId; public string roomId; public float x, y, z, dx, dz; public int pattern; public long timestamp; public float speed; public int mode; public float accelDelay, accelSpeed, curveSpeed; public int fanCount; public float spreadAngle; }
    [Serializable] class UdpHp     { public string type; public uint seq; public string playerId; public string roomId; public int hp; public int maxHp; }
    [Serializable] class UdpHit    { public string type; public uint seq; public string playerId; public string roomId; public int damage; public int hp; public int maxHp; }
}
