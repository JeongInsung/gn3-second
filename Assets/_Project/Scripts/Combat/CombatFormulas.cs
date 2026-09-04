namespace GN3.Combat
{
    public static class CombatFormulas
    {
        public const int MinimumDamage = 1;

        public static int CalculateDamage(Combatant attacker, Combatant defender)
        {
            int raw = attacker.Stats.Attack - defender.Stats.Defense;
            return raw < MinimumDamage ? MinimumDamage : raw;
        }
    }
}
