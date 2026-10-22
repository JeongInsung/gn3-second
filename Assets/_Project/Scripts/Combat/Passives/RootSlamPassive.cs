using System;

namespace GN3.Combat
{
    /// <summary>
    /// 보스 거목 엔트: 한 팔 휘두르기(단일)와 뿌리 내려찍기(광역 — 발밑에서 가시가 솟는다)를 SlamEvery번에 한 번꼴로 섞는다.
    /// 자동 전투는 그 즉시 전원 광역. 직접 전투는 붉은 범위로 예고한 뒤 분출해 범위 안에 남은 아군만 맞는다(분대장은 피할 수 있다,
    /// FieldBattleSession.StartSlam). 엔트는 제자리에 뿌리박아(FieldBattleSession.IsRooted) 손이 닿는 아군이 없으면 휘두를 차례는 넘긴다.
    /// 한 대의 피해는 SlamDamageRatio배. 전투 화면은 LastWasSlam으로 피해 이벤트를 구분한다. 유닛마다 따로 만들어 붙인다(공격 횟수를 센다).
    /// </summary>
    public class RootSlamPassive : PassiveBase
    {
        public const int SlamEvery = 3;
        public const float SlamDamageRatio = 0.6f;

        private int _attacks;

        /// <summary>마지막 공격이 내려찍기였는지.</summary>
        public bool LastWasSlam { get; private set; }

        public override string Name => "뿌리 내려찍기";
        public override string Description => $"{SlamEvery}번째 공격마다 범위 안의 적에게 피해 {SlamDamageRatio:P0}";

        public override bool TryTriggerAoeAttack(Random rng)
        {
            _attacks++;
            LastWasSlam = _attacks % SlamEvery == 0;
            return LastWasSlam;
        }

        public override void OnAttack(AttackContext context)
        {
            if (LastWasSlam)
                context.Damage = Math.Max(CombatFormulas.MinimumDamage, (int)Math.Round(context.Damage * SlamDamageRatio));
        }
    }
}
