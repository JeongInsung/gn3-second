using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.UI
{
    /// <summary>
    /// 끌어 온 것(DragItem)을 받는 곳(대원 가방 카드). 받을 수 있는 것을 끌고 올라오면 금빛 테두리로 강조한다.
    /// 마우스를 받는 그래픽(Image, raycastTarget)이 같은 오브젝트에 있어야 한다.
    /// </summary>
    public class DropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<object, bool> Accepts;
        public Action<object> Drop;
        private GameObject _highlight;

        public void OnDrop(PointerEventData eventData)
        {
            SetHighlight(false);
            var drag = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<DragItem>() : null;
            if (drag == null || drag.Payload == null) return;
            if (Accepts != null && !Accepts(drag.Payload)) return;
            Drop?.Invoke(drag.Payload);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            var drag = DragItem.Current;
            if (drag == null || drag.Payload == null) return;
            SetHighlight(Accepts == null || Accepts(drag.Payload));
        }

        public void OnPointerExit(PointerEventData eventData) => SetHighlight(false);

        private void SetHighlight(bool on)
        {
            if (on && _highlight == null) _highlight = UITheme.CreateSelectFrame(transform, 3f, new Color(1f, 0.8f, 0.35f));
            if (_highlight != null) _highlight.SetActive(on);
        }
    }
}
