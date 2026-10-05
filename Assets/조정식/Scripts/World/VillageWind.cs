using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을에 부는 바람 하나. 꽃잎·구름 그림자·새가 모두 이 방향을 따라 움직여 따로 놀지 않게 한다.
    /// 방향은 꽃화분 애니메이션 시트에서 꽃잎이 날아가는 쪽(오른쪽 위)에 맞췄다.
    /// </summary>
    public static class VillageWind
    {
        public static readonly Vector2 Direction = new Vector2(1f, 0.3f).normalized;

        /// <summary>천천히 오르내리는 돌풍 세기(0.6~1.4).</summary>
        public static float Gust => Mathf.Lerp(0.6f, 1.4f, Mathf.PerlinNoise(Time.time * 0.15f, 0.37f));
    }
}
