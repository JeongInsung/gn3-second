using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>전사 전용 액티브. 적 하나에게 평소보다 훨씬 강한 일격을 먹인다.</summary>
    public class PowerStrikeSkill : SkillBase
    {
        private const float DamageMultiplier = 1.8f;

        public override string Name => "강타";
        public override string Description => $"적 하나에게 피해 {DamageMultiplier}배의 강한 일격을 가한다";
        public override float CooldownSeconds => 8f;
        public override SkillTargetType TargetType => SkillTargetType.Enemy;

        public override List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng)
        {
            return new List<BattleEvent> { CombatResolver.ResolveAttack(user, target, round, enemies, rng, damageMultiplier: DamageMultiplier) };
        }
    }
}
