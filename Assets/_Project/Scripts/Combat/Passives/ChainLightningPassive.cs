using System;

namespace GN3.Combat
{
    /// <summary>마법사 레어 패시브. 이번 턴 공격이 번개처럼 모든 적에게 퍼진다(궁수의 광역 공격보다 자주).</summary>
    public class ChainLightningPassive : PassiveBase
    {
        private const float ProcChance = 0.25f;

        public override string Name => "연쇄 번개";
        public override string Description => $"매 턴 {ProcChance:P0} 확률로 이번 공격이 모든 적에게 적중";

        public override bool TryTriggerAoeAttack(Random rng) => rng.NextDouble() < ProcChance;
    }
}
