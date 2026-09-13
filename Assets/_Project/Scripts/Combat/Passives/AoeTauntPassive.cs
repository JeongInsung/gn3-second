using System;

namespace GN3.Combat
{
    /// <summary>전사 레어 패시브. 낮은 확률로 이번 라운드 동안 모든 적의 공격을 자신에게 유도한다.</summary>
    public class AoeTauntPassive : PassiveBase
    {
        private const float ProcChance = 0.2f;

        public override string Name => "광역 도발";
        public override string Description => $"매 라운드 {ProcChance:P0} 확률로 이번 라운드 동안 모든 적의 공격을 자신에게 유도";

        public override void OnRoundStart(Combatant self, int round, Random rng)
        {
            self.IsTaunting = rng.NextDouble() < ProcChance;
        }
    }
}
