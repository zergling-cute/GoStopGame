using UnityEngine;
using Unity.Netcode;

public class NetworkConnectUI : MonoBehaviour
{
    // 게임이 시작될 때 (스크립트가 켜질 때) 한 번 실행됨
    private void Start()
    {
        // NetworkManager에게 "누가 들어오거나 나가면 나한테 꼭 알려줘!" 라고 예약(구독)해두는 과정입니다.
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // 게임이 꺼지거나 씬이 바뀔 때 실행됨 (메모리 낭비 방지용)
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // 🚀 누군가 방에 접속 성공했을 때 자동으로 실행되는 함수!
    // clientId는 접속한 사람에게 부여되는 고유 번호입니다.
    private void OnClientConnected(ulong clientId)
    {
        // 방장(Host)은 무조건 첫 번째로 접속하므로 ID가 항상 0번입니다.
        if (clientId == 0)
        {
            Debug.Log($"[시스템] 방장(Host)이 방을 생성했습니다. (부여된 ID: {clientId})");
        }
        else
        {
            // 손님들은 1, 2, 3... 순서대로 번호를 받습니다.
            Debug.Log($"[시스템] 🙋 새로운 손님이 접속했습니다! (부여된 ID: {clientId})");

            // 현재 방에 총 몇 명이 있는지 확인하는 방법
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
            Debug.Log($"현재 접속 인원: {currentPlayers}명 / 3명");

            // 만약 3명이 꽉 찼다면?
            if (currentPlayers == 3)
            {
                Debug.Log("🎉 3명이 모두 모였습니다! 이제 게임 시작 버튼을 누르세요.");
            }
        }
    }

    // 🚀 누군가 방에서 나갔거나, 연결이 끊겼을 때 자동으로 실행되는 함수
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[시스템] 🏃 클라이언트가 방을 나갔습니다. (ID: {clientId})");
    }

    // 기존의 접속 버튼 함수들
    public void StartHost()
    {
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient) return;
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient) return;
        NetworkManager.Singleton.StartClient();
    }
}