using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>마우스 근처에 짧은 설명을 띄우는 전역 툴팁. 씬 오브젝트와 무관하게 최초 사용 시 자체 Canvas를 생성한다.</summary>
    public class TooltipUI : MonoBehaviour
    {
        private static TooltipUI _instance;

        public static TooltipUI Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Create();
                return _instance;
            }
        }

        private RectTransform _canvasRect;
        private RectTransform _rect;
        private Text _text;

        private static TooltipUI Create()
        {
            var canvasGO = new GameObject("TooltipCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGO);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var tooltipGO = new GameObject("Tooltip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tooltipGO.transform.SetParent(canvasGO.transform, false);

            var rect = tooltipGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);

            tooltipGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.9f);

            var layout = tooltipGO.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = tooltipGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(tooltipGO.transform, false);
            var text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var instance = tooltipGO.AddComponent<TooltipUI>();
            instance._canvasRect = canvasGO.GetComponent<RectTransform>();
            instance._rect = rect;
            instance._text = text;
            tooltipGO.SetActive(false);
            return instance;
        }

        public void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _text.text = text;
            gameObject.SetActive(true);
            UpdatePosition();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (gameObject.activeSelf)
                UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (Mouse.current == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var localPoint);
            _rect.anchoredPosition = localPoint + new Vector2(16f, -16f);
        }
    }
}
