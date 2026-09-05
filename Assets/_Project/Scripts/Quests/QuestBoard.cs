using System;
using System.Collections.Generic;

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
            var result = new List<Quest>(Size);
            for (int i = 0; i < Size; i++)
                result.Add(QuestGenerator.Generate(MinEnemyCount, MaxEnemyCount, MinDifficulty, MaxDifficulty, _random));

            Listings = result;
            return Listings;
        }
    }
}
