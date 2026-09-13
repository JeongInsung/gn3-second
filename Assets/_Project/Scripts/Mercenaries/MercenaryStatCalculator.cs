using GN3.Combat;

namespace GN3.Mercenaries
{
    public static class MercenaryStatCalculator
    {
        public static CombatStats Calculate(MercenaryClassSO mercenaryClass, int level)
        {
            int levelBonus = level - 1;
            return new CombatStats(
                attack: mercenaryClass.BaseAttack + mercenaryClass.AttackGrowth * levelBonus,
                defense: mercenaryClass.BaseDefense + mercenaryClass.DefenseGrowth * levelBonus,
                maxHealth: mercenaryClass.BaseHealth + mercenaryClass.HealthGrowth * levelBonus,
                moveSpeed: mercenaryClass.BaseMoveSpeed + mercenaryClass.MoveSpeedGrowth * levelBonus
            );
        }
    }
}
