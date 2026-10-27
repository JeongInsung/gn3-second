using System;

namespace GN3.Combat
{
    /// <summary>고블린 방패병 전용. 공격을 받을 때 일정 확률로 방패로 막아 피해를 크게 줄인다(직접 전투는 "막기" 글자·막기 동작).</summary>
    public class ShieldBlockPassive : PassiveBase
    {
        private const double BlockChance = 0.35;
        private const float DamageReduction = 0.7f;

        public override string Name => "방패 막기";
        public override string Description => $"공격을 {BlockChance:P0} 확률로 막아 받는 피해 {DamageReduction:P0} 감소";

        public override void OnDefend(AttackContext context)
        {
            if (context.Rng.NextDouble() >= BlockChance) return;
            context.Damage = Math.Max(0, (int)Math.Round(context.Damage * (1f - DamageReduction)));
            context.Blocked = true;
        }
    }
}
