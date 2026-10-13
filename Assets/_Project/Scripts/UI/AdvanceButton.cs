using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 화면 오른쪽 위의 큰 "진행 N시간" 버튼과 그 아래 시간 슬라이더(1~24시간, 끌어서 정함).
    /// 왼쪽 클릭 = 슬라이더로 정한 시간만큼 진행(TimeAdvanceController.RequestAdvance),
    /// 우클릭 = 슬라이더 아래에 "하루 / 4일 / 일주일" 메뉴. 메뉴는 고르거나, 바깥을 누르거나, ESC·다시 우클릭하면 닫힌다.
    /// 메인 Canvas 맨 뒤(첫 자식)에 두어 파티·시장 창이 열리면 그 아래로 깔린다.
    /// </summary>
    public class AdvanceButton : MonoBehaviour, IPointerClickHandler
    {
        public static readonly Vector2 ButtonSize = new Vector2(180f, 64f);
        private const float MenuItemHeight = 48f;
        private const float SliderHeight = 22f;
        private const float SliderLabelWidth = 30f; // 슬라이더 양옆 "1h" "24h"
        public const float Gap = 6f;
        public const float Margin = 16f; // 화면 가장자리에서 띄우는 거리(옆 저장 버튼도 같이 쓴다)

        private RectTransform _rect;
        private RectTransform _sliderRect;
        private GameObject _menu;
        private RectTransform _menuRect;
        private Canvas _canvas;

        public static Button Create(Transform canvas, TimeAdvanceController controller)
        {
            var go = new GameObject("AdvanceDayButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger), typeof(AdvanceButton));
            go.transform.SetParent(canvas, false);
            go.transform.SetAsFirstSibling();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; // 화면 오른쪽 위
            rect.anchoredPosition = new Vector2(-Margin, -Margin);
            rect.sizeDelta = ButtonSize;
            go.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.5f, 0.9f);
            go.GetComponent<TooltipTrigger>().Text = "우클릭: 며칠 진행";
            var label = CreateText(go.transform, Label(controller.AdvanceHours), 22);
            label.fontStyle = FontStyle.Bold;
            controller.AdvanceHoursChanged += hours => { if (label != null) label.text = Label(hours); };

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(controller.RequestAdvance);

            var self = go.GetComponent<AdvanceButton>();
            self._rect = rect;
            self.BuildSlider(controller);
            self.BuildMenu(controller);
            return button;
        }

        private static string Label(int hours) => $"진행 {hours}시간";

        /// <summary>버튼 바로 아래 "1h ━━●━━ 24h" 슬라이더. 끌면 진행 버튼이 넘길 시간이 바뀐다.</summary>
        private void BuildSlider(TimeAdvanceController controller)
        {
            var root = new GameObject("AdvanceHoursSlider", typeof(RectTransform), typeof(TooltipTrigger));
            root.transform.SetParent(transform, false);
            _sliderRect = root.GetComponent<RectTransform>();
            _sliderRect.anchorMin = _sliderRect.anchorMax = new Vector2(1f, 0f);
            _sliderRect.pivot = Vector2.one; // 버튼 바로 아래, 오른쪽 맞춤
            _sliderRect.anchoredPosition = new Vector2(0f, -Gap);
            _sliderRect.sizeDelta = new Vector2(ButtonSize.x, SliderHeight);
            root.GetComponent<TooltipTrigger>().Text = $"끌어서 진행할 시간 설정 ({TimeAdvanceController.MinAdvanceHours}~{TimeAdvanceController.MaxAdvanceHours}시간)";

            var minLabel = CreateText(root.transform, $"{TimeAdvanceController.MinAdvanceHours}h", 13);
            Stretch(minLabel.rectTransform, 0f, 0f, SliderLabelWidth);
            var maxLabel = CreateText(root.transform, $"{TimeAdvanceController.MaxAdvanceHours}h", 13);
            Stretch(maxLabel.rectTransform, 1f, 1f, SliderLabelWidth);

            // Slider 본체: 배경(가는 홈) + 채움 + 손잡이
            var sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGO.transform.SetParent(root.transform, false);
            var sliderRect = sliderGO.GetComponent<RectTransform>();
            sliderRect.anchorMin = Vector2.zero;
            sliderRect.anchorMax = Vector2.one;
            sliderRect.offsetMin = new Vector2(SliderLabelWidth, 0f);
            sliderRect.offsetMax = new Vector2(-SliderLabelWidth, 0f);

            var track = CreateImage(sliderGO.transform, "Background", new Color(0.12f, 0.12f, 0.15f, 0.9f));
            track.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.3f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.7f);
            fillAreaRect.offsetMin = new Vector2(4f, 0f);
            fillAreaRect.offsetMax = new Vector2(-4f, 0f);
            var fill = CreateImage(fillArea.transform, "Fill", new Color(0.95f, 0.65f, 0.3f, 1f));
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGO.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(6f, 0f);
            handleAreaRect.offsetMax = new Vector2(-6f, 0f);
            var handle = CreateImage(handleArea.transform, "Handle", new Color(0.95f, 0.9f, 0.8f, 1f));
            handle.rectTransform.sizeDelta = new Vector2(12f, 0f);

            var slider = sliderGO.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = TimeAdvanceController.MinAdvanceHours;
            slider.maxValue = TimeAdvanceController.MaxAdvanceHours;
            slider.wholeNumbers = true;
            slider.value = controller.AdvanceHours;
            slider.onValueChanged.AddListener(v => controller.SetAdvanceHours(Mathf.RoundToInt(v)));
            controller.SetHoursSlider(slider);
        }

        private void BuildMenu(TimeAdvanceController controller)
        {
            _menu = new GameObject("SkipMenu", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _menu.transform.SetParent(transform, false);
            _menuRect = _menu.GetComponent<RectTransform>();
            _menuRect.anchorMin = _menuRect.anchorMax = new Vector2(1f, 0f);
            _menuRect.pivot = Vector2.one; // 슬라이더 아래, 오른쪽 맞춤
            _menuRect.anchoredPosition = new Vector2(0f, -(Gap + SliderHeight + Gap));
            _menuRect.sizeDelta = new Vector2(ButtonSize.x, MenuItemHeight * 3 + Gap * 2);
            var layout = _menu.GetComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            controller.SetSkipButtons(
                CreateMenuItem("하루", () => controller.RequestSkipDays(1)),
                CreateMenuItem("4일", () => controller.RequestSkipDays(4)),
                CreateMenuItem("일주일", () => controller.RequestSkipDays(7)));
            EscapeCloser.Register(_menu, Hide, WindowRole.Popup);
            _menu.SetActive(false);
        }

        private Button CreateMenuItem(string label, System.Action onClick)
        {
            var go = new GameObject(label + "SkipButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_menu.transform, false);
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.95f);
            CreateText(go.transform, label, 18);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                Hide();
                onClick();
            });
            return button;
        }

        private void Hide() => _menu.SetActive(false);

        public void OnPointerClick(PointerEventData eventData)
        {
            // Button은 왼쪽 클릭만 받으므로 진행과 겹치지 않는다.
            if (eventData.button != PointerEventData.InputButton.Right) return;
            _menu.SetActive(!_menu.activeSelf);
            if (_menu.activeSelf) UIThemeApplier.ApplyNow(_menu.transform); // 처음 열 때 기본색이 반짝이지 않게
        }

        private void Update()
        {
            if (!_menu.activeSelf) return;
            var mouse = Mouse.current;
            if (mouse == null || !(mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return;

            // 메뉴·버튼 밖을 누르면 닫는다(버튼 위 우클릭은 OnPointerClick이 켜고 끈다).
            Vector2 p = mouse.position.ReadValue();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_menuRect, p, cam)
                && !RectTransformUtility.RectangleContainsScreenPoint(_rect, p, cam))
                Hide();
        }

        /// <summary>슬라이더 옆 글자 칸: anchorX(0 = 왼쪽 끝, 1 = 오른쪽 끝)에 폭 width로 세로 꽉 채운다.</summary>
        private static void Stretch(RectTransform rect, float anchorX, float pivotX, float width)
        {
            rect.anchorMin = new Vector2(anchorX, 0f);
            rect.anchorMax = new Vector2(anchorX, 1f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, 0f);
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string content, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
