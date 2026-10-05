using GN3.Quests;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 하루(또는 건너뛴 여러 날)가 지난 뒤 날짜별로 파견 진행 상황과 있었던 일(DailyLog)을 보여 주는 보고서 창.
    /// 화면 가운데 판(끌어 옮기기 가능), 스크롤 본문, "확인" 버튼·ESC로 닫는다. 테마는 UIThemeApplier가 입힌다.
    /// </summary>
    public class DayReportPanel : MonoBehaviour
    {
        private const float Width = 600f;
        private const float Height = 460f;
        private static readonly Color AlertText = new Color(0.95f, 0.6f, 0.35f);

        private static DayReportPanel _instance;

        private GameObject _panel;
        private Text _title;
        private RectTransform _content;
        private Font _font;

        public static void Show(int fromDay, int toDay)
        {
            if (toDay < fromDay) return;
            if (_instance == null) _instance = Create();
            _instance.Fill(fromDay, toDay);
        }

        private static DayReportPanel Create()
        {
            var go = new GameObject("DayReport", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(DayReportPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var report = go.GetComponent<DayReportPanel>();
            report.Build();
            return report;
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _panel = new GameObject("ReportPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height);
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(rect);

            _title = CreateText(_panel.transform, "", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            _title.fontStyle = FontStyle.Bold;
            Place(_title.rectTransform, new Vector2(22f, -16f), new Vector2(Width - 44f, 34f));

            // 스크롤 본문
            var list = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            list.transform.SetParent(_panel.transform, false);
            Place(list.GetComponent<RectTransform>(), new Vector2(18f, -60f), new Vector2(Width - 36f, Height - 130f));
            list.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f); // UIThemeApplier가 어두운 칸으로
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(list.transform, false);
            _content = content.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = list.GetComponent<ScrollRect>();
            scroll.content = _content;
            scroll.horizontal = false;
            SmoothWheelScroll.Attach(scroll);

            // 확인 버튼
            var ok = new GameObject("ConfirmButton", typeof(RectTransform), typeof(Image), typeof(Button));
            ok.transform.SetParent(_panel.transform, false);
            var okRect = ok.GetComponent<RectTransform>();
            okRect.anchorMin = okRect.anchorMax = okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(0f, 16f);
            okRect.sizeDelta = new Vector2(140f, 42f);
            ok.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍 버튼으로
            ok.GetComponent<Button>().onClick.AddListener(Hide);
            var okText = CreateText(ok.transform, "확인", 18, TextAnchor.MiddleCenter, UITheme.BodyText);
            okText.rectTransform.anchorMin = Vector2.zero;
            okText.rectTransform.anchorMax = Vector2.one;
            okText.rectTransform.offsetMin = okText.rectTransform.offsetMax = Vector2.zero;

            _panel.SetActive(false);
        }

        private void Fill(int fromDay, int toDay)
        {
            // 낮에 생겨 아직 보고하지 않은 일(이전 일차)도 함께 싣는다.
            fromDay = System.Math.Min(fromDay, DailyLog.FirstPendingDay);
            var pending = DailyLog.TakePending();
            _title.text = fromDay == toDay ? $"{fromDay}일차 보고" : $"{fromDay}일차 ~ {toDay}일차 보고";

            // Destroy는 프레임 끝이라 바로 레이아웃을 계산하면 옛 줄이 섞인다 → 먼저 떼어 낸다.
            var old = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in _content) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            for (int day = fromDay; day <= toDay; day++)
            {
                var header = CreateText(_content, $"■ {day}일차", 17, TextAnchor.MiddleLeft, UITheme.TitleText);
                header.fontStyle = FontStyle.Bold;

                var lines = pending.TryGetValue(day, out var dayLines) ? dayLines : new System.Collections.Generic.List<string>();
                if (lines.Count == 0) CreateText(_content, "· " + DailyLog.QuietDay, 15, TextAnchor.UpperLeft, UITheme.MutedText);
                foreach (var line in lines)
                {
                    bool alert = line.Contains("습격") || line.Contains("사망") || line.Contains("전멸");
                    bool arrived = line.Contains("도착!");
                    var color = alert ? AlertText : arrived ? new Color(0.6f, 0.85f, 0.45f) : UITheme.BodyText;
                    CreateText(_content, "· " + line, 15, TextAnchor.UpperLeft, color);
                }
            }

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var scroll = _content.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void Hide() => _panel.SetActive(false);

        private void Update()
        {
            if (_panel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Hide();
        }

        private Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }
    }
}
