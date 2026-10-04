using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GN3.World
{
    /// <summary>
    /// 하루 시각(0~24시)에 맞춰 2D 조명과 후처리를 바꾸는 낮/밤 사이클.
    /// 해의 위치는 위도·날짜로 실제처럼 계산하고(화면 = 북쪽이 위인 지도), 빛 색·밝기는 해의 고도에 따라
    /// 깊은 밤 → 블루아워 → 골든아워 → 한낮으로 바뀐다. 해가 지면 달이 반대편에서 뜨고 가로등이 켜진다.
    /// [ExecuteAlways]라 에디터에서 timeOfDay 슬라이더만 움직여도 바로 보인다.
    /// </summary>
    [ExecuteAlways]
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        /// <summary>밤 조명(가로등·창문 불빛)이 켜진 정도 0~1. 해가 지기 조금 전부터 켜져 밤에 1.</summary>
        public static float NightLightFactor { get; private set; }

        [Header("시간")]
        [Range(0f, 24f)] [SerializeField] private float timeOfDay = 10f;
        [Tooltip("켜면 Play 중 시간이 저절로 흐른다. 끄면 슬라이더로만 조종.")]
        [SerializeField] private bool autoAdvance;
        [SerializeField] private float hoursPerSecond = 0.2f;

        [Header("조명")]
        [SerializeField] private Light2D globalLight;
        [SerializeField] private Light2D sunLight;
        [SerializeField] private Light2D moonLight;
        [Tooltip("가로등 발밑 바닥을 비추는 빛 웅덩이")]
        [SerializeField] private List<Light2D> lampLights = new List<Light2D>();
        [Tooltip("가로등 머리(유리)만 빛나게 하는 작은 빛")]
        [SerializeField] private List<Light2D> lampGlows = new List<Light2D>();

        [Header("궤도")]
        [SerializeField] private Vector2 orbitCenter = new Vector2(0f, 0.5f);
        [SerializeField] private float orbitDistance = 14f;

        [Header("시간대별 빛 (해의 고도 기준)")]
        [Tooltip("주변광 색. 왼쪽 끝 = 고도 -18°(깊은 밤), 오른쪽 끝 = 고도 40°(한낮). " +
                 "-6~0° 블루아워, 0~12° 골든아워.")]
        [SerializeField] private Gradient ambientColor = DefaultAmbientColor();
        [Tooltip("주변광 밝기. 가로축 = 해의 고도(°).")]
        [SerializeField] private AnimationCurve ambientIntensity = DefaultAmbientIntensity();
        [Tooltip("햇빛 색. 왼쪽 끝 = 지평선(0°), 오른쪽 끝 = 고도 40°.")]
        [SerializeField] private Gradient sunColor = DefaultSunColor();
        [Tooltip("햇빛 밝기. 가로축 = 해의 고도(°).")]
        [SerializeField] private AnimationCurve sunIntensity = DefaultSunIntensity();
        [Tooltip("URP 2D 조명 그림자는 화면 끝까지 늘어나 길이를 못 정한다. 그림자는 ProjectedShadow가 그리므로 기본은 끈다.")]
        [SerializeField] private bool sunCastsShadows;

        [Header("후처리 (블룸·비네트·색감)")]
        [SerializeField] private Volume lightingVolume;
        [SerializeField] private float dayVignette = 0.2f;
        [SerializeField] private float nightVignette = 0.45f;
        [SerializeField] private float daySaturation = 10f;
        [SerializeField] private float goldenHourSaturation = 20f;
        [Tooltip("밤에는 사람 눈이 색을 덜 느껴 채도가 빠져 보인다.")]
        [SerializeField] private float nightSaturation = -25f;
        [SerializeField] private float dayContrast = 10f;
        [SerializeField] private float nightContrast = 20f;

        [Header("실제 태양 위치")]
        [Tooltip("위도(°). 37.5 = 서울. 정오 해 높이와 해 뜨고 지는 시각이 이 값과 계절로 정해진다.")]
        [Range(-66f, 66f)] [SerializeField] private float latitude = 37.5f;
        [Tooltip("켜면 오늘 날짜의 계절을 쓴다. 끄면 아래 Day Of Year를 쓴다.")]
        [SerializeField] private bool useTodayDate = true;
        [Tooltip("1월 1일 = 1. 예: 80 춘분, 172 하지, 266 추분, 355 동지")]
        [Range(1, 365)] [SerializeField] private int dayOfYear = 80;

        [Header("그림자 (ProjectedShadow)")]
        [Tooltip("해가 지평선 근처일 때 그림자 최대 길이(실제 높이 대비 배율). 실제로는 무한대로 길어진다.")]
        [SerializeField] private float maxShadowLengthRatio = 1.5f;
        [Tooltip("이 고도(°) 아래에서는 그림자가 서서히 흐려진다(해가 낮으면 빛이 약하고 퍼진다).")]
        [SerializeField] private float shadowFadeAltitude = 12f;
        [Range(0f, 1f)] [SerializeField] private float shadowAlpha = 0.35f;

        [Header("밤")]
        [SerializeField] private Color moonColor = new Color(0.5f, 0.62f, 1f);
        [SerializeField] private float moonIntensity = 0.05f;
        [Tooltip("2D 그림자는 화면 끝까지 늘어나 밤에 검은 띠가 생겨서 기본은 끈다.")]
        [SerializeField] private bool moonCastsShadows;
        [SerializeField] private float lampIntensity = 1.5f;
        [SerializeField] private float lampGlowIntensity = 1.6f;

        private const float GradientMinAltitude = -18f; // 천문 박명이 끝나는 고도 = 깊은 밤
        private const float GradientMaxAltitude = 40f;

        private static Gradient DefaultAmbientColor()
        {
            // 고도 → 위치: (alt + 18) / 58
            var g = new Gradient();
            g.SetKeys(new[]
            {
                new GradientColorKey(new Color(0.15f, 0.2f, 0.45f), 0f),       // -18° 깊은 밤
                new GradientColorKey(new Color(0.45f, 0.4f, 0.75f), 0.207f),   // -6° 블루아워
                new GradientColorKey(new Color(0.85f, 0.55f, 0.6f), 0.31f),    // 0° 해 뜨고 지는 순간(분홍)
                new GradientColorKey(new Color(1f, 0.68f, 0.45f), 0.448f),     // 8° 골든아워
                new GradientColorKey(new Color(1f, 0.98f, 0.95f), 1f),         // 40° 한낮
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultAmbientIntensity() => new AnimationCurve(
            new Keyframe(-18f, 0.03f), new Keyframe(-6f, 0.1f), new Keyframe(0f, 0.18f),
            new Keyframe(12f, 0.36f), new Keyframe(35f, 0.5f));

        private static Gradient DefaultSunColor()
        {
            // 고도 → 위치: alt / 40
            var g = new Gradient();
            g.SetKeys(new[]
            {
                new GradientColorKey(new Color(1f, 0.38f, 0.15f), 0f),    // 지평선: 짙은 주황·빨강
                new GradientColorKey(new Color(1f, 0.66f, 0.38f), 0.25f), // 10°: 골든아워
                new GradientColorKey(new Color(1f, 0.96f, 0.9f), 1f),     // 40°: 흰 햇빛
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultSunIntensity() => new AnimationCurve(
            new Keyframe(-2f, 0f), new Keyframe(3f, 0.9f), new Keyframe(12f, 1f), new Keyframe(35f, 0.8f));

        public float TimeOfDay
        {
            get => timeOfDay;
            set
            {
                timeOfDay = Mathf.Repeat(value, 24f);
                Apply();
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
#endif
            }
        }

        /// <summary>15.33 → "오후 3:20"</summary>
        public static string FormatTime(float hours)
        {
            int totalMinutes = Mathf.FloorToInt(Mathf.Repeat(hours, 24f) * 60f);
            int h = totalMinutes / 60;
            int m = totalMinutes % 60;
            string half = h < 12 ? "오전" : "오후";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{half} {h12}:{m:00}";
        }

        private bool _lightMeshesRebuilt;

        private void OnEnable()
        {
            Instance = this;
            _lightMeshesRebuilt = false;
            Apply();
#if UNITY_EDITOR
            // 에디터 모드에선 Update가 변화가 있을 때만 돌아서, 리로드 직후 다음 틱에 직접 다시 만든다.
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null || _lightMeshesRebuilt) return;
                    RebuildLightMeshes();
                    UnityEditor.SceneView.RepaintAll();
                };
#endif
        }

        /// <summary>
        /// 코드로 추가한 Point Light2D는 씬을 다시 불러오거나 스크립트가 리로드되면 빛 메시가 비어(정점 0개)
        /// 켜져 있어도 아무것도 안 비췄다(가로등·달빛이 사라짐, 실측). lightType을 바꿨다 되돌리면
        /// Light2D가 메시를 다시 만든다. 모든 Awake가 끝난 뒤 한 번 돌도록 첫 Update에서 부른다.
        /// </summary>
        private void RebuildLightMeshes()
        {
            RebuildLightMesh(sunLight);
            RebuildLightMesh(moonLight);
            foreach (var light in lampLights) RebuildLightMesh(light);
            foreach (var light in lampGlows) RebuildLightMesh(light);
            foreach (var glow in FindObjectsByType<WindowGlow>(FindObjectsSortMode.None))
                foreach (var light in glow.SpillLights) RebuildLightMesh(light);
            foreach (var fire in FindObjectsByType<ForgeFire>(FindObjectsSortMode.None))
                RebuildLightMesh(fire.FireLight);
            _lightMeshesRebuilt = true;
        }

#if UNITY_EDITOR
        // 스크립트 리로드 중 OnEnable에서 건 delayCall은 리로드에 지워져서, 리로드가 끝난 뒤 여기서 다시 건다.
        [UnityEditor.InitializeOnLoadMethod]
        private static void RebuildAfterScriptReload()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                foreach (var cycle in FindObjectsByType<DayNightCycle>(FindObjectsSortMode.None))
                {
                    cycle.RebuildLightMeshes();
                    cycle.Apply();
                }
                UnityEditor.SceneView.RepaintAll();
            };
        }
#endif

        // lightType 세터는 "바꾸기 전" 타입으로 메시를 만들고, Global을 거치면 "More than one global light" 에러가 나서
        // 공개 API로는 깨끗하게 다시 만들 방법이 없다. URP 내부 UpdateMesh(bool)를 직접 부른다(URP 17 실측).
        private static readonly System.Reflection.MethodInfo UpdateMeshMethod = typeof(Light2D).GetMethod(
            "UpdateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        public static void RebuildLightMesh(Light2D light)
        {
            if (light == null || light.lightType == Light2D.LightType.Global) return;
            if (UpdateMeshMethod == null)
            {
                Debug.LogWarning("[DayNightCycle] Light2D.UpdateMesh를 찾지 못했습니다(URP 버전 변경?). 빛이 안 보이면 Light2D를 다시 추가하세요.");
                return;
            }
            UpdateMeshMethod.Invoke(light, new object[UpdateMeshMethod.GetParameters().Length]);
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        // OnValidate 안에서는 다른 컴포넌트(Light2D)의 enabled/Transform을 바꾸는 게 무시될 수 있어 한 틱 미룬다.
        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!_lightMeshesRebuilt) RebuildLightMeshes();
                Apply();
                UnityEditor.SceneView.RepaintAll();
            };
#else
            Apply();
#endif
        }

        private void Update()
        {
            if (!_lightMeshesRebuilt) RebuildLightMeshes();
            if (Application.isPlaying && autoAdvance)
                timeOfDay = Mathf.Repeat(timeOfDay + hoursPerSecond * Time.deltaTime, 24f);
            Apply();
        }

        private void Apply()
        {
            GetSunPosition(out float altitudeDeg, out float azimuthDeg);
            // elevation = sin(고도). 밤에는 음수(지평선 아래). 낮·밤·가로등 전환은 이 값으로 정한다.
            float elevation = Mathf.Sin(altitudeDeg * Mathf.Deg2Rad);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.15f, elevation));
            float night = 1f - day;
            // 가로등은 해가 완전히 지기 조금 전부터 켜진다.
            float lamp = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.25f, elevation));
            NightLightFactor = lamp;

            // 화면은 북쪽이 위, 동쪽이 오른쪽인 지도. 해가 있는 방향(지면 기준)과 높이로 조명 위치를 잡는다.
            float azimuth = azimuthDeg * Mathf.Deg2Rad;
            Vector2 sunGround = new Vector2(Mathf.Sin(azimuth), Mathf.Cos(azimuth));
            Vector2 sunDir = sunGround;

            if (globalLight != null)
            {
                globalLight.color = ambientColor.Evaluate(Mathf.InverseLerp(GradientMinAltitude, GradientMaxAltitude, altitudeDeg));
                globalLight.intensity = Mathf.Max(0f, ambientIntensity.Evaluate(altitudeDeg));
            }

            if (sunLight != null)
            {
                sunLight.transform.position = (Vector3)(orbitCenter + sunDir * orbitDistance);
                sunLight.color = sunColor.Evaluate(Mathf.InverseLerp(0f, GradientMaxAltitude, altitudeDeg));
                sunLight.intensity = Mathf.Max(0f, sunIntensity.Evaluate(altitudeDeg));
                sunLight.shadowsEnabled = sunCastsShadows;
                sunLight.enabled = sunLight.intensity > 0.001f;
            }

            ApplyProjectedShadows(altitudeDeg, sunGround, day);

            if (moonLight != null)
            {
                moonLight.transform.position = (Vector3)(orbitCenter - sunDir * orbitDistance);
                moonLight.color = moonColor;
                moonLight.intensity = moonIntensity * night;
                moonLight.shadowsEnabled = moonCastsShadows;
                moonLight.enabled = moonLight.intensity > 0.001f;
            }

            SetLampIntensity(lampLights, lampIntensity * lamp);
            SetLampIntensity(lampGlows, lampGlowIntensity * lamp);
            ApplyPostProcessing(altitudeDeg, night);
        }

        /// <summary>
        /// 밤엔 비네트를 키우고 채도를 빼서(사람 눈은 어두우면 색을 덜 느낀다) 어둠을 강조하고,
        /// 골든아워(고도 0~12° 부근)엔 채도를 올려 노을빛을 진하게 한다. 블룸은 프로필에 고정(가로등 번짐).
        /// </summary>
        private void ApplyPostProcessing(float altitudeDeg, float night)
        {
            if (lightingVolume == null || lightingVolume.sharedProfile == null) return;
            var profile = lightingVolume.sharedProfile;
            float golden = Mathf.Clamp01(1f - Mathf.Abs(altitudeDeg - 5f) / 10f);

            if (profile.TryGet(out Vignette vignette))
                vignette.intensity.value = Mathf.Lerp(dayVignette, nightVignette, night);
            if (profile.TryGet(out ColorAdjustments color))
            {
                float saturation = Mathf.Lerp(daySaturation, goldenHourSaturation, golden);
                color.saturation.value = Mathf.Lerp(saturation, nightSaturation, night);
                color.contrast.value = Mathf.Lerp(dayContrast, nightContrast, night);
            }
        }

        private static readonly int ShadowDirId = Shader.PropertyToID("_GN3ShadowDir");
        private static readonly int ShadowAlphaId = Shader.PropertyToID("_GN3ShadowAlpha");

        /// <summary>
        /// ProjectedShadow 셰이더에 넘길 그림자 벡터(실제 높이 1당 지면 이동량)와 진하기.
        /// 그림자는 해 반대쪽으로, 길이 = 높이 × cot(고도). 북반구라 대부분 화면 위(북)쪽으로 떨어지고
        /// 아침엔 왼쪽 위(북서), 저녁엔 오른쪽 위(북동)로 돈다. 물체마다 그림 높이 중 실제 높이 비율은
        /// ProjectedShadow.heightScale이 곱한다.
        /// </summary>
        private void ApplyProjectedShadows(float altitudeDeg, Vector2 sunGround, float day)
        {
            float altitude = Mathf.Max(altitudeDeg, 0.1f) * Mathf.Deg2Rad;
            float length = Mathf.Min(1f / Mathf.Tan(altitude), maxShadowLengthRatio);
            float lowSunFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, shadowFadeAltitude, altitudeDeg));
            Shader.SetGlobalVector(ShadowDirId, -sunGround * length);
            Shader.SetGlobalFloat(ShadowAlphaId, shadowAlpha * day * lowSunFade);
        }

        /// <summary>
        /// 위도·날짜·시각으로 실제 태양 고도(°)와 방위각(°, 북=0 시계방향)을 구한다(NOAA 근사식).
        /// 시각은 지방 태양시로 보아 12시에 해가 정남쪽에 온다.
        /// </summary>
        private void GetSunPosition(out float altitudeDeg, out float azimuthDeg)
        {
            int n = useTodayDate ? System.DateTime.Now.DayOfYear : dayOfYear;
            float declination = 23.44f * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * (284 + n) / 365f);
            float hourAngle = 15f * (timeOfDay - 12f) * Mathf.Deg2Rad;
            float lat = latitude * Mathf.Deg2Rad;

            float sinAlt = Mathf.Sin(lat) * Mathf.Sin(declination) + Mathf.Cos(lat) * Mathf.Cos(declination) * Mathf.Cos(hourAngle);
            float altitude = Mathf.Asin(Mathf.Clamp(sinAlt, -1f, 1f));
            // 방위각: 남쪽 기준 각을 구한 뒤 북쪽 기준으로 돌린다(오전 = 동쪽, 오후 = 서쪽).
            float azimuthFromSouth = Mathf.Atan2(Mathf.Sin(hourAngle),
                Mathf.Cos(hourAngle) * Mathf.Sin(lat) - Mathf.Tan(declination) * Mathf.Cos(lat));
            altitudeDeg = altitude * Mathf.Rad2Deg;
            azimuthDeg = Mathf.Repeat(azimuthFromSouth * Mathf.Rad2Deg + 180f, 360f);
        }

        private static void SetLampIntensity(List<Light2D> lights, float intensity)
        {
            foreach (var light in lights)
            {
                if (light == null) continue;
                light.intensity = intensity;
                light.enabled = intensity > 0.001f;
            }
        }
    }
}
