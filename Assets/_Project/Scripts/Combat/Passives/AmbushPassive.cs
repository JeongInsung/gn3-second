using System;

namespace GN3.Combat
{
    /// <summary>암살자 전용. 아직 피해를 입지 않은(체력 100%) 대상에게 추가 피해를 준다.</summary>
    public class AmbushPassive : PassiveBase
    {
        private const float BonusDamage = 0.5f;

        public override string Name => "기습";
        public override string Description => $"대상이 아직 피해를 입지 않았다면 피해 {BonusDamage:P0} 증가";

        public override void OnAttack(AttackContext context)
        {
            if (context.Defender.CurrentHealth == context.Defender.Stats.MaxHealth)
                context.Damage = (int)Math.Round(context.Damage * (1f + BonusDamage));
        }
    }
}
