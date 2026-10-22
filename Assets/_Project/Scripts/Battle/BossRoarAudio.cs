using UnityEngine;

namespace GN3.Battle
{
    /// <summary>
    /// 거목 엔트 포효 소리: Resources/Sfx/ent_roars(voicebosch monster roars, 36.7초에 포효 10개)에서 첫째·둘째 포효를 잘라 쓴다.
    /// 구간·싱크 시각은 파형(10ms RMS 포락선)을 재서 정했다 — 시각은 모두 실제 초(소리는 전투 배속을 따르지 않는다).
    ///  - Intro(첫째, 0.46~2.05초): 전투 시작 등장 포효(ent_roar). 크게 터짐(50%) 0.19초 · 큰 소리(40% 이상) 0.93초 이어짐.
    ///  - Stun(둘째, 3.41~5.84초): 기절 포효(ent_roar_stun). 크게 터짐 0.31초 · 큰 소리 1.23초 이어짐.
    /// 클립을 한 번 읽어(Decompress On Load라 GetData 가능) 구간을 잘라 앞뒤 10ms를 페이드해 딸깍 소리 없이 캐시한다.
    /// </summary>
    public static class BossRoarAudio
    {
        private const string Source = "Sfx/ent_roars";
        private const float Fade = 0.01f;

        public struct Roar
        {
            public float From, To;   // 원본에서 자를 구간(초)
            public float Onset;      // 잘린 소리 시작부터 크게 터지는 순간까지(초)
            public float LoudLength; // 크게 터진 뒤 큰 소리가 이어지는 길이(초)
        }

        public static readonly Roar Intro = new Roar { From = 0.46f, To = 2.05f, Onset = 0.19f, LoudLength = 0.93f };
        public static readonly Roar Stun = new Roar { From = 3.41f, To = 5.84f, Onset = 0.31f, LoudLength = 1.23f };

        private static AudioClip _intro, _stun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _intro = _stun = null;

        public static AudioClip IntroClip => _intro != null ? _intro : _intro = Cut(Intro, "ent_roar_intro");
        public static AudioClip StunClip => _stun != null ? _stun : _stun = Cut(Stun, "ent_roar_stun");

        /// <summary>미리 잘라 둔다(전투 중 처음 틀 때 멈칫하지 않게).</summary>
        public static void Prepare()
        {
            _ = IntroClip;
            _ = StunClip;
        }

        private static AudioClip Cut(Roar roar, string name)
        {
            var source = Resources.Load<AudioClip>(Source);
            if (source == null) return null;
            int ch = source.channels, hz = source.frequency;
            int from = Mathf.Clamp(Mathf.RoundToInt(roar.From * hz), 0, source.samples);
            int to = Mathf.Clamp(Mathf.RoundToInt(roar.To * hz), from + 1, source.samples);
            int length = to - from;
            var data = new float[length * ch];
            if (!source.GetData(data, from)) return null;
            int fade = Mathf.Min(length / 2, Mathf.RoundToInt(Fade * hz));
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                for (int c = 0; c < ch; c++)
                {
                    data[i * ch + c] *= k;
                    data[(length - 1 - i) * ch + c] *= k;
                }
            }
            var clip = AudioClip.Create(name, length, ch, hz, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
