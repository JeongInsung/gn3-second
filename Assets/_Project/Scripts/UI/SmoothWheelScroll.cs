using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// ScrollRect 목록을 마우스 휠로 부드럽게 내린다. 휠 한 칸에 StepPixels만큼 목표를 옮기고 SmoothDamp로 따라간다.
    /// ScrollRect 기본 휠 처리(scrollSensitivity)는 끈다. 드래그·스크롤바로 움직이면 목표를 현재 위치로 맞춰 서로 싸우지 않게 한다.
    /// 휠 한 칸의 값은 Input System 버전·플랫폼마다 120 또는 1이라 방향만 쓴다(CameraZoom과 같다).
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class SmoothWheelScroll : MonoBehaviour, IScrollHandler, IBeginDragHandler
    {
        private const float StepPixels = 60f;
        private const float SmoothTime = 0.12f;

        private ScrollRect _scroll;
        private float _target;       // 목표 verticalNormalizedPosition
        private float _velocity;
        private bool _animating;

        public static void Attach(ScrollRect scroll)
        {
            if (scroll.GetComponent<SmoothWheelScroll>() == null) scroll.gameObject.AddComponent<SmoothWheelScroll>();
        }

        private void Awake()
        {
            _scroll = GetComponent<ScrollRect>();
            _scroll.scrollSensitivity = 0f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        public void OnScroll(PointerEventData eventData)
        {
            float wheel = eventData.scrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f || _scroll.content == null) return;

            float scrollable = ScrollableHeight();
            if (scrollable <= 0f) return;

            if (!_animating) _target = _scroll.verticalNormalizedPosition;
            // 휠을 위로(+) 굴리면 위로, 아래로(−) 굴리면 아래로. 정규화 위치는 1이 맨 위.
            _target = Mathf.Clamp01(_target + Mathf.Sign(wheel) * StepPixels / scrollable);
            _animating = true;
        }

        private void LateUpdate()
        {
            if (!_animating) return;
            float current = _scroll.verticalNormalizedPosition;
            float next = Mathf.SmoothDamp(current, _target, ref _velocity, SmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(next - _target) * ScrollableHeight() < 0.5f)
            {
                next = _target;
                _animating = false;
                _velocity = 0f;
            }
            _scroll.verticalNormalizedPosition = next;
        }

        private void OnDisable()
        {
            _animating = false;
            _velocity = 0f;
        }

        // 드래그가 시작되면 휠 애니메이션을 멈춘다(드래그는 ScrollRect가 처리).
        public void OnBeginDrag(PointerEventData eventData)
        {
            _animating = false;
            _velocity = 0f;
        }

        private float ScrollableHeight()
        {
            var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
            return _scroll.content.rect.height - viewport.rect.height;
        }
    }
}
