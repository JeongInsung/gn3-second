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

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private readonly List<Mercenary> _leaderMercs = new List<Mercenary>(); // 결과 화면 ★ 표시(분대장들, 전투 중 쓰러져도 그대로)
        private FieldBattleSession _session;
        private readonly Dictionary<FieldUnit, UnitView> _views = new Dictionary<FieldUnit, UnitView>();
        private readonly List<UnitView> _drawOrder = new List<UnitView>();
        private bool _picking;      // 스킬 대상 고르는 중
        private bool _focusPicking; // 분대 명령 '집중 공격'의 대상 고르는 중(일시정지 중)
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
        private const float BattleSpeed = 0.7f;

        // 타격감 연출(보이는 것만, 판정과 무관)
        private const float BigHitRatio = 0.2f;          // 대상 최대 체력의 이 비율 이상이면 '큰 피해'로 강조
        private const float KickDistance = 12f, BigKickDistance = 18f;
        private const float KickRecoverPerSecond = 120f;
        private const float DeathFallSeconds = 0.4f;
        private const float ResultDelaySeconds = 1.5f;   // 한쪽이 다 쓰러진 뒤 결과창까지(전투 시간): 마지막 쓰러짐을 보여 준다
        private const float ShakeStrength = 4f, ShakeSeconds = 0.15f;
        private static readonly Color BigHitColor = new Color(1f, 0.78f, 0.25f);
        private RectTransform[] _shakeTargets;

        private static readonly Color LeaderColor = new Color(1f, 0.82f, 0.3f);
        private static readonly Color PickingColor = new Color(0.95f, 0.9f, 0.35f);

        public void Init(Expedition expedition)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _expedition = expedition;
            var quest = expedition.Quest;

            _fighters = expedition.Members.Where(m => m.IsAlive).ToList();
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
            Time.timeScale = BattleSpeed;

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
                _drawOrder.Remove(v);
                Destroy(v.Root.gameObject);
                if (v.BossHud != null) Destroy(v.BossHud.gameObject);
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
        /// 판이 우르릉 흔들린다. 소리는 실제 시간이라 전투 배속(BattleSpeed)을 곱해 전투 시간으로 맞춘다.
        /// </summary>
        private void PlayBossIntros()
        {
            foreach (var v in _views.Values)
            {
                if (v.Unit.IsPlayerSide || v.Monster == null || v.Monster.IntroSeconds <= 0f) continue;
                bool voiced = v.Monster.Kind == MonsterKind.Ent;
                float hold = voiced ? BossRoarAudio.Intro.LoudLength * BattleSpeed : 0f;
                v.Unit.BusyTimer = v.Monster.PlayIntro(hold);
                float peak = v.Monster.IntroPeakDelay; // 입을 가장 크게 벌린 장까지(전투 초)
                if (voiced)
                {
                    StartCoroutine(PlayRoarSound(BossRoarAudio.IntroClip, Mathf.Max(0f, peak - BossRoarAudio.Intro.Onset * BattleSpeed)));
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
                if (Time.time - _overSince >= ResultDelaySeconds) ShowResult();
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
            if (_paused)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Rally);
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Retreat);
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Advance);
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) PressSquadOrder(SquadOrder.Focus);
            }
            if (kb.spaceKey.wasPressedThisFrame) SetPaused(!_paused);
            if (kb.tabKey.wasPressedThisFrame) SwitchSquad();
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
            Time.timeScale = _paused ? 0f : BattleSpeed;
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
            if (!unit.IsAlive) return;
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
            v.Group.alpha = Mathf.Lerp(1f, 0.3f, fall);
            v.ClickArea.raycastTarget = alive;
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
                    // 검격 소리: 검을 휘두르는 용병·검 고블린(궁수는 활이라 뺀다)
                    if (attackerView.Animator != null || (attackerView.Monster != null && attackerView.Monster.Kind == MonsterKind.Sword))
                        GameAudio.PlaySfx(SwordSound);
                }

                string label = heal ? $"+{-evt.Damage}" : evt.Evaded ? "회피" : $"-{evt.Damage}";
                Color color = heal ? new Color(0.45f, 0.9f, 0.5f) : evt.Evaded ? Color.white : new Color(1f, 0.45f, 0.4f);

                bool hit = !heal && !evt.Evaded && evt.Damage > 0;
                // 판정은 이미 났다. 보이는 피격(숫자·번쩍임·체력 막대·쓰러짐)은 칼이 내려치는 프레임이나 투사체가 닿을 때로 미룬다.
                // 원거리는 같은 순간(궁수는 활을 놓는 프레임)에 투사체를 쏜다.
                float delay = heal || slam ? 0f : HitDelay(attackerView);
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
            float hold = BossRoarAudio.Stun.LoudLength * BattleSpeed;
            float soundLead = BossRoarAudio.Stun.Onset * BattleSpeed;
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

        // ---------- 결과 ----------

        private void ShowResult()
        {
            if (_resultShown) return;
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
            };
            var report = ExpeditionBattle.ConcludeReport(_expedition, battle, alertShown: true); // 이 씬의 결과 화면이 보여 준다

            _skillButton.interactable = false;
            StartCoroutine(ShowResultAfterBanner(report));
        }

        /// <summary>큰 "승리!"/"패배…" 배너를 잠깐 보여 준 뒤 결과창을 띄운다(결과 계산은 이미 끝난 상태).</summary>
        private IEnumerator ShowResultAfterBanner(ExpeditionBattle.Report report)
        {
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
            string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
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

            // 필드: 피벗을 왼쪽 아래에 둬서 로컬 좌표 = 필드 좌표
            var fieldGO = CreateImage(root, "Field", new Color(0.2f, 0.24f, 0.16f, 1f));
            _field = (RectTransform)fieldGO.transform;
            _field.anchorMin = _field.anchorMax = new Vector2(0.5f, 0f);
            _field.pivot = Vector2.zero;
            _field.sizeDelta = new Vector2(FieldBattleSession.FieldWidth, FieldBattleSession.FieldHeight);
            _field.anchoredPosition = new Vector2(-FieldBattleSession.FieldWidth / 2f, FieldBottom);
            var fieldImage = fieldGO.GetComponent<Image>();
            fieldImage.sprite = UITheme.Inset;
            fieldImage.type = Image.Type.Sliced;
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
            _pauseOverlay = CreateImage(_field, "PauseOverlay", new Color(0f, 0f, 0f, 0.35f));
            var rect = (RectTransform)_pauseOverlay.transform;
            Stretch(rect);
            _pauseOverlay.GetComponent<Image>().raycastTarget = false;

            var label = CreateText(_pauseOverlay.transform, "일시정지\n<size=18>Space: 계속 · 멈춘 동안 명령하고 아래 분대 명령(1~4)을 내릴 수 있습니다</size>", 40, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -40f);
            labelRect.sizeDelta = new Vector2(900f, 110f);
            BuildSquadOrderBar(_pauseOverlay.transform);
            _pauseOverlay.SetActive(false);
        }

        // ---------- 분대 명령(일시정지 중) ----------
        // 멈춘 화면 아래쪽 버튼 4개(단축키 1~4). 조종 중인 분대장의 분대에만 먹히고, 명령마다 쿨타임이 있다(FieldBattleSession).

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
                rect.anchoredPosition = new Vector2(left + i * (width + gap), 28f);
                rect.sizeDelta = new Vector2(width, height);
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
            if (!_paused || !_session.SquadOrderReady(order)) return;
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
                var name = CreateText(root, label, UITheme.SmallSize, TextAnchor.MiddleCenter,
                    unit.IsLeader ? LeaderColor : unit.IsPlayerSide ? UITheme.BodyText : EnemyNameColor(unit.Combatant.Name));
                name.font = UITheme.Font;
                name.supportRichText = true;
                name.raycastTarget = false;
                name.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
                PlaceAbove(name.rectTransform, 154f + headLift, 160f, 20f);
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
                hpText = CreateText(hpBg.transform, "", boss ? 16 : 11, TextAnchor.MiddleCenter, UITheme.BodyText);
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
            // 전리품 줄(승리 때만): 한 줄에 LootPerLine칸씩, 줄 수만큼 구분선·대원 목록을 아래로 민다
            var loot = victory ? report.Loot : new List<(LootItem item, int count)>();
            int lootLines = (loot.Count + LootPerLine - 1) / LootPerLine;
            float lootExtra = lootLines > 0 ? lootLines * LootLineHeight + 4f : 0f;
            float membersTop = -196f - lootExtra;
            float height = -membersTop + ResultRowHeight * rows + 124f;
            var panel = CreateImage(overlay.transform, "ResultPanel", Color.white);
            panel.AddComponent<CanvasGroup>();
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(ResultWidth, height);
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

            // 전리품: 아이콘 ×개수(마우스를 올리면 이름·팔 때 값). 길드 창고에서 판다.
            for (int line = 0; line < lootLines; line++)
            {
                var lootRow = new GameObject("Loot", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                lootRow.transform.SetParent(panel.transform, false);
                PlaceTop((RectTransform)lootRow.transform, -172f - line * LootLineHeight, LootLineHeight, ResultPad);
                var lootLayout = lootRow.GetComponent<HorizontalLayoutGroup>();
                lootLayout.childAlignment = TextAnchor.MiddleCenter;
                lootLayout.spacing = 6f;
                lootLayout.childControlWidth = lootLayout.childControlHeight = true;
                lootLayout.childForceExpandWidth = lootLayout.childForceExpandHeight = false;
                foreach (var (item, count) in loot.Skip(line * LootPerLine).Take(LootPerLine))
                    AddLootChip(lootRow.transform, item, count);
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

        private const int LootPerLine = 6;
        private const float LootLineHeight = 44f;
        private const float LootIconSize = 38f;
        private static readonly Color LootRareColor = new Color(0.78f, 0.62f, 1f);

        /// <summary>전리품 한 칸: 아이콘 + "×n"(드문 것은 보랏빛). 마우스를 올리면 이름·팔 때 값.</summary>
        private void AddLootChip(Transform parent, LootItem item, int count)
        {
            var chip = new GameObject("LootChip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            chip.transform.SetParent(parent, false);
            chip.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // 툴팁용 레이캐스트(보이지 않음)
            var chipLayout = chip.GetComponent<HorizontalLayoutGroup>();
            chipLayout.childAlignment = TextAnchor.MiddleLeft;
            chipLayout.spacing = 2f;
            chipLayout.padding = new RectOffset(4, 8, 0, 0);
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = chipLayout.childForceExpandHeight = false;

            var iconGO = CreateImage(chip.transform, "Icon", Color.white);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = item.Icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconLayout = iconGO.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = iconLayout.preferredHeight = LootIconSize;

            var text = CreateText(chip.transform, $"×{count}", 18, TextAnchor.MiddleLeft, item.Rare ? LootRareColor : UITheme.BodyText);
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.gameObject.AddComponent<LayoutElement>().minWidth = 30f;

            chip.AddComponent<TooltipTrigger>().Text = $"<b>{item.Name}</b>" + (item.Rare ? " (드묾)" : "") + $"\n팔면 개당 {item.Price}G · 길드 창고에서 판다";
        }

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
            nameRect.sizeDelta = new Vector2(150f, 26f);

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
