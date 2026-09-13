using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>힐러 레어 패시브. 전투 중 1회, 낮은 확률로 쓰러진 아군 한 명을 부활시킨다.</summary>
    public class ReviveAllyPassive : PassiveBase
    {
        private const float ProcChance = 0.3f;
        private const float ReviveHealthRatio = 0.5f;

        private IReadOnlyList<Combatant> _allies;
        private bool _used;

        public override string Name => "아군 부활";
        public override string Description => $"전투 중 1회, 매 라운드 {ProcChance:P0} 확률로 쓰러진 아군 1명을 체력 {ReviveHealthRatio:P0}로 부활";

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
            target.Heal((int)Math.Round(target.Stats.MaxHealth * ReviveHealthRatio));
            _used = true;
        }
    }
}
