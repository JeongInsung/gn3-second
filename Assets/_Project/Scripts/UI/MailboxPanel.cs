using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 우편함 창: 모든 편지(Mailbox)를 최신이 위로 보여 주고, 고른 편지의 본문을 아래에 띄운다.
    /// 탭: 전체·중요·도착(선택 대기)·퀘스트·보고·알림. 선택 대기 도착은 본문 아래 [자동 진행] [직접 진행]으로 처리한다.
    /// 열면 일반 알림만 읽음 처리하고, 퀘스트·보고·중요·도착은 눌러 봐야 읽음이 된다([모두 읽음]도 있다).
    /// 화면 오른쪽 아래 우편함 아이콘(MailboxButton) 바로 위에 뜨고, 아이콘·닫기·ESC로 닫는다.
    /// </summary>
    public class MailboxPanel : MonoBehaviour
    {
        private const float Width = 680f;
        private const float Height = 560f;
        private const float RowHeight = 30f;
        private const int MaxRows = 150;

        private enum Tab { All, Important, Arrival, Quest, Report, Notice }
        private static readonly (Tab Tab, string Label)[] Tabs =
        {
            (Tab.All, "전체"), (Tab.Important, "중요"), (Tab.Arrival, "도착"),
            (Tab.Quest, "퀘스트"), (Tab.Report, "보고"), (Tab.Notice, "알림"),
        };

        private static MailboxPanel _instance;

        private GameObject _panel;
        private Text _title;
        private readonly List<Text> _tabLabels = new List<Text>();
        private RectTransform _list;
        private Text _detailTitle;
        private Text _detailWhen;
        private Text _detailBody;
        private ScrollRect _detailScroll;
        private GameObject _actionRow;
        private Font _font;

        private Tab _tab = Tab.All;
        private Mailbox.Mail _selected;
        private bool _dirty;

        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        public static void Toggle()
        {
            if (IsOpen) _instance.Hide();
            else Open();
        }

        /// <summary>우편함을 연다. select가 있으면 그 편지를 고른 채로(중요 알림창의 [우편함 열기]).</summary>
        public static void Open(Mailbox.Mail select = null)
        {
            if (_instance == null) _instance = Create();
            _instance.Show(select);
        }

        private static MailboxPanel Create()
        {
            var go = new GameObject("Mailbox", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MailboxPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<MailboxPanel>();
            panel.Build();
            return panel;
        }

        private void OnEnable() => Mailbox.Changed += MarkDirty;
        private void OnDisable() => Mailbox.Changed -= MarkDirty;
        private void MarkDirty() => _dirty = true;

        private void LateUpdate()
        {
            if (!_dirty || !IsOpen) return;
            Mailbox.MarkAllRead(MailKind.Notice); // 열린 창에 바로 보이는 일반 알림은 읽은 것으로
            _dirty = false;
            Refresh();
        }

        private void Show(Mailbox.Mail select)
        {
            if (select != null)
            {
                _selected = select;
                Mailbox.MarkRead(select);
                if (!InTab(select, _tab)) _tab = Tab.All;
            }
            Mailbox.MarkAllRead(MailKind.Notice);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
            _dirty = false;
        }

        private void Hide() => _panel.SetActive(false);

        private void Select(Mailbox.Mail mail)
        {
            _selected = mail;
            Mailbox.MarkRead(mail); // Changed → 다음 LateUpdate에 다시 그린다
            _dirty = true;
        }

        private void SetTab(Tab tab)
        {
            _tab = tab;
            Refresh();
        }

        private static bool InTab(Mailbox.Mail mail, Tab tab) => tab switch
        {
            Tab.Important => mail.Important,
            Tab.Arrival => mail.IsPendingChoice,
            Tab.Quest => mail.Kind == MailKind.QuestResult,
            Tab.Report => mail.Kind == MailKind.Report,
            Tab.Notice => mail.Kind == MailKind.Notice,
            _ => true,
        };

        private void Refresh()
        {
            _title.text = $"우편함 (안 읽음 {Mailbox.UnreadCount})";
            for (int i = 0; i < Tabs.Length; i++)
            {
                var (tab, label) = Tabs[i];
                int count = tab == Tab.Arrival ? Mailbox.PendingChoiceCount : 0;
                string text = count > 0 ? $"{label} {count}" : label;
                _tabLabels[i].text = tab == _tab ? $"● {text}" : text;
            }

            // 목록: 최신이 위. Destroy는 프레임 끝이라 먼저 떼어 낸다.
            var old = new List<Transform>();
            foreach (Transform child in _list) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            var mails = Mailbox.All.Where(m => InTab(m, _tab)).Reverse().Take(MaxRows).ToList();
            if (mails.Count == 0) CreateEmptyRow();
            foreach (var mail in mails) CreateRow(mail);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            UIThemeApplier.ApplyNow(_list);

            ShowDetail(_selected != null && Mailbox.All.Contains(_selected) ? _selected : null);
        }

        private void ShowDetail(Mailbox.Mail mail)
        {
            if (mail == null)
            {
                _detailTitle.text = "편지를 고르세요";
                _detailWhen.text = "";
                _detailBody.text = "";
                _actionRow.SetActive(false);
                return;
            }
            _detailTitle.text = $"{Mailbox.Colored($"[{Mailbox.KindTag(mail.Kind)}]", Mailbox.KindColor(mail.Kind))} {mail.Title}";
            _detailWhen.text = Mailbox.FormatWhen(mail);
            _detailBody.text = string.IsNullOrEmpty(mail.Body) ? mail.Title : mail.Body;
            _actionRow.SetActive(mail.IsPendingChoice);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_detailBody.rectTransform);
            _detailScroll.verticalNormalizedPosition = 1f;
        }

        private void Auto()
        {
            if (_selected == null) return;
            MailActions.AutoFight(_selected.Expedition); // 결과 편지(중요)는 중요 알림창이 게임을 멈추고 띄운다
        }

        private void Direct()
        {
            if (_selected == null) return;
            MailActions.DirectFight(_selected.Expedition);
        }

        // ---------- 화면 ----------

        private void CreateRow(Mailbox.Mail mail)
        {
            var go = new GameObject("MailRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_list, false);
            go.GetComponent<LayoutElement>().minHeight = RowHeight;
            go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.24f, 0.9f);
            go.GetComponent<Button>().onClick.AddListener(() => Select(mail));

            string mark = mail.Read ? "   " : "● ";
            string tag = Mailbox.Colored($"[{Mailbox.KindTag(mail.Kind)}]", Mailbox.KindColor(mail.Kind));
            string title = mail.Read ? mail.Title : $"<b>{mail.Title}</b>";
            string pending = mail.IsPendingChoice ? " " + Mailbox.Colored("선택 대기", new Color(1f, 0.6f, 0.2f)) : "";
            string selected = mail == _selected ? "▶ " : "";
            var text = CreateText(go.transform, $"{selected}{mark}{tag} {title}{pending}  {Mailbox.Colored(Mailbox.FormatWhen(mail), UITheme.MutedText)}",
                14, TextAnchor.MiddleLeft, UITheme.BodyText);
            text.supportRichText = true;
            text.verticalOverflow = VerticalWrapMode.Truncate; // 한 줄만
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 0f);
            text.rectTransform.offsetMax = new Vector2(-10f, 0f);
        }

        private void CreateEmptyRow()
        {
            var text = CreateText(_list, "편지가 없습니다.", 14, TextAnchor.MiddleLeft, UITheme.MutedText);
            text.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _panel = new GameObject("MailboxPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f); // 오른쪽 아래 우편함 바로 위
            rect.anchoredPosition = new Vector2(-MailboxButton.Margin, MailboxButton.Margin + MailboxButton.Size + 8f);
            rect.sizeDelta = new Vector2(Width, Height);
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(rect);

            _title = CreateText(_panel.transform, "", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            _title.fontStyle = FontStyle.Bold;
            Place(_title.rectTransform, new Vector2(20f, -14f), new Vector2(360f, 34f));
            CreateButton(_panel.transform, "모두 읽음", () => Mailbox.MarkAllRead(), new Vector2(Width - 236f, -14f), new Vector2(110f, 32f));
            CreateButton(_panel.transform, "닫기", Hide, new Vector2(Width - 118f, -14f), new Vector2(100f, 32f));

            // 탭
            float tabWidth = (Width - 40f - 5 * 6f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tab = Tabs[i].Tab;
                var label = CreateButton(_panel.transform, Tabs[i].Label, () => SetTab(tab),
                    new Vector2(20f + i * (tabWidth + 6f), -56f), new Vector2(tabWidth, 30f));
                _tabLabels.Add(label);
            }

            // 목록
            _list = CreateScroll(_panel.transform, "List", new Vector2(18f, -94f), new Vector2(Width - 36f, 250f), out _);
            var layout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 3f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 본문
            _detailTitle = CreateText(_panel.transform, "", 17, TextAnchor.MiddleLeft, UITheme.TitleText);
            _detailTitle.fontStyle = FontStyle.Bold;
            _detailTitle.supportRichText = true;
            _detailTitle.verticalOverflow = VerticalWrapMode.Truncate;
            Place(_detailTitle.rectTransform, new Vector2(20f, -352f), new Vector2(Width - 230f, 28f));
            _detailWhen = CreateText(_panel.transform, "", 13, TextAnchor.MiddleRight, UITheme.MutedText);
            Place(_detailWhen.rectTransform, new Vector2(Width - 210f, -352f), new Vector2(190f, 28f));

            var bodyContent = CreateScroll(_panel.transform, "Detail", new Vector2(18f, -384f), new Vector2(Width - 36f, 120f), out _detailScroll);
            _detailBody = bodyContent.gameObject.AddComponent<Text>();
            _detailBody.font = _font;
            _detailBody.fontSize = 15;
            _detailBody.color = UITheme.BodyText;
            _detailBody.supportRichText = true;
            _detailBody.alignment = TextAnchor.UpperLeft;
            _detailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailBody.verticalOverflow = VerticalWrapMode.Overflow;
            _detailBody.raycastTarget = false;
            bodyContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _actionRow = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _actionRow.transform.SetParent(_panel.transform, false);
            var rowRect = _actionRow.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, 12f);
            rowRect.sizeDelta = new Vector2(360f, 38f);
            var h = _actionRow.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            CreateButton(_actionRow.transform, "자동 진행", Auto, null, null, new Color(0.25f, 0.55f, 0.35f, 1f));
            CreateButton(_actionRow.transform, "직접 진행", Direct, null, null);
            _actionRow.SetActive(false);

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        /// <summary>스크롤 칸(어두운 바탕)을 만들고 내용 RectTransform(위에서부터 쌓임)을 돌려준다.</summary>
        private RectTransform CreateScroll(Transform parent, string name, Vector2 topLeft, Vector2 size, out ScrollRect scroll)
        {
            var view = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            view.transform.SetParent(parent, false);
            Place(view.GetComponent<RectTransform>(), topLeft, size);
            view.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f); // UIThemeApplier가 어두운 칸으로
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(view.transform, false);
            var rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, -8f);
            scroll = view.GetComponent<ScrollRect>();
            scroll.content = rect;
            scroll.horizontal = false;
            SmoothWheelScroll.Attach(scroll);
            return rect;
        }

        /// <summary>버튼을 만들고 글자 Text를 돌려준다. topLeft가 없으면 레이아웃 그룹 안에 넣는다.</summary>
        private Text CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Vector2? topLeft, Vector2? size, Color? color = null)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            if (topLeft.HasValue) Place(go.GetComponent<RectTransform>(), topLeft.Value, size ?? new Vector2(100f, 32f));
            go.GetComponent<Image>().color = color ?? new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍(초록이면 초록) 버튼으로
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var text = CreateText(go.transform, label, 15, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return text;
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
