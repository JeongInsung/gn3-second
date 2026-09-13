using System;

namespace GN3.Combat
{
    /// <summary>암살자 레어 패시브. 낮은 확률로 공격이 대상을 즉사시킨다.</summary>
    public class InstakillPassive : PassiveBase
    {
        private const float ProcChance = 0.12f;
        private const int InstakillDamage = 999999;

        public override string Name => "즉사";
        public override string Description => $"공격 시 {ProcChance:P0} 확률로 대상을 즉사시킴";

        public override void OnAttack(AttackContext context)
        {
            if (context.Rng.NextDouble() < ProcChance)
                context.Damage = InstakillDamage;
        }
    }
}
