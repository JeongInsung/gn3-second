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
        private readonly List<Button> _hireButtons = new List<Button>();
        private Text _capacityText; // 왼쪽 위 "파티 정원 2/3"
        private Font _font;

        private static readonly Color FullColor = new Color(0.94f, 0.45f, 0.38f, 1f);

        private void Awake()
        {
            var panel = (RectTransform)listContainer.parent; // MarketPanel
            UITheme.StyleWindowTitle(panel.Find("Title")?.GetComponent<Text>()); // 시장·퀘스트·파티 제목 줄을 같게
            // MarketPanel(1200x780) 안에서 제목/새로고침 버튼 아래 ~ 패널 하단까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -80f)); // 새로고침 버튼(아래 끝 약 67px)과 띄움

            _font = UITheme.Font;

            // 파티 정원: 제목 줄 왼쪽. 꽉 차면 빨갛게 바꾸고 고용 버튼을 막는다(UpdateCapacity).
            _capacityText = CreateText(panel, "", 18, TextAnchor.MiddleLeft);
            _capacityText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var capacityRect = _capacityText.rectTransform;
            capacityRect.anchorMin = capacityRect.anchorMax = capacityRect.pivot = new Vector2(0f, 1f);
            capacityRect.anchoredPosition = new Vector2(28f, -22f);
            capacityRect.sizeDelta = new Vector2(380f, 40f);

            var classPool = new List<MercenaryClassSO>(Resources.LoadAll<MercenaryClassSO>("MercenaryClasses"));
            _market = new MercenaryMarket(classPool, marketSize, minLevel, maxLevel);

            if (refreshButton != null)
                refreshButton.onClick.AddListener(RefreshMarket);
            PlayerParty.Instance.OnChanged += UpdateCapacity; // 해고·전사·불러오기로 정원이 바뀔 때
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= UpdateCapacity;
        }

        private void Start()
        {
            RefreshMarket();
        }

        /// <summary>"파티 정원 n/m"을 갱신하고, 꽉 찼으면 고용 버튼을 막는다.</summary>
        private void UpdateCapacity()
        {
            var party = PlayerParty.Instance;
            bool full = party.Members.Count >= party.MaxSize;
            if (_capacityText != null)
            {
                _capacityText.text = $"파티 정원 {party.Members.Count}/{party.MaxSize}" + (full ? " · 해고해야 고용 가능" : "");
                _capacityText.color = full ? FullColor : UITheme.BodyText;
            }
            _hireButtons.RemoveAll(b => b == null);
            foreach (var button in _hireButtons) button.interactable = !full;
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

            // 길드 단계에 따라 시장 경력(성장 횟수) 범위·최고 등급이 바뀐다.
            var rank = Guild.Current;
            _market.MinGrowth = rank.MarketMinGrowth;
            _market.MaxGrowth = rank.MarketMaxGrowth;
            _market.MaxGrade = rank.MaxMercGrade;

            _hireButtons.Clear();
            ContractNegotiationPanel.ForgetMarketBreakdowns(); // 새 목록이니 깨졌던 협상은 잊는다
            foreach (var merc in _market.Refresh())
                _cardRows.Add(CreateCard(merc));
            UpdateCapacity();

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
            var nameText = CreateText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}", UITheme.HeadingSize, TextAnchor.MiddleLeft); // 큰 주황 글씨는 창 제목만
            nameText.fontStyle = FontStyle.Bold;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.gameObject.AddComponent<LayoutElement>().minWidth = 220;

            var classText = CreateText(row.transform, $"{merc.Class.ClassName}  {merc.Age}세 · {GrowthTuning.RateLabel(merc.Age)}", 18, TextAnchor.MiddleLeft); // 재능은 숨은 값
            classText.horizontalOverflow = HorizontalWrapMode.Overflow;
            classText.gameObject.AddComponent<LayoutElement>().minWidth = 140;

            var powerText = CreateText(row.transform, $"전투력 {merc.CombatPower}", 18, TextAnchor.MiddleLeft);
            powerText.color = UITheme.TitleText;
            powerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            powerText.gameObject.AddComponent<LayoutElement>().minWidth = 140;

            // 관심도(명성·등급 차이·성격): 관심 없으면 협상도 못 한다
            var demand = ContractTerms.For(merc, renewal: false);
            var interestText = CreateText(row.transform, ContractTerms.InterestLabel(demand.Interest), 16, TextAnchor.MiddleLeft);
            interestText.color = ContractTerms.InterestColor(demand.Interest);
            interestText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var interestLayout = interestText.gameObject.AddComponent<LayoutElement>();
            interestLayout.minWidth = 90;
            interestLayout.flexibleWidth = 1; // 남는 폭을 차지해 협상 버튼을 오른쪽 끝으로 민다

            RowClickHandler.Attach(row, () => MercenaryInfoPanel.ShowGlobal(merc));

            var hireButtonGO = new GameObject("HireButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            hireButtonGO.transform.SetParent(row.transform, false);
            hireButtonGO.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            var hireLayout = hireButtonGO.GetComponent<LayoutElement>();
            hireLayout.minWidth = 130;
            hireLayout.minHeight = 40;
            hireLayout.flexibleWidth = 0;

            bool notInterested = demand.Interest == Interest.NotInterested;
            bool broken = ContractNegotiationPanel.IsBrokenOff(merc);
            var hireText = CreateText(hireButtonGO.transform, notInterested ? "관심 없음" : broken ? "협상 결렬" : "협상", 18, TextAnchor.MiddleCenter);
            var hireTextRect = hireText.GetComponent<RectTransform>();
            hireTextRect.anchorMin = Vector2.zero;
            hireTextRect.anchorMax = Vector2.one;
            hireTextRect.offsetMin = Vector2.zero;
            hireTextRect.offsetMax = Vector2.zero;

            hireButtonGO.AddComponent<TooltipTrigger>().Text = DemandHint(merc, demand);
            var hireButton = hireButtonGO.GetComponent<Button>();
            if (notInterested || broken) hireButton.interactable = false;
            else _hireButtons.Add(hireButton); // 정원이 차면 UpdateCapacity가 막는다
            hireButton.onClick.AddListener(() =>
            {
                var party = PlayerParty.Instance;
                if (party.Members.Count >= party.MaxSize)
                {
                    ToastLog.Show($"파티 정원({party.MaxSize}명)이 가득 찼습니다.", false);
                    return;
                }
                ContractNegotiationPanel.OpenHire(merc, () =>
                {
                    _cardRows.Remove(row);
                    Destroy(row);
                }, () =>
                {
                    if (hireButton == null || !ContractNegotiationPanel.IsBrokenOff(merc)) return;
                    hireText.text = "협상 결렬";
                    hireButton.interactable = false;
                    _hireButtons.Remove(hireButton);
                });
            });

            return row;
        }

        /// <summary>
        /// 협상 버튼 툴팁: 에이전트 귀띔(요구의 대략적인 범위). 시세(고용비)는 전투력·나이·재능·희귀 특성으로 정해지고,
        /// 재능은 숨은 값이라 같은 전투력에 요구가 높으면 재능이 좋다는 뜻이다.
        /// </summary>
        private static string DemandHint(Mercenary merc, ContractDemand demand)
        {
            if (demand.Interest == Interest.NotInterested)
                return "길드 등급·명성에 비해 눈이 높아 협상할 생각이 없습니다.\n명성을 올리면 마음이 바뀔 수 있습니다.";
            float s = ContractTuning.AgentHintSpread;
            int wLow = Mathf.Max(1, Mathf.FloorToInt(demand.WeeklyWage * (1f - s))), wHigh = Mathf.CeilToInt(demand.WeeklyWage * (1f + s));
            string text = $"에이전트 귀띔: 주급 {wLow}~{wHigh}G · 계약금 약 {Mathf.RoundToInt(demand.SigningFee / 10f) * 10}G";
            text += $"\n관심도 {ContractTerms.InterestLabel(demand.Interest)} · 인내심 {demand.Patience}";
            if (merc.HasRarePassive) text += "\n희귀 특성이 있어 몸값이 높습니다.";
            return text + "\n같은 전투력에 요구가 높으면 재능이 좋아 더 빨리 자란다는 뜻입니다.";
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
