using System.Collections.Generic;

namespace GN3.Combat
{
    public abstract class PassiveBase
    {
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>전투 시작 시 1회. self 기준 아군/적군 리스트가 전달된다.</summary>
        public virtual void OnBattleStart(Combatant self, IReadOnlyList<Combatant> allies, IReadOnlyList<Combatant> enemies) { }

        /// <summary>매 라운드 시작 시, 자신의 턴이 오기 직전 호출.</summary>
        public virtual void OnRoundStart(Combatant self, int round, System.Random rng) { }

        /// <summary>공격자 입장. 데미지가 계산된 직후 호출되며 context.Damage/Critical을 수정할 수 있다.</summary>
        public virtual void OnAttack(AttackContext context) { }

        /// <summary>이번 턴에 단일 대상 대신 전체 적을 공격할지 여부(광역 공격 등). true를 반환하면 시뮬레이터가 일반 단일 공격 대신 광역 공격을 수행한다.</summary>
        public virtual bool TryTriggerAoeAttack(System.Random rng) => false;

        /// <summary>방어자 입장. 데미지가 적용되기 직전 호출되며 context.Damage/Evaded를 수정할 수 있다.</summary>
        public virtual void OnDefend(AttackContext context) { }

        /// <summary>공격자 입장. 데미지가 실제로 적용된 직후 호출(흡혈 등 후처리용).</summary>
        public virtual void OnDamageDealt(AttackContext context) { }

        /// <summary>자신과 같은 팀의 다른 유닛이 쓰러졌을 때 호출.</summary>
        public virtual void OnAllyDefeated(Combatant self, Combatant defeatedAlly) { }

        /// <summary>이번 공격이 자신에게 치명적(체력 0)일 때 호출. true를 반환하면 체력 1로 생존한다.</summary>
        public virtual bool OnLethalDamage(AttackContext context) => false;
    }
}
