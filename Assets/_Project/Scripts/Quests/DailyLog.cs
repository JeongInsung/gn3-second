using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 날짜별로 있었던 일을 모아 둔다(하루 보고서 DayReportPanel이 읽는다).
    /// - 이동 중 습격·휴식·사망·전멸: ExpeditionLog.OnTravelEvent를 그 일차에 기록
    /// - 자정마다 진행 중인 파견의 진행 상황(남은 일수 / 막 도착 / 전투 대기)
    /// - 여관 회복 등 다른 곳에서 Add로 남기는 줄
    /// </summary>
    public static class DailyLog
    {
        public const string QuietDay = "조용한 하루였다.";
        private const int KeepDays = 30;

        private static readonly Dictionary<int, List<string>> _days = new Dictionary<int, List<string>>();
        private static readonly Dictionary<string, int> _lastRemaining = new Dictionary<string, int>(); // 퀘스트 Id → 어제 남은 일수
        // 아직 보고서로 보여 주지 않은 줄(낮에 생긴 일 — 예: 빠른 진군 — 도 다음 보고서에 실리게)
        private static readonly List<(int day, string line)> _pending = new List<(int day, string line)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _days.Clear();
            _lastRemaining.Clear();
            _pending.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            // ExpeditionLog가 먼저 만들어져(생성자에서 OnDayAdvanced 구독) 하루 진행을 처리한 뒤에 우리가 기록하게 한다.
            var log = ExpeditionLog.Instance;
            log.OnTravelEvent += Add;
            log.OnChanged += RememberNewExpeditions;
            GameClock.OnDayAdvanced += RecordProgress;
        }

        /// <summary>오늘(현재 일차) 기록에 한 줄 더한다.</summary>
        public static void Add(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            int day = GameClock.CurrentDay;
            if (!_days.TryGetValue(day, out var lines)) _days[day] = lines = new List<string>();
            lines.Add(line);
            _pending.Add((day, line));
            foreach (int old in _days.Keys.Where(d => d < day - KeepDays).ToList()) _days.Remove(old);
        }

        /// <summary>아직 보고하지 않은 줄 중 가장 이른 일차(없으면 int.MaxValue).</summary>
        public static int FirstPendingDay => _pending.Count > 0 ? _pending.Min(p => p.day) : int.MaxValue;

        /// <summary>보고하지 않은 줄을 일차별로 꺼내고 비운다.</summary>
        public static Dictionary<int, List<string>> TakePending()
        {
            var result = new Dictionary<int, List<string>>();
            foreach (var (day, line) in _pending)
            {
                if (!result.TryGetValue(day, out var list)) result[day] = list = new List<string>();
                list.Add(line);
            }
            _pending.Clear();
            return result;
        }

        public static IReadOnlyList<string> Lines(int day) =>
            _days.TryGetValue(day, out var lines) ? lines : (IReadOnlyList<string>)System.Array.Empty<string>();

        // 새로 떠난 파견은 출발 당시 남은 일수를 기억해 둔다(그래야 첫 자정에 "막 도착"을 구분할 수 있다).
        private static void RememberNewExpeditions()
        {
            foreach (var expedition in ExpeditionLog.Instance.Active)
                if (!_lastRemaining.ContainsKey(expedition.Quest.Id))
                    _lastRemaining[expedition.Quest.Id] = expedition.Quest.RemainingDays;
        }

        /// <summary>자정: ExpeditionLog가 남은 일수를 줄이고 습격을 처리한 뒤의 상태를 적는다.</summary>
        private static void RecordProgress()
        {
            var active = ExpeditionLog.Instance.Active;
            if (active.Count == 0)
            {
                Add("진행 중인 파견 없음");
                return;
            }

            foreach (var expedition in active)
            {
                var quest = expedition.Quest;
                string members = string.Join(", ", expedition.Members.Where(m => m.IsAlive).Select(m => m.Name));
                bool wasTraveling = !_lastRemaining.TryGetValue(quest.Id, out int before) || before > 0;

                if (!expedition.IsReady)
                    Add($"[{quest.Title}] 이동 중 — 남은 {quest.RemainingDays}일 (파견: {members})");
                else if (wasTraveling)
                    Add($"[{quest.Title}] 목적지 도착! 파티 패널에서 전투를 시작하세요 (파견: {members})");
                else
                    Add($"[{quest.Title}] 도착해 전투 대기 중 (파견: {members})");

                _lastRemaining[quest.Id] = quest.RemainingDays;
            }
        }
    }
}
