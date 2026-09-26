using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using TMPro;
using System.IO;

/// <summary>
/// Unity 메뉴 BulletHell → Create Scenes 에서 씬 자동 생성
/// </summary>
public static class SceneBuilder
{
    const string ScenePath   = "Assets/Scenes/";
    const string ArtPath     = "Assets/Art/Generated/";
    const string FontPath    = "Assets/font/DNFBitBitv2 SDF.asset";
    const float  BlockSize   = 2f;   // FBX 블록 실제 크기 (2×2×2)
    const float  ArenaHalf   = 20f;  // 아레나 절반 = 블록 10개 × 2유닛 (40×40 맵)

    static TMP_FontAsset _font;
    static TMP_FontAsset Font => _font ??= AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

    // ═══════════════════════════════════════════════════════════════════════
    //  메뉴 항목
    // ═══════════════════════════════════════════════════════════════════════

    static bool NotInPlayMode()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[SceneBuilder] 플레이 모드에서는 씬을 생성할 수 없습니다. 플레이를 중지한 후 실행하세요.");
            return false;
        }
        return true;
    }

    [MenuItem("BulletHell/Create All Scenes")]
    static void CreateAll()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        EnsureTags();
        BuildMainMenuScene();
        BuildRoomListScene();
        BuildGameScene();
        AddScenesToBuildSettings();
        Debug.Log("[SceneBuilder] 씬 생성 완료!");
    }

    [MenuItem("BulletHell/Create Game Scene")]
    static void CreateGameSceneOnly()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        AssetDatabase.Refresh();
        EnsureTags();
        BuildGameScene();
        AddScenesToBuildSettings();
    }

    [MenuItem("BulletHell/Create MainMenu Scene")]
    static void CreateMainMenuOnly()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        BuildMainMenuScene();
        AddScenesToBuildSettings();
    }

    [MenuItem("BulletHell/Create RoomList Scene")]
    static void CreateRoomListOnly()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        BuildRoomListScene();
        AddScenesToBuildSettings();
    }

    [MenuItem("BulletHell/Create Test Scene")]
    static void CreateTestSceneOnly()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        AssetDatabase.Refresh();
        EnsureTags();
        BuildTestScene();
        AddScenesToBuildSettings();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GameScene
    // ═══════════════════════════════════════════════════════════════════════

    static void BuildGameScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 카메라 (3D 탑뷰 정사영) ──────────────────────────────────────
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.orthographic     = true;
        // 45° 클래식 탑뷰 (스타듀밸리/포켓몬 스타일)
        // tan(45°)=1  →  Y=18, Z=-18 로 아레나 중심(0,0,0) 조준
        cam.orthographicSize = 6.67f;
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.45f, 0.72f, 0.95f);
        cam.nearClipPlane    = 0.1f;
        cam.farClipPlane     = 100f;
        camObj.transform.position    = new Vector3(0f, 22f, -22f);
        camObj.transform.eulerAngles = new Vector3(45f, 0f, 0f);
        camObj.AddComponent<AudioListener>();

        // ── 카메라 추적 (CameraFollow) ────────────────────────────────────
        var follow = camObj.AddComponent<CameraFollow>();
        follow.Offset      = new Vector3(0f, 22f, -22f);
        follow.SmoothSpeed = 0.15f;
        // Target은 CameraFollow.Start()에서 "Player1" 태그로 자동 할당

        // ── 방향광 ───────────────────────────────────────────────────────
        var lightObj = new GameObject("Directional Light");
        var dLight   = lightObj.AddComponent<Light>();
        dLight.type      = LightType.Directional;
        dLight.intensity = 1.2f;
        dLight.color     = new Color(1f, 0.95f, 0.88f);
        lightObj.transform.eulerAngles = new Vector3(50f, -15f, 0f); // 30° 뷰 - 위에서 앞쪽으로 비추는 느낌

        // ── EventSystem ───────────────────────────────────────────────────
        var esObj = new GameObject("EventSystem");
        esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esObj.AddComponent<InputSystemUIInputModule>();

        // ── 매니저들 ──────────────────────────────────────────────────────
        var managers = new GameObject("_Managers");
        var gm = managers.AddComponent<GameManager>();
        var nm = managers.AddComponent<NetworkManager>();
        var autoStart = managers.AddComponent<DebugAutoStart>();
        autoStart.AutoStartOnPlay = false; // 멀티플레이 씬: 네트워크 GAME_START 신호로만 시작

        // ── 아레나 ────────────────────────────────────────────────────────
        var arena = new GameObject("Arena");

        TileGround(arena.transform);
        TileWalls(arena.transform);

        // ── BulletPrefab (3D 구체) ────────────────────────────────────────
        GameObject bulletPrefab = GetOrCreateBulletPrefab3D();

        // ── Player1 (로컬) ────────────────────────────────────────────────
        var p1 = CreatePlayer3D("Player1", new Vector3(-3f, BlockSize-1, 0f), "Player1", true,  bulletPrefab);

        // ── Player2 (원격) ────────────────────────────────────────────────
        var p2 = CreatePlayer3D("Player2", new Vector3( 3f, BlockSize-1, 0f), "Player2", false, bulletPrefab);

        // ── GameManager 참조 연결 ─────────────────────────────────────────
        gm.LocalPlayer  = p1.GetComponent<PlayerHealth>();
        gm.RemotePlayer = p2.GetComponent<PlayerHealth>();

        // ── NetworkManager 참조 연결 ──────────────────────────────────────
        nm.RemotePlayerController = p2.GetComponent<PlayerController>();
        nm.RemoteBulletShooter    = p2.GetComponent<BulletShooter>();
        nm.RemotePlayerHealth     = p2.GetComponent<PlayerHealth>();

        // ── UI ────────────────────────────────────────────────────────────
        var uiData = CreateGameUI();
        var gameUI = uiData.canvas.GetComponent<GameUI>();
        if (gameUI != null)
        {
            gameUI.Player1HPBar  = uiData.p1Bar;
            gameUI.Player1HPText = uiData.p1Text;
            gameUI.Player2HPBar  = uiData.p2Bar;
            gameUI.Player2HPText = uiData.p2Text;
            gameUI.DashBar       = uiData.dashBar;
            gameUI.SlotBorders   = uiData.slotBorders;
            gameUI.Slot4Dim      = uiData.slot4Dim;
            gameUI.Slot4Label    = uiData.slot4Label;
            gameUI.Slot4Ammo     = uiData.slot4Ammo;
            gameUI.GameOverPanel = uiData.gameOverPanel;
            gameUI.ResultText    = uiData.resultText;
            gameUI.RestartButton = uiData.restartButton;
        }

        // ── 무기 픽업 아이템 ──────────────────────────────────────────────
        var items = new GameObject("Items");
        CreateWeaponPickup(items.transform, new Vector3(-8f, BlockSize - 1f,  0f),
            SpecialWeaponType.EnhancedSpiral, 30,
            "Assets/graphic/item/shotgun.fbx",    modelScale: 1f);
        CreateWeaponPickup(items.transform, new Vector3( 0f, BlockSize - 1f,  8f),
            SpecialWeaponType.Crusher, 25,
            "Assets/graphic/item/ammo_crate.fbx", modelScale: 1f);
        CreateWeaponPickup(items.transform, new Vector3( 8f, BlockSize - 1f + 0.6f,  0f),
            SpecialWeaponType.Homing, 15,
            "Assets/graphic/item/wand.fbx",       modelScale: 150f);

        // ── 대기방 오버레이 ───────────────────────────────────────────────
        var waitingUI = AddWaitingRoomPanel(uiData.canvas);
        nm.WaitingUI = waitingUI;

        // ── 씬 저장 ───────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, ScenePath + "GameScene.unity");
        Debug.Log("[SceneBuilder] GameScene 생성 완료 (3D 탑뷰)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TestScene  (네트워크 없이 단독 실행 / 무기 테스트용)
    // ═══════════════════════════════════════════════════════════════════════

    const float TestArenaHalf = 32f; // 64×64 (GameScene 40×40 보다 넓음)

    static void BuildTestScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 카메라 ────────────────────────────────────────────────────────
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 9f; // 넓은 맵에 맞게 시야 확장
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.45f, 0.72f, 0.95f);
        cam.nearClipPlane    = 0.1f;
        cam.farClipPlane     = 100f;
        camObj.transform.position    = new Vector3(0f, 22f, -22f);
        camObj.transform.eulerAngles = new Vector3(45f, 0f, 0f);
        camObj.AddComponent<AudioListener>();

        var follow = camObj.AddComponent<CameraFollow>();
        follow.Offset      = new Vector3(0f, 22f, -22f);
        follow.SmoothSpeed = 0.15f;

        // ── 방향광 ───────────────────────────────────────────────────────
        var lightObj = new GameObject("Directional Light");
        var dLight   = lightObj.AddComponent<Light>();
        dLight.type      = LightType.Directional;
        dLight.intensity = 1.2f;
        dLight.color     = new Color(1f, 0.95f, 0.88f);
        lightObj.transform.eulerAngles = new Vector3(50f, -15f, 0f);

        // ── EventSystem ───────────────────────────────────────────────────
        var esObj = new GameObject("EventSystem");
        esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esObj.AddComponent<InputSystemUIInputModule>();

        // ── 매니저 (NetworkManager 없음) ─────────────────────────────────
        var managers  = new GameObject("_Managers");
        var gm        = managers.AddComponent<GameManager>();
        var autoStart = managers.AddComponent<DebugAutoStart>();
        autoStart.AutoStartOnPlay = true;

        // ── 아레나 (넓은 버전, 벽은 장식만) ─────────────────────────────
        var arena = new GameObject("Arena");
        TileGround(arena.transform, TestArenaHalf);
        TileWalls(arena.transform,  TestArenaHalf);

        // ── BulletPrefab ──────────────────────────────────────────────────
        var bulletPrefab = GetOrCreateBulletPrefab3D();

        // ── Player1 (로컬) ────────────────────────────────────────────────
        var p1 = CreatePlayer3D("Player1", new Vector3(-5f, BlockSize - 1f, 0f),
                                "Player1", true, bulletPrefab);

        // ── Player2 (로컬 2P – IJKL + 우클릭, 머티리얼은 P2) ─────────────
        var p2 = CreatePlayer3D("Player2", new Vector3(5f, BlockSize - 1f, 0f),
                                "Player2", true, bulletPrefab, playerIndex: 2);
        var p2ctrl    = p2.GetComponent<PlayerController>();
        var p2shooter = p2.GetComponent<BulletShooter>();
        if (p2ctrl    != null) { p2ctrl.IsLocalPlayer  = true; p2ctrl.PlayerIndex   = 2; }
        if (p2shooter != null) { p2shooter.IsLocalPlayer = true; p2shooter.PlayerIndex = 2; }

        // ── GameManager 참조 ──────────────────────────────────────────────
        gm.LocalPlayer  = p1.GetComponent<PlayerHealth>();
        gm.RemotePlayer = p2.GetComponent<PlayerHealth>();

        // ── TestHelper ────────────────────────────────────────────────────
        var helperObj = new GameObject("TestHelper");
        var helper    = helperObj.AddComponent<TestHelper>();
        helper.LocalShooter = p1.GetComponent<BulletShooter>();
        helper.LocalHealth  = p1.GetComponent<PlayerHealth>();
        helper.RemoteHealth = p2.GetComponent<PlayerHealth>();

        // ── UI (GameScene과 동일 + 힌트 텍스트) ──────────────────────────
        var uiData = CreateGameUI();
        var gameUI = uiData.canvas.GetComponent<GameUI>();
        if (gameUI != null)
        {
            gameUI.Player1HPBar  = uiData.p1Bar;
            gameUI.Player1HPText = uiData.p1Text;
            gameUI.Player2HPBar  = uiData.p2Bar;
            gameUI.Player2HPText = uiData.p2Text;
            gameUI.DashBar       = uiData.dashBar;
            gameUI.SlotBorders   = uiData.slotBorders;
            gameUI.Slot4Dim      = uiData.slot4Dim;
            gameUI.Slot4Label    = uiData.slot4Label;
            gameUI.Slot4Ammo     = uiData.slot4Ammo;
            gameUI.GameOverPanel = uiData.gameOverPanel;
            gameUI.ResultText    = uiData.resultText;
            gameUI.RestartButton = uiData.restartButton;
        }

        // 힌트 텍스트 (우하단)
        var hintText = CreateTMPText("Hint_Text", uiData.canvas.transform,
            "", 13, FontStyles.Normal, new Color(0.7f, 1f, 0.7f),
            new Vector2(760f, -430f), new Vector2(300f, 160f));
        hintText.alignment = TextAlignmentOptions.TopRight;
        helper.HintText    = hintText;

        // ── 무기 픽업 아이템 (TestScene – 무한 재생성) ───────────────────
        const float respawn = 5f; // 5초 후 재생성
        var items = new GameObject("Items");
        SetPickupRespawn(CreateWeaponPickup(items.transform, new Vector3(-10f, BlockSize - 1f,  5f),
            SpecialWeaponType.EnhancedSpiral, 30,
            "Assets/graphic/item/shotgun.fbx",    modelScale: 1f), respawn);
        SetPickupRespawn(CreateWeaponPickup(items.transform, new Vector3(  0f, BlockSize - 1f,  8f),
            SpecialWeaponType.Crusher, 25,
            "Assets/graphic/item/ammo_crate.fbx", modelScale: 1f), respawn);
        SetPickupRespawn(CreateWeaponPickup(items.transform, new Vector3( 10f, BlockSize - 1f + 0.6f,  5f),
            SpecialWeaponType.Homing, 15,
            "Assets/graphic/item/wand.fbx",       modelScale: 150f), respawn);

        // ── 씬 저장 ───────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, ScenePath + "TestScene.unity");
        Debug.Log("[SceneBuilder] TestScene 생성 완료");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MainMenu Scene
    // ═══════════════════════════════════════════════════════════════════════

    static void BuildMainMenuScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 5f;
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.05f, 0.05f, 0.1f);
        camObj.transform.position = new Vector3(0, 0, -10);
        camObj.AddComponent<AudioListener>();

        // ApiClient (DontDestroyOnLoad)
        var apiObj = new GameObject("ApiClient");
        apiObj.AddComponent<ApiClient>();

        // Canvas
        var canvasObj = new GameObject("Canvas");
        var canvas    = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();
        var menuUI = canvasObj.AddComponent<MainMenuUI>();

        // EventSystem
        var esObj = new GameObject("EventSystem");
        esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esObj.AddComponent<InputSystemUIInputModule>();

        // 타이틀 (패널 밖, 항상 표시)
        CreateTMPText("Title", canvasObj.transform,
            "BULLET HELL PvP", 78, FontStyles.Bold, Color.white,
            new Vector2(0f, 330f), new Vector2(900f, 120f));

        // ── Login Panel ──────────────────────────────────────────────────
        var loginPanel = MakeFullPanel("LoginPanel", canvasObj.transform);

        CreateTMPText("ID_Label", loginPanel.transform,
            "아이디", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(0f, 120f), new Vector2(480f, 45f));

        var idField = CreateInputField("PlayerID_Field", loginPanel.transform,
            "", new Vector2(0f, 60f), new Vector2(480f, 68f), fontSize: 30);

        CreateTMPText("PW_Label", loginPanel.transform,
            "비밀번호", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(0f, -30f), new Vector2(480f, 45f));

        var pwField = CreateInputField("Password_Field", loginPanel.transform,
            "", new Vector2(0f, -90f), new Vector2(480f, 68f), fontSize: 30);
        pwField.contentType = TMP_InputField.ContentType.Password;

        var loginBtn = CreateButton("Login_Button", loginPanel.transform,
            "로그인", new Vector2(0f, -210f), new Vector2(330f, 83f),
            new Color(0.2f, 0.6f, 1f), fontSize: 33);

        var toRegisterBtn = CreateButton("ToRegister_Button", loginPanel.transform,
            "회원가입", new Vector2(0f, -315f), new Vector2(330f, 68f),
            new Color(0.25f, 0.25f, 0.35f), fontSize: 33);

        // ── Register Panel (2×2 그리드) ──────────────────────────────────
        var registerPanel = MakeFullPanel("RegisterPanel", canvasObj.transform);

        const float colL = -250f, colR = 250f, fieldW = 440f;

        // Row 1: 아이디 | 이메일
        CreateTMPText("ID_Label", registerPanel.transform,
            "아이디", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(colL, 80f), new Vector2(fieldW, 45f));
        var regIdField = CreateInputField("RegPlayerID_Field", registerPanel.transform,
            "", new Vector2(colL, 20f), new Vector2(fieldW, 68f), fontSize: 30);

        CreateTMPText("Email_Label", registerPanel.transform,
            "이메일", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(colR, 80f), new Vector2(fieldW, 45f));
        var regEmailField = CreateInputField("RegEmail_Field", registerPanel.transform,
            "", new Vector2(colR, 20f), new Vector2(fieldW, 68f), fontSize: 30);
        regEmailField.contentType = TMP_InputField.ContentType.EmailAddress;

        // Row 2: 비밀번호 | 비밀번호 확인
        CreateTMPText("PW_Label", registerPanel.transform,
            "비밀번호", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(colL, -80f), new Vector2(fieldW, 45f));
        var regPwField = CreateInputField("RegPassword_Field", registerPanel.transform,
            "", new Vector2(colL, -140f), new Vector2(fieldW, 68f), fontSize: 30);
        regPwField.contentType = TMP_InputField.ContentType.Password;

        CreateTMPText("Confirm_Label", registerPanel.transform,
            "비밀번호 확인", 30, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(colR, -80f), new Vector2(fieldW, 45f));
        var regConfirmField = CreateInputField("RegConfirm_Field", registerPanel.transform,
            "", new Vector2(colR, -140f), new Vector2(fieldW, 68f), fontSize: 30);
        regConfirmField.contentType = TMP_InputField.ContentType.Password;

        // 버튼
        var registerBtn = CreateButton("Register_Button", registerPanel.transform,
            "가입하기", new Vector2(0f, -250f), new Vector2(330f, 83f),
            new Color(0.2f, 0.75f, 0.4f), fontSize: 33);

        var toLoginBtn = CreateButton("ToLogin_Button", registerPanel.transform,
            "로그인으로", new Vector2(0f, -345f), new Vector2(330f, 68f),
            new Color(0.25f, 0.25f, 0.35f), fontSize: 33);

        registerPanel.SetActive(false);

        // Status 텍스트: 패널보다 나중에 생성해야 위에 렌더링됨
        var statusText = CreateTMPText("Status_Text", canvasObj.transform,
            "", 27, FontStyles.Normal, Color.white,
            new Vector2(0f, -465f), new Vector2(780f, 53f));

        // ── MainMenuUI 참조 연결 ────────────────────────────────────────
        menuUI.LoginPanel      = loginPanel;
        menuUI.PlayerIDField   = idField;
        menuUI.PasswordField   = pwField;
        menuUI.LoginButton     = loginBtn;
        menuUI.ToRegisterButton = toRegisterBtn;

        menuUI.RegisterPanel    = registerPanel;
        menuUI.RegPlayerIDField = regIdField;
        menuUI.RegEmailField    = regEmailField;
        menuUI.RegPasswordField = regPwField;
        menuUI.RegConfirmField  = regConfirmField;
        menuUI.RegisterButton   = registerBtn;
        menuUI.ToLoginButton    = toLoginBtn;

        menuUI.StatusText = statusText;

        EditorSceneManager.SaveScene(scene, ScenePath + "MainMenu.unity");
        Debug.Log("[SceneBuilder] MainMenu 생성 완료");
    }

    // 전체 Canvas를 덮는 투명 패널 (하위 UI 그룹핑용)
    static GameObject MakeFullPanel(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    // 게임 씬 대기방 오버레이 패널 생성
    static WaitingRoomUI AddWaitingRoomPanel(GameObject canvas)
    {
        // 전체화면 반투명 배경
        var panel = CreatePanel("WaitingRoom_Panel", canvas.transform,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.88f));
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;

        var waitingUI = panel.AddComponent<WaitingRoomUI>();
        waitingUI.Panel    = panel;
        waitingUI.ChatFont = Font;

        // ── 타이틀 ────────────────────────────────────────────────────────
        CreateTMPText("Title", panel.transform,
            "대  기  방", 52, FontStyles.Bold, Color.white,
            new Vector2(0f, 470f), new Vector2(700f, 80f));

        // ── 플레이어 목록 (좌측) ──────────────────────────────────────────
        // P1 슬롯
        var p1Slot = CreatePanel("P1_Slot", panel.transform,
            new Vector2(-420f, 270f), new Vector2(480f, 80f), new Color(0.1f, 0.1f, 0.2f, 0.9f));
        waitingUI.P1NameText  = CreateTMPText("P1Name",  p1Slot.transform,
            "대기 중...", 26, FontStyles.Bold, Color.white,
            new Vector2(-80f, 0f), new Vector2(280f, 50f));
        waitingUI.P1ReadyText = CreateTMPText("P1Ready", p1Slot.transform,
            "대기 중", 22, FontStyles.Normal, new Color(0.5f, 0.5f, 0.5f),
            new Vector2(160f, 0f), new Vector2(140f, 50f));

        // P2 슬롯
        var p2Slot = CreatePanel("P2_Slot", panel.transform,
            new Vector2(-420f, 175f), new Vector2(480f, 80f), new Color(0.1f, 0.1f, 0.2f, 0.9f));
        waitingUI.P2NameText  = CreateTMPText("P2Name",  p2Slot.transform,
            "대기 중...", 26, FontStyles.Bold, Color.white,
            new Vector2(-80f, 0f), new Vector2(280f, 50f));
        waitingUI.P2ReadyText = CreateTMPText("P2Ready", p2Slot.transform,
            "대기 중", 22, FontStyles.Normal, new Color(0.5f, 0.5f, 0.5f),
            new Vector2(160f, 0f), new Vector2(140f, 50f));

        // 카운트다운
        waitingUI.CountdownText = CreateTMPText("Countdown", panel.transform,
            "", 120, FontStyles.Bold, Color.yellow,
            new Vector2(-420f, 0f), new Vector2(480f, 160f));

        // Ready 버튼
        var readyBtn = CreateButton("Ready_Button", panel.transform,
            "준  비", new Vector2(-420f, -160f), new Vector2(220f, 75f),
            new Color(0.30f, 0.30f, 0.48f), fontSize: 28);
        waitingUI.ReadyButton      = readyBtn;
        waitingUI.ReadyButtonLabel = readyBtn.GetComponentInChildren<TextMeshProUGUI>();

        // Start 버튼 (방장 전용)
        waitingUI.StartButton = CreateButton("Start_Button", panel.transform,
            "게임 시작", new Vector2(-420f, -255f), new Vector2(220f, 65f),
            new Color(0.15f, 0.55f, 0.25f), fontSize: 26);

        // ── 채팅 (우측) ───────────────────────────────────────────────────
        // 채팅 스크롤 뷰
        var chatScrollGo = new GameObject("ChatScrollView");
        chatScrollGo.transform.SetParent(panel.transform, false);
        var chatScrollRt = chatScrollGo.AddComponent<RectTransform>();
        chatScrollRt.anchoredPosition = new Vector2(280f, 80f);
        chatScrollRt.sizeDelta        = new Vector2(700f, 600f);

        var chatScroll = chatScrollGo.AddComponent<ScrollRect>();
        chatScroll.horizontal = false;
        waitingUI.ChatScrollRect = chatScroll;

        // Viewport
        var chatVP   = new GameObject("Viewport");
        chatVP.transform.SetParent(chatScrollGo.transform, false);
        var chatVPRt = chatVP.AddComponent<RectTransform>();
        chatVPRt.anchorMin = Vector2.zero;
        chatVPRt.anchorMax = Vector2.one;
        chatVPRt.offsetMin = chatVPRt.offsetMax = Vector2.zero;
        chatVP.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.12f, 0.95f);
        chatVP.AddComponent<Mask>().showMaskGraphic = true;
        chatScroll.viewport = chatVPRt;

        // Content (채팅 메시지가 쌓일 영역)
        var chatContent   = new GameObject("ChatContent");
        chatContent.transform.SetParent(chatVP.transform, false);
        var chatContentRt = chatContent.AddComponent<RectTransform>();
        chatContentRt.anchorMin = new Vector2(0f, 1f);
        chatContentRt.anchorMax = new Vector2(1f, 1f);
        chatContentRt.pivot     = new Vector2(0.5f, 1f);
        chatContentRt.offsetMin = chatContentRt.offsetMax = Vector2.zero;
        var vlg = chatContent.AddComponent<VerticalLayoutGroup>();
        vlg.spacing           = 4f;
        vlg.padding           = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var csf = chatContent.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        chatScroll.content         = chatContentRt;
        waitingUI.ChatContent      = chatContentRt;

        // 채팅 입력 + 전송
        var chatInput = CreateInputField("ChatInput", panel.transform,
            "채팅 입력...", new Vector2(240f, -270f), new Vector2(590f, 65f), fontSize: 22);
        waitingUI.ChatInput = chatInput;

        waitingUI.SendButton = CreateButton("Send_Button", panel.transform,
            "전송", new Vector2(575f, -270f), new Vector2(120f, 65f),
            new Color(0.2f, 0.5f, 0.8f), fontSize: 22);

        return waitingUI;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  헬퍼: 게임 오브젝트 생성
    // ═══════════════════════════════════════════════════════════════════════

    // ── 3D 플레이어 생성 ─────────────────────────────────────────────────

    static GameObject CreatePlayer3D(string name, Vector3 pos, string tag,
                                     bool isLocal, GameObject bulletPrefab,
                                     int playerIndex = 0) // 0 = isLocal로 자동 결정
    {
        if (playerIndex == 0) playerIndex = isLocal ? 1 : 2;
        var go = new GameObject(name);
        go.tag = tag;
        go.transform.position = pos;

        // 3D 물리
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity  = false;
        rb.constraints = RigidbodyConstraints.FreezePositionY
                       | RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;

        // 1블록(2×2×2) 크기 기준 캡슐 콜라이더
        var col = go.AddComponent<CapsuleCollider>();
        col.height = 2f;
        col.radius = 0.7f;
        col.center = new Vector3(0f, 1f, 0f);

        // 스크립트
        var ctrl   = go.AddComponent<PlayerController>();
        ctrl.IsLocalPlayer = isLocal;

        go.AddComponent<PlayerHealth>();

        var shooter = go.AddComponent<BulletShooter>();
        shooter.IsLocalPlayer = isLocal;
        shooter.OwnerTag      = tag;
        shooter.BulletPrefab  = bulletPrefab;

        // Mage 3D 모델 (머티리얼은 playerIndex 기준)
        bool isP1  = playerIndex == 1;
        Color tint = isP1 ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.4f, 0.4f);
        var mageFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/graphic/character/Mage.fbx");
        if (mageFbx != null)
        {
            var model = Object.Instantiate(mageFbx);
            model.name = "Model";
            model.transform.SetParent(go.transform);
            model.transform.localPosition = new Vector3(0f, 0f, 0f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale    = Vector3.one;

            string matName = isP1 ? "MageMat_P1" : "MageMat_P2";
            string texPath = isP1
                ? "Assets/graphic/character/player1/mage_texture.png"
                : "Assets/graphic/character/player2/mage_texture.png";
            var mat = GetOrCreateMaterial(matName, texPath, Color.white);
            if (isP1)
                mat.SetColor("_BaseColor", tint);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = mat;

            // 콜라이더 중복 방지
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);

            // ── Animator Controller 자동 연결 ────────────────────────────
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/animation/controller/player.controller");
            if (controller != null)
                animator.runtimeAnimatorController = controller;
            else
                Debug.LogWarning("[SceneBuilder] player.controller 없음 — 경로 확인: Assets/animation/controller/player.controller");

            // ── 스태프 장착 (handslot.r 본에 부착) ──────────────────────
            AttachStaff(model.transform);
        }
        else
        {
            // Mage.fbx 없으면 캡슐로 대체
            var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vis.name = "Model";
            vis.transform.SetParent(go.transform);
            vis.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            vis.transform.localScale    = Vector3.one * 0.8f;
            var mat = GetOrCreateMaterial("PlayerMat_" + tag, "", tint);
            vis.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(vis.GetComponent<CapsuleCollider>());
        }

        // FirePoint (총구 방향 앞쪽)
        var fp = new GameObject("FirePoint");
        fp.transform.SetParent(go.transform);
        fp.transform.localPosition = new Vector3(0f, 1f, 1.1f);
        shooter.FirePoint = fp.transform;

        return go;
    }

    // ── 스태프 장착 ─────────────────────────────────────────────────────────

    static void AttachStaff(Transform modelRoot)
    {
        var staffFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/graphic/character/spellbook_open.fbx");
        if (staffFbx == null) { Debug.LogWarning("[SceneBuilder] spellbook_open.fbx 없음"); return; }

        // handslot.r 본 탐색 (없으면 hand.r → 모델 루트 순으로 폴백)
        Transform slot = FindBone(modelRoot, "handslot.r");
        if (slot == null) slot = FindBone(modelRoot, "hand.r");
        if (slot == null) slot = FindBone(modelRoot, "Hand_R");
        if (slot == null) slot = modelRoot;

        var staff = Object.Instantiate(staffFbx);
        staff.name = "Staff";
        staff.transform.SetParent(slot, worldPositionStays: false);
        // 오른손 기준: 약간 앞으로 오프셋, 수직으로 세움
        staff.transform.localPosition = new Vector3(-0.2f, 0.5f, 0.05f);
        staff.transform.localRotation = Quaternion.Euler(-45f, 90f, 0f);
        staff.transform.localScale    = Vector3.one * 100f;

        foreach (var c in staff.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);
    }

    /// 이름으로 Transform 재귀 탐색 (대소문자 무시)
    static Transform FindBone(Transform root, string boneName)
    {
        if (root.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (Transform child in root)
        {
            var found = FindBone(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    // ── 바닥 타일링 (grass.fbx, BlockSize 간격) ───────────────────────────

    static void TileGround(Transform parent, float arenaHalf = ArenaHalf)
    {
        var root = new GameObject("Ground");
        root.transform.SetParent(parent);

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/graphic/block/grass.fbx");
        var mat = GetOrCreateMaterial("GrassMat",
            "Assets/graphic/block/block_bits_texture.png", new Color(0.28f, 0.55f, 0.2f));

        int tiles = Mathf.RoundToInt(arenaHalf * 2f / BlockSize);
        for (int xi = 0; xi < tiles; xi++)
        for (int zi = 0; zi < tiles; zi++)
        {
            float x = -arenaHalf + BlockSize * 0.5f + xi * BlockSize;
            float z = -arenaHalf + BlockSize * 0.5f + zi * BlockSize;
            PlaceBlock(fbx, mat, root.transform, new Vector3(x, 0f, z), $"Grass_{xi}_{zi}");
        }
    }

    // ── 벽 타일링 (stone_dark.fbx, BlockSize 간격) ────────────────────────

    static void TileWalls(Transform parent, float arenaHalf = ArenaHalf)
    {
        var root = new GameObject("Walls");
        root.transform.SetParent(parent);

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/graphic/block/stone_dark.fbx");
        var mat = GetOrCreateMaterial("WallMat",
            "Assets/graphic/block/block_bits_texture.png", new Color(0.42f, 0.42f, 0.52f));

        int tiles = Mathf.RoundToInt(arenaHalf * 2f / BlockSize);

        // 상단 / 하단 (코너 포함) — 1단(Y=0) + 2단(Y=BlockSize) 으로 쌓기
        for (int i = 0; i <= tiles + 1; i++)
        {
            float x = -arenaHalf - BlockSize * 0.5f + i * BlockSize;
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(x, 0f,         arenaHalf + BlockSize * 0.5f - 0.2f));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(x, 0f,        -arenaHalf - BlockSize * 0.5f));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(x, BlockSize,  arenaHalf + BlockSize * 0.5f - 0.2f));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(x, BlockSize, -arenaHalf - BlockSize * 0.5f));
        }
        // 좌측 / 우측 (코너 제외)
        for (int i = 0; i < tiles; i++)
        {
            float z = -arenaHalf + BlockSize * 0.5f + i * BlockSize;
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(-arenaHalf - BlockSize * 0.5f, 0f,        z));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3( arenaHalf + BlockSize * 0.5f, 0f,        z));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3(-arenaHalf - BlockSize * 0.5f, BlockSize, z));
            PlaceWallBlock(fbx, mat, root.transform, new Vector3( arenaHalf + BlockSize * 0.5f, BlockSize, z));
        }
    }

    // ── 블록 하나 배치 (시각 전용, 콜라이더 없음) ─────────────────────────

    static void PlaceBlock(GameObject fbx, Material mat, Transform parent,
                           Vector3 pos, string name = "Block")
    {
        GameObject go;
        if (fbx != null)
        {
            go = Object.Instantiate(fbx);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position      = pos;
            go.transform.localRotation = Quaternion.identity;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
                mr.sharedMaterial = mat;
            foreach (var c in go.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position      = pos;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        }
    }

    // ── 벽 블록 하나 배치 (BoxCollider + Tag:Wall) ────────────────────────

    static void PlaceWallBlock(GameObject fbx, Material mat, Transform parent, Vector3 pos)
    {
        var go  = new GameObject("Wall");
        go.tag  = "Wall";
        go.transform.SetParent(parent);
        go.transform.position = pos;

        var bc  = go.AddComponent<BoxCollider>();
        bc.size = Vector3.one * BlockSize; // 2×2×2

        if (fbx != null)
        {
            var vis = Object.Instantiate(fbx);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale    = Vector3.one;
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>())
                mr.sharedMaterial = mat;
            foreach (var c in vis.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }
        else
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale    = Vector3.one;
            vis.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(vis.GetComponent<BoxCollider>());
        }
    }

    // ── 무기 픽업 아이템 생성 ──────────────────────────────────────────────

    static readonly Texture2D EKeyTex =
        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/graphic/UI/e.png");

    static GameObject CreateWeaponPickup(Transform parent, Vector3 pos,
                                         SpecialWeaponType weaponType, int ammo,
                                         string fbxPath, float modelScale = 1f)
    {
        string label = weaponType switch
        {
            SpecialWeaponType.EnhancedSpiral => "Pickup_Spiral",
            SpecialWeaponType.Crusher        => "Pickup_Crusher",
            SpecialWeaponType.Homing         => "Pickup_Homing",
            _                                => "Pickup"
        };

        var go = new GameObject(label);
        go.transform.SetParent(parent);
        go.transform.position = pos;

        // ── 3D 모델 ────────────────────────────────────────────────────────
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx != null)
        {
            var model = Object.Instantiate(fbx);
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale    = Vector3.one * modelScale;
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }
        else
        {
            // 폴백: 컬러 큐브
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Model";
            cube.transform.SetParent(go.transform, false);
            cube.transform.localScale = Vector3.one * 0.4f;
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        }

        // ── 트리거 콜라이더 ────────────────────────────────────────────────
        var sphere      = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius    = 1.5f;

        // ── WeaponPickup 컴포넌트 ──────────────────────────────────────────
        var pickup = go.AddComponent<WeaponPickup>();
        pickup.WeaponType          = weaponType;
        pickup.Ammo                = ammo;
        pickup.PickupRadius        = 2.5f;
        pickup.InteractIconTexture = EKeyTex;

        return go;
    }

    // respawnDelay > 0 이면 무한 재생성
    static void SetPickupRespawn(GameObject pickup, float delay)
    {
        var p = pickup.GetComponent<WeaponPickup>();
        if (p != null) p.RespawnDelay = delay;
    }

    static GameObject CreateSpriteObj(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.localPosition = Vector3.zero;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color  = color;
        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  헬퍼: UI 생성
    // ═══════════════════════════════════════════════════════════════════════

    struct GameUIData
    {
        public GameObject canvas;
        public Slider     p1Bar, p2Bar, dashBar;
        public TMP_Text   p1Text, p2Text, resultText;
        public GameObject gameOverPanel;
        public Button     restartButton;
        // 퀵슬롯
        public Image[]   slotBorders;  // [0]~[3]
        public Image     slot4Dim;
        public TMP_Text  slot4Label;
        public TMP_Text  slot4Ammo;
    }

    static GameUIData CreateGameUI()
    {
        var data = new GameUIData();

        // Canvas
        var canvasObj = new GameObject("Canvas");
        data.canvas   = canvasObj;
        var canvas    = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasObj.AddComponent<GameUI>();

        // ── Player1 HP (좌상단) ───────────────────────────────────────────
        var p1Panel = CreatePanel("Player1_HP", canvasObj.transform,
            new Vector2(-760f, 500f), new Vector2(340f, 75f), new Color(0, 0, 0, 0.55f));

        // 이름 라벨 - 패널 상단 왼쪽
        CreateTMPText("Name", p1Panel.transform, "P1", 18, FontStyles.Bold,
            new Color(0.2f, 0.8f, 1f), new Vector2(-120f, 20f), new Vector2(60f, 28f));

        // HP 숫자 - 이름 옆
        data.p1Text = CreateTMPText("HP_Text", p1Panel.transform, "100 / 100",
            15, FontStyles.Normal, Color.white, new Vector2(30f, 20f), new Vector2(160f, 28f));

        // HP 바 - 패널 하단 전체 폭
        data.p1Bar = CreateSlider("HP_Bar", p1Panel.transform,
            new Vector2(0f, -15f), new Vector2(310f, 18f), new Color(0.2f, 0.8f, 0.3f));

        // ── 대시 쿨다운 (P1 패널 바로 아래) ─────────────────────────────
        var dashPanel = CreatePanel("Dash_Panel", canvasObj.transform,
            new Vector2(-760f, 428f), new Vector2(340f, 38f), new Color(0, 0, 0, 0.45f));

        CreateTMPText("Dash_Label", dashPanel.transform, "DASH", 13, FontStyles.Bold,
            new Color(0.9f, 0.7f, 0.2f), new Vector2(-130f, 0f), new Vector2(60f, 24f));

        data.dashBar = CreateSlider("Dash_Bar", dashPanel.transform,
            new Vector2(20f, 0f), new Vector2(230f, 16f), new Color(0.9f, 0.7f, 0.2f));

        // ── Player2 HP (우상단) ───────────────────────────────────────────
        var p2Panel = CreatePanel("Player2_HP", canvasObj.transform,
            new Vector2(760f, 500f), new Vector2(340f, 75f), new Color(0, 0, 0, 0.55f));

        // 이름 라벨 - 패널 상단 오른쪽
        CreateTMPText("Name", p2Panel.transform, "P2", 18, FontStyles.Bold,
            new Color(1f, 0.3f, 0.3f), new Vector2(120f, 20f), new Vector2(60f, 28f));

        // HP 숫자 - 이름 옆
        data.p2Text = CreateTMPText("HP_Text", p2Panel.transform, "100 / 100",
            15, FontStyles.Normal, Color.white, new Vector2(-30f, 20f), new Vector2(160f, 28f));

        // HP 바 - 패널 하단 전체 폭
        data.p2Bar = CreateSlider("HP_Bar", p2Panel.transform,
            new Vector2(0f, -15f), new Vector2(310f, 18f), new Color(1f, 0.3f, 0.3f));

        // ── 퀵슬롯 (하단 중앙, Roblox 스타일) ───────────────────────────
        var qs = CreateQuickslot(canvasObj.transform);
        data.slotBorders = qs.borders;
        data.slot4Dim    = qs.dim4;
        data.slot4Label  = qs.label4;
        data.slot4Ammo   = qs.ammo4;

        // ── Game Over 패널 ────────────────────────────────────────────────
        data.gameOverPanel = CreatePanel("GameOver_Panel", canvasObj.transform,
            Vector2.zero, new Vector2(500f, 300f), new Color(0, 0, 0, 0.85f));
        data.gameOverPanel.SetActive(false);

        data.resultText = CreateTMPText("Result_Text", data.gameOverPanel.transform,
            "YOU WIN!", 60, FontStyles.Bold, Color.yellow,
            new Vector2(0f, 60f), new Vector2(460f, 100f));

        data.restartButton = CreateButton("Restart_Button", data.gameOverPanel.transform,
            "Restart", new Vector2(0f, -60f), new Vector2(180f, 55f), new Color(0.2f, 0.6f, 1f));

        return data;
    }

    // ─── 퀵슬롯 생성 ──────────────────────────────────────────────────────

    struct QuickslotResult
    {
        public Image[]  borders;
        public Image    dim4;
        public TMP_Text label4;
        public TMP_Text ammo4;
    }

    static QuickslotResult CreateQuickslot(Transform canvasParent)
    {
        const float SlotSize  = 72f;
        const float Gap       = 6f;
        const float TotalW    = SlotSize * 4 + Gap * 3; // 306

        // 슬롯별 설정
        string[] keyPaths = {
            "Assets/graphic/UI/1.png",
            "Assets/graphic/UI/2.png",
            "Assets/graphic/UI/3.png",
            "Assets/graphic/UI/4.png",
        };
        string[] labelTexts = { "직선", "점사", "8방향", "---" };
        Color[]  accents = {
            new Color(0.30f, 0.60f, 1.00f, 0.35f), // 파랑  – 직선
            new Color(0.20f, 0.85f, 0.40f, 0.35f), // 초록  – 점사
            new Color(1.00f, 0.55f, 0.15f, 0.35f), // 주황  – 스파이럴
            new Color(0.75f, 0.25f, 1.00f, 0.35f), // 보라  – 특수
        };

        // 전체 바 (반투명 검정 배경)
        var bar = new GameObject("Quickslot_Bar");
        bar.transform.SetParent(canvasParent, false);
        var barRt = bar.AddComponent<RectTransform>();
        barRt.anchorMin        = new Vector2(0.5f, 0f);
        barRt.anchorMax        = new Vector2(0.5f, 0f);
        barRt.pivot            = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = new Vector2(0f, 18f);
        barRt.sizeDelta        = new Vector2(TotalW + 20f, SlotSize + 20f);
        var barImg = bar.AddComponent<Image>();
        barImg.color = new Color(0f, 0f, 0f, 0.45f);

        var result   = new QuickslotResult();
        result.borders = new Image[4];

        for (int i = 0; i < 4; i++)
        {
            float xOff = -TotalW * 0.5f + SlotSize * 0.5f + i * (SlotSize + Gap);

            // ── 테두리 (선택 강조용) ──────────────────────────────────────
            var border = new GameObject($"Slot{i + 1}");
            border.transform.SetParent(bar.transform, false);
            var borderRt = border.AddComponent<RectTransform>();
            borderRt.anchoredPosition = new Vector2(xOff, 0f);
            borderRt.sizeDelta        = new Vector2(SlotSize, SlotSize);
            var borderImg = border.AddComponent<Image>();
            borderImg.color   = new Color(0.40f, 0.40f, 0.40f, 0.60f);
            result.borders[i] = borderImg;

            // ── 내부 배경 ─────────────────────────────────────────────────
            var bg = new GameObject("Bg");
            bg.transform.SetParent(border.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(3f, 3f);
            bgRt.offsetMax = new Vector2(-3f, -3f);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.10f, 0.93f);

            // ── 슬롯 색상 강조 ────────────────────────────────────────────
            var accent = new GameObject("Accent");
            accent.transform.SetParent(bg.transform, false);
            var accentRt = accent.AddComponent<RectTransform>();
            accentRt.anchorMin = Vector2.zero; accentRt.anchorMax = Vector2.one;
            accentRt.offsetMin = Vector2.zero;  accentRt.offsetMax = Vector2.zero;
            accent.AddComponent<Image>().color = accents[i];

            // ── 키 이미지 (좌상단 코너) ───────────────────────────────────
            var keyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(keyPaths[i]);
            if (keyTex != null)
            {
                var keyGO = new GameObject("Key");
                keyGO.transform.SetParent(bg.transform, false);
                var keyRt = keyGO.AddComponent<RectTransform>();
                keyRt.anchorMin        = new Vector2(0f, 1f);
                keyRt.anchorMax        = new Vector2(0f, 1f);
                keyRt.pivot            = new Vector2(0f, 1f);
                keyRt.anchoredPosition = new Vector2(4f, -4f);
                keyRt.sizeDelta        = new Vector2(22f, 22f);
                keyGO.AddComponent<RawImage>().texture = keyTex;
            }

            // ── 패턴 레이블 (중앙) ────────────────────────────────────────
            bool isSpecialSlot = (i == 3);
            var lbl = CreateTMPText("Label", bg.transform,
                labelTexts[i],
                isSpecialSlot ? 11f : 14f,
                FontStyles.Bold, Color.white,
                new Vector2(0f, isSpecialSlot ? 10f : 0f),
                new Vector2(SlotSize - 10f, 22f));

            if (isSpecialSlot)
            {
                result.label4 = lbl;

                // 탄수 텍스트 (하단)
                result.ammo4 = CreateTMPText("Ammo", bg.transform, "",
                    13f, FontStyles.Normal, new Color(1f, 0.88f, 0.25f),
                    new Vector2(0f, -9f), new Vector2(SlotSize - 10f, 18f));

                // Dim 오버레이 (무기 없을 때)
                var dimGO = new GameObject("Dim");
                dimGO.transform.SetParent(bg.transform, false);
                var dimRt = dimGO.AddComponent<RectTransform>();
                dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
                dimRt.offsetMin = Vector2.zero;  dimRt.offsetMax = Vector2.zero;
                result.dim4 = dimGO.AddComponent<Image>();
                result.dim4.color = new Color(0f, 0f, 0f, 0.62f);
            }
        }

        return result;
    }

    // ─── UI 하위 헬퍼들 ───────────────────────────────────────────────────

    static GameObject CreatePanel(string name, Transform parent, Vector2 anchoredPos,
                                  Vector2 size, Color bgColor)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        return go;
    }

    static TMP_Text CreateTMPText(string name, Transform parent, string text, float size,
                                  FontStyles style, Color color, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = sizeDelta;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text       = text;
        tmp.fontSize   = size;
        tmp.fontStyle  = style;
        tmp.color      = color;
        tmp.alignment  = TextAlignmentOptions.Center;
        if (Font != null) tmp.font = Font;
        return tmp;
    }

    static TMP_InputField CreateInputField(string name, Transform parent,
                                           string placeholder, Vector2 pos, Vector2 size,
                                           int fontSize = 20)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.2f);

        var field = go.AddComponent<TMP_InputField>();

        // Text 영역
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(go.transform, false);
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8, 4); textRt.offsetMax = new Vector2(-8, -4);
        var textComp = textObj.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = fontSize; textComp.color = Color.white;
        if (Font != null) textComp.font = Font;

        // Placeholder
        var phObj = new GameObject("Placeholder");
        phObj.transform.SetParent(go.transform, false);
        var phRt  = phObj.AddComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(8, 4); phRt.offsetMax = new Vector2(-8, -4);
        var phText = phObj.AddComponent<TextMeshProUGUI>();
        phText.text      = placeholder;
        phText.fontSize  = fontSize;
        phText.color     = new Color(0.5f, 0.5f, 0.5f);
        phText.fontStyle = FontStyles.Italic;
        if (Font != null) phText.font = Font;

        field.textComponent  = textComp;
        field.placeholder    = phText;
        return field;
    }

    static Button CreateButton(string name, Transform parent, string label,
                               Vector2 pos, Vector2 size, Color bgColor,
                               int fontSize = 22)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(go.transform, false);
        var tRt = textObj.AddComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = tRt.offsetMax = Vector2.zero;
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        if (Font != null) tmp.font = Font;

        btn.targetGraphic = img;
        return btn;
    }

    static Slider CreateSlider(string name, Transform parent,
                               Vector2 pos, Vector2 size, Color fillColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var slider = go.AddComponent<Slider>();

        // Background
        var bg = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f);

        // Fill Area
        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        var faRt = fillArea.AddComponent<RectTransform>();
        faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
        faRt.offsetMin = faRt.offsetMax = Vector2.zero;

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fRt = fill.AddComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.offsetMin = fRt.offsetMax = Vector2.zero;
        var fImg = fill.AddComponent<Image>();
        fImg.color = fillColor;

        slider.fillRect       = fRt;
        slider.targetGraphic  = fImg;
        slider.value          = 1f;
        slider.interactable   = false;
        return slider;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  헬퍼: 스프라이트 & 프리팹
    // ═══════════════════════════════════════════════════════════════════════

    static Sprite GetOrCreateSprite(string filename, int size, bool circle, Color color)
    {
        string path = ArtPath + filename;
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        var center = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool inside = !circle || Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) <= r;
                pixels[y * size + x] = inside ? color : Color.clear;
            }

        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType  = TextureImporterType.Sprite;
        importer.spritePivot  = new Vector2(0.5f, 0.5f);
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ── HitEffect 프리팹 ──────────────────────────────────────────────────

    [MenuItem("BulletHell/Rebuild HitEffect Prefab")]
    static void RebuildHitEffectPrefabMenu()
    {
        if (!NotInPlayMode()) return;
        EnsureFolders();
        GetOrCreateHitEffectPrefab(force: true);
        Debug.Log("[SceneBuilder] HitEffect 프리팹 재생성 완료");
    }

    static GameObject GetOrCreateHitEffectPrefab(bool force = false)
    {
        const string effectDir  = "Assets/Effects/symmetrical_impact_001_large_red/";
        const string prefabPath = "Assets/Art/Generated/HitEffect.prefab";

        // 1단계: 모든 프레임을 Sprite 타입으로 강제 동기 임포트
        for (int i = 0; i < 7; i++)
        {
            string fp  = $"{effectDir}frame{i:D4}.png";
            var    imp = AssetImporter.GetAtPath(fp) as TextureImporter;
            if (imp == null) { Debug.LogError($"[SceneBuilder] 파일 없음: {fp}"); continue; }

            imp.textureType         = TextureImporterType.Sprite;
            imp.spritePivot         = new Vector2(0.5f, 0.5f);
            imp.alphaIsTransparency = true;
            imp.filterMode          = FilterMode.Point;
            imp.SaveAndReimport();
            // SaveAndReimport 직후 강제 동기화 — 이 줄이 없으면 Sprite가 null로 로드됨
            AssetDatabase.ImportAsset(fp, ImportAssetOptions.ForceSynchronousImport);
        }
        AssetDatabase.Refresh();

        // 2단계: 기존 프리팹 유효성 확인 (force 아니면 재사용)
        if (!force)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null && existing.GetComponent<HitEffect>()?.Frames?.Length == 7
                && existing.GetComponent<HitEffect>().Frames[0] != null)
                return existing;
        }

        // 3단계: 스프라이트 로드
        var frames = new Sprite[7];
        for (int i = 0; i < 7; i++)
        {
            string fp  = $"{effectDir}frame{i:D4}.png";
            frames[i]  = AssetDatabase.LoadAssetAtPath<Sprite>(fp);
            if (frames[i] == null)
                Debug.LogError($"[SceneBuilder] Sprite 로드 실패: {fp} — Sprite 타입으로 임포트됐는지 확인");
        }

        // 4단계: 프리팹 생성
        AssetDatabase.DeleteAsset(prefabPath);

        var go     = new GameObject("HitEffect");
        go.AddComponent<SpriteRenderer>();
        var effect = go.AddComponent<HitEffect>();
        effect.Frames       = frames;
        effect.FPS          = 18f;
        effect.Scale        = 3f;
        effect.SortingOrder = 100;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();
        return prefab;
    }

    // ── 3D 총알 프리팹 ────────────────────────────────────────────────────

    static GameObject GetOrCreateBulletPrefab3D()
    {
        string path = "Assets/Art/Generated/Bullet.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        bool needsRebuild = existing == null
            || existing.GetComponent<CircleCollider2D>() != null
            || existing.GetComponent<Bullet>()?.HitEffectPrefab == null;

        if (!needsRebuild) return existing;

        if (existing != null) AssetDatabase.DeleteAsset(path);

        var go = new GameObject("Bullet");
        go.transform.localScale = Vector3.one * 0.2f;

        // 구체 메시
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetOrCreateMaterial("BulletMat", "", new Color(1f, 0.9f, 0f));

        // 3D 트리거 콜라이더
        var sc = go.AddComponent<SphereCollider>();
        sc.radius    = 0.5f;
        sc.isTrigger = true;

        var bullet = go.AddComponent<Bullet>();
        bullet.HitEffectPrefab = GetOrCreateHitEffectPrefab();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ── 머티리얼 헬퍼 ─────────────────────────────────────────────────────

    static Material GetOrCreateMaterial(string matName, string texturePath, Color fallback)
    {
        string matPath = ArtPath + matName + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
        var mat = new Material(shader);

        if (!string.IsNullOrEmpty(texturePath))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex != null)
            {
                mat.mainTexture = tex;
                mat.SetColor("_BaseColor", Color.white);
            }
            else mat.color = fallback;
        }
        else mat.color = fallback;

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RoomList Scene
    // ═══════════════════════════════════════════════════════════════════════

    static void BuildRoomListScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 5f;
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.05f, 0.05f, 0.1f);
        camObj.transform.position = new Vector3(0, 0, -10);
        camObj.AddComponent<AudioListener>();

        // ApiClient (DontDestroyOnLoad)
        var apiObj = new GameObject("ApiClient");
        apiObj.AddComponent<ApiClient>();

        // Canvas
        var canvasObj = new GameObject("Canvas");
        var canvas    = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();
        var roomListUI = canvasObj.AddComponent<RoomListUI>();

        // EventSystem
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();

        // ── 전체 배경 ─────────────────────────────────────────────────────
        var bgPanel = CreatePanel("Background", canvasObj.transform,
            Vector2.zero, new Vector2(1920, 1080), new Color(0.05f, 0.05f, 0.1f));
        var bgRt = bgPanel.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // ── 헤더 ─────────────────────────────────────────────────────────
        var header = CreatePanel("Header", canvasObj.transform,
            new Vector2(0, 490f), new Vector2(1920, 80f), new Color(0.08f, 0.08f, 0.18f));

        CreateTMPText("Title", header.transform, "방  목  록", 36, FontStyles.Bold,
            Color.white, new Vector2(-600f, 0f), new Vector2(400f, 60f));

        var playerIdLabel = CreateTMPText("PlayerIdLabel", header.transform,
            "접속 중: -", 20, FontStyles.Normal, new Color(0.6f, 0.6f, 0.6f),
            new Vector2(700f, 0f), new Vector2(400f, 40f));

        var backBtn = CreateButton("Back_Button", header.transform,
            "뒤로", new Vector2(-830f, 0f), new Vector2(120f, 50f),
            new Color(0.25f, 0.25f, 0.35f));

        // ── 방 목록 스크롤 뷰 ────────────────────────────────────────────
        var scrollGo   = new GameObject("ScrollView");
        scrollGo.transform.SetParent(canvasObj.transform, false);
        var scrollRt   = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, 10f);
        scrollRt.sizeDelta        = new Vector2(1100f, 760f);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        // Viewport
        var viewport   = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = vpRt;

        // Content
        var content   = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing           = 8f;
        vlg.padding           = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentRt;

        // Scrollbar (세로)
        var sbGo = new GameObject("Scrollbar_Vertical");
        sbGo.transform.SetParent(scrollGo.transform, false);
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin        = new Vector2(1, 0);
        sbRt.anchorMax        = Vector2.one;
        sbRt.offsetMin        = new Vector2(-16, 0);
        sbRt.offsetMax        = Vector2.zero;
        sbGo.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);
        var sb = sbGo.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        scrollRect.verticalScrollbar          = sb;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // ── RoomItem 프리팹 ────────────────────────────────────────────────
        var roomItemPrefab = GetOrCreateRoomItemPrefab();

        // ── 우측 패널 (방 만들기 + 새로고침) ──────────────────────────────
        var sidePanel = CreatePanel("SidePanel", canvasObj.transform,
            new Vector2(650f, 10f), new Vector2(340f, 760f), new Color(0.08f, 0.08f, 0.18f));

        CreateTMPText("Side_Title", sidePanel.transform, "빠른 메뉴", 24, FontStyles.Bold,
            Color.white, new Vector2(0f, 340f), new Vector2(300f, 40f));

        var createBtn = CreateButton("Create_Room_Button", sidePanel.transform,
            "+ 방  만  들  기", new Vector2(0f, 260f), new Vector2(280f, 65f),
            new Color(0.15f, 0.5f, 0.9f));

        var refreshBtn = CreateButton("Refresh_Button", sidePanel.transform,
            "새로고침", new Vector2(0f, 180f), new Vector2(280f, 55f),
            new Color(0.2f, 0.35f, 0.5f));

        var statusText = CreateTMPText("Status_Text", sidePanel.transform,
            "방 목록을 불러오는 중...", 18, FontStyles.Normal, Color.gray,
            new Vector2(0f, -320f), new Vector2(300f, 60f));

        // ── 방 만들기 팝업 ────────────────────────────────────────────────
        var popupPanel = CreatePanel("CreateRoom_Panel", canvasObj.transform,
            Vector2.zero, new Vector2(520f, 280f), new Color(0.1f, 0.1f, 0.2f));
        // 외곽선 효과용 약간 큰 패널
        var popupOutline = CreatePanel("Outline", popupPanel.transform,
            Vector2.zero, new Vector2(524f, 284f), new Color(0.3f, 0.5f, 0.9f, 0.6f));
        popupOutline.transform.SetAsFirstSibling();

        CreateTMPText("Popup_Title", popupPanel.transform, "방 만들기", 28, FontStyles.Bold,
            Color.white, new Vector2(0f, 95f), new Vector2(460f, 50f));

        var roomNameInput = CreateInputField("RoomName_Input", popupPanel.transform,
            "방 이름 입력...", new Vector2(0f, 20f), new Vector2(420f, 55f));

        var confirmBtn = CreateButton("Confirm_Button", popupPanel.transform,
            "만들기", new Vector2(110f, -80f), new Vector2(160f, 50f),
            new Color(0.15f, 0.5f, 0.9f));

        var cancelBtn = CreateButton("Cancel_Button", popupPanel.transform,
            "취소", new Vector2(-110f, -80f), new Vector2(160f, 50f),
            new Color(0.35f, 0.2f, 0.2f));

        popupPanel.SetActive(false);

        // ── RoomListUI 참조 연결 ──────────────────────────────────────────
        roomListUI.RoomListContent    = contentRt;
        roomListUI.RoomItemPrefab     = roomItemPrefab;
        roomListUI.RefreshButton      = refreshBtn;
        roomListUI.CreateRoomButton   = createBtn;
        roomListUI.CreateRoomPanel    = popupPanel;
        roomListUI.RoomNameInput      = roomNameInput;
        roomListUI.ConfirmCreateButton = confirmBtn;
        roomListUI.CancelCreateButton  = cancelBtn;
        roomListUI.BackButton         = backBtn;
        roomListUI.StatusText         = statusText;
        roomListUI.PlayerIdLabel      = playerIdLabel;

        EditorSceneManager.SaveScene(scene, ScenePath + "RoomList.unity");
        Debug.Log("[SceneBuilder] RoomList 씬 생성 완료");
    }

    // ─── RoomItem 프리팹 ──────────────────────────────────────────────────────

    static GameObject GetOrCreateRoomItemPrefab()
    {
        string path = "Assets/Art/Generated/RoomItem.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var go = new GameObject("RoomItem");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 80f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.22f);

        var itemUI = go.AddComponent<RoomItemUI>();

        // 방 이름
        var nameText = CreateTMPText("RoomName", go.transform, "방 이름",
            22, FontStyles.Bold, Color.white, new Vector2(-250f, 12f), new Vector2(340f, 30f));

        // 호스트
        CreateTMPText("HostText", go.transform, "호스트: -",
            16, FontStyles.Normal, new Color(0.6f, 0.6f, 0.7f), new Vector2(-250f, -15f), new Vector2(340f, 25f));

        // 플레이어 수
        var playerCount = CreateTMPText("PlayerCount", go.transform, "0 / 2",
            20, FontStyles.Bold, Color.white, new Vector2(150f, 0f), new Vector2(100f, 30f));

        // 상태
        var statusText = CreateTMPText("Status", go.transform, "대기중",
            18, FontStyles.Normal, new Color(0.3f, 0.9f, 0.4f), new Vector2(320f, 0f), new Vector2(100f, 30f));

        // 입장 버튼
        var joinBtn = CreateButton("Join_Button", go.transform,
            "입장", new Vector2(460f, 0f), new Vector2(90f, 55f), new Color(0.15f, 0.5f, 0.9f));

        // RoomItemUI 참조
        itemUI.RoomNameText    = nameText;
        itemUI.PlayerCountText = playerCount;
        itemUI.StatusText      = statusText;
        itemUI.JoinButton      = joinBtn;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  유틸
    // ═══════════════════════════════════════════════════════════════════════

    static void EnsureTags()
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        var tagsProp = tagManager.FindProperty("tags");

        foreach (string tag in new[] { "Wall", "Player1", "Player2" })
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }

            if (!exists)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Generated"))
            AssetDatabase.CreateFolder("Assets/Art", "Generated");
        if (!AssetDatabase.IsValidFolder("Assets/Scripts/Editor"))
            AssetDatabase.CreateFolder("Assets/Scripts", "Editor");
    }

    static void AddScenesToBuildSettings()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity",  true),
            new EditorBuildSettingsScene("Assets/Scenes/RoomList.unity",  true),
            new EditorBuildSettingsScene("Assets/Scenes/GameScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/TestScene.unity", true),
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("[SceneBuilder] Build Settings 업데이트 완료");
    }
}
