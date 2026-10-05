using System;
using System.Collections.Generic;

namespace GN3.Mercenaries
{
    public class MercenaryMarket
    {
        private readonly List<MercenaryClassSO> _classPool;
        private readonly Random _random;

        public int Size { get; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }

        /// <summary>시장에 나올 수 있는 최고 등급(길드 단계가 정한다).</summary>
        public MercenaryGrade MaxGrade { get; set; } = MercenaryGrade.S;
        public IReadOnlyList<Mercenary> Listings { get; private set; } = new List<Mercenary>();

        public MercenaryMarket(List<MercenaryClassSO> classPool, int size = 5, int minLevel = 1, int maxLevel = 3, int? seed = null)
        {
            _classPool = classPool;
            Size = size;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public IReadOnlyList<Mercenary> Refresh()
        {
            Listings = MercenaryMarketGenerator.Generate(_classPool, Size, MinLevel, MaxLevel, _random, MaxGrade);
            return Listings;
        }
    }
}
