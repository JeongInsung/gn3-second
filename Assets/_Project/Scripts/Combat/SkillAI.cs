using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>
    /// AI가 스킬을 언제·누구에게 쓸지. 자동 전투(AutoBattleSimulator)와 직접 전투의 AI 대원(FieldBattleSession)이
    /// 같은 판단을 쓴다 - 두 모드의 스킬 활용이 어긋나 한쪽만 유리해지지 않게.
    /// </summary>
    public static class SkillAI
    {
        /// <summary>체력 비율이 이보다 낮은 아군이 있어야 치유를 쓴다.</summary>
        public const float HealBelowRatio = 0.6f;

        /// <summary>자동 전투 1라운드를 몇 초로 볼지(쿨타임 초 → 라운드). 직접 전투에서 이동속도 4인 유닛이 약 3.4초에 한 번 때리는 것과 맞춘 값.</summary>
        public const float SecondsPerRound = 3f;

        public static int CooldownRounds(SkillBase skill) => Math.Max(1, (int)Math.Ceiling(skill.CooldownSeconds / SecondsPerRound));

        /// <summary>지금 이 스킬을 쓸 대상. 쓰지 않는 게 낫거나 쓸 대상이 없으면 null.
        /// enemiesInReach: 닿는 적(직접 전투는 사거리 안, 자동 전투는 살아 있는 적 전부).
        /// currentTarget: 지금 붙어 싸우는 적(없으면 null).</summary>
        public static Combatant ChooseTarget(SkillBase skill, IEnumerable<Combatant> allies, IEnumerable<Combatant> enemiesInReach, Combatant currentTarget)
        {
            if (skill == null) return null;

            if (skill.TargetType == SkillTargetType.Ally)
            {
                var hurt = allies.Where(a => a.IsAlive).OrderBy(HealthRatio).FirstOrDefault();
                return hurt != null && HealthRatio(hurt) < HealBelowRatio ? hurt : null;
            }

            var reachable = enemiesInReach.Where(e => e.IsAlive).ToList();
            if (reachable.Count == 0) return null;
            var weakest = reachable.OrderBy(HealthRatio).First();
            if (skill is FinishingBlowSkill) return weakest; // 마무리할 수 있는 적부터
            return currentTarget != null && reachable.Contains(currentTarget) ? currentTarget : weakest;
        }

        private static float HealthRatio(Combatant c) => (float)c.CurrentHealth / Math.Max(1, c.Stats.MaxHealth);
    }
}
