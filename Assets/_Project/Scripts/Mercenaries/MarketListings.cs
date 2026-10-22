using System;
using System.Collections.Generic;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 지금 용병시장에 나온 용병과 다음 새 용병 날짜. 새로고침 버튼은 없고 7일마다 저절로 바뀐다(MercenaryMarketUI).
    /// 고용한 용병은 빠지고, 목록은 저장돼 불러와도 같은 사람이 나온다(QuestBoardListings와 같은 모양).
    /// 시장은 근처 길드와 함께 쓴다: 그들이 고용해 가면 빠지고, 그들이 내보낸 용병은 이적 매물(Origin)로 끼어든다(RivalGuilds).
    /// </summary>
    public static class MarketListings
    {
        public const int RefreshDays = 7;

        /// <summary>null이면 아직 안 뽑았다(새 게임·예전 저장).</summary>
        public static List<Mercenary> Current { get; private set; }
        public static int NextRefreshDay { get; private set; }

        /// <summary>이적 매물: 용병 id → 전 소속 근처 길드 id.</summary>
        private static readonly Dictionary<string, string> _origin = new Dictionary<string, string>();

        public static bool IsStale => Current == null || GameClock.CurrentDay >= NextRefreshDay;
        public static int DaysUntilRefresh => Math.Max(0, NextRefreshDay - GameClock.CurrentDay);

        public static event Action OnChanged;

        public static void Set(List<Mercenary> mercs)
        {
            Current = mercs;
            NextRefreshDay = GameClock.CurrentDay + RefreshDays;
            _origin.Clear(); // 새 목록 — 지난 이적 매물은 다른 데로 갔다
            OnChanged?.Invoke();
        }

        public static void Remove(Mercenary merc)
        {
            if (Current == null || merc == null) return;
            _origin.Remove(merc.Id);
            if (Current.Remove(merc)) OnChanged?.Invoke();
        }

        /// <summary>근처 길드에서 나온 용병을 목록 맨 위에 끼운다(다음 새 용병 날까지 있다).</summary>
        public static void AddTransfer(Mercenary merc, string guildId)
        {
            if (Current == null || merc == null) return;
            Current.Insert(0, merc);
            _origin[merc.Id] = guildId;
            OnChanged?.Invoke();
        }

        /// <summary>이적 매물이면 전 소속 길드 id, 아니면 null.</summary>
        public static string OriginOf(Mercenary merc) => merc != null && _origin.TryGetValue(merc.Id, out var id) ? id : null;

        /// <summary>저장용 "용병id|길드id".</summary>
        public static List<string> SaveOrigins()
        {
            var list = new List<string>();
            foreach (var pair in _origin) list.Add(pair.Key + "|" + pair.Value);
            return list;
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 갱신일을 오늘로 — 다음에 그릴 때 새 용병을 뽑는다.</summary>
        public static void DebugExpire() => NextRefreshDay = GameClock.CurrentDay;

        public static void Restore(List<Mercenary> mercs, int nextRefreshDay, IEnumerable<string> origins = null)
        {
            Current = mercs;
            NextRefreshDay = nextRefreshDay;
            _origin.Clear();
            if (origins != null)
                foreach (var entry in origins)
                {
                    int bar = entry.IndexOf('|');
                    if (bar > 0) _origin[entry.Substring(0, bar)] = entry.Substring(bar + 1);
                }
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Current = null;
            NextRefreshDay = 0;
            _origin.Clear();
            OnChanged = null;
        }
    }
}
