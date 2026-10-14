using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GN3.Combat
{
    /// <summary>필드 전투에 선 유닛 하나(위치·이동·공격 대기·스킬 쿨타임). 상태는 FieldBattleSession만 바꾼다.</summary>
    public class FieldUnit
    {
        public Combatant Combatant { get; }
        public bool IsPlayerSide { get; }
        public bool IsLeader { get; }
        /// <summary>근접(전사·암살자·몬스터)은 붙어서, 원거리(궁수·마법사·힐러 등)는 떨어져서 때린다.</summary>
        public bool IsMelee { get; }
        public SkillBase Skill { get; }

        public Vector2 Position { get; internal set; }
        /// <summary>바라보는 쪽: 1 오른쪽, -1 왼쪽.</summary>
        public float Facing { get; internal set; }
        /// <summary>이번 Tick에 움직였는지(걷기 애니메이션용).</summary>
        public bool IsMoving { get; internal set; }

        public float MoveSpeed { get; }      // 필드 단위/초
        public float AttackInterval { get; } // 기본공격 사이 초
        public float AttackTimer { get; internal set; } // AttackInterval까지 차면 사거리 안의 대상을 때린다
        public float Cooldown { get; internal set; }    // 스킬 남은 쿨타임(초)

        /// <summary>기본공격 대상(살아 있는 동안 계속 붙는다).</summary>
        public FieldUnit Target { get; internal set; }
        /// <summary>분대장 이동 명령 지점(도착하면 null).</summary>
        public Vector2? MoveGoal { get; internal set; }
        /// <summary>쓰기로 정한 스킬의 대상 - 사거리 밖이면 다가간 뒤 쓴다.</summary>
        public FieldUnit SkillTarget { get; internal set; }

        public bool IsAlive => Combatant.IsAlive;
        public float AttackRange => IsMelee ? FieldBattleSession.MeleeRange : FieldBattleSession.RangedRange;
        public float AttackReadyRatio => AttackInterval > 0f ? Math.Min(1f, AttackTimer / AttackInterval) : 1f;

        internal FieldUnit(Combatant combatant, bool isPlayerSide, bool isLeader, bool isMelee, SkillBase skill,
            Vector2 position, float moveSpeed, float attackInterval)
        {
            Combatant = combatant;
            IsPlayerSide = isPlayerSide;
            IsLeader = isLeader;
            IsMelee = isMelee;
            Skill = skill;
            Position = position;
            Facing = isPlayerSide ? 1f : -1f;
            MoveSpeed = moveSpeed;
            AttackInterval = attackInterval;
        }
    }

    /// <summary>
    /// 직접 진행 전투(필드형 실시간). 아군은 왼쪽, 적은 오른쪽에서 시작해 서로 다가가 싸운다.
    /// 분대장 한 명만 플레이어 명령(이동·공격·스킬)으로 움직이고, 나머지 아군과 적은 AI가 대상을 골라 쫓아가
    /// 때리며, 아군 AI는 스킬도 알아서 쓴다. 피해·회피·패시브는 자동 전투와 같은 CombatResolver를 쓴다.
    /// Unity 타입에 의존하지 않는 순수 C# - BattleUIController가 매 프레임 Tick을 부르고 Position을 화면에 옮긴다.
    /// </summary>
    public class FieldBattleSession
    {
        // 필드 크기(UI가 같은 크기의 영역에 1:1로 그린다).
        public const float FieldWidth = 1760f;
        public const float FieldHeight = 560f;
        private const float EdgeMargin = 40f;
        /// <summary>배치 단계에서 아군을 놓을 수 있는 구역의 오른쪽 끝(왼쪽 끝은 필드 가장자리).</summary>
        public const float DeployMaxX = 620f;

        public const float MeleeRange = 90f;
        public const float RangedRange = 420f;
        private const float ApproachFraction = 0.85f; // 사거리의 이만큼 안까지 다가가 멈춘다(경계에서 들락날락하지 않게)
        private const float SeparationRadius = 60f;   // 이보다 가까우면 서로 밀어낸다(겹쳐 서지 않게)

        // 이동 속도 = Base + 이동속도 능력치 × PerSpeed (필드 단위/초). 이동속도 4(전사)면 132, 14(암살자)면 212.
        private const float BaseMoveSpeed = 100f;
        private const float MoveSpeedPerStat = 8f;

        // 공격 간격 = 100 / (10 + 이동속도 × 2) × AttackPace 초 - 예전 ATB 게이지와 같은 비율을 쓰되 실시간 조작에 맞게 빠르게.
        // 이동속도 4면 약 3.4초, 14면 약 1.6초.
        private const float BaseFillPerSecond = 10f;
        private const float FillPerSpeedPerSecond = 2f;
        private const float AttackPace = 0.6f;
        private const float SpeedJitterRange = 0.15f;      // 유닛마다 ±15% 흔들어 한꺼번에 몰려 때리지 않게
        private const float StartingTimerJitter = 0.4f;    // 시작할 때 공격 대기를 0~40% 채워 흩어 둔다

        private const float MaxBattleSeconds = 180f; // 라운드당 안전장치(넘기면 무승부 = 실패)
        /// <summary>라운드를 깨고 다음 적이 나오기까지(게임 초). 그동안 대원은 서 있던 자리에서 쉬고 분대장은 움직일 수 있다.</summary>
        public const float WaveBreakSeconds = 2.5f;

        // 직접 전투는 위치(원거리는 뒤에서 안전)와 분대장 조작 덕에 자동 전투보다 유리하다 → 적을 이만큼 세게.
        // 분대장을 가만히 둔 채 이기는 비율이 자동 전투와 비슷해지게 맞춘 값(조작을 잘하면 더 잘 이긴다).
        public const float EnemyHealthMultiplier = 1.3f;
        public const float EnemyAttackMultiplier = 1.15f;
        public const float EnemyDefenseMultiplier = 1.0f;

        public List<FieldUnit> Units { get; } = new List<FieldUnit>();
        public List<Combatant> PlayerTeam { get; }
        /// <summary>지금 라운드의 적.</summary>
        public List<Combatant> EnemyTeam { get; private set; }
        /// <summary>지금까지 나온 모든 라운드의 적(결과용).</summary>
        public List<Combatant> AllEnemies { get; } = new List<Combatant>();
        public FieldUnit Leader { get; }
        public List<BattleEvent> Log { get; } = new List<BattleEvent>();
        public int ActionCount { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>지금 라운드(0부터)와 전체 라운드 수.</summary>
        public int CurrentWave { get; private set; }
        public int WaveCount => _waves.Count;
        /// <summary>라운드를 깨고 다음 적을 기다리는 중.</summary>
        public bool IsBetweenWaves => _breakTimer > 0f;
        /// <summary>다 잡은 라운드 수.</summary>
        public int WavesCleared => CurrentWave + (EnemyTeam.Any(c => c.IsAlive) ? 0 : 1);

        private bool PlayersAlive => PlayerTeam.Any(c => c.IsAlive);
        private bool LastWave => CurrentWave >= WaveCount - 1;

        public bool IsOver => !PlayersAlive || (LastWave && !EnemyTeam.Any(c => c.IsAlive)) || Elapsed >= MaxBattleSeconds * WaveCount;
        public BattleOutcome Outcome => CombatResolver.DetermineOutcome(PlayerTeam, EnemyTeam);

        /// <summary>스킬이 쓰인 순간(UI가 스킬 이름을 띄운다).</summary>
        public event Action<FieldUnit, SkillBase> OnSkillUsed;
        /// <summary>라운드를 깼다(깬 라운드 번호, 0부터). WaveBreakSeconds 뒤에 다음 라운드가 시작된다.</summary>
        public event Action<int> OnWaveCleared;
        /// <summary>다음 라운드 시작(라운드 번호, 치운 적 유닛, 새로 나온 적 유닛).</summary>
        public event Action<int, List<FieldUnit>, List<FieldUnit>> OnWaveStarted;

        private readonly List<List<Combatant>> _waves;
        private float _breakTimer;

        private readonly Random _rng;
        private readonly Dictionary<Combatant, FieldUnit> _byCombatant = new Dictionary<Combatant, FieldUnit>();

        /// <param name="playerTeam">아군(순서대로 배치). leaderIndex번째가 분대장.</param>
        /// <param name="playerMelee">아군 각자가 근접인지(playerTeam과 같은 순서).</param>
        /// <param name="playerSkills">아군 각자의 스킬(없으면 null).</param>
        /// <param name="enemyWaves">라운드별 적(순서대로). 체력은 라운드 사이에 그대로 이어진다.</param>
        public FieldBattleSession(List<Combatant> playerTeam, IList<bool> playerMelee, IList<SkillBase> playerSkills, int leaderIndex,
            IList<List<Combatant>> enemyWaves, Random rng)
        {
            PlayerTeam = playerTeam;
            _waves = enemyWaves.Select(w => w.Select(Strengthen).ToList()).ToList();
            _rng = rng;

            // 아군: 근접은 앞줄(오른쪽), 원거리는 뒷줄. 줄마다 위아래로 고르게 벌린다.
            var front = Enumerable.Range(0, playerTeam.Count).Where(i => playerMelee[i]).ToList();
            var back = Enumerable.Range(0, playerTeam.Count).Where(i => !playerMelee[i]).ToList();
            var spawn = new Vector2[playerTeam.Count];
            PlaceColumn(front, 400f, spawn);
            PlaceColumn(back, 230f, spawn);
            for (int i = 0; i < playerTeam.Count; i++)
            {
                var unit = CreateUnit(playerTeam[i], true, i == leaderIndex, playerMelee[i], playerSkills[i], spawn[i]);
                if (unit.IsLeader) Leader = unit;
            }

            SpawnEnemies(0);
            CombatResolver.RaiseBattleStart(PlayerTeam, EnemyTeam);
        }

        /// <summary>waveIndex번째 라운드 적을 오른쪽에 두 줄로 엇갈려 세운다(몬스터는 모두 근접). 새 유닛을 돌려준다.</summary>
        private List<FieldUnit> SpawnEnemies(int waveIndex)
        {
            CurrentWave = waveIndex;
            EnemyTeam = _waves[waveIndex];
            AllEnemies.AddRange(EnemyTeam);
            var added = new List<FieldUnit>();
            for (int i = 0; i < EnemyTeam.Count; i++)
            {
                float x = FieldWidth - 260f - (i % 2) * 120f;
                float y = Spread(i, EnemyTeam.Count);
                added.Add(CreateUnit(EnemyTeam[i], false, false, true, null, new Vector2(x, y)));
            }
            return added;
        }

        /// <summary>쓰러진 이번 라운드 적을 치우고 다음 라운드 적을 세운다. 아군은 서 있던 자리·체력 그대로.</summary>
        private void StartNextWave()
        {
            var removed = Units.Where(u => !u.IsPlayerSide).ToList();
            foreach (var u in removed)
            {
                Units.Remove(u);
                _byCombatant.Remove(u.Combatant);
            }
            foreach (var u in Units)
            {
                u.Target = null;
                u.SkillTarget = null;
            }
            _orderedTarget = null;

            var added = SpawnEnemies(CurrentWave + 1);
            foreach (var c in EnemyTeam)
                foreach (var passive in c.Passives)
                    passive.OnBattleStart(c, EnemyTeam, PlayerTeam);
            OnWaveStarted?.Invoke(CurrentWave, removed, added);
        }

        /// <summary>직접 전투용으로 적 능력치를 키운 새 유닛(이름·패시브 그대로).</summary>
        private static Combatant Strengthen(Combatant e)
        {
            var s = e.Stats;
            var stats = new CombatStats(
                attack: (int)Math.Round(s.Attack * EnemyAttackMultiplier),
                defense: (int)Math.Round(s.Defense * EnemyDefenseMultiplier),
                maxHealth: Math.Max(1, (int)Math.Round(s.MaxHealth * EnemyHealthMultiplier)),
                moveSpeed: s.MoveSpeed,
                magic: (int)Math.Round(s.Magic * EnemyAttackMultiplier),
                holy: s.Holy);
            return new Combatant(e.Name, stats, e.Passives);
        }

        private void PlaceColumn(List<int> indices, float x, Vector2[] spawn)
        {
            for (int k = 0; k < indices.Count; k++)
                spawn[indices[k]] = new Vector2(x, Spread(k, indices.Count));
        }

        /// <summary>count명을 필드 세로로 고르게 벌린 k번째 y.</summary>
        private static float Spread(int k, int count)
        {
            float usable = FieldHeight - 2f * 70f;
            return count <= 1 ? FieldHeight / 2f : 70f + usable * k / (count - 1);
        }

        private FieldUnit CreateUnit(Combatant c, bool playerSide, bool leader, bool melee, SkillBase skill, Vector2 position)
        {
            float jitter = 1f - SpeedJitterRange + (float)_rng.NextDouble() * (SpeedJitterRange * 2f);
            float fill = (BaseFillPerSecond + c.Stats.MoveSpeed * FillPerSpeedPerSecond) * jitter;
            float interval = 100f / Math.Max(1f, fill) * AttackPace;
            float speed = BaseMoveSpeed + c.Stats.MoveSpeed * MoveSpeedPerStat;
            var unit = new FieldUnit(c, playerSide, leader, melee, skill, position, speed, interval)
            {
                AttackTimer = interval * (float)_rng.NextDouble() * StartingTimerJitter,
            };
            Units.Add(unit);
            _byCombatant[c] = unit;
            return unit;
        }

        public FieldUnit UnitOf(Combatant c) => c != null && _byCombatant.TryGetValue(c, out var u) ? u : null;

        // ---------- 분대장 명령 ----------

        private bool LeaderCanAct => HasStarted && Leader != null && Leader.IsAlive && !IsOver;

        // 플레이어가 직접 찍은 공격 대상(쫓아간다). 명령 없이 알아서 고른 대상은 사거리를 벗어나면 놓는다.
        private FieldUnit _orderedTarget;

        /// <summary>필드의 한 지점으로 이동(공격·스킬 예약은 취소, 도착하면 사거리 안의 적을 알아서 때린다).</summary>
        public void OrderMove(Vector2 point)
        {
            if (!LeaderCanAct) return;
            Leader.MoveGoal = ClampToField(point);
            Leader.Target = null;
            _orderedTarget = null;
            Leader.SkillTarget = null;
        }

        /// <summary>이 적을 쫓아가 계속 공격.</summary>
        public void OrderAttack(FieldUnit enemy)
        {
            if (!LeaderCanAct || enemy == null || enemy.IsPlayerSide || !enemy.IsAlive) return;
            Leader.Target = enemy;
            _orderedTarget = enemy;
            Leader.MoveGoal = null;
            Leader.SkillTarget = null;
        }

        /// <summary>분대장 스킬이 지금 쓸 수 있는 상태인지(쿨타임·생존).</summary>
        public bool LeaderSkillReady => LeaderCanAct && Leader.Skill != null && Leader.Cooldown <= 0f;

        /// <summary>스킬을 이 대상에게 쓴다(사거리 밖이면 다가가서). 대상 종류가 맞지 않거나 쓸 수 없으면 false.</summary>
        public bool OrderSkill(FieldUnit target)
        {
            if (!LeaderSkillReady || target == null || !target.IsAlive) return false;
            bool wantsAlly = Leader.Skill.TargetType == SkillTargetType.Ally;
            if (target.IsPlayerSide != wantsAlly) return false;
            Leader.SkillTarget = target;
            Leader.MoveGoal = null;
            if (!wantsAlly) Leader.Target = _orderedTarget = target; // 스킬 뒤에도 그 적에게 붙어 기본공격을 이어 간다
            return true;
        }

        // ---------- 배치 ----------

        /// <summary>배치 단계가 끝나고 전투가 시작됐는지. 시작 전엔 Tick이 아무것도 하지 않는다.</summary>
        public bool HasStarted { get; private set; }

        public void Begin() => HasStarted = true;

        /// <summary>배치 단계에서 아군을 옮긴다(배치 구역 안으로 맞춤). 전투가 시작된 뒤엔 무시.</summary>
        public void PlaceForDeploy(FieldUnit unit, Vector2 point)
        {
            if (HasStarted || unit == null || !unit.IsPlayerSide) return;
            unit.Position = ClampToDeploy(point);
            unit.Facing = 1f;
        }

        public static Vector2 ClampToDeploy(Vector2 p) => new Vector2(
            Math.Clamp(p.X, EdgeMargin, DeployMaxX),
            Math.Clamp(p.Y, EdgeMargin, FieldHeight - EdgeMargin));

        // ---------- 진행 ----------

        /// <summary>매 프레임 호출. 이번 프레임에 일어난 전투 이벤트(피해·회복)를 돌려준다.</summary>
        public List<BattleEvent> Tick(float dt)
        {
            var events = new List<BattleEvent>();
            if (!HasStarted || IsOver) return events;
            Elapsed += dt;

            foreach (var u in Units)
            {
                u.IsMoving = false;
                if (!u.IsAlive)
                {
                    u.Target = null;
                    u.SkillTarget = null;
                    u.MoveGoal = null;
                    continue;
                }
                u.AttackTimer = Math.Min(u.AttackInterval, u.AttackTimer + dt);
                if (u.Cooldown > 0f) u.Cooldown = Math.Max(0f, u.Cooldown - dt);
            }

            foreach (var u in Units.ToList())
            {
                if (!u.IsAlive || IsOver) continue;
                if (u.IsLeader) ThinkLeader(u);
                else if (u.IsPlayerSide) ThinkAlly(u);
                else ThinkEnemy(u);
                Act(u, dt, events);
            }

            Separate(dt);
            UpdateWaveBreak(dt);
            return events;
        }

        /// <summary>이번 라운드 적을 다 잡았고 다음 라운드가 남았으면 잠깐 쉬었다가 다음 적을 세운다.</summary>
        private void UpdateWaveBreak(float dt)
        {
            if (!PlayersAlive || LastWave || EnemyTeam.Any(c => c.IsAlive)) return;
            if (_breakTimer <= 0f)
            {
                _breakTimer = WaveBreakSeconds;
                OnWaveCleared?.Invoke(CurrentWave);
                return;
            }
            _breakTimer -= dt;
            if (_breakTimer <= 0f)
            {
                _breakTimer = 0f;
                StartNextWave();
            }
        }

        /// <summary>분대장: 명령이 없을 때는 사거리 안의 적만 알아서 때린다(쫓아가지는 않음 - 이동은 플레이어 몫).</summary>
        private void ThinkLeader(FieldUnit u)
        {
            if (u.SkillTarget != null && !u.SkillTarget.IsAlive) u.SkillTarget = null;
            if (u.Target != null && !u.Target.IsAlive) u.Target = null;
            if (u.Target != null && u.Target != _orderedTarget && Distance(u, u.Target) > u.AttackRange) u.Target = null;
            if (u.Target == null && u.MoveGoal == null && u.SkillTarget == null)
            {
                var near = Nearest(u, Enemies(u));
                if (near != null && Distance(u, near) <= u.AttackRange) u.Target = near;
            }
        }

        /// <summary>아군 AI: 가장 가까운 적에게 붙어 싸우고(때리는 중이면 그 대상 유지), 스킬은 쿨타임이 돌면 알아서.</summary>
        private void ThinkAlly(FieldUnit u)
        {
            RetargetNearest(u);
            if (u.SkillTarget != null && !u.SkillTarget.IsAlive) u.SkillTarget = null;
            if (u.Skill == null || u.Cooldown > 0f || u.SkillTarget != null) return;

            // 자동 전투와 같은 판단(SkillAI). 공격 스킬은 사거리 안의 적만, 치유는 어디 있든(다가가서 쓴다).
            var inRange = Enemies(u).Where(e => Distance(u, e) <= u.AttackRange).Select(e => e.Combatant);
            u.SkillTarget = UnitOf(SkillAI.ChooseTarget(u.Skill, PlayerTeam, inRange, u.Target?.Combatant));
        }

        /// <summary>적 AI: 도발 중인 아군이 있으면 그쪽, 아니면 가장 가까운 아군.</summary>
        private void ThinkEnemy(FieldUnit u)
        {
            var taunting = Enemies(u).Where(e => e.Combatant.IsTaunting).ToList();
            if (taunting.Count > 0)
            {
                if (u.Target == null || !u.Target.IsAlive || !u.Target.Combatant.IsTaunting)
                    u.Target = Nearest(u, taunting);
                return;
            }
            RetargetNearest(u);
        }

        /// <summary>대상이 없거나 죽었거나, 아직 사거리 밖인데 더 가까운 적이 있으면 가장 가까운 적으로 바꾼다.</summary>
        private void RetargetNearest(FieldUnit u)
        {
            if (u.Target != null && u.Target.IsAlive && Distance(u, u.Target) <= u.AttackRange) return;
            var near = Nearest(u, Enemies(u));
            if (near == null) { u.Target = null; return; }
            if (u.Target == null || !u.Target.IsAlive || Distance(u, near) < Distance(u, u.Target) * 0.8f)
                u.Target = near;
        }

        /// <summary>정해 둔 의도(스킬 → 공격 → 이동 순)대로 다가가거나 행동한다.</summary>
        private void Act(FieldUnit u, float dt, List<BattleEvent> events)
        {
            if (u.SkillTarget != null)
            {
                var t = u.SkillTarget;
                if (t == u || Distance(u, t) <= u.AttackRange)
                {
                    Face(u, t.Position);
                    UseSkill(u, t, events);
                }
                else MoveToward(u, t.Position, u.AttackRange * ApproachFraction, dt);
                return;
            }

            if (u.Target != null)
            {
                var t = u.Target;
                if (Distance(u, t) <= u.AttackRange)
                {
                    Face(u, t.Position);
                    if (u.AttackTimer >= u.AttackInterval) BasicAttack(u, t, events);
                }
                else MoveToward(u, t.Position, u.AttackRange * ApproachFraction, dt);
                return;
            }

            if (u.MoveGoal.HasValue)
            {
                if (MoveToward(u, u.MoveGoal.Value, 0f, dt)) u.MoveGoal = null;
            }
        }

        private void BasicAttack(FieldUnit u, FieldUnit target, List<BattleEvent> events)
        {
            foreach (var passive in u.Combatant.Passives)
                passive.OnRoundStart(u.Combatant, ++ActionCount, _rng);

            var targetTeam = u.IsPlayerSide ? EnemyTeam : PlayerTeam;
            var produced = new List<BattleEvent>();
            if (u.Combatant.Passives.Any(p => p.TryTriggerAoeAttack(_rng)))
            {
                foreach (var e in targetTeam.Where(c => c.IsAlive).ToList())
                    produced.Add(CombatResolver.ResolveAttack(u.Combatant, e, ActionCount, targetTeam, _rng));
            }
            else
            {
                produced.Add(CombatResolver.ResolveAttack(u.Combatant, target.Combatant, ActionCount, targetTeam, _rng));
            }
            u.AttackTimer = 0f;
            events.AddRange(produced);
            Log.AddRange(produced);
        }

        private void UseSkill(FieldUnit u, FieldUnit target, List<BattleEvent> events)
        {
            u.SkillTarget = null;
            if (u.Skill == null || u.Cooldown > 0f || !target.IsAlive) return;

            foreach (var passive in u.Combatant.Passives)
                passive.OnRoundStart(u.Combatant, ++ActionCount, _rng);

            var allies = u.IsPlayerSide ? PlayerTeam : EnemyTeam;
            var enemies = u.IsPlayerSide ? EnemyTeam : PlayerTeam;
            var produced = u.Skill.Execute(u.Combatant, target.Combatant, allies, enemies, ActionCount, _rng);
            u.Cooldown = u.Skill.CooldownSeconds;
            OnSkillUsed?.Invoke(u, u.Skill);
            events.AddRange(produced);
            Log.AddRange(produced);
        }

        // ---------- 이동 ----------

        /// <summary>목표 지점에서 stopDistance 안까지 다가간다. 이미 도착했으면 true.</summary>
        private bool MoveToward(FieldUnit u, Vector2 goal, float stopDistance, float dt)
        {
            var delta = goal - u.Position;
            float dist = delta.Length();
            if (dist <= stopDistance + 0.5f) return true;

            float step = Math.Min(u.MoveSpeed * dt, dist - stopDistance);
            u.Position = ClampToField(u.Position + delta / dist * step);
            u.IsMoving = true;
            Face(u, goal);
            return dist - step <= stopDistance + 0.5f;
        }

        private static void Face(FieldUnit u, Vector2 point)
        {
            float dx = point.X - u.Position.X;
            if (Math.Abs(dx) > 2f) u.Facing = dx > 0f ? 1f : -1f;
        }

        /// <summary>살아 있는 유닛끼리 너무 붙으면 서로 밀어낸다(같은 자리에 겹쳐 서지 않게).</summary>
        private void Separate(float dt)
        {
            var alive = Units.Where(u => u.IsAlive).ToList();
            float strength = Math.Min(1f, dt * 8f);
            for (int i = 0; i < alive.Count; i++)
            for (int j = i + 1; j < alive.Count; j++)
            {
                var a = alive[i];
                var b = alive[j];
                var delta = b.Position - a.Position;
                float dist = delta.Length();
                if (dist >= SeparationRadius) continue;
                var dir = dist > 0.01f ? delta / dist : new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f);
                if (dir.LengthSquared() < 0.0001f) dir = Vector2.UnitY;
                dir = Vector2.Normalize(dir);
                var push = dir * ((SeparationRadius - dist) * 0.5f * strength);
                a.Position = ClampToField(a.Position - push);
                b.Position = ClampToField(b.Position + push);
            }
        }

        public static Vector2 ClampToField(Vector2 p) => new Vector2(
            Math.Clamp(p.X, EdgeMargin, FieldWidth - EdgeMargin),
            Math.Clamp(p.Y, EdgeMargin, FieldHeight - EdgeMargin));

        // ---------- 도우미 ----------

        private IEnumerable<FieldUnit> Enemies(FieldUnit u) => Units.Where(o => o.IsAlive && o.IsPlayerSide != u.IsPlayerSide);

        private static FieldUnit Nearest(FieldUnit u, IEnumerable<FieldUnit> candidates)
        {
            FieldUnit best = null;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                float d = Vector2.DistanceSquared(u.Position, c.Position);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        public static float Distance(FieldUnit a, FieldUnit b) => Vector2.Distance(a.Position, b.Position);
    }
}
