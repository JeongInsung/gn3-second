using GN3.Combat;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 직업별 직접전투 액티브 스킬 하나씩(ClassPassiveFactory와 같은 자리, 패시브와는 별개).
    /// 길잡이(Guide)는 전투 스킬이 없다는 기존 설계(파견 습격 회피 전담)를 그대로 따라 null을 돌려준다.
    /// </summary>
    public static class ClassSkillFactory
    {
        public static SkillBase Create(MercenaryClassKind kind)
        {
            switch (kind)
            {
                case MercenaryClassKind.Warrior: return new PowerStrikeSkill();
                case MercenaryClassKind.Archer: return new PiercingShotSkill();
                case MercenaryClassKind.Healer: return new HealSkill();
                case MercenaryClassKind.Assassin: return new FinishingBlowSkill();
                default: return null;
            }
        }
    }
}
