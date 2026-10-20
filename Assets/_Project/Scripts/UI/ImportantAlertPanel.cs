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
        private RectTransform _panelRect, _bodyView; // 본문 길이에 맞춰 높이를 줄인다(FitToBody)

        private const float MinBodyHeight = 90f;
        private const float ChromeHeight = 170f; // 판 높이 - 본문 칸 높이(제목 줄 + 버튼 줄 + 여백)
        private GameObject _arrivalRow;
        private GameObject _noticeRow;
        private GameObject _choiceRow; // 사건 선택지(편지마다 다시 만든다)
        private Font _font;

        private Mailbox.Mail _current;

        public static ImportantAlertPanel Create()
        {
            var go = new GameObject("ImportantAlert", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ImportantAlertPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4; // 마을 HUD(3)·우편함(2) 위, 타이틀(5) 아래
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
            // 띄운 사건을 우편함에서 골랐으면 결과를 보여 준다
            if (_current != null && _current.Choices != null && _current.ChoiceResolved && _choiceRow.activeSelf) ShowResult();
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
            bool choosing = mail.IsPendingEvent;
            _arrivalRow.SetActive(arrival);
            _noticeRow.SetActive(!arrival && !choosing);
            _choiceRow.SetActive(choosing);
            if (choosing) BuildChoices(mail);

            _root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body.rectTransform);
            FitToBody();
            _scroll.verticalNormalizedPosition = 1f;
            UIThemeApplier.ApplyNow(_root.transform);
        }

        /// <summary>본문 칸을 글 길이에 맞춘다(MinBodyHeight ~ 원래 높이, 넘치면 스크롤). 판도 같이 줄어 짧은 알림에 빈 회색 칸이 남지 않는다.</summary>
        private void FitToBody()
        {
            float bodyHeight = Mathf.Clamp(_body.preferredHeight + 20f, MinBodyHeight, Height - ChromeHeight);
            _bodyView.sizeDelta = new Vector2(_bodyView.sizeDelta.x, bodyHeight);
            _panelRect.sizeDelta = new Vector2(_panelRect.sizeDelta.x, bodyHeight + ChromeHeight);
        }

        private void Close()
        {
            _current = null;
            _root.SetActive(false);
            // 다음 편지가 없으면 바로 멈춤을 푼다("확인" 직후 누른 진행·건너뛰기가 무시되지 않게).
            // 다음 편지가 있으면 멈춘 채로 두고 다음 Update가 띄운다(연달아 뜰 때 잠깐 풀렸다 멈추지 않게).
            if (Mailbox.NextUnacknowledgedImportant() == null) GamePause.Pop(this);
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

        // ---------- 사건 선택 ----------

        private void BuildChoices(Mailbox.Mail mail)
        {
            for (int i = _choiceRow.transform.childCount - 1; i >= 0; i--) Destroy(_choiceRow.transform.GetChild(i).gameObject);
            for (int i = 0; i < mail.Choices.Count; i++)
            {
                var choice = mail.Choices[i];
                int index = i;
                var button = CreateButton(_choiceRow.transform, choice.Label, () => Choose(index), i == 0 ? new Color(0.25f, 0.55f, 0.35f, 1f) : (Color?)null);
                button.interactable = choice.CanChoose;
                if (!string.IsNullOrEmpty(choice.Tooltip)) button.gameObject.AddComponent<TooltipTrigger>().Text = choice.Tooltip;
            }
            CreateButton(_choiceRow.transform, "나중에", Later, new Color(0.3f, 0.3f, 0.35f, 1f));
        }

        private void Choose(int index)
        {
            if (Mailbox.Choose(_current, index)) ShowResult();
        }

        /// <summary>고른 뒤: 본문(결과가 붙었다)을 다시 그리고 [확인]만 남긴다.</summary>
        private void ShowResult()
        {
            _body.text = _current.Body;
            _choiceRow.SetActive(false);
            _noticeRow.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body.rectTransform);
            FitToBody();
            _scroll.verticalNormalizedPosition = 0f; // 결과가 보이게 맨 아래로
            UIThemeApplier.ApplyNow(_root.transform);
        }

        private void Auto()
        {
            var mail = _current;
            MailActions.AutoFight(mail.Expedition); // 결과 편지가 다음 차례로 뜬다 — 먼저 만들어 두어 Close가 멈춤을 풀지 않게
            Confirm();
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
            if (_current.Kind == MailKind.Arrival || _current.IsPendingEvent) Later();
            else Confirm();
            return true;
        }

        // ---------- 화면 ----------

        private void Build()
        {
            _font = UITheme.Font;

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
            var rect = _panelRect = panel.GetComponent<RectTransform>();
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
            _title.resizeTextForBestFit = true; // 긴 퀘스트 제목은 두 줄로 넘치지 않고 작아진다
            _title.verticalOverflow = VerticalWrapMode.Truncate; // 크기 맞춤은 칸을 넘치지 못하게 해야 동작한다
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.resizeTextMinSize = UITheme.HeadingSize - 2;
            _title.resizeTextMaxSize = UITheme.TitleSize;
            Place(_title.rectTransform, new Vector2(22f, -48f), new Vector2(Width - 44f, 36f));

            // 본문(길면 스크롤)
            var view = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            view.transform.SetParent(panel.transform, false);
            _bodyView = view.GetComponent<RectTransform>();
            Place(_bodyView, new Vector2(18f, -92f), new Vector2(Width - 36f, Height - ChromeHeight));
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

            _choiceRow = CreateButtonRow(panel.transform);
            _choiceRow.SetActive(false);

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

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color? color)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color ?? new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍(초록이면 초록) 버튼으로
            go.GetComponent<LayoutElement>().minHeight = 46f;
            var text = CreateText(go.transform, label, 17, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
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
