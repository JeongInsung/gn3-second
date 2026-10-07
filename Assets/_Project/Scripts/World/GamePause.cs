using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 게임 정지 요청을 모은다. 하나라도 멈춰 달라고 하면 멈춘다(중요 알림창 등).
    /// TimeAdvanceController가 IsPaused를 보고 Time.timeScale을 0으로 두고 저절로 흐르는 시간·진행을 막는다.
    /// UI·카메라는 unscaled 시간을 써서 멈춘 동안에도 움직인다.
    /// </summary>
    public static class GamePause
    {
        private static readonly HashSet<object> _owners = new HashSet<object>();

        public static bool IsPaused => _owners.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _owners.Clear();

        public static void Push(object owner)
        {
            if (owner != null) _owners.Add(owner);
        }

        public static void Pop(object owner)
        {
            if (owner != null) _owners.Remove(owner);
        }
    }
}
