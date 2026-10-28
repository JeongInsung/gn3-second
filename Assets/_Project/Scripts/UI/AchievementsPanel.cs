using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 길드 연대기 창(Y · 메뉴 [업적]): [업적] 이룬 것·남은 것(진행도·보상) · [명예의 전당] 은퇴·전사한 용병과 경력 ·
    /// [통계] 전체 기간 의뢰·성공률·보상·수입/지출. 데이터는 Achievements. GoldLedgerWindow와 같은 구조(자기 캔버스, X·ESC, 끌어 옮기기).
    /// </summary>
    public class AchievementsPanel : MonoBehaviour
    {
        private const float Width = 760f, Height = 680f, TopHeight = 110f;
        private const int SortingOrder = 2;
        private static AchievementsPanel _instance;

        private enum Tab { Achievements, Memorial, Stats }

        private GameObject _panel;
        private Tab _tab;

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
                var go = new GameObject("AchievementsPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(AchievementsPanel));
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                _instance = go.GetComponent<AchievementsPanel>();
            }
            Achievements.Check(); // 열 때 한 번 더 판정(조건을 막 채운 경우)
            _instance.Build();
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private void SetTab(Tab tab)
        {
            _tab = tab;
            Build();
        }

        private void Build()
        {
            Vector2? moved = _panel != null ? DraggablePanel.MovedPosition((RectTransform)_panel.transform) : null; // 탭을 바꿔도 옮긴 자리 그대로
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            EscapeCloser.Register(_panel, Close);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, Height);
            DraggablePanel.Attach(rect, moved);
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;

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

            var title = ShopListUI.CreateText(top.transform,
                $"<b>길드 연대기</b>  <size=14><color=#a3937c>업적 {Achievements.DoneCount}/{Achievements.All.Length} · 명예의 전당 {Achievements.Memorials.Count}명</color></size>",
                22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;

            var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tabs.transform.SetParent(top.transform, false);
            tabs.GetComponent<LayoutElement>().minHeight = 36f;
            var h = tabs.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            TabButton(tabs.transform, "업적", Tab.Achievements);
            TabButton(tabs.transform, "명예의 전당", Tab.Memorial);
            TabButton(tabs.transform, "통계", Tab.Stats);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.padding = new RectOffset(0, 8, 0, 8);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (_tab == Tab.Achievements) FillAchievements(content.transform);
            else if (_tab == Tab.Memorial) FillMemorial(content.transform);
            else FillStats(content.transform);
            ScrollListWrapper.Wrap((RectTransform)content.transform, new Vector2(20f, 18f), new Vector2(-20f, -TopHeight - 6f));

            UITheme.CreateCloseX(_panel.transform, Close);
            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private void TabButton(Transform parent, string label, Tab tab)
        {
            var button = ShopListUI.CreateButton(parent, label, () => SetTab(tab), 120f);
            button.interactable = _tab != tab; // 고른 탭은 눌린 채
        }

        /// <summary>한 칸: 바탕 + 두 줄(굵은 머리 / 흐린 설명).</summary>
        private static void Card(Transform parent, string head, string sub, Color tint)
        {
            var row = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);
            row.GetComponent<Image>().color = tint;
            var v = row.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 8, 8);
            v.spacing = 3f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var headText = ShopListUI.CreateText(row.transform, head, 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            headText.supportRichText = true;
            if (string.IsNullOrEmpty(sub)) return;
            var subText = ShopListUI.CreateText(row.transform, sub, 14, TextAnchor.MiddleLeft, UITheme.MutedText);
            subText.supportRichText = true;
        }

        private static void FillAchievements(Transform content)
        {
            // 이룬 것 먼저(금빛), 그다음 남은 것(진행도)
            foreach (var def in Achievements.All.OrderByDescending(Achievements.IsDone))
            {
                bool done = Achievements.IsDone(def);
                string progress = !done && def.Progress != null ? $"  <color=#a3937c>{def.Progress()}</color>" : "";
                Card(content,
                    (done ? "<color=#ffd36b>★</color> " : "<color=#5a544a>★</color> ") + $"<b>{def.Title}</b>{progress}", // 못 이룬 것은 어두운 별
                    $"{def.Description} · 보상 {def.Reward}G" + (done ? "  <color=#7fd36b>달성</color>" : ""),
                    done ? new Color(0.45f, 0.36f, 0.12f, 0.35f) : new Color(1f, 1f, 1f, 0.05f));
            }
        }

        private static void FillMemorial(Transform content)
        {
            if (Achievements.Memorials.Count == 0)
            {
                ShopListUI.CreateText(content, "아직 길드를 떠난 용병이 없다. 은퇴하거나 쓰러진 용병이 여기에 남는다.", 15, TextAnchor.MiddleLeft, UITheme.MutedText);
                return;
            }
            foreach (var m in Achievements.Memorials.Reverse())
            {
                string record = m.Battles > 0
                    ? $"전투 {m.Battles}회 · 승리 {m.Wins} · 처치 {m.Kills} · 최고 평점 {m.BestScore} · 의뢰 성공 {m.Clears}"
                    : "전투 기록 없음";
                Card(content,
                    $"<b>{m.Name}</b>  <color=#a3937c>{m.ClassName} · {m.Age}세</color>   <color=#e0b06b>{m.Reason}</color>",
                    $"{GameCalendar.Format(m.Day)} · {record}",
                    new Color(1f, 1f, 1f, 0.05f));
            }
        }

        private static void FillStats(Transform content)
        {
            int quests = Achievements.LifeQuests, wins = Achievements.LifeWins;
            int rate = quests > 0 ? Mathf.RoundToInt(100f * wins / quests) : 0;
            Card(content, $"<b>끝낸 의뢰</b>  {quests}건", $"성공 {wins} · 실패 {quests - wins} ({rate}%) · 의뢰 보상 합계 {Achievements.LifeReward:N0}G", new Color(1f, 1f, 1f, 0.05f));
            long net = Achievements.LifeGoldIn - Achievements.LifeGoldOut;
            Card(content, $"<b>금고</b>  누적 수입 <color=#7fd36b>{Achievements.LifeGoldIn:N0}G</color> · 누적 지출 <color=#e0675a>{Achievements.LifeGoldOut:N0}G</color>",
                $"순이익 {(net >= 0 ? "+" : "")}{net:N0}G · 지금 금고 {Wallet.Gold:N0}G", new Color(1f, 1f, 1f, 0.05f));
            Card(content, $"<b>길드</b>  {Guild.CurrentName} · 명성 {Guild.Reputation}",
                $"은월의 숲 Lv{ForestProgress.UnlockedLevel}" + (ForestProgress.BossDefeated ? " · 앤트킹 토벌" : "") +
                $" · 도감 {Bestiary.DiscoveredCount}/{Bestiary.All.Length} · 떠난 용병 {Achievements.Memorials.Count}명", new Color(1f, 1f, 1f, 0.05f));
            ShopListUI.CreateText(content, "의뢰 기록 창(H)은 최근 100건, 장부(G)는 최근 14일만 보여 주지만 여기 통계는 처음부터 센다.", 13, TextAnchor.MiddleLeft, UITheme.MutedText)
                .horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;
    }
}
