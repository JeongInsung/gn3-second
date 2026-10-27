namespace GN3.Combat
{
    /// <summary>
    /// 암살자 고유 특성(일반·레어 패시브와 별개로 늘 붙는다 — ClassPassiveFactory). 이동속도 회피와 따로 한 번 더 피할 기회를 준다.
    /// 먼저 이 확률로 피하고, 못 피하면 원래 회피(이동속도 × 2%, 상한 60%)를 또 굴린다(CombatResolver.ResolveAttack).
    /// </summary>
    public class NimblePassive : PassiveBase
    {
        private const double EvadeChance = 0.3;

        public override string Name => "날렵함";
        public override string Description => $"공격을 {EvadeChance:P0} 확률로 먼저 피한다(이동속도 회피와 따로)";

        public override void OnDefend(AttackContext context)
        {
            if (!context.Evaded && context.Rng.NextDouble() < EvadeChance) context.Evaded = true;
        }
    }
}
