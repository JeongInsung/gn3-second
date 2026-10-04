using GN3.CharacterAnim;
using GN3.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// DesignScene 전용 부트스트랩. 씬 파일을 편집하지 않고 코드로만 UI/캐릭터를 주입하는 프로젝트 컨벤션을 따른다.
    /// (MainMenuBootstrapper와 같은 방식이지만 활성 씬이 DesignScene일 때만 동작해 MainScene에는 영향이 없다.)
    /// "랜덤 캐릭터" 버튼을 누르면 파츠 시트의 1프레임을 조합해 캐릭터를 새로 만든다.
    /// </summary>
    public static class DesignSceneBootstrapper
    {
        private const string TargetSceneName = "DesignScene";

        private static readonly System.Random Rng = new System.Random();
        private static CharacterPartView _view;
        private static Text _infoText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != TargetSceneName) return;

            var designerGO = new GameObject("CharacterDesigner", typeof(CharacterPartView));
            // 파츠 pivot이 바닥 중앙(PPU 64 → 프레임 높이 4유닛)이라 화면 중앙보다 조금 아래에 발을 둔다.
            designerGO.transform.position = new Vector3(0f, -2f, 0f);
            _view = designerGO.GetComponent<CharacterPartView>();

            var canvas = CreateCanvas();
            EnsureEventSystem();
            CreateMenuButton(canvas.transform, "랜덤 캐릭터", GenerateRandomCharacter);
            _infoText = CreateInfoText(canvas.transform);
            CreateTimeControls(canvas.transform);
            ConnectBuildings(CreateBuildingPanel(canvas.transform));

            GenerateRandomCharacter();
        }

        /// <summary>씬의 클릭 가능한 건물(SelectableBuilding)을 누르면 건물 패널이 열리게 연결한다.</summary>
        private static void ConnectBuildings(BuildingPanel panel)
        {
            foreach (var building in Object.FindObjectsByType<SelectableBuilding>(FindObjectsSortMode.None))
                building.onClicked.AddListener(panel.Open);
        }

        /// <summary>
        /// 가운데 건물 패널(처음엔 숨김): 화면 전체 어두운 배경(누르면 닫힘) + 520x420 판에 제목·설명·상품 목록 칸·닫기 버튼.
        /// 상품 데이터가 아직 없어 목록 칸에는 "상품 준비 중"만 보인다.
        /// </summary>
        private static BuildingPanel CreateBuildingPanel(Transform parent)
        {
            var root = new GameObject("BuildingPanel", typeof(RectTransform), typeof(Image), typeof(Button), typeof(BuildingPanel));
            root.transform.SetParent(parent, false);
            Stretch(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var panel = root.GetComponent<BuildingPanel>();
            root.GetComponent<Button>().onClick.AddListener(panel.Close);

            // 판 안 클릭이 부모(배경 닫기 버튼)로 올라가 패널이 닫히지 않게, 판에도 아무 일 안 하는 버튼을 둬 클릭을 받는다.
            var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(Button));
            box.transform.SetParent(root.transform, false);
            box.GetComponent<Button>().transition = Selectable.Transition.None;
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.sizeDelta = new Vector2(520f, 420f);
            boxRect.anchoredPosition = new Vector2(130f, 0f); // 왼쪽에 서는 상인까지 합쳐 화면 가운데에 오게

            // 판 왼쪽에 서 있는 상인(512px 그림 1배). 그림 아래 투명 여백(약 47px)만큼 내려 발을 판 아래 선에 맞추고,
            // 손이 판 가장자리에 살짝 걸치게 판 앞에 둔다. 클릭은 막지 않는다(raycastTarget 끔).
            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(root.transform, false);
            var portraitRect = portrait.GetComponent<RectTransform>();
            portraitRect.pivot = new Vector2(0.5f, 0f);
            portraitRect.sizeDelta = new Vector2(512f, 512f);
            portraitRect.anchoredPosition = new Vector2(130f - 260f - 110f, -210f - 47f);
            var portraitImage = portrait.GetComponent<Image>();
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;
            panel.Portrait = portraitImage;
            box.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.15f, 0.96f);

            panel.Title = CreateText(box.transform, "", 28, TextAnchor.MiddleLeft);
            Place(panel.Title.rectTransform, new Vector2(24f, -20f), new Vector2(400f, 40f));

            panel.Description = CreateText(box.transform, "", 18, TextAnchor.UpperLeft);
            panel.Description.color = new Color(0.85f, 0.85f, 0.85f);
            Place(panel.Description.rectTransform, new Vector2(24f, -66f), new Vector2(472f, 48f));

            // 상품 목록 칸(빈 스크롤 영역). 나중에 ItemListContent 아래에 상품 줄을 추가하면 된다.
            var list = new GameObject("ItemList", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            list.transform.SetParent(box.transform, false);
            Place(list.GetComponent<RectTransform>(), new Vector2(24f, -124f), new Vector2(472f, 272f));
            list.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(list.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = list.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            panel.ItemListContent = contentRect;

            var empty = CreateText(list.transform, "상품 준비 중", 18, TextAnchor.MiddleCenter);
            empty.color = new Color(1f, 1f, 1f, 0.4f);
            Stretch(empty.rectTransform);

            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(box.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-14f, -14f);
            closeRect.sizeDelta = new Vector2(40f, 40f);
            close.GetComponent<Image>().color = new Color(0.3f, 0.28f, 0.33f, 1f);
            close.GetComponent<Button>().onClick.AddListener(panel.Close);
            Stretch(CreateText(close.transform, "X", 22, TextAnchor.MiddleCenter).rectTransform);

            root.SetActive(false);
            return panel;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>부모 왼쪽 위 기준으로 놓는다.</summary>
        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        /// <summary>오른쪽 위에 현재 시각 텍스트 + 0~24시 슬라이더. 씬에 DayNightCycle이 있을 때만 만든다.</summary>
        private static void CreateTimeControls(Transform parent)
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            var timeText = CreateText(parent, DayNightCycle.FormatTime(cycle.TimeOfDay), 28, TextAnchor.MiddleRight);
            var textRect = timeText.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(1f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(1f, 1f);
            textRect.anchoredPosition = new Vector2(-16f, -16f);
            textRect.sizeDelta = new Vector2(480f, 40f);

            var sliderGO = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGO.name = "TimeSlider";
            sliderGO.transform.SetParent(parent, false);
            var sliderRect = sliderGO.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(1f, 1f);
            sliderRect.anchorMax = new Vector2(1f, 1f);
            sliderRect.pivot = new Vector2(1f, 1f);
            sliderRect.anchoredPosition = new Vector2(-16f, -64f);
            sliderRect.sizeDelta = new Vector2(480f, 40f);

            var slider = sliderGO.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 24f;
            slider.value = cycle.TimeOfDay;
            slider.onValueChanged.AddListener(hours => cycle.TimeOfDay = hours);

            // Scene 뷰 패널·인스펙터·autoAdvance로 바뀐 시간도 화면 UI에 따라오게 매 프레임 맞춘다.
            CoroutineHost.Instance.StartCoroutine(SyncTimeControls(cycle, slider, timeText));
        }

        private static System.Collections.IEnumerator SyncTimeControls(DayNightCycle cycle, Slider slider, Text timeText)
        {
            while (cycle != null && slider != null)
            {
                if (!Mathf.Approximately(slider.value, cycle.TimeOfDay))
                    slider.SetValueWithoutNotify(cycle.TimeOfDay);
                timeText.text = DayNightCycle.FormatTime(cycle.TimeOfDay);
                yield return null;
            }
        }

        private static void GenerateRandomCharacter()
        {
            var composed = RandomCharacterComposer.Compose(Rng);
            _view.Show(composed);

            if (composed.Parts.Count == 0)
            {
                _infoText.text = "파츠를 찾지 못했습니다. Resources/CharacterAnim 폴더와 characters.txt를 확인하세요.";
                return;
            }

            _infoText.text =
                $"몸(torso/팔/다리): {composed.BodyCharacter}\n" +
                $"머리(얼굴+머리카락): {composed.SourceOf(CharacterPartId.Head)}";
        }

        private static Canvas CreateCanvas()
        {
            var canvasGO = new GameObject("DesignCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // MainScene의 Canvas 설정과 동일 (1920x1080 기준 스케일)
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            // 프로젝트가 New Input System 전용이라 StandaloneInputModule 대신 InputSystemUIInputModule을 쓴다.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static void CreateMenuButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(160f, 44f);

            buttonGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            var text = CreateText(buttonGO.transform, label, 18, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            buttonGO.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private static Text CreateInfoText(Transform parent)
        {
            var text = CreateText(parent, "", 16, TextAnchor.UpperLeft);
            var rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -72f);
            rect.sizeDelta = new Vector2(480f, 120f);
            return text;
        }

        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }
    }
}
