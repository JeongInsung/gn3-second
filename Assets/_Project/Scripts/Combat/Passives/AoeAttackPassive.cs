using System;

namespace GN3.Combat
{
    /// <summary>궁수 레어 패시브. 낮은 확률로 이번 턴 공격이 모든 적에게 적중한다.</summary>
    public class AoeAttackPassive : PassiveBase
    {
        private const float ProcChance = 0.2f;

        public override string Name => "광역 공격";
        public override string Description => $"매 턴 {ProcChance:P0} 확률로 이번 공격이 모든 적에게 적중";

        public override bool TryTriggerAoeAttack(Random rng) => rng.NextDouble() < ProcChance;
    }
}
