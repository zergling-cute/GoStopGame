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
using UnityEngine.SceneManagement; // 🚀 씬 전환을 위해 추가

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

    [Header("Scene Settings")]
    public string gameSceneName = "GameScene"; // 🚀 실제 게임 씬 이름을 넣어주세요.

    private Lobby _hostLobby;
    private float _heartbeatTimer;

    private async void Awake()
    {
        // 🚀 씬이 넘어가도 로비 매니저가 파괴되지 않게 하거나, 
        // 혹은 씬마다 새로 배치한다면 싱글톤 유지가 필요합니다.
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Multiplayer SDK] 초기화 에러: {e.Message}");
        }

        ShowPanel(panelMainLobby);
    }

    private void Update()
    {
        if (_hostLobby != null && IsServer)
        {
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer > 15f)
            {
                _heartbeatTimer = 0;
                LobbyService.Instance.SendHeartbeatPingAsync(_hostLobby.Id);
            }
        }

        if (panelInRoom.activeSelf && NetworkManager.Singleton.IsListening)
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
            playerCountText.text = $"현재 인원: {currentPlayers} / 3";

            // 🚀 방장에게만 시작 버튼 노출
            btnStartGame.gameObject.SetActive(IsServer);
            btnStartGame.interactable = (currentPlayers >= 1); // 테스트 완료 후 3으로 변경
        }
    }

    public void ShowPanel(GameObject panel)
    {
        panelMainLobby.SetActive(false);
        panelRoomList.SetActive(false);
        panelInRoom.SetActive(false);
        panel.SetActive(true);
    }

    public async void CreateRoom()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetRelayServerData(
                allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData
            );

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject> {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            _hostLobby = await LobbyService.Instance.CreateLobbyAsync("고스톱 한 판!", 3, options);

            NetworkManager.Singleton.StartHost();
            ShowPanel(panelInRoom);
        }
        catch (Exception e)
        {
            Debug.LogError($"방 생성 실패: {e.Message}");
        }
    }

    public void OpenRoomList()
    {
        ShowPanel(panelRoomList);
        RefreshRoomList();
    }

    public async void RefreshRoomList()
    {
        try
        {
            foreach (Transform child in roomListContent) Destroy(child.gameObject);

            var options = new QueryLobbiesOptions
            {
                Filters = new List<QueryFilter> {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                }
            };

            var queryResponse = await LobbyService.Instance.QueryLobbiesAsync(options);

            foreach (var lobby in queryResponse.Results)
            {
                var btnObj = Instantiate(roomItemPrefab, roomListContent);
                btnObj.GetComponentInChildren<TextMeshProUGUI>().text = $"{lobby.Name} ({lobby.Players.Count}/3)";
                btnObj.GetComponent<Button>().onClick.AddListener(() => JoinRoom(lobby));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"방 목록 갱신 실패: {e.Message}");
        }
    }

    public async void JoinRoom(Lobby lobby)
    {
        try
        {
            var joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            string joinCode = joinedLobby.Data["JoinCode"].Value;

            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetClientRelayData(
                joinAllocation.RelayServer.IpV4, (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes, joinAllocation.Key, joinAllocation.ConnectionData, joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
            ShowPanel(panelInRoom);
        }
        catch (Exception e)
        {
            Debug.LogError($"방 접속 실패: {e.Message}");
        }
    }

    // ==========================================
    // 🚀 실제 게임 씬으로 이동하는 로직
    // ==========================================
    public void StartGameFromLobby()
    {
        if (IsServer)
        {
            Debug.Log("방장이 씬 전환을 시작합니다...");
            // 🚀 핵심: 일반 SceneManager가 아니라 NetworkManager의 SceneManager를 호출!
            // 이렇게 해야 접속 중인 모든 손님 클라이언트들도 함께 강제 이동합니다.
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    public async void LeaveRoom()
    {
        try
        {
            // 1. 서버(방장)인 경우: 방을 폭파하고 모든 손님을 내보냄
            if (IsServer)
            {
                Debug.Log("방장이 방을 나갑니다. 방을 해체합니다.");

                // 로비 서비스에서 방 삭제 (목록에서 제거)
                if (_hostLobby != null)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_hostLobby.Id);
                    _hostLobby = null;
                }

                // 모든 클라이언트의 연결을 끊고 서버 종료
                NetworkManager.Singleton.Shutdown();
            }
            // 2. 클라이언트(손님)인 경우: 나만 조용히 나감
            else
            {
                Debug.Log("방을 나갑니다.");

                if (_hostLobby != null)
                {
                    // 로비에서 나 자신을 제거
                    await LobbyService.Instance.RemovePlayerAsync(_hostLobby.Id, AuthenticationService.Instance.PlayerId);
                }

                // 내 연결만 끊기
                NetworkManager.Singleton.Shutdown();
            }

            // 3. UI를 다시 메인 로비 화면으로 전환
            ShowPanel(panelMainLobby);

            // 🚀 만약 게임 씬에 있었다면 메인 씬으로 이동시키는 코드 (선택 사항)
            // UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }
        catch (Exception e)
        {
            Debug.LogError($"방 나가기 실패: {e.Message}");
        }
    }
}