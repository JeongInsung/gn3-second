using System;
using UnityEngine;

namespace GN3.World
{
    /// <summary>게임 내 하루 단위 진행을 관리하는 전역 시계. UI의 "하루 지나기" 버튼으로 진행시킨다.</summary>
    public static class GameClock
    {
        public static int CurrentDay { get; private set; } = 1;

        public static event Action OnDayAdvanced;

        public static void AdvanceDay()
        {
            CurrentDay++;
            OnDayAdvanced?.Invoke();
        }

        // Enter Play Mode Options에서 Domain Reload가 꺼져 있어도 Play 진입마다 호출되는 지점.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            CurrentDay = 1;
            OnDayAdvanced = null;
        }
    }
}
