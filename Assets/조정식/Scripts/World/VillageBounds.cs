using UnityEngine;
using UnityEngine.Tilemaps;

namespace GN3.World
{
    /// <summary>
    /// 마을 전체 범위 = 바닥 타일맵(가장 넓은 Tilemap)의 사각형. 카메라가 둘러볼 수 있는 끝(CameraZoom)이고,
    /// 용병·강아지가 걷는 길찾기 영역(VillagePartyPresenter·VillageDogs)은 여기서 안쪽 여백을 뺀다.
    /// 처음 화면(가운데 칸)을 3×3으로 넓힌 바닥이다(VillageExpandBuilder). 씬마다 처음 물을 때 한 번 찾는다.
    /// </summary>
    public static class VillageBounds
    {
        private static Rect _world;
        private static int _sceneHandle = -1;

        /// <summary>바닥 타일맵의 월드 사각형. 바닥이 없으면 Rect.zero.</summary>
        public static Rect World
        {
            get
            {
                int handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (handle != _sceneHandle || _world.width <= 0f)
                {
                    _world = Measure();
                    _sceneHandle = handle;
                }
                return _world;
            }
        }

        public static bool Exists => World.width > 0f;

        /// <summary>3×3 바닥의 가운데 칸(처음 화면). 용병·강아지는 이 안에서만 걷는다(카메라·건설은 World 전체).</summary>
        public static Rect Center
        {
            get
            {
                var world = World;
                return new Rect(world.x + world.width / 3f, world.y + world.height / 3f, world.width / 3f, world.height / 3f);
            }
        }

        // 바닥은 바깥 바닥·광장·흙길(칠하기 층) 여러 장일 수 있어 가장 넓은 것(바깥 바닥)을 쓴다.
        private static Rect Measure()
        {
            Tilemap floor = null;
            float floorArea = 0f;
            foreach (var tilemap in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            {
                tilemap.CompressBounds();
                var size = tilemap.localBounds.size;
                if (size.x * size.y > floorArea) { floor = tilemap; floorArea = size.x * size.y; }
            }
            if (floor == null) return Rect.zero;
            var local = floor.localBounds;
            Vector3 min = floor.transform.TransformPoint(local.min);
            Vector3 max = floor.transform.TransformPoint(local.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _world = Rect.zero;
            _sceneHandle = -1;
        }
    }
}
