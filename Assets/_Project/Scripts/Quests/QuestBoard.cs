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

            result.Insert(0, SignatureQuest(unlockedRegions[0])); // 맨 위: 늘 올라오는 S급 엔트 토벌(길드 등급 제한과 상관없이)
            Listings = result;
            return Listings;
        }

        // 은월의 숲의 주인(거목 엔트) 토벌: 보스 계층 3라운드를 난이도 4로 — 권장 전투력이 가장 가벼운 구성(유효 적 4.5)도
        // 4.5 × (58 + 21×4) = 639 ≥ 600이라 늘 S급이다. 그래도 S가 아니면 난이도를 올려 다시 뽑는다(안전망).
        public const int SignatureDifficulty = 4;

        private Quest SignatureQuest(Region region)
        {
            for (int difficulty = SignatureDifficulty; ; difficulty++)
            {
                var quest = QuestGenerator.Generate(region, MinEnemyCount, MaxEnemyCount, _random, tierOverride: 2, difficultyOverride: difficulty);
                if (QuestDifficulty.Grade(quest) == MercenaryGrade.S) return quest;
            }
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
