using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;

// ─── 데이터 모델 ─────────────────────────────────────────────────────────────

[Serializable] public class RegisterRequest  { public string username; public string email; public string password; }
[Serializable] public class LoginRequest     { public string username; public string password; }
[Serializable] public class LoginResponse    { public string access_token; public string token_type; }
[Serializable] class ApiError { public string detail; }
[Serializable] public class RankEntry        { public string playerId; public int wins; public int losses; }
[Serializable] public class RankListResponse { public List<RankEntry> ranks; }

[Serializable] public class RoomInfo         { public string roomId; public string roomName; public int playerCount; public int maxPlayers; public string status; }
[Serializable] public class RoomListResponse { public List<RoomInfo> rooms; }
[Serializable] public class CreateRoomRequest{ public string roomName; public string hostId; }
[Serializable] public class JoinRoomResponse { public string roomId; public string relayHost; public int tcpPort; public int udpPort; }

// ─── ApiClient ────────────────────────────────────────────────────────────────

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    // 인증/방 관리 API 서버 주소 — 공개 저장소에서는 플레이스홀더로 대체
    const string BaseUrl = "https://your-api.example.com";

    public string AuthToken { get; private set; }
    public string PlayerId  { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        PlayerId  = PlayerPrefs.GetString("PlayerId",  "");
        AuthToken = PlayerPrefs.GetString("AuthToken", "");
    }

    // ─── 인증 ─────────────────────────────────────────────────────────────

    public void Register(string username, string email, string password,
                         Action onSuccess, Action<string> onError)
    {
        var body = JsonUtility.ToJson(new RegisterRequest { username = username, email = email, password = password });
        StartCoroutine(Post<LoginResponse>("/auth/register", body, _ => onSuccess?.Invoke(), onError));
    }

    public void Login(string username, string password,
                      Action<LoginResponse> onSuccess, Action<string> onError)
    {
        var body = JsonUtility.ToJson(new LoginRequest { username = username, password = password });
        StartCoroutine(Post<LoginResponse>("/auth/login", body, res =>
        {
            AuthToken = res.access_token;
            PlayerId  = username;
            PlayerPrefs.SetString("PlayerId",  PlayerId);
            PlayerPrefs.SetString("AuthToken", AuthToken);
            PlayerPrefs.Save();
            onSuccess?.Invoke(res);
        }, onError));
    }

    // ─── 방 목록 ─────────────────────────────────────────────────────────

    public void GetRooms(Action<System.Collections.Generic.List<RoomInfo>> onSuccess, Action<string> onError)
    {
        StartCoroutine(Get<RoomListResponse>("/rooms", res => onSuccess?.Invoke(res.rooms), onError));
    }

    public void CreateRoom(string roomName, Action<RoomInfo> onSuccess, Action<string> onError)
    {
        var body = JsonUtility.ToJson(new CreateRoomRequest { roomName = roomName, hostId = PlayerId });
        StartCoroutine(Post<RoomInfo>("/rooms", body, onSuccess, onError));
    }

    public void JoinRoom(string roomId, Action<JoinRoomResponse> onSuccess, Action<string> onError)
    {
        StartCoroutine(Post<JoinRoomResponse>($"/rooms/{roomId}/join", "{}", onSuccess, onError));
    }

    // ─── 랭킹 ────────────────────────────────────────────────────────────

    public void GetRank(Action<List<RankEntry>> onSuccess, Action<string> onError)
    {
        StartCoroutine(Get<RankListResponse>("/rank", res => onSuccess?.Invoke(res.ranks), onError));
    }

    // ─── HTTP 헬퍼 ────────────────────────────────────────────────────────

    IEnumerator Get<T>(string endpoint, Action<T> onSuccess, Action<string> onError)
    {
        using var req = UnityWebRequest.Get(BaseUrl + endpoint);
        SetHeaders(req);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            onError?.Invoke(req.error);
        else
            onSuccess?.Invoke(JsonUtility.FromJson<T>(req.downloadHandler.text));
    }

    IEnumerator Post<T>(string endpoint, string jsonBody,
                        Action<T> onSuccess, Action<string> onError)
    {
        using var req = new UnityWebRequest(BaseUrl + endpoint, "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
        req.downloadHandler = new DownloadHandlerBuffer();
        SetHeaders(req);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string body = req.downloadHandler?.text;
            Debug.LogWarning($"[API] POST {endpoint} → {req.responseCode} | body: {(string.IsNullOrEmpty(body) ? "<empty>" : body)}");
            string msg = req.error;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var err = JsonUtility.FromJson<ApiError>(body);
                    msg = string.IsNullOrEmpty(err?.detail) ? body : err.detail;
                }
                catch { msg = body; }
            }
            onError?.Invoke(msg);
        }
        else
            onSuccess?.Invoke(JsonUtility.FromJson<T>(req.downloadHandler.text));
    }

    void SetHeaders(UnityWebRequest req)
    {
        req.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(AuthToken))
            req.SetRequestHeader("Authorization", $"Bearer {AuthToken}");
    }
}
