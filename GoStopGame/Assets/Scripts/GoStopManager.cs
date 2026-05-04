using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public enum CardType : byte { 광, 열끝, 띠, 피 }

[System.Serializable]
public struct HwatuCard : INetworkSerializable
{
    public int month; public CardType type; public int id;
    public HwatuCard(int month, CardType type, int id) { this.month = month; this.type = type; this.id = id; }
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref month); serializer.SerializeValue(ref type); serializer.SerializeValue(ref id);
    }
}

public class GoStopManager : NetworkBehaviour
{
    public static GoStopManager Instance;
    private void Awake() { Instance = this; }

    [Header("Deck & Field")]
    public List<HwatuCard> deck = new List<HwatuCard>();
    public List<HwatuCard> fieldCards = new List<HwatuCard>();

    [Header("Players Hands")]
    public List<HwatuCard> player1Hand = new List<HwatuCard>();
    public List<HwatuCard> player2Hand = new List<HwatuCard>();
    public List<HwatuCard> player3Hand = new List<HwatuCard>();

    [Header("Players Captured")]
    public List<HwatuCard> player1Captured = new List<HwatuCard>();
    public List<HwatuCard> player2Captured = new List<HwatuCard>();
    public List<HwatuCard> player3Captured = new List<HwatuCard>();

    [Header("UI Areas")]
    public GameObject cardPrefab;
    public Transform fieldAreaUI; public Transform myHandAreaUI; public Transform myCapturedAreaUI;
    public Transform op1HandAreaUI; public Transform op1CapturedAreaUI;
    public Transform op2HandAreaUI; public Transform op2CapturedAreaUI;

    // ==========================================
    // 게임 상태 관리 변수들
    // ==========================================
    public bool isWaitingChoice = false;
    public ulong choosingPlayerId = 999;
    public int choiceMonth = -1;
    public HwatuCard pendingCard;
    public bool isDeckPhaseChoice = false;

    // 🚀 [새로 추가됨] 연출이 진행되는 동안 아무도 클릭하지 못하게 막는 자물쇠
    public bool isProcessingTurn = false;

    public void StartGame()
    {
        if (!IsServer) return;
        InitializeDeck(); ShuffleDeck(); DealCardsFor3Players();
    }

    void InitializeDeck()
    {
        deck.Clear();
        for (int m = 1; m <= 12; m++)
        {
            CardType first = (m == 1 || m == 3 || m == 8 || m == 11 || m == 12) ? CardType.광 : CardType.피;
            deck.Add(new HwatuCard(m, first, 1)); deck.Add(new HwatuCard(m, CardType.열끝, 2));
            deck.Add(new HwatuCard(m, CardType.띠, 3)); deck.Add(new HwatuCard(m, CardType.피, 4));
        }
    }
    void ShuffleDeck()
    {
        for (int i = 0; i < deck.Count; i++)
        {
            int r = Random.Range(i, deck.Count);
            HwatuCard temp = deck[i]; deck[i] = deck[r]; deck[r] = temp;
        }
    }
    void DealCardsFor3Players()
    {
        player1Hand.Clear(); player2Hand.Clear(); player3Hand.Clear(); fieldCards.Clear();
        player1Captured.Clear(); player2Captured.Clear(); player3Captured.Clear();
        DrawCards(player1Hand, 7); DrawCards(player2Hand, 7); DrawCards(player3Hand, 7); DrawCards(fieldCards, 6);
        player1Hand = player1Hand.OrderBy(c => c.month).ToList(); player2Hand = player2Hand.OrderBy(c => c.month).ToList(); player3Hand = player3Hand.OrderBy(c => c.month).ToList();

        SyncState();
    }
    void DrawCards(List<HwatuCard> target, int count)
    {
        for (int i = 0; i < count; i++) { if (deck.Count > 0) { target.Add(deck[0]); deck.RemoveAt(0); } }
    }

    public void OnCardClicked(HwatuCard clickedCard)
    {
        ulong myId = NetworkManager.Singleton.LocalClientId;
        // 자물쇠가 걸려있으면 클릭 무시!
        if (isProcessingTurn) return;

        if (isWaitingChoice && choosingPlayerId == myId)
        {
            ChooseBoardCardServerRpc(clickedCard, myId);
        }
        else if (!isWaitingChoice)
        {
            PlayCardServerRpc(clickedCard, myId);
        }
    }

    // ==========================================
    // 🚀 여기서부터 타이밍을 조절하는 '코루틴' 마법이 시작됩니다!
    // ==========================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PlayCardServerRpc(HwatuCard cardToPlay, ulong clientId)
    {
        if (isProcessingTurn) return;
        isProcessingTurn = true; // 자물쇠 잠그기

        // 1. 손패에서 지우기
        if (clientId == 0) player1Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 1) player2Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 2) player3Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);

        // 2. 코루틴 출발!
        StartCoroutine(PlayTurnRoutine(cardToPlay, clientId));
    }

    IEnumerator PlayTurnRoutine(HwatuCard cardToPlay, ulong clientId)
    {
        fieldCards.Add(cardToPlay);
        SyncState();
        yield return new WaitForSeconds(0.6f); // 0.6초 대기

        fieldCards.Remove(cardToPlay);
        List<HwatuCard> matches = fieldCards.Where(c => c.month == cardToPlay.month).ToList();

        if (matches.Count == 2) { /* 기존 선택 로직 유지 */ }

        // ==========================================
        // 🚀 [추가됨] 짝이 맞았을 때 강조 연출!
        // ==========================================
        if (matches.Count > 0)
        {
            // 방금 바닥에 깔린 카드를 포함해서, 같은 월인 애들을 찾아 번쩍이게 합니다.
            fieldCards.Add(cardToPlay); // 연출을 위해 잠깐 바닥에 다시 둡니다.
            SyncState();

            // 화면에 깔린 카드 UI들을 다 뒤져서 같은 월인 애들을 번쩍이게 합니다.
            CardUI[] allBoardCards = fieldAreaUI.GetComponentsInChildren<CardUI>();
            foreach (CardUI ui in allBoardCards)
            {
                if (ui.myCardInfo.month == cardToPlay.month) ui.ShowHighlightEffect();
            }

            fieldCards.Remove(cardToPlay); // 연출 끝났으니 다시 뺌
            yield return new WaitForSeconds(0.4f); // 번쩍이는 거 볼 시간 주기!
        }
        // ==========================================

        ResolveMatch(cardToPlay, matches, clientId);
        SyncState();
        yield return new WaitForSeconds(0.3f);

        StartCoroutine(DrawDeckRoutine(clientId));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ChooseBoardCardServerRpc(HwatuCard chosenCard, ulong clientId)
    {
        if (!isWaitingChoice || choosingPlayerId != clientId || chosenCard.month != choiceMonth) return;

        isWaitingChoice = false;
        isProcessingTurn = true; // 다시 연출 시작이니 자물쇠 잠그기

        fieldCards.RemoveAll(c => c.month == chosenCard.month && c.id == chosenCard.id);
        AddCapture(clientId, pendingCard); AddCapture(clientId, chosenCard);

        SyncState();

        if (!isDeckPhaseChoice) StartCoroutine(DrawDeckRoutine(clientId));
        else { isProcessingTurn = false; SyncState(); } // 완전 종료
    }

    IEnumerator DrawDeckRoutine(ulong clientId)
    {
        yield return new WaitForSeconds(0.4f);

        if (deck.Count == 0) { isProcessingTurn = false; SyncState(); yield break; }

        HwatuCard drawnCard = deck[0];
        deck.RemoveAt(0);

        fieldCards.Add(drawnCard);
        SyncState();
        yield return new WaitForSeconds(0.6f);

        fieldCards.Remove(drawnCard);
        List<HwatuCard> matches = fieldCards.Where(c => c.month == drawnCard.month).ToList();

        if (matches.Count == 2) { /* 기존 선택 로직 유지 */ }

        // ==========================================
        // 🚀 [추가됨] 덱에서 깠는데 짝이 맞았을 때 강조 연출!
        // ==========================================
        if (matches.Count > 0)
        {
            fieldCards.Add(drawnCard);
            SyncState();

            CardUI[] allBoardCards = fieldAreaUI.GetComponentsInChildren<CardUI>();
            foreach (CardUI ui in allBoardCards)
            {
                if (ui.myCardInfo.month == drawnCard.month) ui.ShowHighlightEffect();
            }

            fieldCards.Remove(drawnCard);
            yield return new WaitForSeconds(0.4f);
        }
        // ==========================================

        ResolveMatch(drawnCard, matches, clientId);
        isProcessingTurn = false;
        SyncState();
    }

    void ResolveMatch(HwatuCard playedCard, List<HwatuCard> matches, ulong clientId)
    {
        if (matches.Count == 0) fieldCards.Add(playedCard);
        else if (matches.Count == 1 || matches.Count == 3)
        {
            AddCapture(clientId, playedCard);
            foreach (var m in matches) { fieldCards.Remove(m); AddCapture(clientId, m); }
        }
    }

    void AddCapture(ulong clientId, HwatuCard card)
    {
        if (clientId == 0) player1Captured.Add(card); else if (clientId == 1) player2Captured.Add(card); else if (clientId == 2) player3Captured.Add(card);
    }

    // ==========================================
    // 상태 동기화 및 UI
    // ==========================================
    void SyncState()
    {
        UpdateUI();
        UpdateGameStateClientRpc(
            player1Hand.ToArray(), player2Hand.ToArray(), player3Hand.ToArray(), fieldCards.ToArray(),
            player1Captured.ToArray(), player2Captured.ToArray(), player3Captured.ToArray(),
            isWaitingChoice, choosingPlayerId, choiceMonth, isProcessingTurn // 🚀 통신 데이터에 자물쇠 상태 추가
        );
    }

    [ClientRpc]
    void UpdateGameStateClientRpc(
        HwatuCard[] p1, HwatuCard[] p2, HwatuCard[] p3, HwatuCard[] field,
        HwatuCard[] cap1, HwatuCard[] cap2, HwatuCard[] cap3,
        bool wait, ulong chooser, int month, bool processing)
    {
        if (IsServer) return;

        player1Hand = p1.ToList(); player2Hand = p2.ToList(); player3Hand = p3.ToList(); fieldCards = field.ToList();
        player1Captured = cap1.ToList(); player2Captured = cap2.ToList(); player3Captured = cap3.ToList();

        isWaitingChoice = wait; choosingPlayerId = chooser; choiceMonth = month; isProcessingTurn = processing;
        UpdateUI();
    }

    void UpdateUI()
    {
        foreach (Transform child in fieldAreaUI) Destroy(child.gameObject);
        foreach (Transform child in myHandAreaUI) Destroy(child.gameObject);
        foreach (Transform child in myCapturedAreaUI) Destroy(child.gameObject);
        foreach (Transform child in op1HandAreaUI) Destroy(child.gameObject);
        foreach (Transform child in op1CapturedAreaUI) Destroy(child.gameObject);
        foreach (Transform child in op2HandAreaUI) Destroy(child.gameObject);
        foreach (Transform child in op2CapturedAreaUI) Destroy(child.gameObject);

        ulong myId = NetworkManager.Singleton.LocalClientId;
        List<HwatuCard> myHand = null, op1Hand = null, op2Hand = null;
        List<HwatuCard> myCap = null, op1Cap = null, op2Cap = null;

        if (myId == 0) { myHand = player1Hand; op1Hand = player2Hand; op2Hand = player3Hand; myCap = player1Captured; op1Cap = player2Captured; op2Cap = player3Captured; }
        else if (myId == 1) { myHand = player2Hand; op1Hand = player3Hand; op2Hand = player1Hand; myCap = player2Captured; op1Cap = player3Captured; op2Cap = player1Captured; }
        else { myHand = player3Hand; op1Hand = player1Hand; op2Hand = player2Hand; myCap = player3Captured; op1Cap = player1Captured; op2Cap = player2Captured; }

        bool isMyChoiceTurn = (isWaitingChoice && choosingPlayerId == myId);

        // 🚀 카드가 날아다니는 동안(isProcessingTurn == true)에는 내 손패도 누를 수 없게 막습니다!
        bool canClickHand = (!isWaitingChoice && !isProcessingTurn);

        SpawnCards(myHand, myHandAreaUI, isClickable: canClickHand, isHidden: false);
        SpawnCards(myCap, myCapturedAreaUI, isClickable: false, isHidden: false);

        foreach (var card in fieldCards)
        {
            GameObject obj = Instantiate(cardPrefab, fieldAreaUI);
            bool canClickBoard = (isMyChoiceTurn && card.month == choiceMonth);
            obj.GetComponent<CardUI>().SetCard(card, isClickable: canClickBoard, isHidden: false);
        }

        SpawnCards(op1Hand, op1HandAreaUI, isClickable: false, isHidden: true);
        SpawnCards(op1Cap, op1CapturedAreaUI, isClickable: false, isHidden: false);
        SpawnCards(op2Hand, op2HandAreaUI, isClickable: false, isHidden: true);
        SpawnCards(op2Cap, op2CapturedAreaUI, isClickable: false, isHidden: false);
    }

    void SpawnCards(List<HwatuCard> cards, Transform parentUI, bool isClickable, bool isHidden)
    {
        foreach (var card in cards)
        {
            GameObject obj = Instantiate(cardPrefab, parentUI);
            obj.GetComponent<CardUI>().SetCard(card, isClickable, isHidden);
        }
    }
}