using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Login Panel")]
    public GameObject     LoginPanel;
    public TMP_InputField PlayerIDField;
    public TMP_InputField PasswordField;
    public Button         LoginButton;
    public Button         ToRegisterButton;

    [Header("Register Panel")]
    public GameObject     RegisterPanel;
    public TMP_InputField RegPlayerIDField;
    public TMP_InputField RegEmailField;
    public TMP_InputField RegPasswordField;
    public TMP_InputField RegConfirmField;
    public Button         RegisterButton;
    public Button         ToLoginButton;

    [Header("Shared")]
    public TMP_Text StatusText;

    void Start()
    {
        LoginButton.onClick.AddListener(OnLoginClicked);
        ToRegisterButton.onClick.AddListener(() => ShowPanel(false));
        RegisterButton.onClick.AddListener(OnRegisterClicked);
        ToLoginButton.onClick.AddListener(() => ShowPanel(true));

        ShowPanel(true);
    }

    void ShowPanel(bool isLogin)
    {
        LoginPanel.SetActive(isLogin);
        RegisterPanel.SetActive(!isLogin);
        SetStatus("", Color.white);

        if (isLogin)
        {
            PlayerIDField.text = "";
            PasswordField.text = "";
        }
        else
        {
            RegPlayerIDField.text = "";
            RegEmailField.text    = "";
            RegPasswordField.text = "";
            RegConfirmField.text  = "";
        }
    }

    void OnLoginClicked()
    {
        string username = PlayerIDField.text.Trim();
        string password = PasswordField.text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("아이디 또는 비밀번호를 입력하세요.", Color.red);
            return;
        }

        SetStatus("로그인 중...", Color.yellow);
        LoginButton.interactable = false;

        ApiClient.Instance.Login(username, password, res =>
        {
            SceneManager.LoadScene("RoomList");
        },
        err =>
        {
            SetStatus($"로그인 실패: {err}", Color.red);
            LoginButton.interactable = true;
        });
    }

    void OnRegisterClicked()
    {
        string username = RegPlayerIDField.text.Trim();
        string email    = RegEmailField.text.Trim();
        string password = RegPasswordField.text.Trim();
        string confirm  = RegConfirmField.text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("모든 항목을 입력하세요.", Color.red);
            return;
        }

        if (password != confirm)
        {
            SetStatus("비밀번호가 일치하지 않습니다.", Color.red);
            return;
        }

        SetStatus("가입 중...", Color.yellow);
        RegisterButton.interactable = false;

        ApiClient.Instance.Register(username, email, password,
        () =>
        {
            SetStatus("가입 완료! 로그인하세요.", Color.green);
            RegisterButton.interactable = true;
            ShowPanel(true);
            PlayerIDField.text = username;
        },
        err =>
        {
            SetStatus($"가입 실패: {err}", Color.red);
            RegisterButton.interactable = true;
        });
    }

    void SetStatus(string msg, Color color)
    {
        if (StatusText == null) return;
        StatusText.text  = msg;
        StatusText.color = color;
    }
}
