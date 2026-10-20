using System;
using System.Collections.Generic;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 지금 게시판에 붙은 퀘스트와 다음 새 의뢰 날짜. 새로고침 버튼은 없고 3~4일마다 저절로 바뀐다(QuestBoardUI).
    /// 수락한 퀘스트는 빠지고, 목록은 저장돼 불러와도 같다(불러오기로 다시 뽑는 꼼수 방지).
    /// 창이 한 번도 안 열려도 저장·날짜가 맞도록 정적으로 보관한다.
    /// </summary>
    public static class QuestBoardListings
    {
        public const int MinRefreshDays = 3, MaxRefreshDays = 4;

        /// <summary>null이면 아직 안 뽑았다(새 게임·예전 저장).</summary>
        public static List<Quest> Current { get; private set; }
        public static int NextRefreshDay { get; private set; }

        public static bool IsStale => Current == null || GameClock.CurrentDay >= NextRefreshDay;
        public static int DaysUntilRefresh => Math.Max(0, NextRefreshDay - GameClock.CurrentDay);

        public static event Action OnChanged;

        /// <summary>새 목록을 붙이고 다음 새 의뢰 날짜(3~4일 뒤)를 정한다.</summary>
        public static void Set(List<Quest> quests, System.Random rng)
        {
            Current = quests;
            NextRefreshDay = GameClock.CurrentDay + rng.Next(MinRefreshDays, MaxRefreshDays + 1);
            OnChanged?.Invoke();
        }

        /// <summary>목록은 그대로 두고 내용만 바꿨을 때(은월의 숲 레벨 개방으로 도전 퀘스트 교체 등).</summary>
        public static void NotifyChanged() => OnChanged?.Invoke();

        public static void Remove(Quest quest)
        {
            if (Current != null && Current.Remove(quest)) OnChanged?.Invoke();
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 퀘스트를 게시판 맨 위에 붙인다.
        /// 아직 목록이 없으면 새로 만들고 갱신일을 MinRefreshDays 뒤로(창을 열 때 다시 뽑혀 사라지지 않게).</summary>
        public static void DebugAdd(Quest quest)
        {
            if (Current == null)
            {
                Current = new List<Quest>();
                NextRefreshDay = GameClock.CurrentDay + MinRefreshDays;
            }
            Current.Insert(0, quest);
            OnChanged?.Invoke();
        }

        /// <summary>테스트·디버그용. 갱신일을 오늘로 — 다음에 그릴 때 새로 뽑는다.</summary>
        public static void DebugExpire() => NextRefreshDay = GameClock.CurrentDay;

        public static void Restore(List<Quest> quests, int nextRefreshDay)
        {
            Current = quests;
            NextRefreshDay = nextRefreshDay;
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Current = null;
            NextRefreshDay = 0;
            OnChanged = null;
        }
    }
}
