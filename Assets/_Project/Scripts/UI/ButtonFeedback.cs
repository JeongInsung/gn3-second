using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 버튼 크기 반응: 마우스를 올리면 살짝 커지고(1.05), 누르면 눌려 들어간다(0.95). 비활성 버튼은 그대로.
    /// 색 변화는 Button.colors가 맡고 여기선 크기만 바꾼다(위치는 레이아웃 그룹 몫이라 건드리지 않는다).
    /// 시간을 멈춰도 동작하게 unscaled 시간을 쓴다. UIThemeApplier가 버튼마다 붙인다.
    /// </summary>
    public class ButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private const float HoverScale = 1.05f;
        private const float PressedScale = 0.95f;
        private const float SmoothTime = 0.08f;

        private Button _button;
        private bool _hovered;
        private bool _pressed;
        private float _scale = 1f;
        private float _velocity;

        private void Awake() => _button = GetComponent<Button>();

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _pressed = false;
        }

        private void Update()
        {
            bool interactable = _button == null || _button.IsInteractable();
            float target = !interactable ? 1f : _pressed ? PressedScale : _hovered ? HoverScale : 1f;
            if (Mathf.Approximately(_scale, target) && Mathf.Approximately(_velocity, 0f)) return;
            _scale = Mathf.SmoothDamp(_scale, target, ref _velocity, SmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(_scale - target) < 0.001f) { _scale = target; _velocity = 0f; }
            transform.localScale = new Vector3(_scale, _scale, 1f);
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            _scale = 1f;
            _velocity = 0f;
            transform.localScale = Vector3.one;
        }
    }
}
