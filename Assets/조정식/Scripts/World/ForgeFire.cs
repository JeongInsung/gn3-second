using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GN3.World
{
    /// <summary>
    /// 대장간 화덕 불: 불꽃 픽셀이 위로 흘러가며 일렁이는 빛(GN3/FireGlow) + 빠르게 깜빡이는 화덕 조명 + 위로 튀는 불티.
    /// 화덕은 대장간이 쓰는 불이라 창문처럼 새벽에 꺼지지 않고, 낮에도 은은하게 일렁이다 밤에 세게 빛난다.
    /// 에디터 메뉴 "GN3/Light/창문 불빛 만들기 (현재 씬)"이 화덕 사각형(WindowRects의 forgeIndices)으로 만든다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class ForgeFire : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FireTimeId = Shader.PropertyToID("_FireTime");
        private static readonly int ForgeUvId = Shader.PropertyToID("_ForgeUV");

        [SerializeField] private float glowIntensity = 1.6f;
        [SerializeField] private float lightIntensity = 1.3f;
        [Tooltip("낮에도 이 비율만큼은 불이 보인다(밤 = 1).")]
        [Range(0f, 1f)] [SerializeField] private float dayLevel = 0.35f;
        [Tooltip("조명 깜빡임 세기(±비율)")]
        [Range(0f, 0.6f)] [SerializeField] private float flicker = 0.35f;
        [SerializeField] private float embersPerSecond = 6f;
        [SerializeField] private Light2D fireLight;
        [SerializeField] private ParticleSystem embers;
        [Tooltip("마스크 텍스처 안에서 불꽃의 아래 v와 높이(셰이더가 아래 노랑 → 위 주황으로 색을 나눈다)")]
        [SerializeField] private Vector2 forgeUv = new Vector2(0f, 1f);

        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _block;

        public Light2D FireLight => fireLight;

        public void Configure(Light2D light, ParticleSystem particles, Vector2 uv)
        {
            fireLight = light;
            embers = particles;
            forgeUv = uv;
        }

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _block ??= new MaterialPropertyBlock();
            DayNightCycle.Applied += Apply; // 시간이 바뀌면 에디터에서도 바로 밝기를 바꾼다
            Apply();
        }

        private void OnDisable() => DayNightCycle.Applied -= Apply;

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (_renderer == null) return;
            float time = Application.isPlaying ? Time.time : (float)(System.DateTime.Now.TimeOfDay.TotalSeconds % 1000.0);
            float level = Mathf.Lerp(dayLevel, 1f, DayNightCycle.NightLightFactor);

            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, glowIntensity * level);
            _block.SetFloat(FireTimeId, time);
            _block.SetVector(ForgeUvId, new Vector4(forgeUv.x, forgeUv.y, 0f, 0f));
            _renderer.SetPropertyBlock(_block);

            // 느린 일렁임 + 빠른 떨림 두 겹. 밝기만 바꾸고 enabled는 건드리지 않는다(Light2DManager Assertion 방지).
            if (fireLight != null)
            {
                float slow = Mathf.PerlinNoise(time * 2.3f, 0.37f) - 0.5f;
                float fast = Mathf.PerlinNoise(time * 11f, 5.1f) - 0.5f;
                fireLight.intensity = lightIntensity * level * Mathf.Max(0f, 1f + flicker * (slow * 1.4f + fast * 0.8f));
            }

            if (embers != null)
            {
                var emission = embers.emission;
                emission.rateOverTime = embersPerSecond * level;
            }
        }
    }
}
