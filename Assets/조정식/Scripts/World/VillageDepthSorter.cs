using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GN3.World
{
    /// <summary>
    /// 마을 건물·장식과 걸어 다니는 캐릭터의 앞뒤를 "발밑 높이(y)"로 정한다(Renderer2D 정렬축 = Y, 아래일수록 앞).
    ///
    /// 그냥 두면 두 가지가 어긋난다.
    /// - 물체 그림은 pivot이 그림 한가운데라, 정렬 기준이 발밑이 아니라 그림 중간 높이가 된다.
    /// - 창문 불빛(order 2)·외곽선(order 3) 같은 덧그림이 건물(1)보다 order가 높아 앞을 지나는 캐릭터(1)를 덮는다.
    ///
    /// 그래서 Play 시작 때 물체마다 바닥 점유 영역 가운데 높이에 "SortAnchor"(SortingGroup, order 1)를 만들고
    /// 물체를 그 아래로 옮긴다. 덧그림은 그룹 안에서만 순서를 가지므로 캐릭터를 덮지 않고,
    /// 그룹 전체가 SortAnchor 높이로 캐릭터(발 위치 SortingGroup)와 앞뒤가 정해진다.
    /// 그림자는 그룹 안에 있으면 건물과 함께 캐릭터 위에 그려지므로 그룹 밖으로 빼고 sourceOverride로 계속 따라가게 한다.
    /// 월드 위치는 그대로라 보이는 모습·조명·애니메이션은 바뀌지 않는다.
    /// </summary>
    public static class VillageDepthSorter
    {
        private const int SortingOrder = 1; // 캐릭터(VillageWanderer)와 같은 order → Y축으로만 앞뒤가 갈린다

        public static void Apply(IEnumerable<VillageNavGrid.Obstacle> obstacles)
        {
            foreach (var obstacle in obstacles)
            {
                var target = obstacle.Renderer.transform;
                if (target.parent != null && target.parent.name == "SortAnchor") continue; // 이미 적용됨

                var anchor = new GameObject("SortAnchor", typeof(SortingGroup)).transform;
                anchor.SetParent(target.parent, false);
                anchor.position = new Vector3(obstacle.Footprint.center.x, obstacle.Footprint.center.y, target.position.z);
                anchor.SetSiblingIndex(target.GetSiblingIndex());
                var group = anchor.GetComponent<SortingGroup>();
                group.sortingLayerID = obstacle.Renderer.sortingLayerID;
                group.sortingOrder = SortingOrder;

                var shadow = obstacle.Shadow;
                if (shadow != null) // 그림자 없이 놓인 물체는 SortAnchor만
                {
                    shadow.sourceOverride = obstacle.Renderer;
                    shadow.transform.SetParent(target.parent, true);
                }

                target.SetParent(anchor, true);
            }
        }
    }
}
