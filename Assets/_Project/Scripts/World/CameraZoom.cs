using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GN3.World
{
    /// <summary>
    /// 마우스 휠로 마을 화면을 커서 쪽으로 확대/축소한다(orthographic 카메라).
    /// 마을을 두른 성벽이 있으면 성벽 사각형 밖은 축소·이동 어느 쪽으로도 보이지 않게 가두고,
    /// 가장 멀리는 화면이 성벽 안에 꽉 차는 크기, 가장 가까이는 MinSize. 성벽이 없으면 시작 화면 사각형이 그 범위다.
    /// WASD나 커서를 화면 가장자리에 대면 카메라가 그쪽으로 움직인다.
    /// UI(상점 목록·파티 목록 등) 위에서는 휠을 목록 스크롤에 양보한다. MainMenuBootstrapper가 메인 카메라에 붙인다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraZoom : MonoBehaviour
    {
        private const float MinSize = 2f;
        private const float StepFactor = 0.85f; // 휠 한 칸마다 size ×0.85(확대) / ÷0.85(축소)
        private const float SmoothSpeed = 12f;
        private const float PanSpeed = 1.2f;  // 초당 orthographicSize × 1.2만큼 이동
        private const float PanSmooth = 8f;   // 이동 가속·감속(작을수록 더 미끄럽게 출발·정지)
        private const float EdgePx = 24f;     // 커서가 화면 가장자리에서 이만큼(px) 안쪽이면 그쪽으로 이동(끝에 가까울수록 빠르게)
        private const string WallGroupName = "성벽";

        private Camera _camera;
        private float _maxSize;
        private float _targetSize;
        private Rect _bounds;
        private Vector2 _panVelocity;
        private bool _fitToWalls;
        private float _aspect;

        private void Start()
        {
            _camera = GetComponent<Camera>();
            _maxSize = _camera.orthographicSize;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 center = transform.position;
            _bounds = Rect.MinMaxRect(center.x - halfWidth, center.y - halfHeight, center.x + halfWidth, center.y + halfHeight);
            FitToWalls();
            _targetSize = _camera.orthographicSize;
        }

        /// <summary>
        /// 마을을 두른 성벽(이름이 "성벽"인 그룹)이 있으면 그 사각형을 가둘 범위로 쓴다.
        /// 최대 축소 크기는 화면이 두 축 모두 성벽 안에 들어가는 크기라, 끝까지 축소해도 성벽 밖은 보이지 않는다.
        /// 시작 화면이 그보다 넓으면 그 크기로 줄인다.
        /// </summary>
        private void FitToWalls()
        {
            var walls = GameObject.Find(WallGroupName);
            if (walls == null) return;
            var renderers = walls.GetComponentsInChildren<SpriteRenderer>();
            if (renderers.Length == 0) return;

            var area = renderers[0].bounds;
            foreach (var r in renderers) area.Encapsulate(r.bounds);
            _bounds = Rect.MinMaxRect(area.min.x, area.min.y, area.max.x, area.max.y);
            _fitToWalls = true;
            UpdateMaxSize();
            if (_camera.orthographicSize > _maxSize) _camera.orthographicSize = _maxSize;
            ClampToBounds();
        }

        private void UpdateMaxSize()
        {
            _aspect = _camera.aspect;
            _maxSize = Mathf.Min(_bounds.height * 0.5f, _bounds.width * 0.5f / _aspect);
        }

        private void Update()
        {
            // 창 비율이 바뀌면 성벽 안에 들어가는 최대 크기도 달라진다.
            if (_fitToWalls && !Mathf.Approximately(_aspect, _camera.aspect))
            {
                UpdateMaxSize();
                _targetSize = Mathf.Min(_targetSize, _maxSize);
                if (_camera.orthographicSize > _maxSize) _camera.orthographicSize = _maxSize;
                ClampToBounds();
            }

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

            Vector2 screen = mouse.position.ReadValue();
            bool moved = false;
            if (!Mathf.Approximately(_camera.orthographicSize, _targetSize))
            {
                // 커서가 가리키는 월드 지점이 줌 전후 화면의 같은 자리에 머물도록 카메라를 옮긴다.
                Vector3 before = _camera.ScreenToWorldPoint(screen);
                _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetSize, 1f - Mathf.Exp(-SmoothSpeed * Time.unscaledDeltaTime));
                if (Mathf.Abs(_camera.orthographicSize - _targetSize) < 0.001f) _camera.orthographicSize = _targetSize;
                Vector3 after = _camera.ScreenToWorldPoint(screen);
                transform.position += before - after;
                moved = true;
            }

            // 목표 속도로 서서히 다가가 미끄러지듯 출발·정지한다. 확대할수록 천천히, 배속·일시정지와 무관하게.
            Vector2 targetVelocity = PanInput(screen) * (PanSpeed * _camera.orthographicSize);
            _panVelocity = Vector2.Lerp(_panVelocity, targetVelocity, 1f - Mathf.Exp(-PanSmooth * Time.unscaledDeltaTime));
            if (targetVelocity == Vector2.zero && _panVelocity.sqrMagnitude < 1e-6f) _panVelocity = Vector2.zero;
            if (_panVelocity != Vector2.zero)
            {
                transform.position += (Vector3)(_panVelocity * Time.unscaledDeltaTime);
                moved = true;
            }

            if (!moved) return;
            Vector3 unclamped = transform.position;
            ClampToBounds();
            // 성벽 끝에 막힌 축은 남은 속도를 버린다(반대로 돌아설 때 멈칫하지 않게).
            if (!Mathf.Approximately(unclamped.x, transform.position.x)) _panVelocity.x = 0f;
            if (!Mathf.Approximately(unclamped.y, transform.position.y)) _panVelocity.y = 0f;
        }

        /// <summary>WASD와 화면 가장자리에 댄 커서로 정한 이동 방향(길이 최대 1). 창이 포커스를 잃었거나 커서가 창 밖이면 가장자리 이동은 하지 않는다.</summary>
        private static Vector2 PanInput(Vector2 screen)
        {
            var dir = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) dir.y += 1f;
                if (keyboard.sKey.isPressed) dir.y -= 1f;
                if (keyboard.dKey.isPressed) dir.x += 1f;
                if (keyboard.aKey.isPressed) dir.x -= 1f;
            }

            bool inWindow = screen.x >= 0f && screen.y >= 0f && screen.x <= Screen.width && screen.y <= Screen.height;
            if (Application.isFocused && inWindow)
            {
                // 가장자리 띠 안으로 깊이 들어갈수록 0→1
                if (screen.x < EdgePx) dir.x -= 1f - screen.x / EdgePx;
                else if (screen.x > Screen.width - EdgePx) dir.x += 1f - (Screen.width - screen.x) / EdgePx;
                if (screen.y < EdgePx) dir.y -= 1f - screen.y / EdgePx;
                else if (screen.y > Screen.height - EdgePx) dir.y += 1f - (Screen.height - screen.y) / EdgePx;
            }
            return Vector2.ClampMagnitude(dir, 1f);
        }

        /// <summary>지금 보이는 화면이 시작 화면 사각형 안에 들어가게 카메라 중심을 가둔다.</summary>
        private void ClampToBounds()
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 p = transform.position;
            // 화면이 가둘 사각형보다 넓어진 축은 가운데에 둔다(한쪽 끝에 붙지 않게).
            p.x = halfWidth * 2f >= _bounds.width ? _bounds.center.x : Mathf.Clamp(p.x, _bounds.xMin + halfWidth, _bounds.xMax - halfWidth);
            p.y = halfHeight * 2f >= _bounds.height ? _bounds.center.y : Mathf.Clamp(p.y, _bounds.yMin + halfHeight, _bounds.yMax - halfHeight);
            transform.position = p;
        }
    }
}
