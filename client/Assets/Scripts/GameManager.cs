using UnityEngine;
using UnityEngine.Events;

public enum GameState { Waiting, Playing, GameOver }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game State")]
    public GameState State { get; private set; } = GameState.Waiting;

    [Header("Players")]
    public PlayerHealth LocalPlayer;
    public PlayerHealth RemotePlayer;

    public UnityEvent<GameState> OnGameStateChanged;
    public UnityEvent<bool> OnGameOver; // true = 로컬 플레이어 승리

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 역할 구성은 GAME_START 수신 시 NetworkManager가 처리
    }

    public void StartGame()
    {
        if (State != GameState.Waiting) return;
        SetState(GameState.Playing);
    }

    public void OnPlayerDied(PlayerHealth deadPlayer)
    {
        if (State != GameState.Playing) return;

        bool localWon = deadPlayer == RemotePlayer;
        SetState(GameState.GameOver);
        OnGameOver?.Invoke(localWon);
    }

    void SetState(GameState newState)
    {
        State = newState;
        OnGameStateChanged?.Invoke(newState);
    }

    public bool IsPlaying() => State == GameState.Playing;
}
