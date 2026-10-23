using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GN3.Combat
{
    /// <summary>분대장이 분대 전체에 내리는 명령(FieldBattleSession.OrderRally 등). 순서 = 화면 단축키 1~4.</summary>
    public enum SquadOrder { Rally, Retreat, Advance, Focus }

    /// <summary>필드 전투에 선 유닛 하나(위치·이동·공격 대기·스킬 쿨타임). 상태는 FieldBattleSession만 바꾼다.</summary>
    public class FieldUnit
    {
        public Combatant Combatant { get; }
        public bool IsPlayerSide { get; }
        /// <summary>자기 분대의 분대장(두 분대 전투면 둘). 지금 조종 중인 분대장은 FieldBattleSession.Leader.</summary>
        public bool IsLeader { get; }
        /// <summary>분대(0=1분대, 1=2분대). 적과 한 분대 전투는 0.</summary>
        public int Squad { get; internal set; }
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

        /// <summary>공격 동작(검격 애니메이션) 길이(초). UI가 그림에 맞춰 넣는다. 0이면 공격 직후에도 바로 움직인다.</summary>
        public float AttackAnimSeconds { get; set; }
        /// <summary>공격 동작이 끝날 때까지 남은 시간. 그동안은 이동·새 공격·스킬을 하지 않고 밀려나지도 않는다.</summary>
        public float BusyTimer { get; internal set; }
        public bool IsBusy => BusyTimer > 0f;
        /// <summary>기절(거목 엔트 포효)이 풀릴 때까지 남은 시간. 그동안은 생각·이동·공격·스킬을 하지 않고 공격 대기도 차지 않는다.</summary>
        public float StunTimer { get; internal set; }
        public bool IsStunned => StunTimer > 0f;
        /// <summary>이번 기절의 전체 길이(남은 시간 막대가 가득에서 줄어들게 — 포효 2초·해머 1초).</summary>
        public float StunTotal { get; internal set; }
        /// <summary>공격 시작부터 그림에서 공격이 닿는 순간까지(전투 초). UI가 그림에 맞춰 넣는다 — 해머 기절을 닿는 순간에 건다(0이면 바로).</summary>
        public float AttackHitDelay { get; set; }
        /// <summary>뿌리박은 엔트만: 다음 기절 포효까지 남은 시간.</summary>
        internal float RoarTimer { get; set; }

        /// <summary>기본공격 대상(살아 있는 동안 계속 붙는다).</summary>
        public FieldUnit Target { get; internal set; }
        /// <summary>분대장 이동 명령 지점(도착하면 null).</summary>
        public Vector2? MoveGoal { get; internal set; }
        /// <summary>쓰기로 정한 스킬의 대상 - 사거리 밖이면 다가간 뒤 쓴다.</summary>
        public FieldUnit SkillTarget { get; internal set; }
        /// <summary>플레이어가 끌어서 자리를 정해 준 대원(분대장 말고): 그 자리로 걸어간 뒤 지키며 사거리 안의 적만 때린다.
        /// 적 위에 끌어 놓아 공격을 시키면 풀린다.</summary>
        public bool HoldPosition { get; internal set; }
        /// <summary>플레이어가 찍어 준 공격 대상: 더 가까운 적이 생겨도 바꾸지 않는다(분대장 말고 대원용).</summary>
        internal bool TargetOrdered;

        public bool IsAlive => Combatant.IsAlive;
        public float AttackRange => IsMelee ? FieldBattleSession.MeleeRange : FieldBattleSession.RangedRange;
        /// <summary>몸 반지름: 사거리·밀어내기를 몸 가장자리부터 잰다. 보통 0, 큰 보스(거목 엔트)만 크다.</summary>
        public float BodyRadius { get; internal set; }
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
    /// 분대장은 플레이어 명령(이동·공격·스킬)으로 움직이고, 나머지 아군과 적은 AI가 대상을 골라 쫓아가
    /// 때리며, 아군 AI는 스킬도 알아서 쓴다. 다른 대원도 끌어 놓아 자리(OrderUnitMove)나 공격 대상(OrderUnitAttack)을 정해 줄 수 있다. 피해·회피·패시브는 자동 전투와 같은 CombatResolver를 쓴다.
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
        // 2026-10-16 전투 긴박감: 적은 공격 간격을 이만큼 짧게(더 자주 때린다). 이동 속도 능력치(회피·이동)는 그대로.
        // 자동 전투는 라운드제라 공격 속도가 없어 영향 없다.
        // 직접 전투 500판(전원 자동 조작): 중간 59→47% · 어려움 32→22%, 한 판 평균 71→58초.
        public const float EnemyAttackSpeedMultiplier = 1.3f;

        public List<FieldUnit> Units { get; } = new List<FieldUnit>();
        public List<Combatant> PlayerTeam { get; }
        /// <summary>지금 라운드의 적.</summary>
        public List<Combatant> EnemyTeam { get; private set; }
        /// <summary>지금까지 나온 모든 라운드의 적(결과용).</summary>
        public List<Combatant> AllEnemies { get; } = new List<Combatant>();
        /// <summary>지금 플레이어가 조종 중인 분대장. 두 분대 전투면 SwitchLeader(Tab)로 바꾸고, 쓰러지면 다른 분대장에게 넘어간다.</summary>
        public FieldUnit Leader { get; private set; }
        /// <summary>분대장들(분대 순서). 한 분대 전투면 하나.</summary>
        public List<FieldUnit> Leaders { get; } = new List<FieldUnit>();
        public bool HasTwoSquads => Leaders.Count > 1;
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
        /// <param name="squads">아군 각자의 분대(0·1, playerTeam과 같은 순서). 없으면 전원 1분대.</param>
        /// <param name="leaderIndex2">2분대 분대장 인덱스(두 분대 전투만, 없으면 -1).</param>
        public FieldBattleSession(List<Combatant> playerTeam, IList<bool> playerMelee, IList<SkillBase> playerSkills, int leaderIndex,
            IList<List<Combatant>> enemyWaves, Random rng, IList<int> squads = null, int leaderIndex2 = -1)
        {
            PlayerTeam = playerTeam;
            _waves = enemyWaves.Select(w => w.Select(Strengthen).ToList()).ToList();
            _rng = rng;

            // 아군: 근접은 앞줄(오른쪽), 원거리는 뒷줄. 줄마다 위아래로 고르게 벌린다.
            // 두 분대면 1분대는 위쪽 절반, 2분대는 아래쪽 절반에 따로 선다.
            int SquadAt(int i) => squads != null && i < squads.Count ? squads[i] : 0;
            bool twoSquads = leaderIndex2 >= 0 && Enumerable.Range(0, playerTeam.Count).Any(i => SquadAt(i) == 1);
            var spawn = new Vector2[playerTeam.Count];
            for (int s = 0; s < (twoSquads ? 2 : 1); s++)
            {
                var members = Enumerable.Range(0, playerTeam.Count).Where(i => !twoSquads || SquadAt(i) == s).ToList();
                float top = twoSquads ? (s == 0 ? 70f : FieldHeight / 2f + 30f) : 70f;
                float bottom = twoSquads ? (s == 0 ? FieldHeight / 2f - 30f : FieldHeight - 70f) : FieldHeight - 70f;
                PlaceColumn(members.Where(i => playerMelee[i]).ToList(), 400f, spawn, top, bottom);
                PlaceColumn(members.Where(i => !playerMelee[i]).ToList(), 230f, spawn, top, bottom);
            }
            for (int i = 0; i < playerTeam.Count; i++)
            {
                bool leader = i == leaderIndex || (twoSquads && i == leaderIndex2);
                var unit = CreateUnit(playerTeam[i], true, leader, playerMelee[i], playerSkills[i], spawn[i]);
                unit.Squad = twoSquads ? SquadAt(i) : 0;
                if (leader) Leaders.Add(unit);
            }
            Leaders.Sort((a, b) => a.Squad.CompareTo(b.Squad));
            Leader = Leaders.FirstOrDefault();

            SpawnEnemies(0);
            CombatResolver.RaiseBattleStart(PlayerTeam, EnemyTeam);
        }

        /// <summary>
        /// waveIndex번째 라운드 적을 오른쪽에 세운다. 근접은 두 줄로 엇갈리고, 원거리(궁수)는 그 뒤 한 줄.
        /// 적이 2명 이상이면 3명 중 1명꼴(3번째·6번째…)이 궁수다 — 2명 라운드는 근접만, 혼자 나오는 보스도 근접.
        /// 새 유닛을 돌려준다.
        /// </summary>
        private List<FieldUnit> SpawnEnemies(int waveIndex)
        {
            CurrentWave = waveIndex;
            EnemyTeam = _waves[waveIndex];
            // 짐승(늑대 등)은 활을 쏘지 않는다: 궁수 자리여도 근접 그대로.
            // 독버섯은 늘 원거리(포자를 쏜다)로 뒷줄에 선다 — 이름에 "궁수"는 붙이지 않는다.
            var archers = new bool[EnemyTeam.Count];
            var ranged = new bool[EnemyTeam.Count];
            for (int i = 0; i < EnemyTeam.Count; i++)
            {
                archers[i] = IsArcherSlot(i, EnemyTeam.Count) && !IsBeast(EnemyTeam[i].Name);
                ranged[i] = archers[i] || IsSporeShooter(EnemyTeam[i].Name);
            }
            for (int i = 0; i < EnemyTeam.Count; i++)
                if (archers[i])
                    EnemyTeam[i] = Renamed(EnemyTeam[i], EnemyTeam[i].Name + " 궁수");
            AllEnemies.AddRange(EnemyTeam);
            var added = new List<FieldUnit>();
            for (int i = 0; i < EnemyTeam.Count; i++)
            {
                bool archer = ranged[i];
                float x = archer ? FieldWidth - ArcherColumnInset : FieldWidth - 260f - (i % 2) * 120f;
                float y = Spread(i, EnemyTeam.Count);
                added.Add(CreateUnit(EnemyTeam[i], false, false, !archer, null, new Vector2(x, y)));
            }
            return added;
        }

        private const float ArcherColumnInset = 140f; // 궁수 줄: 필드 오른쪽 끝에서 이만큼 안쪽(근접 두 줄보다 뒤)

        private static bool IsArcherSlot(int index, int count) => count >= 2 && index % 3 == 2;

        /// <summary>활을 쓰지 않는 짐승·괴물 적(이름에 늑대·울프·버섯·엔트). 전투 그림도 그 종류로 나온다(BattleMonsterSprite.KindFor).</summary>
        private static bool IsBeast(string name) => name != null && (name.Contains("늑대") || name.Contains("울프") || name.Contains("버섯") || GN3.World.RegionInfo.IsEnt(name));

        /// <summary>멀리서 포자를 쏘는 적(독버섯).</summary>
        private static bool IsSporeShooter(string name) => name != null && name.Contains("버섯");

        /// <summary>늑대를 탄 고블린(늑대기수). 필드에서 훨씬 빨리 달린다 — 이동만 빠르고 회피·공격 속도(이동 속도 능력치)는 그대로.</summary>
        private static bool IsRider(string name) => name != null && name.Contains("늑대기수");
        private const float RiderFieldSpeedMultiplier = 1.6f; // 이동 약 160 → 260(암살자 212보다 빠르다)

        /// <summary>제자리에 뿌리박은 보스(앤트킹·예전 거목 엔트): 걸어가지 않고 밀리지도 않는다. 손이 닿지 않는 적은 뿌리 내려찍기로 친다.</summary>
        private static bool IsRooted(string name) => GN3.World.RegionInfo.IsEnt(name);

        /// <summary>거목 엔트 몸 반지름: 그린 그림의 뿌리(발밑) 폭 약 260의 절반 — 근접 용병이 다리 안이 아니라 몸 가장자리에 선다
        /// (그림 크기 BattleMonsterSprite Ent SizeScale과 같은 비율로 맞춘다. 2026-10-19 그림 1.4배 → 90에서 126).</summary>
        private const float RootedBodyRadius = 126f;

        /// <summary>몸 가장자리끼리의 거리(몸 반지름이 0이면 중심 거리와 같다).</summary>
        private static float Gap(FieldUnit a, FieldUnit b) => Distance(a, b) - a.BodyRadius - b.BodyRadius;

        /// <summary>u의 사거리 안에 t가 있는지(몸 가장자리 기준).</summary>
        private static bool InReach(FieldUnit u, FieldUnit t) => Gap(u, t) <= u.AttackRange;

        /// <summary>밀어내기에서 버티는 유닛: 공격 동작 중이거나 뿌리박은 적.</summary>
        private static bool Anchored(FieldUnit u) => u.IsBusy || (!u.IsPlayerSide && IsRooted(u.Combatant.Name));

        private static Combatant Renamed(Combatant c, string name) => new Combatant(name, c.Stats, c.Passives);

        /// <summary>쓰러진 이번 라운드 적을 치우고 다음 라운드 적을 세운다. 아군은 서 있던 자리·체력 그대로.</summary>
        private void StartNextWave()
        {
            CancelSlams(); // 앞 라운드 보스가 걸어 둔 예고는 없앤다
            CancelRoars();
            var removed = Units.Where(u => !u.IsPlayerSide).ToList();
            foreach (var u in removed)
            {
                Units.Remove(u);
                _byCombatant.Remove(u.Combatant);
            }
            foreach (var u in Units)
            {
                u.Target = null;
                u.TargetOrdered = false;
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

        private void PlaceColumn(List<int> indices, float x, Vector2[] spawn, float top, float bottom)
        {
            for (int k = 0; k < indices.Count; k++)
                spawn[indices[k]] = new Vector2(x, Spread(k, indices.Count, top, bottom));
        }

        /// <summary>count명을 top~bottom 사이에 고르게 벌린 k번째 y(한 명이면 가운데).</summary>
        private static float Spread(int k, int count, float top = 70f, float bottom = FieldHeight - 70f) =>
            count <= 1 ? (top + bottom) / 2f : top + (bottom - top) * k / (count - 1);

        private FieldUnit CreateUnit(Combatant c, bool playerSide, bool leader, bool melee, SkillBase skill, Vector2 position)
        {
            float jitter = 1f - SpeedJitterRange + (float)_rng.NextDouble() * (SpeedJitterRange * 2f);
            float fill = (BaseFillPerSecond + c.Stats.MoveSpeed * FillPerSpeedPerSecond) * jitter;
            float interval = 100f / Math.Max(1f, fill) * AttackPace;
            if (!playerSide) interval /= EnemyAttackSpeedMultiplier;
            if (!playerSide && IsRooted(c.Name)) interval /= RootedAttackSpeedMultiplier;
            float speed = BaseMoveSpeed + c.Stats.MoveSpeed * MoveSpeedPerStat;
            if (!playerSide && IsRider(c.Name)) speed *= RiderFieldSpeedMultiplier;
            var unit = new FieldUnit(c, playerSide, leader, melee, skill, position, speed, interval)
            {
                AttackTimer = interval * (float)_rng.NextDouble() * StartingTimerJitter,
                BodyRadius = !playerSide && IsRooted(c.Name) ? RootedBodyRadius : 0f,
                RoarTimer = !playerSide && IsRooted(c.Name) ? RoarFirstDelay : 0f,
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

        /// <summary>두 분대 전투: 조종을 살아 있는 다른 분대장에게 넘긴다(넘겨준 분대장은 이제 AI). 바뀌었으면 true.</summary>
        public bool SwitchLeader()
        {
            var next = Leaders.FirstOrDefault(l => l != Leader && l.IsAlive);
            if (next == null || IsOver) return false;
            if (Leader != null)
            {
                Leader.MoveGoal = null;
                Leader.SkillTarget = null;
            }
            _orderedTarget = null;
            Leader = next;
            return true;
        }

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

        // ---------- 대원 명령(끌어 놓기) ----------

        private bool CanOrder(FieldUnit unit) =>
            HasStarted && !IsOver && unit != null && unit.IsPlayerSide && unit.IsAlive;

        /// <summary>대원을 그 지점으로 걸어가게 한다(가는 동안 적을 쫓지 않음). 분대장이면 OrderMove와 같고,
        /// 다른 대원은 도착한 뒤 그 자리를 지킨다(HoldPosition). 명령이 먹혔으면 true.</summary>
        public bool OrderUnitMove(FieldUnit unit, Vector2 point)
        {
            if (!CanOrder(unit)) return false;
            if (unit == Leader) { OrderMove(point); return true; }
            unit.MoveGoal = ClampToField(point);
            unit.Target = null;
            unit.TargetOrdered = false;
            unit.SkillTarget = null;
            unit.HoldPosition = true;
            return true;
        }

        /// <summary>대원에게 이 적을 공격시킨다. 분대장이면 OrderAttack과 같고, 다른 대원은 자리 지키기를 풀고 그 적부터 쫓는다
        /// (그 뒤엔 다시 알아서 싸운다).</summary>
        public bool OrderUnitAttack(FieldUnit unit, FieldUnit enemy)
        {
            if (!CanOrder(unit) || enemy == null || enemy.IsPlayerSide || !enemy.IsAlive) return false;
            if (unit == Leader) { OrderAttack(enemy); return true; }
            unit.HoldPosition = false;
            unit.MoveGoal = null;
            unit.SkillTarget = null;
            unit.Target = enemy;
            unit.TargetOrdered = true;
            return true;
        }

        // ---------- 분대 명령(일시정지 중 분대장이 내린다) ----------
        // 지금 조종 중인 분대장의 분대에만 먹힌다(두 분대 전투면 다른 분대는 그대로). 명령마다 쿨타임(전투 시간).

        public const float RallyCooldown = 10f;
        public const float RetreatCooldown = 12f;
        public const float AdvanceCooldown = 8f;
        public const float FocusCooldown = 12f;
        /// <summary>후퇴: 지금 자리에서 왼쪽(아군 쪽)으로 이만큼 물러난다.</summary>
        public const float RetreatDistance = 320f;
        private const float RallySpacing = 70f;

        private readonly float[] _squadCooldowns = new float[4];

        public float SquadOrderCooldown(SquadOrder order) => _squadCooldowns[(int)order];
        public bool SquadOrderReady(SquadOrder order) => LeaderCanAct && _squadCooldowns[(int)order] <= 0f;

        /// <summary>조종 중인 분대장의 분대에서 살아 있는 대원(분대장 빼고).</summary>
        private List<FieldUnit> SquadMembers() => Units
            .Where(u => u.IsPlayerSide && u.IsAlive && u != Leader && u.Squad == Leader.Squad)
            .ToList();

        /// <summary>집결: 대원들이 분대장 둘레(뒤쪽부터)로 모여 그 자리를 지킨다.</summary>
        public bool OrderRally()
        {
            if (!SquadOrderReady(SquadOrder.Rally)) return false;
            var members = SquadMembers();
            for (int i = 0; i < members.Count; i++)
                OrderUnitMove(members[i], Leader.Position + RallyOffset(i));
            _squadCooldowns[(int)SquadOrder.Rally] = RallyCooldown;
            return true;
        }

        /// <summary>분대장 둘레 자리: 뒤 → 위아래 → 뒤 대각선 → 두 줄 뒤 … (분대장 자리와 겹치지 않게).</summary>
        private static Vector2 RallyOffset(int i)
        {
            Vector2[] ring =
            {
                new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f), new Vector2(-1f, 1f),
                new Vector2(-1f, -1f), new Vector2(-2f, 0f), new Vector2(-2f, 1f), new Vector2(-2f, -1f),
            };
            var o = ring[i % ring.Length] + new Vector2(-2f * (i / ring.Length), 0f);
            return o * RallySpacing;
        }

        /// <summary>후퇴: 분대장과 대원 모두 왼쪽으로 물러나고(적을 쫓지 않음), 대원은 물러난 자리를 지킨다.</summary>
        public bool OrderRetreat()
        {
            if (!SquadOrderReady(SquadOrder.Retreat)) return false;
            foreach (var u in SquadMembers())
                OrderUnitMove(u, u.Position - new Vector2(RetreatDistance, 0f));
            OrderMove(Leader.Position - new Vector2(RetreatDistance, 0f));
            _squadCooldowns[(int)SquadOrder.Retreat] = RetreatCooldown;
            return true;
        }

        /// <summary>전진: 대원들의 자리 지키기·이동·지정 대상을 모두 풀어 가장 가까운 적에게 달려들게 한다(분대장은 그대로).</summary>
        public bool OrderAdvance()
        {
            if (!SquadOrderReady(SquadOrder.Advance)) return false;
            foreach (var u in SquadMembers())
            {
                u.HoldPosition = false;
                u.MoveGoal = null;
                u.Target = null;
                u.TargetOrdered = false;
                u.SkillTarget = null;
            }
            _squadCooldowns[(int)SquadOrder.Advance] = AdvanceCooldown;
            return true;
        }

        /// <summary>집중 공격: 분대장과 대원 모두 이 적 하나를 노린다(쓰러지면 다시 각자 싸운다).</summary>
        public bool OrderFocus(FieldUnit enemy)
        {
            if (!SquadOrderReady(SquadOrder.Focus) || enemy == null || enemy.IsPlayerSide || !enemy.IsAlive) return false;
            foreach (var u in SquadMembers()) OrderUnitAttack(u, enemy);
            OrderAttack(enemy);
            _squadCooldowns[(int)SquadOrder.Focus] = FocusCooldown;
            return true;
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
            for (int i = 0; i < _squadCooldowns.Length; i++)
                if (_squadCooldowns[i] > 0f) _squadCooldowns[i] = Math.Max(0f, _squadCooldowns[i] - dt);
            if (Leader != null && !Leader.IsAlive) SwitchLeader(); // 조종 중인 분대장이 쓰러지면 다른 분대장에게

            foreach (var u in Units)
            {
                u.IsMoving = false;
                if (!u.IsAlive)
                {
                    u.Target = null;
                    u.SkillTarget = null;
                    u.MoveGoal = null;
                    u.BusyTimer = 0f;
                    continue;
                }
                if (u.StunTimer > 0f) u.StunTimer = Math.Max(0f, u.StunTimer - dt); // 기절 중엔 공격 대기도 차지 않는다
                else u.AttackTimer = Math.Min(u.AttackInterval, u.AttackTimer + dt);
                if (u.Cooldown > 0f) u.Cooldown = Math.Max(0f, u.Cooldown - dt);
                if (u.BusyTimer > 0f) u.BusyTimer = Math.Max(0f, u.BusyTimer - dt);
                if (u.RoarTimer > 0f) u.RoarTimer = Math.Max(0f, u.RoarTimer - dt);
            }

            foreach (var u in Units.ToList())
            {
                if (!u.IsAlive || IsOver) continue;
                if (u.IsStunned) continue; // 기절: 아무것도 못 한다(명령은 남는다)
                if (u.IsBusy) continue; // 공격 동작 중: 명령은 남겨 두고 동작이 끝난 뒤 이어서 한다
                if (!u.IsPlayerSide && IsRooted(u.Combatant.Name) && u.RoarTimer <= 0f)
                {
                    StartRoar(u); // 거목 엔트: 때가 되면 다른 행동 대신 기절 포효
                    continue;
                }
                if (u == Leader) ThinkLeader(u); // 조종하지 않는 다른 분대장은 아군 AI
                else if (u.IsPlayerSide) ThinkAlly(u);
                else ThinkEnemy(u);
                Act(u, dt, events);
            }

            TickSlams(dt, events);
            TickRoars(dt);
            TickPendingStuns(dt);
            TickPoison(dt, events);
            Separate(dt);
            UpdateWaveBreak(dt);
            return events;
        }

        private const float PoisonTickSeconds = 1f;
        private float _poisonTimer;

        /// <summary>독은 1초(전투 시간 — 배속·일시정지를 따른다)마다 한 번 들어간다(자동 전투의 라운드 한 번과 같은 몫).</summary>
        private void TickPoison(float dt, List<BattleEvent> events)
        {
            if (IsOver) return;
            _poisonTimer += dt;
            if (_poisonTimer < PoisonTickSeconds) return;
            _poisonTimer -= PoisonTickSeconds;
            var produced = CombatResolver.TickPoison(PlayerTeam, EnemyTeam, ActionCount);
            events.AddRange(produced);
            Log.AddRange(produced);
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
            if (u.Target != null && u.Target != _orderedTarget && !InReach(u, u.Target)) u.Target = null;
            if (u.Target == null && u.MoveGoal == null && u.SkillTarget == null)
            {
                var near = Nearest(u, Enemies(u));
                if (near != null && InReach(u, near)) u.Target = near;
            }
        }

        /// <summary>아군 AI: 가장 가까운 적에게 붙어 싸우고(때리는 중이면 그 대상 유지), 스킬은 쿨타임이 돌면 알아서.
        /// 끌어서 자리를 정해 준 대원은 그 자리로 가는 동안 싸우지 않고, 도착한 뒤엔 자리를 지키며 사거리 안의 적만 때린다.</summary>
        private void ThinkAlly(FieldUnit u)
        {
            if (u.Target != null && !u.Target.IsAlive) { u.Target = null; u.TargetOrdered = false; }
            if (u.SkillTarget != null && !u.SkillTarget.IsAlive) u.SkillTarget = null;
            if (u.HoldPosition)
            {
                if (u.MoveGoal.HasValue) return; // 걸어가는 중: 적을 쫓지도, 스킬을 쓰지도 않는다
                if (u.Target != null && !InReach(u, u.Target)) u.Target = null;
                if (u.Target == null)
                {
                    var near = Nearest(u, Enemies(u));
                    if (near != null && InReach(u, near)) u.Target = near;
                }
            }
            else if (!u.TargetOrdered) RetargetNearest(u);
            if (u.Skill == null || u.Cooldown > 0f || u.SkillTarget != null) return;

            // 자동 전투와 같은 판단(SkillAI). 공격 스킬은 사거리 안의 적만, 치유는 어디 있든(다가가서 쓴다 — 자리를 지키는 중이면 닿는 아군만).
            var inRange = Enemies(u).Where(e => InReach(u, e)).Select(e => e.Combatant);
            var allies = u.HoldPosition ? PlayerTeam.Where(c => { var a = UnitOf(c); return a != null && InReach(u, a); }).ToList() : PlayerTeam;
            u.SkillTarget = UnitOf(SkillAI.ChooseTarget(u.Skill, allies, inRange, u.Target?.Combatant));
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
            if (u.Target != null && u.Target.IsAlive && InReach(u, u.Target)) return;
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
                if (t == u || InReach(u, t))
                {
                    Face(u, t.Position);
                    UseSkill(u, t, events);
                }
                else MoveToward(u, t.Position, u.AttackRange * ApproachFraction + u.BodyRadius + t.BodyRadius, dt);
                return;
            }

            if (u.Target != null)
            {
                var t = u.Target;
                if (InReach(u, t))
                {
                    Face(u, t.Position);
                    if (u.AttackTimer >= u.AttackInterval) Attack(u, t, events, outOfReach: false);
                }
                else if (!u.IsPlayerSide && IsRooted(u.Combatant.Name))
                {
                    // 뿌리박은 거목 엔트: 다가가지 않고, 손이 닿지 않으면 뿌리 내려찍기로만 친다
                    Face(u, t.Position);
                    if (u.AttackTimer >= u.AttackInterval) Attack(u, t, events, outOfReach: true);
                }
                else MoveToward(u, t.Position, u.AttackRange * ApproachFraction + u.BodyRadius + t.BodyRadius, dt);
                return;
            }

            if (u.MoveGoal.HasValue)
            {
                if (MoveToward(u, u.MoveGoal.Value, 0f, dt)) u.MoveGoal = null;
            }
        }

        /// <summary>기본공격 차례. 거목 엔트는 순번(3타마다)에 뿌리 내려찍기 시전(예고 뒤 분출), 아니면 휘두르기 —
        /// 손이 닿는 아군이 없으면 휘두를 차례는 뿌리를 모으며 그냥 넘긴다(멀리 있어도 3타마다 내려찍기는 온다).</summary>
        private void Attack(FieldUnit u, FieldUnit target, List<BattleEvent> events, bool outOfReach)
        {
            var slam = u.IsPlayerSide ? null : u.Combatant.Passives.OfType<RootSlamPassive>().FirstOrDefault();
            if (slam == null) { BasicAttack(u, target, events); return; }
            if (slam.TryTriggerAoeAttack(_rng)) StartSlam(u);
            else if (outOfReach) u.AttackTimer = 0f;
            else
            {
                // 하강 해머 슬램(기본 공격): 순번은 방금 셌다 — 광역 판정을 다시 굴리지 않는다.
                // 회피되지 않고 맞았으면(쓰러뜨리지 않았으면) 해머가 그림에서 닿는 순간 1초 기절.
                int before = events.Count;
                BasicAttack(u, target, events, allowAoe: false);
                for (int i = before; i < events.Count; i++)
                    if (events[i].Target == target.Combatant && !events[i].Evaded && !events[i].TargetDefeated)
                        _pendingStuns.Add((target, u.AttackHitDelay));
            }
        }

        /// <summary>거목 엔트 하강 해머 슬램에 맞으면 기절하는 시간(직접 전투만 — 자동 전투엔 시간 개념이 없다).</summary>
        public const float HammerStunSeconds = 1f;

        private readonly List<(FieldUnit target, float delay)> _pendingStuns = new List<(FieldUnit, float)>();

        /// <summary>해머가 닿기를 기다리는 기절을 시간만큼 줄이고, 닿으면 건다.</summary>
        private void TickPendingStuns(float dt)
        {
            for (int i = _pendingStuns.Count - 1; i >= 0; i--)
            {
                var (target, delay) = _pendingStuns[i];
                delay -= dt;
                if (delay > 0f) { _pendingStuns[i] = (target, delay); continue; }
                _pendingStuns.RemoveAt(i);
                if (target.IsAlive) ApplyStun(target, HammerStunSeconds);
            }
        }

        /// <summary>기절을 건다(이미 기절 중이면 더 긴 쪽).</summary>
        private static void ApplyStun(FieldUnit unit, float seconds)
        {
            if (seconds <= unit.StunTimer) return;
            unit.StunTimer = seconds;
            unit.StunTotal = seconds;
        }

        private void BasicAttack(FieldUnit u, FieldUnit target, List<BattleEvent> events, bool allowAoe = true)
        {
            foreach (var passive in u.Combatant.Passives)
                passive.OnRoundStart(u.Combatant, ++ActionCount, _rng);

            var targetTeam = u.IsPlayerSide ? EnemyTeam : PlayerTeam;
            var produced = new List<BattleEvent>();
            if (allowAoe && u.Combatant.Passives.Any(p => p.TryTriggerAoeAttack(_rng)))
            {
                foreach (var e in targetTeam.Where(c => c.IsAlive).ToList())
                    produced.Add(CombatResolver.ResolveAttack(u.Combatant, e, ActionCount, targetTeam, _rng));
            }
            else
            {
                produced.Add(CombatResolver.ResolveAttack(u.Combatant, target.Combatant, ActionCount, targetTeam, _rng));
            }
            u.AttackTimer = 0f;
            u.BusyTimer = u.AttackAnimSeconds;
            events.AddRange(produced);
            Log.AddRange(produced);
        }

        // ---------- 거목 엔트 뿌리 내려찍기(예고 → 분출) ----------

        /// <summary>예고 시간(전투 초): 붉은 범위가 차오르는 동안 분대장은 빠져나갈 수 있다.
        /// 2026-10-19 1.6 → 2.6(사용자 요청: 보고 반응해 피할 여유).</summary>
        public const float SlamTelegraphSeconds = 2.6f;
        /// <summary>거목 엔트는 공격 간격을 이만큼 짧게(이동 속도 능력치가 낮아 약 2.6초였다 → 약 1.7초). 2026-10-19 사용자 요청.</summary>
        private const float RootedAttackSpeedMultiplier = 1.5f;
        /// <summary>범위 반지름. 화면의 붉은 원과 같은 크기다. 2026-10-19 70 → 110(사용자 요청: 범위를 더 크게).</summary>
        public const float SlamZoneRadius = 110f;
        /// <summary>한 번에 솟는 범위 수 — 아군을 노리지 않고 엔트 앞(아군 쪽) 판 위 무작위 자리에 흩어 놓는다(2026-10-19 사용자 요청).</summary>
        public const int SlamZoneCount = 6;
        private const float SlamZoneSpacing = 1.5f; // 범위 중심끼리 반지름의 이 배보다 가깝지 않게(고루 흩어지게)
        private const int SlamZoneTries = 30;
        private const float SlamRecoverSeconds = 0.5f; // 분출 뒤 내려찍기 동작이 끝날 때까지(그동안 다음 공격 없음)

        private readonly List<RootSlamCast> _slams = new List<RootSlamCast>();

        /// <summary>뿌리 내려찍기 예고가 시작됐다(UI가 붉은 범위를 그리고, 분출 순간 가시를 띄운다).</summary>
        public event Action<RootSlamCast> OnSlamTelegraph;

        /// <summary>시전: 엔트 앞 판 위 무작위 자리에 범위 SlamZoneCount개를 정해 두고 예고 시간 뒤에 분출한다(아군을 노리지 않는다).</summary>
        private void StartSlam(FieldUnit u)
        {
            var zones = RandomSlamZones(u);
            var cast = new RootSlamCast(u, zones, SlamTelegraphSeconds);
            _slams.Add(cast);
            u.AttackTimer = 0f;
            u.BusyTimer = SlamTelegraphSeconds + SlamRecoverSeconds; // 예고 중엔 휘두르지 않는다(LastWasSlam도 분출까지 유지)
            OnSlamTelegraph?.Invoke(cast);
        }

        /// <summary>엔트 앞(아군 쪽 — 판 왼쪽 끝부터 엔트 몸 앞까지) 판 안에 범위 중심을 고루 흩어 고른다.
        /// 서로 SlamZoneSpacing × 반지름보다 가까우면 다시 고르고, SlamZoneTries번 안에 못 찾으면 마지막 자리를 쓴다.</summary>
        private Vector2[] RandomSlamZones(FieldUnit caster)
        {
            const float r = SlamZoneRadius;
            float minX = EdgeMargin + r, maxX = Math.Max(minX, caster.Position.X - caster.BodyRadius - r);
            float minY = r, maxY = FieldHeight - r;
            var zones = new Vector2[SlamZoneCount];
            for (int i = 0; i < zones.Length; i++)
            {
                Vector2 p = default;
                for (int t = 0; t < SlamZoneTries; t++)
                {
                    p = new Vector2(minX + (float)_rng.NextDouble() * (maxX - minX), minY + (float)_rng.NextDouble() * (maxY - minY));
                    bool apart = true;
                    for (int j = 0; j < i; j++)
                        if (Vector2.Distance(p, zones[j]) < r * SlamZoneSpacing) { apart = false; break; }
                    if (apart) break;
                }
                zones[i] = p;
            }
            return zones;
        }

        private void TickSlams(float dt, List<BattleEvent> events)
        {
            for (int i = _slams.Count - 1; i >= 0; i--)
            {
                var cast = _slams[i];
                if (!cast.Caster.IsAlive || IsOver)
                {
                    cast.State = RootSlamCast.Phase.Cancelled;
                    _slams.RemoveAt(i);
                    continue;
                }
                cast.Remaining = Math.Max(0f, cast.Remaining - dt);
                if (cast.Remaining > 0f) continue;
                _slams.RemoveAt(i);
                Erupt(cast, events);
            }
        }

        /// <summary>분출: 그 순간 범위 안(몸 반지름 포함)에 서 있는 아군만 맞는다.</summary>
        private void Erupt(RootSlamCast cast, List<BattleEvent> events)
        {
            var caster = cast.Caster.Combatant;
            foreach (var passive in caster.Passives)
                passive.OnRoundStart(caster, ++ActionCount, _rng);
            var produced = new List<BattleEvent>();
            foreach (var p in Units.Where(p => p.IsPlayerSide && p.IsAlive).ToList())
                if (cast.Zones.Any(z => Vector2.Distance(z, p.Position) <= SlamZoneRadius + p.BodyRadius))
                    produced.Add(CombatResolver.ResolveAttack(caster, p.Combatant, ActionCount, PlayerTeam, _rng));
            cast.State = RootSlamCast.Phase.Erupted;
            cast.HitCount = produced.Count;
            events.AddRange(produced);
            Log.AddRange(produced);
        }

        private void CancelSlams()
        {
            foreach (var cast in _slams) cast.State = RootSlamCast.Phase.Cancelled;
            _slams.Clear();
        }

        // ---------- 거목 엔트 기절 포효(예고 → 기절) ----------

        /// <summary>전투 시작 뒤 첫 포효까지, 그 뒤 포효 간격(전투 초).</summary>
        public const float RoarFirstDelay = 8f, RoarInterval = 15f;
        /// <summary>예고(엔트 앞쪽 큰 붉은 반원) 시간. 이 안에 범위 밖(앞쪽 끝 너머나 엔트 뒤)으로 나가면 기절하지 않는다.
        /// 2026-10-19 범위를 크게 하며 2.0 → 2.6(내려찍기와 같게 — 용병 이동 약 140~180/초로 앞쪽 끝까지 약 410을 빠져나갈 수 있게),
        /// 같은 날 2.6 → 3.6(사용자 요청: 포효 준비 시간을 더 길게).</summary>
        public const float RoarTelegraphSeconds = 3.6f;
        /// <summary>범위: 엔트가 바라보는 쪽(용병 쪽) 반원 — 중심에서 이 거리(+ 상대 몸 반지름) 안이고 엔트보다 앞쪽.
        /// 근접(중심 약 208)은 물론 사거리 끝에서 쏘는 원거리(약 480~546)까지 걸리고, 판 세로 전체를 덮는다. 엔트 뒤쪽은 안전.
        /// 2026-10-19 원 330 → 앞쪽 반원 620(사용자: 원거리가 안 맞는다).</summary>
        public const float RoarRadius = 620f;
        public const float RoarStunSeconds = 2f;
        // 포효를 마치는 동작(그동안 다른 행동 없음): 터진 뒤 입 벌린 장 유지(둘째 포효 소리의 큰 부분 1.23초 × 전투 배속 0.7 ≈ 0.86)
        // + 입을 다무는 2장(0.25) — 포효 소리가 끝나기 전에 휘두르기로 동작이 끊기지 않게(BossRoarAudio.Stun, BattleUIController.RoarTelegraph).
        private const float RoarRecoverSeconds = 1.2f;

        private readonly List<RoarCast> _roars = new List<RoarCast>();

        /// <summary>기절 포효 예고가 시작됐다(UI가 큰 붉은 원을 그리고, 터지는 순간 충격파·기절 표시).</summary>
        public event Action<RoarCast> OnRoarTelegraph;

        private void StartRoar(FieldUnit u)
        {
            var cast = new RoarCast(u, u.Position, u.Facing, RoarRadius, RoarTelegraphSeconds); // 바라보는 쪽(대상 쪽) 반원으로 고정
            _roars.Add(cast);
            u.RoarTimer = RoarInterval;
            u.BusyTimer = RoarTelegraphSeconds + RoarRecoverSeconds; // 예고 중엔 다른 공격 없음
            OnRoarTelegraph?.Invoke(cast);
        }

        private void TickRoars(float dt)
        {
            for (int i = _roars.Count - 1; i >= 0; i--)
            {
                var cast = _roars[i];
                if (!cast.Caster.IsAlive || IsOver)
                {
                    cast.State = RoarCast.Phase.Cancelled;
                    _roars.RemoveAt(i);
                    continue;
                }
                cast.Remaining = Math.Max(0f, cast.Remaining - dt);
                if (cast.Remaining > 0f) continue;
                _roars.RemoveAt(i);
                // 터짐: 그 순간 앞쪽 반원 안(몸 반지름 포함)에 있는 아군은 기절(이미 기절이면 더 긴 쪽)
                int stunned = 0;
                foreach (var p in Units.Where(p => p.IsPlayerSide && p.IsAlive))
                    if (cast.Contains(p.Position, p.BodyRadius))
                    {
                        ApplyStun(p, RoarStunSeconds);
                        stunned++;
                    }
                cast.StunnedCount = stunned;
                cast.State = RoarCast.Phase.Erupted;
            }
        }

        private void CancelRoars()
        {
            foreach (var cast in _roars) cast.State = RoarCast.Phase.Cancelled;
            _roars.Clear();
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
            if (produced.Any(e => e.Damage >= 0)) u.BusyTimer = u.AttackAnimSeconds; // 치유만 하는 스킬은 검격 동작이 없다
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
                float radius = SeparationRadius + a.BodyRadius + b.BodyRadius; // 큰 보스 둘레엔 더 멀리 선다
                if (dist >= radius) continue;
                var dir = dist > 0.01f ? delta / dist : new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f);
                if (dir.LengthSquared() < 0.0001f) dir = Vector2.UnitY;
                dir = Vector2.Normalize(dir);
                // 공격 동작 중인 쪽·뿌리박은 엔트는 밀리지 않고 상대가 그만큼 더 비켜선다(둘 다 버티면 그대로).
                bool aHolds = Anchored(a), bHolds = Anchored(b);
                if (aHolds && bHolds) continue;
                float share = aHolds || bHolds ? 1f : 0.5f;
                var push = dir * ((radius - dist) * share * strength);
                if (!aHolds) a.Position = ClampToField(a.Position - push);
                if (!bHolds) b.Position = ClampToField(b.Position + push);
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

    /// <summary>거목 엔트의 뿌리 내려찍기 한 번: 시전 순간 정한 범위들(중심 = 엔트 앞 무작위 자리, 반지름 SlamZoneRadius)과 남은 예고 시간.
    /// 상태는 FieldBattleSession만 바꾸고, UI는 읽어서 붉은 범위·분출 가시를 그린다.</summary>
    public class RootSlamCast
    {
        public enum Phase { Pending, Erupted, Cancelled }

        public FieldUnit Caster { get; }
        public Vector2[] Zones { get; }
        public float Total { get; }
        public float Remaining { get; internal set; }
        public Phase State { get; internal set; } = Phase.Pending;
        /// <summary>분출 때 맞은 아군 수.</summary>
        public int HitCount { get; internal set; }
        /// <summary>예고가 얼마나 찼는지 0 → 1.</summary>
        public float Progress => Total > 0f ? 1f - Remaining / Total : 1f;

        public RootSlamCast(FieldUnit caster, Vector2[] zones, float seconds)
        {
            Caster = caster;
            Zones = zones;
            Total = Remaining = seconds;
        }
    }

    /// <summary>거목 엔트의 기절 포효 한 번: 엔트가 바라보는 쪽 반원(중심·방향·반지름)과 남은 예고 시간. 터지면 반원 안 아군이 기절한다.</summary>
    public class RoarCast
    {
        public enum Phase { Pending, Erupted, Cancelled }

        public FieldUnit Caster { get; }
        public Vector2 Center { get; }
        /// <summary>반원이 펼쳐지는 쪽: −1 왼쪽, +1 오른쪽(시전 순간 엔트가 보던 쪽).</summary>
        public float Facing { get; }
        public float Radius { get; }
        public float Total { get; }
        public float Remaining { get; internal set; }
        public Phase State { get; internal set; } = Phase.Pending;
        /// <summary>터질 때 기절한 아군 수.</summary>
        public int StunnedCount { get; internal set; }
        public float Progress => Total > 0f ? 1f - Remaining / Total : 1f;

        public RoarCast(FieldUnit caster, Vector2 center, float facing, float radius, float seconds)
        {
            Caster = caster;
            Center = center;
            Facing = facing >= 0f ? 1f : -1f;
            Radius = radius;
            Total = Remaining = seconds;
        }

        /// <summary>이 자리(몸 반지름 포함)가 반원 안인가: 반지름 안이고, 엔트 중심보다 바라보는 쪽(곧은 변 위 포함).</summary>
        public bool Contains(Vector2 position, float bodyRadius) =>
            Vector2.Distance(Center, position) <= Radius + bodyRadius && (position.X - Center.X) * Facing >= 0f;
    }
}
