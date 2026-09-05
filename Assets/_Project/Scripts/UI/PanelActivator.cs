using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class PanelActivator
    {
        public static void Open(GameObject panel)
        {
            panel.SetActive(true);

            // 비활성 상태였던 동안에는 ScrollRect 안 리스트의 레이아웃이 계산되지 않으므로
            // 활성화되는 시점에 각 ContentSizeFitter를 직접 다시 계산해준다.
            foreach (var fitter in panel.GetComponentsInChildren<ContentSizeFitter>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate(fitter.GetComponent<RectTransform>());
        }
    }
}
