using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

// NetworkBehaviour를 상속받아야 넷코드 기능을 쓸 수 있습니다.
public class LobbyManager : NetworkBehaviour
{
    // 방장(Host)이 '게임 시작' 버튼을 눌렀을 때 실행될 함수
    public void GoToGameScene()
    {
        // 1. 방장(서버) 권한이 있는지 확인
        if (!IsServer)
        {
            Debug.LogWarning("손님은 게임을 시작할 권한이 없습니다!");
            return;
        }

        Debug.Log("방장이 게임 씬으로 이동을 지시합니다. 모두 꽉 잡으세요!");

        // 2. 🚀 핵심 코드: 일반 SceneManager가 아니라 NetworkManager의 SceneManager를 씁니다!
        // 이렇게 하면 접속해 있는 모든 클라이언트(손님)들의 씬도 "GameScene"으로 강제 이동됩니다.
        NetworkManager.Singleton.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
    }
}