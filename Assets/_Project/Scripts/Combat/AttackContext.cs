namespace GN3.Combat
{
    public class AttackContext
    {
        public readonly Combatant Attacker;
        public readonly Combatant Defender;
        public readonly int Round;
        public readonly System.Random Rng;
        public int Damage;
        public bool Evaded;
        public bool Critical;
        /// <summary>방패로 막아 피해가 줄었다(ShieldBlockPassive) — 직접 전투가 "막기" 글자·막기 동작을 보인다.</summary>
        public bool Blocked;

        public AttackContext(Combatant attacker, Combatant defender, int round, int damage, System.Random rng)
        {
            Attacker = attacker;
            Defender = defender;
            Round = round;
            Damage = damage;
            Rng = rng;
        }
    }
}
