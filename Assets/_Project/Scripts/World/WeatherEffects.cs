using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 오늘 날씨(Weather)를 화면에 보인다: 비·폭우는 비스듬히 떨어지는 빗줄기, 눈은 흔들리며 내려오는 눈송이.
    /// 파티클은 메인 카메라의 자식이라 화면을 따라다니고, 줌에 맞춰 내리는 범위·양을 화면 넓이에 맞춘다.
    /// 날씨가 바뀌면 방출만 바꿔 이미 내리던 것은 끝까지 떨어진다. 조명 계수 전환(Weather.Tick)도 여기서 돌린다.
    /// 흐린 날(Weather.Overcast)은 화면 전체에 회청색 막을 얇게 덮어 가라앉힌다.
    /// MainMenuBootstrapper가 MainScene에서 만든다.
    /// </summary>
    public class WeatherEffects : MonoBehaviour
    {
        private const int SortingOrder = 97;      // 새·나비(96) 위
        private const float RainSpeed = 14f;      // 유닛/초
        private const float RainSlant = -2.5f;    // 바람에 왼쪽으로 기운다
        private const float SnowSpeed = 0.9f;

        private Camera _camera;
        private float _referenceArea;
        private ParticleSystem _rain;
        private ParticleSystem _snow;
        private SpriteRenderer _veil;

        private const int VeilSortingOrder = 98;   // 비·눈(97) 위, UI 아래 — 빗줄기도 같이 가라앉는다
        private const float VeilMaxAlpha = 0.12f; // 0.3은 비·흐림에서 화면이 너무 뿌옇게 떠 보였다
        private static readonly Color VeilGrey = new Color(0.45f, 0.5f, 0.58f);
        private static readonly Color VeilSnow = new Color(0.88f, 0.9f, 0.96f);

        private void Start()
        {
            _camera = Camera.main;
            if (_camera == null || !_camera.orthographic)
            {
                enabled = false;
                return;
            }
            _referenceArea = ViewArea();
            var material = CreateMaterial();
            _rain = CreateSystem("Rain", material, RainTexture(), true);
            _snow = CreateSystem("Snow", material, SnowTexture(), false);
            _veil = CreateVeil();
        }

        /// <summary>흐린 날 화면 전체를 회청색(눈 오는 날은 흰빛)으로 가라앉히는 막. 조명을 안 받는 재질.</summary>
        private SpriteRenderer CreateVeil()
        {
            var go = new GameObject("OvercastVeil", typeof(SpriteRenderer));
            go.transform.SetParent(_camera.transform, false);
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader);
            renderer.sortingOrder = VeilSortingOrder;
            renderer.color = Color.clear;
            return renderer;
        }

        private void UpdateVeil()
        {
            float halfHeight = _camera.orthographicSize;
            _veil.transform.localPosition = new Vector3(0f, 0f, 10f); // 카메라 앞(월드 z 0)
            _veil.transform.localScale = new Vector3(halfHeight * 2f * _camera.aspect + 2f, halfHeight * 2f + 2f, 1f);
            // 밤엔 회색 막이 어둠을 밝혀 보이지 않게 약하게
            float alpha = Weather.Overcast * VeilMaxAlpha * (1f - DayNightCycle.NightLightFactor * 0.7f);
            var color = Color.Lerp(VeilGrey, VeilSnow, Weather.SnowWhiteness);
            color.a = alpha;
            _veil.color = color;
            _veil.enabled = alpha > 0.002f;
        }

        private void Update()
        {
            Weather.Tick(Time.unscaledDeltaTime);

            var kind = Weather.Today.Kind;
            float areaScale = ViewArea() / _referenceArea;
            float rate = Rate(kind) * areaScale;
            Fit(_rain, RainSpeed, Weather.IsRain(kind) ? rate : 0f);
            Fit(_snow, SnowSpeed, Weather.IsSnow(kind) ? rate : 0f);
            UpdateVeil();
        }

        /// <summary>시작 화면 넓이 기준 초당 개수.</summary>
        private static float Rate(WeatherKind kind) => kind switch
        {
            WeatherKind.SunShower => 120f,
            WeatherKind.Rain => 250f,
            WeatherKind.HeavyRain => 700f,
            WeatherKind.SunnySnow => 50f,
            WeatherKind.Snow => 120f,
            WeatherKind.HeavySnow => 450f,
            _ => 0f,
        };

        private float ViewArea()
        {
            float h = _camera.orthographicSize * 2f;
            return h * h * _camera.aspect;
        }

        /// <summary>지금 화면 위쪽 가장자리 전체에서 나와 화면 아래까지 떨어지게 범위·수명·방출량을 맞춘다.</summary>
        private void Fit(ParticleSystem system, float speed, float rate)
        {
            float halfHeight = _camera.orthographicSize;
            float width = halfHeight * 2f * _camera.aspect;
            var shape = system.shape;
            shape.scale = new Vector3(width + 4f, 0.1f, 0.1f); // 비스듬히 떨어져도 오른쪽 끝이 비지 않게 넉넉히
            system.transform.localPosition = new Vector3(1f, halfHeight + 0.5f, 10f); // 카메라 앞(z=+10 → 월드 z 0)
            var main = system.main;
            main.startLifetime = (halfHeight * 2f + 1f) / speed;
            var emission = system.emission;
            emission.rateOverTime = rate;
        }

        private ParticleSystem CreateSystem(string name, Material material, Texture2D texture, bool rain)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(_camera.transform, false);
            var system = go.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = rain ? 4000 : 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 화면을 따라다닌다
            main.startSpeed = 0f;                                        // 움직임은 velocityOverLifetime이 정한다
            main.startSize3D = true;
            if (rain)
            {
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.025f, 0.035f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startRotation = Mathf.Atan2(-RainSlant, RainSpeed); // 떨어지는 방향으로 기울인다
                main.startColor = new Color(0.75f, 0.82f, 0.95f, 0.45f);
            }
            else
            {
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                main.startSizeY = main.startSizeX;
                main.startColor = new Color(1f, 1f, 1f, 0.9f);
            }
            main.startSizeZ = 1f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // x·y·z는 같은 모드(두 상수 사이 무작위)여야 한다.
            velocity.x = rain ? new ParticleSystem.MinMaxCurve(RainSlant, RainSlant) : new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            velocity.y = rain ? new ParticleSystem.MinMaxCurve(-RainSpeed * 1.1f, -RainSpeed * 0.9f) : new ParticleSystem.MinMaxCurve(-SnowSpeed * 1.2f, -SnowSpeed * 0.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            if (!rain)
            {
                var noise = system.noise; // 눈송이가 좌우로 흔들린다
                noise.enabled = true;
                noise.strength = 0.35f;
                noise.frequency = 0.4f;
                noise.scrollSpeed = 0.2f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = SortingOrder;
            renderer.material = new Material(material) { mainTexture = texture };

            var emission = system.emission;
            emission.rateOverTime = 0f;
            system.Play();
            return system;
        }

        /// <summary>낮엔 밝고 밤엔 어두워지게 2D 조명을 받는 재질. 없으면 조명을 안 받는 재질로.</summary>
        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")
                         ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
            return new Material(shader);
        }

        /// <summary>2×16 흰 선, 위로 갈수록 투명(빗줄기 꼬리).</summary>
        private static Texture2D RainTexture()
        {
            const int w = 2, h = 16;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.15f, y / (h - 1f))));
            texture.Apply();
            return texture;
        }

        /// <summary>8×8 부드러운 흰 점.</summary>
        private static Texture2D SnowTexture()
        {
            const int size = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - d)));
            }
            texture.Apply();
            return texture;
        }
    }
}
