using System;
using UnityEngine;

namespace GN3.World
{
    /// <summary>게임 내 시간 진행을 관리하는 전역 시계. UI의 "진행" 버튼으로 3시간씩 진행시키고,
    /// 자정을 넘으면 하루가 지나 OnDayAdvanced가 발생한다(파견 남은 일수 등은 하루 단위 그대로).</summary>
    public static class GameClock
    {
        public const float StartHour = 6f;
        public const float HoursPerStep = 3f;
        public const float NightStartHour = 22f; // 밤 10시 ~ 오전 5시는 출전(파견 시작) 불가
        public const float NightEndHour = 5f;

        public static bool IsNight => CurrentHour >= NightStartHour || CurrentHour < NightEndHour;

        public static int CurrentDay { get; private set; } = 1;

        /// <summary>하루 중 시각 0~24시.</summary>
        public static float CurrentHour { get; private set; } = StartHour;

        public static event Action OnDayAdvanced;
        public static event Action OnTimeAdvanced;
        /// <summary>게임 시간이 흐를 때마다(저절로 흐르기·진행·건너뛰기 모두) 흐른 시간(시)으로 불린다. 훈련처럼 시간에 비례하는 것들이 구독한다.</summary>
        public static event Action<float> OnHoursPassed;

        /// <summary>
        /// '진행'으로 건너뛰는 동안 SkipStepHours마다(그 시간이 흐르기 직전) 흐를 시간과 함께 불린다.
        /// 실시간이면 마을에서 걸어 다니며 하던 일(쉼터·훈련 가기)을 건너뛸 때는 여기서 굴린다(VillageSkipVisits).
        /// </summary>
        public static event Action<float> OnSkipStep;
        public const float SkipStepHours = 1f;

        public static void AdvanceTime() => AdvanceTime(HoursPerStep);

        /// <summary>남은 시간 표시: "1일 6시간" / "5시간" / "1일". 시간은 올림(0.2시간 남아도 1시간).</summary>
        public static string FormatDuration(float hours)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(hours - 0.001f));
            int days = total / 24, rest = total % 24;
            if (days == 0) return $"{rest}시간";
            return rest == 0 ? $"{days}일" : $"{days}일 {rest}시간";
        }

        /// <summary>
        /// 진행 버튼: hours만큼 진행(1~24시간, 슬라이더로 정함). 자정을 넘으면 하루가 지난다.
        /// 한 번에 넘기지 않고 SkipStepHours씩 나눠 흘려서, 그 사이 쉼터·훈련에 들어가고 하루가 바뀌는 순서가 실시간과 같게 한다.
        /// </summary>
        public static void AdvanceTime(float hours)
        {
            if (hours <= 0f) return;
            for (float left = hours; left > 0f; left -= SkipStepHours)
            {
                float step = Mathf.Min(SkipStepHours, left);
                OnSkipStep?.Invoke(step);
                AdvanceHours(step);
            }
            OnTimeAdvanced?.Invoke();
        }

        /// <summary>
        /// 시간을 조금씩 흘린다(가만히 둬도 흐르는 시간, TimeAdvanceController가 매 프레임 부른다). 자정을 넘으면 하루가 지난다.
        /// OnTimeAdvanced는 진행 버튼(AdvanceTime) 때만 부르므로 여기서는 부르지 않는다.
        /// </summary>
        public static void AdvanceHours(float hours)
        {
            if (hours <= 0f) return;
            OnHoursPassed?.Invoke(hours);
            CurrentHour += hours;
            while (CurrentHour >= 24f)
            {
                CurrentHour -= 24f;
                AdvanceDay();
            }
        }

        /// <summary>저장 파일에서 불러올 때. 일차·시각을 맞추고 OnTimeAdvanced로 화면(낮/밤·시각 표시)만 갱신한다(하루 경과 처리는 없음).</summary>
        public static void Restore(int day, float hour)
        {
            CurrentDay = System.Math.Max(1, day);
            CurrentHour = Mathf.Repeat(hour, 24f);
            OnTimeAdvanced?.Invoke();
        }

        private static void AdvanceDay()
        {
            CurrentDay++;
            OnDayAdvanced?.Invoke();
        }

        // Enter Play Mode Options에서 Domain Reload가 꺼져 있어도 Play 진입마다 호출되는 지점.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            CurrentDay = 1;
            CurrentHour = StartHour;
            OnDayAdvanced = null;
            OnTimeAdvanced = null;
            OnHoursPassed = null;
            OnSkipStep = null;
        }
    }
}
