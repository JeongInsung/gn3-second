using System.Collections.Generic;
using GN3.Economy;
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
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -80f)); // 새로고침 버튼(아래 끝 약 67px)과 띄움

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

        /// <summary>불러오기 직후처럼 밖에서 시장을 새로 뽑을 때.</summary>
        public void RefreshFromOutside()
        {
            if (_market != null) RefreshMarket(); // 아직 한 번도 안 열려 Awake 전이면 열 때 Start에서 뽑는다
        }

        private void RefreshMarket()
        {
            foreach (var row in _cardRows)
                Destroy(row);
            _cardRows.Clear();

            // 길드 단계에 따라 시장 레벨 범위·최고 등급이 바뀐다.
            var rank = Guild.Current;
            _market.MinLevel = rank.MarketMinLevel;
            _market.MaxLevel = rank.MarketMaxLevel;
            _market.MaxGrade = rank.MaxMercGrade;

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

            // 줄에는 등급·이름·직업·레벨·전투력만. 성격·패시브·능력치는 줄을 클릭하면 열리는 캐릭터 창에서 본다.
            var nameText = CreateText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}", 22, TextAnchor.MiddleLeft);
            nameText.fontStyle = FontStyle.Bold;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.gameObject.AddComponent<LayoutElement>().minWidth = 220;

            var classText = CreateText(row.transform, $"{merc.Class.ClassName}  Lv.{merc.Level}", 18, TextAnchor.MiddleLeft);
            classText.horizontalOverflow = HorizontalWrapMode.Overflow;
            classText.gameObject.AddComponent<LayoutElement>().minWidth = 140;

            var powerText = CreateText(row.transform, $"전투력 {merc.CombatPower}", 18, TextAnchor.MiddleLeft);
            powerText.color = UITheme.TitleText;
            powerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var powerLayout = powerText.gameObject.AddComponent<LayoutElement>();
            powerLayout.minWidth = 140;
            powerLayout.flexibleWidth = 1; // 남는 폭을 차지해 고용 버튼을 오른쪽 끝으로 민다

            RowClickHandler.Attach(row, () => MercenaryInfoPanel.ShowGlobal(merc));

            var hireButtonGO = new GameObject("HireButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            hireButtonGO.transform.SetParent(row.transform, false);
            hireButtonGO.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            var hireLayout = hireButtonGO.GetComponent<LayoutElement>();
            hireLayout.minWidth = 130;
            hireLayout.minHeight = 40;
            hireLayout.flexibleWidth = 0;

            int cost = Pricing.HireCost(merc);
            var hireText = CreateText(hireButtonGO.transform, $"고용 ({cost}G)", 18, TextAnchor.MiddleCenter);
            var hireTextRect = hireText.GetComponent<RectTransform>();
            hireTextRect.anchorMin = Vector2.zero;
            hireTextRect.anchorMax = Vector2.one;
            hireTextRect.offsetMin = Vector2.zero;
            hireTextRect.offsetMax = Vector2.zero;

            hireButtonGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                var party = PlayerParty.Instance;
                if (party.Members.Count >= party.MaxSize)
                {
                    ToastLog.Show($"파티 정원({party.MaxSize}명)이 가득 찼습니다.");
                    return;
                }
                if (!Wallet.TrySpend(cost))
                {
                    ToastLog.Show($"골드가 부족합니다 (필요 {cost}G, 보유 {Wallet.Gold}G)");
                    return;
                }
                if (!party.TryAdd(merc))
                {
                    Wallet.Add(cost); // 이미 고용된 용병 등으로 실패하면 돌려준다
                    return;
                }
                ToastLog.Show($"{merc.Name} 고용 (-{cost}G)");

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
