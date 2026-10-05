using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 목록 줄 전체를 클릭 가능하게 한다(줄 안의 버튼을 누른 경우는 버튼이 먼저 받으므로 여기로 오지 않는다).
    /// 줄에 Button을 붙이면 UIThemeApplier가 진홍 버튼으로 칠해 버려서 대신 쓴다. 마우스를 올리면 줄이 살짝 밝아진다.
    /// </summary>
    public class RowClickHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverBrightness = 1.35f;

        private System.Action _onClick;
        private Image _image;
        private Color _baseColor;
        private bool _hovered;

        public static RowClickHandler Attach(GameObject row, System.Action onClick)
        {
            var handler = row.GetComponent<RowClickHandler>() ?? row.AddComponent<RowClickHandler>();
            handler._onClick = onClick;
            handler._image = row.GetComponent<Image>();
            return handler;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _onClick?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_image == null || _hovered) return;
            _hovered = true;
            _baseColor = _image.color; // 테마가 입혀진 뒤의 색을 기준으로 한다
            var c = _baseColor;
            _image.color = new Color(Mathf.Min(1f, c.r * HoverBrightness), Mathf.Min(1f, c.g * HoverBrightness), Mathf.Min(1f, c.b * HoverBrightness), c.a);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image == null || !_hovered) return;
            _hovered = false;
            _image.color = _baseColor;
        }
    }
}
