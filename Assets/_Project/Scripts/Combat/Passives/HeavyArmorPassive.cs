using System;

namespace GN3.Combat
{
    /// <summary>
    /// 전사 고유 특성(일반·레어 패시브와 별개로 늘 붙는다 — ClassPassiveFactory). 받는 피해를 늘 줄인다.
    /// 방어 능력치는 최소 피해(공격의 25%, CombatFormulas.MinimumDamageRatio)에 걸려 앞에서 많이 맞는 전사를 지키지 못해 %감소로 둔다.
    /// </summary>
    public class HeavyArmorPassive : PassiveBase
    {
        private const float DamageReduction = 0.3f;

        public override string Name => "중갑";
        public override string Description => $"받는 피해 {DamageReduction:P0} 감소";

        public override void OnDefend(AttackContext context)
        {
            if (context.Damage <= 0) return;
            context.Damage = Math.Max(CombatFormulas.MinimumDamage, (int)Math.Round(context.Damage * (1f - DamageReduction)));
        }
    }
}
