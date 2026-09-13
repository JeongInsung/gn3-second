using System;

namespace GN3.Combat
{
    /// <summary>전사 전용. 체력이 낮아지면 받는 피해가 줄어든다.</summary>
    public class UnyieldingBulwarkPassive : PassiveBase
    {
        private const float HealthThreshold = 0.3f;
        private const float DamageReduction = 0.25f;

        public override string Name => "불굴의 방벽";
        public override string Description => $"체력 {HealthThreshold:P0} 이하일 때 받는 피해 {DamageReduction:P0} 감소";

        public override void OnDefend(AttackContext context)
        {
            var defender = context.Defender;
            if (defender.CurrentHealth <= defender.Stats.MaxHealth * HealthThreshold)
                context.Damage = Math.Max(0, (int)Math.Round(context.Damage * (1f - DamageReduction)));
        }
    }
}
