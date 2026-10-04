using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GN3.World
{
    /// <summary>
    /// 건물 창문 유리만 밤에 따뜻하게 빛나게 한다. 자식 "WindowGlow"(창 유리 마스크 스프라이트 + GN3/WindowGlow 가산 셰이더)에 붙는다.
    /// 밝기는 DayNightCycle.NightLightFactor(가로등과 같은 계수)를 따르되, 건물마다 켜지는 시점을 조금씩 늦추고
    /// 느린 노이즈로 살짝 깜빡여 촛불·벽난로 빛처럼 보이게 한다. 창 앞 바닥은 작은 Light2D(SpillLights)로 비춘다.
    /// 마스크·조명은 에디터 메뉴 "GN3/Light/창문 불빛 만들기 (현재 씬)"이 만든다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class WindowGlow : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Tooltip("밤에 창 유리 밝기(1 넘으면 Bloom으로 번진다).")]
        [SerializeField] private float glowIntensity = 1.4f;
        [Tooltip("깜빡임 세기(밝기 대비 ±비율).")]
        [Range(0f, 0.5f)] [SerializeField] private float flickerAmount = 0.12f;
        [SerializeField] private float flickerSpeed = 0.6f;
        [Tooltip("해 질 녘에 이 건물이 켜지는 늦음(0~1). 집마다 다르게 하면 하나둘 켜진다.")]
        [Range(0f, 0.5f)] [SerializeField] private float turnOnDelay;
        [Tooltip("이 집이 잠자리에 들어 불을 끄는 시각(0~12시). 집마다 다르게 하면 새벽에 하나둘 꺼진다. 아침까지 꺼져 있다.")]
        [Range(0f, 12f)] [SerializeField] private float bedtimeHour = 1.5f;
        [Tooltip("창 앞 바닥을 비추는 작은 빛들.")]
        [SerializeField] private List<Light2D> spillLights = new List<Light2D>();
        [SerializeField] private float spillIntensity = 0.6f;

        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _block;
        private float _seed;

        public IReadOnlyList<Light2D> SpillLights => spillLights;

        public float BedtimeHour => bedtimeHour;

        public void Configure(float delay, float bedtime, List<Light2D> lights)
        {
            turnOnDelay = delay;
            bedtimeHour = bedtime;
            spillLights = lights;
        }

        /// <summary>자정~정오 사이 bedtimeHour가 지나면 0. 게임 시간 약 3분 동안 어두워진다(툭 꺼지지 않게).</summary>
        private float AwakeFactor()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return 1f;
            float hour = cycle.TimeOfDay;
            if (hour >= 12f) return 1f;
            return 1f - Mathf.Clamp01((hour - bedtimeHour) / BedtimeFadeHours);
        }

        private const float BedtimeFadeHours = 0.05f;

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _block ??= new MaterialPropertyBlock();
            // 이름으로 고정 시드: 건물마다 깜빡임 위상이 다르고, 다시 열어도 같다.
            _seed = Mathf.Abs((transform.parent != null ? transform.parent.name : name).GetHashCode() % 1000) * 0.137f;
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (_renderer == null) return;

            float night = DayNightCycle.NightLightFactor;
            float on = Mathf.Clamp01((night - turnOnDelay) / Mathf.Max(0.01f, 1f - turnOnDelay));
            float time = Application.isPlaying ? Time.time : (float)(System.DateTime.Now.TimeOfDay.TotalSeconds % 1000.0);
            float flicker = 1f + flickerAmount * (Mathf.PerlinNoise(_seed, time * flickerSpeed) - 0.5f) * 2f;
            float level = on * AwakeFactor() * flicker;

            _renderer.enabled = level > 0.001f;
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, glowIntensity * level);
            _renderer.SetPropertyBlock(_block);

            // 밝기만 바꾼다(0이면 안 보인다). OnEnable 중에 Light2D.enabled를 켜고 끄면 아직 등록 전인 빛이라
            // URP Light2DManager에서 "Assertion failed"가 났다.
            foreach (var light in spillLights)
                if (light != null) light.intensity = spillIntensity * level;
        }
    }
}
