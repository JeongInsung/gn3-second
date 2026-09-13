namespace GN3.Combat
{
    public readonly struct BattleEvent
    {
        public readonly int Round;
        public readonly Combatant Attacker;
        public readonly Combatant Target;
        public readonly int Damage;
        public readonly bool TargetDefeated;
        public readonly bool Evaded;

        public BattleEvent(int round, Combatant attacker, Combatant target, int damage, bool targetDefeated, bool evaded = false)
        {
            Round = round;
            Attacker = attacker;
            Target = target;
            Damage = damage;
            TargetDefeated = targetDefeated;
            Evaded = evaded;
        }
    }
}
