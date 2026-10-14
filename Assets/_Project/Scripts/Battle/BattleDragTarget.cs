using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.Battle
{
    /// <summary>배치 단계에서 아군을 끌어 옮기고(호버로 테두리 표시) 받는 입력. 아군 유닛의 클릭 영역에만 붙는다(땅 클릭은 BattleClickTarget).</summary>
    public class BattleDragTarget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<PointerEventData> OnBegin;
        public Action<PointerEventData> OnMove;
        public Action<PointerEventData> OnEnd;
        public Action OnEnter;
        public Action OnExit;

        public void OnBeginDrag(PointerEventData eventData) => OnBegin?.Invoke(eventData);
        public void OnDrag(PointerEventData eventData) => OnMove?.Invoke(eventData);
        public void OnEndDrag(PointerEventData eventData) => OnEnd?.Invoke(eventData);
        public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
    }
}
