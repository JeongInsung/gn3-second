using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병 한 명의 경력: 전투 수·승리·처치·부상·질병, 평균·최고 평점(BattleRating), 레벨별 의뢰 성공 수, 최근 전투 몇 판.
    /// 파견 전투가 끝나면 ExpeditionBattle.ConcludeReport가, 부상·질병은 Ailments.Inflict가 쌓는다. 저장 파일에 함께 남는다.
    /// </summary>
    public class MercenaryRecord
    {
        public const int MaxLevel = 5;
        public const int RecentCount = 10;

        public class Entry
        {
            public int Day;
            public string Quest;
            public int Level;
            public bool Win;
            public int Score;
            public int Damage;
            public int Kills;
            public int Healing;
            public bool Fallen;
        }

        public int Battles { get; private set; }
        public int Wins { get; private set; }
        public int Kills { get; private set; }
        public int Injuries { get; private set; }
        public int Illnesses { get; private set; }
        public int ScoreSum { get; private set; }
        public int BestScore { get; private set; }

        private readonly int[] _clears = new int[MaxLevel + 1]; // [레벨] 이겨서 살아 돌아온 의뢰 수(0은 안 씀)
        private readonly List<Entry> _recent = new List<Entry>(); // 최근이 뒤

        public IReadOnlyList<Entry> Recent => _recent;
        public int ClearsAt(int level) => level >= 1 && level <= MaxLevel ? _clears[level] : 0;
        public int TotalClears => _clears.Sum();
        public float AverageScore => Battles > 0 ? (float)ScoreSum / Battles : 0f;
        public string AverageGrade => Battles > 0 ? BattleRating.Grade(AverageScore) : "-";

        /// <summary>파견 전투 한 판(이겼고 살아 돌아오면 그 레벨 의뢰 성공 +1).</summary>
        public void AddBattle(int day, string quest, int level, bool win, int score, int damage, int kills, int healing, bool fallen)
        {
            Battles++;
            if (win) Wins++;
            Kills += kills;
            ScoreSum += score;
            BestScore = Battles == 1 ? score : Math.Max(BestScore, score);
            if (win && !fallen && level >= 1 && level <= MaxLevel) _clears[level]++;
            _recent.Add(new Entry { Day = day, Quest = quest, Level = level, Win = win, Score = score, Damage = damage, Kills = kills, Healing = healing, Fallen = fallen });
            while (_recent.Count > RecentCount) _recent.RemoveAt(0);
        }

        public void AddInjury() => Injuries++;
        public void AddIllness() => Illnesses++;

        /// <summary>저장 파일에서 불러올 때.</summary>
        public void Restore(int battles, int wins, int kills, int injuries, int illnesses, int scoreSum, int bestScore,
            IReadOnlyList<int> clears, IEnumerable<Entry> recent)
        {
            Battles = Math.Max(0, battles);
            Wins = Math.Max(0, wins);
            Kills = Math.Max(0, kills);
            Injuries = Math.Max(0, injuries);
            Illnesses = Math.Max(0, illnesses);
            ScoreSum = Math.Max(0, scoreSum);
            BestScore = Math.Max(0, bestScore);
            Array.Clear(_clears, 0, _clears.Length);
            if (clears != null)
                for (int i = 0; i < clears.Count && i < _clears.Length; i++) _clears[i] = Math.Max(0, clears[i]);
            _recent.Clear();
            if (recent != null) _recent.AddRange(recent.Where(e => e != null).TakeLast(RecentCount));
        }

        public List<int> SaveClears() => _clears.ToList();
    }
}
