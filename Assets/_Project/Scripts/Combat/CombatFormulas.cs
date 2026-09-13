namespace GN3.Combat
{
    public static class CombatFormulas
    {
        public const int MinimumDamage = 1;
        public const float EvadeChancePerMoveSpeed = 0.02f;
        public const float MaxEvadeChance = 0.6f;

        public static int CalculateDamage(Combatant attacker, Combatant defender)
        {
            int raw = attacker.Stats.Attack - defender.Stats.Defense;
            return raw < MinimumDamage ? MinimumDamage : raw;
        }

        public static float CalculateEvadeChance(Combatant defender)
        {
            float chance = defender.Stats.MoveSpeed * EvadeChancePerMoveSpeed;
            if (chance < 0f) return 0f;
            return chance > MaxEvadeChance ? MaxEvadeChance : chance;
        }
    }
}
