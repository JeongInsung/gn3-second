using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Combat
{
    public static class EnemySquadGenerator
    {
        // 전투 전체 난도(자동·직접·습격 공통). 2026-10-16 체력·공격 ×1.4 더 올림(1.3·1.15 → 1.82·1.61):
        // 용병 성장·연구·스킬이 붙으며 예전 값으로는 판정 '중간' 승률이 82%까지 올라 쉬웠다.
        // 권장 전투력(QuestDifficulty.RecommendedScale)도 함께 다시 맞췄다 — 승률 수치는 그쪽 주석.
        // 같은 날 전투 피해·회복 ×0.35(CombatFormulas.DamageScale)와 함께 체력 1.82 → 2.4, 공격 1.61 → 1.2:
        // 용병이 판정 3 몬스터를 4~5대에 잡고(전엔 한 방), 판정별 승률은 그대로(자동 전투 3000판: 중간 59→60%, 어려움 27→25%).
        public const float HealthMultiplier = 2.4f;
        // 같은 날 다시 공격 1.2 → 1.44(×1.2): 전투 긴박감. 판정(권장 전투력)은 그대로 둬 같은 판정에서 조금 더 어렵다
        // (자동 전투 6000판: 중간 64→49%, 어려움 26→15%. ×1.4는 중간 33%로 너무 어려웠다).
        public const float AttackMultiplier = 1.44f;

        // 보스(계층 2)는 여러 라운드 퀘스트의 마지막에 혼자 나와 앞 라운드를 버틴 파견대(최대 인원 = 가장 큰 라운드 + 1)를
        // 상대하므로 따로 세게.
        public const float SporeHealthMultiplier = 0.67f; // 독버섯(독이 더해지는 원거리 몹). 피해 ×0.35 뒤 전투가 길어져 0.8이면 늑대보다 어려웠다(중간 24% vs 39%) → 0.67(39%)
        public const float RiderHealthMultiplier = 0.9f; // 고블린 늑대기수(늑대를 탄 고블린): 빠른 대신(회피↑) 조금 덜 단단 — 1.2면 고블린보다 크게 어려웠다(중간 28% vs 43%), 0.9면 47%
        public const int RiderMoveSpeedBonus = 2;
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

                // 독버섯: 맞히면 독(PoisonSporePassive)이 더해지는 원거리 몹이라 체력은 조금 낮게.
                bool spore = enemyName.Contains("버섯");
                bool rider = enemyName.Contains("늑대기수");
                float speciesHealth = spore ? SporeHealthMultiplier : rider ? RiderHealthMultiplier : 1f;
                if (rider) moveSpeed += RiderMoveSpeedBonus;

                var stats = new CombatStats(
                    attack: Math.Max(1, (int)Math.Round(attack * attackMul)),
                    defense: Math.Max(0, defense),
                    maxHealth: Math.Max(1, (int)Math.Round(health * healthMul * speciesHealth)),
                    moveSpeed: Math.Max(0, moveSpeed));

                var passives = spore ? new List<PassiveBase> { new PoisonSporePassive() } : null;
                result.Add(new Combatant(enemyName, stats, passives));
            }
            return result;
        }
    }
}
