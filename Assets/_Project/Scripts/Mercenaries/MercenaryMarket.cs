using System;
using System.Collections.Generic;

namespace GN3.Mercenaries
{
    public class MercenaryMarket
    {
        private readonly List<MercenaryClassSO> _classPool;
        private readonly Random _random;

        public int Size { get; }
        /// <summary>길드 단계가 정하는 시장 용병의 경력(성장 횟수) 범위. 나이 경력은 따로 더해진다.</summary>
        public int MinGrowth { get; set; }
        public int MaxGrowth { get; set; }

        /// <summary>시장에 나올 수 있는 최고 등급(길드 단계가 정한다).</summary>
        public MercenaryGrade MaxGrade { get; set; } = MercenaryGrade.S;

        /// <summary>용병마다 재능을 두 번 굴려 좋은 쪽을 쓸 확률(마을 매력 — VillageCharm.MarketQualityChance).</summary>
        public float QualityChance { get; set; }
        public IReadOnlyList<Mercenary> Listings { get; private set; } = new List<Mercenary>();

        public MercenaryMarket(List<MercenaryClassSO> classPool, int size = 5, int minGrowth = 0, int maxGrowth = 4, int? seed = null)
        {
            _classPool = classPool;
            Size = size;
            MinGrowth = minGrowth;
            MaxGrowth = maxGrowth;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public IReadOnlyList<Mercenary> Refresh()
        {
            Listings = MercenaryMarketGenerator.Generate(_classPool, Size, MinGrowth, MaxGrowth, _random, MaxGrade, QualityChance);
            return Listings;
        }
    }
}
