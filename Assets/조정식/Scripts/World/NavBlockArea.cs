using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 길찾기(VillageNavGrid)에서 통째로 막을 바닥 범위. 그림자로 장애물을 알 수 없는 것(가축 우리처럼 울타리로 둘러싼 땅)에 단다.
    /// 용병·강아지는 이 범위로 들어가지도, 이 안에서 나타나지도 않는다.
    /// </summary>
    public class NavBlockArea : MonoBehaviour
    {
        [SerializeField] private Rect area; // 월드 좌표

        public Rect Area => area;

        public void Configure(Rect worldArea) => area = worldArea;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(area.center, area.size);
        }
    }
}
