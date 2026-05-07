using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MultiplayerLobbyManager : NetworkBehaviour
{
    public static MultiplayerLobbyManager Instance;

    [Header("UI Panels")]
    public GameObject panelMainLobby;
    public GameObject panelRoomList;
    public GameObject panelInRoom;

    [Header("Room List UI")]
    public Transform roomListContent;
    public GameObject roomItemPrefab;

    [Header("In Room UI")]
    public TextMeshProUGUI playerCountText;
    public Button btnStartGame;
    public GameObject[] playerIcons = new GameObject[3]; // 플레이어 이미지 배열

    [Header("Scene Settings")]
    public string lobbySceneName = "LobbyScene";
    public string gameSceneName = "GameScene";

    private Lobby _hostLobby;
    private float _heartbeatTimer;

    private async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (Exception e) { Debug.LogError($"초기화 에러: {e.Message}"); }

        FindAndAllConnectUI();
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"<color=yellow>씬 로드 완료: {scene.name}. UI 전체 재연결 시작</color>");
        FindAndAllConnectUI();
    }

    private void FindAndAllConnectUI()
    {
        // 1. 비활성화된 패널/오브젝트들 전수 조사 및 할당
        Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();

        for (int i = 0; i < 3; i++) playerIcons[i] = null; // 초기화

        foreach (Transform t in allTransforms)
        {
            if (t.hideFlags == HideFlags.None)
            {
                // 패널 및 텍스트
                if (t.name == "Panel_MainLobby") panelMainLobby = t.gameObject;
                else if (t.name == "Panel_RoomList") panelRoomList = t.gameObject;
                else if (t.name == "Panel_InRoom") panelInRoom = t.gameObject;
                else if (t.name == "RoomListContent") roomListContent = t;
                else if (t.name == "PlayerCountText") playerCountText = t.GetComponent<TextMeshProUGUI>();
                else if (t.name == "StartGameButton") btnStartGame = t.GetComponent<Button>();

                // 🚀 플레이어 아이콘 (Player_0, Player_1, Player_2)
                else if (t.name == "Player_0") playerIcons[0] = t.gameObject;
                else if (t.name == "Player_1") playerIcons[1] = t.gameObject;
                else if (t.name == "Player_2") playerIcons[2] = t.gameObject;
            }
        }

        // 2. 버튼 이벤트 재연결
        ConnectButton("CreateButton", CreateRoom);
        ConnectButton("FindButton", OpenRoomList);
        ConnectButton("RefreshButton", RefreshRoomList);
        ConnectButton("BackButton", () => ShowPanel(panelMainLobby));
        ConnectButton("StartGameButton", StartGameFromLobby);
        ConnectButton("QuitButton", LeaveOrQuitGame);

        if (SceneManager.GetActiveScene().name == lobbySceneName) ShowPanel(panelMainLobby);
    }

    private void ConnectButton(string objName, UnityEngine.Events.UnityAction action)
    {
        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button btn in allButtons)
        {
            if (btn.name == objName && btn.gameObject.hideFlags == HideFlags.None)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(action);
                return;
            }
        }
    }

    private void Update()
    {
        if (_hostLobby != null && IsServer)
        {
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer > 15f) { _heartbeatTimer = 0; LobbyService.Instance.SendHeartbeatPingAsync(_hostLobby.Id); }
        }

        if (panelInRoom != null && panelInRoom.activeInHierarchy && NetworkManager.Singleton.IsListening)
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

            // 텍스트 업데이트
            if (playerCountText != null) playerCountText.text = $"현재 인원: {currentPlayers} / 3";

            // 🚀 [추가] 플레이어 아이콘 활성화 로직
            for (int i = 0; i < playerIcons.Length; i++)
            {
                if (playerIcons[i] != null)
                {
                    playerIcons[i].SetActive(i < currentPlayers);
                }
            }

            // 시작 버튼 관리
            if (btnStartGame != null)
            {
                btnStartGame.gameObject.SetActive(IsServer);
                btnStartGame.interactable = (currentPlayers >= 1); // 테스트용 1명
            }
        }
    }

    public void ShowPanel(GameObject panel)
    {
        if (panelMainLobby) panelMainLobby.SetActive(false);
        if (panelRoomList) panelRoomList.SetActive(false);
        if (panelInRoom) panelInRoom.SetActive(false);
        if (panel) panel.SetActive(true);
    }

    // --- 네트워크 로직 ---
    public async void CreateRoom()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetRelayServerData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port, allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);
            var options = new CreateLobbyOptions { IsPrivate = false, Data = new Dictionary<string, DataObject> { { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) } } };
            _hostLobby = await LobbyService.Instance.CreateLobbyAsync("고스톱 한 판!", 3, options);
            NetworkManager.Singleton.StartHost();
            ShowPanel(panelInRoom);
        }
        catch (Exception e) { Debug.LogError($"방 생성 실패: {e.Message}"); }
    }

    public void OpenRoomList() { ShowPanel(panelRoomList); RefreshRoomList(); }

    public async void RefreshRoomList()
    {
        if (roomListContent == null) return;
        try
        {
            foreach (Transform child in roomListContent) Destroy(child.gameObject);
            var queryResponse = await LobbyService.Instance.QueryLobbiesAsync();
            foreach (var lobby in queryResponse.Results)
            {
                var btnObj = Instantiate(roomItemPrefab, roomListContent);
                btnObj.GetComponentInChildren<TextMeshProUGUI>().text = $"{lobby.Name} ({lobby.Players.Count}/3)";
                btnObj.GetComponent<Button>().onClick.AddListener(() => JoinRoom(lobby));
            }
        }
        catch (Exception e) { Debug.LogError($"목록 갱신 실패: {e.Message}"); }
    }

    public async void JoinRoom(Lobby lobby)
    {
        try
        {
            var joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            string joinCode = joinedLobby.Data["JoinCode"].Value;
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetClientRelayData(joinAllocation.RelayServer.IpV4, (ushort)joinAllocation.RelayServer.Port, joinAllocation.AllocationIdBytes, joinAllocation.Key, joinAllocation.ConnectionData, joinAllocation.HostConnectionData);
            NetworkManager.Singleton.StartClient();
            ShowPanel(panelInRoom);
        }
        catch (Exception e) { Debug.LogError($"접속 실패: {e.Message}"); }
    }

    public void StartGameFromLobby() { if (IsServer) NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single); }

    public async void LeaveOrQuitGame()
    {
        try { if (NetworkManager.Singleton.IsListening) { if (IsServer && _hostLobby != null) await LobbyService.Instance.DeleteLobbyAsync(_hostLobby.Id); NetworkManager.Singleton.Shutdown(); } }
        catch (Exception e) { Debug.LogWarning(e.Message); }
        SceneManager.LoadScene(lobbySceneName);
    }

    private void OnClientDisconnect(ulong clientId) { if (!IsServer && clientId == NetworkManager.Singleton.LocalClientId) { NetworkManager.Singleton.Shutdown(); SceneManager.LoadScene(lobbySceneName); } }

    public override void OnDestroy() { if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect; SceneManager.sceneLoaded -= OnSceneLoaded; base.OnDestroy(); }
}