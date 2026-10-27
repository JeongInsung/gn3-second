using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 퀘스트 게시판 줄을 클릭하면 뜨는 상세 창: 등급·지역·적·난이도·권장 전투력과 내 판정·소요 일수·최대 인원·보상, 수락 버튼.
    /// 내 판정·최선 편성 일수는 0.5초마다 다시 계산한다(고용·무기 변화 반영). 끌어 옮길 수 있고 X·ESC로 닫는다.
    /// </summary>
    public class QuestInfoPanel : MonoBehaviour
    {
        private const float Width = 340f; // 화면 오른쪽 빈 곳(게시판 오른쪽 끝 ~ 화면 끝)에 들어가게 — 넓으면 게시판 수락 버튼을 덮었다
        private const float Height = 400f;
        private const float RefreshInterval = 0.5f;

        public static QuestInfoPanel Instance { get; private set; }

        private GameObject _panel;
        private Text _title;
        private Text _body;
        private Button _acceptButton;
        private Font _font;

        private Quest _quest;
        private System.Action _onAccept;
        private bool _accepted;
        private float _refreshTimer;

        public static QuestInfoPanel Create()
        {
            var go = new GameObject("QuestInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(QuestInfoPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<QuestInfoPanel>();
            panel.Build();
            Instance = panel;
            return panel;
        }

        public static void ShowGlobal(Quest quest, System.Action onAccept)
        {
            if (Instance != null && quest != null) Instance.Show(quest, onAccept);
        }

        public void Show(Quest quest, System.Action onAccept)
        {
            _quest = quest;
            _onAccept = onAccept;
            _accepted = false;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
        }

        public void Hide()
        {
            _quest = null;
            _onAccept = null;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_quest == null) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f) Refresh();
        }

        private void Refresh()
        {
            _refreshTimer = RefreshInterval;
            var q = _quest;
            _title.text = (q.IsChallenge ? "<color=#ffd36b>[도전]</color> " : "") + QuestDifficulty.ShortRichTitle(q); // 핵심만(레벨은 본문, 의뢰인은 부탁 글 아래) · 도전 의뢰는 깨면 다음 숲 레벨
            _body.text =
                (string.IsNullOrEmpty(q.RequestText) ? "" : $"<i>\"{q.RequestText}\"</i>\n<color=#a3937c>— {q.Requester}</color>\n\n") + // 마을 사람의 부탁
                (string.IsNullOrEmpty(q.LocationName) ? "" : $"장소: {q.LocationName}\n") + // 지역(동서남북) 구분은 없앴다
                (q.WaveCount > 1
                    ? $"라운드 {q.WaveCount}개 (체력은 라운드 사이에 이어짐)    은월의 숲 Lv{q.Difficulty}\n{QuestDifficulty.WavesText(q)}\n\n"
                    : $"적: {q.EnemyName} × {q.EnemyCount}    은월의 숲 Lv{q.Difficulty}\n\n") +
                $"{QuestDifficulty.DescribeForBoard(q)}\n" +
                $"{QuestDifficulty.BoardDurationText(q)}\n" +
                (q.RequiredDispatchSize <= 0 ? $"최대 파견 인원: {q.MaxDispatchSize}명\n\n"
                    : q.SquadCount > 1 ? $"필요 인원: {q.RequiredDispatchSize}명 ({q.SquadSize}명씩 두 분대)\n\n"
                    : $"필요 인원: 정확히 {q.RequiredDispatchSize}명\n\n") +
                (RivalDeals.JointLabel(q) is string joint ? $"<color=#6ab8ff>{joint}</color>\n" : "") + // 연합 의뢰
                (RivalGuilds.TargetOf(q) is RivalGuild rival ? $"<color=#f0a54a>{Josa.IGa(rival.Name)} 이 의뢰를 노리고 있다 — 오늘 안 맡으면 내일 가져간다.</color>\n" : "") +
                $"<color=#f0a54a>보상 {Pricing.QuestReward(q)}G{(q.MaterialReward.Count > 0 ? $" · {q.MaterialRewardText}" : "")}</color>\n" +                $"<color=#a3937c>얻는 재료(몬스터): {string.Join("·", q.DroppedMaterials.Select(m => m.Name))}</color>";
            _acceptButton.interactable = !_accepted && _onAccept != null;
            LayoutBody();
        }

        /// <summary>제목·본문이 길면 잘리지 않게 본문을 내리고 창을 아래로 늘린다(최소 Height, 수락 버튼은 창 아래에 붙어 따라 내려간다).</summary>
        private void LayoutBody()
        {
            var titleRect = _title.rectTransform;
            float titleHeight = Mathf.Max(52f, _title.preferredHeight);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, titleHeight);
            float bodyTop = -16f - titleHeight - 10f;
            var bodyRect = _body.rectTransform;
            bodyRect.anchoredPosition = new Vector2(bodyRect.anchoredPosition.x, bodyTop);
            float bodyHeight = Mathf.Max(Height - 150f, _body.preferredHeight);
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight);
            var panelRect = (RectTransform)_panel.transform;
            panelRect.sizeDelta = new Vector2(Width, Mathf.Max(Height, -(bodyTop - bodyHeight) + 72f));
        }

        private void Accept()
        {
            if (_accepted || _onAccept == null) return;
            _accepted = true;
            _onAccept();
            Hide();
        }

        // ---------- 화면 ----------

        private void Build()
        {
            _font = UITheme.Font;

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; // 화면 오른쪽 위
            rect.anchoredPosition = new Vector2(-16f, -120f);
            rect.sizeDelta = new Vector2(Width, Height);
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(rect);

            _title = CreateText("Title", 20, FontStyle.Bold, new Vector2(18f, -16f), new Vector2(Width - 70f, 52f));
            _title.supportRichText = true;
            _title.verticalOverflow = VerticalWrapMode.Overflow;

            _body = CreateText("Body", 16, FontStyle.Normal, new Vector2(18f, -78f), new Vector2(Width - 36f, Height - 150f));
            _body.supportRichText = true; // 판정·보상 색

            // 수락 버튼
            var accept = new GameObject("AcceptButton", typeof(RectTransform), typeof(Image), typeof(Button));
            accept.transform.SetParent(_panel.transform, false);
            var acceptRect = accept.GetComponent<RectTransform>();
            acceptRect.anchorMin = acceptRect.anchorMax = acceptRect.pivot = new Vector2(0.5f, 0f);
            acceptRect.anchoredPosition = new Vector2(0f, 16f);
            acceptRect.sizeDelta = new Vector2(160f, 44f);
            accept.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f); // 초록 → 테마 초록 버튼
            _acceptButton = accept.GetComponent<Button>();
            _acceptButton.onClick.AddListener(Accept);
            FillLabel(accept.transform, "수락", 18);

            // 닫기
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            close.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Hide);
            EscapeCloser.Register(_panel, Hide, WindowRole.Linked); // 게시판 줄을 눌러 여는 창: 퀘스트 창을 닫지 않는다
            FillLabel(close.transform, "X", 16);

            _panel.SetActive(false);
        }

        private Text CreateText(string name, int size, FontStyle style, Vector2 topLeft, Vector2 boxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = boxSize;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = UITheme.BodyText;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow; // 길면 LayoutBody가 칸과 창을 늘린다
            text.raycastTarget = false;
            return text;
        }

        private void FillLabel(Transform parent, string label, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.text = label;
            text.font = _font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
