using System;
using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.World;

namespace GN3.Quests
{
    public class QuestBoard
    {
        private readonly Random _random;

        public int Size { get; }
        public int MinEnemyCount { get; }
        public int MaxEnemyCount { get; }
        public int MinDifficulty { get; }
        public int MaxDifficulty { get; }
        public IReadOnlyList<Quest> Listings { get; private set; } = new List<Quest>();

        /// <summary>게시판에 나올 수 있는 최고 퀘스트 등급(길드 단계가 정한다).</summary>
        public MercenaryGrade MaxGrade { get; set; } = MercenaryGrade.S;

        private const int MaxRerolls = 30;

        public QuestBoard(int size = 5, int minEnemyCount = 2, int maxEnemyCount = 5, int minDifficulty = 1, int maxDifficulty = 3, int? seed = null)
        {
            Size = size;
            MinEnemyCount = minEnemyCount;
            MaxEnemyCount = maxEnemyCount;
            MinDifficulty = minDifficulty;
            MaxDifficulty = maxDifficulty;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public IReadOnlyList<Quest> Refresh()
        {
            var unlockedRegions = RegionInfo.GetUnlockedRegions();
            var result = new List<Quest>(Size);
            for (int i = 0; i < Size; i++)
            {
                // 최고 등급을 넘는 퀘스트는 다시 뽑는다. 끝내 못 맞추면 그중 가장 낮은 등급을 쓴다.
                Quest best = null;
                for (int attempt = 0; attempt < MaxRerolls; attempt++)
                {
                    var region = unlockedRegions[_random.Next(unlockedRegions.Count)];
                    var quest = QuestGenerator.Generate(region, MinEnemyCount, MaxEnemyCount, MinDifficulty, MaxDifficulty, _random);
                    if (best == null || QuestDifficulty.Grade(quest) < QuestDifficulty.Grade(best)) best = quest;
                    if (QuestDifficulty.Grade(quest) <= MaxGrade) { best = quest; break; }
                }
                result.Add(best);
            }

            Listings = result;
            return Listings;
        }
    }
}
