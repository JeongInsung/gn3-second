using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.UI
{
    /// <summary>이 오브젝트에 마우스를 올리면 TooltipUI에 Text를 표시한다.</summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            TooltipUI.Instance.Show(Text);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            TooltipUI.Instance.Hide();
        }

        private void OnDisable()
        {
            if (TooltipUI.Instance != null)
                TooltipUI.Instance.Hide();
        }
    }
}
