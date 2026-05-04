using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
using TMPro;

// 🚀 1. 쌍피 타입이 추가되었습니다!
public enum CardType : byte { 광, 열끝, 띠, 피, 쌍피 }

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

    [Header("Score UI")]
    public TextMeshProUGUI myScoreText;
    public TextMeshProUGUI op1ScoreText;
    public TextMeshProUGUI op2ScoreText;

    public bool isWaitingChoice = false;
    public ulong choosingPlayerId = 999;
    public int choiceMonth = -1;
    public HwatuCard pendingCard;
    public bool isDeckPhaseChoice = false;
    public bool isProcessingTurn = false;

    public void StartGame()
    {
        if (!IsServer) return;
        InitializeDeck(); ShuffleDeck(); DealCardsFor3Players();
    }

    // ==========================================
    // 🚀 2. 진짜 고스톱 규격 48장 생성기! (가장 공들인 로직입니다)
    // ==========================================
    void InitializeDeck()
    {
        deck.Clear();
        int[] gwangMonths = { 1, 3, 8, 11, 12 };
        int[] danMonths = { 1, 2, 3, 4, 5, 6, 7, 9, 10, 12 }; // 띠가 있는 월
        int[] yeolMonths = { 2, 4, 5, 6, 7, 8, 9, 10, 12 }; // 열끝이 있는 월
        int[] ssangpiMonths = { 11, 12 }; // 오동(똥)과 비에는 쌍피가 있죠!

        for (int m = 1; m <= 12; m++)
        {
            int id = 1;
            if (gwangMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.광, id++));
            if (yeolMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.열끝, id++));
            if (danMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.띠, id++));
            if (ssangpiMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.쌍피, id++));

            // 남는 자리는 전부 평범한 '피'로 꽉꽉 4장까지 채웁니다.
            while (id <= 4) deck.Add(new HwatuCard(m, CardType.피, id++));
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
        if (isProcessingTurn) return;

        if (isWaitingChoice && choosingPlayerId == myId) ChooseBoardCardServerRpc(clickedCard, myId);
        else if (!isWaitingChoice) PlayCardServerRpc(clickedCard, myId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PlayCardServerRpc(HwatuCard cardToPlay, ulong clientId)
    {
        if (isProcessingTurn) return;
        isProcessingTurn = true;

        if (clientId == 0) player1Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 1) player2Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 2) player3Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);

        StartCoroutine(PlayTurnRoutine(cardToPlay, clientId));
    }

    IEnumerator PlayTurnRoutine(HwatuCard cardToPlay, ulong clientId)
    {
        fieldCards.Add(cardToPlay);
        SyncState();
        yield return new WaitForSeconds(0.6f);

        fieldCards.Remove(cardToPlay);
        List<HwatuCard> matches = fieldCards.Where(c => c.month == cardToPlay.month).ToList();

        if (matches.Count == 2)
        {
            isWaitingChoice = true; choosingPlayerId = clientId; choiceMonth = cardToPlay.month;
            pendingCard = cardToPlay; isDeckPhaseChoice = false;
            isProcessingTurn = false;
            SyncState();
            yield break;
        }

        if (matches.Count > 0)
        {
            fieldCards.Add(cardToPlay);
            SyncState();
            ShowHighlightClientRpc(cardToPlay.month);
            fieldCards.Remove(cardToPlay);
            yield return new WaitForSeconds(0.4f);
        }

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
        isProcessingTurn = true;

        fieldCards.RemoveAll(c => c.month == chosenCard.month && c.id == chosenCard.id);
        AddCapture(clientId, pendingCard); AddCapture(clientId, chosenCard);

        SyncState();

        if (!isDeckPhaseChoice) StartCoroutine(DrawDeckRoutine(clientId));
        else { isProcessingTurn = false; SyncState(); }
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

        if (matches.Count == 2)
        {
            isWaitingChoice = true; choosingPlayerId = clientId; choiceMonth = drawnCard.month;
            pendingCard = drawnCard; isDeckPhaseChoice = true;
            isProcessingTurn = false;
            SyncState();
            yield break;
        }

        if (matches.Count > 0)
        {
            fieldCards.Add(drawnCard);
            SyncState();
            ShowHighlightClientRpc(drawnCard.month);
            fieldCards.Remove(drawnCard);
            yield return new WaitForSeconds(0.4f);
        }

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

    void SyncState()
    {
        UpdateUI();
        UpdateGameStateClientRpc(
            player1Hand.ToArray(), player2Hand.ToArray(), player3Hand.ToArray(), fieldCards.ToArray(),
            player1Captured.ToArray(), player2Captured.ToArray(), player3Captured.ToArray(),
            isWaitingChoice, choosingPlayerId, choiceMonth, isProcessingTurn
        );
    }

    [ClientRpc]
    void ShowHighlightClientRpc(int targetMonth)
    {
        CardUI[] allBoardCards = fieldAreaUI.GetComponentsInChildren<CardUI>();
        foreach (CardUI ui in allBoardCards)
        {
            if (ui.myCardInfo.month == targetMonth) ui.ShowHighlightEffect();
        }
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
        ClearOnlyCards(fieldAreaUI);
        ClearOnlyCards(myHandAreaUI);
        ClearOnlyCards(myCapturedAreaUI);
        ClearOnlyCards(op1HandAreaUI);
        ClearOnlyCards(op1CapturedAreaUI);
        ClearOnlyCards(op2HandAreaUI);
        ClearOnlyCards(op2CapturedAreaUI);

        ulong myId = NetworkManager.Singleton.LocalClientId;
        List<HwatuCard> myHand = null, op1Hand = null, op2Hand = null;
        List<HwatuCard> myCap = null, op1Cap = null, op2Cap = null;

        if (myId == 0) { myHand = player1Hand; op1Hand = player2Hand; op2Hand = player3Hand; myCap = player1Captured; op1Cap = player2Captured; op2Cap = player3Captured; }
        else if (myId == 1) { myHand = player2Hand; op1Hand = player3Hand; op2Hand = player1Hand; myCap = player2Captured; op1Cap = player3Captured; op2Cap = player1Captured; }
        else { myHand = player3Hand; op1Hand = player1Hand; op2Hand = player2Hand; myCap = player3Captured; op1Cap = player1Captured; op2Cap = player2Captured; }

        bool isMyChoiceTurn = (isWaitingChoice && choosingPlayerId == myId);
        bool canClickHand = (!isWaitingChoice && !isProcessingTurn);

        SpawnCards(myHand, myHandAreaUI, isClickable: canClickHand, isHidden: false);

        foreach (var card in fieldCards)
        {
            GameObject obj = Instantiate(cardPrefab, fieldAreaUI);
            bool canClickBoard = (isMyChoiceTurn && card.month == choiceMonth);
            obj.GetComponent<CardUI>().SetCard(card, isClickable: canClickBoard, isHidden: false);
        }

        SpawnCards(op1Hand, op1HandAreaUI, isClickable: false, isHidden: true);
        SpawnCards(op2Hand, op2HandAreaUI, isClickable: false, isHidden: true);

        SpawnCapturedCards(myCap, myCapturedAreaUI);
        SpawnCapturedCards(op1Cap, op1CapturedAreaUI);
        SpawnCapturedCards(op2Cap, op2CapturedAreaUI);

        // 🚀 [여기 추가!] UI가 업데이트될 때마다 내 점수 계산해서 콘솔창에 띄우기!
        // ==========================================
        int myScore = CalculateScore(myCap);
        int op1Score = CalculateScore(op1Cap);
        int op2Score = CalculateScore(op2Cap);

        if (myScoreText != null) myScoreText.text = $"내 점수: {myScore}점";
        if (op1ScoreText != null) op1ScoreText.text = $"점수: {op1Score}점";
        if (op2ScoreText != null) op2ScoreText.text = $"점수: {op2Score}점";

    }

    void ClearOnlyCards(Transform targetArea)
    {
        if (targetArea == null) return;
        CardUI[] cards = targetArea.GetComponentsInChildren<CardUI>();
        foreach (CardUI card in cards)
        {
            Destroy(card.gameObject);
        }
    }

    void SpawnCards(List<HwatuCard> cards, Transform parentUI, bool isClickable, bool isHidden)
    {
        foreach (var card in cards)
        {
            GameObject obj = Instantiate(cardPrefab, parentUI);
            obj.GetComponent<CardUI>().SetCard(card, isClickable, isHidden);
        }
    }

    void SpawnCapturedCards(List<HwatuCard> capCards, Transform capAreaUI)
    {
        if (capAreaUI == null) return;

        Transform rowGwang = capAreaUI.Find("Row_Gwang") ?? capAreaUI;
        Transform rowDan = capAreaUI.Find("Row_Dan") ?? capAreaUI;
        Transform rowPi = capAreaUI.Find("Row_Pi") ?? capAreaUI;

        foreach (var card in capCards)
        {
            Transform targetRow = rowPi;

            if (card.type == CardType.광 || card.type == CardType.열끝) targetRow = rowGwang;
            else if (card.type == CardType.띠) targetRow = rowDan;
            // 🚀 3. 피와 '쌍피'는 모두 아랫줄로 꽂아줍니다!

            GameObject obj = Instantiate(cardPrefab, targetRow);
            obj.GetComponent<CardUI>().SetCard(card, isClickable: false, isHidden: false);
        }
    }

    // 🚀 [새로 추가됨] 고스톱 점수 자동 계산기!
    // ==========================================
    public int CalculateScore(List<HwatuCard> capCards)
    {
        if (capCards == null || capCards.Count == 0) return 0;
        int score = 0;

        // 🎴 1. 광(Gwang) 점수 계산
        var gwangs = capCards.Where(c => c.type == CardType.광).ToList();
        int gwangCount = gwangs.Count;
        bool hasBiGwang = gwangs.Any(c => c.month == 12); // 비광 포함 여부

        if (gwangCount == 5) score += 15; // 오광
        else if (gwangCount == 4) score += 4; // 사광
        else if (gwangCount == 3) score += hasBiGwang ? 2 : 3; // 비삼광은 2점, 그냥 삼광은 3점

        // 🎴 2. 단(띠) 점수 계산
        var dans = capCards.Where(c => c.type == CardType.띠).ToList();
        if (dans.Count >= 5) score += (dans.Count - 4); // 띠 5장부터 1점, 이후 1장당 1점 추가

        int hongdan = dans.Count(c => c.month == 1 || c.month == 2 || c.month == 3);
        int cheongdan = dans.Count(c => c.month == 6 || c.month == 9 || c.month == 10);
        int chodan = dans.Count(c => c.month == 4 || c.month == 5 || c.month == 7);

        if (hongdan == 3) score += 3; // 홍단
        if (cheongdan == 3) score += 3; // 청단
        if (chodan == 3) score += 3; // 초단

        // 🎴 3. 열끝(멍텅구리) 점수 계산
        var yeols = capCards.Where(c => c.type == CardType.열끝).ToList();
        if (yeols.Count >= 5) score += (yeols.Count - 4); // 열끝 5장부터 1점, 이후 1장당 1점 추가

        int godori = yeols.Count(c => c.month == 2 || c.month == 4 || c.month == 8);
        if (godori == 3) score += 5; // 고도리 (새 5마리)

        // 🎴 4. 피 점수 계산 (쌍피는 2장으로 계산!)
        int piCount = capCards.Count(c => c.type == CardType.피);
        int ssangpiCount = capCards.Count(c => c.type == CardType.쌍피);
        int totalPi = piCount + (ssangpiCount * 2); // 쌍피는 곱하기 2!

        if (totalPi >= 10) score += (totalPi - 9); // 피 10장부터 1점, 이후 1장당 1점 추가

        return score;
    }

}