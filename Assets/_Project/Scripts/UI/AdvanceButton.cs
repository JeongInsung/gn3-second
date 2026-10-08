using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 화면 오른쪽 위의 큰 "진행" 버튼. 왼쪽 클릭 = 3시간 진행(TimeAdvanceController.RequestAdvance),
    /// 우클릭 = 바로 아래에 "하루 / 4일 / 일주일" 메뉴. 메뉴는 고르거나, 바깥을 누르거나, ESC·다시 우클릭하면 닫힌다.
    /// 메인 Canvas 맨 뒤(첫 자식)에 두어 파티·시장 창이 열리면 그 아래로 깔린다.
    /// </summary>
    public class AdvanceButton : MonoBehaviour, IPointerClickHandler
    {
        public static readonly Vector2 ButtonSize = new Vector2(180f, 64f);
        private const float MenuItemHeight = 48f;
        public const float Gap = 6f;
        public const float Margin = 16f; // 화면 가장자리에서 띄우는 거리(옆 저장 버튼도 같이 쓴다)

        private RectTransform _rect;
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
            var label = CreateText(go.transform, "진행", 26);
            label.fontStyle = FontStyle.Bold;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(controller.RequestAdvance);

            var self = go.GetComponent<AdvanceButton>();
            self._rect = rect;
            self.BuildMenu(controller);
            return button;
        }

        private void BuildMenu(TimeAdvanceController controller)
        {
            _menu = new GameObject("SkipMenu", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _menu.transform.SetParent(transform, false);
            _menuRect = _menu.GetComponent<RectTransform>();
            _menuRect.anchorMin = _menuRect.anchorMax = new Vector2(1f, 0f);
            _menuRect.pivot = Vector2.one; // 버튼 바로 아래, 오른쪽 맞춤
            _menuRect.anchoredPosition = new Vector2(0f, -Gap);
            _menuRect.sizeDelta = new Vector2(ButtonSize.x, MenuItemHeight * 3 + Gap * 2);
            var layout = _menu.GetComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            controller.SetSkipButtons(
                CreateMenuItem("하루", () => controller.RequestSkipDays(1)),
                CreateMenuItem("4일", () => controller.RequestSkipDays(4)),
                CreateMenuItem("일주일", () => controller.RequestSkipDays(7)));
            EscapeCloser.Register(_menu, Hide);
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
