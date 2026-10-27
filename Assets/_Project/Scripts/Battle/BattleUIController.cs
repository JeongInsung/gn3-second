using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GN3.CharacterAnim;
using GN3.Combat;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 직접 진행 전투 화면(필드형). 전투 규칙과 AI는 FieldBattleSession이 맡고, 이 클래스는 유닛을 필드 위
    /// 위치에 그리고 분대장 조작(클릭 이동·적 클릭 공격·Q 스킬)을 세션 명령으로 넘긴다.
    /// 들어오면 먼저 배치 단계(아군을 끌어 왼쪽 배치 구역 안에 놓음, 시간 정지) → '전투 시작'/Space로 전투.
    /// 필드 좌표는 화면 필드 영역의 로컬 좌표와 1:1(왼쪽 아래가 0,0)이다.
    /// 몬스터 그림이 아직 없어 기본 도형(BattlePlaceholderSprites)으로 그린다 - 그림이 생기면 CreateUnitView의 적 분기만 바꾸면 된다.
    /// </summary>
    public class BattleUIController : MonoBehaviour
    {
        private class UnitView
        {
            public FieldUnit Unit;
            public RectTransform Root;      // 발 위치(필드 좌표)
            public RectTransform Body;      // 그림(바라보는 방향으로 좌우 뒤집음)
            public CanvasGroup Group;
            public Image[] Tintables;       // 피격 때 붉게 물들일 그림들
            public bool PoisonTinted;       // 중독으로 초록빛을 입혔는가(풀리면 원래 색으로)
            public Text PoisonTag;          // 체력 막대 왼쪽 "독 3"(남은 횟수, 중독 중에만)
            public float PoisonBubbleTimer; // 중독 중 몸에서 거품을 피워 올리는 간격
            public bool PoisonShown;        // 독을 건 공격이 화면에서 닿았는가(그 전엔 중독 표시를 미룬다)
            public Color[] BaseColors;
            public GameObject Bars;         // 머리 위 체력·공격 대기 막대(쓰러지면 숨김). 보스는 화면 위 큰 막대(BossHud 안)
            public CanvasGroup BossHud;     // 보스만: 화면 위쪽 가운데 칭호·이름·큰 체력 막대
            public GameObject StunStars;    // 아군만: 기절 중 머리 위에서 도는 별(+ 남은 기절 시간 막대)
            public bool WasStunned;         // 지난 프레임에 기절이었는가(걸리는 순간을 알아챈다)
            public bool StunTinted;         // 기절 색을 입혔는가(풀리면 원래 색으로)
            public RectTransform HpFill;    // 오른쪽 끝(anchorMax.x) = 체력 비율
            public Image HpFillImage;
            public RectTransform HpTrail;   // 깎인 만큼 잠깐 남았다 줄어드는 밝은 막대
            public Text HpText;             // 아군만 "45/60"
            public RectTransform ReadyFill;
            public Image ReadyFillImage;
            public float TrailRatio = 1f;
            public float TrailHold;
            public Image TargetRing;        // 분대장이 노리는 적
            public Image ClickArea;
            public BattlePortraitAnimator Animator; // 용병만(파츠 조합 캐릭터)
            public BattleMonsterSprite Monster;     // 몬스터만(고블린 시트: 대기·검 공격)
            public float HopPhase;
            public Vector2 Kick;            // 맞고 밀려난 만큼(그림만, 빠르게 0으로 돌아온다)
            public float DeathStart = -1f;  // 쓰러지기 시작한 시각(쓰러지는 동작용)
            public int PendingHits;         // 판정은 났지만 아직 칼·투사체가 닿지 않은 공격 수(그동안 체력 막대·쓰러짐을 미룬다)
            public int ShownHealth = -1;    // 막대에 보이는 체력(맞을 때마다 그만큼 깎이고, 기다리는 공격이 없으면 실제 체력)
        }

        // ---------- 시체 뒤지기 ----------
        // 쓰러진 몬스터는 쓰러짐 동작 마지막 그림으로 남는다(시체). 누르면 머리 위 황금 진행률 바가 LootSeconds 동안 차고,
        // 다 차면 그 몬스터 한 마리 몫 전리품이 튀어나와 _lootedItems에 쌓인다(결과 창·가방 — ExpeditionBattle.Battle.PreLooted).
        // 앞 라운드 시체도 필드에 남는다(_corpses). 이기면 결과 창 전에 '전리품 수습' 시간(_salvaging).
        private const float LootSeconds = 1.2f;
        private const float LootedAlpha = 0.55f;   // 털린 시체
        private const float LootBarWidth = 110f;
        private const float LootBarHeight = 10f;
        private const float LootBarLift = 74f;     // 시체 발밑에서 바까지
        // 채움 칸이 테두리 그림 안에서 차지하는 비율(tools/bake_loot_bar.py 출력)
        private const float LootBarInsetLeft = 0.0442f, LootBarInsetRight = 0.0401f, LootBarInsetBottom = 0.2249f, LootBarInsetTop = 0.2308f;

        private class LootBar
        {
            public UnitView View;
            public RectTransform Root;
            public Image Fill;
            public float Progress;
        }

        private readonly Dictionary<FieldUnit, UnitView> _corpses = new Dictionary<FieldUnit, UnitView>(); // 앞 라운드 시체(지우지 않고 남김)
        private readonly HashSet<FieldUnit> _looted = new HashSet<FieldUnit>();
        private readonly Dictionary<FieldUnit, LootBar> _looting = new Dictionary<FieldUnit, LootBar>();
        private readonly List<(LootItem item, int count)> _lootedItems = new List<(LootItem, int)>();
        private readonly System.Random _lootRng = new System.Random();

        // 뒤진 시체 창: 다 뒤지면 나온 것을 창에 띄워 고른 것만 가져간다(전투는 멈추지 않는다). 여러 시체면 하나씩 차례로.
        private class LootChoice
        {
            public FieldUnit Unit;
            public UnitView View;
            public List<(LootItem item, int count)> Loot;
            public bool[] Picked; // 처음엔 전부 고름
        }
        private readonly Queue<LootChoice> _lootChoices = new Queue<LootChoice>();
        private LootChoice _lootChoiceShown;
        private GameObject _lootChoicePanel;
        private Button _lootTakeButton;  // 고른 게 남은 가방 칸보다 많으면 막는다
        private Text _lootChoiceNote;    // "가방이 n칸 모자랍니다"
        private Button _lootSwapButton;  // 모자란 만큼 가방의 싼 것을 버리고 가져가기(버릴 것이 고른 것보다 쌀 때만)
        private List<LootItem> _lootSwapDrops = new List<LootItem>();

        // 가방: 파견대 가방 칸 합(BagCatalog)만큼만 들고 온다. 쓴 칸 = 챙긴 것 개수(건설 재료·동전 주머니 빼고 — BagCatalog.UsesSlot).
        private int _bagCapacity;
        private int BagUsed => BagCatalog.SlotsUsed(_lootedItems);
        private int BagFree => Mathf.Max(0, _bagCapacity - BagUsed);
        private const float LootChoiceWidth = 380f, LootChoiceRowHeight = 42f, LootChoiceIcon = 34f;
        private const float LootChoiceMargin = 24f;       // 화면 오른쪽 끝에서
        private const float LootChoiceUnpickedAlpha = 0.35f;
        // 가방 창(B·오른쪽 위 [가방] 버튼, 화면 위 가운데): 대원마다 큰 칸 가방 카드(BagSlotsUI.CreateMercBagCard), 챙긴 것을 칸 순서대로 채운다.
        // 칸을 골라 [버리기]로 비운다.
        private const float BagWindowWidth = 900f, BagWindowTop = -100f;
        private Button _bagButton;
        private Text _bagButtonText;
        private GameObject _bagWindow;
        private int _bagSelected = -1;   // 고른 칸(BagSlotsUI.Units 순서), -1 = 없음
        private Sprite _lootFrameSprite, _lootFillSprite;
        private bool _salvaging;
        private bool _victoryBannerShown;
        private GameObject _salvageBar;
        private Text _salvageText;
        private float _salvageDoneAt = -1f;

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private readonly List<Mercenary> _leaderMercs = new List<Mercenary>(); // 결과 화면 ★ 표시(분대장들, 전투 중 쓰러져도 그대로)
        private FieldBattleSession _session;
        private readonly Dictionary<FieldUnit, UnitView> _views = new Dictionary<FieldUnit, UnitView>();
        private readonly List<UnitView> _drawOrder = new List<UnitView>();
        private bool _picking;      // 스킬 대상 고르는 중
        private bool _focusPicking; // 분대 명령 '집중 공격'의 대상 고르는 중
        private Button _resultBackButton; // 결과 화면 [마을로 돌아가기](Enter·Space)
        private float _resultBackAt;
        private bool _resultShown;
        private float _overSince = -1f;
        private AudioSource _music;     // 전투 브금(설정 창의 음악 볼륨을 따른다) // 전투가 끝난(한쪽이 다 쓰러진) 시각 — ResultDelaySeconds 뒤 결과창
        private bool _leaderDownNoticed;
        private FieldUnit _lastControlled; // 두 분대 전투: 지난 갱신 때 조종하던 분대장(쓰러져 넘어간 것을 알리려고)
        private bool _paused;       // Space: 전투 멈춤(멈춘 동안에도 분대장 명령은 내릴 수 있다)
        private bool _deploying;    // 배치 단계(전투 시작 전)
        private bool _cutIn;        // 보스 등장 컷인 중(전투 시간이 멈춘다)
        private readonly Dictionary<FieldUnit, Mercenary> _mercOf = new Dictionary<FieldUnit, Mercenary>();
        private readonly Dictionary<FieldUnit, System.Numerics.Vector2> _defaultPos = new Dictionary<FieldUnit, System.Numerics.Vector2>();
        private FieldUnit _dragUnit;
        private FieldUnit _hoverUnit;          // 배치 중 마우스가 올라간 아군(흰 테두리)
        private Vector2 _dragOffset;           // 잡은 곳과 발 위치의 차이(끄는 동안 유지)
        private System.Numerics.Vector2 _dragStart;
        private GameObject _deployZone;
        private Button _resetButton;

        private Canvas _canvas;
        private RectTransform _field;
        // 필드 안의 층: 땅 표시(이동 지점) → 유닛(앞뒤 정렬) → 효과(투사체·숫자). 모두 필드와 같은 좌표.
        private RectTransform _groundLayer;
        private RectTransform _unitLayer;
        private RectTransform _fxLayer;
        private Text _leaderLabel;
        private Text _hintLabel;
        private Text _timerLabel;
        private GameObject _pauseOverlay;
        private Text _logText;
        private readonly LinkedList<string> _logLines = new LinkedList<string>();
        private Button _skillButton;
        private Text _skillButtonText;

        private const int MaxLogLines = 9;
        private const float FieldBottom = 250f; // 아래 패널(230) 위
        public const float FieldZoom = 0.8f;    // 판(필드 좌표)을 이만큼 줄여 그린다 = 줌 아웃. 유닛·글자·이펙트가 판 안이라 함께 작아진다
        private static readonly Vector3 Unzoom = new Vector3(1f / FieldZoom, 1f / FieldZoom, 1f); // 판 안이지만 화면 크기 그대로 둘 것(일시정지 글자·분대 명령)
        private const int UnitNameSize = 16;    // 머리 위 이름(판 배율로 화면에선 약 13)
        private const int UnitHpTextSize = 13;  // 체력 막대 안 수(화면에선 약 10)
        private const float PauseDimMargin = 4000f; // 일시정지 어두운 막이 판 밖으로 뻗는 여유(화면 끝까지)
        private const float PortraitSize = 110f;
        private const float MonsterSize = 132f; // 몬스터 그림 칸(고블린이 용병과 비슷한 키로 보이는 크기)
        private const float MonsterHeadRatio = 0.85f; // 칸 높이 중 머리 꼭대기까지의 비율(고블린 그림 실측)
        private const float ProjectileSpeed = 1400f;
        private const float ArrowLength = 56f; // 고블린 궁수 화살 그림 길이(높이는 그림 비율대로)
        private const string MusicPath = "Music/전투 브금 1";
        private const float MusicVolume = 0.6f;
        private const string SwordSound = "검격";         // Resources/Sfx: 검격 애니메이션이 시작될 때
        private const string MonsterHitSound = "몬스터 피격"; // Resources/Sfx: 몬스터가 실제로 맞았을 때
        /// <summary>전투씬 전체 속도(Time.timeScale). 이동·공격·쿨타임·애니메이션·연출이 같은 비율로 느려져 밸런스는 그대로.</summary>
        private const float BattleSpeed = 0.95f; // 2026-10-26 0.7 → 0.95: 전투가 너무 길다는 요청(CombatFormulas.DamageScale 상향과 합쳐 약 절반)
        /// <summary>1·2·3배속(버튼·F). static이라 다음 전투에도 이어진다.</summary>
        private static int _speed = 1;
        /// <summary>지금 전투 시간 배율 = BattleSpeed × 배속.</summary>
        private static float TimeScale => BattleSpeed * _speed;
        private Button _speedButton;
        private Text _speedButtonText;
        private static readonly Color SpeedFastColor = new Color(1f, 0.75f, 0.35f); // 2배속이면 버튼을 주황빛으로
        private static readonly Color SpeedFasterColor = new Color(1f, 0.5f, 0.3f); // 3배속은 더 진한 붉은 주황

        // 타격감 연출(보이는 것만, 판정과 무관)
        private const float BigHitRatio = 0.2f;          // 대상 최대 체력의 이 비율 이상이면 '큰 피해'로 강조
        private const float KickDistance = 12f, BigKickDistance = 18f;
        private const float KickRecoverPerSecond = 120f;
        private const float DeathFallSeconds = 0.4f;
        private const float ResultDelaySeconds = 1.5f;   // 한쪽이 다 쓰러진 뒤 결과창까지(전투 시간): 마지막 쓰러짐을 보여 준다
        private const float ShakeStrength = 4f, ShakeSeconds = 0.15f;
        private static readonly Color BigHitColor = new Color(1f, 0.78f, 0.25f);
        private static readonly Color BlockColor = new Color(0.7f, 0.82f, 0.95f); // 방패로 막은 피해(강철빛)
        private RectTransform[] _shakeTargets;

        private static readonly Color LeaderColor = new Color(1f, 0.82f, 0.3f);
        private static readonly Color PickingColor = new Color(0.95f, 0.9f, 0.35f);

        public void Init(Expedition expedition)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _expedition = expedition;
            var quest = expedition.Quest;

            _fighters = expedition.Members.Where(m => m.IsAlive).ToList();
            _bagCapacity = BagCatalog.CapacityOf(_fighters); // 전투 중 쓰러진 대원 몫도 동료가 들고 온다
            var combatants = _fighters.Select(m => m.ToCombatant()).ToList();
            var melee = _fighters.Select(m => IsMeleeClass(m.Class.Kind)).ToList();
            var skills = _fighters.Select(m => ClassSkillFactory.Create(m.Class.Kind)).ToList();
            int leaderIndex = Math.Max(0, _fighters.IndexOf(expedition.Leader));
            // 두 분대 퀘스트(Lv5 앤트킹): 분대마다 분대장, Tab으로 조종할 분대장을 바꾼다
            bool twoSquads = quest.SquadCount > 1 && expedition.LeaderOf(1) != null;
            var squads = twoSquads ? _fighters.Select(m => expedition.SquadOf(m)).ToList() : null;
            int leaderIndex2 = twoSquads ? _fighters.IndexOf(expedition.LeaderOf(1)) : -1;
            _leaderMercs.Add(expedition.Leader);
            if (twoSquads) _leaderMercs.Add(expedition.LeaderOf(1));

            var rng = new System.Random();
            var waves = Enumerable.Range(0, quest.WaveCount).Select(w => ExpeditionBattle.SpawnWave(quest, w, rng)).ToList();

            _session = new FieldBattleSession(combatants, melee, skills, leaderIndex, waves, rng, squads, leaderIndex2);
            _session.OnWaveCleared += OnWaveCleared;
            _session.OnWaveStarted += OnWaveStarted;
            _session.OnSlamTelegraph += cast => StartCoroutine(SlamTelegraph(cast));
            _session.OnRoarTelegraph += cast => StartCoroutine(RoarTelegraph(cast));
            _session.OnSkillUsed += (unit, skill) =>
            {
                if (_views.TryGetValue(unit, out var v)) StartCoroutine(FloatText(v, skill.Name, new Color(0.6f, 0.85f, 1f), 175f));
                AppendLog($"<color=#9fd0ff>{unit.Combatant.Name} — {skill.Name}</color>");
            };

            BuildUI(quest);
            _shakeTargets = new[] { _field, _canvas.transform.Find("Surroundings/Outskirts") as RectTransform }; // 판과 바깥 잔디를 같이 흔든다
            foreach (var unit in _session.Units)
            {
                int i = combatants.IndexOf(unit.Combatant);
                var view = unit.IsPlayerSide
                    ? CreateUnitView(unit, _fighters[i].Appearance, -1)
                    : CreateUnitView(unit, null, quest.Waves[0].Tier);
                _views[unit] = view;
                _drawOrder.Add(view);
                if (unit.IsPlayerSide)
                {
                    _mercOf[unit] = _fighters[i];
                    _defaultPos[unit] = unit.Position;
                    if (BattleFormation.TryGet(_fighters[i].Id, out var saved)) // 지난 전투에서 정한 자리
                        _session.PlaceForDeploy(unit, new System.Numerics.Vector2(saved.x, saved.y));
                }
            }

            PlayMusic();
            Time.timeScale = TimeScale;

            _deploying = true;
            BuildDeployZone();
            AppendLog("배치 — 대원을 끌어 자리를 정한 뒤 '전투 시작'(Space)");
            if (quest.WaveCount > 1)
                AppendLog($"라운드 {quest.WaveCount}개: {QuestDifficulty.WavesText(quest)} · 체력은 라운드 사이에 이어집니다");
            foreach (var v in _views.Values) UpdateUnitView(v);
            SortByDepth();
            RefreshDeployPanel();
        }

        // ---------- 라운드 ----------

        private void OnWaveCleared(int wave)
        {
            var quest = _expedition.Quest;
            var next = quest.Waves[wave + 1];
            AppendLog($"<color=#ffd36b>{wave + 1}라운드 돌파! 다음: {(next.IsBoss ? "보스 " : "")}{next.EnemyName} ×{next.Count}</color>");
            StartCoroutine(Banner($"{wave + 1}라운드 돌파!", $"다음 — {(next.IsBoss ? "보스 " : "")}{next.EnemyName} ×{next.Count}",
                new Color(1f, 0.85f, 0.4f)));
        }

        /// <summary>쓰러진 앞 라운드 적을 치우고 새 적을 그린다(아군은 그대로).</summary>
        private void OnWaveStarted(int wave, List<FieldUnit> removed, List<FieldUnit> added)
        {
            foreach (var u in removed)
            {
                if (!_views.TryGetValue(u, out var v)) continue;
                _views.Remove(u);
                if (v.BossHud != null) Destroy(v.BossHud.gameObject);
                if (!u.IsPlayerSide && !u.IsAlive)
                {
                    _corpses[u] = v; // 시체는 필드에 남겨 뒤질 수 있게(그림은 쓰러짐 마지막 프레임에 멈춰 있다)
                    bool looted = _looted.Contains(u);
                    v.Group.alpha = looted ? LootedAlpha : 1f;
                    v.ClickArea.raycastTarget = !looted;
                    continue;
                }
                _drawOrder.Remove(v);
                Destroy(v.Root.gameObject);
            }
            int tier = _expedition.Quest.Waves[wave].Tier;
            foreach (var u in added)
            {
                var v = CreateUnitView(u, null, tier);
                _views[u] = v;
                _drawOrder.Add(v);
            }
            var w = _expedition.Quest.Waves[wave];
            bool last = wave == _session.WaveCount - 1;
            string bossTitle = w.IsBoss ? GN3.World.RegionInfo.BossTitle(w.EnemyName) : null; // "은월의 숲의 주인"
            string bossName = bossTitle != null ? $"{bossTitle}, {w.EnemyName}" : w.EnemyName;
            AppendLog($"<color=#ffd36b>{wave + 1}라운드 시작 — {(w.IsBoss ? $"보스 {bossName}" : w.EnemyName)} ×{w.Count}</color>");
            if (w.IsBoss && StartBossCutIn()) return; // 그림이 있는 보스는 컷인이 배너를 대신하고, 끝나면 포효
            StartCoroutine(Banner(w.IsBoss ? "보스 등장!" : last ? $"마지막 {wave + 1}라운드" : $"{wave + 1}라운드",
                w.IsBoss ? $"{wave + 1}라운드 — {bossName}" : $"{w.EnemyName} ×{w.Count}",
                w.IsBoss ? new Color(1f, 0.45f, 0.4f) : Color.white));
            if (w.IsBoss) PlayBossIntros();
        }

        /// <summary>
        /// 컷인 그림이 있는 보스(거목 엔트)면 등장 컷인(BossCutIn)을 띄우고 true. 컷인 동안은 전투가 멈추고(_cutIn),
        /// 끝나면 필드에서 포효(PlayBossIntros)하며 전투가 이어진다. 그림이 없으면 false(배너로).
        /// </summary>
        private bool StartBossCutIn()
        {
            var boss = _views.Values.FirstOrDefault(v => !v.Unit.IsPlayerSide && v.BossHud != null && v.Monster != null);
            var portrait = boss != null ? BattleMonsterSprite.PortraitFor(boss.Monster.Kind) : null;
            if (portrait == null) return false;
            string name = boss.Unit.Combatant.Name;
            StartCoroutine(PlayBossCutIn(portrait, GN3.World.RegionInfo.BossTitle(name), name));
            return true;
        }

        private IEnumerator PlayBossCutIn(Sprite portrait, string title, string name)
        {
            _cutIn = true;
            yield return BossCutIn.Play(_canvas.transform, portrait, title, name,
                () => { if (_shakeTargets != null) StartCoroutine(BattleFx.Shake(_shakeTargets, ShakeStrength, ShakeSeconds)); });
            _cutIn = false;
            PlayBossIntros();
        }

        /// <summary>보스 컷인이 끝난 뒤에 이어서 튼다(첫 라운드가 보스면 "전투 시작!" 배너가 컷인 뒤에 보이게).</summary>
        private IEnumerator AfterCutIn(IEnumerator next)
        {
            while (_cutIn) yield return null;
            yield return next;
        }

        /// <summary>
        /// 보스 등장: 등장 동작이 있는 보스(거목 엔트 포효)는 그 자리에서 포효한다. 포효하는 동안은 움직이지 않는다.
        /// 거목 엔트는 첫째 포효 소리(BossRoarAudio.Intro)와 싱크: 소리가 크게 터지는 순간 = 입을 가장 크게 벌린 장, 큰 소리가 이어지는 동안 그 장을 유지하고
        /// 판이 우르릉 흔들린다. 소리는 실제 시간이라 전투 배속(TimeScale)을 곱해 전투 시간으로 맞춘다.
        /// </summary>
        private void PlayBossIntros()
        {
            foreach (var v in _views.Values)
            {
                if (v.Unit.IsPlayerSide || v.Monster == null || v.Monster.IntroSeconds <= 0f) continue;
                bool voiced = v.Monster.Kind == MonsterKind.Ent;
                float hold = voiced ? BossRoarAudio.Intro.LoudLength * TimeScale : 0f;
                v.Unit.BusyTimer = v.Monster.PlayIntro(hold);
                float peak = v.Monster.IntroPeakDelay; // 입을 가장 크게 벌린 장까지(전투 초)
                if (voiced)
                {
                    StartCoroutine(PlayRoarSound(BossRoarAudio.IntroClip, Mathf.Max(0f, peak - BossRoarAudio.Intro.Onset * TimeScale)));
                    StartCoroutine(RumbleAfter(peak, ShakeStrength * 1.8f, hold + 0.25f));
                }
                else StartCoroutine(ShakeAfter(peak));
            }
        }

        /// <summary>delay(전투 초) 뒤에 포효 소리를 튼다.</summary>
        private IEnumerator PlayRoarSound(AudioClip clip, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            GameAudio.PlayClip(clip);
        }

        /// <summary>delay(전투 초) 뒤에 판을 묵직하게 우르릉 흔든다(포효).</summary>
        private IEnumerator RumbleAfter(float delay, float strength, float duration)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (_shakeTargets != null) yield return BattleFx.Shake(_shakeTargets, strength, duration, rumble: true);
        }

        /// <summary>필드 위쪽 가운데에 큰 글자로 잠깐 띄운다(나타났다 사라짐).</summary>
        private IEnumerator Banner(string title, string sub, Color color)
        {
            var label = CreateText(_fxLayer, $"{title}\n<size=22>{sub}</size>", 44, TextAnchor.MiddleCenter, color);
            label.fontStyle = FontStyle.Bold;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -60f);
            rect.sizeDelta = new Vector2(1000f, 110f);
            var group = label.gameObject.AddComponent<CanvasGroup>();

            const float duration = 1.8f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (label == null) yield break;
                float k = t / duration;
                group.alpha = k < 0.15f ? k / 0.15f : k > 0.75f ? (1f - k) / 0.25f : 1f;
                rect.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(k / 0.15f));
                yield return null;
            }
            if (label != null) Destroy(label.gameObject);
        }

        // ---------- 배치 ----------

        /// <summary>배치를 마치고 전투를 시작한다. 정한 자리는 용병별로 기억해 다음 전투에서 그대로 쓴다.</summary>
        private void BeginBattle()
        {
            if (!_deploying) return;
            EventSystem.current?.SetSelectedGameObject(null);
            _deploying = false;
            _dragUnit = null;
            _hoverUnit = null;
            UpdateDeployOutlines(); // 테두리 끄기
            foreach (var kv in _mercOf)
                BattleFormation.Remember(kv.Value.Id, new Vector2(kv.Key.Position.X, kv.Key.Position.Y));
            _session.Begin();
            if (_expedition.Quest.Waves[0].IsBoss && !StartBossCutIn()) PlayBossIntros(); // 첫 라운드부터 보스(그림이 있으면 컷인 뒤 포효)
            if (_deployZone != null) StartCoroutine(BattleFx.FadeOutAndDestroy(_deployZone, 0.4f));
            _deployZone = null;
            _resetButton.gameObject.SetActive(false);

            var leader = _session.Leader;
            StartCoroutine(AfterCutIn(Banner("전투 시작!", leader != null ? $"분대장 {leader.Combatant.Name} 지휘" : "", new Color(1f, 0.85f, 0.4f))));
            AppendLog(leader != null
                ? $"전투 시작! 분대장 {leader.Combatant.Name} 지휘 — 땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬"
                : "전투 시작!");
            RefreshPanel();
        }

        /// <summary>마우스를 올린(또는 끌고 있는) 아군에게만 마을 건물처럼 흰 테두리가 반짝인다 — 배치 중에도, 전투 중(끌어서 명령)에도.</summary>
        private void UpdateDeployOutlines()
        {
            bool canDrag = _deploying || (_session.HasStarted && !_session.IsOver && !_resultShown);
            foreach (var v in _views.Values)
                v.Animator?.SetOutline(canDrag && v.Unit.IsAlive && (v.Unit == _hoverUnit || v.Unit == _dragUnit));
        }

        private void ResetFormation()
        {
            EventSystem.current?.SetSelectedGameObject(null);
            foreach (var kv in _defaultPos) _session.PlaceForDeploy(kv.Key, kv.Value);
        }

        private void RefreshDeployPanel()
        {
            _timerLabel.text = "배치 중";
            var leader = _session.Leader;
            _leaderLabel.text = leader != null ? $"배치 — ★ 분대장 {leader.Combatant.Name}" : "배치";
            _skillButton.interactable = true;
            _skillButtonText.text = "전투 시작 (Space)";
            _hintLabel.text = "대원을 끌어 밝은 구역 안에 놓으세요 · 대원 위에 놓으면 자리 바꿈\n근접은 앞, 원거리·힐러는 뒤가 안전합니다";
        }

        private bool TryFieldPoint(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_field, e.position, e.pressEventCamera, out local);

        private void OnDragBegin(FieldUnit unit, PointerEventData e)
        {
            if (!TryFieldPoint(e, out var local)) return;
            if (!_deploying)
            {
                BeginOrderDrag(unit, local);
                return;
            }
            _dragUnit = unit;
            _dragStart = unit.Position;
            _dragOffset = new Vector2(unit.Position.X, unit.Position.Y) - local;
        }

        private void OnDragMove(FieldUnit unit, PointerEventData e)
        {
            if (_dragUnit != unit || !TryFieldPoint(e, out var local)) return;
            if (!_deploying)
            {
                UpdateOrderDrag(local + _dragOffset);
                return;
            }
            var p = local + _dragOffset;
            _session.PlaceForDeploy(unit, new System.Numerics.Vector2(p.x, p.y));
        }

        /// <summary>놓은 자리에 다른 대원이 있으면 둘의 자리를 바꾼다(겹쳐 서지 않게).</summary>
        private void OnDragEnd(FieldUnit unit, PointerEventData e)
        {
            if (_dragUnit != unit) return;
            if (!_deploying)
            {
                EndOrderDrag(unit, e);
                return;
            }
            _dragUnit = null;
            const float swapRadius = 60f;
            var other = _session.Units
                .Where(o => o != unit && o.IsPlayerSide && System.Numerics.Vector2.Distance(o.Position, unit.Position) < swapRadius)
                .OrderBy(o => System.Numerics.Vector2.Distance(o.Position, unit.Position))
                .FirstOrDefault();
            if (other != null) _session.PlaceForDeploy(other, _dragStart);
        }

        // ---------- 전투 중 대원 끌어 놓기 ----------
        // 아군을 끌어 땅에 놓으면 그 자리로 걸어가 지키고(분대장은 땅 클릭 이동과 같음), 적 위에 놓으면 그 적을 공격한다.
        // 끄는 동안 대원에서 놓을 곳까지 점선과 고리가 따라다닌다(공격이면 붉게). 일시정지 중에도 된다.

        private const float DropOnEnemyRadius = 70f; // 놓은 곳에서 이만큼 안에 적 발이 있으면 '그 적 공격'
        private static readonly Color OrderMoveColor = new Color(0.5f, 1f, 0.6f, 0.9f);
        private static readonly Color OrderAttackColor = new Color(1f, 0.4f, 0.35f, 0.95f);
        private RectTransform _orderLine;
        private Image _orderLineImage;
        private RectTransform _orderRing;
        private Image _orderRingImage;
        private Vector2 _orderDragPoint; // 마지막으로 가리킨 땅(마우스가 멈춰도 대원이 움직이면 점선을 다시 그린다)

        private bool CanOrderDrag(FieldUnit unit) =>
            _session.HasStarted && !_session.IsOver && !_resultShown && !_cutIn && unit.IsAlive;

        private void BeginOrderDrag(FieldUnit unit, Vector2 local)
        {
            if (!CanOrderDrag(unit)) return;
            _picking = false;
            _dragUnit = unit;
            _dragOffset = Vector2.zero; // 놓을 곳 = 마우스가 가리키는 땅
            EnsureOrderPreview();
            UpdateDeployOutlines();
            _orderLine.gameObject.SetActive(true);
            _orderRing.gameObject.SetActive(true);
            UpdateOrderDrag(local);
        }

        private void UpdateOrderDrag(Vector2 point)
        {
            if (_dragUnit == null || _orderLine == null) return;
            if (!CanOrderDrag(_dragUnit))
            {
                // 끄는 사이 쓰러졌거나 전투가 끝났다
                _dragUnit = null;
                HideOrderPreview();
                UpdateDeployOutlines();
                return;
            }
            _orderDragPoint = point;
            var clamped = FieldBattleSession.ClampToField(new System.Numerics.Vector2(point.x, point.y));
            var to = new Vector2(clamped.X, clamped.Y);
            var from = new Vector2(_dragUnit.Position.X, _dragUnit.Position.Y);
            var enemy = EnemyAtDrop(to);
            if (enemy != null) to = new Vector2(enemy.Position.X, enemy.Position.Y);
            var color = enemy != null ? OrderAttackColor : OrderMoveColor;

            var dir = to - from;
            _orderLine.anchoredPosition = from;
            _orderLine.sizeDelta = new Vector2(dir.magnitude, 4f);
            _orderLine.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            _orderLineImage.color = new Color(color.r, color.g, color.b, 0.55f);
            _orderRing.anchoredPosition = to;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f);
            _orderRing.sizeDelta = new Vector2(56f, 26f) * (enemy != null ? 1.4f : 1f) * pulse;
            _orderRingImage.color = color;
        }

        private void EndOrderDrag(FieldUnit unit, PointerEventData e)
        {
            _dragUnit = null;
            HideOrderPreview();
            UpdateDeployOutlines();
            if (!CanOrderDrag(unit) || !TryFieldPoint(e, out var local)) return;
            var clamped = FieldBattleSession.ClampToField(new System.Numerics.Vector2(local.x, local.y));
            var enemy = EnemyAtDrop(new Vector2(clamped.X, clamped.Y));
            string name = unit.Combatant.Name;
            if (enemy != null)
            {
                if (_session.OrderUnitAttack(unit, enemy))
                    AppendLog($"<color=#ff8a7a>{Josa.EulReul(name)} {enemy.Combatant.Name} 공격 지시</color>");
                return;
            }
            if (!_session.OrderUnitMove(unit, clamped)) return;
            StartCoroutine(MoveMarker(new Vector2(clamped.X, clamped.Y)));
            if (unit != _session.Leader) AppendLog($"<color=#8affa0>{Josa.IGa(name)} 이동 — 도착하면 그 자리를 지킵니다</color>");
        }

        /// <summary>놓은 곳 가까이에 서 있는 살아 있는 적(가장 가까운 하나).</summary>
        private FieldUnit EnemyAtDrop(Vector2 point) => _session.Units
            .Where(u => !u.IsPlayerSide && u.IsAlive)
            .Select(u => (u, d: Vector2.Distance(point, new Vector2(u.Position.X, u.Position.Y)) - u.BodyRadius))
            .Where(x => x.d < DropOnEnemyRadius)
            .OrderBy(x => x.d)
            .Select(x => x.u)
            .FirstOrDefault();

        private void EnsureOrderPreview()
        {
            if (_orderLine != null) return;
            var lineGO = new GameObject("OrderLine", typeof(RectTransform), typeof(Image));
            lineGO.transform.SetParent(_groundLayer, false);
            _orderLine = (RectTransform)lineGO.transform;
            _orderLine.anchorMin = _orderLine.anchorMax = Vector2.zero;
            _orderLine.pivot = new Vector2(0f, 0.5f);
            _orderLineImage = lineGO.GetComponent<Image>();
            _orderLineImage.raycastTarget = false;

            var ringGO = new GameObject("OrderRing", typeof(RectTransform), typeof(Image));
            ringGO.transform.SetParent(_groundLayer, false);
            _orderRing = (RectTransform)ringGO.transform;
            _orderRing.anchorMin = _orderRing.anchorMax = Vector2.zero;
            _orderRingImage = ringGO.GetComponent<Image>();
            _orderRingImage.sprite = BattlePlaceholderSprites.Ring;
            _orderRingImage.raycastTarget = false;
        }

        private void HideOrderPreview()
        {
            if (_orderLine != null) _orderLine.gameObject.SetActive(false);
            if (_orderRing != null) _orderRing.gameObject.SetActive(false);
        }

        /// <summary>필드 왼쪽의 밝은 배치 구역(옅어지는 바탕·흐르는 점선 경계·글자, BattleFx). 클릭은 막지 않는다.</summary>
        private void BuildDeployZone()
        {
            _deployZone = BattleFx.CreateDeployZone(_groundLayer, FieldBattleSession.DeployMaxX + 40f);
        }

        /// <summary>전투 배경음(Resources/Music). 이 오브젝트에 붙어 있어 씬을 떠나면 같이 멈춘다.</summary>
        private void PlayMusic()
        {
            var clip = Resources.Load<AudioClip>(MusicPath);
            if (clip == null)
            {
                Debug.LogWarning($"[BattleUIController] 전투 배경음을 찾지 못했습니다: Resources/{MusicPath}");
                return;
            }
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = clip;
            _music.loop = true;
            _music.playOnAwake = false;
            ApplyMusicVolume();
            GameAudio.OnChanged += ApplyMusicVolume; // 설정 창의 음악 볼륨을 바로 따른다
            _music.Play();
        }

        private void ApplyMusicVolume()
        {
            if (_music != null) _music.volume = MusicVolume * GameAudio.MusicVolume;
        }

        /// <summary>전사·암살자처럼 몸으로 부딪히는 직업만 근접, 나머지는 원거리.</summary>
        private static bool IsMeleeClass(MercenaryClassKind kind) =>
            kind == MercenaryClassKind.Warrior || kind == MercenaryClassKind.Assassin;

        // ---------- 진행 ----------

        private void Update()
        {
            if (_session == null) return;
            if (_resultShown)
            {
                foreach (var v in _views.Values) UpdateUnitView(v); // 마지막에 쓰러진 유닛도 끝까지 넘어가게(그림만)
                // 결과 화면: Enter·Space로 [마을로 돌아가기]
                var key = Keyboard.current;
                if (_resultBackButton != null && key != null && Time.unscaledTime - _resultBackAt > 0.6f // 뜨자마자 누르던 키로 넘어가지 않게
                    && (key.enterKey.wasPressedThisFrame || key.numpadEnterKey.wasPressedThisFrame || key.spaceKey.wasPressedThisFrame))
                    _resultBackButton.onClick.Invoke();
                return;
            }
            UpdateLooting();
            var bagKey = Keyboard.current;
            if (bagKey != null && bagKey.bKey.wasPressedThisFrame && !_cutIn) ToggleBagWindow();
            if (_salvaging)
            {
                UpdateSalvage();
                return;
            }

            if (_deploying)
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) BeginBattle();
                foreach (var v in _views.Values) UpdateUnitView(v);
                UpdateDeployOutlines();
                SortByDepth();
                if (_deploying) RefreshDeployPanel();
                else RefreshPanel();
                return;
            }

            if (_cutIn)
            {
                // 보스 등장 컷인 중: 전투 시간은 멈추고(세션 Tick 없음, 키 입력은 컷인이 받는다) 그림만 숨 쉰다
                foreach (var v in _views.Values) UpdateUnitView(v);
                return;
            }

            HandleKeys();
            var events = _session.Tick(Time.deltaTime);
            if (events.Count > 0) ApplyEventsVisual(events);

            foreach (var v in _views.Values) UpdateUnitView(v);
            SortByDepth();
            if (_dragUnit != null) UpdateOrderDrag(_orderDragPoint);
            UpdateDeployOutlines();
            RefreshSquadOrderBar();

            if (_session.IsOver)
            {
                // 결과창은 바로 띄우지 않고 마지막 쓰러짐·피격 연출이 끝날 때까지 잠깐 기다린다(일시정지 중엔 기다림도 멈춤).
                if (_overSince < 0f) _overSince = Time.time;
                if (Time.time - _overSince < ResultDelaySeconds) return;
                // 이기면 결과 창 전에 남은 시체를 뒤질 '전리품 수습' 시간
                // (가방이 꽉 찼으면 남은 시체는 못 뒤지니 고를 창이 남았을 때만)
                if (_session.Outcome == BattleOutcome.TeamAVictory && ((RemainingCorpses() > 0 && BagFree > 0) || _lootChoiceShown != null)) BeginSalvage();
                else ShowResult();
                return;
            }
            RefreshPanel();
        }

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.qKey.wasPressedThisFrame) PressSkill();
            if (kb.escapeKey.wasPressedThisFrame) { _picking = false; _focusPicking = false; }
            // 분대 명령 1~4: 멈추지 않아도 바로(일시정지 화면 버튼과 같은 명령·쿨타임)
            if (!_resultShown && !_salvaging)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Rally);
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Retreat);
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Advance);
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Focus);
            }
            if (kb.spaceKey.wasPressedThisFrame) SetPaused(!_paused);
            if (kb.tabKey.wasPressedThisFrame) SwitchSquad();
            if (kb.fKey.wasPressedThisFrame) ToggleSpeed();
        }

        /// <summary>1배속 → 2배속 → 3배속 → 1배속. 멈춘 동안 바꾸면 풀릴 때 적용된다.</summary>
        private void ToggleSpeed()
        {
            if (_resultShown) return;
            _speed = _speed % 3 + 1;
            if (!_paused) Time.timeScale = TimeScale;
            UpdateSpeedButton();
        }

        private void UpdateSpeedButton()
        {
            if (_speedButton == null) return;
            _speedButtonText.text = $"{_speed}배속 (F)";
            _speedButton.GetComponent<Image>().color = _speed == 1 ? Color.white : _speed == 2 ? SpeedFastColor : SpeedFasterColor;
        }

        /// <summary>두 분대 전투: Tab으로 조종할 분대장을 바꾼다(넘겨준 분대장은 알아서 싸운다).</summary>
        private void SwitchSquad()
        {
            if (_deploying || !_session.HasTwoSquads || !_session.SwitchLeader()) return;
            _picking = false;
            var leader = _session.Leader;
            AppendLog($"<color=#ffd34a>{leader.Squad + 1}분대장 {leader.Combatant.Name} 조종</color>");
            RefreshPanel();
        }

        // ---------- 입력 ----------

        /// <summary>Time.timeScale로 전투(세션 Tick·애니메이션·연출)를 통째로 멈춘다. 배경음은 계속.</summary>
        private void SetPaused(bool paused)
        {
            _paused = paused && !_resultShown;
            if (!_paused) _focusPicking = false;
            Time.timeScale = _paused ? 0f : TimeScale;
            _pauseOverlay.SetActive(_paused);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f; // 멈춘 채 씬을 떠나도 마을 시간이 멈춰 있지 않게
            GameAudio.OnChanged -= ApplyMusicVolume;
        }

        /// <summary>아래 큰 버튼: 배치 중엔 '전투 시작', 전투 중엔 분대장 스킬.</summary>
        private void OnMainButton()
        {
            if (_deploying) BeginBattle();
            else PressSkill();
        }

        /// <summary>스킬 버튼·Q: 공격 스킬은 지금 노리는 적이 있으면 바로 그 적에게, 없으면 대상 고르기.
        /// 회복 스킬은 대상 고르기(고르는 중에 한 번 더 누르면 자기 자신).</summary>
        private void PressSkill()
        {
            // 스킬 버튼을 눌러 선택된 채로 두면 Space가 버튼을 다시 누르는 것으로 먹힐 수 있어 선택을 푼다
            EventSystem.current?.SetSelectedGameObject(null);
            if (!_session.LeaderSkillReady) return;
            var leader = _session.Leader;
            if (leader.Skill.TargetType == SkillTargetType.Ally)
            {
                if (_picking) { _session.OrderSkill(leader); _picking = false; }
                else _picking = true;
                return;
            }
            if (!_picking && leader.Target != null && leader.Target.IsAlive && _session.OrderSkill(leader.Target)) return;
            _picking = !_picking;
        }

        private void OnGroundClicked(PointerEventData e)
        {
            if (_deploying) return;
            if (_focusPicking)
            {
                _focusPicking = false; // 빈 땅을 누르면 집중 공격 대상 고르기 취소
                return;
            }
            if (_picking && e.button == PointerEventData.InputButton.Left)
            {
                _picking = false; // 빈 땅을 누르면 대상 고르기 취소
                return;
            }
            _picking = false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_field, e.position, e.pressEventCamera, out var local)) return;
            _session.OrderMove(new System.Numerics.Vector2(local.x, local.y));
            if (_session.Leader != null && _session.Leader.IsAlive)
                StartCoroutine(MoveMarker(local));
        }

        private void OnUnitClicked(FieldUnit unit, PointerEventData e)
        {
            if (_deploying) return;
            if (!unit.IsAlive)
            {
                BeginLoot(unit); // 쓰러진 몬스터: 뒤지기
                return;
            }
            if (_salvaging || _resultShown) return;
            if (_focusPicking)
            {
                if (!unit.IsPlayerSide && _session.OrderFocus(unit))
                {
                    _focusPicking = false;
                    AppendLog($"<color=#ffd34a>분대 명령: 집중 공격 — 모두 {unit.Combatant.Name}에게</color>");
                }
                return;
            }
            if (_picking)
            {
                if (_session.OrderSkill(unit)) _picking = false;
                return;
            }
            if (!unit.IsPlayerSide) _session.OrderAttack(unit);
            else OnGroundClicked(e); // 아군을 누르면 그 자리로 이동
        }

        // ---------- 화면 갱신 ----------

        private void RefreshPanel()
        {
            var leader = _session.Leader;
            int secs = Mathf.FloorToInt(_session.Elapsed);
            _timerLabel.text = _session.WaveCount > 1
                ? $"라운드 {_session.CurrentWave + 1}/{_session.WaveCount}    {secs / 60}:{secs % 60:00}"
                : $"{secs / 60}:{secs % 60:00}";

            if (leader == null || !leader.IsAlive)
            {
                _picking = false;
                _leaderLabel.text = leader == null ? "분대장 없음" : $"★ {leader.Combatant.Name} 쓰러짐";
                _hintLabel.text = "남은 대원이 알아서 싸웁니다";
                _skillButton.interactable = false;
                _skillButtonText.text = "—";
                if (leader != null && !_leaderDownNoticed)
                {
                    _leaderDownNoticed = true;
                    AppendLog($"<color=#ff8a7a>분대장 {leader.Combatant.Name}이(가) 쓰러졌다! 남은 대원이 자동으로 싸운다</color>");
                }
                return;
            }

            var c = leader.Combatant;
            bool canSwitch = _session.HasTwoSquads && _session.Leaders.Any(l => l != leader && l.IsAlive);
            _leaderLabel.text = _session.HasTwoSquads
                ? $"★ {leader.Squad + 1}분대장 {c.Name}   체력 {c.CurrentHealth}/{c.Stats.MaxHealth}" + (canSwitch ? "   (Tab: 분대 전환)" : "")
                : $"★ 분대장 {c.Name}   체력 {c.CurrentHealth}/{c.Stats.MaxHealth}";
            if (_session.HasTwoSquads && leader != _lastControlled && _lastControlled != null && !_lastControlled.IsAlive)
                AppendLog($"<color=#ff8a7a>{_lastControlled.Squad + 1}분대장 {_lastControlled.Combatant.Name}이(가) 쓰러졌다! {leader.Squad + 1}분대장 {c.Name}이(가) 지휘를 이어받는다</color>");
            _lastControlled = leader;

            var skill = leader.Skill;
            if (skill == null)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = "전투 스킬 없음";
            }
            else if (leader.Cooldown > 0f)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = $"[Q] {skill.Name} ({Mathf.CeilToInt(leader.Cooldown)})";
            }
            else
            {
                _skillButton.interactable = true;
                _skillButtonText.text = _picking ? $"{skill.Name} — 대상 선택" : $"[Q] {skill.Name}";
            }

            _hintLabel.text = _focusPicking
                ? "집중 공격할 적을 클릭 (Esc·빈 땅: 취소)"
                : _picking
                ? (skill != null && skill.TargetType == SkillTargetType.Ally
                    ? "치료할 아군을 클릭 (Q: 자신 · Esc: 취소)"
                    : "스킬을 쓸 적을 클릭 (Esc: 취소)")
                : "땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬 · Space: 일시정지" + (_session.HasTwoSquads ? " · Tab: 분대 전환" : "")
                    + "\n대원을 끌어 땅에 놓으면 그 자리로 · 적 위에 놓으면 그 적 공격";
        }

        private void UpdateUnitView(UnitView v)
        {
            var u = v.Unit;
            bool alive = u.IsAlive || v.PendingHits > 0; // 마지막 칼이 닿기 전엔 서 있다
            v.Root.anchoredPosition = new Vector2(u.Position.X, u.Position.Y);

            // 쓰러짐: 0.4초 동안 옆으로 넘어가며 흐려진다.
            // 쓰러짐 그림이 있는 고블린은 그림이 직접 무릎 꿇으므로 눕히지 않고, 그 동작이 끝난 뒤에 흐려진다.
            if (!alive && v.DeathStart < 0f) v.DeathStart = Time.time;
            if (alive) v.DeathStart = -1f;
            bool animatedDeath = v.Monster != null && v.Monster.HasDie;
            float deathTime = alive ? 0f : Time.time - v.DeathStart - (animatedDeath ? v.Monster.DieSeconds : 0f);
            float fall = alive ? 0f : 1f - Mathf.Pow(1f - Mathf.Clamp01(deathTime / DeathFallSeconds), 3f);
            bool corpse = !alive && !u.IsPlayerSide;
            bool looted = _looted.Contains(u);
            // 몬스터 시체는 쓰러진 그림 그대로 남고(털리면 조금 흐려짐), 쓰러짐이 끝나면 눌러 뒤질 수 있다. 아군은 흐려진다.
            v.Group.alpha = corpse ? (looted ? LootedAlpha : 1f) : Mathf.Lerp(1f, 0.3f, fall);
            v.ClickArea.raycastTarget = alive || (corpse && !looted && fall >= 1f);
            v.Kick = Vector2.MoveTowards(v.Kick, Vector2.zero, KickRecoverPerSecond * Time.deltaTime);
            // 중독은 판정(쏘는 순간)이 아니라 포자가 닿은 뒤부터 보인다(여러 마리가 연달아 쏴도 첫 포자가 닿으면 바로).
            if (!u.Combatant.IsPoisoned) v.PoisonShown = false;
            else if (v.PendingHits == 0) v.PoisonShown = true;
            bool poisoned = alive && u.Combatant.IsPoisoned && v.PoisonShown;
            UpdateBars(v, alive, poisoned);
            UpdatePoison(v, poisoned);
            bool stunned = alive && u.IsStunned;
            UpdateStun(v, stunned, poisoned);

            var leader = _session.Leader;
            bool targeted = alive && leader != null && leader.IsAlive && leader.Target == u;
            v.TargetRing.enabled = targeted || (_picking && alive && IsPickable(u)) || (_focusPicking && alive && !u.IsPlayerSide);
            v.TargetRing.color = targeted && !_picking ? new Color(1f, 0.35f, 0.3f, 0.9f) : new Color(PickingColor.r, PickingColor.g, PickingColor.b, 0.85f);

            // 바라보는 쪽(그림은 오른쪽을 본다). 죽으면 옆으로 눕힌다.
            v.Body.localScale = new Vector3(u.Facing, 1f, 1f);
            v.Body.localRotation = Quaternion.Euler(0f, 0f, animatedDeath ? 0f : 90f * u.Facing * fall);
            if (stunned) v.Body.localRotation = Quaternion.Euler(0f, 0f, StunSwayDegrees * Mathf.Sin(Time.time * 7f + v.HopPhase)); // 기절: 비틀비틀

            if (v.Animator != null)
            {
                if (alive && u.IsMoving) v.Animator.PlayWalk(); else v.Animator.PlayIdle();
                v.Body.anchoredPosition = v.Kick;
            }
            else if (v.Monster != null)
            {
                // 몬스터 그림: 움직이면 걷기, 멈추면 숨쉬기, 쓰러지면 쓰러짐 동작. 걷기 그림이 없는 궁수는 걸을 때 통통 튄다.
                if (alive) v.Monster.SetMoving(u.IsMoving); else v.Monster.SetDead();
                float hop = v.Monster.IsHopping ? Hop(v, alive) : 0f;
                v.Body.anchoredPosition = new Vector2(0f, hop) + v.Kick;
            }
            else
            {
                // 몬스터(도형)는 걸을 때 통통 튄다
                v.Body.anchoredPosition = new Vector2(0f, Hop(v, alive)) + v.Kick;
            }
        }

        private static float Hop(UnitView v, bool alive) =>
            alive && v.Unit.IsMoving ? Mathf.Abs(Mathf.Sin(Time.time * 10f + v.HopPhase)) * 8f : 0f;

        // 막대 색: 우리 UI(숯빛·청동·호박색)에 맞춘 차분한 톤
        private static readonly Color HpHigh = new Color(0.45f, 0.72f, 0.3f);    // 이끼 초록
        private static readonly Color HpMid = new Color(0.88f, 0.66f, 0.26f);    // 호박색
        private static readonly Color HpLow = new Color(0.8f, 0.3f, 0.24f);      // 적갈색
        private static readonly Color EnemyHp = new Color(0.72f, 0.2f, 0.18f);   // 진홍
        private static readonly Color HpBack = new Color(0.16f, 0.07f, 0.06f);   // 비어 있는 체력(어두운 적갈)
        private static readonly Color HpTrailColor = new Color(0.94f, 0.75f, 0.45f, 0.85f); // 깎인 자국(밝은 호박)
        private static readonly Color ReadyBack = new Color(0.1f, 0.08f, 0.07f);
        private static readonly Color ReadyCharging = UITheme.BronzeLight;     // 공격 대기 차오르는 중
        private static readonly Color ReadyFull = new Color(1f, 0.8f, 0.45f);  // 다 참(곧 공격)
        private const float BarPlateHeight = 26f; // 테두리 4 + 공격 대기 4 + 구분선 1 + 체력 13 + 테두리 4
        private const float ReadyBarHeight = 4f;
        // 보스 큰 막대(화면 위쪽 가운데, 제목·라운드 줄 아래): 같은 판을 크게
        private const float BossBarWidth = 900f;
        private const float BossBarPlateHeight = 38f; // 테두리 4 + 공격 대기 6 + 구분선 1 + 체력 23 + 테두리 4
        private const float BossReadyBarHeight = 6f;
        private const float BossHudY = -96f, BossHudHeight = 74f; // 위 끝 기준(제목 -24, 라운드 줄 -62~-90 아래)
        private const float BossHudSlide = 30f;       // 나타날 때 위에서 이만큼 내려온다
        private const float TrailHoldSeconds = 0.4f;
        private const float TrailDrainPerSecond = 0.8f;

        /// <summary>체력 막대: 길이 = 비율, 아군은 초록 → 노랑 → 빨강, 적은 빨강. 깎인 부분은 밝은 막대로 잠깐 남았다 줄고,
        /// 30% 아래면 깜빡여 눈에 띄게 한다.</summary>
        private void UpdateBars(UnitView v, bool alive, bool poisoned)
        {
            if (v.BossHud != null)
            {
                // 보스 큰 막대: 나타날 땐 위에서 내려오며 또렷해지고, 쓰러지면 빈 막대로 천천히 흐려진다
                v.BossHud.alpha = Mathf.MoveTowards(v.BossHud.alpha, alive ? 1f : 0f, Time.unscaledDeltaTime * (alive ? 3f : 0.8f));
                ((RectTransform)v.BossHud.transform).anchoredPosition =
                    new Vector2(0f, BossHudY + BossHudSlide * (1f - (alive ? v.BossHud.alpha : 1f)));
                if (!alive) { SetLength(v.HpFill, 0f); if (v.HpText != null) v.HpText.text = $"0/{v.Unit.Combatant.Stats.MaxHealth}"; }
            }
            v.Bars.SetActive(alive || v.BossHud != null);
            if (!alive) return;

            var c = v.Unit.Combatant;
            if (v.PendingHits == 0 || v.ShownHealth < 0) v.ShownHealth = c.CurrentHealth;
            int health = Math.Max(v.ShownHealth, c.CurrentHealth); // 기다리는 공격이 있으면 닿기 전 체력
            float ratio = Mathf.Clamp01((float)health / Math.Max(1, c.Stats.MaxHealth));
            SetLength(v.HpFill, ratio);

            if (ratio >= v.TrailRatio) { v.TrailRatio = ratio; v.TrailHold = 0f; } // 회복은 바로
            else if (v.TrailHold < TrailHoldSeconds) v.TrailHold += Time.deltaTime;
            else v.TrailRatio = Mathf.Max(ratio, v.TrailRatio - TrailDrainPerSecond * Time.deltaTime);
            SetLength(v.HpTrail, v.TrailRatio);
            if (v.TrailRatio <= ratio) v.TrailHold = 0f;

            Color color = !v.Unit.IsPlayerSide ? EnemyHp
                : ratio > 0.5f ? Color.Lerp(HpMid, HpHigh, (ratio - 0.5f) * 2f)
                : Color.Lerp(HpLow, HpMid, Mathf.Clamp01((ratio - 0.15f) / 0.35f));
            // 중독: 형광 연두 ↔ 짙은 독초록으로 맥박(평소 이끼 초록 막대와 구별되게)
            if (poisoned) color = Color.Lerp(PoisonBarDark, PoisonBarColor, (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f);
            if (ratio < 0.3f) color = Color.Lerp(color, Color.white, (Mathf.Sin(Time.unscaledTime * 10f) + 1f) * 0.2f);
            v.HpFillImage.color = color;

            if (v.HpText != null) v.HpText.text = $"{health}/{c.Stats.MaxHealth}";
            float ready = v.Unit.AttackReadyRatio;
            SetLength(v.ReadyFill, ready);
            v.ReadyFillImage.color = ready >= 0.999f ? ReadyFull : Color.Lerp(UITheme.BronzeDark, ReadyCharging, ready);
        }

        private static void SetLength(RectTransform fill, float ratio)
        {
            var max = fill.anchorMax;
            max.x = Mathf.Clamp01(ratio);
            fill.anchorMax = max;
        }

        private bool IsPickable(FieldUnit u)
        {
            var skill = _session.Leader?.Skill;
            if (skill == null) return false;
            return u.IsPlayerSide == (skill.TargetType == SkillTargetType.Ally);
        }

        /// <summary>아래(화면 앞)에 선 유닛이 위에 그려지게 매 프레임 순서를 맞춘다.</summary>
        private void SortByDepth()
        {
            _drawOrder.Sort((a, b) => b.Unit.Position.Y.CompareTo(a.Unit.Position.Y));
            for (int i = 0; i < _drawOrder.Count; i++)
                _drawOrder[i].Root.SetSiblingIndex(i);
        }

        private void ApplyEventsVisual(List<BattleEvent> events)
        {
            foreach (var evt in events)
            {
                AppendLog(FormatEvent(evt));
                if (evt.IsPoison) // 독 한 번: 공격 동작·투사체 없이 맞은 쪽에 초록 숫자와 독 거품만
                {
                    var poisoned = _session.UnitOf(evt.Target);
                    if (poisoned != null && _views.TryGetValue(poisoned, out var poisonedView) && poisonedView.Root != null)
                    {
                        Vector2 feet = poisonedView.Root.anchoredPosition;
                        StartCoroutine(BattleFx.DamageNumber(_fxLayer, feet + new Vector2(UnityEngine.Random.Range(-12f, 12f), 130f),
                            $"-{evt.Damage}", PoisonColor, false));
                        StartCoroutine(BattleFx.PoisonPuff(_fxLayer, feet + new Vector2(0f, 70f)));
                    }
                    continue;
                }
                var attacker = _session.UnitOf(evt.Attacker);
                var target = _session.UnitOf(evt.Target);
                if (attacker == null || target == null) continue;
                var attackerView = _views[attacker];
                var targetView = _views[target];

                bool heal = evt.Damage < 0;
                // 거목 엔트 뿌리 분출 피해: 내려찍는 동작·가시·흔들림은 예고 연출(SlamTelegraph)이 분출 순간에 이미 했다 → 피격만 바로
                bool slam = !heal && IsSlam(attackerView);
                if (!heal && !slam)
                {
                    attackerView.Animator?.PlaySlash();
                    if (attackerView.Monster != null) attackerView.Monster.PlayAttack(); // 고블린 검 공격·궁수 사격
                    // 검격 소리: 검을 휘두르는 용병·검 고블린·망치 든 고블린 목수·방패로 치는 방패병(궁수는 활이라 뺀다)
                    if (attackerView.Animator != null || (attackerView.Monster != null
                        && (attackerView.Monster.Kind == MonsterKind.Sword || attackerView.Monster.Kind == MonsterKind.Carpenter
                            || attackerView.Monster.Kind == MonsterKind.Shield)))
                        GameAudio.PlaySfx(SwordSound);
                }

                string label = heal ? $"+{-evt.Damage}" : evt.Evaded ? "회피" : evt.Blocked ? $"막기 -{evt.Damage}" : $"-{evt.Damage}";
                Color color = heal ? new Color(0.45f, 0.9f, 0.5f) : evt.Evaded ? Color.white : evt.Blocked ? BlockColor : new Color(1f, 0.45f, 0.4f);

                bool hit = !heal && !evt.Evaded && evt.Damage > 0;
                // 판정은 이미 났다. 보이는 피격(숫자·번쩍임·체력 막대·쓰러짐)은 칼이 내려치는 프레임이나 투사체가 닿을 때로 미룬다.
                // 원거리는 같은 순간(궁수는 활을 놓는 프레임)에 투사체를 쏜다.
                float delay = heal || slam ? 0f : HitDelay(attackerView);
                if (evt.Blocked && targetView.Monster != null) StartCoroutine(GuardAfter(targetView.Monster, delay)); // 방패병: 맞는 순간 방패를 든다
                if (!attacker.IsMelee && attacker != target && !heal)
                {
                    targetView.PendingHits++;
                    StartCoroutine(Projectile(attackerView, targetView, label, color, hit, evt.Damage, delay));
                }
                else if (delay > 0f)
                {
                    targetView.PendingHits++;
                    StartCoroutine(DelayedHit(delay, attackerView, targetView, label, color, hit, evt.Damage));
                }
                else
                    PlayHit(attackerView, targetView, label, color, hit, evt.Damage);
            }
        }

        /// <summary>공격 동작 시작부터 맞는(검이 닿는·화살을 놓는) 프레임까지(전투 시간). 고블린은 공격 시트, 용병은 검격 클립 기준.</summary>
        private static float HitDelay(UnitView attacker)
        {
            if (attacker.Monster != null) return attacker.Monster.AttackHitDelay;
            if (attacker.Animator != null) return attacker.Animator.SlashHitDelay;
            return 0f;
        }

        /// <summary>이번 피해가 거목 엔트의 뿌리 분출인지(분출까지 엔트는 다른 공격을 하지 않아 LastWasSlam이 그대로다).</summary>
        private static bool IsSlam(UnitView attacker) =>
            attacker.Monster != null && attacker.Monster.HasSlam
            && attacker.Unit.Combatant.Passives.OfType<RootSlamPassive>().Any(p => p.LastWasSlam);

        /// <summary>
        /// 뿌리 내려찍기 예고: 범위마다 땅 위에 붉은 원(안쪽이 예고 시간 동안 차오르고 테두리가 점점 빠르게 깜빡임).
        /// 엔트는 땅을 치는 장이 분출 순간에 맞게 내려찍기를 시작하고, 분출하면 범위마다 가시가 솟고 판이 흔들린다.
        /// 엔트가 쓰러지거나 라운드가 바뀌어 취소되면 원만 흐려진다.
        /// </summary>
        private IEnumerator SlamTelegraph(RootSlamCast cast)
        {
            _views.TryGetValue(cast.Caster, out var casterView);
            var zones = cast.Zones.Select(z => BattleFx.CreateDangerZone(_groundLayer, new Vector2(z.X, z.Y), FieldBattleSession.SlamZoneRadius)).ToList();
            bool swung = false;
            while (cast.State == RootSlamCast.Phase.Pending && !_session.IsOver)
            {
                foreach (var zone in zones) zone.SetProgress(cast.Progress);
                if (!swung && casterView?.Monster != null && cast.Remaining <= casterView.Monster.SlamHitDelay)
                {
                    casterView.Monster.PlayAttack(slam: true); // 땅을 치는 장 = 분출
                    swung = true;
                }
                yield return null;
            }
            if (cast.State == RootSlamCast.Phase.Erupted)
            {
                foreach (var z in cast.Zones) StartCoroutine(BattleFx.RootSpikes(_fxLayer, new Vector2(z.X, z.Y)));
                if (_shakeTargets != null) StartCoroutine(BattleFx.Shake(_shakeTargets, ShakeStrength, ShakeSeconds));
                foreach (var zone in zones) zone.Flash(); // 하얗게 번쩍이고 사라진다
            }
            else
                foreach (var zone in zones) zone.FadeOut();
        }

        private IEnumerator ShakeAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_shakeTargets != null) yield return BattleFx.Shake(_shakeTargets, ShakeStrength, ShakeSeconds);
        }

        /// <summary>
        /// 기절 포효 예고: 엔트 둘레에 큰 붉은 원(판정 반지름 그대로)이 차오르고, 입을 크게 벌리는 장이 터지는 순간에 맞게 포효한다.
        /// 터지면 충격파 고리·큰 흔들림과 로그, 원 안 용병 머리 위엔 기절 별(UpdateUnitView). 취소되면 원만 흐려진다.
        /// </summary>
        private IEnumerator RoarTelegraph(RoarCast cast)
        {
            _views.TryGetValue(cast.Caster, out var casterView);
            var center = new Vector2(cast.Center.X, cast.Center.Y);
            var zone = BattleFx.CreateDangerZone(_groundLayer, center, cast.Radius, cast.Facing); // 용병 쪽 반원
            AppendLog($"<color=#ff8a6a>{Josa.IGa(cast.Caster.Combatant.Name)} 포효하려 한다! 붉은 범위 밖으로(엔트 뒤는 안전)!</color>");
            // 둘째 포효 소리(BossRoarAudio.Stun)와 싱크: 기절이 터지는 순간 = 소리가 크게 터지는 순간 = 입을 가장 크게 벌린 장.
            // 소리는 그보다 Onset만큼 먼저, 동작은 RoarPeakDelay만큼 먼저 시작하고, 큰 소리가 이어지는 동안 입 벌린 장을 유지한다.
            float hold = BossRoarAudio.Stun.LoudLength * TimeScale;
            float soundLead = BossRoarAudio.Stun.Onset * TimeScale;
            bool roared = false, voiced = false;
            while (cast.State == RoarCast.Phase.Pending && !_session.IsOver)
            {
                zone.SetProgress(cast.Progress);
                if (!voiced && cast.Remaining <= soundLead)
                {
                    GameAudio.PlayClip(BossRoarAudio.StunClip);
                    voiced = true;
                }
                if (!roared && casterView?.Monster != null && cast.Remaining <= casterView.Monster.RoarPeakDelay)
                {
                    casterView.Monster.PlayRoar(hold); // 입을 크게 벌리는 장 = 기절
                    roared = true;
                }
                yield return null;
            }
            if (cast.State == RoarCast.Phase.Erupted)
            {
                zone.Flash();
                StartCoroutine(BattleFx.Shockwave(_fxLayer, center, cast.Radius, new Color(1f, 0.55f, 0.35f)));
                if (_shakeTargets != null) StartCoroutine(BattleFx.Shake(_shakeTargets, ShakeStrength * 2.4f, hold + 0.3f, rumble: true)); // 큰 소리 동안 우르릉
                AppendLog(cast.StunnedCount > 0
                    ? $"<color=#ffd36b>{cast.Caster.Combatant.Name}의 포효! 용병 {cast.StunnedCount}명 기절</color>"
                    : $"<color=#ffd36b>{cast.Caster.Combatant.Name}의 포효 — 모두 피했다!</color>");
            }
            else zone.FadeOut();
        }

        private IEnumerator DelayedHit(float delay, UnitView attacker, UnitView target, string label, Color color, bool hit, int damage)
        {
            yield return new WaitForSeconds(delay); // 전투 배속·일시정지(timeScale)를 따른다
            LandHit(attacker, target, label, color, hit, damage);
        }

        /// <summary>방패병이 막은 공격이 닿는 순간 막기 동작(보이는 연출만).</summary>
        private IEnumerator GuardAfter(BattleMonsterSprite monster, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (monster != null) monster.PlayGuard();
        }

        /// <summary>기다리던 공격이 닿았다: 보이는 체력을 그만큼 깎고 피격 연출.</summary>
        private void LandHit(UnitView attacker, UnitView target, string label, Color color, bool hit, int damage)
        {
            target.PendingHits = Math.Max(0, target.PendingHits - 1);
            if (hit && target.Unit.Combatant.IsPoisoned) target.PoisonShown = true; // 독을 건 포자가 닿았다
            if (hit) target.ShownHealth = Math.Max(target.Unit.Combatant.CurrentHealth, target.ShownHealth - damage);
            if (target.Root == null) return;
            PlayHit(attacker, target, label, color, hit, damage);
        }

        /// <summary>맞은 쪽 연출: 숫자(큰 피해는 크게 "!")와, 실제로 맞았으면 붉게 번쩍임·스파크·넉백, 큰 피해면 화면 흔들림.</summary>
        private void PlayHit(UnitView attacker, UnitView target, string label, Color color, bool hit, int damage)
        {
            int maxHealth = Math.Max(1, target.Unit.Combatant.Stats.MaxHealth);
            bool big = hit && damage >= maxHealth * BigHitRatio;
            Vector2 feet = target.Root.anchoredPosition;
            StartCoroutine(BattleFx.DamageNumber(_fxLayer, feet + new Vector2(UnityEngine.Random.Range(-12f, 12f), 150f),
                big ? label + "!" : label, big ? BigHitColor : color, big));
            if (!hit) return;

            if (!target.Unit.IsPlayerSide) GameAudio.PlaySfx(MonsterHitSound); // 몬스터가 맞는 소리(칼이 닿는·화살이 꽂히는 순간)
            StartCoroutine(HitFlash(target));
            StartCoroutine(BattleFx.HitSpark(_fxLayer, feet + new Vector2(0f, 60f), big ? BigHitColor : Color.white));
            float away = Mathf.Sign(feet.x - attacker.Root.anchoredPosition.x);
            if (away == 0f) away = -target.Unit.Facing;
            target.Kick = new Vector2(away * (big ? BigKickDistance : KickDistance), 0f);
            if (big && _shakeTargets != null) StartCoroutine(BattleFx.Shake(_shakeTargets, ShakeStrength, ShakeSeconds));
        }

        // ---------- 시체 뒤지기 ----------

        private UnitView CorpseView(FieldUnit unit) =>
            _views.TryGetValue(unit, out var v) ? v : _corpses.TryGetValue(unit, out var c) ? c : null;

        /// <summary>쓰러진 몬스터를 뒤지기 시작한다: 머리 위에 황금 진행률 바(여러 시체를 같이 뒤질 수 있다).</summary>
        private void BeginLoot(FieldUnit unit)
        {
            if (unit.IsPlayerSide || unit.IsAlive || _resultShown || _looted.Contains(unit) || _looting.ContainsKey(unit)) return;
            var view = CorpseView(unit);
            if (view == null) return;
            if (BagFree <= 0) // 가방이 꽉 찼으면 뒤지지 않는다(시체는 털리지 않은 채 남는다)
            {
                StartCoroutine(BattleFx.DamageNumber(_fxLayer, view.Root.anchoredPosition + new Vector2(0f, 60f), "가방이 가득 찼다", new Color(0.75f, 0.7f, 0.62f), false));
                if (_bagWindow == null) ToggleBagWindow(); // 버릴 것을 고를 수 있게 가방 창을 연다
                return;
            }
            _looting[unit] = new LootBar { View = view, Root = CreateLootBar(view, out var fill), Fill = fill };
        }

        private RectTransform CreateLootBar(UnitView view, out Image fill)
        {
            if (_lootFrameSprite == null) _lootFrameSprite = Resources.Load<Sprite>("Battle/UI/loot_bar_frame");
            if (_lootFillSprite == null) _lootFillSprite = Resources.Load<Sprite>("Battle/UI/loot_bar_fill");
            var go = new GameObject("LootBar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_fxLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(LootBarWidth, LootBarHeight * 2.2f);
            rect.anchoredPosition = view.Root.anchoredPosition + new Vector2(0f, LootBarLift);
            var frame = go.GetComponent<Image>();
            frame.sprite = _lootFrameSprite;
            frame.color = _lootFrameSprite != null ? Color.white : new Color(0.25f, 0.15f, 0.08f);
            frame.raycastTarget = false;

            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(go.transform, false);
            var fillRect = (RectTransform)fillGO.transform;
            fillRect.anchorMin = new Vector2(LootBarInsetLeft, LootBarInsetBottom);
            fillRect.anchorMax = new Vector2(1f - LootBarInsetRight, 1f - LootBarInsetTop);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill = fillGO.GetComponent<Image>();
            fill.sprite = _lootFillSprite;
            fill.color = _lootFillSprite != null ? Color.white : new Color(1f, 0.65f, 0.15f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            return rect;
        }

        /// <summary>뒤지는 중인 시체마다 바를 채우고(실제 시간, 일시정지 중엔 멈춤), 다 차면 전리품을 꺼낸다.</summary>
        private void UpdateLooting()
        {
            if (_looting.Count == 0) return;
            float dt = _paused ? 0f : Time.unscaledDeltaTime;
            foreach (var pair in _looting.ToList())
            {
                var bar = pair.Value;
                if (bar.Root == null) { _looting.Remove(pair.Key); continue; }
                bar.Progress += dt / LootSeconds;
                bar.Fill.fillAmount = Mathf.Clamp01(bar.Progress);
                bar.Root.anchoredPosition = bar.View.Root.anchoredPosition + new Vector2(0f, LootBarLift);
                if (bar.Progress < 1f) continue;
                _looting.Remove(pair.Key);
                Destroy(bar.Root.gameObject);
                FinishLoot(pair.Key, bar.View);
            }
        }

        /// <summary>그 몬스터 한 마리 몫을 굴려 뒤진 시체 창 대기열에 넣는다(가져갈 것은 창에서 고른다). 털린 시체는 흐려지고 더 누를 수 없다.</summary>
        private void FinishLoot(FieldUnit unit, UnitView view)
        {
            _looted.Add(unit);
            view.Group.alpha = LootedAlpha;
            view.ClickArea.raycastTarget = false;
            var loot = LootCatalog.Roll(new[] { unit.Combatant.Name }, _lootRng, archerSplit: false);
            if (loot.Count == 0)
            {
                Vector2 origin = view.Root.anchoredPosition + new Vector2(0f, 30f);
                StartCoroutine(BattleFx.DamageNumber(_fxLayer, origin, "빈손", new Color(0.75f, 0.7f, 0.62f), false));
                return;
            }
            _lootChoices.Enqueue(new LootChoice { Unit = unit, View = view, Loot = loot, Picked = loot.Select(_ => true).ToArray() });
            if (_lootChoiceShown == null) ShowNextLootChoice();
            else RefreshLootChoiceTitle();
        }

        /// <summary>고른 것만 챙긴다(시체에서 튀어나오는 연출·로그). 하나도 안 골랐으면 두고 간다.</summary>
        private void TakeLoot(LootChoice choice, bool take)
        {
            var taken = take ? choice.Loot.Where((_, i) => choice.Picked[i]).ToList() : new List<(LootItem item, int count)>();
            string name = choice.Unit.Combatant.Name;
            if (taken.Count == 0)
            {
                AppendLog($"<color=#a3937c>{Josa.EulReul(name)} 뒤졌지만 두고 갔다</color>");
                return;
            }
            _lootedItems.AddRange(taken);
            if (choice.View != null && choice.View.Root != null)
            {
                Vector2 origin = choice.View.Root.anchoredPosition + new Vector2(0f, 30f);
                for (int i = 0; i < taken.Count; i++)
                    StartCoroutine(PopLoot(taken[i].item, taken[i].count, origin, i, taken.Count));
            }
            AppendLog($"<color=#ffd36b>{Josa.EulReul(name)} 뒤졌다 — {LootCatalog.Describe(taken)}</color>");
            RefreshBag();
        }

        // ---------- 가방 창 ----------

        private void UpdateBagButton()
        {
            if (_bagButtonText == null) return;
            _bagButtonText.text = $"가방 {BagUsed}/{_bagCapacity} (B)";
            _bagButton.GetComponent<Image>().color = BagFree <= 0 && _bagCapacity > 0 ? SpeedFastColor : Color.white; // 꽉 차면 주황빛
        }

        /// <summary>가방이 바뀌면: 버튼 글자 · 열린 가방 창 · 시체 창의 칸 수를 다시 쓴다.</summary>
        private void RefreshBag()
        {
            UpdateBagButton();
            if (_bagWindow != null) BuildBagWindow();
            UpdateLootChoiceState();
        }

        private void ToggleBagWindow()
        {
            if (_resultShown) return;
            if (_bagWindow != null) CloseBagWindow();
            else
            {
                _bagSelected = -1;
                BuildBagWindow();
            }
        }

        private void CloseBagWindow()
        {
            if (_bagWindow != null) Destroy(_bagWindow);
            _bagWindow = null;
            _bagSelected = -1;
        }

        /// <summary>
        /// 가방 창(화면 위 가운데 — 오른쪽 시체 창과 겹치지 않게): 제목 "가방 n/m" → 대원마다 큰 칸 가방 카드(챙긴 것을 대원 순서대로 채움) →
        /// 가방 밖(재료·동전 주머니) → [버리기] [닫기]. 칸을 누르면 고르고, [버리기]로 하나 버린다. 전투는 멈추지 않는다.
        /// </summary>
        private void BuildBagWindow()
        {
            if (_bagWindow != null) Destroy(_bagWindow);
            var panel = new GameObject("BagWindow", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_canvas.transform, false);
            _bagWindow = panel;
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, BagWindowTop);
            rect.sizeDelta = new Vector2(BagWindowWidth, 0f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.11f, 0.94f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 12);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateText(panel.transform, $"<b>가방</b>  <size=15><color=#ffd36b>{BagUsed}/{_bagCapacity}칸</color></size>", 18, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;
            title.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

            var units = BagSlotsUI.Units(_lootedItems);
            if (_bagSelected >= units.Count) _bagSelected = -1;
            var shares = BagSlotsUI.Split(units, _fighters); // 앞 대원 가방 빈 칸부터 차례로 채운다
            int start = 0;
            for (int m = 0; m < _fighters.Count; m++)
            {
                // 지난 원정부터 가방에 남아 있던 것이 앞 칸, 이번에 챙긴 몫이 그 뒤(남아 있던 것은 고르거나 버릴 수 없다)
                int offset = start, kept = _fighters[m].BagContents.Count, count = shares[m].Count;
                var items = _fighters[m].BagContents.Concat(shares[m]).ToList();
                int selected = _bagSelected >= offset && _bagSelected < offset + count ? _bagSelected - offset + kept : -1;
                BagSlotsUI.CreateMercBagCard(panel.transform, _fighters[m], items, BagSlotsUI.LargeSlot, BagSlotsUI.LargePerLine, BagSlotsUI.LargeGap,
                    i => { if (i >= kept) SelectBagSlot(offset + i - kept); }, selected, i => { if (i >= kept) DropBagItem(offset + i - kept); });
                start += count;
            }
            BagSlotsUI.CreateOutsideRow(panel.transform, _lootedItems, 28f);

            string selectedName = _bagSelected >= 0 ? units[_bagSelected].Name : null;
            var note = CreateText(panel.transform, selectedName != null ? $"고른 칸: {selectedName}" : units.Count > 0 ? "칸을 눌러 골라 [버리기] · 우클릭하면 바로 버린다" : "아직 챙긴 것이 없다",
                13, TextAnchor.MiddleLeft, UITheme.MutedText);
            note.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            buttons.GetComponent<LayoutElement>().minHeight = 38f;
            var h = buttons.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var drop = ShopListUI.CreateButton(buttons.transform, "버리기", DropSelectedBagItem, 100f);
            drop.interactable = _bagSelected >= 0;
            ShopListUI.CreateButton(buttons.transform, "닫기 (B)", CloseBagWindow, 100f);
            UIThemeApplier.ApplyNow(buttons.transform);
            panel.transform.SetAsLastSibling();
        }

        private void SelectBagSlot(int index)
        {
            _bagSelected = _bagSelected == index ? -1 : index;
            BuildBagWindow();
        }

        /// <summary>고른 칸의 아이템 하나를 버린다(시체는 이미 털려 되돌아가지 않는다). 칸이 비면 다시 시체를 뒤질 수 있다.</summary>
        private void DropSelectedBagItem() => DropBagItem(_bagSelected);

        /// <summary>index번째 칸(BagSlotsUI.Units 순서)의 아이템 하나를 버린다(우클릭·[버리기]).</summary>
        private void DropBagItem(int index)
        {
            var units = BagSlotsUI.Units(_lootedItems);
            if (index < 0 || index >= units.Count) return;
            RemoveOneLooted(units[index]);
            _bagSelected = -1;
            RefreshBag();
            UpdateLootChoiceState(); // 칸이 비면 뜬 시체 창의 [가져가기]가 다시 열린다
        }

        /// <summary>챙긴 것에서 그 아이템 하나를 빼고 로그를 남긴다.</summary>
        private void RemoveOneLooted(LootItem item)
        {
            int at = _lootedItems.FindIndex(l => l.item == item && l.count > 0);
            if (at < 0) return;
            var (_, count) = _lootedItems[at];
            if (count > 1) _lootedItems[at] = (item, count - 1);
            else _lootedItems.RemoveAt(at);
            AppendLog($"<color=#a3937c>{Josa.EulReul(item.Name)} 버렸다</color>");
        }

        /// <summary>고른 것 중 남은 가방 칸에 드는 만큼만(값 높은 것부터) 가져간다.</summary>
        private void TakeFitting(LootChoice choice)
        {
            var picked = choice.Loot.Where((_, i) => choice.Picked[i]).ToList();
            var fit = BagCatalog.Fit(picked, BagFree, out _);
            choice.Loot = fit;
            choice.Picked = fit.Select(_ => true).ToArray();
            TakeLoot(choice, true);
        }

        /// <summary>처음 고름: 남은 가방 칸 안에서 값 높은 줄부터 켠다(넘치는 줄은 꺼 둔다). 칸을 안 쓰는 것(건설 재료·동전 주머니)은 늘 켠다.</summary>
        private void PickWithinBag(LootChoice choice)
        {
            int free = BagFree;
            var order = Enumerable.Range(0, choice.Loot.Count).OrderByDescending(i => choice.Loot[i].item.Price);
            foreach (int i in order)
            {
                var (item, count) = choice.Loot[i];
                bool slotless = !BagCatalog.UsesSlot(item);
                choice.Picked[i] = slotless || count <= free;
                if (choice.Picked[i] && !slotless) free -= count;
            }
        }

        /// <summary>고른 칸 수가 남은 가방 칸을 넘으면 [가져가기]를 막고 안내를 띄운다. 제목의 "가방 n/m"도 다시 쓴다.</summary>
        private void UpdateLootChoiceState()
        {
            if (_lootChoiceShown == null) return;
            int need = BagCatalog.SlotsUsed(_lootChoiceShown.Loot.Where((_, i) => _lootChoiceShown.Picked[i]));
            int over = need - BagFree;
            if (_lootTakeButton != null) _lootTakeButton.interactable = over <= 0;
            _lootSwapDrops = over > 0 ? SwapDrops(_lootChoiceShown, over) : new List<LootItem>();
            if (_lootSwapButton != null)
            {
                bool canSwap = _lootSwapDrops.Count > 0;
                _lootSwapButton.gameObject.SetActive(canSwap);
                if (canSwap)
                {
                    _lootSwapButton.GetComponentInChildren<Text>().text = $"싼 것 {_lootSwapDrops.Count}개 버리고 가져가기";
                    var tip = _lootSwapButton.GetComponent<TooltipTrigger>();
                    if (tip == null) tip = _lootSwapButton.gameObject.AddComponent<TooltipTrigger>();
                    tip.Text = "가방에서 버릴 것:\n" + string.Join("\n", _lootSwapDrops.Select(d => $"{d.Name} ({d.Price}G)"));
                }
            }
            if (_lootChoiceNote != null)
            {
                _lootChoiceNote.text = over > 0
                    ? $"가방이 {over}칸 모자랍니다 — 덜 고르거나" + (_lootSwapDrops.Count > 0 ? " 싼 것과 바꾸세요" : " 가방 창(B)에서 버리세요")
                    : "";
                _lootChoiceNote.gameObject.SetActive(over > 0);
            }
            RefreshLootChoiceTitle();
        }

        /// <summary>
        /// 모자란 over칸만큼 가방에서 버릴 것: 가방의 가장 싼 것 over개. 그 하나하나가 이번에 고른 칸 쓰는 것 중 가장 싼 것보다 싸야 한다
        /// (더 비싼 걸 버리고 싼 걸 줍는 일은 없게). 안 되면 빈 목록.
        /// </summary>
        private List<LootItem> SwapDrops(LootChoice choice, int over)
        {
            var picked = choice.Loot.Where((l, i) => choice.Picked[i] && BagCatalog.UsesSlot(l.item)).Select(l => l.item).ToList();
            if (picked.Count == 0) return new List<LootItem>();
            int cheapestPicked = picked.Min(p => p.Price);
            var drops = BagSlotsUI.Units(_lootedItems).OrderBy(u => u.Price).Take(over).ToList();
            return drops.Count == over && drops.All(d => d.Price < cheapestPicked) ? drops : new List<LootItem>();
        }

        /// <summary>[싼 것 n개 버리고 가져가기]: 가방의 싼 것을 버리고 고른 것을 모두 챙긴다.</summary>
        private void SwapAndTake()
        {
            if (_lootChoiceShown == null || _lootSwapDrops.Count == 0) return;
            foreach (var item in _lootSwapDrops) RemoveOneLooted(item);
            _lootSwapDrops = new List<LootItem>();
            ResolveLootChoice(true);
        }

        /// <summary>떠 있는 창을 치우고 대기열의 다음 시체 창을 띄운다(없으면 닫힌 채로).</summary>
        private void ShowNextLootChoice()
        {
            if (_lootChoicePanel != null) Destroy(_lootChoicePanel);
            _lootChoicePanel = null;
            _lootChoiceShown = _lootChoices.Count > 0 ? _lootChoices.Dequeue() : null;
            if (_lootChoiceShown != null && !_resultShown) BuildLootChoicePanel(_lootChoiceShown);
        }

        /// <summary>창 버튼: 고른 것 가져가기 / 두고 가기 → 다음 시체 창.</summary>
        private void ResolveLootChoice(bool take)
        {
            if (_lootChoiceShown == null) return;
            TakeLoot(_lootChoiceShown, take);
            ShowNextLootChoice();
        }

        /// <summary>결과로 넘어갈 때: 아직 고르지 않은 창·대기열은 고른 것 중 값 높은 것부터 남은 가방 칸만큼 가져간다 — 뒤진 것을 되도록 잃지 않게.</summary>
        private void FlushLootChoices()
        {
            if (_lootChoiceShown != null) TakeFitting(_lootChoiceShown);
            while (_lootChoices.Count > 0) TakeFitting(_lootChoices.Dequeue());
            _lootChoiceShown = null;
            if (_lootChoicePanel != null) Destroy(_lootChoicePanel);
            _lootChoicePanel = null;
        }

        /// <summary>
        /// 뒤진 시체 창(화면 오른쪽 아래, 아래 패널 위): 제목 · 아이템 줄(고름 칸 · 등급 색 테두리 아이콘 · 이름 ×n · 등급 · 값) · [가져가기] [두고 가기].
        /// 줄을 누르면 고름/안 고름. 판만 클릭을 받고 필드는 가리지 않는다. 전투는 멈추지 않는다.
        /// </summary>
        private void BuildLootChoicePanel(LootChoice choice)
        {
            var panel = new GameObject("LootChoicePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_canvas.transform, false);
            _lootChoicePanel = panel;
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-LootChoiceMargin, FieldBottom + 10f);
            rect.sizeDelta = new Vector2(LootChoiceWidth, 0f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.11f, 0.94f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 12);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateText(panel.transform, "", 18, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.name = "Title";
            title.supportRichText = true;
            title.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

            PickWithinBag(choice); // 남은 가방 칸 안에서 값 높은 것부터 미리 고른다
            for (int i = 0; i < choice.Loot.Count; i++)
                CreateLootChoiceRow(panel.transform, choice, i);

            _lootChoiceNote = CreateText(panel.transform, "", 14, TextAnchor.MiddleLeft, new Color(0.95f, 0.45f, 0.4f));
            _lootChoiceNote.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

            // 가방이 모자랄 때만 보이는 교체 버튼(창 폭 전체, 버튼 줄 위)
            _lootSwapButton = ShopListUI.CreateButton(panel.transform, "싼 것 버리고 가져가기", SwapAndTake, LootChoiceWidth - 28f);
            UIThemeApplier.ApplyNow(_lootSwapButton.transform);

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            buttons.GetComponent<LayoutElement>().minHeight = 38f;
            var h = buttons.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            _lootTakeButton = ShopListUI.CreateButton(buttons.transform, "가져가기", () => ResolveLootChoice(true), 120f);
            ShopListUI.CreateButton(buttons.transform, "두고 가기", () => ResolveLootChoice(false), 110f);
            UIThemeApplier.ApplyNow(buttons.transform);
            panel.transform.SetAsLastSibling();
            UpdateLootChoiceState(); // 제목의 가방 칸 · [가져가기] 막힘
        }

        /// <summary>창 제목: "○○을(를) 뒤졌다" + 뒤에 기다리는 시체 수.</summary>
        private void RefreshLootChoiceTitle()
        {
            if (_lootChoicePanel == null || _lootChoiceShown == null) return;
            var title = _lootChoicePanel.transform.Find("Title")?.GetComponent<Text>();
            if (title == null) return;
            int need = BagCatalog.SlotsUsed(_lootChoiceShown.Loot.Where((_, i) => _lootChoiceShown.Picked[i]));
            title.text = $"<b>{Josa.EulReul(_lootChoiceShown.Unit.Combatant.Name)} 뒤졌다</b>"
                + $"  <size=14><color=#ffd36b>가방 {BagUsed + need}/{_bagCapacity}</color></size>"
                + (_lootChoices.Count > 0 ? $"  <size=14><color=#a3937c>다음 {_lootChoices.Count}</color></size>" : "");
        }

        /// <summary>아이템 한 줄(누르면 고름/안 고름): 고름 칸 · 등급 색 테두리 아이콘 · 이름 ×n · 등급 · 값.</summary>
        private void CreateLootChoiceRow(Transform parent, LootChoice choice, int index)
        {
            var (item, count) = choice.Loot[index];
            Color gradeColor = item.HasGrade ? LootGrades.ColorOf(item.Grade) : UITheme.BodyText;
            var row = new GameObject("LootRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = LootChoiceRowHeight;
            var rowImage = row.GetComponent<Image>();
            rowImage.color = new Color(1f, 1f, 1f, 0.05f);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 10, 4, 4);
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            // 고름 칸: 고르면 금빛으로 채워진 네모, 안 고르면 빈 네모
            var box = CreateImage(row.transform, "Pick", Color.white);
            var boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.minWidth = boxLayout.preferredWidth = 18f;
            boxLayout.minHeight = boxLayout.preferredHeight = 18f;
            var boxImage = box.GetComponent<Image>();
            boxImage.raycastTarget = false;

            var icon = CreateImage(row.transform, "Icon", Color.white);
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = item.Icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconLayout = icon.AddComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = LootChoiceIcon;
            iconLayout.minHeight = iconLayout.preferredHeight = LootChoiceIcon;
            if (item.HasGrade)
            {
                var outline = icon.AddComponent<Outline>();
                outline.effectColor = gradeColor;
                outline.effectDistance = new Vector2(LootGradeOutline, -LootGradeOutline);
            }

            string gradeText = item.HasGrade ? $"  <size=13><color={LootGrades.HexOf(item.Grade)}>{item.GradeTag}</color></size>" : "";
            var label = CreateText(row.transform, $"{item.Name}{(count > 1 ? $" ×{count}" : "")}{gradeText}", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            label.supportRichText = true;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var price = CreateText(row.transform, $"{item.Price * count}G", 15, TextAnchor.MiddleRight, UITheme.MutedText);
            price.raycastTarget = false;
            price.gameObject.AddComponent<LayoutElement>().minWidth = 52f;

            var group = row.GetComponent<CanvasGroup>();
            void Refresh()
            {
                bool picked = choice.Picked[index];
                boxImage.color = picked ? new Color(0.95f, 0.75f, 0.3f) : new Color(1f, 1f, 1f, 0.15f);
                group.alpha = picked ? 1f : LootChoiceUnpickedAlpha;
            }
            var button = row.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                choice.Picked[index] = !choice.Picked[index];
                Refresh();
                UpdateLootChoiceState();
            });
            row.AddComponent<TooltipTrigger>().Text = $"<b>{item.Name}</b>" + (item.HasGrade ? $" <color={LootGrades.HexOf(item.Grade)}>{item.GradeTag}</color>" : "")
                + $"\n팔면 개당 {item.Price}G · 눌러서 가져갈지 고른다";
            Refresh();
        }

        /// <summary>시체에서 아이템 아이콘이 위로 툭 튀었다가 조금 떨어지며 사라지고, 그 위에 "+이름" 글자가 뜬다.</summary>
        private IEnumerator PopLoot(LootItem item, int count, Vector2 origin, int index, int total)
        {
            yield return new WaitForSecondsRealtime(index * 0.12f);
            var go = new GameObject("LootPop", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(_fxLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(34f, 34f);
            var image = go.GetComponent<Image>();
            image.sprite = item.Icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (item.Icon == null) image.color = new Color(1f, 0.8f, 0.3f);
            var group = go.GetComponent<CanvasGroup>();
            float side = (index - (total - 1) * 0.5f) * 34f;
            StartCoroutine(BattleFx.DamageNumber(_fxLayer, origin + new Vector2(side, 46f), count > 1 ? $"+{item.Name} ×{count}" : $"+{item.Name}",
                new Color(1f, 0.85f, 0.4f), false));
            const float duration = 0.9f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (go == null) yield break;
                float k = t / duration;
                float arc = 70f * k - 80f * k * k; // 위로 튀었다 조금 떨어진다
                rect.anchoredPosition = origin + new Vector2(side * k, arc + 10f);
                rect.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.4f, 1.2f, k / 0.15f) : 1f);
                group.alpha = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        private IEnumerable<UnitView> AllCorpseViews() =>
            _views.Values.Where(v => !v.Unit.IsPlayerSide && !v.Unit.IsAlive).Concat(_corpses.Values);

        private int RemainingCorpses() => AllCorpseViews().Count(v => !_looted.Contains(v.Unit));

        /// <summary>이겼다: "승리!" 뒤 화면 아래 '전리품 수습' 막대(남은 시체 수 · [수습 끝내기]). 시체를 다 뒤지면 저절로 결과 창.</summary>
        private void BeginSalvage()
        {
            _salvaging = true;
            _picking = _focusPicking = false;
            SetPaused(false);
            _victoryBannerShown = true;
            StartCoroutine(BattleFx.ResultBanner(_canvas.transform, "승리!", VictoryTitleColor));

            _salvageBar = new GameObject("SalvageBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            _salvageBar.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)_salvageBar.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 40f);
            var image = _salvageBar.GetComponent<Image>();
            image.sprite = UITheme.Panel;
            image.type = Image.Type.Sliced;
            var layout = _salvageBar.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 12, 8, 8);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = _salvageBar.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _salvageText = ShopListUI.CreateText(_salvageBar.transform, "", 18, TextAnchor.MiddleLeft, UITheme.BodyText);
            _salvageText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _salvageText.supportRichText = true;
            ShopListUI.CreateButton(_salvageBar.transform, "수습 끝내기", ShowResult, 130f);
            UIThemeApplier.ApplyNow(_salvageBar.transform);
            _salvageBar.transform.SetAsLastSibling();
            UpdateSalvage();
        }

        private void UpdateSalvage()
        {
            foreach (var v in _views.Values) UpdateUnitView(v);
            SortByDepth();
            int remaining = RemainingCorpses();
            if (_salvageText != null)
                _salvageText.text = $"<b>전리품 수습</b>  시체를 눌러 뒤지세요 · 남은 시체 {remaining}" +
                                    $"  <color=#ffd36b>가방 {BagUsed}/{_bagCapacity}</color>";
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ShowResult();
                return;
            }
            // 다 뒤졌으면(뒤지는 중인 것도 없으면) 잠깐 뒤 결과 창
            bool canLootMore = remaining > 0 && BagFree > 0; // 가방이 꽉 차면 남은 시체는 못 뒤진다
            if (canLootMore || _looting.Count > 0 || _lootChoiceShown != null) { _salvageDoneAt = -1f; return; } // 고를 창이 남아 있으면 기다린다
            if (_salvageDoneAt < 0f) _salvageDoneAt = Time.unscaledTime;
            if (Time.unscaledTime - _salvageDoneAt >= 1f) ShowResult();
        }

        /// <summary>수습을 끝낸다: 뒤지는 중이던 바는 버리고(그 시체 몫은 없음) 막대를 치운다.</summary>
        private void EndSalvage()
        {
            foreach (var bar in _looting.Values)
                if (bar.Root != null) Destroy(bar.Root.gameObject);
            _looting.Clear();
            _salvaging = false;
            if (_salvageBar != null) Destroy(_salvageBar);
            _salvageBar = null;
        }

        // ---------- 결과 ----------

        private void ShowResult()
        {
            if (_resultShown) return;
            FlushLootChoices(); // 고르지 않은 시체 창은 고른 그대로 가져간다(PreLooted 전에)
            CloseBagWindow();
            if (_bagButton != null) _bagButton.gameObject.SetActive(false); // 결과 화면이 가방 칸을 보여 준다
            _resultShown = true;
            SetPaused(false);
            _picking = false;

            var result = new BattleResult
            {
                Outcome = _session.Outcome,
                Rounds = _session.ActionCount,
                Log = _session.Log,
                TeamA = _session.PlayerTeam,
                TeamB = _session.AllEnemies,
            };
            var battle = new ExpeditionBattle.Battle
            {
                Result = result, Fighters = _fighters, Combatants = _session.PlayerTeam,
                WavesCleared = _session.WavesCleared, WaveCount = _session.WaveCount,
                NamedArchers = true, // 궁수는 "고블린 궁수"로 이름이 붙어 있다
                Direct = true,       // 이기면 보상·명성·성장을 더 받는다
                PreLooted = new List<(LootItem, int)>(_lootedItems), // 시체를 뒤져 챙긴 것만(안 뒤진 시체 몫은 없다)
            };
            EndSalvage();
            var report = ExpeditionBattle.ConcludeReport(_expedition, battle, alertShown: true); // 이 씬의 결과 화면이 보여 준다

            _skillButton.interactable = false;
            StartCoroutine(ShowResultAfterBanner(report));
        }

        /// <summary>큰 "승리!"/"패배…" 배너를 잠깐 보여 준 뒤 결과창을 띄운다(결과 계산은 이미 끝난 상태).</summary>
        private IEnumerator ShowResultAfterBanner(ExpeditionBattle.Report report)
        {
            if (!_victoryBannerShown) // 수습 단계 앞에서 이미 "승리!"를 보였으면 다시 띄우지 않는다
                yield return BattleFx.ResultBanner(_canvas.transform, report.Victory ? "승리!" : "패배…",
                    report.Victory ? VictoryTitleColor : DefeatColor);
            BuildResultOverlay(report);
        }

        // ---------- 연출 ----------

        /// <summary>투사체: launchDelay(활을 놓는 프레임까지) 기다렸다가 쏘고, 닿으면 피격. 고블린 궁수는 날아가는 쪽을 보는 화살, 그 밖은 빛 덩어리.</summary>
        private IEnumerator Projectile(UnitView from, UnitView to, string label, Color color, bool hit, int damage, float launchDelay)
        {
            if (launchDelay > 0f) yield return new WaitForSeconds(launchDelay); // 전투 배속·일시정지를 따른다
            if (from.Root == null || to.Root == null) // 기다리는 사이 라운드가 바뀌어 그림이 치워졌다
            {
                LandHit(from, to, label, color, hit, damage);
                yield break;
            }

            bool arrow = from.Monster != null && from.Monster.Kind == MonsterKind.Archer;
            bool spore = from.Monster != null && from.Monster.Kind == MonsterKind.Mushroom; // 독버섯: 흔들리며 날아가는 연두 포자
            var go = new GameObject(arrow ? "Arrow" : spore ? "Spore" : "Projectile", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_fxLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            if (arrow)
            {
                var texture = BattleFx.Arrow;
                rect.sizeDelta = new Vector2(ArrowLength, ArrowLength * texture.height / texture.width); // 그림 비율 그대로
                rect.pivot = new Vector2(1f, 0.5f); // 화살촉이 위치점 — 촉이 대상에 닿을 때 맞는다
                img.texture = texture;
            }
            else if (spore)
            {
                rect.sizeDelta = new Vector2(30f, 30f);
                img.texture = BattleDecor.Glow;
                img.color = new Color(0.75f, 1f, 0.3f, 0.95f);
            }
            else
            {
                rect.sizeDelta = new Vector2(14f, 14f);
                img.texture = BattlePlaceholderSprites.Circle.texture;
                img.color = new Color(1f, 0.9f, 0.6f);
            }

            // 궁수는 활 높이·앞쪽에서 쏜다(그림이 바라보는 쪽).
            Vector2 offset = arrow ? new Vector2(from.Unit.Facing * 34f, 66f) : new Vector2(0f, 60f);
            Vector2 start = from.Root.anchoredPosition + offset;
            Vector2 end = to.Root.anchoredPosition + new Vector2(0f, 60f);
            float duration = Mathf.Max(0.08f, Vector2.Distance(start, end) / ProjectileSpeed);
            rect.anchoredPosition = start;
            if (arrow) AimArrow(rect, start, end);
            for (float t = 0f; t < duration && to.Root != null; t += Time.deltaTime)
            {
                end = to.Root.anchoredPosition + new Vector2(0f, 60f); // 움직이는 대상을 따라간다
                var pos = Vector2.Lerp(start, end, t / duration);
                if (arrow) AimArrow(rect, pos, end); // 지금 자리에서 맞을 캐릭터 몸 가운데를 향한다
                if (spore) // 포자는 위아래로 흔들리며 부풀었다 줄고, 지나간 자리에 작은 가루를 흘린다
                {
                    pos += new Vector2(0f, Mathf.Sin(t * 18f) * 8f);
                    rect.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(t * 25f));
                    if (UnityEngine.Random.value < 0.35f) StartCoroutine(BattleFx.SporeMote(_fxLayer, pos));
                }
                rect.anchoredPosition = pos;
                yield return null;
            }
            Destroy(go);
            LandHit(from, to, label, color, hit, damage);
        }

        /// <summary>화살 그림(오른쪽을 향함)을 from → to 방향으로 돌린다. 거의 닿아 방향이 흔들릴 만큼 가까우면 그대로 둔다.</summary>
        private static void AimArrow(RectTransform rect, Vector2 from, Vector2 to)
        {
            var dir = to - from;
            if (dir.sqrMagnitude < 4f) return;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        private IEnumerator MoveMarker(Vector2 point)
        {
            var go = new GameObject("MoveMarker", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_groundLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = point;
            var img = go.GetComponent<Image>();
            img.sprite = BattlePlaceholderSprites.Ring;
            img.raycastTarget = false;
            const float duration = 0.5f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                rect.sizeDelta = new Vector2(Mathf.Lerp(50f, 20f, k), Mathf.Lerp(24f, 10f, k));
                img.color = new Color(0.5f, 1f, 0.6f, 1f - k);
                yield return null;
            }
            Destroy(go);
        }

        private static readonly Color PoisonColor = new Color(0.56f, 0.86f, 0.35f);

        private static readonly Color PoisonBarColor = new Color(0.75f, 1f, 0.2f);
        private static readonly Color PoisonBarDark = new Color(0.2f, 0.45f, 0.12f);

        /// <summary>중독 표시: 걸리는 순간 "중독!" 알림과 거품, 걸려 있는 동안 머리 위 "독 n" 꼬리표·진한 초록 몸·피어오르는 거품
        /// (피격 번쩍임이 그 위에 덮이고, 끝나면 다시 초록). 풀리면 원래 색으로.</summary>
        private void UpdatePoison(UnitView v, bool poisoned)
        {
            if (v.PoisonTag != null)
            {
                v.PoisonTag.transform.parent.gameObject.SetActive(poisoned);
                if (poisoned) v.PoisonTag.text = $"독 {v.Unit.Combatant.PoisonTicks}";
            }
            if (!poisoned)
            {
                if (!v.PoisonTinted) return;
                v.PoisonTinted = false;
                for (int i = 0; i < v.Tintables.Length; i++)
                    if (v.Tintables[i] != null) v.Tintables[i].color = v.BaseColors[i];
                return;
            }

            var feet = v.Root.anchoredPosition;
            if (!v.PoisonTinted)
            {
                StartCoroutine(BattleFx.DamageNumber(_fxLayer, feet + new Vector2(0f, 175f), "중독!", PoisonColor, true));
                StartCoroutine(BattleFx.PoisonPuff(_fxLayer, feet + new Vector2(0f, 70f)));
                v.PoisonBubbleTimer = 0f;
            }
            v.PoisonTinted = true;
            v.PoisonBubbleTimer -= Time.deltaTime;
            if (v.PoisonBubbleTimer <= 0f)
            {
                v.PoisonBubbleTimer = 0.35f;
                StartCoroutine(BattleFx.PoisonBubble(_fxLayer, feet + new Vector2(0f, 60f)));
            }

            float k = 0.45f + 0.15f * Mathf.Sin(Time.time * 5f); // 진하게, 그래도 얼굴·옷은 알아보게
            var green = new Color(0.4f, 1f, 0.3f);
            for (int i = 0; i < v.Tintables.Length; i++)
                if (v.Tintables[i] != null) v.Tintables[i].color = Color.Lerp(v.BaseColors[i], green * v.BaseColors[i], k);
        }

        private static readonly Color StunColor = new Color(1f, 0.86f, 0.3f);      // 기절 글자·고리·번쩍임(노랑)
        private static readonly Color StunTint = new Color(0.62f, 0.58f, 0.95f);   // 기절 중 그림(회보랏빛)
        private const float StunSwayDegrees = 7f;

        /// <summary>
        /// 기절 표시: 걸리는 순간 머리 위 큰 노란 "기절!" + 발밑에서 노란 고리가 퍼지고 노랗게 번쩍인다.
        /// 기절 중엔 별이 돌고 그 아래 남은 시간 막대가 줄며, 그림은 회보랏빛으로 어둡게 맥박친다(중독 색보다 우선, 비틀거림은 UpdateUnitView).
        /// 풀리면 별·막대가 사라지고 색이 원래(중독이면 중독 색)로 돌아간다.
        /// </summary>
        private void UpdateStun(UnitView v, bool stunned, bool poisoned)
        {
            if (v.StunStars == null) return;
            if (stunned && !v.WasStunned)
            {
                var feet = v.Root.anchoredPosition;
                StartCoroutine(BattleFx.DamageNumber(_fxLayer, feet + new Vector2(0f, 200f), "기절!", StunColor, true));
                StartCoroutine(BattleFx.Shockwave(_fxLayer, feet + new Vector2(0f, 8f), 70f, StunColor));
                StartCoroutine(HitFlash(v, StunColor));
            }
            v.WasStunned = stunned;
            if (v.StunStars.activeSelf != stunned) v.StunStars.SetActive(stunned);
            if (stunned)
            {
                v.StunStars.GetComponent<BattleStunStars>().SetRemaining(v.Unit.StunTimer / Mathf.Max(0.01f, v.Unit.StunTotal)); // 포효 2초·해머 1초 모두 가득에서
                float k = 0.55f + 0.15f * Mathf.Sin(Time.time * 6f);
                for (int i = 0; i < v.Tintables.Length; i++)
                    if (v.Tintables[i] != null) v.Tintables[i].color = Color.Lerp(v.BaseColors[i], StunTint * v.BaseColors[i], k);
                v.StunTinted = true;
            }
            else if (v.StunTinted)
            {
                v.StunTinted = false;
                if (!poisoned) // 중독이면 UpdatePoison이 매 프레임 중독 색을 입힌다
                    for (int i = 0; i < v.Tintables.Length; i++)
                        if (v.Tintables[i] != null) v.Tintables[i].color = v.BaseColors[i];
            }
        }

        private IEnumerator HitFlash(UnitView v) => HitFlash(v, new Color(1f, 0.35f, 0.3f));

        private IEnumerator HitFlash(UnitView v, Color hit)
        {
            const float duration = 0.3f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                for (int i = 0; i < v.Tintables.Length; i++)
                    if (v.Tintables[i] != null) v.Tintables[i].color = Color.Lerp(hit * v.BaseColors[i], v.BaseColors[i], k);
                yield return null;
            }
            for (int i = 0; i < v.Tintables.Length; i++)
                if (v.Tintables[i] != null) v.Tintables[i].color = v.BaseColors[i];
        }

        private IEnumerator FloatText(UnitView v, string text, Color color, float height)
        {
            var go = new GameObject("Float", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
            go.transform.SetParent(_fxLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(200f, 30f);
            Vector2 origin = v.Root.anchoredPosition + new Vector2(UnityEngine.Random.Range(-12f, 12f), height);

            var label = go.GetComponent<Text>();
            label.font = _font;
            label.fontSize = 22;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            var group = go.GetComponent<CanvasGroup>();
            const float duration = 0.9f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                rect.anchoredPosition = origin + new Vector2(0f, 44f * (t / duration));
                group.alpha = 1f - t / duration;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        private static string FormatEvent(BattleEvent evt)
        {
            if (evt.IsPoison) return $"<color=#8fdc5a>{evt.Target.Name} 독 피해 : {evt.Damage}{(evt.TargetDefeated ? " (처치)" : "")}</color>";
            if (evt.Damage < 0) return $"{evt.Attacker.Name} → {evt.Target.Name} : +{-evt.Damage} 회복";
            string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : evt.Blocked ? " (막기)" : "";
            return $"{evt.Attacker.Name} → {evt.Target.Name} : {evt.Damage}{suffix}";
        }

        private void AppendLog(string line)
        {
            _logLines.AddLast(line);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveFirst();
            _logText.text = string.Join("\n", _logLines);
        }

        // ---------- 화면 생성 ----------

        private void BuildUI(Quest quest)
        {
            var canvasGO = new GameObject("BattleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGO.transform;

            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            var backdrop = CreateImage(root, "Backdrop", new Color(0.1f, 0.11f, 0.15f, 1f));
            Stretch((RectTransform)backdrop.transform);

            var title = CreateText(root, QuestDifficulty.RichTitle(quest), 24, TextAnchor.MiddleCenter, UITheme.TitleText);
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(1200f, 40f);

            if (quest.WaveCount > 1)
            {
                var waves = CreateText(root, QuestDifficulty.WavesText(quest), 17, TextAnchor.MiddleCenter, UITheme.MutedText);
                var wavesRect = waves.rectTransform;
                wavesRect.anchorMin = wavesRect.anchorMax = wavesRect.pivot = new Vector2(0.5f, 1f);
                wavesRect.anchoredPosition = new Vector2(0f, -62f);
                wavesRect.sizeDelta = new Vector2(1300f, 28f);
            }

            _timerLabel = CreateText(root, "0:00", 22, TextAnchor.MiddleRight, UITheme.MutedText);
            var timerRect = _timerLabel.rectTransform;
            timerRect.anchorMin = timerRect.anchorMax = timerRect.pivot = new Vector2(1f, 1f);
            timerRect.anchoredPosition = new Vector2(-40f, -24f);
            timerRect.sizeDelta = new Vector2(320f, 40f);

            _speedButton = CreateActionButton(root, Vector2.zero, out _speedButtonText); // 타이머 아래 1배속/2배속 토글
            var speedRect = (RectTransform)_speedButton.transform;
            speedRect.anchorMin = speedRect.anchorMax = speedRect.pivot = new Vector2(1f, 1f);
            speedRect.anchoredPosition = new Vector2(-40f, -68f);
            speedRect.sizeDelta = new Vector2(120f, 40f);
            _speedButtonText.fontSize = 16;
            _speedButton.onClick.AddListener(ToggleSpeed);
            UpdateSpeedButton();

            _bagButton = CreateActionButton(root, Vector2.zero, out _bagButtonText); // 배속 버튼 아래 가방 창 열기
            var bagRect = (RectTransform)_bagButton.transform;
            bagRect.anchorMin = bagRect.anchorMax = bagRect.pivot = new Vector2(1f, 1f);
            bagRect.anchoredPosition = new Vector2(-40f, -114f);
            bagRect.sizeDelta = new Vector2(120f, 40f);
            _bagButtonText.fontSize = 16;
            _bagButton.onClick.AddListener(ToggleBagWindow);
            UpdateBagButton();

            // 필드: 피벗을 왼쪽 아래에 둬서 로컬 좌표 = 필드 좌표. 판 그림은 투명(땅 클릭만 받는다) — 바닥 타일이 판 밖까지 이어져 경계가 안 보인다
            var fieldGO = CreateImage(root, "Field", Color.clear);
            _field = (RectTransform)fieldGO.transform;
            _field.anchorMin = _field.anchorMax = new Vector2(0.5f, 0f);
            _field.pivot = Vector2.zero;
            _field.sizeDelta = new Vector2(FieldBattleSession.FieldWidth, FieldBattleSession.FieldHeight);
            _field.localScale = new Vector3(FieldZoom, FieldZoom, 1f);
            _field.anchoredPosition = new Vector2(-FieldBattleSession.FieldWidth * FieldZoom / 2f, FieldBottom);
            fieldGO.AddComponent<BattleClickTarget>().OnClick = OnGroundClicked;
            BattleGroundTiles.Build(_field, quest.Region, quest.Title.GetHashCode(), // 지역별 바닥 타일(맨 아래 층)
                deepForest: quest.Waves.Any(w => w.IsBoss)); // 보스전은 숲 한가운데(양옆까지 숲)
            if (quest.Waves.Any(w => w.IsBoss))
            {
                // 보스 위험 범위 그림을 씬을 여는 김에 미리 그려 둔다(큰 반원은 약 0.4초 걸려 전투 중에 처음 그리면 멈칫한다)
                DangerZoneArt.For(FieldBattleSession.SlamZoneRadius * 2f);
                DangerZoneArt.For(FieldBattleSession.RoarRadius * 2f);
                BossRoarAudio.Prepare(); // 포효 소리 두 개도 미리 잘라 둔다
            }
            _groundLayer = CreateLayer("GroundLayer");
            _unitLayer = CreateLayer("UnitLayer");
            _fxLayer = CreateLayer("FxLayer");
            BattleLighting.Create(_field, _unitLayer); // 마을과 같은 시간대 빛·날씨·유닛 그림자 층

            BuildPauseOverlay();
            BuildActionPanel(root);
        }

        /// <summary>필드를 꽉 채우는 빈 층(피벗 왼쪽 아래 - 자식 좌표 = 필드 좌표). 그래픽이 없어 클릭을 막지 않는다.</summary>
        private RectTransform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_field, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            rect.pivot = Vector2.zero;
            return rect;
        }

        /// <summary>필드 위를 어둡게 덮는 '일시정지' 표시. 클릭은 막지 않는다(멈춘 채 명령 가능).</summary>
        private void BuildPauseOverlay()
        {
            _pauseOverlay = new GameObject("PauseOverlay", typeof(RectTransform));
            _pauseOverlay.transform.SetParent(_field, false);
            var rect = (RectTransform)_pauseOverlay.transform;
            Stretch(rect);

            // 어두운 막은 판 밖 화면 끝까지(판 경계가 드러나지 않게). 아래 패널은 판보다 나중에 그려져 덮이지 않는다.
            var dim = CreateImage(rect, "Dim", new Color(0f, 0f, 0f, 0.35f));
            var dimRect = (RectTransform)dim.transform;
            Stretch(dimRect);
            dimRect.offsetMin = new Vector2(-PauseDimMargin, -PauseDimMargin);
            dimRect.offsetMax = new Vector2(PauseDimMargin, PauseDimMargin);
            dim.GetComponent<Image>().raycastTarget = false;

            var label = CreateText(_pauseOverlay.transform, "일시정지\n<size=18>Space: 계속 · 멈춘 동안 명령하고 아래 분대 명령(1~4)을 내릴 수 있습니다</size>", 40, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -40f / FieldZoom);
            labelRect.sizeDelta = new Vector2(900f, 110f);
            labelRect.localScale = Unzoom; // 판이 줄어 그려져도 글자·버튼은 화면 크기 그대로
            BuildSquadOrderBar(_pauseOverlay.transform);
            _pauseOverlay.SetActive(false);
        }

        // ---------- 분대 명령 ----------
        // 단축키 1~4는 싸우는 중에도 바로 먹고, 멈춘 화면 아래쪽엔 같은 버튼 4개(쿨타임 표시). 조종 중인 분대장의 분대에만 먹히고, 명령마다 쿨타임이 있다(FieldBattleSession).

        private static readonly string[] SquadOrderNames = { "집결", "후퇴", "전진", "집중 공격" };
        private static readonly string[] SquadOrderHelp = { "분대장 곁에 모여 지킴", "뒤로 물러나 지킴", "모두 적에게 돌격", "적 하나를 다 같이" };
        private readonly Button[] _squadOrderButtons = new Button[4];
        private readonly Text[] _squadOrderLabels = new Text[4];

        private void BuildSquadOrderBar(Transform parent)
        {
            const float width = 180f, height = 64f, gap = 14f;
            float left = -(width * 4 + gap * 3) / 2f + width / 2f;
            for (int i = 0; i < 4; i++)
            {
                var order = (SquadOrder)i;
                var button = CreateActionButton(parent, Vector2.zero, out var label);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(left + i * (width + gap), 28f) / FieldZoom;
                rect.sizeDelta = new Vector2(width, height);
                rect.localScale = Unzoom;
                label.supportRichText = true;
                label.lineSpacing = 0.9f;
                button.onClick.AddListener(() => PressSquadOrder(order));
                _squadOrderButtons[i] = button;
                _squadOrderLabels[i] = label;
            }
        }

        private void PressSquadOrder(SquadOrder order)
        {
            EventSystem.current?.SetSelectedGameObject(null); // 버튼이 선택된 채 남아 Space에 다시 눌리지 않게
            if (_resultShown || _salvaging || _deploying) return;
            if (!_session.SquadOrderReady(order))
            {
                float cd = _session.SquadOrderCooldown(order);
                if (cd > 0f) AppendLog($"<color=#a3937c>{SquadOrderNames[(int)order]}: {Mathf.CeilToInt(cd)}초 뒤 다시 내릴 수 있다</color>");
                return;
            }
            _picking = false;
            switch (order)
            {
                case SquadOrder.Rally:
                    if (_session.OrderRally()) OnSquadMoveOrdered("집결 — 분대장 곁으로 모여 지킨다");
                    break;
                case SquadOrder.Retreat:
                    if (_session.OrderRetreat()) OnSquadMoveOrdered("후퇴 — 뒤로 물러나 지킨다");
                    break;
                case SquadOrder.Advance:
                    if (_session.OrderAdvance()) AppendLog("<color=#ffd34a>분대 명령: 전진 — 모두 적에게 달려든다</color>");
                    break;
                case SquadOrder.Focus:
                    _focusPicking = !_focusPicking; // 적을 누르면 내려진다(OnUnitClicked)
                    break;
            }
        }

        /// <summary>집결·후퇴: 기록을 남기고 대원마다 갈 곳에 이동 표시.</summary>
        private void OnSquadMoveOrdered(string what)
        {
            AppendLog($"<color=#ffd34a>분대 명령: {what}</color>");
            var leader = _session.Leader;
            foreach (var u in _session.Units)
                if (u.IsPlayerSide && u.IsAlive && u.Squad == leader.Squad && u.MoveGoal.HasValue)
                    StartCoroutine(MoveMarker(new Vector2(u.MoveGoal.Value.X, u.MoveGoal.Value.Y)));
        }

        private void RefreshSquadOrderBar()
        {
            if (!_paused || _squadOrderButtons[0] == null) return;
            for (int i = 0; i < 4; i++)
            {
                var order = (SquadOrder)i;
                float cd = _session.SquadOrderCooldown(order);
                bool ready = _session.SquadOrderReady(order);
                _squadOrderButtons[i].interactable = ready;
                string head = order == SquadOrder.Focus && _focusPicking
                    ? "집중 공격 — 적 선택"
                    : cd > 0f ? $"[{i + 1}] {SquadOrderNames[i]} ({Mathf.CeilToInt(cd)})" : $"[{i + 1}] {SquadOrderNames[i]}";
                _squadOrderLabels[i].text = $"{head}\n<size=12>{SquadOrderHelp[i]}</size>";
            }
        }

        private UnitView CreateUnitView(FieldUnit unit, ComposedCharacter appearance, int tier)
        {
            var rootGO = new GameObject(unit.Combatant.Name, typeof(RectTransform), typeof(CanvasGroup));
            rootGO.transform.SetParent(_unitLayer, false);
            var root = (RectTransform)rootGO.transform;
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            // 발밑 그림자 · 분대장 고리 · 대상 고리
            var shadow = CreateImage(root, "Shadow", new Color(0f, 0f, 0f, 0.35f));
            shadow.GetComponent<Image>().sprite = BattlePlaceholderSprites.Circle;
            PlaceAtFeet(shadow, 70f, 20f);
            if (unit.IsLeader)
            {
                var ring = CreateImage(root, "LeaderRing", LeaderColor);
                ring.GetComponent<Image>().sprite = BattlePlaceholderSprites.Ring;
                PlaceAtFeet(ring, 96f, 30f);
            }
            var targetRingGO = CreateImage(root, "TargetRing", Color.red);
            var targetRing = targetRingGO.GetComponent<Image>();
            targetRing.sprite = BattlePlaceholderSprites.Ring;
            PlaceAtFeet(targetRingGO, 104f, 34f);
            targetRing.enabled = false;

            // 그림
            var bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(root, false);
            var body = (RectTransform)bodyGO.transform;
            body.anchorMin = body.anchorMax = new Vector2(0.5f, 0f);
            body.pivot = new Vector2(0.5f, 0f);
            body.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            BattlePortraitAnimator animator = null;
            BattleMonsterSprite monsterSprite = null;
            Image[] tintables;
            float headLift = 0f; // 큰 몬스터(보스)는 머리가 높아 이름·막대를 그만큼 올린다
            if (appearance != null)
            {
                var portrait = BattlePortraitAnimator.Create(body, appearance, (int)PortraitSize, clip: false); // 검격이 칸에 잘리지 않게
                var pr = (RectTransform)portrait.transform;
                pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
                pr.sizeDelta = new Vector2(PortraitSize, PortraitSize);
                pr.anchoredPosition = new Vector2(0f, -6f);
                animator = portrait.GetComponent<BattlePortraitAnimator>();
                tintables = portrait.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != portrait).ToArray();
                unit.AttackAnimSeconds = animator.SlashSeconds; // 검격이 끝날 때까지 제자리
            }
            else if (BattleMonsterSprite.Frames() != null)
            {
                // 몬스터 그림(고블린 숨쉬기 8프레임). 보스는 더 크게. 칸 아래 여백만큼 내려 발을 그림자 위에 세운다.
                var kind = BattleMonsterSprite.KindFor(unit.Combatant.Name, unit.IsMelee); // 늑대 계열은 늑대, 그 밖엔 검 고블린·고블린 궁수
                float s = MonsterSize * BattlePlaceholderSprites.SizeForTier(tier) * BattleMonsterSprite.SizeScale(kind); // 종류별 크기(고블린은 작게)
                var monster = CreateImage(body, "Monster", Color.white);
                var image = monster.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                BattleMonsterSprite.Layout((RectTransform)monster.transform, s, kind); // 시트 여백까지 담고 발을 그림자 위에
                monsterSprite = monster.AddComponent<BattleMonsterSprite>();
                monsterSprite.Init(kind);
                tintables = new[] { image };
                unit.AttackAnimSeconds = monsterSprite.AttackSeconds; // 공격 동작이 끝날 때까지 제자리
                unit.AttackHitDelay = monsterSprite.AttackHitDelay;   // 해머 기절은 해머가 닿는 순간에(거목 엔트)
                headLift = (s - MonsterSize) * MonsterHeadRatio;
            }
            else
            {
                float s = 76f * BattlePlaceholderSprites.SizeForTier(tier);
                var blob = CreateImage(body, "Placeholder", BattlePlaceholderSprites.ColorForTier(tier));
                blob.GetComponent<Image>().sprite = BattlePlaceholderSprites.Circle;
                blob.GetComponent<Image>().raycastTarget = false;
                var br = (RectTransform)blob.transform;
                br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0f);
                br.sizeDelta = new Vector2(s, s);
                br.anchoredPosition = new Vector2(0f, 4f);
                tintables = new[] { blob.GetComponent<Image>() };
            }
            BattleShadow.AttachUnit(root, tintables, shadow.GetComponent<Image>()); // 마을과 같은 해 그림자(발밑 기준)

            // 머리 위: 이름 · 체력 · 공격 대기. 보스는 머리 위 대신 화면 위쪽 가운데 큰 막대(칭호·이름 · 체력 · 공격 대기).
            bool boss = !unit.IsPlayerSide && tier >= 2;
            CanvasGroup bossHud = boss ? CreateBossHud(unit.Combatant.Name) : null;
            if (!boss)
            {
                string label = unit.IsLeader ? $"★{unit.Combatant.Name}" : unit.Combatant.Name;
                var name = CreateText(root, label, UnitNameSize, TextAnchor.MiddleCenter,
                    unit.IsLeader ? LeaderColor : unit.IsPlayerSide ? UITheme.BodyText : EnemyNameColor(unit.Combatant.Name));
                name.font = UITheme.Font;
                name.supportRichText = true;
                name.raycastTarget = false;
                name.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
                PlaceAbove(name.rectTransform, 154f + headLift, 200f, 24f);
            }

            // 막대 판(우리 UI와 같은 청동 베벨, 분대장은 금빛): 위 칸 = 체력(깎인 자국 → 체력, 위 광택·아래 그림자),
            // 아래 칸 = 공격 대기(청동색으로 차오르고 다 차면 밝게). 보스는 화면 위 큰 막대 안에 같은 판을 크게 만든다.
            float barWidth = boss ? BossBarWidth : 108f;
            var bars = new GameObject("Bars", typeof(RectTransform));
            if (boss)
            {
                bars.transform.SetParent(bossHud.transform, false);
                var barsRect = (RectTransform)bars.transform;
                barsRect.anchorMin = barsRect.anchorMax = barsRect.pivot = new Vector2(0.5f, 0f);
                barsRect.sizeDelta = new Vector2(BossBarWidth, BossBarPlateHeight);
                barsRect.anchoredPosition = Vector2.zero;
            }
            else
            {
                bars.transform.SetParent(root, false);
                PlaceAbove((RectTransform)bars.transform, 120f + headLift, barWidth, BarPlateHeight);
            }

            var frame = CreateImage(bars.transform, "BarPlate", Color.white);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = BattleFx.BarPlate(unit.IsLeader);
            frameImage.type = Image.Type.Sliced;
            Inset((RectTransform)frame.transform, 0f);

            const float plate = BattleFx.BarPlateBorder;
            var hpBg = CreateImage(frame.transform, "HpBg", HpBack);
            var hpBgRect = (RectTransform)hpBg.transform;
            Inset(hpBgRect, plate);
            float readyHeight = boss ? BossReadyBarHeight : ReadyBarHeight;
            hpBgRect.offsetMin = new Vector2(plate, plate + readyHeight + 1f); // 아래는 공격 대기 칸 + 1px 구분선
            var trail = CreateBarFill(hpBg.transform, HpTrailColor);
            var hpFill = CreateBarFill(hpBg.transform, HpHigh);
            var gloss = CreateImage(hpFill.transform, "Gloss", new Color(1f, 1f, 1f, 0.1f));
            var glossRect = (RectTransform)gloss.transform;
            glossRect.anchorMin = new Vector2(0f, 0.6f);
            glossRect.anchorMax = Vector2.one;
            glossRect.offsetMin = glossRect.offsetMax = Vector2.zero;
            gloss.GetComponent<Image>().raycastTarget = false;
            var shade = CreateImage(hpFill.transform, "Shade", new Color(0f, 0f, 0f, 0.22f));
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = new Vector2(1f, 0f);
            shadeRect.offsetMin = Vector2.zero;
            shadeRect.offsetMax = new Vector2(0f, 2f);
            shade.GetComponent<Image>().raycastTarget = false;

            Text hpText = null;
            if (unit.IsPlayerSide || boss)
            {
                hpText = CreateText(hpBg.transform, "", boss ? 16 : UnitHpTextSize, TextAnchor.MiddleCenter, UITheme.BodyText);
                hpText.font = UITheme.BoldFont;
                hpText.raycastTarget = false;
                hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
                hpText.verticalOverflow = VerticalWrapMode.Overflow;
                hpText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                Stretch(hpText.rectTransform);
            }

            // 공격 대기: 같은 판 아래 칸
            var readyBg = CreateImage(frame.transform, "ReadyBg", ReadyBack);
            var readyRect = (RectTransform)readyBg.transform;
            readyRect.anchorMin = Vector2.zero;
            readyRect.anchorMax = new Vector2(1f, 0f);
            readyRect.offsetMin = new Vector2(plate, plate);
            readyRect.offsetMax = new Vector2(-plate, plate + readyHeight);
            readyBg.GetComponent<Image>().raycastTarget = false;
            var readyFill = CreateBarFill(readyBg.transform, ReadyCharging);

            // 클릭 영역(그림 크기만큼, 투명)
            var clickGO = CreateImage(root, "Click", new Color(0f, 0f, 0f, 0f));
            var clickRect = (RectTransform)clickGO.transform;
            clickRect.anchorMin = clickRect.anchorMax = new Vector2(0.5f, 0f);
            clickRect.pivot = new Vector2(0.5f, 0f);
            clickRect.sizeDelta = new Vector2(80f, 120f);
            var clickImage = clickGO.GetComponent<Image>();
            clickImage.raycastTarget = true;
            clickGO.AddComponent<BattleClickTarget>().OnClick = e => OnUnitClicked(unit, e);
            if (!unit.IsPlayerSide) // 적에 마우스를 올리면 이름·등급·체력·공격 방식(체력은 계속 갱신)
            {
                string rank = tier >= 2 ? "보스" : tier == 1 ? "정예" : "일반";
                var stats = unit.Combatant.Stats;
                clickGO.AddComponent<TooltipTrigger>();
                clickGO.AddComponent<LiveTooltip>().Build = () =>
                    $"<b>{unit.Combatant.Name}</b> ({rank})\n체력 {Mathf.Max(0, unit.Combatant.CurrentHealth)}/{stats.MaxHealth} · 공격 {stats.Attack} · 방어 {stats.Defense}\n" +
                    (unit.IsMelee ? "근접 공격" : "원거리 공격");
            }
            if (unit.IsPlayerSide)
            {
                var drag = clickGO.AddComponent<BattleDragTarget>();
                drag.OnBegin = e => OnDragBegin(unit, e);
                drag.OnMove = e => OnDragMove(unit, e);
                drag.OnEnd = e => OnDragEnd(unit, e);
                drag.OnEnter = () => _hoverUnit = unit;
                drag.OnExit = () => { if (_hoverUnit == unit) _hoverUnit = null; };
            }

            var view = new UnitView
            {
                Unit = unit,
                Root = root,
                Body = body,
                Group = rootGO.GetComponent<CanvasGroup>(),
                Tintables = tintables,
                BaseColors = tintables.Select(i => i.color).ToArray(),
                Bars = bars,
                BossHud = bossHud,
                StunStars = unit.IsPlayerSide ? BattleFx.CreateStunStars(root, 176f + headLift) : null, // 이름 바로 위
                HpFill = (RectTransform)hpFill.transform,
                HpFillImage = hpFill,
                HpTrail = (RectTransform)trail.transform,
                HpText = hpText,
                ReadyFill = (RectTransform)readyFill.transform,
                ReadyFillImage = readyFill,
                TargetRing = targetRing,
                ClickArea = clickImage,
                Animator = animator,
                Monster = monsterSprite,
                HopPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                PoisonTag = BattleFx.CreatePoisonTag(root, new Vector2(-(boss ? 108f : barWidth) * 0.5f - 4f, 122f + headLift)), // 체력 막대 왼쪽(보스는 머리 위 같은 자리)
            };
            UpdateUnitView(view);
            return view;
        }

        /// <summary>
        /// 보스 큰 막대 자리(화면 위쪽 가운데): 붉은 BOSS 꼬리표 · 칭호(금빛) + 이름, 그 아래에 CreateUnitView가 체력·공격 대기 판을 크게 넣는다.
        /// 빛 막보다 위(캔버스 맨 앞)라 밤·비에도 어두워지지 않는다. 처음엔 투명 — UpdateBars가 위에서 내려오며 나타나게 한다.
        /// </summary>
        private CanvasGroup CreateBossHud(string bossName)
        {
            var go = new GameObject("BossHud", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_canvas.transform, false);
            go.transform.SetAsLastSibling();
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(BossBarWidth, BossHudHeight);
            rect.anchoredPosition = new Vector2(0f, BossHudY + BossHudSlide);
            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            string title = GN3.World.RegionInfo.BossTitle(bossName); // "은월의 숲의 주인"
            var label = CreateText(go.transform, title != null ? $"<size=17><color=#e8c27a>{title}</color></size>   {bossName}" : bossName,
                26, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.7f));
            label.font = UITheme.BoldFont;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.95f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(0f, BossHudHeight - BossBarPlateHeight - 2f);
            labelRect.anchoredPosition = Vector2.zero;

            BattleFx.CreateBossTag(go.transform, 0f); // 막대 왼쪽 위 붉은 "BOSS"(숨 쉬듯)
            var tag = (RectTransform)go.transform.Find("BossTag");
            tag.anchoredPosition = new Vector2(-BossBarWidth * 0.5f + 36f, BossBarPlateHeight + 6f);
            return group;
        }

        private static void PlaceAtFeet(GameObject go, float width, float height)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, -height / 2f);
            go.GetComponent<Image>().raycastTarget = false;
        }

        private static void PlaceAbove(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);
            var img = rect.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }

        /// <summary>막대 묶음 안에서 가로로 꽉 찬 한 줄(아래에서 y, 높이 height, 좌우 여백 pad).</summary>
        private static void PlaceBar(RectTransform rect, float y, float height, float pad)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(pad, y);
            rect.offsetMax = new Vector2(-pad, y + height);
            rect.GetComponent<Image>().raycastTarget = false;
        }

        private static void Inset(RectTransform rect, float border)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(border, border);
            rect.offsetMax = new Vector2(-border, -border);
            rect.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>왼쪽부터 채워지는 막대. 길이는 anchorMax.x로 정한다(스프라이트 없는 Image는 Filled가 먹지 않아서).</summary>
        private static Image CreateBarFill(Transform parent, Color color)
        {
            var go = CreateImage(parent, "Fill", color);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private void BuildActionPanel(Transform root)
        {
            var panel = CreateImage(root, "ActionPanel", new Color(1f, 1f, 1f, 0.97f));
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 230f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;

            _leaderLabel = CreateText(panel.transform, "", 19, TextAnchor.UpperLeft, LeaderColor);
            _leaderLabel.fontStyle = FontStyle.Bold;
            var leaderRect = _leaderLabel.rectTransform;
            leaderRect.anchorMin = leaderRect.anchorMax = leaderRect.pivot = new Vector2(0f, 1f);
            leaderRect.anchoredPosition = new Vector2(28f, -18f);
            leaderRect.sizeDelta = new Vector2(420f, 30f);

            _skillButton = CreateActionButton(panel.transform, new Vector2(28f, -60f), out _skillButtonText);
            _skillButton.onClick.AddListener(OnMainButton);

            _resetButton = CreateActionButton(panel.transform, new Vector2(340f, -60f), out var resetLabel);
            ((RectTransform)_resetButton.transform).sizeDelta = new Vector2(130f, 56f);
            resetLabel.text = "기본 배치";
            _resetButton.onClick.AddListener(ResetFormation);

            _hintLabel = CreateText(panel.transform, "", 15, TextAnchor.UpperLeft, UITheme.MutedText);
            var hintRect = _hintLabel.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0f, 1f);
            hintRect.anchoredPosition = new Vector2(28f, -128f);
            hintRect.sizeDelta = new Vector2(420f, 80f);

            var logBg = CreateImage(panel.transform, "LogPanel", Color.white);
            var logRect = (RectTransform)logBg.transform;
            logRect.anchorMin = new Vector2(0f, 0f);
            logRect.anchorMax = new Vector2(1f, 1f);
            logRect.offsetMin = new Vector2(480f, 16f);
            logRect.offsetMax = new Vector2(-28f, -16f);
            var logBgImage = logBg.GetComponent<Image>();
            logBgImage.sprite = UITheme.Inset;
            logBgImage.type = Image.Type.Sliced;

            _logText = CreateText(logBg.transform, "", 14, TextAnchor.LowerLeft, UITheme.MutedText);
            _logText.supportRichText = true;
            var logTextRect = _logText.rectTransform;
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(12f, 8f);
            logTextRect.offsetMax = new Vector2(-12f, -8f);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private Button CreateActionButton(Transform parent, Vector2 anchoredPosition, out Text label)
        {
            var go = CreateImage(parent, "ActionButton", Color.white);
            go.AddComponent<Button>();
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(300f, 56f);

            var image = go.GetComponent<Image>();
            image.sprite = UITheme.Button;
            image.type = Image.Type.Sliced;

            label = CreateText(go.transform, "", 18, TextAnchor.MiddleCenter, UITheme.BodyText);
            Stretch(label.rectTransform);
            label.raycastTarget = false;

            return go.GetComponent<Button>();
        }

        // ---------- 결과(보상) 화면 ----------

        private const float ResultWidth = 640f;
        private const float ResultPad = 40f;            // 판 안쪽 좌우 여백
        private const float ResultRowHeight = 46f;
        private const float ResultIconSize = 40f;
        private const float ResultEmblemSize = 46f;    // 늑대 휘장은 교차한 검 때문에 가로로 넓어 방패가 작아 보여 조금 더 크게
        private static readonly Color VictoryColor = new Color(1f, 0.82f, 0.35f);   // 승리 쪽 강조(장식선·골드)
        private static readonly Color VictoryTitleColor = Color.white;              // "승리" 글자(배너·결과창 제목)
        private static readonly Color DefeatColor = new Color(0.92f, 0.4f, 0.35f);
        private static readonly Color GainColor = new Color(0.55f, 0.9f, 0.5f);
        private static readonly Color RepColor = new Color(0.6f, 0.8f, 1f);
        private static readonly Color DividerColor = new Color(UITheme.BronzeLight.r, UITheme.BronzeLight.g, UITheme.BronzeLight.b, 0.35f);

        /// <summary>
        /// 결과 화면(가운데 정렬, 상자 없이): 제목("승 리"/"패 배" + 양옆 가는 선) → 한 줄 부제(퀘스트 · 전투 시간 · 라운드) →
        /// 보상 한 줄(코인 +골드, 늑대 휘장 +명성) → 가는 구분선 → 대원 줄(초상 · 이름 · 가는 체력선 · 성장/전사) →
        /// 귀환 안내 · '마을로 돌아가기'. 아래에서 살짝 올라오며 나타난다.
        /// </summary>
        private void BuildResultOverlay(ExpeditionBattle.Report report)
        {
            bool victory = report.Victory;
            Color accent = victory ? VictoryColor : DefeatColor;

            var overlay = CreateImage(_canvas.transform, "ResultOverlay", new Color(0f, 0f, 0f, 0.6f));
            Stretch((RectTransform)overlay.transform);
            overlay.transform.SetAsLastSibling();

            int rows = report.Members.Count;
            // 가방(챙긴 것이 있을 때 — 지기 전에 뒤져 챙긴 것도): 머리줄 + 가방 칸 격자 + 가방 밖 줄. 높이만큼 구분선·대원 목록을 아래로 민다
            var loot = report.Loot ?? new List<(LootItem item, int count)>();
            bool hasOutside = BagSlotsUI.Outside(loot).Count > 0;
            // 대원마다 큰 칸 가방 카드: 이미 가방에 담긴 뒤라(ExpeditionBattle.StowLoot) 지금 든 것 그대로, 쓰러진 대원은 잃어서 빈 가방
            var bagShares = _fighters.Select(f => f.IsAlive ? f.BagContents.ToList() : new List<LootItem>()).ToList();
            float cardsHeight = 0f;
            for (int m = 0; m < _fighters.Count; m++)
                cardsHeight += BagSlotsUI.CardHeight(_fighters[m], bagShares[m].Count, ResultBagSlot, ResultBagPerLine, ResultBagGap) + ResultBagCardGap;
            float bagHeight = ResultBagHeader + 6f + cardsHeight + (hasOutside ? 6f + ResultBagOutside : 0f);
            float lootExtra = loot.Count > 0 ? bagHeight + 8f : 0f;
            float membersTop = -196f - lootExtra;
            float height = -membersTop + ResultRowHeight * rows + 124f;
            var panel = CreateImage(overlay.transform, "ResultPanel", Color.white);
            panel.AddComponent<CanvasGroup>();
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(loot.Count > 0 ? ResultBagWidth : ResultWidth, height); // 가방 카드가 들어가게 넓힌다
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;

            // 제목: 자간을 띄운 짧은 말 + 양옆 가는 선
            var title = CreateText(panel.transform, victory ? "승  리" : "패  배", 40, TextAnchor.MiddleCenter, victory ? VictoryTitleColor : accent);
            title.fontStyle = FontStyle.Bold;
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -2f);
            PlaceTop(title.rectTransform, -30f, 52f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                var line = CreateImage(panel.transform, "TitleRule", new Color(accent.r, accent.g, accent.b, 0.6f));
                var lineRect = (RectTransform)line.transform;
                lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 1f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.anchoredPosition = new Vector2(side * 140f, -56f);
                lineRect.sizeDelta = new Vector2(80f, 2f);
                line.GetComponent<Image>().raycastTarget = false;
            }

            // 부제 한 줄
            int secs = Mathf.FloorToInt(_session.Elapsed);
            string waveLine = report.WaveCount <= 1 ? ""
                : victory ? $"  ·  {report.WaveCount}라운드 돌파"
                : $"  ·  {report.WavesCleared + 1}/{report.WaveCount}라운드";
            var subtitle = CreateText(panel.transform, $"{QuestDifficulty.RichTitle(_expedition.Quest)}  ·  {secs / 60}:{secs % 60:00}{waveLine}",
                15, TextAnchor.MiddleCenter, UITheme.MutedText);
            subtitle.supportRichText = true;
            PlaceTop(subtitle.rectTransform, -86f, 24f, ResultPad);

            // 보상 한 줄(가운데 정렬): 코인 +골드 · 휘장 +명성
            var rewards = new GameObject("Rewards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rewards.transform.SetParent(panel.transform, false);
            PlaceTop((RectTransform)rewards.transform, -118f, 48f, ResultPad);
            var layout = rewards.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var goldValue = AddReward(rewards.transform, UIIcons.Load("UIIcons/gold"), ResultIconSize,
                victory && report.Gold > 0 ? "+0 G" : "보상 없음", victory && report.Gold > 0 ? VictoryColor : UITheme.MutedText);
            AddRewardGap(rewards.transform, 36f);
            AddReward(rewards.transform, UIIcons.Load("UIIcons/reputation"), ResultEmblemSize,
                (report.Reputation >= 0 ? $"+{report.Reputation}" : report.Reputation.ToString()) + " 명성",
                report.Reputation >= 0 ? RepColor : DefeatColor);
            if (victory && (report.DirectGold > 0 || report.DirectReputation > 0))
            {
                AddRewardGap(rewards.transform, 24f);
                var direct = AddReward(rewards.transform, null, 0f, $"(직접 전투 보너스 +{report.DirectGold}G · 명성 +{report.DirectReputation})", RepColor);
                direct.fontSize = 18;
                direct.fontStyle = FontStyle.Normal;
            }

            // 가방: "가방 n/m칸" + 칸 격자(채운 칸은 등급 색 테두리 아이콘, 마우스를 올리면 이름·값) + 가방 밖(재료·동전).
            // 전리품은 대원 가방에 남고, 마을에서 가방 창의 [창고에 넣기]로 꺼내 판다.
            if (loot.Count > 0)
            {
                var bag = new GameObject("Bag", typeof(RectTransform), typeof(VerticalLayoutGroup));
                bag.transform.SetParent(panel.transform, false);
                PlaceTop((RectTransform)bag.transform, -172f, bagHeight, ResultPad);
                var bagLayout = bag.GetComponent<VerticalLayoutGroup>();
                bagLayout.childAlignment = TextAnchor.UpperCenter;
                bagLayout.spacing = ResultBagCardGap;
                bagLayout.childControlWidth = bagLayout.childControlHeight = true;
                bagLayout.childForceExpandWidth = true;
                bagLayout.childForceExpandHeight = false;
                string left = (report.BagLeft > 0 ? $"  <color=#a3937c>· 칸이 모자라 {report.BagLeft}개는 두고 왔다</color>" : "")
                    + (report.BagLost > 0 ? $"  <color=#a3937c>· 쓰러진 대원의 가방과 함께 {report.BagLost}개를 잃었다</color>" : "");
                var header = CreateText(bag.transform, $"<b>가방</b>  <color=#ffd36b>{BagCatalog.SlotsUsed(loot)}/{report.BagCapacity}칸</color>{left}",
                    16, TextAnchor.MiddleCenter, UITheme.BodyText);
                header.supportRichText = true;
                header.horizontalOverflow = HorizontalWrapMode.Overflow;
                header.gameObject.AddComponent<LayoutElement>().minHeight = ResultBagHeader;
                for (int m = 0; m < _fighters.Count; m++)
                    BagSlotsUI.CreateMercBagCard(bag.transform, _fighters[m], bagShares[m], ResultBagSlot, ResultBagPerLine, ResultBagGap);
                BagSlotsUI.CreateOutsideRow(bag.transform, loot, ResultBagOutside - 4f);
                FirstTimeTips.Show(FirstTimeTips.BattleLoot, "전리품과 가방",
                    "챙긴 전리품은 대원 가방에 담겨 마을까지 그대로 남는다(건설 재료는 창고로, 동전 주머니는 바로 금화로).\n" +
                    "팔거나 제작·건설에 쓰려면 길드 창고의 [가방 비우기]나 가방 창의 [창고에 넣기]로 꺼내자.\n" +
                    "가방이 차 있으면 다음 원정에서 그만큼 덜 줍는다. 쓰러진 대원의 가방은 함께 잃는다.");
            }

            // 구분선
            var divider = CreateImage(panel.transform, "Divider", DividerColor);
            PlaceTop((RectTransform)divider.transform, -180f - lootExtra, 1f, ResultPad);
            divider.GetComponent<Image>().raycastTarget = false;

            // 대원
            float y = membersTop;
            foreach (var m in report.Members)
            {
                CreateResultRow(panel.transform, m, y);
                y -= ResultRowHeight;
            }

            // 귀환 안내 + 버튼
            int survivors = report.Members.Count(m => !m.Fallen);
            string footer = survivors > 0
                ? $"생존 {survivors}명 귀환  ·  마을까지 {GN3.World.GameClock.FormatDuration(report.ReturnHours)}"
                : "전멸 — 돌아올 대원이 없습니다";
            var footerText = CreateText(panel.transform, footer, 14, TextAnchor.MiddleCenter, survivors > 0 ? UITheme.MutedText : DefeatColor);
            var footerRect = footerText.rectTransform;
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.offsetMin = new Vector2(ResultPad, 80f);
            footerRect.offsetMax = new Vector2(-ResultPad, 102f);

            var backButton = CreateActionButton(panel.transform, Vector2.zero, out var backLabel);
            backLabel.text = "마을로 돌아가기";
            backLabel.fontStyle = FontStyle.Bold;
            var backRect = (RectTransform)backButton.transform;
            backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 24f);
            backRect.sizeDelta = new Vector2(240f, 48f);
            backButton.onClick.AddListener(() => SceneManager.LoadScene("MainScene"));
            _resultBackButton = backButton;
            _resultBackAt = Time.unscaledTime;
            backButton.gameObject.AddComponent<TooltipTrigger>().Text = "마을로 돌아가기 (Enter · Space)";

            StartCoroutine(RiseIn(panelRect));
            if (victory && report.Gold > 0) StartCoroutine(CountUp(goldValue, report.Gold));
        }

        /// <summary>부모 위쪽에 가로로 꽉 찬 줄(위에서 y, 높이 height, 좌우 여백 pad).</summary>
        private static void PlaceTop(RectTransform rect, float y, float height, float pad)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(pad, y - height);
            rect.offsetMax = new Vector2(-pad, y);
        }

        /// <summary>보상 한 칸: 아이콘(iconSize) + 값. 값 글자를 돌려준다(골드 세기 연출용). 아이콘이 없으면 글자만.</summary>
        private Text AddReward(Transform parent, Sprite icon, float iconSize, string value, Color color)
        {
            if (icon != null)
            {
                var iconGO = CreateImage(parent, "Icon", Color.white);
                var iconImage = iconGO.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                var iconLayout = iconGO.AddComponent<LayoutElement>();
                iconLayout.preferredWidth = iconLayout.preferredHeight = iconSize;
            }
            var text = CreateText(parent, value, 26, TextAnchor.MiddleLeft, color);
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -1f);
            // 골드가 세어 올라가며 길어져도 옆 칸이 밀리지 않게 넉넉한 최소 폭
            text.gameObject.AddComponent<LayoutElement>().minWidth = 120f;
            return text;
        }

        /// <summary>적 이름표 색: 개체 등급(이름 앞말)이 강한·우두머리면 눈에 띄게, 약한은 흐리게.</summary>
        private static Color EnemyNameColor(string name) => MonsterIV.RankOf(name) switch
        {
            MonsterRank.Weak => new Color(0.7f, 0.66f, 0.64f),
            MonsterRank.Strong => new Color(1f, 0.65f, 0.29f),
            MonsterRank.Alpha => new Color(1f, 0.42f, 0.35f),
            _ => new Color(0.92f, 0.66f, 0.6f),
        };

        private const float LootGradeOutline = 1.5f; // 아이콘 둘레 등급 색 테두리 두께
        // 결과 화면 가방(BagSlotsUI 용병별 카드): 한 줄 10칸(가장 큰 가방), 칸 44
        private const int ResultBagPerLine = 10;
        private const float ResultBagSlot = 44f, ResultBagGap = 4f, ResultBagCardGap = 6f;
        private const float ResultBagWidth = 760f;   // 가방이 있으면 결과 판을 이만큼 넓힌다(카드 폭 ≈ 초상 44 + 정보 112 + 칸 476 + 여백)
        private const float ResultBagHeader = 22f;   // "가방 n/m칸" 줄
        private const float ResultBagOutside = 28f;  // 가방 밖(재료·동전) 줄

        private static void AddRewardGap(Transform parent, float width)
        {
            var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(parent, false);
            gap.GetComponent<LayoutElement>().preferredWidth = width;
        }

        /// <summary>대원 한 줄(줄무늬 없이): 초상 · 이름(★ 분대장) · 가는 체력선(잃은 만큼 옅은 빨강, 숫자는 마우스를 올리면) · 오른쪽에 성장 또는 전사.
        /// 전사한 대원은 줄 전체를 흐리게.</summary>
        private void CreateResultRow(Transform parent, ExpeditionBattle.MemberReport m, float y)
        {
            var row = new GameObject(m.Merc.Name, typeof(RectTransform), typeof(CanvasGroup));
            row.transform.SetParent(parent, false);
            PlaceTop((RectTransform)row.transform, y, ResultRowHeight, ResultPad);
            if (m.Fallen) row.GetComponent<CanvasGroup>().alpha = 0.5f;

            var portrait = CharacterPortraitUI.Create(row.transform, m.Merc.Appearance, 36);
            var pr = (RectTransform)portrait.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0f, 0.5f);
            pr.pivot = new Vector2(0f, 0.5f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(36f, 36f);

            bool leader = _leaderMercs.Contains(m.Merc);
            var name = CreateText(row.transform, leader ? $"★ {m.Merc.Name}" : m.Merc.Name, 17, TextAnchor.MiddleLeft,
                leader ? LeaderColor : UITheme.BodyText);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.raycastTarget = false;
            var nameRect = name.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 0.5f);
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.anchoredPosition = new Vector2(50f, 0f);
            nameRect.sizeDelta = new Vector2(104f, 26f);

            // 평점 등급(BattleRating): 이름과 체력선 사이. 마우스를 올리면 활약 수치
            var grade = CreateText(row.transform, BattleRating.RichGrade(m.Score), 18, TextAnchor.MiddleCenter, UITheme.BodyText);
            grade.supportRichText = true;
            grade.fontStyle = FontStyle.Bold;
            grade.raycastTarget = true;
            var gradeRect = grade.rectTransform;
            gradeRect.anchorMin = gradeRect.anchorMax = new Vector2(0f, 0.5f);
            gradeRect.pivot = new Vector2(0f, 0.5f);
            gradeRect.anchoredPosition = new Vector2(156f, 0f);
            gradeRect.sizeDelta = new Vector2(48f, 30f);
            grade.gameObject.AddComponent<TooltipTrigger>().Text = $"평점 {BattleRating.Grade(m.Score)} ({m.Score}점){(m.Mvp ? " · MVP" : "")}\n{m.Stats}";

            // 가는 체력선: 남은 체력(색) + 이번 전투에서 잃은 부분(옅은 빨강). 숫자는 툴팁으로.
            float max = Math.Max(1, m.MaxHealth);
            float before = Mathf.Clamp01(m.HealthBefore / max), after = Mathf.Clamp01(m.HealthAfter / max);
            var track = CreateImage(row.transform, "HpTrack", new Color(0f, 0f, 0f, 0.55f));
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 0.5f);
            trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.anchoredPosition = new Vector2(210f, 0f);
            trackRect.sizeDelta = new Vector2(170f, 6f);
            var lost = CreateBarFill(track.transform, new Color(1f, 0.45f, 0.4f, 0.45f));
            SetLength((RectTransform)lost.transform, before);
            var hp = CreateBarFill(track.transform, after > 0.5f ? HpHigh : after > 0.25f ? HpMid : HpLow);
            SetLength((RectTransform)hp.transform, after);
            // 마우스를 올리기 쉽게 보이지 않는 넓은 칸에 툴팁
            var hover = CreateImage(row.transform, "HpHover", Color.clear);
            var hoverRect = (RectTransform)hover.transform;
            hoverRect.anchorMin = hoverRect.anchorMax = new Vector2(0f, 0.5f);
            hoverRect.pivot = new Vector2(0f, 0.5f);
            hoverRect.anchoredPosition = new Vector2(210f, 0f);
            hoverRect.sizeDelta = new Vector2(170f, 24f);
            hover.AddComponent<TooltipTrigger>().Text = m.Fallen ? $"체력 {m.HealthBefore} → 0"
                : m.HealthAfter < m.HealthBefore ? $"체력 {m.HealthBefore} → {m.HealthAfter} / {m.MaxHealth}"
                : $"체력 {m.HealthAfter} / {m.MaxHealth}";

            string result = m.Fallen ? "전사" : m.Gains.Count > 0 ? "▲ " + string.Join(", ", m.Gains) : "";
            if (m.Mvp) result = result.Length > 0 ? $"MVP · {result}" : "MVP"; // 이번 판 최고 평점
            var resultText = CreateText(row.transform, result, 15, TextAnchor.MiddleRight, m.Fallen ? DefeatColor : GainColor);
            resultText.fontStyle = FontStyle.Bold;
            resultText.raycastTarget = false;
            var resultRect = resultText.rectTransform;
            resultRect.anchorMin = new Vector2(0f, 0f);
            resultRect.anchorMax = new Vector2(1f, 1f);
            resultRect.offsetMin = new Vector2(396f, 0f);
            resultRect.offsetMax = Vector2.zero;
        }

        /// <summary>결과 창이 아래에서 살짝 올라오며 흐릿함 → 또렷하게 나타난다(실제 시간 기준).</summary>
        private IEnumerator RiseIn(RectTransform panel)
        {
            const float duration = 0.25f, rise = 12f;
            var group = panel.GetComponent<CanvasGroup>();
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(-rise, 0f, k));
                group.alpha = k;
                yield return null;
            }
            panel.anchoredPosition = Vector2.zero;
            group.alpha = 1f;
        }

        /// <summary>보상 골드를 0부터 올라가며 센다.</summary>
        private IEnumerator CountUp(Text label, int target)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            const float duration = 0.9f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / duration, 3f);
                label.text = $"+{Mathf.RoundToInt(target * k)} G";
                yield return null;
            }
            label.text = $"+{target} G";
        }

        private static GameObject CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
