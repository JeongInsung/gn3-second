using System.Linq;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 의뢰 기록 창(H · 메뉴 [의뢰 기록]): 요약(총 건수 · 성공률 · 총 보상) · 레벨별 성공/시도 · [전체/성공/실패] ·
    /// 끝낸 의뢰 목록(최근 것이 위: 날짜 · 태그 · 레벨 · 제목 · 승패 · 보상 / 대원 · MVP · 전사). 기록은 QuestHistory(최근 100건).
    /// GoldLedgerWindow와 같은 구조: 처음 열 때 자기 캔버스를 만들고, X·ESC로 닫는다.
    /// </summary>
    public class QuestHistoryWindow : MonoBehaviour
    {
        private const float Width = 860f, Height = 680f;
        private const float TopHeight = 150f; // 제목 · 요약 · 레벨별 · 탭(고정) — 그 아래는 스크롤
        private const int SortingOrder = 2;
        private static QuestHistoryWindow _instance;

        private enum Filter { All, Win, Lose }

        private GameObject _panel;
        private Filter _filter;

        public static bool IsOpen => _instance != null && _instance._panel != null;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (_instance == null) Create();
            _instance.Build();
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private static void Create()
        {
            var go = new GameObject("QuestHistoryWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(QuestHistoryWindow));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _instance = go.GetComponent<QuestHistoryWindow>();
        }

        private void SetFilter(Filter filter)
        {
            _filter = filter;
            Build();
        }

        private void Build()
        {
            Vector2? moved = _panel != null ? DraggablePanel.MovedPosition((RectTransform)_panel.transform) : null; // 탭을 바꿔도 옮긴 자리 그대로
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

            // 위(고정)
            var top = new GameObject("Top", typeof(RectTransform), typeof(VerticalLayoutGroup));
            top.transform.SetParent(_panel.transform, false);
            var topRect = (RectTransform)top.transform;
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = Vector2.one;
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.offsetMin = new Vector2(20f, -TopHeight);
            topRect.offsetMax = new Vector2(-52f, -14f);
            var topLayout = top.GetComponent<VerticalLayoutGroup>();
            topLayout.spacing = 6f;
            topLayout.childControlWidth = topLayout.childControlHeight = true;
            topLayout.childForceExpandWidth = true;
            topLayout.childForceExpandHeight = false;

            var title = ShopListUI.CreateText(top.transform, "<b>의뢰 기록</b>", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;

            var all = QuestHistory.Entries;
            int wins = QuestHistory.Wins;
            int rate = all.Count > 0 ? Mathf.RoundToInt(100f * wins / all.Count) : 0;
            var summary = ShopListUI.CreateText(top.transform,
                $"총 {all.Count}건 · 성공 <color=#7fd36b>{wins}</color> · 실패 <color=#e0675a>{all.Count - wins}</color> ({rate}%) · 총 보상 <color=#ffd36b>{QuestHistory.TotalReward}G</color>",
                16, TextAnchor.MiddleLeft, UITheme.BodyText);
            summary.supportRichText = true;
            var byLevel = QuestHistory.ByLevel();
            var levels = ShopListUI.CreateText(top.transform,
                byLevel.Count > 0 ? "레벨별 성공  " + string.Join("  ·  ", byLevel.Select(l => $"Lv{l.level} {l.wins}/{l.tries}")) : "아직 끝낸 의뢰가 없다.",
                14, TextAnchor.MiddleLeft, UITheme.MutedText);
            levels.horizontalOverflow = HorizontalWrapMode.Wrap;

            var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tabs.transform.SetParent(top.transform, false);
            tabs.GetComponent<LayoutElement>().minHeight = 36f;
            var h = tabs.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Tab(tabs.transform, "전체", Filter.All);
            Tab(tabs.transform, "성공", Filter.Win);
            Tab(tabs.transform, "실패", Filter.Lose);

            // 아래(스크롤): 최근 것이 위
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.padding = new RectOffset(0, 8, 0, 8);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var shown = all.Where(e => _filter == Filter.All || e.Win == (_filter == Filter.Win)).Reverse().ToList();
            if (shown.Count == 0)
            {
                var empty = ShopListUI.CreateText(content.transform, all.Count == 0 ? "의뢰를 끝내면 여기에 쌓인다." : "해당하는 의뢰가 없다.", 15, TextAnchor.MiddleLeft, UITheme.MutedText);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            foreach (var e in shown) EntryRow(content.transform, e);
            ScrollListWrapper.Wrap((RectTransform)content.transform, new Vector2(20f, 18f), new Vector2(-20f, -TopHeight - 10f));

            UITheme.CreateCloseX(_panel.transform, Close);
            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private void Tab(Transform parent, string label, Filter filter)
        {
            var button = ShopListUI.CreateButton(parent, label, () => SetFilter(filter), 80f);
            if (_filter == filter) UITheme.MarkSelected(button); // 고른 탭: 흰 테두리 + 금빛 글씨(다른 창과 같게)
        }

        /// <summary>한 건: 바탕 칸 안에 두 줄(날짜·태그·레벨·제목·승패·보상 / 대원·MVP·전사).</summary>
        private static void EntryRow(Transform parent, QuestHistory.Entry e)
        {
            var row = new GameObject("Entry", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);
            row.GetComponent<Image>().color = e.Win ? new Color(0.2f, 0.35f, 0.25f, 0.35f) : new Color(0.4f, 0.18f, 0.15f, 0.35f);
            var v = row.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 8, 8);
            v.spacing = 3f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            string tags = (e.Challenge ? "<color=#ffd36b>[도전]</color> " : "") + (e.Joint ? "<color=#6ab8ff>[연합]</color> " : "");
            string result = e.Win ? $"<color=#7fd36b>성공</color>  <color=#ffd36b>+{e.Reward}G</color>" : "<color=#e0675a>실패</color>";
            var head = ShopListUI.CreateText(row.transform, $"<color=#a3937c>{GameCalendar.Format(e.Day)}</color>   {tags}<b>Lv{e.Level} {e.Title}</b>   {result}", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            head.supportRichText = true;

            string members = e.Members.Count > 0 ? "대원: " + string.Join(", ", e.Members.Select(n => n == e.Mvp ? $"<color=#ffd36b>★{n}(MVP)</color>" : n)) : "대원 기록 없음";
            if (e.Fallen.Count > 0) members += $"   <color=#e0675a>전사: {string.Join(", ", e.Fallen)}</color>";
            if (e.HasStats) members += "   <color=#a3937c>· 눌러서 전투 통계 ▼</color>";
            var sub = ShopListUI.CreateText(row.transform, members, 14, TextAnchor.MiddleLeft, UITheme.MutedText);
            sub.supportRichText = true;

            // 누르면 줄 안에 그 전투의 대원별 딜량·힐량·받은 피해 세 칸을 펼친다(처음 누를 때 만들고, 다시 누르면 접는다 — 창을 다시 만들지 않아 스크롤 그대로)
            if (!e.HasStats) return;
            GameObject stats = null;
            RowClickHandler.Attach(row, () =>
            {
                if (stats == null) stats = BuildStats(row.transform, e);
                else stats.SetActive(!stats.activeSelf);
                sub.text = sub.text.Replace(stats.activeSelf ? "전투 통계 ▼" : "전투 통계 ▲", stats.activeSelf ? "전투 통계 ▲" : "전투 통계 ▼");
            });
        }

        private static readonly string[] StatLabels = { "딜량", "힐량", "받은 피해" };
        private static readonly Color[] StatColors =
        {
            new Color(0.92f, 0.45f, 0.3f), new Color(0.45f, 0.85f, 0.45f), new Color(0.5f, 0.65f, 0.95f), // 전투 통계 그래프와 같은 색
        };
        private const float StatRowHeight = 18f, StatNameWidth = 90f, StatValueWidth = 50f;

        /// <summary>세 칸 나란히: 칸마다 제목 + 대원별 이름 · 막대(그 칸 1등 기준) · 값, 큰 순서.</summary>
        private static GameObject BuildStats(Transform row, QuestHistory.Entry e)
        {
            var block = new GameObject("Stats", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            block.transform.SetParent(row, false);
            var h = block.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.padding = new RectOffset(0, 0, 6, 2);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            var columns = new[] { e.Dealt, e.Healed, e.Taken };
            for (int k = 0; k < columns.Length; k++)
            {
                var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
                column.transform.SetParent(block.transform, false);
                var v = column.GetComponent<VerticalLayoutGroup>();
                v.spacing = 2f;
                v.childControlWidth = v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
                var title = ShopListUI.CreateText(column.transform, $"<b>{StatLabels[k]}</b>", 14, TextAnchor.MiddleLeft, StatColors[k]);
                title.supportRichText = true;

                var values = columns[k];
                int top = Mathf.Max(1, values.Count > 0 ? values.Max() : 1);
                foreach (int i in Enumerable.Range(0, e.Members.Count).OrderByDescending(i => values[i]))
                    StatRow(column.transform, e.Members[i], values[i], (float)values[i] / top, StatColors[k], e.Fallen.Contains(e.Members[i]));
            }
            return block;
        }

        private static void StatRow(Transform parent, string name, int value, float ratio, Color color, bool fallen)
        {
            var go = new GameObject("StatRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = StatRowHeight;

            var label = ShopListUI.CreateText(go.transform, name, 12, TextAnchor.MiddleLeft, fallen ? UITheme.MutedText : UITheme.BodyText);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(StatNameWidth, 0f);

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(go.transform, false);
            track.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            track.GetComponent<Image>().raycastTarget = false;
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0f, 0.2f);
            trackRect.anchorMax = new Vector2(1f, 0.8f);
            trackRect.offsetMin = new Vector2(StatNameWidth + 4f, 0f);
            trackRect.offsetMax = new Vector2(-(StatValueWidth + 4f), 0f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            fill.GetComponent<Image>().color = color;
            fill.GetComponent<Image>().raycastTarget = false;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

            var number = ShopListUI.CreateText(go.transform, value.ToString(), 12, TextAnchor.MiddleRight, UITheme.BodyText);
            number.horizontalOverflow = HorizontalWrapMode.Overflow;
            var numberRect = number.rectTransform;
            numberRect.anchorMin = new Vector2(1f, 0f);
            numberRect.anchorMax = Vector2.one;
            numberRect.pivot = new Vector2(1f, 0.5f);
            numberRect.offsetMin = new Vector2(-StatValueWidth, 0f);
            numberRect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
