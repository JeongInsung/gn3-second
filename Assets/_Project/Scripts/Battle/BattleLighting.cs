using System.Collections.Generic;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 화면에 마을(DayNightCycle)과 같은 시간대 빛·날씨를 입힌다. 전투 화면은 오버레이 UI라 URP 2D 조명이 닿지 않아서
    /// 같은 하늘 계산(DayNightCycle.Sample)으로 구한 빛 색을 화면 전체에 곱한다(GN3/UIMultiply).
    ///  - 마을 스프라이트는 (주변광 + 햇빛)을 받는다. 정오 기준값으로 나눠 정오엔 지금 전투 화면 그대로, 저녁엔 노을빛, 밤엔 어둡게.
    ///    밤에도 싸움이 보이게 푸른 달빛 바닥값 아래로는 어두워지지 않는다.
    ///  - 흐린 날은 마을처럼 회청색(눈 오면 흰빛) 막을 얇게 덮는다.
    ///  - 층: 판(Field) 안 유닛 층 바로 뒤에 [비·눈 → 빛 막 → 흐림 막 → 유닛 이름·막대]. 피해 숫자·일시정지·행동 패널·결과창은 그 위라 어두워지지 않는다.
    ///    막은 판보다 훨씬 크게 잡아 판 밖 잔디·숲까지 덮고, 제목·시간 글자는 막 위로 올린다.
    ///  - 유닛 그림자 층(UnitShadows)도 여기서 유닛 층 바로 앞에 만든다.
    /// BattleUIController.BuildUI가 층을 만든 뒤 Create를 부른다.
    /// </summary>
    public class BattleLighting : MonoBehaviour
    {
        private const float CoverMargin = 4000f;  // 판 밖 화면 끝까지 덮는 여유
        // 맑은 낮의 (주변광 + 햇빛) 기준 — 이 이상이면 전투 화면이 원래 모습 그대로. 여름 정오(1.25)보다 조금 낮게 잡아
        // 바닥이 이미 0.85 밝기로 깔린 전투 화면이 흐림·비에 마을보다 더 어두워지지 않게 한다.
        private const float NoonReference = 1.1f;
        private static readonly Color NightFloor = new Color(0.3f, 0.34f, 0.5f); // 달빛: 이보다 어두워지지 않는다
        private const float BlobAlpha = 0.35f;     // 유닛 발밑 동그란 그림자(기본)

        /// <summary>지금 전투 화면의 하늘(그림자 방향·진하기 등). 전투씬 밖이면 기본값.</summary>
        public static DayNightCycle.SkySample Current { get; private set; }

        /// <summary>해가 비치는 정도 0~1(구름 그림자·나비가 쓴다). 밤·흐림이면 0에 가깝다.</summary>
        public static float Sunshine { get; private set; } = 1f;

        /// <summary>나비를 숨길 때(비·눈·밤).</summary>
        public static bool HideButterflies { get; private set; }

        /// <summary>유닛 그림자가 들어가는 층(유닛 층 바로 앞).</summary>
        public static RectTransform UnitShadowLayer { get; private set; }

        /// <summary>유닛 이름·체력 막대가 옮겨 가는 층(빛 막 위 — 마을 이름표처럼 밤에도 어두워지지 않는다).</summary>
        public static RectTransform UnitLabelLayer { get; private set; }

        /// <summary>지금 빛 색(빛 막이 곱하는 색). 빛 막 위에 그려지는 것(떨어지는 잎)이 직접 곱한다.</summary>
        public static Color LightColor { get; private set; } = Color.white;

        private static readonly List<Image> _blobs = new List<Image>();

        private Image _light;
        private Image _veil;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Current = default;
            Sunshine = 1f;
            HideButterflies = false;
            UnitShadowLayer = null;
            UnitLabelLayer = null;
            LightColor = Color.white;
            _blobs.Clear();
        }

        public static void Create(RectTransform field, RectTransform unitLayer)
        {
            UnitShadowLayer = CreateCover(field, "UnitShadows", false);
            UnitShadowLayer.SetSiblingIndex(unitLayer.GetSiblingIndex());

            int index = unitLayer.GetSiblingIndex() + 1;
            var weather = CreateCover(field, "Weather", false);
            weather.SetSiblingIndex(index++);
            weather.gameObject.AddComponent<BattleWeather>();

            var light = CreateCover(field, "Light", true);
            light.SetSiblingIndex(index++);
            var lightImage = light.GetComponent<Image>();
            var shader = Resources.Load<Shader>("Shaders/UIMultiply");
            if (shader != null) lightImage.material = new Material(shader) { hideFlags = HideFlags.DontSave };
            else Debug.LogWarning("[BattleLighting] Resources/Shaders/UIMultiply를 찾지 못했습니다. 빛 막을 끕니다.");
            lightImage.enabled = shader != null;

            var veil = CreateCover(field, "OvercastVeil", true);
            veil.SetSiblingIndex(index++);

            UnitLabelLayer = CreateCover(field, "UnitLabels", false);
            UnitLabelLayer.SetSiblingIndex(index);

            var lighting = light.gameObject.AddComponent<BattleLighting>();
            lighting._light = lightImage;
            lighting._veil = veil.GetComponent<Image>();
            lighting.Apply();

            RaiseTitles(field);
        }

        /// <summary>유닛 발밑 동그란 그림자. 해 그림자가 진할수록 옅게(밤·비에는 그대로 둬 유닛이 떠 보이지 않게).</summary>
        public static void RegisterBlob(Image blob)
        {
            if (blob != null) _blobs.Add(blob);
        }

        /// <summary>판을 기준으로 화면 끝까지 덮는 층. 클릭은 막지 않는다.</summary>
        private static RectTransform CreateCover(RectTransform field, string name, bool image)
        {
            var go = image ? new GameObject(name, typeof(RectTransform), typeof(Image)) : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(field, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.zero;
            if (image)
            {
                rect.offsetMin = new Vector2(-CoverMargin, -CoverMargin);
                rect.offsetMax = new Vector2(CoverMargin, CoverMargin);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.color = Color.clear;
            }
            else
            {
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            return rect;
        }

        /// <summary>판보다 먼저 그려지던 제목·웨이브·시간 글자를 빛 막 위로(판 바로 다음 Atmosphere 뒤로) 올린다.</summary>
        private static void RaiseTitles(RectTransform field)
        {
            var root = field.parent;
            if (root == null) return;
            var titles = new List<Transform>();
            for (int i = 0; i < field.GetSiblingIndex(); i++)
            {
                var child = root.GetChild(i);
                if (child.GetComponent<Text>() != null) titles.Add(child);
            }
            var atmosphere = root.Find("Atmosphere");
            foreach (var title in titles)
            {
                int after = atmosphere != null ? atmosphere.GetSiblingIndex() : field.GetSiblingIndex();
                title.SetSiblingIndex(after); // 앞쪽이 빠지면서 한 칸씩 당겨지므로 같은 자리에 넣으면 순서가 유지된다
            }
        }

        private void Update() => Apply();

        private void Apply()
        {
            var sky = DayNightCycle.Sample(GameClock.CurrentHour);
            Current = sky;

            // 마을 스프라이트가 받는 빛(전역광 + 햇빛, 햇빛은 맵 전체에 닿는다)을 정오 기준으로 나눈다.
            Color lit = (sky.AmbientColor * sky.AmbientIntensity + sky.SunColor * sky.SunIntensity) / NoonReference;
            var color = new Color(
                Mathf.Clamp01(Mathf.Max(lit.r, NightFloor.r)),
                Mathf.Clamp01(Mathf.Max(lit.g, NightFloor.g)),
                Mathf.Clamp01(Mathf.Max(lit.b, NightFloor.b)), 1f);
            if (_light != null) _light.color = color;
            LightColor = color;

            // 흐린 날 회청색 막(마을 WeatherEffects와 같은 색·진하기). 밤엔 약하게.
            float veilAlpha = Weather.Overcast * WeatherEffects.VeilMaxAlpha * (1f - sky.Lamp * 0.7f);
            var veil = Color.Lerp(WeatherEffects.VeilGrey, WeatherEffects.VeilSnow, Weather.SnowWhiteness);
            veil.a = veilAlpha;
            if (_veil != null)
            {
                _veil.color = veil;
                _veil.enabled = veilAlpha > 0.002f;
            }

            Sunshine = Mathf.Clamp01(sky.SunIntensity) * sky.Day;
            var kind = Weather.Today.Kind;
            HideButterflies = Weather.IsRain(kind) || Weather.IsSnow(kind) || sky.Night > 0.5f;

            float sunShadow = Mathf.Clamp01(sky.ShadowAlpha / 0.35f);
            for (int i = _blobs.Count - 1; i >= 0; i--)
            {
                if (_blobs[i] == null) { _blobs.RemoveAt(i); continue; }
                var c = _blobs[i].color;
                c.a = BlobAlpha * (1f - 0.6f * sunShadow);
                _blobs[i].color = c;
            }
        }
    }
}
