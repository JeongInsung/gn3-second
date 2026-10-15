using GN3.UI;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 화면의 비·눈과 빗소리. 오늘 날씨(Weather, 마을과 같다)를 따른다.
    /// 모양·기울기·속도·양은 마을 WeatherEffects 값을 화면 픽셀로 옮겨 쓴다(마을 카메라 기본 줌: 세로 10유닛 = 1080px).
    /// 입자 수백 개를 한 장짜리 메시(BattleWeatherParticles)로 그려 가볍다. 빛 막 아래에 있어 밤엔 함께 어두워진다.
    /// 마을처럼 Weather.Tick도 여기서 돌려 날씨 전환(흐림 막·그림자 진하기)이 자연스럽게 이어진다.
    /// </summary>
    public class BattleWeather : MonoBehaviour
    {
        internal const float PixelsPerUnit = 108f;
        private const string RainAmbience = "Sfx/빗소리";
        private const float AmbienceVolume = 0.5f; // 마을 VillageMusic과 같다
        private const float FadeSeconds = 2f;

        private BattleWeatherParticles _rain;
        private BattleWeatherParticles _snow;
        private AudioSource _ambience;
        private float _ambienceLevel;

        private void Start()
        {
            _rain = CreateParticles("Rain", true);
            _snow = CreateParticles("Snow", false);
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.volume = 0f;
        }

        private BattleWeatherParticles CreateParticles(string name, bool rain)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(BattleWeatherParticles));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var particles = go.GetComponent<BattleWeatherParticles>();
            particles.Init(rain);
            return particles;
        }

        private void Update()
        {
            Weather.Tick(Time.unscaledDeltaTime);
            var kind = Weather.Today.Kind;
            float rate = WeatherEffects.Rate(kind);
            bool raining = Weather.IsRain(kind);
            if (_rain != null) _rain.Rate = raining ? rate : 0f;
            if (_snow != null) _snow.Rate = Weather.IsSnow(kind) ? rate : 0f;
            UpdateAmbience(raining);
        }

        /// <summary>비가 오면 빗소리를 서서히 키우고, 그치면 줄여 멈춘다(마을 VillageMusic과 같은 크기·속도).</summary>
        private void UpdateAmbience(bool raining)
        {
            if (_ambience == null) return;
            _ambienceLevel = Mathf.MoveTowards(_ambienceLevel, raining ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            if (_ambienceLevel > 0f && !_ambience.isPlaying)
            {
                if (_ambience.clip == null) _ambience.clip = Resources.Load<AudioClip>(RainAmbience);
                if (_ambience.clip == null) return;
                _ambience.Play();
            }
            _ambience.volume = AmbienceVolume * GameAudio.SfxVolume * _ambienceLevel;
            if (_ambienceLevel <= 0f && _ambience.isPlaying) _ambience.Stop();
        }
    }

    /// <summary>
    /// 빗줄기 또는 눈송이 여러 개를 사각형으로 한 메시에 그린다. 화면 위쪽 가장자리 전체에서 나와 아래로 떨어진다.
    /// Time.deltaTime이라 전투 일시정지·배속을 따른다(떨어지는 잎과 같다).
    /// </summary>
    public class BattleWeatherParticles : MaskableGraphic
    {
        private const int MaxRain = 600, MaxSnow = 1500;
        private static readonly Color RainColor = new Color(0.75f, 0.82f, 0.95f, 0.45f);
        private static readonly Color SnowColor = new Color(1f, 1f, 1f, 0.9f);

        private struct Drop
        {
            public Vector2 Position, Velocity, Size;
            public float Phase;
        }

        private bool _isRain;
        private Texture2D _texture;
        private Drop[] _drops;
        private int _count;
        private float _spawnCarry;

        /// <summary>마을 기준 초당 개수(WeatherEffects.Rate). 0이면 새로 나오지 않고 내리던 것만 끝까지 떨어진다.</summary>
        public float Rate { get; set; }

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        public void Init(bool rain)
        {
            _isRain = rain;
            _texture = rain ? WeatherEffects.RainTexture() : WeatherEffects.SnowTexture();
            _texture.hideFlags = HideFlags.DontSave;
            _drops = new Drop[rain ? MaxRain : MaxSnow];
            raycastTarget = false;
            color = rain ? RainColor : SnowColor;
        }

        /// <summary>화면(캔버스) 전체 사각형을 이 그래픽 좌표로.</summary>
        private Rect ScreenRect()
        {
            var canvasRect = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
            if (canvasRect == null) return rectTransform.rect;
            var corners = new Vector3[4];
            canvasRect.GetWorldCorners(corners);
            Vector2 min = rectTransform.InverseTransformPoint(corners[0]);
            Vector2 max = rectTransform.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Update()
        {
            if (_drops == null) return;
            float dt = Time.deltaTime;
            var screen = ScreenRect();

            // 마을은 화면 넓이에 맞춰 양을 정한다: 마을 기본 화면(1920×1080)과 같은 넓이라 Rate를 그대로 쓴다.
            _spawnCarry += Rate * dt;
            while (_spawnCarry >= 1f)
            {
                _spawnCarry -= 1f;
                if (_count < _drops.Length) _drops[_count++] = Spawn(screen);
            }

            float time = Time.time;
            for (int i = _count - 1; i >= 0; i--)
            {
                var d = _drops[i];
                d.Position += d.Velocity * dt;
                if (!_isRain) d.Position.x += Mathf.Sin(time * 1.3f + d.Phase) * 0.35f * BattleWeather.PixelsPerUnit * dt; // 좌우로 흔들림
                if (d.Position.y < screen.yMin - d.Size.y)
                {
                    _drops[i] = _drops[--_count]; // 다 떨어진 것은 마지막 것과 바꿔 지운다
                    continue;
                }
                _drops[i] = d;
            }
            if (_count > 0 || Rate > 0f) SetVerticesDirty();
        }

        private Drop Spawn(Rect screen)
        {
            float ppu = BattleWeather.PixelsPerUnit;
            var drop = new Drop
            {
                // 비스듬히 떨어져도 오른쪽 끝이 비지 않게 오른쪽으로 넉넉히(마을과 같다)
                Position = new Vector2(Random.Range(screen.xMin - 2f * ppu, screen.xMax + 2f * ppu), screen.yMax + Random.Range(0f, 0.5f * ppu)),
                Phase = Random.Range(0f, 10f),
            };
            if (_isRain)
            {
                float speed = Random.Range(0.9f, 1.1f) * WeatherEffects.RainSpeed * ppu;
                drop.Velocity = new Vector2(WeatherEffects.RainSlant * ppu, -speed);
                drop.Size = new Vector2(Random.Range(0.025f, 0.035f), Random.Range(0.35f, 0.55f)) * ppu;
            }
            else
            {
                drop.Velocity = new Vector2(Random.Range(-0.3f, 0.3f) * ppu, -Random.Range(0.8f, 1.2f) * WeatherEffects.SnowSpeed * ppu);
                float s = Random.Range(0.05f, 0.11f) * ppu;
                drop.Size = new Vector2(s, s);
            }
            return drop;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_drops == null) return;
            var c = (Color32)color;
            for (int i = 0; i < _count; i++)
            {
                var d = _drops[i];
                // 빗줄기는 떨어지는 방향으로 기울인다(위쪽 끝을 속도 반대쪽으로).
                Vector2 up = _isRain ? -d.Velocity.normalized : Vector2.up;
                Vector2 side = new Vector2(up.y, -up.x);
                Vector2 halfW = side * d.Size.x * 0.5f, halfH = up * d.Size.y * 0.5f;
                int v = vh.currentVertCount;
                vh.AddVert(d.Position - halfW - halfH, c, new Vector2(0f, 0f));
                vh.AddVert(d.Position - halfW + halfH, c, new Vector2(0f, 1f));
                vh.AddVert(d.Position + halfW + halfH, c, new Vector2(1f, 1f));
                vh.AddVert(d.Position + halfW - halfH, c, new Vector2(1f, 0f));
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v + 2, v + 3, v);
            }
        }
    }
}
