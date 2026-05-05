using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
using TMPro;

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
    private void Awake() { Instance = this; Application.targetFrameRate = 60; Application.runInBackground = true; }

    [Header("Deck & Field")]
    public List<HwatuCard> deck = new List<HwatuCard>();
    public List<HwatuCard> fieldCards = new List<HwatuCard>();

    [Header("Players Hands & Captures")]
    public List<HwatuCard> player1Hand = new List<HwatuCard>(); public List<HwatuCard> player1Captured = new List<HwatuCard>();
    public List<HwatuCard> player2Hand = new List<HwatuCard>(); public List<HwatuCard> player2Captured = new List<HwatuCard>();
    public List<HwatuCard> player3Hand = new List<HwatuCard>(); public List<HwatuCard> player3Captured = new List<HwatuCard>();

    [Header("UI Areas")]
    public GameObject cardPrefab;
    public Transform fieldAreaUI; public Transform myHandAreaUI; public Transform myCapturedAreaUI;
    public Transform op1HandAreaUI; public Transform op1CapturedAreaUI; public Transform op2HandAreaUI; public Transform op2CapturedAreaUI;

    [Header("Score & System UI")]
    public TextMeshProUGUI myScoreText; public TextMeshProUGUI op1ScoreText; public TextMeshProUGUI op2ScoreText;
    public TextMeshProUGUI turnInfoText; public TextMeshProUGUI eventText; public GameObject goStopPanel;

    [Header("Game State")]
    public ulong currentTurnId = 0;
    public bool isWaitingChoice = false; public ulong choosingPlayerId = 999; public int choiceMonth = -1;
    public HwatuCard pendingCard; public bool isDeckPhaseChoice = false; public bool isProcessingTurn = false;
    public bool isWaitingGoStop = false; public bool isGameOver = false;
    public int[] previousScores = new int[3]; public int[] goCounts = new int[3];

    public HwatuCard currentHandCard; public bool hasHandCard = false;
    public HwatuCard currentDeckCard; public bool hasDeckCard = false;
    public HwatuCard handTargetCard; public bool hasHandTarget = false;
    public HwatuCard deckTargetCard; public bool hasDeckTarget = false;

    public void StartGame()
    {
        if (!IsServer) return;
        currentTurnId = 0; isGameOver = false; isWaitingGoStop = false;
        previousScores = new int[3] { 0, 0, 0 }; goCounts = new int[3] { 0, 0, 0 };
        if (eventText != null) eventText.text = "";
        InitializeDeck(); ShuffleDeck(); DealCardsFor3Players();
    }

    void InitializeDeck()
    {
        deck.Clear();
        int[] gwangMonths = { 1, 3, 8, 11, 12 }; int[] danMonths = { 1, 2, 3, 4, 5, 6, 7, 9, 10, 12 };
        int[] yeolMonths = { 2, 4, 5, 6, 7, 8, 9, 10, 12 }; int[] ssangpiMonths = { 11, 12 };
        for (int m = 1; m <= 12; m++)
        {
            int id = 1;
            if (gwangMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.광, id++));
            if (yeolMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.열끝, id++));
            if (danMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.띠, id++));
            if (ssangpiMonths.Contains(m)) deck.Add(new HwatuCard(m, CardType.쌍피, id++));
            while (id <= 4) deck.Add(new HwatuCard(m, CardType.피, id++));
        }
    }

    void ShuffleDeck() { for (int i = 0; i < deck.Count; i++) { int r = Random.Range(i, deck.Count); HwatuCard temp = deck[i]; deck[i] = deck[r]; deck[r] = temp; } }
    void DealCardsFor3Players()
    {
        player1Hand.Clear(); player2Hand.Clear(); player3Hand.Clear(); fieldCards.Clear();
        player1Captured.Clear(); player2Captured.Clear(); player3Captured.Clear();
        DrawCards(player1Hand, 7); DrawCards(player2Hand, 7); DrawCards(player3Hand, 7); DrawCards(fieldCards, 6);
        player1Hand = player1Hand.OrderBy(c => c.month).ToList(); player2Hand = player2Hand.OrderBy(c => c.month).ToList(); player3Hand = player3Hand.OrderBy(c => c.month).ToList();
        SyncState();
    }
    void DrawCards(List<HwatuCard> target, int count) { for (int i = 0; i < count; i++) { if (deck.Count > 0) { target.Add(deck[0]); deck.RemoveAt(0); } } }

    public void OnCardClicked(HwatuCard clickedCard)
    {
        ulong myId = NetworkManager.Singleton.LocalClientId;
        if (isGameOver || isWaitingGoStop || currentTurnId != myId || isProcessingTurn) return;
        if (isWaitingChoice && choosingPlayerId == myId) ChooseBoardCardServerRpc(clickedCard, myId);
        else if (!isWaitingChoice) PlayCardServerRpc(clickedCard, myId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PlayCardServerRpc(HwatuCard cardToPlay, ulong clientId)
    {
        if (isProcessingTurn || currentTurnId != clientId) return;
        isProcessingTurn = true;
        hasHandCard = false; hasDeckCard = false; hasHandTarget = false; hasDeckTarget = false;

        if (clientId == 0) player1Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 1) player2Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);
        else if (clientId == 2) player3Hand.RemoveAll(c => c.month == cardToPlay.month && c.id == cardToPlay.id);

        StartCoroutine(PlayTurnRoutine(cardToPlay, clientId));
    }

    IEnumerator PlayTurnRoutine(HwatuCard cardToPlay, ulong clientId)
    {
        hasHandCard = true; currentHandCard = cardToPlay;
        fieldCards.Add(cardToPlay);
        fieldCards = fieldCards.OrderBy(c => c.month).ToList();
        SyncState();
        yield return new WaitForSeconds(0.6f);

        int matchCount = fieldCards.Count(c => c.month == cardToPlay.month);
        if (matchCount >= 2) ShowHighlightClientRpc(cardToPlay.month);

        if (matchCount == 3)
        {
            isWaitingChoice = true; choosingPlayerId = clientId; choiceMonth = cardToPlay.month;
            pendingCard = cardToPlay; isDeckPhaseChoice = false; isProcessingTurn = false;
            SyncState(); yield break;
        }

        StartCoroutine(DrawDeckRoutine(clientId));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ChooseBoardCardServerRpc(HwatuCard chosenCard, ulong clientId)
    {
        if (!isWaitingChoice || choosingPlayerId != clientId || chosenCard.month != choiceMonth) return;

        // 🚀 [초강력 방어코드] 혹시라도 자기가 방금 낸 카드(pending)를 눌렀다면 무시하고 튕겨냄!
        if (chosenCard.id == pendingCard.id) return;

        isWaitingChoice = false; isProcessingTurn = true;

        if (!isDeckPhaseChoice)
        {
            hasHandTarget = true; handTargetCard = chosenCard;
            StartCoroutine(DrawDeckRoutine(clientId));
        }
        else
        {
            hasDeckTarget = true; deckTargetCard = chosenCard;
            EndTurnEvaluation();
        }
    }

    IEnumerator DrawDeckRoutine(ulong clientId)
    {
        yield return new WaitForSeconds(0.4f);
        if (deck.Count == 0) { EndTurnEvaluation(); yield break; }

        HwatuCard drawnCard = deck[0]; deck.RemoveAt(0);
        hasDeckCard = true; currentDeckCard = drawnCard;

        fieldCards.Add(drawnCard);
        fieldCards = fieldCards.OrderBy(c => c.month).ToList();
        SyncState();

        int matchCount = fieldCards.Count(c => c.month == drawnCard.month);
        if (matchCount >= 2) ShowHighlightClientRpc(drawnCard.month);

        yield return new WaitForSeconds(0.6f);

        if (matchCount == 3)
        {
            isWaitingChoice = true; choosingPlayerId = clientId; choiceMonth = drawnCard.month;
            pendingCard = drawnCard; isDeckPhaseChoice = true; isProcessingTurn = false;
            SyncState(); yield break;
        }

        EndTurnEvaluation();
    }

    void EndTurnEvaluation()
    {
        int hMonth = hasHandCard ? currentHandCard.month : -1;
        int dMonth = hasDeckCard ? currentDeckCard.month : -1;
        int hCount = fieldCards.Count(c => c.month == hMonth);
        int dCount = fieldCards.Count(c => c.month == dMonth);

        bool isPpeok = false; bool isJjok = false; bool isTtadak = false;
        bool atePpeok1 = false; bool atePpeok2 = false;

        List<HwatuCard> cardsToCapture = new List<HwatuCard>();

        if (hasHandCard && hasDeckCard && hMonth == dMonth)
        {
            if (hCount == 2) { isJjok = true; cardsToCapture.AddRange(fieldCards.Where(c => c.month == hMonth)); }
            else if (hCount == 3) { isPpeok = true; }
            else if (hCount == 4) { isTtadak = true; cardsToCapture.AddRange(fieldCards.Where(c => c.month == hMonth)); }
        }
        else
        {
            if (hCount == 2) cardsToCapture.AddRange(fieldCards.Where(c => c.month == hMonth));
            else if (hCount == 3 && hasHandTarget) { cardsToCapture.Add(currentHandCard); cardsToCapture.Add(handTargetCard); }
            else if (hCount == 4) { atePpeok1 = true; cardsToCapture.AddRange(fieldCards.Where(c => c.month == hMonth)); }

            if (dCount == 2) cardsToCapture.AddRange(fieldCards.Where(c => c.month == dMonth));
            else if (dCount == 3 && hasDeckTarget) { cardsToCapture.Add(currentDeckCard); cardsToCapture.Add(deckTargetCard); }
            else if (dCount == 4) { atePpeok2 = true; cardsToCapture.AddRange(fieldCards.Where(c => c.month == dMonth)); }
        }

        foreach (var c in cardsToCapture)
        {
            fieldCards.RemoveAll(f => f.month == c.month && f.id == c.id);
            AddCapture(currentTurnId, c);
        }

        bool isSsak = (fieldCards.Count == 0);

        if (isPpeok) ShowEventTextClientRpc("<color=red>앗! 쌌다! (뻑)</color>");
        else if (isJjok) StealPi(currentTurnId, "쪽");
        else if (isTtadak) StealPi(currentTurnId, "따닥");
        else if (atePpeok1 || atePpeok2) StealPi(currentTurnId, "뻑 먹음");

        if (isSsak && !isPpeok) StealPi(currentTurnId, "싹쓸이");

        isProcessingTurn = false;
        CheckGoStopAndEndTurn();
    }

    void CheckGoStopAndEndTurn()
    {
        List<HwatuCard> myCaps = (currentTurnId == 0) ? player1Captured : (currentTurnId == 1) ? player2Captured : player3Captured;
        int currentScore = CalculateScore(myCaps); int myPrevScore = previousScores[currentTurnId];
        if (currentScore >= 3 && currentScore > myPrevScore) { previousScores[currentTurnId] = currentScore; isWaitingGoStop = true; SyncState(); }
        else { previousScores[currentTurnId] = currentScore; ChangeTurn(); }
    }

    void StealPi(ulong winnerId, string eventName)
    {
        ShowEventTextClientRpc($"<color=yellow>★ {eventName}! ★</color>\n피를 1장씩 뺏어옵니다!");
        for (ulong i = 0; i < 3; i++)
        {
            if (i == winnerId) continue;
            List<HwatuCard> targetCaps = (i == 0) ? player1Captured : (i == 1) ? player2Captured : player3Captured;
            HwatuCard? piToSteal = null;
            var normalPis = targetCaps.Where(c => c.type == CardType.피).ToList();
            if (normalPis.Count > 0) piToSteal = normalPis[0];
            else { var ssangPis = targetCaps.Where(c => c.type == CardType.쌍피).ToList(); if (ssangPis.Count > 0) piToSteal = ssangPis[0]; }
            if (piToSteal.HasValue) { targetCaps.Remove(piToSteal.Value); AddCapture(winnerId, piToSteal.Value); }
        }
        SyncState();
    }

    [ClientRpc] void ShowEventTextClientRpc(string message) { StartCoroutine(EventTextRoutine(message)); }

    // 🚀 지난번에 수정했던 이벤트 텍스트 4초 노출 유지!
    IEnumerator EventTextRoutine(string message) { if (eventText != null) { eventText.text = message; yield return new WaitForSeconds(4.0f); eventText.text = ""; } }

    public void OnClickGoButton() { DeclareGoStopServerRpc(NetworkManager.Singleton.LocalClientId, true); }
    public void OnClickStopButton() { DeclareGoStopServerRpc(NetworkManager.Singleton.LocalClientId, false); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void DeclareGoStopServerRpc(ulong clientId, bool isGo)
    {
        if (!isWaitingGoStop || currentTurnId != clientId) return;
        isWaitingGoStop = false;
        if (isGo) { goCounts[clientId]++; ChangeTurn(); }
        else { isGameOver = true; EndGameClientRpc(clientId); }
    }

    void ChangeTurn() { currentTurnId = (currentTurnId + 1) % 3; SyncState(); }
    void AddCapture(ulong clientId, HwatuCard card) { if (clientId == 0) player1Captured.Add(card); else if (clientId == 1) player2Captured.Add(card); else if (clientId == 2) player3Captured.Add(card); }

    // 🚀 [동기화 핵심 수정] 서버가 손님들에게 방금 낸 카드(pendingCard)를 통신으로 알려줍니다!
    void SyncState()
    {
        UpdateUI();
        UpdateGameStateClientRpc(
            player1Hand.ToArray(), player2Hand.ToArray(), player3Hand.ToArray(), fieldCards.ToArray(),
            player1Captured.ToArray(), player2Captured.ToArray(), player3Captured.ToArray(),
            isWaitingChoice, choosingPlayerId, choiceMonth, isProcessingTurn, currentTurnId, isWaitingGoStop, isGameOver,
            pendingCard // 🚀 추가됨!
        );
    }

    [ClientRpc]
    void UpdateGameStateClientRpc(
        HwatuCard[] p1, HwatuCard[] p2, HwatuCard[] p3, HwatuCard[] field,
        HwatuCard[] cap1, HwatuCard[] cap2, HwatuCard[] cap3,
        bool wait, ulong chooser, int month, bool processing, ulong turn, bool isWaitGoStop, bool isOver,
        HwatuCard syncedPendingCard) // 🚀 추가됨!
    {
        if (IsServer) return;
        player1Hand = p1.ToList(); player2Hand = p2.ToList(); player3Hand = p3.ToList(); fieldCards = field.ToList();
        player1Captured = cap1.ToList(); player2Captured = cap2.ToList(); player3Captured = cap3.ToList();
        isWaitingChoice = wait; choosingPlayerId = chooser; choiceMonth = month; isProcessingTurn = processing;
        currentTurnId = turn; isWaitingGoStop = isWaitGoStop; isGameOver = isOver;

        pendingCard = syncedPendingCard; // 🚀 클라이언트에도 방금 낸 카드가 뭔지 세팅!
        UpdateUI();
    }

    void UpdateUI()
    {
        ClearOnlyCards(fieldAreaUI); ClearOnlyCards(myHandAreaUI); ClearOnlyCards(myCapturedAreaUI); ClearOnlyCards(op1HandAreaUI); ClearOnlyCards(op1CapturedAreaUI); ClearOnlyCards(op2HandAreaUI); ClearOnlyCards(op2CapturedAreaUI);
        ulong myId = NetworkManager.Singleton.LocalClientId; List<HwatuCard> myHand, op1Hand, op2Hand, myCap, op1Cap, op2Cap;
        if (myId == 0) { myHand = player1Hand; op1Hand = player2Hand; op2Hand = player3Hand; myCap = player1Captured; op1Cap = player2Captured; op2Cap = player3Captured; }
        else if (myId == 1) { myHand = player2Hand; op1Hand = player3Hand; op2Hand = player1Hand; myCap = player2Captured; op1Cap = player3Captured; op2Cap = player1Captured; }
        else { myHand = player3Hand; op1Hand = player1Hand; op2Hand = player2Hand; myCap = player3Captured; op1Cap = player1Captured; op2Cap = player2Captured; }

        bool isMyChoiceTurn = (isWaitingChoice && choosingPlayerId == myId);
        bool canClickHand = (!isWaitingChoice && !isProcessingTurn && currentTurnId == myId && !isWaitingGoStop && !isGameOver);
        SpawnCards(myHand, myHandAreaUI, isClickable: canClickHand, isHidden: false);

        Dictionary<int, int> boardMonthCounts = new Dictionary<int, int>();
        foreach (var c in fieldCards)
        {
            if (!boardMonthCounts.ContainsKey(c.month)) boardMonthCounts[c.month] = 0;
            boardMonthCounts[c.month]++;
        }

        int currentActionMonth = -1;
        if (isWaitingChoice) currentActionMonth = choiceMonth;
        else if (isProcessingTurn)
        {
            if (hasDeckCard) currentActionMonth = currentDeckCard.month;
            else if (hasHandCard) currentActionMonth = currentHandCard.month;
        }

        foreach (var card in fieldCards)
        {
            GameObject obj = Instantiate(cardPrefab, fieldAreaUI);
            bool canClickBoard = (isMyChoiceTurn && card.month == choiceMonth && card.id != pendingCard.id && !isWaitingGoStop && !isGameOver);
            CardUI ui = obj.GetComponent<CardUI>();
            ui.SetCard(card, isClickable: canClickBoard, isHidden: false);

            bool isConfirmedPpeok = (boardMonthCounts[card.month] == 3 && card.month != currentActionMonth);
            if (isConfirmedPpeok && ui.backgroundImage != null)
            {
                ui.backgroundImage.color = new Color(1f, 0.8f, 0.8f);
            }
        }

        SpawnCards(op1Hand, op1HandAreaUI, isClickable: false, isHidden: true); SpawnCards(op2Hand, op2HandAreaUI, isClickable: false, isHidden: true);
        SpawnCapturedCards(myCap, myCapturedAreaUI); SpawnCapturedCards(op1Cap, op1CapturedAreaUI); SpawnCapturedCards(op2Cap, op2CapturedAreaUI);

        int myScore = CalculateScore(myCap); int op1Score = CalculateScore(op1Cap); int op2Score = CalculateScore(op2Cap);
        if (myScoreText != null) myScoreText.text = $"내 점수: {myScore}점"; if (op1ScoreText != null) op1ScoreText.text = $"점수: {op1Score}점"; if (op2ScoreText != null) op2ScoreText.text = $"점수: {op2Score}점";
        if (turnInfoText != null && !isGameOver) { if (currentTurnId == myId) turnInfoText.text = isWaitingGoStop ? "<color=yellow>고/스톱을 결정해주세요!</color>" : "<color=green>★ 내 차례입니다! ★</color>"; else turnInfoText.text = $"플레이어 {currentTurnId}의 차례 대기 중..."; }
        if (goStopPanel != null) goStopPanel.SetActive(isWaitingGoStop && currentTurnId == myId);
    }

    void ClearOnlyCards(Transform targetArea) { if (targetArea == null) return; CardUI[] cards = targetArea.GetComponentsInChildren<CardUI>(); foreach (CardUI card in cards) Destroy(card.gameObject); }
    void SpawnCards(List<HwatuCard> cards, Transform parentUI, bool isClickable, bool isHidden) { foreach (var card in cards) { GameObject obj = Instantiate(cardPrefab, parentUI); obj.GetComponent<CardUI>().SetCard(card, isClickable, isHidden); } }
    void SpawnCapturedCards(List<HwatuCard> capCards, Transform capAreaUI)
    {
        if (capAreaUI == null) return; Transform rowGwang = capAreaUI.Find("Row_Gwang") ?? capAreaUI; Transform rowDan = capAreaUI.Find("Row_Dan") ?? capAreaUI; Transform rowPi = capAreaUI.Find("Row_Pi") ?? capAreaUI;
        foreach (var card in capCards) { Transform targetRow = rowPi; if (card.type == CardType.광 || card.type == CardType.열끝) targetRow = rowGwang; else if (card.type == CardType.띠) targetRow = rowDan; GameObject obj = Instantiate(cardPrefab, targetRow); obj.GetComponent<CardUI>().SetCard(card, isClickable: false, isHidden: false); }
    }

    public int CalculateScore(List<HwatuCard> capCards)
    {
        if (capCards == null || capCards.Count == 0) return 0; int score = 0;
        var gwangs = capCards.Where(c => c.type == CardType.광).ToList(); int gwangCount = gwangs.Count; bool hasBiGwang = gwangs.Any(c => c.month == 12);
        if (gwangCount == 5) score += 15; else if (gwangCount == 4) score += 4; else if (gwangCount == 3) score += hasBiGwang ? 2 : 3;
        var dans = capCards.Where(c => c.type == CardType.띠).ToList(); if (dans.Count >= 5) score += (dans.Count - 4);
        int hongdan = dans.Count(c => c.month == 1 || c.month == 2 || c.month == 3); int cheongdan = dans.Count(c => c.month == 6 || c.month == 9 || c.month == 10); int chodan = dans.Count(c => c.month == 4 || c.month == 5 || c.month == 7);
        if (hongdan == 3) score += 3; if (cheongdan == 3) score += 3; if (chodan == 3) score += 3;
        var yeols = capCards.Where(c => c.type == CardType.열끝).ToList(); if (yeols.Count >= 5) score += (yeols.Count - 4);
        int godori = yeols.Count(c => c.month == 2 || c.month == 4 || c.month == 8); if (godori == 3) score += 5;
        int piCount = capCards.Count(c => c.type == CardType.피); int ssangpiCount = capCards.Count(c => c.type == CardType.쌍피); int totalPi = piCount + (ssangpiCount * 2);
        if (totalPi >= 10) score += (totalPi - 9); return score;
    }

    [ClientRpc] void ShowHighlightClientRpc(int targetMonth) { CardUI[] allBoardCards = fieldAreaUI.GetComponentsInChildren<CardUI>(); foreach (CardUI ui in allBoardCards) { if (ui.myCardInfo.month == targetMonth) ui.ShowHighlightEffect(); } }

    // 🚀 지난번에 만들었던 박(피박, 광박, 고박) 정산 로직 유지!
    [ClientRpc]
    void EndGameClientRpc(ulong winnerId)
    {
        isGameOver = true; isWaitingGoStop = false; UpdateUI();
        int winnerScore = CalculateScore(GetCapturedList(winnerId)); int winnerPi = GetPiCount(winnerId); int winnerGwang = GetGwangCount(winnerId);
        ulong loser1 = (winnerId + 1) % 3; ulong loser2 = (winnerId + 2) % 3;
        int loser1Pay = winnerScore; int loser2Pay = winnerScore;

        string resultMsg = $"<color=red>★★ 플레이어 {winnerId} 승리! (기본 {winnerScore}점) ★★</color>\n";
        bool l1Pibak = false, l2Pibak = false;
        if (winnerPi >= 10) { if (GetPiCount(loser1) < 6) { loser1Pay *= 2; l1Pibak = true; } if (GetPiCount(loser2) < 6) { loser2Pay *= 2; l2Pibak = true; } }

        bool l1Gwangbak = false, l2Gwangbak = false;
        if (winnerGwang >= 3) { if (GetGwangCount(loser1) == 0) { loser1Pay *= 2; l1Gwangbak = true; } if (GetGwangCount(loser2) == 0) { loser2Pay *= 2; l2Gwangbak = true; } }

        bool isGobak = false; ulong gobakPlayer = 999;
        if (goCounts[loser1] > 0) { isGobak = true; gobakPlayer = loser1; } else if (goCounts[loser2] > 0) { isGobak = true; gobakPlayer = loser2; }

        if (isGobak)
        {
            if (gobakPlayer == loser1) { loser1Pay += loser2Pay; loser2Pay = 0; resultMsg += $"<color=yellow>[플레이어 {loser1} 고박(독박)! 모든 점수 덤터기!]</color>\n"; }
            else { loser2Pay += loser1Pay; loser1Pay = 0; resultMsg += $"<color=yellow>[플레이어 {loser2} 고박(독박)! 모든 점수 덤터기!]</color>\n"; }
        }
        else
        {
            if (l1Pibak) resultMsg += $"플레이어 {loser1} 피박! "; if (l1Gwangbak) resultMsg += $"플레이어 {loser1} 광박! ";
            if (l2Pibak) resultMsg += $"플레이어 {loser2} 피박! "; if (l2Gwangbak) resultMsg += $"플레이어 {loser2} 광박! ";
            resultMsg += "\n";
        }
        resultMsg += $"\n<color=white>최종 잃은 점수: [플레이어 {loser1}: -{loser1Pay}점] / [플레이어 {loser2}: -{loser2Pay}점]</color>";
        if (turnInfoText != null) turnInfoText.text = resultMsg;
    }

    List<HwatuCard> GetCapturedList(ulong id) { return id == 0 ? player1Captured : id == 1 ? player2Captured : player3Captured; }
    int GetPiCount(ulong id) { var caps = GetCapturedList(id); return caps.Count(c => c.type == CardType.피) + (caps.Count(c => c.type == CardType.쌍피) * 2); }
    int GetGwangCount(ulong id) { return GetCapturedList(id).Count(c => c.type == CardType.광); }
}