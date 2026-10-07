using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 위를 천천히 지나가는 큰 구름 그림자. 위에서 내려다보는 화면이라 하늘은 안 보이지만 그림자로 하늘이 있다는 걸 느끼게 한다.
    /// 구름 모양은 Perlin 노이즈로 만들고, 알파를 몇 단계로 끊고 도트를 크게 잡아 픽셀아트처럼 보이게 한다.
    /// 바람(VillageWind) 방향으로 흘러가다 화면을 벗어나면 반대편에서 새 모양으로 다시 들어온다. 밤엔 사라진다.
    /// </summary>
    public class CloudShadows : MonoBehaviour
    {
        [Tooltip("Sprite-Unlit-Default. 그림자 진하기가 조명에 따라 바뀌지 않게")]
        [SerializeField] private Material material;
        [SerializeField] private int count = 2;
        [Tooltip("바람 방향 이동 속도(유닛/초). 돌풍 세기가 곱해진다")]
        [SerializeField] private float speed = 0.07f;
        [Tooltip("구름 폭(화면 폭 대비 비율) 최소~최대")]
        [SerializeField] private Vector2 widthRange = new Vector2(0.18f, 0.3f);
        [SerializeField] private Color color = new Color(0.08f, 0.1f, 0.22f);
        [Tooltip("0.16은 낮 화면에서 8% 정도만 어두워져 거의 안 보였다")]
        [Range(0f, 0.5f)] [SerializeField] private float opacity = 0.3f;
        [Tooltip("구름 도트 크기(화면 px)")]
        [SerializeField] private int dotPixels = 4;
        [SerializeField] private int sortingOrder = 90; // 건물·캐릭터(1) 위, 꽃잎(95) 아래

        private readonly List<SpriteRenderer> _clouds = new List<SpriteRenderer>();
        private Rect _view;
        private float _dot;

        public void Configure(Material unlit) => material = unlit;

        private void Start()
        {
            var cam = Camera.main;
            if (cam == null || material == null)
            {
                enabled = false;
                return;
            }
            _view = PixelSprites.ViewRect(cam);
            _dot = PixelSprites.ScreenPixel(cam) * dotPixels;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Cloud Shadow", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sortingOrder;
                _clouds.Add(renderer);
                Reshape(renderer);
                // 처음엔 화면 여기저기에 흩어 둔다(전부 가장자리에서 시작하면 한동안 그림자가 없다)
                float x = Mathf.Lerp(_view.xMin, _view.xMax, (i + Random.value) / count);
                go.transform.position = new Vector3(x, Random.Range(_view.yMin, _view.yMax), 0f);
            }
        }

        private void Update()
        {
            Vector3 step = (Vector3)(VillageWind.Direction * speed * VillageWind.Gust * Time.deltaTime);
            var tint = color;
            tint.a = opacity * (1f - DayNightCycle.NightLightFactor);
            if (Application.isPlaying) tint.a *= 1f - Weather.Overcast; // 하늘이 덮이면 개별 구름 그림자는 없다

            foreach (var cloud in _clouds)
            {
                cloud.transform.position += step;
                cloud.color = tint;
                var bounds = cloud.bounds;
                if (bounds.min.x > _view.xMax || bounds.min.y > _view.yMax) Respawn(cloud);
            }
        }

        /// <summary>바람이 불어오는 쪽(왼쪽 아래) 화면 밖에서 새 모양으로 다시 들어온다.</summary>
        private void Respawn(SpriteRenderer cloud)
        {
            Reshape(cloud);
            var extents = cloud.bounds.extents;
            // 위쪽으로도 조금씩 흘러가므로 아래쪽에 조금 더 치우쳐 넣는다
            float y = Random.Range(_view.yMin - extents.y, _view.yMax - extents.y);
            cloud.transform.position = new Vector3(_view.xMin - extents.x, y, 0f);
        }

        private void Reshape(SpriteRenderer cloud)
        {
            if (cloud.sprite != null)
            {
                Destroy(cloud.sprite.texture);
                Destroy(cloud.sprite);
            }
            float worldWidth = _view.width * Random.Range(widthRange.x, widthRange.y);
            int width = Mathf.Max(8, Mathf.RoundToInt(worldWidth / _dot));
            int height = Mathf.Max(6, Mathf.RoundToInt(width * Random.Range(0.45f, 0.6f)));
            var texture = CloudTexture(width, height, Random.Range(0f, 1000f));
            cloud.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 1f / _dot);
        }

        /// <summary>타원 안쪽일수록 진하고, 노이즈로 가장자리를 울퉁불퉁하게. 알파는 3단계로 끊는다.</summary>
        private static Texture2D CloudTexture(int width, int height, float seed)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            float noiseScale = 6f / width;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float ex = (x + 0.5f) / width * 2f - 1f, ey = (y + 0.5f) / height * 2f - 1f;
                float falloff = 1f - (ex * ex + ey * ey);
                float noise = Mathf.PerlinNoise(seed + x * noiseScale, seed + y * noiseScale * 1.6f) * 0.65f
                            + Mathf.PerlinNoise(seed * 2f + x * noiseScale * 2.5f, y * noiseScale * 2.5f) * 0.35f;
                float value = falloff + (noise - 0.5f) * 0.9f;
                float alpha = value > 0.45f ? 1f : value > 0.22f ? 0.65f : value > 0.05f ? 0.3f : 0f;
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
