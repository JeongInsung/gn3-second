using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 가이드북(F1 · 메뉴 [가이드]): 창마다 붙어 있던 조작·보조 설명을 한곳에 모은 책. 왼쪽 장 목록, 오른쪽 본문.
    /// 창 화면에는 지금 필요한 정보만 남기고, "어떻게 쓰는지·몇 일치 기록인지" 같은 설명은 여기서 본다.
    /// 마을과 전투 씬 어디서나 연다(자기 캔버스를 만든다 — CalendarPanel과 같은 구조). 전투에서 열면 '직접 전투' 장부터.
    /// </summary>
    public class GuidebookPanel : MonoBehaviour
    {
        private const float Width = 900f, Height = 640f, TopHeight = 70f, TabWidth = 170f;
        private const int SortingOrder = 30; // 전투 화면 위에도 뜨게
        private static GuidebookPanel _instance;

        public enum Chapter { Village, Quests, Battle, Mercenaries, Guild, Records }

        private static readonly (Chapter chapter, string title)[] Chapters =
        {
            (Chapter.Village, "마을 조작"),
            (Chapter.Quests, "의뢰와 파견"),
            (Chapter.Battle, "직접 전투"),
            (Chapter.Mercenaries, "용병 관리"),
            (Chapter.Guild, "길드와 마을"),
            (Chapter.Records, "기록 창"),
        };

        private GameObject _panel;
        private Chapter _chapter;

        public static bool IsOpen => _instance != null && _instance._panel != null;

        public static void Toggle(Chapter start = Chapter.Village)
        {
            if (IsOpen) Close();
            else Open(start);
        }

        public static void Open(Chapter start = Chapter.Village)
        {
            if (_instance == null)
            {
                var go = new GameObject("GuidebookPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GuidebookPanel));
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                _instance = go.GetComponent<GuidebookPanel>();
            }
            _instance._chapter = start;
            _instance.Build();
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private void Build()
        {
            Vector2? moved = _panel != null ? DraggablePanel.MovedPosition((RectTransform)_panel.transform) : null;
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            EscapeCloser.Register(_panel, Close, justOpened: true); // 열 때마다 새로 만든다 — 열리면 다른 창을 닫게
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, Height);
            DraggablePanel.Attach(rect, moved);
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;

            var title = ShopListUI.CreateText(_panel.transform, "<b>가이드북</b>", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(20f, -TopHeight + 10f);
            titleRect.offsetMax = new Vector2(-52f, -14f);

            // 왼쪽: 장 목록(고른 장은 흰 테두리 + 금빛 글씨)
            var tabs = new GameObject("Chapters", typeof(RectTransform), typeof(VerticalLayoutGroup));
            tabs.transform.SetParent(_panel.transform, false);
            var tabsRect = (RectTransform)tabs.transform;
            tabsRect.anchorMin = new Vector2(0f, 0f);
            tabsRect.anchorMax = new Vector2(0f, 1f);
            tabsRect.pivot = new Vector2(0f, 1f);
            tabsRect.offsetMin = new Vector2(20f, 20f);
            tabsRect.offsetMax = new Vector2(20f + TabWidth, -TopHeight);
            var tv = tabs.GetComponent<VerticalLayoutGroup>();
            tv.spacing = 6f;
            tv.childControlWidth = tv.childControlHeight = true;
            tv.childForceExpandWidth = true;
            tv.childForceExpandHeight = false;
            foreach (var (chapter, label) in Chapters)
            {
                var c = chapter;
                var button = ShopListUI.CreateButton(tabs.transform, label, () => { _chapter = c; Build(); }, TabWidth);
                UITheme.StyleButton(button);
                if (chapter == _chapter) UITheme.MarkSelected(button);
            }

            // 오른쪽: 본문
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.padding = new RectOffset(0, 12, 0, 12);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Fill(content.transform, _chapter);
            ScrollListWrapper.Wrap((RectTransform)content.transform, new Vector2(20f + TabWidth + 20f, 20f), new Vector2(-20f, -TopHeight));

            UITheme.CreateCloseX(_panel.transform, Close);
            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        private static void Fill(Transform parent, Chapter chapter)
        {
            switch (chapter)
            {
                case Chapter.Village:
                    Section(parent, "창 단축키",
                        "M 용병시장 · T 파티 구성 · Q 퀘스트 게시판 · L 우편함 · B 건설",
                        "G 금고 장부 · H 의뢰 기록 · C 마을 일정 · Y 길드 연대기(업적)",
                        "U 길드 · K 건강 · R 관계 · N 근처 길드 · J 도감 · F1 가이드북",
                        "열린 창은 같은 키나 ESC, 오른쪽 위 X로 닫습니다. 창 머리를 잡고 끌어 옮길 수 있습니다.");
                    Section(parent, "시간",
                        "Space: 진행(정한 시간만큼) · F: 배속 · P: 일시정지",
                        "진행 버튼 메뉴: 하루 · 4일 · 일주일 · 도착까지(가장 먼저 도착할 파견대까지).",
                        "자정마다 자동 저장됩니다. 중요한 알림창이 떠 있는 동안에는 시간이 멈춥니다.");
                    break;
                case Chapter.Quests:
                    Section(parent, "의뢰 고르기",
                        "게시판(Q)에서 의뢰를 수락하면 파티 창에서 보낼 대원을 고릅니다.",
                        "권장 전투력과 파견대 전투력을 비교한 판정(매우 쉬움~매우 어려움)을 참고하세요.",
                        "[도전] 의뢰를 깨면 은월의 숲 다음 레벨이 열립니다. [연합] 의뢰는 근처 길드와 함께하고 보상을 나눕니다.",
                        "정렬(기본·보상·쉬운 순·기한 임박)과 거르기(연합·도전)로 목록을 줄일 수 있습니다.",
                        "편성: [자동 편성]은 갈 수 있는 가장 강한 대원으로, [지난 편성]은 지난번 팀으로 채웁니다.",
                        "판정이 어려움 이상이면 출발을 한 번 더 눌러야 합니다.");
                    Section(parent, "파견과 도착",
                        "목적지에 도착하면 알림창에서 자동 진행·직접 진행·나중에를 고릅니다.",
                        "직접 전투로 이기면 보상·명성·성장을 더 받습니다.",
                        "오가는 길에 습격을 받을 수 있습니다. 길잡이가 있으면 습격을 피하기도 합니다.",
                        "가는 중인 파견대는 파티 창 [불러들이기]로 돌려보낼 수 있습니다(의뢰 실패, 명성 벌점 절반).");
                    Section(parent, "라운드",
                        "Lv3부터 여러 라운드가 이어지고, 체력은 라운드 사이에 그대로 이어집니다.",
                        "한 라운드에 다른 무리가 섞여 나오기도 합니다(예: 고블린 + 늑대기수).");
                    break;
                case Chapter.Battle:
                    Section(parent, "분대장 조작",
                        "땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬(대상 클릭, 치료는 Q로 자신)",
                        "Space: 일시정지(멈춘 채로도 명령할 수 있습니다) · F: 배속 · B: 가방 · D: 전투 통계",
                        "두 분대 전투에서는 Tab으로 조종할 분대장을 바꿉니다. 1~4: 분대 명령.",
                        "A: 분대장 자동(대원처럼 알아서 싸움). 직접 클릭해 명령하면 꺼집니다.",
                        "일시정지 중 [전투 포기]를 두 번 누르면 물러납니다. 의뢰는 실패하고, 뒤진 전리품은 챙깁니다.");
                    Section(parent, "대원에게 명령",
                        "대원을 끌어 땅에 놓으면 그 자리로 걸어가 지키고, 적 위에 놓으면 그 적을 공격합니다.");
                    Section(parent, "배치",
                        "전투 전 밝은 구역 안에 대원을 끌어 놓습니다. 대원 위에 놓으면 둘의 자리를 바꿉니다.",
                        "근접은 앞, 원거리·힐러는 뒤가 안전합니다. 정한 자리는 다음 전투에도 기억됩니다.");
                    Section(parent, "전리품",
                        "라운드 사이 정리 시간과 승리 뒤에 시체를 눌러 뒤집니다. 가방 칸이 모자라면 두고 가야 합니다.");
                    Section(parent, "앤트킹",
                        "붉은 범위가 차오르면 내려찍기·포효가 옵니다. 범위 밖(포효는 앤트킹 뒤쪽)으로 피하세요.",
                        "체력이 절반 아래로 떨어지면 격노합니다: 포효가 잦아지고 범위가 늘며 그루터기 엔트를 불러냅니다.");
                    break;
                case Chapter.Mercenaries:
                    Section(parent, "고용과 계약",
                        "용병시장(M)에서 협상으로 고용합니다. 관심이 없는 용병과는 협상할 수 없습니다.",
                        "계약이 끝나는 날 길드를 떠나니 그 전에 재계약하세요. 주급은 매주 지급됩니다.");
                    Section(parent, "상태",
                        "피로가 쌓이면 지치고(파견 불가), 피로 50 이상·사기 30 미만이면 능력치가 깎입니다.",
                        "부상·질병은 건강 창(K)에서 봅니다. 중한 것은 파견할 수 없으니 병원에 보내세요.",
                        "빨간 '!'는 파견을 막는 상태, 주황은 능력치가 깎이는 상태입니다.");
                    Section(parent, "관계",
                        "대원끼리의 관계와 궁합은 관계 창(R)에서 봅니다.");
                    break;
                case Chapter.Guild:
                    Section(parent, "길드 단계",
                        "명성이 쌓이면 길드 단계가 오릅니다(U). 정원·창고·의뢰 등급·용병 재능이 늘어납니다.",
                        "길드 창의 동그라미 줄에 마우스를 올리면 전체 단계 표가 나옵니다.",
                        "의뢰 성공은 명성을 올리고, 실패는 깎습니다.");
                    Section(parent, "마을",
                        "건설(B)로 건물과 장식품을 짓습니다. 장식품은 마을 매력을 올립니다.",
                        "떠돌이 상인·암거래 상인·의뢰 중개인이 정해진 날에 찾아옵니다(일정 C).");
                    break;
                case Chapter.Records:
                    Section(parent, "기록이 남는 기간",
                        "금고 장부(G): 최근 14일 동안 골드가 들고 난 기록. 날짜를 넘겨 봅니다.",
                        "의뢰 기록(H): 끝낸 의뢰 최근 100건.",
                        "마을 일정(C): 오늘부터 14일 안의 일정.",
                        "길드 연대기(Y)의 통계 탭은 기간 제한 없이 처음부터 셉니다.");
                    Section(parent, "업적",
                        "업적을 이루면 편지와 함께 보상(금화)을 받습니다. 은퇴하거나 쓰러진 용병은 명예의 전당에 남습니다.");
                    break;
            }
        }

        /// <summary>한 묶음: 금빛 머리 + 줄마다 한 문장.</summary>
        private static void Section(Transform parent, string heading, params string[] lines)
        {
            var head = ShopListUI.CreateText(parent, $"<b>{heading}</b>", 18, TextAnchor.MiddleLeft, UITheme.TitleText);
            head.supportRichText = true;
            head.gameObject.AddComponent<LayoutElement>().minHeight = 32f;
            foreach (var line in lines)
            {
                var text = ShopListUI.CreateText(parent, "· " + line, 16, TextAnchor.UpperLeft, UITheme.BodyText);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;
    }
}
