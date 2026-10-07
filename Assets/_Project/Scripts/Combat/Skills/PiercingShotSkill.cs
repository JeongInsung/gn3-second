using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>궁수 전용 액티브. 방어력을 무시하고 적 하나를 쏜다(방어가 두꺼운 상대에게 특히 유용).</summary>
    public class PiercingShotSkill : SkillBase
    {
        public override string Name => "관통 사격";
        public override string Description => "방어력을 무시하고 적 하나를 공격한다";
        public override float CooldownSeconds => 6f;
        public override SkillTargetType TargetType => SkillTargetType.Enemy;

        public override List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng)
        {
            return new List<BattleEvent> { CombatResolver.ResolveAttack(user, target, round, enemies, rng, ignoreDefense: true) };
        }
    }
}
