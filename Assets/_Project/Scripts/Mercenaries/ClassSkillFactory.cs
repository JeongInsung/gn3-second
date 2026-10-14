using System.Collections.Generic;
using GN3.Combat;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 직업별 액티브 스킬 하나씩(ClassPassiveFactory와 같은 자리, 패시브와는 별개). 직접 전투와 자동 전투 모두 쓴다.
    /// 길잡이(Guide)는 전투 스킬이 없다는 기존 설계(파견 습격 회피 전담)를 그대로 따라 null을 돌려준다.
    /// </summary>
    public static class ClassSkillFactory
    {
        /// <summary>파견원과 그 전투 유닛(같은 순서)으로 자동 전투용 스킬 표를 만든다.</summary>
        public static Dictionary<Combatant, SkillBase> ForTeam(IList<Mercenary> mercs, IList<Combatant> combatants)
        {
            var skills = new Dictionary<Combatant, SkillBase>();
            for (int i = 0; i < mercs.Count; i++)
            {
                var skill = Create(mercs[i].Class.Kind);
                if (skill != null) skills[combatants[i]] = skill;
            }
            return skills;
        }

        public static SkillBase Create(MercenaryClassKind kind)
        {
            switch (kind)
            {
                case MercenaryClassKind.Warrior: return new PowerStrikeSkill();
                case MercenaryClassKind.Archer: return new PiercingShotSkill();
                case MercenaryClassKind.Healer: return new HealSkill();
                case MercenaryClassKind.Assassin: return new FinishingBlowSkill();
                case MercenaryClassKind.Mage: return new FireballSkill();
                default: return null;
            }
        }
    }
}
