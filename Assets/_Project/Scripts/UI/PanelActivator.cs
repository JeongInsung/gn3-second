using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class PanelActivator
    {
        private static readonly List<GameObject> _group = new List<GameObject>();

        /// <summary>서로 배타적으로 열리는 패널 그룹을 등록한다. 이후 Open은 같은 그룹의 다른 패널을 자동으로 닫는다.</summary>
        public static void RegisterGroup(params GameObject[] panels)
        {
            _group.Clear();
            _group.AddRange(panels);
        }

        public static void Open(GameObject panel)
        {
            foreach (var other in _group)
            {
                if (other != null && other != panel)
                    other.SetActive(false);
            }

            panel.SetActive(true);

            // 비활성 상태였던 동안에는 ScrollRect 안 리스트의 레이아웃이 계산되지 않으므로
            // 활성화되는 시점에 각 ContentSizeFitter를 직접 다시 계산해준다.
            foreach (var fitter in panel.GetComponentsInChildren<ContentSizeFitter>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate(fitter.GetComponent<RectTransform>());
        }
    }
}
