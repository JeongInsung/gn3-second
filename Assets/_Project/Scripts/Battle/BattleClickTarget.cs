using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.Battle
{
    /// <summary>전투 필드의 클릭(왼쪽·오른쪽 버튼 모두)을 받아 넘겨 준다. 땅(필드 배경)과 유닛마다 하나씩 붙는다.</summary>
    public class BattleClickTarget : MonoBehaviour, IPointerClickHandler
    {
        public Action<PointerEventData> OnClick;

        public void OnPointerClick(PointerEventData eventData) => OnClick?.Invoke(eventData);
    }
}
