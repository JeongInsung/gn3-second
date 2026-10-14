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
        /// <summary>마력: 마법사의 공격 능력치(마법사는 Attack 대신 이 값으로 싸운다).</summary>
        public int Magic;
        /// <summary>신성력: 힐러의 회복량을 정한다.</summary>
        public int Holy;

        /// <summary>공격할 때 쓰는 값(공격 + 마력). 마법사가 아니면 마력이 0이라 공격과 같다.</summary>
        public int Offense => Attack + Magic;

        public CombatStats(int attack, int defense, int maxHealth, int moveSpeed = 0, int magic = 0, int holy = 0)
        {
            Attack = attack;
            Defense = defense;
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            Magic = magic;
            Holy = holy;
        }
    }
}
