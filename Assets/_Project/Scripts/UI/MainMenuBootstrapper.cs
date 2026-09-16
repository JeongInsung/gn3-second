using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class MainMenuBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var marketPanel = GameObject.Find("MarketPanel");
            var partyPanel = GameObject.Find("PartyPanel");
            var questPanel = GameObject.Find("QuestPanel");
            var canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null || marketPanel == null || partyPanel == null || questPanel == null)
            {
                Debug.LogWarning("[MainMenuBootstrapper] Canvas/MarketPanel/PartyPanel/QuestPanel를 찾지 못해 메뉴 버튼을 생성하지 못했습니다.");
                return;
            }

            PanelActivator.RegisterGroup(marketPanel, partyPanel, questPanel);

            CreateMenuBar(canvas.transform, marketPanel, partyPanel, questPanel);
            CreateCloseButton(marketPanel.transform, marketPanel);
            CreateCloseButton(partyPanel.transform, partyPanel);
            CreateCloseButton(questPanel.transform, questPanel);
            CreateDayControls(canvas.transform);

            marketPanel.SetActive(false);
            partyPanel.SetActive(false);
            questPanel.SetActive(false);
        }

        /// <summary>날짜 표시 + "하루 지나기" 버튼. 누르면 GameClock을 1일 진행시키고,
        /// 그 이벤트를 구독하는 진행 중인 파견들의 남은 일수가 함께 줄어든다(ExpeditionLog).
        /// MarketPanel/PartyPanel/QuestPanel은 전부 화면 우측에 붙어 있어서(우상단 anchor),
        /// 패널 안쪽 버튼(예: PartyUI의 파견 시작 버튼)과 겹치지 않도록 좌상단 메뉴 버튼바 바로 아래에 둔다.</summary>
        private static void CreateDayControls(Transform canvasTransform)
        {
            var barGO = new GameObject("DayControlBar", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(DayAdvanceInput));
            barGO.transform.SetParent(canvasTransform, false);

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -64f);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var dayTextGO = new GameObject("DayText", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            dayTextGO.transform.SetParent(barGO.transform, false);
            var dayTextLayout = dayTextGO.GetComponent<LayoutElement>();
            dayTextLayout.minWidth = 90f;
            dayTextLayout.minHeight = 40f;
            var dayText = dayTextGO.GetComponent<Text>();
            dayText.text = $"{GameClock.CurrentDay}일차";
            dayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            dayText.fontSize = 18;
            dayText.alignment = TextAnchor.MiddleLeft;
            dayText.color = Color.white;

            // 버튼 클릭이든 스페이스바(DayAdvanceInput)든 GameClock.AdvanceDay()만 부르면
            // 이 이벤트 하나로 날짜 텍스트가 갱신된다.
            GameClock.OnDayAdvanced += () => dayText.text = $"{GameClock.CurrentDay}일차";

            var buttonGO = new GameObject("AdvanceDayButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(barGO.transform, false);
            buttonGO.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.5f, 0.9f);
            var buttonLayout = buttonGO.GetComponent<LayoutElement>();
            buttonLayout.minWidth = 120f;
            buttonLayout.minHeight = 40f;
            CreateFillText(buttonGO.transform, "하루 지나기", 16);

            buttonGO.GetComponent<Button>().onClick.AddListener(GameClock.AdvanceDay);
        }

        private static void CreateMenuBar(Transform canvasTransform, GameObject marketPanel, GameObject partyPanel, GameObject questPanel)
        {
            var barGO = new GameObject("MenuButtonBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGO.transform.SetParent(canvasTransform, false);

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateMenuButton(barGO.transform, "용병시장", () => OpenPanel(marketPanel));
            CreateMenuButton(barGO.transform, "파티 구성", () => OpenPanel(partyPanel));
            CreateMenuButton(barGO.transform, "퀘스트", () => OpenPanel(questPanel));
        }

        private static void OpenPanel(GameObject panel) => PanelActivator.Open(panel);

        private static void CreateMenuButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(parent, false);

            buttonGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            var layoutElement = buttonGO.GetComponent<LayoutElement>();
            layoutElement.minWidth = 120f;
            layoutElement.minHeight = 40f;

            CreateFillText(buttonGO.transform, label, 18);
            buttonGO.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private static void CreateCloseButton(Transform panelTransform, GameObject panel)
        {
            var buttonGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(panelTransform, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-8f, -8f);
            rect.sizeDelta = new Vector2(32f, 32f);

            buttonGO.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);

            CreateFillText(buttonGO.transform, "X", 18);
            buttonGO.GetComponent<Button>().onClick.AddListener(() => panel.SetActive(false));
        }

        private static void CreateFillText(Transform parent, string content, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }
    }
}
