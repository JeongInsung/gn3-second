using System;
using System.Collections.Generic;

namespace GN3.Combat
{
    public static class EnemySquadGenerator
    {
        private static readonly string[] Names =
        {
            "고블린", "오크", "슬라임", "스켈레톤", "도적", "늑대", "코볼트", "트롤"
        };

        public static List<Combatant> Generate(int count, int difficulty, Random rng)
        {
            var result = new List<Combatant>(count);
            for (int i = 0; i < count; i++)
            {
                string name = Names[rng.Next(Names.Length)];
                int attack = 6 + difficulty * 2 + rng.Next(-1, 2);
                int defense = 2 + difficulty + rng.Next(-1, 2);
                int health = 20 + difficulty * 8 + rng.Next(-3, 4);
                int moveSpeed = 4 + difficulty + rng.Next(-1, 2);

                var stats = new CombatStats(
                    attack: Math.Max(1, attack),
                    defense: Math.Max(0, defense),
                    maxHealth: Math.Max(1, health),
                    moveSpeed: Math.Max(0, moveSpeed));

                result.Add(new Combatant(name, stats));
            }
            return result;
        }
    }
}
