using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Combat
{
    public static class EnemySquadGenerator
    {
        // 전투 전체 난도(자동·직접·습격 공통). 자동 전투에 스킬이 들어가 쉬워진 만큼 올림 —
        // 권장 전투력 판정 '중간'이면 자동 전투 승률 약 63%, '어려움' 약 26%(예전 91%·58%).
        public const float HealthMultiplier = 1.3f;
        public const float AttackMultiplier = 1.15f;

        // 보스(계층 2)는 여러 라운드 퀘스트의 마지막에 혼자 나와 앞 라운드를 버틴 파견대(최대 인원 = 가장 큰 라운드 + 1)를
        // 상대하므로 따로 세게.
        public const float BossHealthMultiplier = 3f;
        public const float BossAttackMultiplier = 1.3f;

        /// <summary>tierOverride를 주면 이름 계층(약탈 세력/지형 동물/보스)은 그 값으로 고정하고
        /// difficulty는 스탯 강도에만 반영한다. 예: 이동 중 습격은 항상 "약탈 세력" 이름(tier 0)을 쓰되,
        /// 퀘스트 난이도에 맞춰 스탯만 세게 만들 때 사용. name을 주면 모두 그 이름(퀘스트 라운드의 적), 없으면 계층 목록에서 무작위.</summary>
        public static List<Combatant> Generate(Region region, int count, int difficulty, Random rng, int? tierOverride = null, string name = null)
        {
            int tier = tierOverride ?? Math.Clamp(difficulty - 1, 0, 2);
            float healthMul = HealthMultiplier * (tier >= 2 ? BossHealthMultiplier : 1f);
            float attackMul = AttackMultiplier * (tier >= 2 ? BossAttackMultiplier : 1f);

            var result = new List<Combatant>(count);
            for (int i = 0; i < count; i++)
            {
                string enemyName = name ?? RegionInfo.GetMonsterName(region, tier, rng);
                int attack = 6 + difficulty * 2 + rng.Next(-1, 2);
                int defense = 2 + difficulty + rng.Next(-1, 2);
                int health = 20 + difficulty * 8 + rng.Next(-3, 4);
                int moveSpeed = 4 + difficulty + rng.Next(-1, 2);

                var stats = new CombatStats(
                    attack: Math.Max(1, (int)Math.Round(attack * attackMul)),
                    defense: Math.Max(0, defense),
                    maxHealth: Math.Max(1, (int)Math.Round(health * healthMul)),
                    moveSpeed: Math.Max(0, moveSpeed));

                result.Add(new Combatant(enemyName, stats));
            }
            return result;
        }
    }
}
