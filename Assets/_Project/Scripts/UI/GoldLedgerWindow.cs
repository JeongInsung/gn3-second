using System.Linq;
using GN3.Economy;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 금고 장부 창(G · 메뉴 [장부] · 골드 표시 클릭): 날짜 고르기 [◀ n일째 ▶] · 그날 수입/지출/순익 ·
    /// 이유별 막대(수입 초록 · 지출 붉은색, 큰 순) · 그날 거래 목록(시각 · 이유 · ±금액). 기록은 GoldLedger(최근 14일).
    /// MercenaryRecordWindow와 같은 구조: 처음 열 때 자기 캔버스를 만들고, X·ESC로 닫는다.
    /// </summary>
    public class GoldLedgerWindow : MonoBehaviour
    {
        private const float Width = 720f, Height = 640f;
        private const float TopHeight = 136f;  // 제목 · 날짜 · 합계(고정) — 그 아래는 스크롤
        private const float BarMaxWidth = 300f;
        private const int SortingOrder = 2;
        private static readonly Color IncomeColor = new Color(0.45f, 0.82f, 0.4f);
        private static readonly Color ExpenseColor = new Color(0.9f, 0.4f, 0.35f);
        private static GoldLedgerWindow _instance;

        private GameObject _panel;
        private int _day;

        public static bool IsOpen => _instance != null && _instance._panel != null;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (_instance == null) Create();
            _instance._day = GameClock.CurrentDay;
            _instance.Build();
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private static void Create()
        {
            var go = new GameObject("GoldLedgerWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GoldLedgerWindow));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _instance = go.GetComponent<GoldLedgerWindow>();
        }

        private int OldestDay => Mathf.Max(1, GameClock.CurrentDay - GoldLedger.KeepDays + 1);

        private void ShowDay(int day)
        {
            _day = Mathf.Clamp(day, OldestDay, GameClock.CurrentDay);
            Build();
        }

        private void Build()
        {
            Vector2? moved = _panel != null ? DraggablePanel.MovedPosition((RectTransform)_panel.transform) : null; // 날짜를 넘겨도 옮긴 자리 그대로
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            EscapeCloser.Register(_panel, Close, justOpened: true); // 열 때마다 새로 만든다 — 열리면 다른 창을 닫게
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, Height);
            DraggablePanel.Attach(rect, moved); // 잡고 끌어 옮길 수 있다
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;

            // 위(고정): 제목 · [◀ 날짜 ▶] · 그날 합계
            var top = new GameObject("Top", typeof(RectTransform), typeof(VerticalLayoutGroup));
            top.transform.SetParent(_panel.transform, false);
            var topRect = (RectTransform)top.transform;
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = Vector2.one;
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.offsetMin = new Vector2(20f, -TopHeight);
            topRect.offsetMax = new Vector2(-52f, -14f);
            var topLayout = top.GetComponent<VerticalLayoutGroup>();
            topLayout.spacing = 8f;
            topLayout.childControlWidth = topLayout.childControlHeight = true;
            topLayout.childForceExpandWidth = true;
            topLayout.childForceExpandHeight = false;

            var title = ShopListUI.CreateText(top.transform, "<b>금고 장부</b>", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;

            var nav = Row(top.transform, 36f);
            var prev = ShopListUI.CreateButton(nav.transform, "◀", () => ShowDay(_day - 1), 44f);
            prev.interactable = _day > OldestDay;
            var dayText = ShopListUI.CreateText(nav.transform, $"<b>{GameCalendar.Format(_day)}</b>" + (_day == GameClock.CurrentDay ? "  <color=#a3937c>(오늘)</color>" : ""),
                17, TextAnchor.MiddleCenter, UITheme.TitleText);
            dayText.supportRichText = true;
            dayText.gameObject.AddComponent<LayoutElement>().minWidth = 220f;
            var next = ShopListUI.CreateButton(nav.transform, "▶", () => ShowDay(_day + 1), 44f);
            next.interactable = _day < GameClock.CurrentDay;
            var today = ShopListUI.CreateButton(nav.transform, "오늘", () => ShowDay(GameClock.CurrentDay), 64f);
            today.interactable = _day < GameClock.CurrentDay;

            var totals = GoldLedger.Totals(_day, _day);
            int income = totals.Sum(t => t.income), expense = totals.Sum(t => t.expense), net = income - expense;
            var sum = ShopListUI.CreateText(top.transform,
                $"수입 <color=#7fd36b>+{income}G</color>   ·   지출 <color=#e0675a>-{expense}G</color>   ·   순 {(net >= 0 ? $"<color=#7fd36b>+{net}G</color>" : $"<color=#e0675a>{net}G</color>")}",
                16, TextAnchor.MiddleLeft, UITheme.BodyText);
            sum.supportRichText = true;

            // 아래(스크롤): 이유별 막대 → 거래 목록
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.padding = new RectOffset(0, 8, 0, 8);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var entries = GoldLedger.OnDay(_day);
            if (entries.Count == 0)
            {
                Line(content.transform, _day < GoldLedger.FirstDay && GoldLedger.Entries.Count > 0
                    ? "이 날은 기록이 남아 있지 않다." : "이 날은 골드가 들고 나지 않았다.", UITheme.MutedText);
            }
            else
            {
                Heading(content.transform, "이유별");
                int max = Mathf.Max(1, totals.Max(t => Mathf.Max(t.income, t.expense)));
                foreach (var t in totals.Where(t => t.income > 0).OrderByDescending(t => t.income))
                    ReasonRow(content.transform, t.reason, t.income, max, true);
                foreach (var t in totals.Where(t => t.expense > 0).OrderByDescending(t => t.expense))
                    ReasonRow(content.transform, t.reason, t.expense, max, false);

                Heading(content.transform, $"거래 {entries.Count}건");
                foreach (var e in entries.AsEnumerable().Reverse()) // 최근 것이 위
                {
                    string amount = e.Amount > 0 ? $"<color=#7fd36b>+{e.Amount}G</color>" : $"<color=#e0675a>{e.Amount}G</color>";
                    Line(content.transform, $"<color=#a3937c>{e.Hour:00}:00</color>   {e.Reason}   {amount}", UITheme.BodyText);
                }
            }
            ScrollListWrapper.Wrap((RectTransform)content.transform, new Vector2(20f, 18f), new Vector2(-20f, -TopHeight - 10f));

            UITheme.CreateCloseX(_panel.transform, Close);
            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private static GameObject Row(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = height;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return row;
        }

        /// <summary>이유 한 줄: [이유] [막대(금액 비례)] [±금액].</summary>
        private static void ReasonRow(Transform parent, string reason, int amount, int max, bool income)
        {
            var row = Row(parent, 24f);
            var label = ShopListUI.CreateText(row.transform, reason, 15, TextAnchor.MiddleLeft, UITheme.BodyText);
            label.gameObject.AddComponent<LayoutElement>().minWidth = 120f;
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(row.transform, false);
            var image = bar.GetComponent<Image>();
            image.color = income ? IncomeColor : ExpenseColor;
            image.type = Image.Type.Filled; // 채움 막대: UIThemeApplier가 어두운 칸으로 덮지 않는다(그림이 없으면 그냥 색 칸)
            image.fillAmount = 1f;
            image.raycastTarget = false;
            var barLayout = bar.GetComponent<LayoutElement>();
            barLayout.minWidth = barLayout.preferredWidth = Mathf.Max(3f, BarMaxWidth * amount / max);
            barLayout.minHeight = barLayout.preferredHeight = 14f;
            var value = ShopListUI.CreateText(row.transform, income ? $"+{amount}G" : $"-{amount}G", 15, TextAnchor.MiddleLeft, income ? IncomeColor : ExpenseColor);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private static void Heading(Transform parent, string text)
        {
            var t = ShopListUI.CreateText(parent, $"<b>{text}</b>", 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            t.supportRichText = true;
        }

        private static void Line(Transform parent, string text, Color color)
        {
            var t = ShopListUI.CreateText(parent, text, 15, TextAnchor.MiddleLeft, color);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
