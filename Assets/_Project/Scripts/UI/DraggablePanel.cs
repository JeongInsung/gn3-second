using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.UI
{
    /// <summary>
    /// 창(패널)의 빈 곳·제목을 잡고 끌어 옮긴다. 누르면 같은 Canvas 안에서 맨 앞으로 오고, 화면 밖으로는 나가지 않는다.
    /// 목록(ScrollRect) 위에서 끌면 목록이 먼저 드래그를 받으므로 스크롤은 그대로다.
    /// 옮긴 창은 닫히면(OnDisable) 원래 자리로 돌아가, 다음에 열 때 기본 위치에 뜬다.
    /// </summary>
    public class DraggablePanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        private const float MinVisible = 80f; // 창이 화면보다 클 때 최소한 보이게 둘 폭

        private RectTransform _rect;
        private Canvas _canvas;
        private Vector2 _home;  // 처음 끌기 직전 위치 = 코드·씬이 정해 둔 기본 위치
        private bool _moved;

        public static void Attach(RectTransform panel)
        {
            if (panel != null && panel.GetComponent<DraggablePanel>() == null)
                panel.gameObject.AddComponent<DraggablePanel>();
        }

        private void Awake() => _rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData eventData) => _rect.SetAsLastSibling();

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (!_moved)
            {
                _home = _rect.anchoredPosition;
                _moved = true;
            }
        }

        // 닫는 방법(닫기 버튼·ESC·부모 끄기)과 상관없이 꺼질 때 원래 자리로
        private void OnDisable()
        {
            if (!_moved) return;
            _rect.anchoredPosition = _home;
            _moved = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rect.anchoredPosition += eventData.delta / scale;
            ClampToCanvas();
        }

        /// <summary>창 네 모서리를 Canvas 좌표로 바꿔, 밖으로 나간 만큼 되돌린다.</summary>
        private void ClampToCanvas()
        {
            if (_canvas == null) return;
            var canvasRect = (RectTransform)_canvas.transform;
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            Vector2 min = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRect.InverseTransformPoint(corners[2]);
            Rect bounds = canvasRect.rect;

            Vector2 shift = Vector2.zero;
            shift.x = Push(min.x, max.x, bounds.xMin, bounds.xMax);
            shift.y = Push(min.y, max.y, bounds.yMin, bounds.yMax);
            _rect.anchoredPosition += shift;
        }

        private static float Push(float min, float max, float lo, float hi)
        {
            if (max - min <= hi - lo)
            {
                if (min < lo) return lo - min;
                if (max > hi) return hi - max;
                return 0f;
            }
            // 창이 화면보다 크면 MinVisible만큼은 화면 안에 남긴다.
            if (max < lo + MinVisible) return lo + MinVisible - max;
            if (min > hi - MinVisible) return hi - MinVisible - min;
            return 0f;
        }
    }
}
