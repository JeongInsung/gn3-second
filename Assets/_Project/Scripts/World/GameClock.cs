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

        public static int CurrentDay { get; private set; } = 1;

        /// <summary>하루 중 시각 0~24시.</summary>
        public static float CurrentHour { get; private set; } = StartHour;

        public static event Action OnDayAdvanced;
        public static event Action OnTimeAdvanced;

        public static void AdvanceTime()
        {
            CurrentHour += HoursPerStep;
            if (CurrentHour >= 24f)
            {
                CurrentHour -= 24f;
                AdvanceDay();
            }
            OnTimeAdvanced?.Invoke();
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
        }
    }
}
