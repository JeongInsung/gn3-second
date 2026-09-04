using System;
using System.Collections.Generic;

namespace GN3.Mercenaries
{
    public static class MercenaryMarketGenerator
    {
        public static List<Mercenary> Generate(IReadOnlyList<MercenaryClassSO> classPool, int count, int minLevel, int maxLevel, Random rng)
        {
            var result = new List<Mercenary>(count);
            for (int i = 0; i < count; i++)
            {
                var mercClass = classPool[rng.Next(classPool.Count)];
                int level = rng.Next(minLevel, maxLevel + 1);
                string name = MercenaryNamePool.GetRandom(rng);
                result.Add(new Mercenary(name, mercClass, level));
            }
            return result;
        }
    }
}
