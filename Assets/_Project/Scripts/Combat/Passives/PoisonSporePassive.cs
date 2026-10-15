using System;

namespace GN3.Combat
{
    /// <summary>
    /// 독버섯 전용. 포자가 실제로 맞으면(피해 &gt; 0, 회피 아님) 대상에게 독을 건다:
    /// TicksPerHit번, 한 번에 공격력 × DamageRatio. 자동 전투는 라운드마다, 직접 전투는 1초마다 들어간다(CombatResolver.TickPoison).
    /// 다시 맞으면 남은 횟수를 새로 채운다(겹쳐 쌓이지 않음).
    /// </summary>
    public class PoisonSporePassive : PassiveBase
    {
        public const int TicksPerHit = 3;
        public const float DamageRatio = 0.15f; // 0.3이면 버섯 퀘스트가 같은 판정 늑대 퀘스트보다 크게 어려웠다(중간 25% vs 39%)

        public override string Name => "독 포자";
        public override string Description => $"맞은 대상에게 독({TicksPerHit}회, 공격력의 {DamageRatio * CombatFormulas.DamageScale:P0}씩)";

        public override void OnDamageDealt(AttackContext context)
        {
            if (context.Evaded || context.Damage <= 0) return;
            int damage = CombatFormulas.Scale(context.Attacker.Stats.Attack * DamageRatio); // 기본 공격과 같은 배율
            context.Defender.ApplyPoison(context.Attacker, damage, TicksPerHit);
        }
    }
}
