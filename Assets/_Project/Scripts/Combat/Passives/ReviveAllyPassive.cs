using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>힐러 레어 패시브. 전투 중 1회, 낮은 확률로 쓰러진 아군 한 명을 부활시킨다.</summary>
    public class ReviveAllyPassive : PassiveBase
    {
        private const float ProcChance = 0.3f;
        private const float ReviveHolyRatio = 2.5f;

        private IReadOnlyList<Combatant> _allies;
        private bool _used;

        public override string Name => "아군 부활";
        public override string Description => $"전투 중 1회, 매 라운드 {ProcChance:P0} 확률로 쓰러진 아군 1명을 신성력 × {ReviveHolyRatio * CombatFormulas.DamageScale:0.##}의 체력으로 부활(최대 체력까지)";

        public override void OnBattleStart(Combatant self, IReadOnlyList<Combatant> allies, IReadOnlyList<Combatant> enemies)
        {
            _allies = allies;
            _used = false;
        }

        public override void OnRoundStart(Combatant self, int round, Random rng)
        {
            if (_used || _allies == null) return;

            var fallen = _allies.Where(a => !a.IsAlive).ToList();
            if (fallen.Count == 0) return;

            if (rng.NextDouble() >= ProcChance) return;

            var target = fallen[rng.Next(fallen.Count)];
            int before = target.CurrentHealth;
            target.Heal(Math.Clamp(CombatFormulas.Scale(self.Stats.Holy * ReviveHolyRatio), 1, target.Stats.MaxHealth));
            Combatant.RecordHeal(self, before, target); // 평점용(살려 낸 체력)
            _used = true;
            PassiveEvents.Push(new BattleEvent(round, self, target, -(target.CurrentHealth - before), false, proc: "부활!")); // 직접 전투: 금빛 "부활!" + 초록 +N
        }
    }
}
