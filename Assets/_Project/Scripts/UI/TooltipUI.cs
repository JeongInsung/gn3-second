using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마우스 근처에 짧은 설명을 띄우는 전역 툴팁. 씬 오브젝트와 무관하게 최초 사용 시 자체 Canvas를 생성한다.
    /// 화면 가장자리에서는 커서 반대쪽으로 뒤집어 화면 안에 둔다.
    /// </summary>
    public class TooltipUI : MonoBehaviour
    {
        private const float CursorOffset = 16f;
        private const float ScreenMargin = 4f;

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
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rect); // 새 글자 크기로 바로 맞춰야 첫 프레임부터 화면 안에 둔다
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

            // 기본은 커서 오른쪽 아래. 화면을 넘는 쪽은 커서 반대편으로 뒤집고, 그래도 넘치면 화면 안으로 민다.
            // Canvas 로컬 좌표는 가운데가 원점, 툴팁 pivot은 왼쪽 위.
            Vector2 size = _rect.rect.size;
            Vector2 half = _canvasRect.rect.size * 0.5f;
            float x = localPoint.x + CursorOffset;
            float y = localPoint.y - CursorOffset;
            if (x + size.x > half.x - ScreenMargin) x = localPoint.x - CursorOffset - size.x;
            if (y - size.y < -half.y + ScreenMargin) y = localPoint.y + CursorOffset + size.y;
            x = Mathf.Clamp(x, -half.x + ScreenMargin, Mathf.Max(-half.x + ScreenMargin, half.x - ScreenMargin - size.x));
            y = Mathf.Clamp(y, Mathf.Min(half.y - ScreenMargin, -half.y + ScreenMargin + size.y), half.y - ScreenMargin);
            _rect.anchoredPosition = new Vector2(x, y);
        }
    }
}
