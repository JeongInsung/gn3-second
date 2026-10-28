using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 길드 의뢰 기록: 끝낸 의뢰(성공·실패)를 날짜·레벨·보상·대원·MVP·전사자와 함께 최근 MaxEntries건 적는다
    /// (ExpeditionBattle.ConcludeReport). 의뢰 기록 창(QuestHistoryWindow)이 읽는다. 저장은 SaveData.questHistory.
    /// </summary>
    public static class QuestHistory
    {
        public const int MaxEntries = 100;

        public class Entry
        {
            public int Day;
            public string Title;
            public int Level;
            public bool Win;
            public int Reward;
            public bool Joint;
            public bool Challenge;
            public List<string> Members = new List<string>();
            public string Mvp;
            public List<string> Fallen = new List<string>();
            // 대원별 전투 통계(Members 순서). 예전 기록은 비어 있다
            public List<int> Dealt = new List<int>();
            public List<int> Healed = new List<int>();
            public List<int> Taken = new List<int>();

            /// <summary>대원 수만큼 통계가 있는가(통계를 남기기 전의 기록이면 false).</summary>
            public bool HasStats => Members.Count > 0 && Dealt.Count == Members.Count && Healed.Count == Members.Count && Taken.Count == Members.Count;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>오래된 것부터.</summary>
        public static IReadOnlyList<Entry> Entries => _entries;

        public static int Wins => _entries.Count(e => e.Win);
        public static int TotalReward => _entries.Sum(e => e.Reward);

        /// <summary>레벨별 (성공, 시도) — 기록이 있는 레벨만, 낮은 레벨부터.</summary>
        public static List<(int level, int wins, int tries)> ByLevel() =>
            _entries.GroupBy(e => e.Level).OrderBy(g => g.Key).Select(g => (g.Key, g.Count(e => e.Win), g.Count())).ToList();

        /// <param name="stats">대원별 (딜량, 힐량, 받은 피해) — members와 같은 순서.</param>
        public static void Record(Quest quest, bool win, int reward, IEnumerable<string> members, string mvp, IEnumerable<string> fallen,
            IEnumerable<(int dealt, int healed, int taken)> stats = null)
        {
            if (quest == null) return;
            var statList = stats?.ToList() ?? new List<(int dealt, int healed, int taken)>();
            var entry = new Entry
            {
                Day = GameClock.CurrentDay,
                Title = quest.Title,
                Level = quest.Difficulty,
                Win = win,
                Reward = win ? reward : 0,
                Joint = quest.IsJoint,
                Challenge = quest.IsChallenge,
                Members = members?.ToList() ?? new List<string>(),
                Mvp = mvp,
                Fallen = fallen?.ToList() ?? new List<string>(),
                Dealt = statList.Select(s => s.dealt).ToList(),
                Healed = statList.Select(s => s.healed).ToList(),
                Taken = statList.Select(s => s.taken).ToList(),
            };
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            OnRecorded?.Invoke(entry);
        }

        /// <summary>의뢰 하나를 기록했을 때(업적·전체 통계 — 기록은 최근 MaxEntries건만 남아도 누적은 따로 센다).</summary>
        public static event System.Action<Entry> OnRecorded;

        /// <summary>저장 파일에서 불러온 기록으로 바꾼다.</summary>
        public static void Restore(IEnumerable<Entry> entries)
        {
            _entries.Clear();
            if (entries != null) _entries.AddRange(entries);
        }

        // Domain Reload가 꺼져 있어도 Play 진입마다 비운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _entries.Clear();
            OnRecorded = null;
        }
    }
}
