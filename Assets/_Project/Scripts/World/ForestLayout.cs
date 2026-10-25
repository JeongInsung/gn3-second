using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 벤 숲(나무·덤불·바위)과 오늘 벤 횟수. 키는 Village "숲" 그룹 자식 이름 + "#같은 이름 중 몇 번째"(프리팹 순서 — VillageLayoutApplier).
    /// 씬을 다시 불러도 VillageLayoutApplier.Apply가 이 목록대로 다시 숨긴다. 저장 파일에 같이 들어간다(SaveSystem).
    /// </summary>
    public static class ForestLayout
    {
        public const int FellsPerDay = 6;

        private static readonly HashSet<string> _felled = new HashSet<string>();
        private static int _fellsUsed;
        private static int _fellsDay = -1;

        public static IReadOnlyCollection<string> Felled => _felled;
        public static bool IsFelled(string key) => _felled.Contains(key);

        /// <summary>오늘 더 벨 수 있는 횟수(날이 바뀌면 다시 FellsPerDay).</summary>
        public static int FellsLeft => _fellsDay == GameClock.CurrentDay ? Mathf.Max(0, FellsPerDay - _fellsUsed) : FellsPerDay;

        public static void Add(string key)
        {
            if (!_felled.Add(key)) return;
            if (_fellsDay != GameClock.CurrentDay)
            {
                _fellsDay = GameClock.CurrentDay;
                _fellsUsed = 0;
            }
            _fellsUsed++;
        }

        // ---------- 저장 ----------

        public static List<string> SaveEntries() => new List<string>(_felled);
        public static int SavedUsed => _fellsUsed;
        public static int SavedDay => _fellsDay;

        public static void Restore(IEnumerable<string> felled, int used, int day)
        {
            _felled.Clear();
            if (felled != null) foreach (var key in felled) _felled.Add(key);
            _fellsUsed = used;
            _fellsDay = day;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _felled.Clear();
            _fellsUsed = 0;
            _fellsDay = -1;
        }
    }
}
