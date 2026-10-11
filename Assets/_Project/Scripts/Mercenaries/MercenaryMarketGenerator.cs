using System;
using System.Collections.Generic;
using System.Linq;
using GN3.CharacterAnim;
using GN3.Traits;

namespace GN3.Mercenaries
{
    public static class MercenaryMarketGenerator
    {
        /// <summary>
        /// 시장 용병을 만든다. 나이는 GrowthTuning.RollHireAge, 처음 경력(성장 횟수) = 나이 경력(StartGrowthForAge) + 길드 단계 경력(minGrowth~maxGrowth).
        /// 나이 든 용병은 처음부터 강하지만 덜 자라고, 어린 용병은 약하지만 빨리 자란다.
        /// </summary>
        public static List<Mercenary> Generate(IReadOnlyList<MercenaryClassSO> classPool, int count, int minGrowth, int maxGrowth, Random rng,
            MercenaryGrade maxGrade = MercenaryGrade.S)
        {
            var result = new List<Mercenary>(count);

            // 현재 파티에 고용 중인 용병의 이름은 시장에 다시 등장하지 않는다(해고하면 다시 등장 가능).
            var excludedNames = new HashSet<string>(PlayerParty.Instance.Members.Select(m => m.Name));

            for (int i = 0; i < count; i++)
            {
                var mercClass = classPool[rng.Next(classPool.Count)];
                int age = GrowthTuning.RollHireAge(rng);
                int birthday = rng.Next(GN3.World.GameCalendar.DaysPerYear);
                string name = MercenaryNamePool.GetRandom(rng, excludedNames);
                excludedNames.Add(name);
                var appearance = RandomCharacterComposer.Compose(rng);
                var personality = PersonalityTable.GetRandom(rng);
                bool hasRarePassive = ClassPassiveFactory.RollRare(rng);
                var talent = GradeTable.Roll(rng, maxGrade); // 재능(길드 단계 상한). 등급은 전투력으로 정해진다
                var merc = new Mercenary(name, mercClass, age, birthday, appearance, personality, hasRarePassive, talent);
                merc.GrantStartingGrowth(GrowthTuning.StartGrowthForAge(age) + rng.Next(minGrowth, maxGrowth + 1), rng);
                result.Add(merc);
            }
            return result;
        }
    }
}
