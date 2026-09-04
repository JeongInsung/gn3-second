using System.Collections.Generic;
using GN3.Mercenaries;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public class MercenaryMarketUI : MonoBehaviour
    {
        [SerializeField] private Transform listContainer;
        [SerializeField] private Button refreshButton;
        [SerializeField] private int marketSize = 5;
        [SerializeField] private int minLevel = 1;
        [SerializeField] private int maxLevel = 3;

        private MercenaryMarket _market;
        private readonly List<GameObject> _cardRows = new List<GameObject>();
        private Font _font;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var classPool = new List<MercenaryClassSO>(Resources.LoadAll<MercenaryClassSO>("MercenaryClasses"));
            _market = new MercenaryMarket(classPool, marketSize, minLevel, maxLevel);

            if (refreshButton != null)
                refreshButton.onClick.AddListener(RefreshMarket);
        }

        private void Start()
        {
            RefreshMarket();
        }

        private void RefreshMarket()
        {
            foreach (var row in _cardRows)
                Destroy(row);
            _cardRows.Clear();

            foreach (var merc in _market.Refresh())
                _cardRows.Add(CreateCard(merc));
        }

        private GameObject CreateCard(Mercenary merc)
        {
            var row = new GameObject(merc.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);

            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = 64;
            layoutElement.preferredHeight = 64;

            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(16, 16, 8, 8);
            hLayout.spacing = 12;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            var stats = merc.CurrentStats;
            string info = $"{merc.Name}   {merc.Class.ClassName} Lv.{merc.Level}    ATK {stats.Attack} / DEF {stats.Defense} / HP {stats.MaxHealth}";
            var infoText = CreateText(row.transform, info, 20, TextAnchor.MiddleLeft);
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 100;

            var hireButtonGO = new GameObject("HireButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            hireButtonGO.transform.SetParent(row.transform, false);
            hireButtonGO.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            var hireLayout = hireButtonGO.GetComponent<LayoutElement>();
            hireLayout.minWidth = 90;
            hireLayout.minHeight = 40;
            hireLayout.flexibleWidth = 0;

            var hireText = CreateText(hireButtonGO.transform, "고용", 18, TextAnchor.MiddleCenter);
            var hireTextRect = hireText.GetComponent<RectTransform>();
            hireTextRect.anchorMin = Vector2.zero;
            hireTextRect.anchorMax = Vector2.one;
            hireTextRect.offsetMin = Vector2.zero;
            hireTextRect.offsetMax = Vector2.zero;

            hireButtonGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (!PlayerParty.Instance.TryAdd(merc))
                {
                    Debug.Log($"{merc.Name} 고용 실패: 파티 정원이 가득 찼습니다.");
                    return;
                }

                Debug.Log($"{merc.Name} 고용됨 ({merc.Class.ClassName} Lv.{merc.Level})");
                _cardRows.Remove(row);
                Destroy(row);
            });

            return row;
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
