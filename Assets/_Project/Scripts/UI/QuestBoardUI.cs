using System.Collections.Generic;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 퀘스트 게시판. 새로고침 버튼은 없고 3~4일마다 새 의뢰가 붙는다(QuestBoardListings — 저장돼 불러와도 같은 목록).
    /// 은월의 숲 레벨이 열리면 새 레벨 도전 퀘스트만 맨 위에 더한다.
    /// 의뢰 중개인(QuestBroker)이 와 있는 날엔 그 의뢰가 맨 위에 붙는다. 게시판 건물을 아직 안 지었으면 게시판 목록은 보이지 않고
    /// 중개인 의뢰만 받을 수 있다(게시판 목록은 뒤에서 계속 갈아 두어 지으면 바로 보인다).
    /// </summary>
    public class QuestBoardUI : MonoBehaviour
    {
        [SerializeField] private Transform listContainer;
        [SerializeField] private Button refreshButton; // 씬에 남아 있는 새로고침 버튼 — 숨긴다(목록은 시간 따라 바뀐다)
        [SerializeField] private GameObject partyPanel;
        [SerializeField] private int boardSize = 5; // 쓰지 않음: 게시판 수는 은월의 숲 진행이 정한다(QuestBoard.ListingCount)
        [SerializeField] private int minEnemyCount = 2;
        [SerializeField] private int maxEnemyCount = 5;
        [SerializeField] private int minDifficulty = 1;
        [SerializeField] private int maxDifficulty = 3;

        private QuestBoard _board;
        private readonly List<GameObject> _cardRows = new List<GameObject>();
        private Font _font;
        private Text _nextText; // 제목 줄 오른쪽 "새 의뢰까지 ○일"

        private void Awake()
        {
            // QuestPanel(1200x780)에서 제목 아래 ~ 패널 하단까지의 고정 영역.
            var panel = (RectTransform)listContainer.parent;
            UITheme.StyleWindowTitle(panel.Find("Title")?.GetComponent<Text>()); // 시장·퀘스트·파티 제목 줄을 같게
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 20f), new Vector2(-20f, -100f));

            _font = UITheme.Font;
            _board = new QuestBoard(boardSize, minEnemyCount, maxEnemyCount, minDifficulty, maxDifficulty);

            _nextText = ListingTimer.ReplaceRefreshButton(refreshButton, panel, _font, -23f);
            _goalText = CreateGoalText(panel);
        }

        private Text _goalText; // 제목 줄 가운데 "은월의 숲 Lv3/5 · 다음 목표 …"

        /// <summary>지금 목표 한 줄(ForestProgress.GoalText): "새 의뢰까지" 글자 왼쪽, 제목 줄 높이.</summary>
        private Text CreateGoalText(RectTransform panel)
        {
            var go = new GameObject("GoalText", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            var next = _nextText.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(560f, 40f);
            rect.anchoredPosition = next.anchoredPosition - new Vector2(next.sizeDelta.x + 16f, 0f);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleRight;
            text.color = UITheme.MutedText;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        private void Start()
        {
            ShowListings();
            ForestProgress.OnChanged += OnLevelUnlocked;
            GameClock.OnDayAdvanced += ShowListings; // 날이 바뀌어 기한이 되면 새 의뢰
            RivalGuilds.OnChanged += RedrawIfListed; // 근처 길드가 의뢰를 가져가거나 노릴 때
            QuestBroker.OnChanged += RedrawIfListed;  // 중개인이 오거나 떠날 때
            VillageLayout.OnChanged += RedrawIfListed; // 게시판을 지었을 때
            PartyUI.PendingChanged += RedrawIfListed;  // 편성 시작·취소·출발 — "편성 중" 표시
        }

        private void RedrawIfListed()
        {
            if (QuestBoardListings.Current != null && _nextText != null) Redraw();
        }

        private void OnDestroy()
        {
            ForestProgress.OnChanged -= OnLevelUnlocked;
            GameClock.OnDayAdvanced -= ShowListings;
            RivalGuilds.OnChanged -= RedrawIfListed;
            QuestBroker.OnChanged -= RedrawIfListed;
            VillageLayout.OnChanged -= RedrawIfListed;
            PartyUI.PendingChanged -= RedrawIfListed;
        }

        /// <summary>불러오기 직후: 저장된 목록으로 다시 그린다(낡았으면 새로 뽑는다).</summary>
        public void RefreshFromOutside()
        {
            if (_board != null) ShowListings(); // 아직 한 번도 안 열려 Awake 전이면 열 때 Start에서 그린다
        }

        /// <summary>기한이 됐거나 아직 없으면 새로 뽑고, 아니면 붙어 있는 목록 그대로 그린다.</summary>
        private void ShowListings()
        {
            QuestBroker.EnsureFirstVisit(); // 새 게임: 게시판이 없으니 중개인이 바로 온다
            if (QuestBoardListings.IsStale)
            {
                _board.MaxGrade = Guild.Current.MaxQuestGrade; // 길드 단계에 따라 높은 등급 퀘스트가 열린다
                var quests = new List<Quest>(_board.Refresh());
                RivalDeals.MakeJointQuests(quests); // 우호 길드가 있으면 하나는 연합 의뢰로
                QuestBoardListings.Set(quests, _board.Rng);
                RivalGuilds.PickTarget(); // 새 게시판에서 내일 가로챌 의뢰
            }
            Redraw();
        }

        /// <summary>은월의 숲 레벨이 열리면 게시판을 다시 뽑지 않고 새 레벨 도전 퀘스트만 맨 위에 더한다(불러오기 Restore 때는 그대로).</summary>
        private void OnLevelUnlocked()
        {
            var current = QuestBoardListings.Current;
            if (current == null) { ShowListings(); return; }
            bool hasChallenge = current.Exists(q => q.Difficulty >= ForestProgress.UnlockedLevel);
            if (!hasChallenge)
            {
                current.Insert(0, _board.GenerateChallenge());
                QuestBoardListings.NotifyChanged();
            }
            Redraw();
        }

        private void Redraw()
        {
            foreach (var row in _cardRows)
                Destroy(row);
            _cardRows.Clear();

            // 중개인 의뢰(중개인이 떠나도 기한까지 남는다)를 맨 위에 — 카드 끝 "기한 N일"로 게시판 의뢰와 구분된다
            foreach (var quest in QuestBroker.Quests)
                _cardRows.Add(CreateCard(quest, ExpiryText(QuestBroker.DaysLeft(quest))));
            bool hasBoard = QuestBroker.HasBoard;
            if (hasBoard)
                foreach (var quest in QuestBoardListings.Current)
                    _cardRows.Add(CreateCard(quest));
            _nextText.text = hasBoard ? ListingTimer.Label("의뢰", QuestBoardListings.DaysUntilRefresh) : "";
            if (_goalText != null) _goalText.text = ForestProgress.GoalText;

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
        }

        /// <summary>중개인 의뢰 기한 꼬리 글: "· 기한 3일", 마지막 날이면 주황 "· 오늘까지".</summary>
        private static string ExpiryText(int daysLeft) =>
            daysLeft < 0 ? "" : daysLeft <= 1 ? "  <color=#f0a54a>· 오늘까지</color>" : $"  <color=#a3937c>· 기한 {daysLeft}일</color>";

        private GameObject CreateCard(Quest quest, string extra = "")
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

            // 줄에는 [등급] 이름 · 판정·권장 전투력 · 보상. 지역·적·소요 일수는 줄을 클릭하면 열리는 상세 창에서 본다.
            string jointTag = quest.IsJoint ? "<color=#6ab8ff>[연합]</color> " : "";
            string challengeTag = quest.IsChallenge ? "<color=#ffd36b>[도전]</color> " : ""; // 깨면 다음 숲 레벨이 열린다(ForestProgress)
            var titleText = CreateText(row.transform, challengeTag + jointTag + QuestDifficulty.RichTitle(quest), UITheme.HeadingSize, TextAnchor.MiddleLeft); // 큰 주황 글씨는 창 제목만
            titleText.fontStyle = FontStyle.Bold;
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText.gameObject.AddComponent<LayoutElement>().minWidth = 520;

            string rounds = quest.WaveCount > 1 ? $"<color=#a3937c>{quest.WaveCount}라운드</color>  " : "";
            var rival = RivalGuilds.TargetOf(quest); // 내일 자정에 가져갈 근처 길드
            string targeted = rival != null ? $"  <color=#f0a54a>{Josa.IGa(rival.Name)} 노리는 중</color>" : "";
            string materials = quest.MaterialReward.Count > 0 ? $" · {quest.MaterialRewardText}" : ""; // 부탁한 사람이 주는 건설 재료
            // 판정: 지금 보낼 수 있는 최선의 팀 vs 권장 전투력(쉬움·중간·어려움 — 상세 창을 열지 않고 비교)
            int best = QuestDifficulty.BestTeamPower(quest);
            string verdict = (best > 0 ? QuestDifficulty.RichLabel(QuestDifficulty.Rate(best, quest)) : "<color=#a3937c>[용병 없음]</color>")
                + $" <color=#a3937c>권장 {QuestDifficulty.RecommendedPower(quest)}</color>  ";
            var rewardText = CreateText(row.transform, $"{verdict}{rounds}보상 {Pricing.QuestReward(quest)}G{materials}{targeted}{extra}", 18, TextAnchor.MiddleLeft);
            rewardText.supportRichText = true;
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

            bool pending = PartyUI.PendingQuest == quest; // 편성 중인 의뢰: 바탕을 밝게, 버튼은 파티 창을 다시 연다
            if (pending)
            {
                row.GetComponent<Image>().color = new Color(1f, 0.85f, 0.4f, 0.14f);
                acceptButtonGO.GetComponent<Image>().color = new Color(0.55f, 0.45f, 0.2f, 1f);
            }
            var acceptText = CreateText(acceptButtonGO.transform, pending ? "편성 중" : "수락", 18, TextAnchor.MiddleCenter);
            var acceptTextRect = acceptText.GetComponent<RectTransform>();
            acceptTextRect.anchorMin = Vector2.zero;
            acceptTextRect.anchorMax = Vector2.one;
            acceptTextRect.offsetMin = Vector2.zero;
            acceptTextRect.offsetMax = Vector2.zero;

            acceptButtonGO.GetComponent<Button>().onClick.AddListener(() => Accept(quest, row));

            return row;
        }

        /// <summary>
        /// 수락: 파견 편성을 시작하고 파티 패널을 연다(줄 버튼·상세 창 버튼 공용). 이미 편성 중인 의뢰면 파티 패널만 연다.
        /// 의뢰는 출발할 때 게시판에서 뗀다(PartyUI.ConfirmDispatch) — 편성을 취소하거나 다른 의뢰를 수락해도 잃지 않는다.
        /// </summary>
        private void Accept(Quest quest, GameObject row)
        {
            if (PartyUI.PendingQuest != quest) GetComponent<PartyUI>()?.BeginDispatch(quest);
            if (partyPanel != null)
                PanelActivator.Open(partyPanel);
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
