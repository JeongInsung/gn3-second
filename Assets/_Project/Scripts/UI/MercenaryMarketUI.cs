using System.Collections.Generic;
using GN3.Characters;
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
            // MarketPanel(900x680) 안에서 제목/새로고침 버튼 아래 ~ 패널 하단까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -70f));

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

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
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

            CreatePortrait(row.transform, merc.Appearance, 48);

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

        private void CreatePortrait(Transform parent, CharacterAppearance appearance, int size)
        {
            var portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(LayoutElement));
            portraitGO.transform.SetParent(parent, false);

            var layout = portraitGO.GetComponent<LayoutElement>();
            layout.minWidth = size;
            layout.minHeight = size;
            layout.preferredWidth = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0;

            var background = AddPortraitLayer(portraitGO.transform, null);
            background.color = new Color(1f, 1f, 1f, 0.08f);

            // 뒤에서 앞으로 겹쳐 그림: 몸통 -> 다리 -> 팔 -> 무기 -> 머리
            AddPortraitLayer(portraitGO.transform, appearance.Body);
            AddPortraitLayer(portraitGO.transform, appearance.Leg);
            AddPortraitLayer(portraitGO.transform, appearance.Arm);
            AddPortraitLayer(portraitGO.transform, appearance.Weapon);
            AddPortraitLayer(portraitGO.transform, appearance.Head);
        }

        private Image AddPortraitLayer(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Part", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            return image;
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
