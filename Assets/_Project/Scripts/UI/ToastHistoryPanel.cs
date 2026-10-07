using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 왼쪽 아래에 잠깐 떴다 사라진 알림(ToastLog)을 다시 보는 창. 최신이 위, "N일차 시각 · 메시지".
    /// 상단 바의 "알림 기록" 버튼으로 열고 닫는다(닫기 버튼·ESC도 됨). 열려 있는 동안 새 알림이 오면 바로 추가된다.
    /// 모양은 하루 보고서(DayReportPanel)와 같고 테마는 UIThemeApplier가 입힌다.
    /// </summary>
    public class ToastHistoryPanel : MonoBehaviour
    {
        private const float Width = 600f;
        private const float Height = 460f;

        private static ToastHistoryPanel _instance;

        private GameObject _panel;
        private Text _title;
        private RectTransform _content;
        private Font _font;

        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        public static void Toggle()
        {
            if (_instance == null) _instance = Create();
            if (_instance._panel.activeSelf) _instance.Hide();
            else _instance.Open();
        }

        private static ToastHistoryPanel Create()
        {
            var go = new GameObject("ToastHistory", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ToastHistoryPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<ToastHistoryPanel>();
            panel.Build();
            return panel;
        }

        private void OnEnable() => ToastLog.Added += HandleAdded;
        private void OnDisable() => ToastLog.Added -= HandleAdded;

        private void HandleAdded()
        {
            if (_panel != null && _panel.activeSelf) Fill();
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _panel = new GameObject("HistoryPanel", typeof(RectTransform), typeof(Image));
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

            // 닫기 버튼
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.anchoredPosition = new Vector2(0f, 16f);
            closeRect.sizeDelta = new Vector2(140f, 42f);
            close.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍 버튼으로
            close.GetComponent<Button>().onClick.AddListener(Hide);
            var closeText = CreateText(close.transform, "닫기", 18, TextAnchor.MiddleCenter, UITheme.BodyText);
            closeText.rectTransform.anchorMin = Vector2.zero;
            closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = closeText.rectTransform.offsetMax = Vector2.zero;

            _panel.SetActive(false);
        }

        private void Open()
        {
            Fill();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var scroll = _content.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void Fill()
        {
            var history = ToastLog.History;
            _title.text = $"알림 기록 ({history.Count})";

            // Destroy는 프레임 끝이라 바로 레이아웃을 계산하면 옛 줄이 섞인다 → 먼저 떼어 낸다.
            var old = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in _content) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            if (history.Count == 0)
                CreateText(_content, "아직 알림이 없습니다.", 15, TextAnchor.UpperLeft, UITheme.MutedText);
            for (int i = history.Count - 1; i >= 0; i--) // 최신이 위
            {
                var entry = history[i];
                CreateText(_content, $"{entry.Day}일차 {DayNightCycle.FormatTime(entry.Hour)}  ·  {entry.Message}", 15, TextAnchor.UpperLeft, UITheme.BodyText);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
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
