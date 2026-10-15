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
        /// <summary>독 피해 한 번(공격 동작·투사체 없이 피해만, Attacker = 독을 건 유닛).</summary>
        public readonly bool IsPoison;

        public BattleEvent(int round, Combatant attacker, Combatant target, int damage, bool targetDefeated, bool evaded = false, bool isPoison = false)
        {
            IsPoison = isPoison;
            Round = round;
            Attacker = attacker;
            Target = target;
            Damage = damage;
            TargetDefeated = targetDefeated;
            Evaded = evaded;
        }
    }
}
