using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GN3.World
{
    /// <summary>
    /// 마우스 휠로 마을 화면을 커서 쪽으로 확대/축소한다(orthographic 카메라).
    /// 가장 멀리는 시작 화면(마을 전체), 가장 가까이는 MinSize. 시작 때 보이던 화면 사각형 밖은 보이지 않게
    /// 카메라 중심을 가두므로, 끝까지 축소하면 원래 화면 위치로 돌아온다.
    /// UI(상점 목록·파티 목록 등) 위에서는 휠을 목록 스크롤에 양보한다. MainMenuBootstrapper가 메인 카메라에 붙인다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraZoom : MonoBehaviour
    {
        private const float MinSize = 2f;
        private const float StepFactor = 0.85f; // 휠 한 칸마다 size ×0.85(확대) / ÷0.85(축소)
        private const float SmoothSpeed = 12f;

        private Camera _camera;
        private float _maxSize;
        private float _targetSize;
        private Rect _bounds;

        private void Start()
        {
            _camera = GetComponent<Camera>();
            _maxSize = _camera.orthographicSize;
            _targetSize = _maxSize;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 center = transform.position;
            _bounds = Rect.MinMaxRect(center.x - halfWidth, center.y - halfHeight, center.x + halfWidth, center.y + halfHeight);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            float scroll = mouse.scroll.ReadValue().y;
            if (!overUI && Mathf.Abs(scroll) > 0.01f)
            {
                // 휠 한 칸의 scroll 값은 Input System 버전·플랫폼마다 달라(120 또는 1) 방향만 쓴다.
                float step = scroll > 0f ? StepFactor : 1f / StepFactor;
                _targetSize = Mathf.Clamp(_targetSize * step, MinSize, _maxSize);
            }

            if (Mathf.Approximately(_camera.orthographicSize, _targetSize)) return;

            // 커서가 가리키는 월드 지점이 줌 전후 화면의 같은 자리에 머물도록 카메라를 옮긴다.
            Vector2 screen = mouse.position.ReadValue();
            Vector3 before = _camera.ScreenToWorldPoint(screen);
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetSize, 1f - Mathf.Exp(-SmoothSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(_camera.orthographicSize - _targetSize) < 0.001f) _camera.orthographicSize = _targetSize;
            Vector3 after = _camera.ScreenToWorldPoint(screen);
            transform.position += before - after;

            ClampToBounds();
        }

        /// <summary>지금 보이는 화면이 시작 화면 사각형 안에 들어가게 카메라 중심을 가둔다.</summary>
        private void ClampToBounds()
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, _bounds.xMin + halfWidth, Mathf.Max(_bounds.xMin + halfWidth, _bounds.xMax - halfWidth));
            p.y = Mathf.Clamp(p.y, _bounds.yMin + halfHeight, Mathf.Max(_bounds.yMin + halfHeight, _bounds.yMax - halfHeight));
            transform.position = p;
        }
    }
}
