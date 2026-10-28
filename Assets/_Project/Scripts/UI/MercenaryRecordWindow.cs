using System.Linq;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 용병 기록 창(용병 정보의 "전적" 줄을 누르면): 요약(전투·승률·평균/최고 평점·처치·부상·질병) ·
    /// 은월의 숲 레벨별 의뢰 성공 · 최근 전투(날짜·의뢰·레벨·승패·평점·피해·처치·치유). 기록은 Mercenary.Record.
    /// MercenaryBagWindow와 같은 구조: 처음 열 때 자기 캔버스를 만들고, X·ESC로 닫는다.
    /// </summary>
    public class MercenaryRecordWindow : MonoBehaviour
    {
        private const float Width = 760f;
        private const int SortingOrder = 2; // 용병 정보 창(1) 위
        private static MercenaryRecordWindow _instance;

        private GameObject _panel;

        public static void Open(Mercenary merc)
        {
            if (merc == null) return;
            if (_instance == null) Create();
            _instance.Build(merc);
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private static void Create()
        {
            var go = new GameObject("MercenaryRecordWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MercenaryRecordWindow));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _instance = go.GetComponent<MercenaryRecordWindow>();
        }

        private void Build(Mercenary merc)
        {
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _panel.transform.SetParent(transform, false);
            EscapeCloser.Register(_panel, Close, WindowRole.Linked); // 아래 용병 정보 창은 남긴다
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, 0f);
            DraggablePanel.Attach(rect); // 잡고 끌어 옮길 수 있다
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;
            var layout = _panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 18);
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 제목 줄: "이름의 기록" + X
            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            header.transform.SetParent(_panel.transform, false);
            header.GetComponent<LayoutElement>().minHeight = 32f;
            var h = header.GetComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var title = ShopListUI.CreateText(header.transform, $"<b>{merc.Name}의 기록</b>", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            header.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 32, 0, 0); // 오른쪽 위 X 자리
            UITheme.CreateCloseX(_panel.transform, Close); // 공통 닫기 X

            var r = merc.Record;
            if (r.Battles == 0 && r.Injuries == 0 && r.Illnesses == 0)
            {
                AddLine("아직 전투 기록이 없다. 파견을 마치면 전투마다 평점과 기록이 쌓인다.", UITheme.MutedText);
            }
            else
            {
                // 요약
                AddHeading("요약");
                int rate = r.Battles > 0 ? Mathf.RoundToInt(100f * r.Wins / r.Battles) : 0;
                string average = r.Battles > 0 ? BattleRating.RichGrade(r.AverageScore) : "-";
                string best = r.Battles > 0 ? BattleRating.RichGrade(r.BestScore) : "-";
                AddLine($"전투 {r.Battles}회 · 승리 {r.Wins}회 ({rate}%) · 평균 평점 {average} · 최고 {best}", UITheme.BodyText);
                AddLine($"처치 {r.Kills} · 부상 {r.Injuries}번 · 질병 {r.Illnesses}번", UITheme.BodyText);

                // 레벨별 의뢰 성공(이겨서 살아 돌아온 것)
                AddHeading("은월의 숲 의뢰 성공");
                var clears = Enumerable.Range(1, MercenaryRecord.MaxLevel).Select(lv => $"Lv{lv} {r.ClearsAt(lv)}회");
                AddLine(r.TotalClears > 0 ? string.Join("  ·  ", clears) : "아직 성공한 의뢰가 없다.", r.TotalClears > 0 ? UITheme.BodyText : UITheme.MutedText);

                // 최근 전투(최신이 위)
                AddHeading($"최근 전투 (최대 {MercenaryRecord.RecentCount}판)");
                if (r.Recent.Count == 0) AddLine("기록된 전투가 없다.", UITheme.MutedText);
                foreach (var e in r.Recent.Reverse())
                {
                    string result = e.Win ? "<color=#7fd36b>승리</color>" : "<color=#e05a4a>패배</color>";
                    string fallen = e.Fallen ? " <color=#e05a4a>(쓰러짐)</color>" : "";
                    AddLine($"{GameCalendar.Format(e.Day)}  {e.Quest} Lv{e.Level}  {result}{fallen}  평점 {BattleRating.RichGrade(e.Score)}  " +
                            $"<color=#a3937c>피해 {e.Damage} · 처치 {e.Kills} · 치유 {e.Healing}</color>", UITheme.BodyText);
                }
            }

            AddLine("평점은 준 피해·치유·버틴 피해를 동료와 견주고 처치·생존·승패를 더해 매긴다 (S · A+ A A- · B+ B B- · C+ C C- · D)", UITheme.MutedText, 13);

            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private void AddHeading(string text)
        {
            var t = ShopListUI.CreateText(_panel.transform, $"<b>{text}</b>", 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            t.supportRichText = true;
        }

        private void AddLine(string text, Color color, int size = 15)
        {
            var t = ShopListUI.CreateText(_panel.transform, text, size, TextAnchor.MiddleLeft, color);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
