using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>
    /// 직접전투(ATB 실시간 전투)의 상태 기계. 유닛마다 이동속도 기반 "준비 게이지"가 실시간으로 차오르다가
    /// 다 차면 행동한다 - 적은 즉시 자동으로, 플레이어 유닛은 WaitingPlayerActor에 채워 입력을 기다린다
    /// (그 사이에도 다른 유닛의 게이지는 계속 참 - 이것이 턴제와 다른 "실시간" 핵심).
    /// AutoBattleSimulator와 같은 핵심 규칙(CombatResolver)을 공유하되, 턴 순서 대신 게이지로 진행한다.
    /// Unity 타입에 의존하지 않는 순수 C# 클래스 - BattleUIController가 매 프레임 Tick을 호출해 화면에 반영한다.
    /// </summary>
    public class ManualBattleSession
    {
        private const float MaxReadiness = 100f;

        // 게이지 충전 속도(%/초) = BaseFillPerSecond + MoveSpeed × FillPerSpeedPerSecond.
        // 이동속도 4(전사 기본)면 약 2.8초, 14(암살자)면 약 1.3초에 한 번 행동 - 감으로 정한 값, 체감 보고 조정.
        private const float BaseFillPerSecond = 20f;
        private const float FillPerSpeedPerSecond = 4f;

        private const float MaxBattleSeconds = 120f; // 턴제의 maxRounds에 해당하는 안전장치(무승부 처리)

        // 플레이어 유닛이 준비되면 이 시간 동안 스킬을 쓸 기회를 준다 - 그 안에 스킬을 쓰지 않으면
        // 기본공격이 랜덤 대상에게 자동으로 나간다(적과 동일하게 자동, 스킬만 직접 개입).
        private const float AutoAttackDelay = 2f;

        public List<Combatant> PlayerTeam { get; }
        public List<Combatant> EnemyTeam { get; }
        public List<BattleEvent> Log { get; } = new List<BattleEvent>();

        public bool IsOver => !PlayerTeam.Any(c => c.IsAlive) || !EnemyTeam.Any(c => c.IsAlive) || _elapsed >= MaxBattleSeconds;
        public BattleOutcome Outcome => CombatResolver.DetermineOutcome(PlayerTeam, EnemyTeam);
        public int ActionCount { get; private set; }

        /// <summary>지금 "스킬을 쓸지 말지" 기회가 주어진 플레이어 유닛(없으면 null). 시간 안에 스킬을 고르지
        /// 않으면 기본공격이 랜덤 대상에게 자동으로 나간다. 스킬을 고르는 중(Deciding)이면 시간이 멈춘다.</summary>
        public Combatant WaitingPlayerActor { get; private set; }

        /// <summary>true면 플레이어가 스킬 대상을 고르는 중 - 자동공격 타이머를 멈춘다(UI가 설정).</summary>
        public bool Deciding { get; set; }

        /// <summary>자동공격까지 남은 시간(초). WaitingPlayerActor가 없으면 0.</summary>
        public float AutoAttackCountdown => WaitingPlayerActor != null ? System.Math.Max(0f, _waitTimer) : 0f;

        private readonly Dictionary<Combatant, SkillBase> _skills;
        private readonly Dictionary<Combatant, float> _readiness = new Dictionary<Combatant, float>();
        private readonly Dictionary<Combatant, float> _cooldowns = new Dictionary<Combatant, float>();
        private readonly System.Random _rng;
        private float _elapsed;
        private float _waitTimer;

        public ManualBattleSession(List<Combatant> playerTeam, Dictionary<Combatant, SkillBase> skills, List<Combatant> enemyTeam, System.Random rng)
        {
            PlayerTeam = playerTeam;
            EnemyTeam = enemyTeam;
            _skills = skills;
            _rng = rng;

            foreach (var c in playerTeam.Concat(enemyTeam))
                _readiness[c] = 0f;
            foreach (var c in playerTeam)
                _cooldowns[c] = 0f;

            CombatResolver.RaiseBattleStart(PlayerTeam, EnemyTeam);
        }

        public SkillBase SkillOf(Combatant c) => _skills.TryGetValue(c, out var s) ? s : null;
        public float CooldownRemaining(Combatant c) => _cooldowns.TryGetValue(c, out var v) ? v : 0f;
        public float ReadinessOf(Combatant c) => _readiness.TryGetValue(c, out var v) ? v : 0f;
        public float ReadinessRatioOf(Combatant c) => ReadinessOf(c) / MaxReadiness;

        /// <summary>매 프레임 호출. 게이지를 채우고 쿨타임을 줄이며, 준비된 적은 즉시 자동으로 행동시킨다.
        /// 플레이어 유닛이 새로 준비되면 WaitingPlayerActor에 채워 입력을 기다리게 한다(최대 한 명씩 순서대로).
        /// 전투 중 발생한 이벤트 목록을 돌려준다(연출·로그 표시용, 없으면 빈 리스트).</summary>
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
                if (c == WaitingPlayerActor) continue; // 입력 대기 중엔 가득 찬 채로 고정
                float fillPerSecond = BaseFillPerSecond + c.Stats.MoveSpeed * FillPerSpeedPerSecond;
                _readiness[c] = System.Math.Min(MaxReadiness, _readiness[c] + fillPerSecond * deltaTime);
            }

            if (WaitingPlayerActor == null)
            {
                var readyPlayer = PlayerTeam.Where(c => c.IsAlive && _readiness[c] >= MaxReadiness)
                    .OrderByDescending(c => _readiness[c]).FirstOrDefault();
                if (readyPlayer != null)
                {
                    WaitingPlayerActor = readyPlayer;
                    Deciding = false;
                    var skill = SkillOf(readyPlayer);
                    // 쓸 수 있는 스킬이 아예 없으면(없음/쿨타임 중) 고민할 게 없으니 기다리지 않고 바로 자동공격.
                    _waitTimer = (skill == null || CooldownRemaining(readyPlayer) > 0f) ? 0f : AutoAttackDelay;
                }
            }

            events.AddRange(ResolveReadyEnemies());

            if (WaitingPlayerActor != null && !WaitingPlayerActor.IsAlive)
            {
                // 대기하는 사이 쓰러짐(적 자동 공격에 당함) - 대기 취소.
                _readiness[WaitingPlayerActor] = 0f;
                WaitingPlayerActor = null;
            }
            else if (WaitingPlayerActor != null && !Deciding)
            {
                _waitTimer -= deltaTime;
                if (_waitTimer <= 0f)
                    events.AddRange(PlayerAct(false, PickRandomAliveEnemy()));
            }

            return events;
        }

        private Combatant PickRandomAliveEnemy()
        {
            var alive = EnemyTeam.Where(c => c.IsAlive).ToList();
            return alive.Count == 0 ? null : alive[_rng.Next(alive.Count)];
        }

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

        /// <summary>플레이어 입력 확정(WaitingPlayerActor가 있을 때만 동작). 처리 후 게이지를 비우고
        /// 다음 유닛이 대기자가 될 수 있도록 WaitingPlayerActor를 비운다.</summary>
        public List<BattleEvent> PlayerAct(bool useSkill, Combatant target)
        {
            var actor = WaitingPlayerActor;
            var events = new List<BattleEvent>();
            if (actor == null) return events;

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
            }

            Log.AddRange(events);
            _readiness[actor] = 0f;
            WaitingPlayerActor = null;
            return events;
        }
    }
}
