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
        private const float Width = 460f;
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
            string place = string.IsNullOrEmpty(q.LocationName)
                ? RegionInfo.DisplayName(q.Region)
                : $"{RegionInfo.DisplayName(q.Region)} · {q.LocationName}";

            _title.text = QuestDifficulty.RichTitle(q);
            _body.text =
                $"지역: {place}\n" +
                $"적: {q.EnemyName} × {q.EnemyCount}    난이도 {q.Difficulty}\n\n" +
                $"{QuestDifficulty.DescribeForBoard(q)}\n" +
                $"{QuestDifficulty.BoardDurationText(q)}\n" +
                $"최대 파견 인원: {q.MaxDispatchSize}명\n\n" +
                $"<color=#f0a54a>보상 {Pricing.QuestReward(q)}G</color>";
            _acceptButton.interactable = !_accepted && _onAccept != null;
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
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            text.verticalOverflow = VerticalWrapMode.Truncate;
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
