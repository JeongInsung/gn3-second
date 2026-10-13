using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>
    /// 직접전투(ATB 실시간 전투)의 상태 기계. 유닛마다 이동속도 기반 "준비 게이지"가 실시간으로 차오르다가
    /// 다 차면 기본공격이 자동으로 나간다(적·플레이어 공통). 스킬은 게이지와 완전히 별개 - 쿨타임만 다 되면
    /// 플레이어가 아무 때나 즉시 쓸 수 있다(UI가 TryUseSkill을 호출). 공격이든 스킬이든 "확정"되면 바로
    /// 때리지 않고 대상 쪽으로 다가가는 모습이 보일 시간(StrikeDelay)을 준 뒤 실제로 적용한다 - 여러 유닛이
    /// 동시에 확정·접근·타격할 수 있다(한 번에 한 명씩만 행동하던 예전 방식과 다른, 진짜 "실시간" 핵심).
    /// AutoBattleSimulator와 같은 핵심 규칙(CombatResolver)을 공유한다. Unity 타입에 의존하지 않는 순수
    /// C# 클래스 - BattleUIController가 매 프레임 Tick을 호출해 화면에 반영한다.
    /// </summary>
    public class ManualBattleSession
    {
        private const float MaxReadiness = 100f;

        // 게이지 충전 속도(%/초) = BaseFillPerSecond + MoveSpeed × FillPerSpeedPerSecond.
        // 이동속도 4(전사 기본)면 약 5.6초, 14(암살자)면 약 2.6초에 한 번 행동. 감으로 정한 값, 체감 보고 조정.
        private const float BaseFillPerSecond = 10f;
        private const float FillPerSpeedPerSecond = 2f;

        private const float MaxBattleSeconds = 180f; // 턴제의 maxRounds에 해당하는 안전장치(무승부 처리)

        // 시작 게이지를 0~40%로 무작위로 흩어두고, 유닛마다 충전 속도도 ±15% 흔들어서
        // 다들 같은 속도로 동시에 시작했을 때 생기는 "몰려서 번갈아 때리는" 패턴을 막는다.
        private const float StartingReadinessJitter = 40f;
        private const float SpeedJitterRange = 0.15f;

        // 공격·스킬이 확정된 뒤 실제로 적용되기까지의 선딜레이 - 근접 유닛이 대상 쪽으로 다가가는 모습이
        // 보일 시간을 번다(걷기 Lerp 속도와 맞춰둔 값, 둘 중 하나만 바꾸면 같이 맞춰야 함).
        private const float StrikeDelay = 0.8f;

        public List<Combatant> PlayerTeam { get; }
        public List<Combatant> EnemyTeam { get; }
        public List<BattleEvent> Log { get; } = new List<BattleEvent>();

        public bool IsOver => !PlayerTeam.Any(c => c.IsAlive) || !EnemyTeam.Any(c => c.IsAlive) || _elapsed >= MaxBattleSeconds;
        public BattleOutcome Outcome => CombatResolver.DetermineOutcome(PlayerTeam, EnemyTeam);
        public int ActionCount { get; private set; }

        private readonly Dictionary<Combatant, SkillBase> _skills;
        private readonly Dictionary<Combatant, float> _readiness = new Dictionary<Combatant, float>();
        private readonly Dictionary<Combatant, float> _cooldowns = new Dictionary<Combatant, float>();
        private readonly Dictionary<Combatant, float> _speedJitter = new Dictionary<Combatant, float>();

        // 플레이어 유닛별 "지금 붙어서 싸우는 대상" - 기본공격은 이 대상이 죽기 전까지 계속 같은 대상에게
        // 나간다. 적에게 쓰는 스킬도 이걸 갱신해서 그다음 기본공격도 이어서 그 대상에게 나가게 한다.
        private readonly Dictionary<Combatant, Combatant> _engagedTarget = new Dictionary<Combatant, Combatant>();

        private class Pending
        {
            public Combatant Target;
            public bool UseSkill;
            public float Timer;
        }

        // 확정됐지만 아직 실제로 적용 안 된 행동(기본공격·스킬) - 플레이어 유닛당 최대 하나, 여러 유닛이 동시에 가능.
        private readonly Dictionary<Combatant, Pending> _pending = new Dictionary<Combatant, Pending>();

        private readonly System.Random _rng;
        private float _elapsed;

        public ManualBattleSession(List<Combatant> playerTeam, Dictionary<Combatant, SkillBase> skills, List<Combatant> enemyTeam, System.Random rng)
        {
            PlayerTeam = playerTeam;
            EnemyTeam = enemyTeam;
            _skills = skills;
            _rng = rng;

            foreach (var c in playerTeam.Concat(enemyTeam))
            {
                _readiness[c] = (float)_rng.NextDouble() * StartingReadinessJitter;
                _speedJitter[c] = 1f - SpeedJitterRange + (float)_rng.NextDouble() * (SpeedJitterRange * 2f);
            }
            foreach (var c in playerTeam)
                _cooldowns[c] = 0f;

            CombatResolver.RaiseBattleStart(PlayerTeam, EnemyTeam);
        }

        public SkillBase SkillOf(Combatant c) => _skills.TryGetValue(c, out var s) ? s : null;
        public float CooldownRemaining(Combatant c) => _cooldowns.TryGetValue(c, out var v) ? v : 0f;
        public float ReadinessOf(Combatant c) => _readiness.TryGetValue(c, out var v) ? v : 0f;
        public float ReadinessRatioOf(Combatant c) => ReadinessOf(c) / MaxReadiness;

        /// <summary>이 유닛이 지금 확정된 행동(기본공격·스킬)을 적용 대기 중인지(다가가는 모습이 보이는 구간).</summary>
        public bool IsActing(Combatant c) => _pending.ContainsKey(c);

        /// <summary>이 플레이어 유닛이 지금 붙어서 싸우는 대상(없으면 null - 아직 한 번도 공격 안 했거나 대상이 죽음).</summary>
        public Combatant EngagedTargetOf(Combatant actor) =>
            _engagedTarget.TryGetValue(actor, out var t) && t != null && t.IsAlive ? t : null;

        /// <summary>용병 클릭→적 클릭(또는 자동)으로 기본공격 대상을 지정한다. 게이지가 이미 다 찼으면 즉시
        /// 확정(그 자리에서 다가가기 시작), 아직 안 찼으면 다음에 찰 때 이 대상을 쓰도록만 기록해둔다.</summary>
        public void CommandAttack(Combatant actor, Combatant target)
        {
            if (actor == null || target == null || !actor.IsAlive || !target.IsAlive) return;
            _engagedTarget[actor] = target;
            if (!_pending.ContainsKey(actor) && ReadinessOf(actor) >= MaxReadiness)
                Commit(actor, false, target);
        }

        /// <summary>플레이어가 지금 스킬을 쓴다 - 게이지·차례와 무관하게 쿨타임만 다 됐으면 언제든 가능.
        /// 성공하면 true(이때부터 StrikeDelay 뒤 실제로 적용됨). 이미 행동 확정 중이거나 쿨타임 중이면 false.</summary>
        public bool TryUseSkill(Combatant actor, Combatant target)
        {
            if (actor == null || target == null || !actor.IsAlive || !target.IsAlive) return false;
            if (_pending.ContainsKey(actor)) return false;
            var skill = SkillOf(actor);
            if (skill == null || CooldownRemaining(actor) > 0f) return false;

            Commit(actor, true, target);
            return true;
        }

        private void Commit(Combatant actor, bool useSkill, Combatant target)
        {
            if (useSkill)
            {
                var skill = SkillOf(actor);
                // 적에게 쓰는 스킬도 기본공격처럼 그 대상에게 "붙는다"(근접이면 그 자리로 다가가는 연출이 이어짐).
                if (skill != null && skill.TargetType == SkillTargetType.Enemy)
                    _engagedTarget[actor] = target;
            }
            else
            {
                _engagedTarget[actor] = target;
            }
            _pending[actor] = new Pending { Target = target, UseSkill = useSkill, Timer = StrikeDelay };
        }

        /// <summary>매 프레임 호출. 게이지를 채우고 쿨타임을 줄이며, 준비된 적·플레이어는 자동으로 기본공격을
        /// 확정시킨다(여러 명 동시에 가능). 확정된 행동은 선딜레이가 다 차면 실제로 적용한다. 전투 중 발생한
        /// 이벤트 목록을 돌려준다(연출·로그 표시용, 없으면 빈 리스트).</summary>
        public List<BattleEvent> Tick(float deltaTime)
        {
            var events = new List<BattleEvent>();
            if (IsOver) return events;

            _elapsed += deltaTime;

            foreach (var c in PlayerTeam)
                if (c.IsAlive && _cooldowns[c] > 0f)
                    _cooldowns[c] = System.Math.Max(0f, _cooldowns[c] - deltaTime);

            foreach (var c in PlayerTeam.Concat(EnemyTeam))
            {
                if (!c.IsAlive) { _readiness[c] = 0f; continue; }
                if (_pending.ContainsKey(c)) continue; // 확정돼 다가가는 중엔 가득 찬 채로 고정
                float fillPerSecond = (BaseFillPerSecond + c.Stats.MoveSpeed * FillPerSpeedPerSecond) * _speedJitter[c];
                _readiness[c] = System.Math.Min(MaxReadiness, _readiness[c] + fillPerSecond * deltaTime);
            }

            // 플레이어 쪽: 게이지가 다 찼는데 아직 확정 안 된 유닛은 즉시 기본공격을 확정한다(여러 명 동시 가능).
            foreach (var c in PlayerTeam)
                if (c.IsAlive && !_pending.ContainsKey(c) && _readiness[c] >= MaxReadiness)
                    Commit(c, false, ChooseBasicAttackTarget(c));

            // 적 쪽은 그대로 즉시 자동 처리(접근 연출은 UI가 이벤트를 보고 반응적으로 재생).
            events.AddRange(ResolveReadyEnemies());

            // 확정된 행동(기본공격·스킬)들을 진행시킨다.
            if (_pending.Count > 0)
            {
                foreach (var actor in _pending.Keys.ToList())
                {
                    var pending = _pending[actor];
                    pending.Timer -= deltaTime;
                    if (pending.Timer <= 0f)
                    {
                        _pending.Remove(actor);
                        events.AddRange(ResolveAction(actor, pending.UseSkill, pending.Target));
                    }
                }
            }

            return events;
        }

        private Combatant PickRandomAliveEnemy()
        {
            var alive = EnemyTeam.Where(c => c.IsAlive).ToList();
            return alive.Count == 0 ? null : alive[_rng.Next(alive.Count)];
        }

        /// <summary>기본공격 자동 대상 선정: 붙어서 싸우던 대상이 아직 살아있으면 계속 그 대상, 없으면 새로 랜덤.</summary>
        private Combatant ChooseBasicAttackTarget(Combatant actor) => EngagedTargetOf(actor) ?? PickRandomAliveEnemy();

        private List<BattleEvent> ResolveReadyEnemies()
        {
            var events = new List<BattleEvent>();
            foreach (var enemy in EnemyTeam.Where(c => c.IsAlive && _readiness[c] >= MaxReadiness).ToList())
            {
                _readiness[enemy] = 0f;
                var target = CombatResolver.PickTarget(PlayerTeam, _rng);
                if (target == null) continue;

                foreach (var passive in enemy.Passives)
                    passive.OnRoundStart(enemy, ++ActionCount, _rng);

                var evt = CombatResolver.ResolveAttack(enemy, target, ActionCount, PlayerTeam, _rng);
                events.Add(evt);
                Log.Add(evt);
            }
            return events;
        }

        /// <summary>선딜레이가 끝나 실제로 타격을 적용한다(쿨타임 소비·데미지·로그 전부 여기서).
        /// 기본공격만 게이지를 0으로 되돌린다(스킬은 게이지와 무관한 별개 자원이라 건드리지 않음).</summary>
        private List<BattleEvent> ResolveAction(Combatant actor, bool useSkill, Combatant target)
        {
            var events = new List<BattleEvent>();
            if (actor == null || !actor.IsAlive) return events;

            // 다가가는 사이(선딜레이) 대상이 다른 공격 등으로 죽었으면 - 기본공격은 새 대상으로 돌리고, 스킬은 불발시킨다.
            if (target != null && !target.IsAlive)
                target = useSkill ? null : PickRandomAliveEnemy();

            foreach (var passive in actor.Passives)
                passive.OnRoundStart(actor, ++ActionCount, _rng);

            if (useSkill)
            {
                var skill = SkillOf(actor);
                if (skill != null && CooldownRemaining(actor) <= 0f && target != null)
                {
                    events.AddRange(skill.Execute(actor, target, PlayerTeam, EnemyTeam, ActionCount, _rng));
                    _cooldowns[actor] = skill.CooldownSeconds;
                }
            }
            else if (actor.Passives.Any(p => p.TryTriggerAoeAttack(_rng)))
            {
                foreach (var enemy in EnemyTeam.Where(c => c.IsAlive).ToList())
                    events.Add(CombatResolver.ResolveAttack(actor, enemy, ActionCount, EnemyTeam, _rng));
            }
            else if (target != null)
            {
                events.Add(CombatResolver.ResolveAttack(actor, target, ActionCount, EnemyTeam, _rng));
                _engagedTarget[actor] = target; // 다음 기본공격도 이 대상이 죽기 전까지 계속 같은 대상에게.
            }

            Log.AddRange(events);
            if (!useSkill) _readiness[actor] = 0f;
            return events;
        }
    }
}
