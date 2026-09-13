using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Characters;
using GN3.Traits;

namespace GN3.Mercenaries
{
    public static class MercenaryMarketGenerator
    {
        public static List<Mercenary> Generate(IReadOnlyList<MercenaryClassSO> classPool, int count, int minLevel, int maxLevel, Random rng)
        {
            var result = new List<Mercenary>(count);

            // 현재 파티에 고용 중인 용병의 이름은 시장에 다시 등장하지 않는다(해고하면 다시 등장 가능).
            var excludedNames = new HashSet<string>(PlayerParty.Instance.Members.Select(m => m.Name));

            for (int i = 0; i < count; i++)
            {
                var mercClass = classPool[rng.Next(classPool.Count)];
                int level = rng.Next(minLevel, maxLevel + 1);
                string name = MercenaryNamePool.GetRandom(rng, excludedNames);
                excludedNames.Add(name);
                var appearance = CharacterAppearance.GenerateRandom(rng);
                var personality = PersonalityTable.GetRandom(rng);
                bool hasRarePassive = ClassPassiveFactory.RollRare(rng);
                result.Add(new Mercenary(name, mercClass, level, appearance, personality, hasRarePassive));
            }
            return result;
        }
    }
}
