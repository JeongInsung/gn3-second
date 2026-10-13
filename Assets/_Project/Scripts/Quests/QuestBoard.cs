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

        // 계층 가중치 = 1 / (1 + 그 지역·계층 클리어 횟수 × DecayPerClear). 많이 깬 지역·계층일수록 비율이
        // 줄고(0이 되진 않음, 완전히 안 나오진 않게), 아직 안 깨봤거나 적게 깬 계층이 상대적으로 더 자주 나온다.
        private const double DecayPerClear = 0.2;

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
                    var (region, tier) = PickWeightedRegionTier(unlockedRegions);
                    var quest = QuestGenerator.Generate(region, MinEnemyCount, MaxEnemyCount, _random, tierOverride: tier);
                    if (best == null || QuestDifficulty.Grade(quest) < QuestDifficulty.Grade(best)) best = quest;
                    if (QuestDifficulty.Grade(quest) <= MaxGrade) { best = quest; break; }
                }
                result.Add(best);
            }

            Listings = result;
            return Listings;
        }

        /// <summary>지역×계층(0~2) 전부를 후보로 두고, 이미 많이 깬 조합일수록 뽑힐 확률을 낮춘다(QuestClearHistory).</summary>
        private (Region region, int tier) PickWeightedRegionTier(IReadOnlyList<Region> regions)
        {
            var candidates = new List<(Region region, int tier, double weight)>(regions.Count * 3);
            double total = 0;
            foreach (var region in regions)
                for (int tier = 0; tier <= 2; tier++)
                {
                    double w = 1.0 / (1.0 + QuestClearHistory.ClearsOf(region, tier) * DecayPerClear);
                    candidates.Add((region, tier, w));
                    total += w;
                }

            double roll = _random.NextDouble() * total;
            foreach (var c in candidates)
            {
                roll -= c.weight;
                if (roll <= 0) return (c.region, c.tier);
            }

            var last = candidates[candidates.Count - 1];
            return (last.region, last.tier); // 부동소수 오차로 못 고르는 극히 드문 경우의 안전망
        }
    }
}
