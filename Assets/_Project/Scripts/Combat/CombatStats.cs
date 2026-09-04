using System;

namespace GN3.Combat
{
    [Serializable]
    public class CombatStats
    {
        public int Attack;
        public int Defense;
        public int MaxHealth;

        public CombatStats(int attack, int defense, int maxHealth)
        {
            Attack = attack;
            Defense = defense;
            MaxHealth = maxHealth;
        }
    }
}
