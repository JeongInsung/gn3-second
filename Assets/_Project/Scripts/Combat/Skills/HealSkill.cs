using System;
using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>힐러 전용 액티브. 패시브(치유의 기운)와 달리 플레이어가 직접 지정한 아군 한 명을 크게 회복시킨다.
    /// 로그(BattleEvent)에는 관례상 Damage를 음수로 담아 "회복"을 표시한다(TargetDefeated/Evaded는 항상 false).</summary>
    public class HealSkill : SkillBase
    {
        private const float HealRatio = 0.35f;

        public override string Name => "치유";
        public override string Description => $"아군 하나의 체력을 최대체력의 {HealRatio:P0} 회복시킨다";
        public override int CooldownTurns => 2;
        public override SkillTargetType TargetType => SkillTargetType.Ally;

        public override List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng)
        {
            int amount = (int)Math.Round(target.Stats.MaxHealth * HealRatio);
            target.Heal(amount);
            return new List<BattleEvent> { new BattleEvent(round, user, target, -amount, false, false) };
        }
    }
}
