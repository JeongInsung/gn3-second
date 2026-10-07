using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>암살자 전용 액티브. 체력이 40% 이하인 대상은 확정 처치(회피는 그대로 가능), 그 외엔 1.5배 피해.</summary>
    public class FinishingBlowSkill : SkillBase
    {
        private const float DamageMultiplier = 1.5f;
        private const float ExecuteThreshold = 0.4f;
        private const float ExecuteMultiplier = 100f; // 방어력을 뚫고 확정 처치할 만큼 충분히 큰 배율(InstakillPassive와 같은 발상)

        public override string Name => "마무리 일격";
        public override string Description => $"체력 {ExecuteThreshold:P0} 이하인 적을 확정 처치, 그 외엔 피해 {DamageMultiplier}배";
        public override float CooldownSeconds => 5f;
        public override SkillTargetType TargetType => SkillTargetType.Enemy;

        public override List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng)
        {
            bool execute = target.CurrentHealth <= target.Stats.MaxHealth * ExecuteThreshold;
            float multiplier = execute ? ExecuteMultiplier : DamageMultiplier;
            return new List<BattleEvent> { CombatResolver.ResolveAttack(user, target, round, enemies, rng, damageMultiplier: multiplier) };
        }
    }
}
