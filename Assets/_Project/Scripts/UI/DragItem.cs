using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 끌어서 옮기는 칸(전투의 시체 수색 카드·가방 칸). 끌면 아이콘 유령이 마우스를 따라오고 원래 칸은 흐려진다.
    /// 놓은 곳에 DropTarget이 있으면 그쪽이 Payload를 받는다(DropTarget.Drop). 끌기가 시작되면 Button 클릭은 일어나지 않는다(Unity 기본).
    /// </summary>
    public class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>옮기는 것(받는 쪽이 종류를 보고 처리한다).</summary>
        public object Payload;
        /// <summary>유령 그림(없으면 회색 네모).</summary>
        public Sprite Icon;
        /// <summary>끌기를 허락하는가(가려진 수색 카드 등은 false).</summary>
        public Func<bool> CanDrag;

        /// <summary>지금 끌고 있는 것(DropTarget이 올렸을 때 강조할지 고른다).</summary>
        public static DragItem Current { get; private set; }

        private RectTransform _ghost;
        private CanvasGroup _group;
        private float _alphaBefore = 1f;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (CanDrag != null && !CanDrag()) { eventData.pointerDrag = null; return; }
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            Current = this;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _alphaBefore = _group.alpha;
            _group.alpha = 0.35f;

            var ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            ghost.transform.SetParent(canvas.transform, false);
            _ghost = (RectTransform)ghost.transform;
            _ghost.sizeDelta = new Vector2(56f, 56f);
            var image = ghost.GetComponent<Image>();
            image.sprite = Icon;
            image.preserveAspect = true;
            image.color = Icon != null ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.8f);
            image.raycastTarget = false;
            ghost.GetComponent<CanvasGroup>().blocksRaycasts = false; // 아래 DropTarget이 마우스를 받게
            ghost.transform.SetAsLastSibling();
            Follow(eventData);
        }

        public void OnDrag(PointerEventData eventData) => Follow(eventData);

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            if (_group != null) _group.alpha = _alphaBefore;
            if (Current == this) Current = null;
            // 놓기는 DropTarget.OnDrop이 먼저 받는다(Unity가 OnEndDrag보다 OnDrop을 먼저 부른다)
        }

        private void Follow(PointerEventData eventData)
        {
            if (_ghost == null) return;
            _ghost.position = eventData.position;
        }

        private void OnDisable()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            if (Current == this) Current = null;
        }
    }

}
