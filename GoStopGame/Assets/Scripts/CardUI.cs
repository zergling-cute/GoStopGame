using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class CardUI : MonoBehaviour
{
    public TextMeshProUGUI cardText;
    public Image backgroundImage;
    public Button cardButton;

    [Header("Card Images")]
    public Sprite backSprite;
    private Sprite defaultSprite;

    public HwatuCard myCardInfo;

    private void Awake()
    {
        backgroundImage = GetComponent<Image>();
        cardButton = GetComponent<Button>();
        cardText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (backgroundImage != null) defaultSprite = backgroundImage.sprite;
    }

    public void SetCard(HwatuCard card, bool isClickable, bool isHidden = false)
    {
        myCardInfo = card;
        string detailName = GetCardDetailName(card.month, card.type); // 🚀 진짜 이름 찾기
        gameObject.name = $"카드_{card.month}월_{detailName}";

        if (isHidden)
        {
            if (cardText != null) cardText.gameObject.SetActive(false);
            if (backSprite != null) { backgroundImage.sprite = backSprite; backgroundImage.color = Color.white; }
            else { backgroundImage.sprite = null; backgroundImage.color = new Color(0.7f, 0.1f, 0.1f); }
            if (cardButton != null) cardButton.interactable = false;
        }
        else
        {
            if (cardText != null)
            {
                cardText.gameObject.SetActive(true);
                cardText.text = $"{card.month}월\n{detailName}"; // 🚀 청단, 쌍피 등 출력!

                // 글자 색상 세팅 (쌍피는 보라색!)
                if (card.type == CardType.광) cardText.color = Color.red;
                else if (card.type == CardType.쌍피) cardText.color = new Color(0.6f, 0.1f, 0.8f);
                else if (card.type == CardType.피) cardText.color = Color.black;
                else cardText.color = new Color(0.1f, 0.4f, 0.8f); // 파란색
            }
            backgroundImage.sprite = defaultSprite;
            backgroundImage.color = Color.white;

            if (cardButton != null)
            {
                cardButton.interactable = isClickable;
                cardButton.onClick.RemoveAllListeners();
                if (isClickable) cardButton.onClick.AddListener(OnClickCard);
            }
        }
    }

    // ==========================================
    // 🚀 [핵심] 카드의 진짜 족보 이름을 찾아주는 마법의 함수!
    // ==========================================
    string GetCardDetailName(int m, CardType t)
    {
        if (t == CardType.광) return (m == 12) ? "비광" : "광";
        if (t == CardType.쌍피) return "쌍피";
        if (t == CardType.열끝) return (m == 2 || m == 4 || m == 8) ? "고도리" : "열끝";
        if (t == CardType.띠)
        {
            if (m == 1 || m == 2 || m == 3) return "홍단";
            if (m == 6 || m == 9 || m == 10) return "청단";
            if (m == 4 || m == 5 || m == 7) return "초단";
            return "띠"; // 12월 비띠
        }
        return "피";
    }

    void OnClickCard() { GoStopManager.Instance.OnCardClicked(myCardInfo); }

    public void ShowHighlightEffect() { StartCoroutine(HighlightRoutine()); }
    private IEnumerator HighlightRoutine()
    {
        transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
        backgroundImage.color = new Color(1f, 0.9f, 0.5f);
        yield return new WaitForSeconds(0.3f);
        transform.localScale = Vector3.one;
        backgroundImage.color = Color.white;
    }
}