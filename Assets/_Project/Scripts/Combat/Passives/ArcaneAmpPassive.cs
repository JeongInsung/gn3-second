using System;

namespace GN3.Combat
{
    /// <summary>마법사 전용. 모든 공격 피해가 늘어난다.</summary>
    public class ArcaneAmpPassive : PassiveBase
    {
        private const float DamageBonus = 0.2f;

        public override string Name => "마력 증폭";
        public override string Description => $"공격 피해 +{DamageBonus:P0}";

        public override void OnAttack(AttackContext context)
        {
            context.Damage = (int)Math.Round(context.Damage * (1f + DamageBonus));
        }
    }
}
