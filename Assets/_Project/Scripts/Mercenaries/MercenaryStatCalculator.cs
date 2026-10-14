using GN3.Combat;

namespace GN3.Mercenaries
{
    /// <summary>직업 기본값 + 능력치별 성장 횟수 × 직업 성장치(레벨 대신, Mercenary.GrowthAttack 등).</summary>
    public static class MercenaryStatCalculator
    {
        public static CombatStats Calculate(MercenaryClassSO mercenaryClass, int attackGrowths, int defenseGrowths, int healthGrowths, int speedGrowths, int holyGrowths = 0)
        {
            return new CombatStats(
                attack: mercenaryClass.BaseAttack + mercenaryClass.AttackGrowth * attackGrowths,
                defense: mercenaryClass.BaseDefense + mercenaryClass.DefenseGrowth * defenseGrowths,
                maxHealth: mercenaryClass.BaseHealth + mercenaryClass.HealthGrowth * healthGrowths,
                moveSpeed: mercenaryClass.BaseMoveSpeed + mercenaryClass.MoveSpeedGrowth * speedGrowths,
                holy: mercenaryClass.BaseHoly + mercenaryClass.HolyGrowth * holyGrowths
            );
        }
    }
}
