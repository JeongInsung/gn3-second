using System.Collections.Generic;
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
            // QuestPanel(1200x780)에서 제목/새로고침 버튼 아래 ~ 패널 하단까지의 고정 영역 (MarketPanel과 동일 레이아웃)
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -70f));

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _board = new QuestBoard(boardSize, minEnemyCount, maxEnemyCount, minDifficulty, maxDifficulty);

            if (refreshButton != null)
                refreshButton.onClick.AddListener(RefreshBoard);
        }

        private void Start()
        {
            RefreshBoard();
        }

        private void RefreshBoard()
        {
            foreach (var row in _cardRows)
                Destroy(row);
            _cardRows.Clear();

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

            string info = $"{quest.Title}    난이도 {quest.Difficulty}    예상 소요 {quest.DurationDays}일";
            var infoText = CreateText(row.transform, info, 20, TextAnchor.MiddleLeft);
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 100;

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

            acceptButtonGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                GetComponent<PartyUI>()?.BeginDispatch(quest);
                if (partyPanel != null)
                    PanelActivator.Open(partyPanel);

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
