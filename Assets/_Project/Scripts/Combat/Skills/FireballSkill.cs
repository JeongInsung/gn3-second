using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>마법사 전용 액티브. 살아 있는 적 전원을 약한 피해로 한꺼번에 공격한다(대상 지정 없이 적 하나를 고르면 그 줄 전체).</summary>
    public class FireballSkill : SkillBase
    {
        private const float DamageMultiplier = 0.6f;

        public override string Name => "화염구";
        public override string Description => $"모든 적을 피해 {DamageMultiplier:P0}로 공격한다";
        public override float CooldownSeconds => 8f;
        public override SkillTargetType TargetType => SkillTargetType.Enemy;

        public override List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng)
        {
            var events = new List<BattleEvent>();
            foreach (var enemy in enemies.Where(e => e.IsAlive).ToList())
                events.Add(CombatResolver.ResolveAttack(user, enemy, round, enemies, rng, damageMultiplier: DamageMultiplier));
            return events;
        }
    }
}
