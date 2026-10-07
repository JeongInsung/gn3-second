using UnityEngine;

namespace GN3.World
{
    /// <summary>순서 = F10 순환 순서. 맑은 비(여우비)·맑은 눈은 해가 난 채 가볍게 내린다.</summary>
    public enum WeatherKind { Clear, Cloudy, SunShower, Rain, HeavyRain, SunnySnow, Snow, HeavySnow }

    /// <summary>
    /// 서울 평년값(1991–2020 근사)으로 날마다 날씨·기온을 정한다. 일차로 씨앗을 정해 뽑으므로 저장하지 않아도
    /// 같은 날은 늘 같은 날씨다. 평균기온이 1℃ 이하인 날 내리는 것은 눈, 7·8월은 장마·집중호우로 폭우가 잦다.
    /// 시각별 기온은 오후 3시 최고·새벽 3시 무렵 최저인 코사인 곡선.
    /// 조명 계수(햇빛·주변광·채도·눈 하양)는 DayNightCycle이 읽고, Tick으로 몇 초에 걸쳐 새 날씨로 넘어간다.
    /// </summary>
    public static class Weather
    {
        // 월별(1~12월) 서울 평년값
        private static readonly float[] MeanTemp = { -1.9f, 0.7f, 6.1f, 12.6f, 18.2f, 22.7f, 25.3f, 26.1f, 21.6f, 14.8f, 7.2f, 0.4f };
        private static readonly float[] DailyRange = { 9f, 9f, 10f, 10f, 10f, 7f, 7f, 7f, 10f, 10f, 10f, 9f };
        private static readonly float[] PrecipChance = { 0.20f, 0.20f, 0.23f, 0.27f, 0.30f, 0.33f, 0.53f, 0.47f, 0.30f, 0.20f, 0.27f, 0.27f };
        private static readonly float[] HeavyChance = { 0.05f, 0.05f, 0.05f, 0.05f, 0.05f, 0.15f, 0.35f, 0.35f, 0.15f, 0.05f, 0.05f, 0.05f };
        private static readonly float[] CloudyChance = { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.45f, 0.45f, 0.45f, 0.3f, 0.3f, 0.3f, 0.3f };

        private const float SnowBelow = 1f;      // 이 평균기온 이하에서 내리면 눈
        private const float SunShowerChance = 0.1f;  // 비 오는 날 중 맑은 비
        private const float HeavySnowChance = 0.15f; // 눈 오는 날 중 폭설
        private const float SunnySnowChance = 0.15f; // 눈 오는 날 중 맑은 눈
        private const float TemperatureNoise = 3f;
        private const float BlendSpeed = 0.6f;    // 조명 계수가 새 날씨로 넘어가는 빠르기(초당, 지수)

        public readonly struct Day
        {
            public readonly WeatherKind Kind;
            public readonly float MeanTemp, Range;
            public float Min => MeanTemp - Range * 0.5f;
            public float Max => MeanTemp + Range * 0.5f;

            public Day(WeatherKind kind, float mean, float range)
            {
                Kind = kind;
                MeanTemp = mean;
                Range = range;
            }
        }

        /// <summary>시험용: 값이 있으면 오늘 날씨를 이것으로 덮는다(기온은 그대로).</summary>
        public static WeatherKind? DebugOverride;

        /// <summary>시험용: 값이 있으면 지금 기온을 이것으로 고정한다(햇빛 세기·색도 따라 바뀐다).</summary>
        public static float? DebugTemperature;

        private static int _cachedDay = -1;
        private static Day _cached;

        public static float SunFactor { get; private set; } = 1f;
        public static float AmbientFactor { get; private set; } = 1f;
        public static float SaturationOffset { get; private set; }
        public static float SnowWhiteness { get; private set; }
        /// <summary>하늘이 덮인 정도 0~1. 회청색 주변광·화면 회색 막·구름 그림자 사라짐에 쓴다.</summary>
        public static float Overcast { get; private set; }
        public static float ContrastOffset { get; private set; }
        /// <summary>기온에 따른 햇빛 색 −1(차가운 푸른빛) ~ +1(따뜻한 노란빛).</summary>
        public static float SunWarmth { get; private set; }
        private static bool _blendStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            DebugOverride = null;
            DebugTemperature = null;
            _cachedDay = -1;
            _blendStarted = false;
            SunFactor = AmbientFactor = 1f;
            SaturationOffset = SnowWhiteness = Overcast = ContrastOffset = SunWarmth = 0f;
        }

        public static Day Today
        {
            get
            {
                if (_cachedDay != GameClock.CurrentDay)
                {
                    _cachedDay = GameClock.CurrentDay;
                    _cached = Generate(_cachedDay);
                }
                return DebugOverride.HasValue ? new Day(DebugOverride.Value, _cached.MeanTemp, _cached.Range) : _cached;
            }
        }

        /// <summary>지금 시각의 기온(℃). 시험용 고정값이 있으면 그 값.</summary>
        public static float CurrentTemperature => CurrentTemperatureAt(GameClock.CurrentHour);

        /// <summary>오늘 그 시각의 기온. 시험용 고정값(DebugTemperature)이 있으면 그 값.</summary>
        public static float CurrentTemperatureAt(float hour) => DebugTemperature ?? TemperatureAt(Today, hour);

        public static float TemperatureAt(Day day, float hour) =>
            day.MeanTemp + day.Range * 0.5f * Mathf.Cos(2f * Mathf.PI * (hour - 15f) / 24f);

        public static string KindName(WeatherKind kind) => kind switch
        {
            WeatherKind.Clear => "맑음",
            WeatherKind.Cloudy => "흐림",
            WeatherKind.SunShower => "맑은 비",
            WeatherKind.Rain => "비",
            WeatherKind.HeavyRain => "폭우",
            WeatherKind.SunnySnow => "맑은 눈",
            WeatherKind.Snow => "눈",
            _ => "폭설",
        };

        public static bool IsRain(WeatherKind kind) => kind == WeatherKind.SunShower || kind == WeatherKind.Rain || kind == WeatherKind.HeavyRain;
        public static bool IsSnow(WeatherKind kind) => kind == WeatherKind.SunnySnow || kind == WeatherKind.Snow || kind == WeatherKind.HeavySnow;

        /// <summary>그날의 날씨. 같은 일차는 늘 같은 결과(저장 불필요).</summary>
        public static Day Generate(int dayNumber)
        {
            var rng = new System.Random(dayNumber * 7919 + 650);
            var date = GameCalendar.FromDay(dayNumber);
            int m = date.Month - 1;
            int next = (m + 1) % 12;
            // 달 가운데(15일)가 평년값, 그 사이는 이웃 달과 보간
            float t = (date.Day - 15.5f) / GameCalendar.DaysPerMonth;
            int other = t >= 0f ? next : (m + 11) % 12;
            float k = Mathf.Abs(t);
            float mean = Mathf.Lerp(MeanTemp[m], MeanTemp[other], k);
            float range = Mathf.Lerp(DailyRange[m], DailyRange[other], k);
            mean += ((float)rng.NextDouble() * 2f - 1f) * TemperatureNoise;
            mean += PrecipChance[m] * 2f; // 비 오는 날 −2℃를 메워 한 달 평균이 평년값에 맞게

            WeatherKind kind;
            if (rng.NextDouble() < PrecipChance[m])
            {
                double roll = rng.NextDouble();
                if (mean <= SnowBelow)
                {
                    kind = roll < HeavySnowChance ? WeatherKind.HeavySnow
                        : roll < HeavySnowChance + SunnySnowChance ? WeatherKind.SunnySnow
                        : WeatherKind.Snow;
                    mean -= 1f;
                }
                else
                {
                    kind = roll < SunShowerChance ? WeatherKind.SunShower
                        : rng.NextDouble() < HeavyChance[m] ? WeatherKind.HeavyRain
                        : WeatherKind.Rain;
                    mean -= 2f;
                }
                range *= 0.6f; // 비·눈 오는 날은 일교차가 작다
            }
            else
            {
                kind = rng.NextDouble() < CloudyChance[m] ? WeatherKind.Cloudy : WeatherKind.Clear;
                if (kind == WeatherKind.Cloudy) range *= 0.8f;
            }
            return new Day(kind, mean, range);
        }

        /// <summary>조명 계수를 오늘 날씨 목표값으로 서서히 옮긴다. WeatherEffects가 매 프레임 부른다.</summary>
        public static void Tick(float deltaTime)
        {
            var today = Today;
            var t = Targets(today.Kind);
            // 기온 → 햇빛: 더우면 세고 노랗게, 추우면 약하고 푸르스름하게(모든 날씨 공통)
            float temperature = CurrentTemperature;
            float sunTarget = t.Sun * (1f + Mathf.Clamp((temperature - 15f) / 50f, -0.25f, 0.25f));
            float warmthTarget = Mathf.Clamp((temperature - 15f) / 20f, -1f, 1f);
            float k = _blendStarted ? 1f - Mathf.Exp(-BlendSpeed * deltaTime) : 1f; // 처음엔 바로 맞춘다
            _blendStarted = true;
            SunFactor = Mathf.Lerp(SunFactor, sunTarget, k);
            SunWarmth = Mathf.Lerp(SunWarmth, warmthTarget, k);
            AmbientFactor = Mathf.Lerp(AmbientFactor, t.Ambient, k);
            SaturationOffset = Mathf.Lerp(SaturationOffset, t.Saturation, k);
            ContrastOffset = Mathf.Lerp(ContrastOffset, t.Contrast, k);
            Overcast = Mathf.Lerp(Overcast, t.Overcast, k);
            SnowWhiteness = Mathf.Lerp(SnowWhiteness, t.Snow, k);
        }

        private struct Look
        {
            public float Sun, Ambient, Saturation, Contrast, Overcast, Snow;
        }

        /// <summary>날씨별 목표 조명. 흐림부터는 햇빛이 크게 줄어 그림자가 거의 사라지고, 색이 빠지고, 회색으로 가라앉는다.</summary>
        private static Look Targets(WeatherKind kind) => kind switch
        {
            // 대비는 조금만 낮춘다(많이 낮추면 화면이 물 빠진 듯 뿌옇게 뜬다)
            WeatherKind.Cloudy => new Look { Sun = 0.42f, Ambient = 0.83f, Saturation = -28f, Contrast = -4f, Overcast = 0.42f },
            WeatherKind.SunShower => new Look { Sun = 0.85f, Ambient = 0.95f, Saturation = -5f, Overcast = 0.1f },
            WeatherKind.Rain => new Look { Sun = 0.2f, Ambient = 0.65f, Saturation = -45f, Contrast = -6f, Overcast = 0.8f },
            WeatherKind.HeavyRain => new Look { Sun = 0.1f, Ambient = 0.55f, Saturation = -55f, Contrast = -6f, Overcast = 1f },
            WeatherKind.SunnySnow => new Look { Sun = 0.8f, Ambient = 1f, Saturation = -8f, Overcast = 0.1f, Snow = 0.3f },
            WeatherKind.Snow => new Look { Sun = 0.35f, Ambient = 1f, Saturation = -35f, Contrast = -4f, Overcast = 0.5f, Snow = 0.6f },
            WeatherKind.HeavySnow => new Look { Sun = 0.15f, Ambient = 0.85f, Saturation = -45f, Contrast = -6f, Overcast = 0.9f, Snow = 0.8f },
            _ => new Look { Sun = 1f, Ambient = 1f },
        };
    }
}
