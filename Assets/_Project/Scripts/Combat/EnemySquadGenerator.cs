using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Combat
{
    public static class EnemySquadGenerator
    {
        /// <summary>tierOverride를 주면 이름 계층(약탈 세력/지형 동물/보스)은 그 값으로 고정하고
        /// difficulty는 스탯 강도에만 반영한다. 예: 이동 중 습격은 항상 "약탈 세력" 이름(tier 0)을 쓰되,
        /// 퀘스트 난이도에 맞춰 스탯만 세게 만들 때 사용.</summary>
        public static List<Combatant> Generate(Region region, int count, int difficulty, Random rng, int? tierOverride = null)
        {
            int tier = tierOverride ?? Math.Clamp(difficulty - 1, 0, 2);

            var result = new List<Combatant>(count);
            for (int i = 0; i < count; i++)
            {
                string name = RegionInfo.GetMonsterName(region, tier, rng);
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
