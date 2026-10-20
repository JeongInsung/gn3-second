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

        // 레벨 가중치 = 1 / (1 + 그 지역·레벨 클리어 횟수 × DecayPerClear). 많이 깬 레벨일수록 비율이
        // 줄고(0이 되진 않음, 완전히 안 나오진 않게), 아직 안 깨봤거나 적게 깬 레벨이 상대적으로 더 자주 나온다.
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

        /// <summary>게시판 퀘스트 수(맨 위 도전 포함): 처음엔 3개, 은월의 숲 레벨이 열릴 때마다 1개씩 늘어 Lv5면 7개.
        /// 생성자의 size는 쓰지 않는다(예전 고정 5칸).</summary>
        public static int ListingCount => 2 + ForestProgress.UnlockedLevel;

        public IReadOnlyList<Quest> Refresh()
        {
            var unlockedRegions = RegionInfo.GetUnlockedRegions();
            int normal = ListingCount - 1; // 맨 위 도전 퀘스트를 뺀 일반 칸
            var result = new List<Quest>(ListingCount);
            for (int i = 0; i < normal; i++)
            {
                // 최고 등급을 넘는 퀘스트는 다시 뽑는다. 끝내 못 맞추면 그중 가장 낮은 등급을 쓴다.
                Quest best = null;
                for (int attempt = 0; attempt < MaxRerolls; attempt++)
                {
                    var (region, level) = PickWeightedRegionLevel(unlockedRegions);
                    var quest = QuestGenerator.Generate(region, level, MinEnemyCount, MaxEnemyCount, _random);
                    if (best == null || QuestDifficulty.Grade(quest) < QuestDifficulty.Grade(best)) best = quest;
                    if (QuestDifficulty.Grade(quest) <= MaxGrade) { best = quest; break; }
                }
                result.Add(best);
            }

            result.Insert(0, GenerateChallenge());
            Listings = result;
            return Listings;
        }

        /// <summary>맨 위 도전 퀘스트: 지금 열린 최고 레벨(길드 등급 제한과 상관없이 — 레벨 진행이 막히지 않게). Lv5면 앤트킹 토벌.
        /// 은월의 숲 레벨이 열리면 게시판을 다시 뽑지 않고 이것만 맨 위에 더한다(QuestBoardUI).</summary>
        public Quest GenerateChallenge()
        {
            var quest = QuestGenerator.Generate(RegionInfo.GetUnlockedRegions()[0], ForestProgress.UnlockedLevel, MinEnemyCount, MaxEnemyCount, _random);
            quest.IsChallenge = true;
            return quest;
        }

        /// <summary>다음 새 의뢰 날짜를 굴릴 때(QuestBoardListings.Set).</summary>
        public Random Rng => _random;

        /// <summary>지역×열린 레벨(1~UnlockedLevel) 전부를 후보로 두고, 이미 많이 깬 조합일수록 뽑힐 확률을 낮춘다(QuestClearHistory).</summary>
        private (Region region, int level) PickWeightedRegionLevel(IReadOnlyList<Region> regions)
        {
            int maxLevel = ForestProgress.UnlockedLevel;
            var candidates = new List<(Region region, int level, double weight)>(regions.Count * maxLevel);
            double total = 0;
            foreach (var region in regions)
                for (int level = 1; level <= maxLevel; level++)
                {
                    double w = 1.0 / (1.0 + QuestClearHistory.ClearsOf(region, level) * DecayPerClear);
                    candidates.Add((region, level, w));
                    total += w;
                }

            double roll = _random.NextDouble() * total;
            foreach (var c in candidates)
            {
                roll -= c.weight;
                if (roll <= 0) return (c.region, c.level);
            }

            var last = candidates[candidates.Count - 1];
            return (last.region, last.level); // 부동소수 오차로 못 고르는 극히 드문 경우의 안전망
        }
    }
}
