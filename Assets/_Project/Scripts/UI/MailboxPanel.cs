using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 우편함 창: 모든 편지(Mailbox)를 최신이 위로, 짧은 제목(Mail.ShortTitle)만 한 줄씩 보여 준다.
    /// 줄을 누르면 그 바로 아래에 내용이 펼쳐지고(다시 누르면 접힘), 선택 대기 도착은 펼친 칸 안의 [자동 진행] [직접 진행]으로 처리한다.
    /// 탭: 전체·중요·도착(선택 대기)·퀘스트·보고·알림.
    /// 열면 일반 알림만 읽음 처리하고, 퀘스트·보고·중요·도착은 눌러 봐야 읽음이 된다([모두 읽음]도 있다).
    /// 화면 오른쪽 아래 우편함 아이콘(MailboxButton) 바로 위에 뜨고, 아이콘·닫기·ESC로 닫는다.
    /// </summary>
    public class MailboxPanel : MonoBehaviour
    {
        private const float Width = 680f;
        private const float Height = 560f;
        private const float ListTop = 94f;
        private const float RowHeight = 30f;
        private const float WhenWidth = 170f;
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
        private ScrollRect _listScroll;
        private Font _font;

        private Tab _tab = Tab.All;
        private Mailbox.Mail _selected; // 펼친 편지(없으면 null)
        private bool _dirty;
        private bool _scrollToSelected;

        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        public static void Toggle()
        {
            if (IsOpen) _instance.Hide();
            else Open();
        }

        /// <summary>우편함을 연다. select가 있으면 그 편지를 펼친 채로(중요 알림창의 [우편함 열기]).</summary>
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
                _scrollToSelected = true;
            }
            Mailbox.MarkAllRead(MailKind.Notice);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
            _dirty = false;
        }

        private void Hide() => _panel.SetActive(false);

        /// <summary>줄을 누르면 펼치고, 펼친 줄을 다시 누르면 접는다.</summary>
        private void ToggleExpand(Mailbox.Mail mail)
        {
            _selected = _selected == mail ? null : mail;
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
            if (_selected != null && !Mailbox.All.Contains(_selected)) _selected = null;

            var mails = Mailbox.All.Where(m => InTab(m, _tab)).Reverse().Take(MaxRows).ToList();
            if (mails.Count == 0) CreateEmptyRow();
            RectTransform selectedRow = null;
            foreach (var mail in mails)
            {
                var row = CreateRow(mail);
                if (mail != _selected) continue;
                selectedRow = row;
                CreateExpanded(mail);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            UIThemeApplier.ApplyNow(_list);

            if (_scrollToSelected && selectedRow != null) ScrollTo(selectedRow);
            _scrollToSelected = false;
        }

        /// <summary>row가 목록 칸 위쪽에 보이도록 스크롤한다.</summary>
        private void ScrollTo(RectTransform row)
        {
            float viewHeight = ((RectTransform)_listScroll.transform).rect.height;
            float contentHeight = _list.rect.height;
            if (contentHeight <= viewHeight) return;
            float top = -row.localPosition.y - row.rect.height * row.pivot.y; // 내용 위쪽에서 줄 위쪽까지
            _listScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(top / (contentHeight - viewHeight));
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

        /// <summary>한 줄: 왼쪽 "▶ ● [태그] 짧은 제목 선택 대기", 오른쪽 날짜·시각.</summary>
        private RectTransform CreateRow(Mailbox.Mail mail)
        {
            var go = new GameObject("MailRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_list, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = RowHeight;
            go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.24f, 0.9f);
            go.GetComponent<Button>().onClick.AddListener(() => ToggleExpand(mail));

            string arrow = mail == _selected ? "▼ " : "▶ ";
            string mark = mail.Read ? "" : "● ";
            string tag = Mailbox.Colored($"[{Mailbox.KindTag(mail.Kind)}]", Mailbox.KindColor(mail.Kind));
            string title = mail.Read ? mail.ShortTitle : $"<b>{mail.ShortTitle}</b>";
            string pending = mail.IsPendingChoice ? " " + Mailbox.Colored("선택 대기", new Color(1f, 0.6f, 0.2f)) : "";
            var text = CreateText(go.transform, $"{arrow}{mark}{tag} {title}{pending}", 14, TextAnchor.MiddleLeft, UITheme.BodyText);
            text.supportRichText = true;
            text.verticalOverflow = VerticalWrapMode.Truncate; // 한 줄만(넘치면 날짜 칸을 덮지 않고 잘린다)
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 0f);
            text.rectTransform.offsetMax = new Vector2(-(WhenWidth + 10f), 0f);

            var when = CreateText(go.transform, Mailbox.FormatWhen(mail), 13, TextAnchor.MiddleRight, UITheme.MutedText);
            when.horizontalOverflow = HorizontalWrapMode.Overflow;
            var whenRect = when.rectTransform;
            whenRect.anchorMin = new Vector2(1f, 0f);
            whenRect.anchorMax = Vector2.one;
            whenRect.pivot = new Vector2(1f, 0.5f);
            whenRect.offsetMin = new Vector2(-WhenWidth, 0f);
            whenRect.offsetMax = new Vector2(-10f, 0f);
            return go.GetComponent<RectTransform>();
        }

        /// <summary>펼친 편지의 내용 칸(줄 바로 아래). 높이는 내용만큼 늘어난다. 선택 대기 도착이면 [자동 진행] [직접 진행].</summary>
        private void CreateExpanded(Mailbox.Mail mail)
        {
            var box = new GameObject("MailBody", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            box.transform.SetParent(_list, false);
            box.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.9f); // UIThemeApplier가 어두운 칸으로
            var v = box.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 14, 10, 12);
            v.spacing = 10f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var body = CreateText(box.transform, Mailbox.FullText(mail), 15, TextAnchor.UpperLeft, UITheme.BodyText);
            body.supportRichText = true;

            if (!mail.IsPendingChoice) return;
            var actions = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            actions.transform.SetParent(box.transform, false);
            actions.GetComponent<LayoutElement>().preferredHeight = 34f;
            var h = actions.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.padding = new RectOffset(0, 200, 0, 0); // 버튼이 너무 넓어지지 않게 오른쪽을 비운다
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            CreateButton(actions.transform, "자동 진행", Auto, null, null, new Color(0.25f, 0.55f, 0.35f, 1f));
            CreateButton(actions.transform, "직접 진행", Direct, null, null);
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

            // 목록(펼친 내용까지 같은 칸 안에서 스크롤)
            _list = CreateScroll(_panel.transform, "List", new Vector2(18f, -ListTop), new Vector2(Width - 36f, Height - ListTop - 14f), out _listScroll);
            var layout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 3f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
