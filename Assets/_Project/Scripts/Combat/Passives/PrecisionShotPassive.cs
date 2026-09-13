using System;

namespace GN3.Combat
{
    /// <summary>궁수 전용. 일정 확률로 치명타를 낸다.</summary>
    public class PrecisionShotPassive : PassiveBase
    {
        private const float CritChance = 0.25f;
        private const float CritMultiplier = 2f;

        public override string Name => "정밀 사격";
        public override string Description => $"공격 시 {CritChance:P0} 확률로 치명타 (피해 {CritMultiplier}배)";

        public override void OnAttack(AttackContext context)
        {
            if (context.Rng.NextDouble() < CritChance)
            {
                context.Damage = (int)Math.Round(context.Damage * CritMultiplier);
                context.Critical = true;
            }
        }
    }
}
