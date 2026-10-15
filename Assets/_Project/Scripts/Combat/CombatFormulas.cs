using System;

namespace GN3.Combat
{
    public static class CombatFormulas
    {
        public const int MinimumDamage = 1;
        public const float EvadeChancePerMoveSpeed = 0.02f;
        public const float MaxEvadeChance = 0.6f;

        // 전투 안의 모든 피해·회복(기본 공격, 방어 무시 스킬, 독, 치유)에 곱하는 배율. 2026-10-16 1 → 0.35:
        // 용병 공격(60~90)이 체력(25~60)보다 훨씬 커 서로 한두 방에 쓰러졌다. 비율로 줄여 방어의 의미는 그대로, 전투 밖 체력은 건드리지 않는다.
        // 적 체력·공격은 EnemySquadGenerator에서 다시 맞췄다(서로 4~6대, 승률은 그대로).
        public const float DamageScale = 0.35f;

        /// <summary>피해·회복량에 DamageScale을 곱한다(반올림, 최소 1).</summary>
        public static int Scale(float amount) => Math.Max(MinimumDamage, (int)Math.Round(amount * DamageScale));

        public static int CalculateDamage(Combatant attacker, Combatant defender)
        {
            int raw = attacker.Stats.Offense - defender.Stats.Defense;
            return Scale(raw < MinimumDamage ? MinimumDamage : raw);
        }

        public static float CalculateEvadeChance(Combatant defender)
        {
            float chance = defender.Stats.MoveSpeed * EvadeChancePerMoveSpeed;
            if (chance < 0f) return 0f;
            return chance > MaxEvadeChance ? MaxEvadeChance : chance;
        }
    }
}
