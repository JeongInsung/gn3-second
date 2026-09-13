using System;

namespace GN3.Combat
{
    [Serializable]
    public class CombatStats
    {
        public int Attack;
        public int Defense;
        public int MaxHealth;
        public int MoveSpeed;

        public CombatStats(int attack, int defense, int maxHealth, int moveSpeed = 0)
        {
            Attack = attack;
            Defense = defense;
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
        }
    }
}
