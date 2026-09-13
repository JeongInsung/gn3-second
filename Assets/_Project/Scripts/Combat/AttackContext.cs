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
