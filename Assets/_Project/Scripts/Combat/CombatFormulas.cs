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
        // 2026-10-26 0.35 → 0.55: 전투가 너무 길다는 요청 — 쓰러뜨리는 타수가 약 36% 줄고(아군·적 같은 비율이라 승률은 거의 그대로),
        // 직접 전투 화면 속도(BattleUIController.BattleSpeed 0.7 → 0.95)와 합쳐 전투 시간이 약 절반. 적 공격은 EnemySquadGenerator에서 조금 올렸다.
        // 2026-10-26 0.55 → 0.9: "피해가 안 박힌다" — 한 대 평균 9 안팎이라 적 하나에 13~20대가 들었다.
        public const float DamageScale = 0.9f;

        // 방어가 아무리 높아도 공격의 이만큼은 들어간다(2026-10-26 추가: 공격이 낮은 길잡이는 절반이 1 피해였다).
        public const float MinimumDamageRatio = 0.25f;

        /// <summary>피해·회복량에 DamageScale을 곱한다(반올림, 최소 1).</summary>
        public static int Scale(float amount) => Math.Max(MinimumDamage, (int)Math.Round(amount * DamageScale));

        /// <summary>(공격 − 방어)와 공격 × MinimumDamageRatio 중 큰 쪽 × DamageScale.</summary>
        public static int CalculateDamage(Combatant attacker, Combatant defender)
        {
            float raw = Math.Max(attacker.Stats.Offense - defender.Stats.Defense, attacker.Stats.Offense * MinimumDamageRatio);
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
