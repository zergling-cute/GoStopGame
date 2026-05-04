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

        // 🚀 true를 넣어서 꺼져있거나 숨겨진 텍스트도 샅샅이 뒤져서 찾아냅니다!
        cardText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (backgroundImage != null) defaultSprite = backgroundImage.sprite;
    }

    public void SetCard(HwatuCard card, bool isClickable, bool isHidden = false)
    {
        myCardInfo = card;

        // 🚨 [진단 1] 게임이 시작되면 카드가 자기 '이름'을 바꿉니다! (하이어라키 창 확인용)
        gameObject.name = $"카드_{card.month}월_{card.type}";

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
                cardText.text = $"{card.month}월\n{card.type}";
                if (card.type == CardType.광) cardText.color = Color.red;
                else if (card.type == CardType.피) cardText.color = Color.black;
                else cardText.color = Color.blue;
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

    void OnClickCard()
    {
        // 🚨 [진단 2] 클릭이 먹히는지 확인용 콘솔 메시지!
        Debug.Log($"[마우스 클릭 감지 성공!] {myCardInfo.month}월 카드를 눌렀습니다!");
        GoStopManager.Instance.OnCardClicked(myCardInfo);
    }

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