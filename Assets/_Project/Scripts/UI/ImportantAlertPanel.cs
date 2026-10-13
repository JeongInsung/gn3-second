using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 중요 소식(파견대 도착·퀘스트 결과·용병 전사/전멸·길드 등급 상승·주급 미지급)을 게임을 멈추고 화면 가운데에 띄운다.
    /// 우편함(Mailbox)의 아직 확인하지 않은 중요 편지를 오래된 것부터 하나씩 보여 주고, 다 확인하면 게임이 다시 흐른다.
    /// 도착: [자동 진행] [직접 진행] [나중에] — 나중에는 우편함에 "선택 대기"로 남는다. 그 밖: [확인] [우편함 열기].
    /// ESC는 도착이면 [나중에], 그 밖이면 [확인]. MainMenuBootstrapper가 MainScene에서 만든다.
    /// </summary>
    public class ImportantAlertPanel : MonoBehaviour
    {
        private const float Width = 580f;
        private const float Height = 400f;

        private GameObject _root;
        private Text _tag;
        private Text _title;
        private Text _when;
        private Text _body;
        private ScrollRect _scroll;
        private GameObject _arrivalRow;
        private GameObject _noticeRow;
        private Font _font;

        private Mailbox.Mail _current;

        public static ImportantAlertPanel Create()
        {
            var go = new GameObject("ImportantAlert", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ImportantAlertPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3; // 우편함(2) 위, 이어하기 창(4) 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<ImportantAlertPanel>();
            panel.Build();
            return panel;
        }

        private void OnDestroy() => GamePause.Pop(this); // 씬을 넘어가도 멈춤이 남지 않게

        private void Update()
        {
            // 띄운 도착 편지를 우편함·파티 패널 등 다른 곳에서 처리했으면 닫는다.
            if (_current != null && _current.Kind == MailKind.Arrival && !_current.IsPendingChoice)
            {
                Mailbox.Acknowledge(_current);
                Close();
            }
            if (_current != null) return;

            var next = Mailbox.NextUnacknowledgedImportant();
            if (next == null)
            {
                GamePause.Pop(this);
                return;
            }
            Show(next);
        }

        private void Show(Mailbox.Mail mail)
        {
            _current = mail;
            GamePause.Push(this);
            Mailbox.MarkRead(mail);

            _tag.text = $"[{Mailbox.KindTag(mail.Kind)}]";
            _tag.color = Mailbox.KindColor(mail.Kind);
            _title.text = mail.Title;
            _when.text = Mailbox.FormatWhen(mail);
            _body.text = mail.Body;
            bool arrival = mail.Kind == MailKind.Arrival;
            _arrivalRow.SetActive(arrival);
            _noticeRow.SetActive(!arrival);

            _root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body.rectTransform);
            _scroll.verticalNormalizedPosition = 1f;
            UIThemeApplier.ApplyNow(_root.transform);
        }

        private void Close()
        {
            _current = null;
            _root.SetActive(false);
            // 멈춤은 다음 Update에서 다음 편지가 없을 때 푼다(연달아 뜰 때 잠깐 풀렸다 멈추지 않게).
        }

        private void Confirm()
        {
            Mailbox.Acknowledge(_current);
            Close();
        }

        private void OpenMailbox()
        {
            var mail = _current;
            Confirm();
            MailboxPanel.Open(mail);
        }

        private void Later() => Confirm(); // 확인만 하고 선택은 우편함에 남긴다

        private void Auto()
        {
            var mail = _current;
            Confirm();
            MailActions.AutoFight(mail.Expedition); // 결과 편지가 다음 차례로 뜬다
        }

        private void Direct()
        {
            var mail = _current;
            Confirm();
            GamePause.Pop(this);
            MailActions.DirectFight(mail.Expedition);
        }

        private bool TryEscape()
        {
            if (_current == null) return false;
            if (_current.Kind == MailKind.Arrival) Later();
            else Confirm();
            return true;
        }

        // ---------- 화면 ----------

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 뒤 화면 클릭 막기(화면 전체 어둡게 — 테마 자동 적용에서 빠지는 전체 화면 막)
            _root = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);
            var dimRect = _root.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var panel = new GameObject("AlertPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height);
            rect.anchoredPosition = new Vector2(0f, 30f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.97f);
            DraggablePanel.Attach(rect);

            _tag = CreateText(panel.transform, "", 16, TextAnchor.MiddleLeft, Color.white);
            _tag.fontStyle = FontStyle.Bold;
            Place(_tag.rectTransform, new Vector2(22f, -16f), new Vector2(90f, 30f));
            _when = CreateText(panel.transform, "", 14, TextAnchor.MiddleRight, UITheme.MutedText);
            Place(_when.rectTransform, new Vector2(Width - 262f, -16f), new Vector2(240f, 30f));
            _title = CreateText(panel.transform, "", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            _title.fontStyle = FontStyle.Bold;
            _title.supportRichText = true;
            Place(_title.rectTransform, new Vector2(22f, -48f), new Vector2(Width - 44f, 36f));

            // 본문(길면 스크롤)
            var view = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            view.transform.SetParent(panel.transform, false);
            Place(view.GetComponent<RectTransform>(), new Vector2(18f, -92f), new Vector2(Width - 36f, Height - 170f));
            view.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f); // UIThemeApplier가 어두운 칸으로
            _body = CreateText(view.transform, "", 16, TextAnchor.UpperLeft, UITheme.BodyText);
            _body.supportRichText = true;
            var bodyRect = _body.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = new Vector2(12f, 0f);
            bodyRect.offsetMax = new Vector2(-12f, -10f);
            _body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = view.GetComponent<ScrollRect>();
            _scroll.content = bodyRect;
            _scroll.horizontal = false;
            SmoothWheelScroll.Attach(_scroll);

            _arrivalRow = CreateButtonRow(panel.transform);
            CreateButton(_arrivalRow.transform, "자동 진행", Auto, new Color(0.25f, 0.55f, 0.35f, 1f));
            CreateButton(_arrivalRow.transform, "직접 진행", Direct, null);
            CreateButton(_arrivalRow.transform, "나중에", Later, new Color(0.3f, 0.3f, 0.35f, 1f));

            _noticeRow = CreateButtonRow(panel.transform);
            CreateButton(_noticeRow.transform, "확인", Confirm, new Color(0.25f, 0.55f, 0.35f, 1f));
            CreateButton(_noticeRow.transform, "우편함 열기", OpenMailbox, new Color(0.3f, 0.3f, 0.35f, 1f));

            _root.SetActive(false);
            EscapeCloser.Register(_root, TryEscape, WindowRole.Popup);
        }

        private GameObject CreateButtonRow(Transform parent)
        {
            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, 20f);
            rowRect.sizeDelta = new Vector2(Width - 60f, 46f);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            return row;
        }

        private void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color? color)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color ?? new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍(초록이면 초록) 버튼으로
            go.GetComponent<LayoutElement>().minHeight = 46f;
            var text = CreateText(go.transform, label, 17, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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
