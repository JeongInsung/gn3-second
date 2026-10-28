using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을 일정표(C · 메뉴 [일정]): 앞으로 Days일 안의 일정을 날짜순으로 — 떠돌이·암거래 상인과 의뢰 중개인 방문, 중개인 의뢰 마감,
    /// 주급날(금화가 모자라면 빨강), 계약 만료, 생일(은퇴할 생일은 따로). 날짜는 이미 여러 곳에 있지만 보여 주는 곳이 없었다.
    /// GoldLedgerWindow와 같은 구조: 처음 열 때 자기 캔버스를 만들고, X·ESC로 닫는다. 끌어 옮길 수 있다.
    /// </summary>
    public class CalendarPanel : MonoBehaviour
    {
        private const int Days = 14;
        private const float Width = 640f, Height = 620f, TopHeight = 70f;
        private const int SortingOrder = 2;
        private static CalendarPanel _instance;

        private static readonly Color VisitColor = new Color(0.55f, 0.8f, 1f);
        private static readonly Color MoneyColor = new Color(1f, 0.83f, 0.42f);
        private static readonly Color WarnColor = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color PersonColor = new Color(0.75f, 0.9f, 0.6f);

        private GameObject _panel;

        public static bool IsOpen => _instance != null && _instance._panel != null;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (_instance == null)
            {
                var go = new GameObject("CalendarPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CalendarPanel));
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                _instance = go.GetComponent<CalendarPanel>();
            }
            _instance.Build();
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private struct Item
        {
            public int Day;
            public string Text;
            public Color Color;
        }

        /// <summary>오늘부터 Days일 안의 일정(날짜순, 같은 날은 넣은 순서).</summary>
        private static List<Item> Collect()
        {
            int today = GameClock.CurrentDay, last = today + Days;
            var items = new List<Item>();
            void Add(int day, string text, Color color)
            {
                if (day >= today && day <= last) items.Add(new Item { Day = day, Text = text, Color = color });
            }

            // 찾아오는 사람들(와 있으면 오늘)
            Add(TravelingMerchant.IsHere ? today : TravelingMerchant.SavedNextDay, TravelingMerchant.IsHere ? "떠돌이 상인이 와 있다(오늘까지)" : "떠돌이 상인 방문", VisitColor);
            Add(BlackMarket.IsHere ? today : BlackMarket.SavedNextDay, BlackMarket.IsHere ? "암거래 상인이 와 있다(오늘까지) — 전리품을 비싸게 판다" : "암거래 상인 방문 — 전리품을 모아 두자", VisitColor);
            Add(QuestBroker.IsHere ? today : QuestBroker.SavedNextDay, QuestBroker.IsHere ? "의뢰 중개인이 와 있다" : "의뢰 중개인 방문", VisitColor);
            foreach (var quest in QuestBroker.Quests)
            {
                int expire = QuestBroker.ExpireDayOf(quest);
                if (expire >= 0) Add(expire, $"중개인 의뢰 마감: {quest.Title}", WarnColor);
            }

            // 주급날: 금화가 모자라면 빨강
            int wage = MercenaryCondition.TotalWeeklyWage();
            if (wage > 0)
            {
                bool short_ = Wallet.Gold < wage;
                Add(MercenaryCondition.NextPayday, $"주급날 · {wage}G" + (short_ ? $"  (지금 {wage - Wallet.Gold}G 부족)" : ""), short_ ? WarnColor : MoneyColor);
            }

            // 용병: 계약 만료 · 생일(은퇴할 생일은 경고)
            int todayIndex = MercenaryAge.TodayIndex;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive))
            {
                if (!Loans.IsGuest(merc)) Add(today + Contracts.RemainingDays(merc), $"{merc.Name} 계약 만료 — 재계약할지 정하자", WarnColor);
                int untilBirthday = (merc.BirthdayIndex - todayIndex + GameCalendar.DaysPerYear) % GameCalendar.DaysPerYear;
                bool retires = merc.Age + 1 >= GrowthTuning.RetireAge;
                Add(today + untilBirthday, retires ? $"{merc.Name} 생일 — {merc.Age + 1}세가 되어 은퇴한다" : $"{merc.Name} 생일 ({merc.Age + 1}세)",
                    retires ? WarnColor : PersonColor);
            }
            return items.OrderBy(i => i.Day).ToList();
        }

        private void Build()
        {
            Vector2? moved = _panel != null ? DraggablePanel.MovedPosition((RectTransform)_panel.transform) : null;
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            EscapeCloser.Register(_panel, Close);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, Height);
            DraggablePanel.Attach(rect, moved); // 잡고 끌어 옮길 수 있다
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;

            var title = ShopListUI.CreateText(_panel.transform,
                $"<b>마을 일정</b>  <size=14><color=#a3937c>오늘 {GameCalendar.Format(GameClock.CurrentDay)}부터 {Days}일</color></size>",
                22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(20f, -TopHeight + 10f);
            titleRect.offsetMax = new Vector2(-52f, -14f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.padding = new RectOffset(0, 8, 0, 8);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var items = Collect();
            if (items.Count == 0)
                ShopListUI.CreateText(content.transform, $"앞으로 {Days}일 안에 정해진 일정이 없다.", 15, TextAnchor.MiddleLeft, UITheme.MutedText);
            int lastDay = -1;
            foreach (var item in items)
            {
                if (item.Day != lastDay)
                {
                    lastDay = item.Day;
                    int d = item.Day - GameClock.CurrentDay;
                    string when = d == 0 ? "오늘" : d == 1 ? "내일" : $"D-{d}";
                    var header = ShopListUI.CreateText(content.transform, $"<b>{when}</b>  <color=#a3937c>{GameCalendar.Format(item.Day)}</color>", 16, TextAnchor.MiddleLeft, UITheme.TitleText);
                    header.supportRichText = true;
                    header.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
                }
                var line = ShopListUI.CreateText(content.transform, "   · " + item.Text, 15, TextAnchor.MiddleLeft, item.Color);
                line.gameObject.AddComponent<LayoutElement>().minHeight = 22f;
            }
            ScrollListWrapper.Wrap((RectTransform)content.transform, new Vector2(20f, 18f), new Vector2(-20f, -TopHeight));

            UITheme.CreateCloseX(_panel.transform, Close);
            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;
    }
}
