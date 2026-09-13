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

            marketPanel.SetActive(false);
            partyPanel.SetActive(false);
            questPanel.SetActive(false);
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
            layout.childControlWidth = false;
            layout.childControlHeight = false;

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
