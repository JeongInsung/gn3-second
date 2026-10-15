using System.Collections.Generic;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 음악·효과음 볼륨과 효과음 재생을 한 군데서 맡는다.
    ///  - MusicVolume / SfxVolume(0~1)은 설정 창에서 바꾸고 PlayerPrefs에 저장한다(게임 저장 파일과 따로).
    ///    처음엔 예전 "전체 음량"(gn3.volume) 값을 둘 다에 이어받는다. 마스터(AudioListener.volume)는 1로 둔다.
    ///  - 음악은 VillageMusic·전투 브금이 MusicVolume을 곱하고, 바뀌면 OnChanged로 다시 맞춘다.
    ///  - PlaySfx(이름): Resources/Sfx/이름을 SfxVolume으로 한 번 재생. 같은 소리가 아주 짧은 사이에 몰려오면
    ///    (여러 유닛이 한꺼번에 때릴 때) 하나만 낸다. 씬을 넘어가도 끊기지 않게 DontDestroyOnLoad 소리통 하나를 쓴다.
    /// </summary>
    public static class GameAudio
    {
        private const string MusicKey = "gn3.musicVolume";
        private const string SfxKey = "gn3.sfxVolume";
        private const string LegacyVolumeKey = "gn3.volume"; // 예전 설정 창의 전체 음량
        private const string SfxFolder = "Sfx/";
        private const float SameSoundGap = 0.05f;           // 같은 소리는 이 간격 안에 한 번만

        /// <summary>음악 또는 효과음 볼륨이 바뀌었다.</summary>
        public static event System.Action OnChanged;

        private static AudioSource _sfxSource;
        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private static readonly HashSet<string> _missing = new HashSet<string>();

        /// <summary>시험·디버그용: 지금까지 낸 효과음 이름별 횟수.</summary>
        public static readonly Dictionary<string, int> PlayCounts = new Dictionary<string, int>();

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            OnChanged = null;
            _sfxSource = null;
            _clips.Clear();
            _lastPlayed.Clear();
            _missing.Clear();
            PlayCounts.Clear();
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, LegacyVolume);
            set => Store(MusicKey, value);
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, LegacyVolume);
            set => Store(SfxKey, value);
        }

        private static float LegacyVolume => PlayerPrefs.GetFloat(LegacyVolumeKey, 1f);

        private static void Store(string key, float value)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        /// <summary>효과음 한 번(scale = 이 소리만의 크기 배율). 소리 파일이 없으면 경고 한 번만 남기고 조용히 넘어간다.</summary>
        public static void PlaySfx(string name, float scale = 1f)
        {
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(name, out var last) && now - last < SameSoundGap) return;
            var clip = Clip(name);
            if (clip == null) return;
            _lastPlayed[name] = now;
            PlayCounts[name] = PlayCounts.TryGetValue(name, out var n) ? n + 1 : 1;
            float volume = SfxVolume * scale;
            if (volume <= 0f) return;
            Source.PlayOneShot(clip, volume);
        }

        /// <summary>파일이 아닌 클립(코드로 만든 소리 등)을 효과음 볼륨으로 한 번. 시험용으로 이름 "clip:이름"으로 횟수를 센다.</summary>
        public static void PlayClip(AudioClip clip, float scale = 1f)
        {
            if (clip == null) return;
            string key = "clip:" + clip.name;
            PlayCounts[key] = PlayCounts.TryGetValue(key, out var n) ? n + 1 : 1;
            float volume = SfxVolume * scale;
            if (volume <= 0f) return;
            Source.PlayOneShot(clip, volume);
        }

        /// <summary>Resources/Sfx에 그 이름의 소리 파일이 있는지(없어도 경고를 남기지 않는다).</summary>
        public static bool HasSfx(string name) => Resources.Load<AudioClip>(SfxFolder + name) != null;

        private static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var cached) && cached != null) return cached;
            var clip = Resources.Load<AudioClip>(SfxFolder + name);
            if (clip == null)
            {
                if (_missing.Add(name)) Debug.LogWarning($"[GameAudio] 효과음을 찾지 못했습니다: Resources/{SfxFolder}{name}");
                return null;
            }
            _clips[name] = clip;
            return clip;
        }

        private static AudioSource Source
        {
            get
            {
                if (_sfxSource != null) return _sfxSource;
                var go = new GameObject("SfxPlayer", typeof(AudioSource));
                Object.DontDestroyOnLoad(go);
                _sfxSource = go.GetComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
                return _sfxSource;
            }
        }
    }
}
