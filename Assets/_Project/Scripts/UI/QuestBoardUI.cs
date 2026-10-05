using System.Collections.Generic;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public class QuestBoardUI : MonoBehaviour
    {
        [SerializeField] private Transform listContainer;
        [SerializeField] private Button refreshButton;
        [SerializeField] private GameObject partyPanel;
        [SerializeField] private int boardSize = 5;
        [SerializeField] private int minEnemyCount = 2;
        [SerializeField] private int maxEnemyCount = 5;
        [SerializeField] private int minDifficulty = 1;
        [SerializeField] private int maxDifficulty = 3;

        private QuestBoard _board;
        private readonly List<GameObject> _cardRows = new List<GameObject>();
        private Font _font;

        private void Awake()
        {
            // QuestPanel(1200x780)에서 제목/새로고침 버튼 아래 ~ 패널 하단까지의 고정 영역.
            // 새로고침 버튼 아래 끝이 위에서 약 88px이라 100px부터 시작해 첫 줄이 버튼과 겹치지 않게 한다.
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -100f));

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _board = new QuestBoard(boardSize, minEnemyCount, maxEnemyCount, minDifficulty, maxDifficulty);

            if (refreshButton != null)
                refreshButton.onClick.AddListener(RefreshBoard);
        }

        private void Start()
        {
            RefreshBoard();
        }

        /// <summary>불러오기 직후처럼 밖에서 게시판을 새로 뽑을 때.</summary>
        public void RefreshFromOutside()
        {
            if (_board != null) RefreshBoard(); // 아직 한 번도 안 열려 Awake 전이면 열 때 Start에서 뽑는다
        }

        private void RefreshBoard()
        {
            foreach (var row in _cardRows)
                Destroy(row);
            _cardRows.Clear();

            _board.MaxGrade = Guild.Current.MaxQuestGrade; // 길드 단계에 따라 높은 등급 퀘스트가 열린다
            foreach (var quest in _board.Refresh())
                _cardRows.Add(CreateCard(quest));

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
        }

        private GameObject CreateCard(Quest quest)
        {
            var row = new GameObject(quest.Title, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
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

            // 줄에는 [등급] 이름 · 보상만. 지역·적·권장 전투력·판정·소요 일수는 줄을 클릭하면 열리는 상세 창에서 본다.
            var titleText = CreateText(row.transform, QuestDifficulty.RichTitle(quest), 20, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText.gameObject.AddComponent<LayoutElement>().minWidth = 520;

            var rewardText = CreateText(row.transform, $"보상 {Pricing.QuestReward(quest)}G", 18, TextAnchor.MiddleLeft);
            rewardText.color = UITheme.TitleText;
            rewardText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rewardLayout = rewardText.gameObject.AddComponent<LayoutElement>();
            rewardLayout.minWidth = 120;
            rewardLayout.flexibleWidth = 1; // 남는 폭을 차지해 수락 버튼을 오른쪽 끝으로 민다

            RowClickHandler.Attach(row, () => QuestInfoPanel.ShowGlobal(quest, () => Accept(quest, row)));

            var acceptButtonGO = new GameObject("AcceptButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            acceptButtonGO.transform.SetParent(row.transform, false);
            acceptButtonGO.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            var acceptLayout = acceptButtonGO.GetComponent<LayoutElement>();
            acceptLayout.minWidth = 90;
            acceptLayout.minHeight = 40;
            acceptLayout.flexibleWidth = 0;

            var acceptText = CreateText(acceptButtonGO.transform, "수락", 18, TextAnchor.MiddleCenter);
            var acceptTextRect = acceptText.GetComponent<RectTransform>();
            acceptTextRect.anchorMin = Vector2.zero;
            acceptTextRect.anchorMax = Vector2.one;
            acceptTextRect.offsetMin = Vector2.zero;
            acceptTextRect.offsetMax = Vector2.zero;

            acceptButtonGO.GetComponent<Button>().onClick.AddListener(() => Accept(quest, row));

            return row;
        }

        /// <summary>수락: 파견 편성을 시작하고 파티 패널을 연 뒤 게시판에서 그 줄을 지운다(줄 버튼·상세 창 버튼 공용).</summary>
        private void Accept(Quest quest, GameObject row)
        {
            if (row == null) return; // 이미 수락된 줄
            GetComponent<PartyUI>()?.BeginDispatch(quest);
            if (partyPanel != null)
                PanelActivator.Open(partyPanel);

            _cardRows.Remove(row);
            Destroy(row);
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
