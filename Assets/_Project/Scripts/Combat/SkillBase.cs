using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>스킬이 누구를 대상으로 하는지. 직접전투 UI가 대상 목록(적/아군)을 고를 때 쓴다.</summary>
    public enum SkillTargetType
    {
        Enemy,
        Ally,
    }

    /// <summary>
    /// 직업별 "액티브 스킬" 기반 클래스. 패시브(PassiveBase)는 전투 중 자동 확률로 발동하는 데 비해,
    /// 스킬은 직접전투에서 플레이어가 턴마다 직접 고르는 1개짜리 행동이다(쿨타임 있음).
    /// </summary>
    public abstract class SkillBase
    {
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>한 번 쓰면 다시 쓸 수 있을 때까지 걸리는 실시간 초(ATB - 게이지와 별개로 실시간으로 흐른다).</summary>
        public abstract float CooldownSeconds { get; }

        public abstract SkillTargetType TargetType { get; }

        /// <summary>
        /// 스킬 발동. target은 TargetType에 맞는 대상(적이면 적 하나, 아군이면 아군 하나 - 자신 포함 가능).
        /// 발생한 전투 이벤트(로그·연출용)를 돌려준다. 데미지를 주는 스킬은 CombatResolver.ResolveAttack을
        /// 그대로 써서 회피/패시브 훅이 기본 공격과 동일하게 걸리게 한다.
        /// </summary>
        public abstract List<BattleEvent> Execute(Combatant user, Combatant target, List<Combatant> allies, List<Combatant> enemies,
            int round, System.Random rng);
    }
}
