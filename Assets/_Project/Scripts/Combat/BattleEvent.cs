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
        /// <summary>방패로 막아 피해가 줄었다(ShieldBlockPassive).</summary>
        public readonly bool Blocked;
        /// <summary>패시브 발동 이름("부활!", "즉사!", "도발!") — 직접 전투가 대상 머리 위에 띄운다(PassiveEvents). 없으면 null.</summary>
        public readonly string Proc;

        public BattleEvent(int round, Combatant attacker, Combatant target, int damage, bool targetDefeated, bool evaded = false, bool isPoison = false,
            bool blocked = false, string proc = null)
        {
            Proc = proc;
            IsPoison = isPoison;
            Blocked = blocked;
            Round = round;
            Attacker = attacker;
            Target = target;
            Damage = damage;
            TargetDefeated = targetDefeated;
            Evaded = evaded;
        }
    }
}
