using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.Traits;
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
            // MarketPanel(1200x780) 안에서 제목/새로고침 버튼 아래 ~ 패널 하단까지의 고정 영역
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

            CharacterPortraitUI.Create(row.transform, merc.Appearance, 48);

            var stats = merc.CurrentStats;
            var personalityMod = PersonalityTable.Get(merc.Personality);
            var passives = ClassPassiveFactory.Create(merc.Class.Kind, merc.HasRarePassive);
            string rareMark = merc.HasRarePassive ? " (레어)" : "";

            string nameInfo = $"{merc.Name}   {merc.Class.ClassName} Lv.{merc.Level}";
            var nameText = CreateText(row.transform, nameInfo, 20, TextAnchor.MiddleLeft);
            var nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1;
            nameLayout.minWidth = 100;

            CreateTaggedLabel(row.transform, $"[{personalityMod.Label}]", 20, personalityMod.Description);

            if (passives.Count > 0)
                CreateTaggedLabel(row.transform, $"<{passives[0].Name}{rareMark}>", 20, passives[0].Description);

            string statsInfo = $"ATK {stats.Attack} / DEF {stats.Defense} / HP {stats.MaxHealth} / SPD {stats.MoveSpeed}";
            var statsText = CreateText(row.transform, statsInfo, 20, TextAnchor.MiddleLeft);
            statsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            statsText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0;

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

        private void CreateTaggedLabel(Transform parent, string label, int fontSize, string tooltip)
        {
            var text = CreateText(parent, label, fontSize, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 0;
            var trigger = text.gameObject.AddComponent<TooltipTrigger>();
            trigger.Text = tooltip;
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
