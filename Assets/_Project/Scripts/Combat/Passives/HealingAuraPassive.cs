using System;
using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>힐러 전용. 매 라운드 아군 중 체력 비율이 가장 낮은 유닛을 회복시킨다.</summary>
    public class HealingAuraPassive : PassiveBase
    {
        private const float HealRatio = 0.1f;

        private IReadOnlyList<Combatant> _allies;

        public override string Name => "치유의 기운";
        public override string Description => $"매 라운드 아군 중 체력 비율이 가장 낮은 유닛을 최대체력의 {HealRatio:P0}만큼 회복";

        public override void OnBattleStart(Combatant self, IReadOnlyList<Combatant> allies, IReadOnlyList<Combatant> enemies)
        {
            _allies = allies;
        }

        public override void OnRoundStart(Combatant self, int round, System.Random rng)
        {
            if (_allies == null) return;

            Combatant lowest = null;
            float lowestRatio = 1f;

            foreach (var ally in _allies)
            {
                if (!ally.IsAlive) continue;
                float ratio = (float)ally.CurrentHealth / ally.Stats.MaxHealth;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    lowest = ally;
                }
            }

            if (lowest != null)
                lowest.Heal((int)Math.Round(lowest.Stats.MaxHealth * HealRatio));
        }
    }
}
