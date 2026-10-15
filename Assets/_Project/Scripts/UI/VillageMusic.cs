using System.Collections.Generic;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 마을(MainScene) 배경음. MainMenuBootstrapper가 MainScene이 로드될 때마다 만든다(전투씬으로 가면 씬과 함께 사라진다).
    ///  - 맑은 날: Resources/Music/마을의 "마을 브금 1~4"를 섞은 순서로 돌아가며 튼다. 한 바퀴 돌면 다시 섞고, 같은 곡이 연달아 나오지 않는다.
    ///  - 비 오는 날(여우비·비·폭우): "비 올때"를 반복하고, 빗소리(Resources/Sfx/빗소리, 효과음 볼륨)를 위에 깐다.
    ///    1초마다 오늘 날씨를 보고 바뀌면 그 자리에서 넘어간다.
    /// 곡 사이·날씨 전환은 AudioSource 두 개로 크로스페이드한다. 실제 시간이라 게임이 멈춰 있어도(타이틀·알림창) 음악은 흐른다.
    /// 볼륨은 설정 창의 음악 볼륨(GameAudio.MusicVolume)을 매 프레임 따른다.
    /// </summary>
    public class VillageMusic : MonoBehaviour
    {
        private const string Folder = "Music/마을/";
        private static readonly string[] DayTracks = { "마을 브금 1", "마을 브금 2", "마을 브금 3", "마을 브금 4" };
        private const string RainTrack = "비 올때";
        private const float Volume = 0.6f;          // 전투 브금과 같은 크기
        private const float FadeSeconds = 2f;
        private const float WeatherCheckSeconds = 1f;
        private const string RainAmbience = "Sfx/빗소리";  // 비 브금 위에 깔리는 빗소리(효과음 볼륨을 따른다)
        private const float AmbienceVolume = 0.5f;

        private AudioSource _ambience;
        private float _ambienceLevel;               // 0~1, 비가 오면 FadeSeconds에 걸쳐 1로·그치면 0으로

        private readonly AudioSource[] _sources = new AudioSource[2];
        private int _active;                        // 지금 들리는(또는 커지는 중인) 쪽
        private float _fadeTime = -1f;              // 0 이상이면 크로스페이드 중
        private float _fadeFromVolume;              // 나가는 곡의 시작 볼륨(최대 볼륨 대비 비율)
        private readonly List<string> _queue = new List<string>();
        private string _current;
        private bool _rainMode;
        private float _weatherTimer;
        private readonly System.Random _rng = new System.Random();
        private readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>시험·디버그용: 지금 곡 이름.</summary>
        public string CurrentTrack => _current;

        public static VillageMusic Create() => new GameObject(nameof(VillageMusic), typeof(VillageMusic)).GetComponent<VillageMusic>();

        private void Awake()
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.volume = 0f;
                _sources[i] = source;
            }
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.volume = 0f;
        }

        /// <summary>비가 오면 빗소리를 서서히 키우고, 그치면 서서히 줄여 멈춘다. 효과음 볼륨 설정을 바로 따른다.</summary>
        private void UpdateAmbience()
        {
            if (_ambience == null) return;
            float target = _rainMode ? 1f : 0f;
            _ambienceLevel = Mathf.MoveTowards(_ambienceLevel, target, Time.unscaledDeltaTime / FadeSeconds);
            if (_ambienceLevel > 0f && !_ambience.isPlaying)
            {
                if (_ambience.clip == null) _ambience.clip = Resources.Load<AudioClip>(RainAmbience);
                if (_ambience.clip == null)
                {
                    if (_warned.Add(RainAmbience)) Debug.LogWarning($"[VillageMusic] 빗소리를 찾지 못했습니다: Resources/{RainAmbience}");
                    return;
                }
                _ambience.Play();
            }
            _ambience.volume = AmbienceVolume * GameAudio.SfxVolume * _ambienceLevel;
            if (_ambienceLevel <= 0f && _ambience.isPlaying) _ambience.Stop();
        }

        private void Start()
        {
            _rainMode = IsRaining();
            PlayNext();
        }

        private void Update()
        {
            // 에디터에서 Play 중에 스크립트가 다시 컴파일되면 AudioSource 목록이 비어 돌아온다(Awake는 다시 안 불림) — 그때는 조용히 쉰다.
            if (_sources[0] == null || _sources[1] == null) return;
            UpdateFade();
            UpdateAmbience();

            _weatherTimer -= Time.unscaledDeltaTime;
            if (_weatherTimer <= 0f)
            {
                _weatherTimer = WeatherCheckSeconds;
                bool rain = IsRaining();
                if (rain != _rainMode)
                {
                    _rainMode = rain;
                    _queue.Clear(); // 비가 그치면 새로 섞은 순서로
                    PlayNext();
                    return;
                }
            }

            // 맑은 날 곡이 끝나 가면 다음 곡으로 넘어간다(끝나기 FadeSeconds 전부터 겹쳐 흐른다).
            var active = _sources[_active];
            if (_rainMode || _fadeTime >= 0f || active.clip == null) return;
            if (!active.isPlaying || active.time >= active.clip.length - FadeSeconds) PlayNext();
        }

        private static bool IsRaining() => Weather.IsRain(Weather.Today.Kind);

        /// <summary>날씨에 맞는 다음 곡을 크로스페이드로 튼다.</summary>
        private void PlayNext()
        {
            string name = _rainMode ? RainTrack : NextDayTrack();
            if (name == null) return;
            if (_rainMode && name == _current && _sources[_active].isPlaying) return; // 이미 비 브금 중
            var clip = Load(name);
            if (clip == null)
            {
                if (_rainMode) // 비 브금이 없으면 맑은 날 곡으로
                {
                    name = NextDayTrack();
                    clip = name != null ? Load(name) : null;
                }
                if (clip == null) return;
            }

            var from = _sources[_active];
            _active = 1 - _active;
            var to = _sources[_active];
            to.clip = clip;
            to.loop = _rainMode;
            to.volume = 0f;
            to.time = 0f;
            to.Play();
            // 나가는 곡이 지금 최대 볼륨의 몇 할인지(페이드 도중 다시 넘어가도 튀지 않게, 음악 볼륨이 바뀌어도 따라가게)
            _fadeFromVolume = from.isPlaying ? Mathf.Clamp01(from.volume / Mathf.Max(0.0001f, Target)) : 0f;
            _fadeTime = 0f;
            _current = name;
        }

        /// <summary>섞은 목록에서 다음 곡. 목록이 비면 다시 섞고, 첫 곡이 방금 곡과 같으면 뒤로 보낸다.</summary>
        private string NextDayTrack()
        {
            if (_queue.Count == 0)
            {
                _queue.AddRange(DayTracks);
                for (int i = _queue.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
                }
                if (_queue.Count > 1 && _queue[0] == _current)
                    (_queue[0], _queue[_queue.Count - 1]) = (_queue[_queue.Count - 1], _queue[0]);
            }
            if (_queue.Count == 0) return null;
            string next = _queue[0];
            _queue.RemoveAt(0);
            return next;
        }

        /// <summary>지금 곡의 최대 볼륨: 기본 크기 × 설정 창의 음악 볼륨.</summary>
        private static float Target => Volume * GameAudio.MusicVolume;

        /// <summary>크로스페이드를 진행하고, 페이드 중이 아니어도 매 프레임 음악 볼륨 설정을 바로 따른다.</summary>
        private void UpdateFade()
        {
            if (_fadeTime < 0f)
            {
                _sources[_active].volume = Target;
                return;
            }
            _fadeTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_fadeTime / FadeSeconds);
            var to = _sources[_active];
            var from = _sources[1 - _active];
            to.volume = Target * k;
            from.volume = Target * _fadeFromVolume * (1f - k);
            if (k >= 1f)
            {
                from.Stop();
                from.clip = null;
                _fadeTime = -1f;
            }
        }

        private AudioClip Load(string name)
        {
            var clip = Resources.Load<AudioClip>(Folder + name);
            if (clip == null && _warned.Add(name))
                Debug.LogWarning($"[VillageMusic] 마을 배경음을 찾지 못했습니다: Resources/{Folder}{name}");
            return clip;
        }
    }
}
