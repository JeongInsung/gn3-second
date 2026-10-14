using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GN3.CharacterAnim;
using GN3.Combat;
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
            public Color[] BaseColors;
            public GameObject Bars;         // 머리 위 체력·공격 대기 막대(쓰러지면 숨김)
            public RectTransform HpFill;    // 오른쪽 끝(anchorMax.x) = 체력 비율
            public Image HpFillImage;
            public RectTransform HpTrail;   // 깎인 만큼 잠깐 남았다 줄어드는 밝은 막대
            public Text HpText;             // 아군만 "45/60"
            public RectTransform ReadyFill;
            public float TrailRatio = 1f;
            public float TrailHold;
            public Image TargetRing;        // 분대장이 노리는 적
            public Image ClickArea;
            public BattlePortraitAnimator Animator; // 용병만(파츠 조합 캐릭터)
            public float HopPhase;
        }

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private Mercenary _leaderMerc; // 결과 화면 ★ 표시(전투 중 쓰러져도 그대로)
        private FieldBattleSession _session;
        private readonly Dictionary<FieldUnit, UnitView> _views = new Dictionary<FieldUnit, UnitView>();
        private readonly List<UnitView> _drawOrder = new List<UnitView>();
        private bool _picking;      // 스킬 대상 고르는 중
        private bool _resultShown;
        private bool _leaderDownNoticed;
        private bool _paused;       // Space: 전투 멈춤(멈춘 동안에도 분대장 명령은 내릴 수 있다)
        private bool _deploying;    // 배치 단계(전투 시작 전)
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
        private const float ProjectileSpeed = 1400f;
        private const string MusicPath = "Music/전투 브금 1";
        private const float MusicVolume = 0.6f;
        /// <summary>전투씬 전체 속도(Time.timeScale). 이동·공격·쿨타임·애니메이션·연출이 같은 비율로 느려져 밸런스는 그대로.</summary>
        private const float BattleSpeed = 0.7f;

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
            _leaderMerc = expedition.Leader;

            var rng = new System.Random();
            var waves = Enumerable.Range(0, quest.WaveCount).Select(w => ExpeditionBattle.SpawnWave(quest, w, rng)).ToList();

            _session = new FieldBattleSession(combatants, melee, skills, leaderIndex, waves, rng);
            _session.OnWaveCleared += OnWaveCleared;
            _session.OnWaveStarted += OnWaveStarted;
            _session.OnSkillUsed += (unit, skill) =>
            {
                if (_views.TryGetValue(unit, out var v)) StartCoroutine(FloatText(v, skill.Name, new Color(0.6f, 0.85f, 1f), 175f));
                AppendLog($"<color=#9fd0ff>{unit.Combatant.Name} — {skill.Name}</color>");
            };

            BuildUI(quest);
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
            AppendLog($"<color=#ffd36b>{wave + 1}라운드 시작 — {(w.IsBoss ? "보스 " : "")}{w.EnemyName} ×{w.Count}</color>");
            StartCoroutine(Banner(last ? $"마지막 {wave + 1}라운드" : $"{wave + 1}라운드", $"{(w.IsBoss ? "보스 " : "")}{w.EnemyName} ×{w.Count}",
                w.IsBoss ? new Color(1f, 0.45f, 0.4f) : Color.white));
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
            if (_deployZone != null) Destroy(_deployZone);
            _resetButton.gameObject.SetActive(false);

            var leader = _session.Leader;
            AppendLog(leader != null
                ? $"전투 시작! 분대장 {leader.Combatant.Name} 지휘 — 땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬"
                : "전투 시작!");
            RefreshPanel();
        }

        /// <summary>배치 중 마우스를 올린(또는 끌고 있는) 아군에게만 마을 건물처럼 흰 테두리가 반짝인다.</summary>
        private void UpdateDeployOutlines()
        {
            foreach (var v in _views.Values)
                v.Animator?.SetOutline(_deploying && (v.Unit == _hoverUnit || v.Unit == _dragUnit));
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
            if (!_deploying || !TryFieldPoint(e, out var local)) return;
            _dragUnit = unit;
            _dragStart = unit.Position;
            _dragOffset = new Vector2(unit.Position.X, unit.Position.Y) - local;
        }

        private void OnDragMove(FieldUnit unit, PointerEventData e)
        {
            if (!_deploying || _dragUnit != unit || !TryFieldPoint(e, out var local)) return;
            var p = local + _dragOffset;
            _session.PlaceForDeploy(unit, new System.Numerics.Vector2(p.x, p.y));
        }

        /// <summary>놓은 자리에 다른 대원이 있으면 둘의 자리를 바꾼다(겹쳐 서지 않게).</summary>
        private void OnDragEnd(FieldUnit unit, PointerEventData e)
        {
            if (!_deploying || _dragUnit != unit) return;
            _dragUnit = null;
            const float swapRadius = 60f;
            var other = _session.Units
                .Where(o => o != unit && o.IsPlayerSide && System.Numerics.Vector2.Distance(o.Position, unit.Position) < swapRadius)
                .OrderBy(o => System.Numerics.Vector2.Distance(o.Position, unit.Position))
                .FirstOrDefault();
            if (other != null) _session.PlaceForDeploy(other, _dragStart);
        }

        /// <summary>필드 왼쪽의 밝은 배치 구역(오른쪽 경계선·글자). 클릭은 막지 않는다.</summary>
        private void BuildDeployZone()
        {
            _deployZone = CreateImage(_groundLayer, "DeployZone", new Color(0.55f, 0.9f, 0.55f, 0.12f));
            var rect = (RectTransform)_deployZone.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(FieldBattleSession.DeployMaxX + 40f, 0f);
            _deployZone.GetComponent<Image>().raycastTarget = false;

            var edge = CreateImage(_deployZone.transform, "Edge", new Color(0.6f, 1f, 0.6f, 0.6f));
            var edgeRect = (RectTransform)edge.transform;
            edgeRect.anchorMin = new Vector2(1f, 0f);
            edgeRect.anchorMax = Vector2.one;
            edgeRect.pivot = new Vector2(1f, 0.5f);
            edgeRect.offsetMin = new Vector2(-3f, 0f);
            edgeRect.offsetMax = Vector2.zero;
            edge.GetComponent<Image>().raycastTarget = false;

            var label = CreateText(_deployZone.transform, "배치 구역", 18, TextAnchor.UpperCenter, new Color(0.75f, 1f, 0.75f, 0.85f));
            label.raycastTarget = false;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(0f, -34f);
            labelRect.offsetMax = new Vector2(0f, -8f);
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
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = MusicVolume;
            source.playOnAwake = false;
            source.Play();
        }

        /// <summary>전사·암살자처럼 몸으로 부딪히는 직업만 근접, 나머지는 원거리.</summary>
        private static bool IsMeleeClass(MercenaryClassKind kind) =>
            kind == MercenaryClassKind.Warrior || kind == MercenaryClassKind.Assassin;

        // ---------- 진행 ----------

        private void Update()
        {
            if (_session == null || _resultShown) return;

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

            HandleKeys();
            var events = _session.Tick(Time.deltaTime);
            if (events.Count > 0) ApplyEventsVisual(events);

            foreach (var v in _views.Values) UpdateUnitView(v);
            SortByDepth();

            if (_session.IsOver)
            {
                ShowResult();
                return;
            }
            RefreshPanel();
        }

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.qKey.wasPressedThisFrame) PressSkill();
            if (kb.escapeKey.wasPressedThisFrame) _picking = false;
            if (kb.spaceKey.wasPressedThisFrame) SetPaused(!_paused);
        }

        // ---------- 입력 ----------

        /// <summary>Time.timeScale로 전투(세션 Tick·애니메이션·연출)를 통째로 멈춘다. 배경음은 계속.</summary>
        private void SetPaused(bool paused)
        {
            _paused = paused && !_resultShown;
            Time.timeScale = _paused ? 0f : BattleSpeed;
            _pauseOverlay.SetActive(_paused);
        }

        private void OnDestroy() => Time.timeScale = 1f; // 멈춘 채 씬을 떠나도 마을 시간이 멈춰 있지 않게

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
            _leaderLabel.text = $"★ 분대장 {c.Name}   체력 {c.CurrentHealth}/{c.Stats.MaxHealth}";

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

            _hintLabel.text = _picking
                ? (skill != null && skill.TargetType == SkillTargetType.Ally
                    ? "치료할 아군을 클릭 (Q: 자신 · Esc: 취소)"
                    : "스킬을 쓸 적을 클릭 (Esc: 취소)")
                : "땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬 · Space: 일시정지";
        }

        private void UpdateUnitView(UnitView v)
        {
            var u = v.Unit;
            bool alive = u.IsAlive;
            v.Root.anchoredPosition = new Vector2(u.Position.X, u.Position.Y);

            v.Group.alpha = alive ? 1f : 0.3f;
            v.ClickArea.raycastTarget = alive;
            UpdateBars(v, alive);

            var leader = _session.Leader;
            bool targeted = alive && leader != null && leader.IsAlive && leader.Target == u;
            v.TargetRing.enabled = targeted || (_picking && alive && IsPickable(u));
            v.TargetRing.color = targeted && !_picking ? new Color(1f, 0.35f, 0.3f, 0.9f) : new Color(PickingColor.r, PickingColor.g, PickingColor.b, 0.85f);

            // 바라보는 쪽(그림은 오른쪽을 본다). 죽으면 옆으로 눕힌다.
            v.Body.localScale = new Vector3(u.Facing, 1f, 1f);
            v.Body.localRotation = alive ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f * u.Facing);

            if (v.Animator != null)
            {
                if (alive && u.IsMoving) v.Animator.PlayWalk(); else v.Animator.PlayIdle();
                v.Body.anchoredPosition = Vector2.zero;
            }
            else
            {
                // 몬스터(도형)는 걸을 때 통통 튄다
                float hop = alive && u.IsMoving ? Mathf.Abs(Mathf.Sin(Time.time * 10f + v.HopPhase)) * 8f : 0f;
                v.Body.anchoredPosition = new Vector2(0f, hop);
            }
        }

        private static readonly Color HpHigh = new Color(0.35f, 0.85f, 0.3f);
        private static readonly Color HpMid = new Color(0.95f, 0.8f, 0.2f);
        private static readonly Color HpLow = new Color(0.95f, 0.25f, 0.2f);
        private static readonly Color EnemyHp = new Color(0.9f, 0.22f, 0.18f);
        private const float TrailHoldSeconds = 0.4f;
        private const float TrailDrainPerSecond = 0.8f;

        /// <summary>체력 막대: 길이 = 비율, 아군은 초록 → 노랑 → 빨강, 적은 빨강. 깎인 부분은 밝은 막대로 잠깐 남았다 줄고,
        /// 30% 아래면 깜빡여 눈에 띄게 한다.</summary>
        private void UpdateBars(UnitView v, bool alive)
        {
            v.Bars.SetActive(alive);
            if (!alive) return;

            var c = v.Unit.Combatant;
            float ratio = Mathf.Clamp01((float)c.CurrentHealth / Math.Max(1, c.Stats.MaxHealth));
            SetLength(v.HpFill, ratio);

            if (ratio >= v.TrailRatio) { v.TrailRatio = ratio; v.TrailHold = 0f; } // 회복은 바로
            else if (v.TrailHold < TrailHoldSeconds) v.TrailHold += Time.deltaTime;
            else v.TrailRatio = Mathf.Max(ratio, v.TrailRatio - TrailDrainPerSecond * Time.deltaTime);
            SetLength(v.HpTrail, v.TrailRatio);
            if (v.TrailRatio <= ratio) v.TrailHold = 0f;

            Color color = !v.Unit.IsPlayerSide ? EnemyHp
                : ratio > 0.5f ? Color.Lerp(HpMid, HpHigh, (ratio - 0.5f) * 2f)
                : Color.Lerp(HpLow, HpMid, Mathf.Clamp01((ratio - 0.15f) / 0.35f));
            if (ratio < 0.3f) color = Color.Lerp(color, Color.white, (Mathf.Sin(Time.unscaledTime * 10f) + 1f) * 0.2f);
            v.HpFillImage.color = color;

            if (v.HpText != null) v.HpText.text = $"{c.CurrentHealth}/{c.Stats.MaxHealth}";
            SetLength(v.ReadyFill, v.Unit.AttackReadyRatio);
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
                var attacker = _session.UnitOf(evt.Attacker);
                var target = _session.UnitOf(evt.Target);
                if (attacker == null || target == null) continue;
                var attackerView = _views[attacker];
                var targetView = _views[target];

                bool heal = evt.Damage < 0;
                if (!heal) attackerView.Animator?.PlaySlash();

                string label = heal ? $"+{-evt.Damage}" : evt.Evaded ? "회피" : $"-{evt.Damage}";
                Color color = heal ? new Color(0.45f, 0.9f, 0.5f) : evt.Evaded ? Color.white : new Color(1f, 0.45f, 0.4f);

                if (!attacker.IsMelee && attacker != target && !heal)
                    StartCoroutine(Projectile(attackerView, targetView, label, color, !evt.Evaded && evt.Damage > 0));
                else
                {
                    StartCoroutine(FloatText(targetView, label, color, 150f));
                    if (!heal && !evt.Evaded && evt.Damage > 0) StartCoroutine(HitFlash(targetView));
                }
            }
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
            };
            var report = ExpeditionBattle.ConcludeReport(_expedition, battle, alertShown: true); // 이 씬의 결과 화면이 보여 준다

            _skillButton.interactable = false;
            BuildResultOverlay(report);
        }

        // ---------- 연출 ----------

        private IEnumerator Projectile(UnitView from, UnitView to, string label, Color color, bool hit)
        {
            var go = new GameObject("Projectile", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_fxLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(14f, 14f);
            var img = go.GetComponent<Image>();
            img.sprite = BattlePlaceholderSprites.Circle;
            img.color = new Color(1f, 0.9f, 0.6f);
            img.raycastTarget = false;

            Vector2 start = from.Root.anchoredPosition + new Vector2(0f, 60f);
            Vector2 end = to.Root.anchoredPosition + new Vector2(0f, 60f);
            float duration = Mathf.Max(0.08f, Vector2.Distance(start, end) / ProjectileSpeed);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                end = to.Root.anchoredPosition + new Vector2(0f, 60f); // 움직이는 대상을 따라간다
                rect.anchoredPosition = Vector2.Lerp(start, end, t / duration);
                yield return null;
            }
            Destroy(go);
            StartCoroutine(FloatText(to, label, color, 150f));
            if (hit) StartCoroutine(HitFlash(to));
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

        private IEnumerator HitFlash(UnitView v)
        {
            const float duration = 0.3f;
            var hit = new Color(1f, 0.35f, 0.3f);
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
            _groundLayer = CreateLayer("GroundLayer");
            _unitLayer = CreateLayer("UnitLayer");
            _fxLayer = CreateLayer("FxLayer");

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

            var label = CreateText(_pauseOverlay.transform, "일시정지\n<size=18>Space: 계속 · 멈춘 동안에도 명령할 수 있습니다</size>", 40, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -40f);
            labelRect.sizeDelta = new Vector2(900f, 110f);
            _pauseOverlay.SetActive(false);
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
            Image[] tintables;
            if (appearance != null)
            {
                var portrait = BattlePortraitAnimator.Create(body, appearance, (int)PortraitSize, clip: false); // 검격이 칸에 잘리지 않게
                var pr = (RectTransform)portrait.transform;
                pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
                pr.sizeDelta = new Vector2(PortraitSize, PortraitSize);
                pr.anchoredPosition = new Vector2(0f, -6f);
                animator = portrait.GetComponent<BattlePortraitAnimator>();
                tintables = portrait.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != portrait).ToArray();
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

            // 머리 위: 이름 · 체력 · 공격 대기
            string label = unit.IsLeader ? $"★{unit.Combatant.Name}" : unit.Combatant.Name;
            var name = CreateText(root, label, 14, TextAnchor.MiddleCenter, unit.IsLeader ? LeaderColor : unit.IsPlayerSide ? UITheme.BodyText : new Color(1f, 0.75f, 0.7f));
            name.raycastTarget = false;
            name.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            PlaceAbove(name.rectTransform, 154f, 160f, 20f);

            // 체력 막대: 테두리(분대장은 금색) 안에 어두운 바탕 → 깎인 자국 → 체력, 위쪽에 얇은 광택. 보스는 더 넓게.
            float barWidth = tier >= 2 ? 130f : 104f;
            var bars = new GameObject("Bars", typeof(RectTransform));
            bars.transform.SetParent(root, false);
            PlaceAbove((RectTransform)bars.transform, 124f, barWidth, 22f);

            var frame = CreateImage(bars.transform, "HpFrame", unit.IsLeader ? LeaderColor : new Color(0f, 0f, 0f, 0.95f));
            PlaceBar((RectTransform)frame.transform, 6f, 16f, 0f);
            var hpBg = CreateImage(frame.transform, "HpBg", new Color(0.18f, 0.06f, 0.06f, 1f));
            Inset((RectTransform)hpBg.transform, 2f);
            var trail = CreateBarFill(hpBg.transform, new Color(1f, 0.92f, 0.6f, 0.9f));
            var hpFill = CreateBarFill(hpBg.transform, HpHigh);
            var gloss = CreateImage(hpFill.transform, "Gloss", new Color(1f, 1f, 1f, 0.28f));
            var glossRect = (RectTransform)gloss.transform;
            glossRect.anchorMin = new Vector2(0f, 0.55f);
            glossRect.anchorMax = Vector2.one;
            glossRect.offsetMin = glossRect.offsetMax = Vector2.zero;
            gloss.GetComponent<Image>().raycastTarget = false;

            Text hpText = null;
            if (unit.IsPlayerSide)
            {
                hpText = CreateText(frame.transform, "", 11, TextAnchor.MiddleCenter, Color.white);
                hpText.fontStyle = FontStyle.Bold;
                hpText.raycastTarget = false;
                hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
                hpText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                Stretch(hpText.rectTransform);
            }

            // 공격 대기: 체력 막대 바로 아래 얇은 하늘색 줄
            var readyBg = CreateImage(bars.transform, "ReadyBg", new Color(0f, 0f, 0f, 0.85f));
            PlaceBar((RectTransform)readyBg.transform, 0f, 6f, 2f);
            var readyInner = CreateImage(readyBg.transform, "ReadyInner", new Color(0.08f, 0.1f, 0.14f, 1f));
            Inset((RectTransform)readyInner.transform, 1f);
            var readyFill = CreateBarFill(readyInner.transform, new Color(0.4f, 0.8f, 1f));

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
                HpFill = (RectTransform)hpFill.transform,
                HpFillImage = hpFill,
                HpTrail = (RectTransform)trail.transform,
                HpText = hpText,
                ReadyFill = (RectTransform)readyFill.transform,
                TargetRing = targetRing,
                ClickArea = clickImage,
                Animator = animator,
                HopPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            };
            UpdateUnitView(view);
            return view;
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

        private const float ResultWidth = 800f;
        private const float ResultRowHeight = 58f;
        private static readonly Color VictoryColor = new Color(1f, 0.82f, 0.35f);
        private static readonly Color DefeatColor = new Color(0.92f, 0.4f, 0.35f);
        private static readonly Color GainColor = new Color(0.55f, 0.9f, 0.5f);
        private static readonly Color RepColor = new Color(0.6f, 0.8f, 1f);

        /// <summary>
        /// 결과 화면: 승리/패배 머리띠(퀘스트·전투 시간) → 보상 카드 두 장(골드는 0부터 올라가며 셈, 명성) →
        /// 대원 줄(초상화·이름·체력 변화 막대·오른 능력치 또는 전사) → 귀환 안내와 '마을로 돌아가기'.
        /// </summary>
        private void BuildResultOverlay(ExpeditionBattle.Report report)
        {
            bool victory = report.Victory;
            Color accent = victory ? VictoryColor : DefeatColor;

            var overlay = CreateImage(_canvas.transform, "ResultOverlay", new Color(0f, 0f, 0f, 0.72f));
            Stretch((RectTransform)overlay.transform);
            overlay.transform.SetAsLastSibling();

            int rows = report.Members.Count;
            float height = 150f + 124f + 34f + ResultRowHeight * rows + 124f;
            var panel = CreateImage(overlay.transform, "ResultPanel", Color.white);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(ResultWidth, height);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;

            // 머리띠
            var band = CreateImage(panel.transform, "Band", new Color(accent.r, accent.g, accent.b, 0.16f));
            PlaceTop((RectTransform)band.transform, -18f, 116f, 18f);
            var bandEdge = CreateImage(band.transform, "Edge", new Color(accent.r, accent.g, accent.b, 0.7f));
            var edgeRect = (RectTransform)bandEdge.transform;
            edgeRect.anchorMin = Vector2.zero;
            edgeRect.anchorMax = new Vector2(1f, 0f);
            edgeRect.pivot = new Vector2(0.5f, 0f);
            edgeRect.offsetMin = Vector2.zero;
            edgeRect.offsetMax = new Vector2(0f, 3f);

            var title = CreateText(band.transform, victory ? "승리!" : "패배...", 52, TextAnchor.MiddleCenter, accent);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            PlaceTop(title.rectTransform, -6f, 66f, 0f);

            int secs = Mathf.FloorToInt(_session.Elapsed);
            string waveLine = report.WaveCount <= 1 ? ""
                : victory ? $"    ·    {report.WaveCount}라운드 모두 돌파"
                : $"    ·    {report.WavesCleared + 1}/{report.WaveCount}라운드에서 패배";
            var subtitle = CreateText(band.transform, $"{QuestDifficulty.RichTitle(_expedition.Quest)}    ·    전투 시간 {secs / 60}:{secs % 60:00}{waveLine}",
                18, TextAnchor.MiddleCenter, UITheme.BodyText);
            subtitle.supportRichText = true;
            PlaceTop(subtitle.rectTransform, -74f, 30f, 0f);

            // 보상 카드
            const float cardY = -152f;
            var goldValue = CreateRewardCard(panel.transform, -182f, cardY, victory ? "보상" : "보상 없음",
                victory ? "+0 G" : "0 G", victory ? VictoryColor : UITheme.MutedText, UIIcons.Load("UIIcons/gold"), null);
            CreateRewardCard(panel.transform, 182f, cardY, "명성",
                report.Reputation >= 0 ? $"+{report.Reputation}" : report.Reputation.ToString(),
                report.Reputation >= 0 ? RepColor : DefeatColor, null, "★");

            // 대원
            float y = cardY - 124f;
            var section = CreateText(panel.transform, "대원", 16, TextAnchor.MiddleLeft, UITheme.MutedText);
            section.fontStyle = FontStyle.Bold;
            PlaceTop(section.rectTransform, y, 26f, 40f);
            y -= 34f;
            for (int i = 0; i < rows; i++)
            {
                CreateResultRow(panel.transform, report.Members[i], y, i % 2 == 0);
                y -= ResultRowHeight;
            }

            // 귀환 안내 + 버튼
            int survivors = report.Members.Count(m => !m.Fallen);
            string footer = survivors > 0
                ? $"생존 {survivors}명 귀환길에 오릅니다 · 마을까지 {GN3.World.GameClock.FormatDuration(report.ReturnHours)}"
                : "전멸 — 돌아올 대원이 없습니다";
            var footerText = CreateText(panel.transform, footer, 16, TextAnchor.MiddleCenter, survivors > 0 ? UITheme.BodyText : DefeatColor);
            var footerRect = footerText.rectTransform;
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.offsetMin = new Vector2(30f, 90f);
            footerRect.offsetMax = new Vector2(-30f, 116f);

            var backButton = CreateActionButton(panel.transform, Vector2.zero, out var backLabel);
            backLabel.text = "마을로 돌아가기";
            backLabel.fontStyle = FontStyle.Bold;
            var backRect = (RectTransform)backButton.transform;
            backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 24f);
            backRect.sizeDelta = new Vector2(280f, 56f);
            backButton.onClick.AddListener(() => SceneManager.LoadScene("MainScene"));

            StartCoroutine(PopIn(panelRect));
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

        /// <summary>보상 카드 한 장(아이콘 또는 큰 기호 + 작은 설명 + 큰 값). 값 글자를 돌려준다(셈 연출용).</summary>
        private Text CreateRewardCard(Transform parent, float x, float y, string caption, string value, Color valueColor, Sprite icon, string glyph)
        {
            var card = CreateImage(parent, "RewardCard", Color.white);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(340f, 104f);
            var image = card.GetComponent<Image>();
            image.sprite = UITheme.Inset;
            image.type = Image.Type.Sliced;

            RectTransform iconRect;
            if (icon != null)
            {
                var iconGO = CreateImage(card.transform, "Icon", Color.white);
                var iconImage = iconGO.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconRect = (RectTransform)iconGO.transform;
            }
            else
            {
                var glyphText = CreateText(card.transform, glyph ?? "", 44, TextAnchor.MiddleCenter, valueColor);
                glyphText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                iconRect = glyphText.rectTransform;
            }
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(24f, 0f);
            iconRect.sizeDelta = new Vector2(56f, 56f);

            var captionText = CreateText(card.transform, caption, 15, TextAnchor.UpperLeft, UITheme.MutedText);
            var captionRect = captionText.rectTransform;
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(0f, 1f);
            captionRect.pivot = new Vector2(0f, 1f);
            captionRect.anchoredPosition = new Vector2(100f, -16f);
            captionRect.sizeDelta = new Vector2(220f, 22f);

            var valueText = CreateText(card.transform, value, 36, TextAnchor.UpperLeft, valueColor);
            valueText.fontStyle = FontStyle.Bold;
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            valueText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            var valueRect = valueText.rectTransform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(0f, 1f);
            valueRect.pivot = new Vector2(0f, 1f);
            valueRect.anchoredPosition = new Vector2(100f, -40f);
            valueRect.sizeDelta = new Vector2(220f, 46f);
            return valueText;
        }

        /// <summary>대원 한 줄: 초상화 · 이름(★ 분대장) · 체력 막대(이번 전투에서 잃은 만큼 옅은 빨강) · 성장 또는 전사.</summary>
        private void CreateResultRow(Transform parent, ExpeditionBattle.MemberReport m, float y, bool shaded)
        {
            var row = CreateImage(parent, m.Merc.Name, new Color(1f, 1f, 1f, shaded ? 0.05f : 0f));
            PlaceTop((RectTransform)row.transform, y, ResultRowHeight - 4f, 30f);
            row.GetComponent<Image>().raycastTarget = false;

            var portrait = CharacterPortraitUI.Create(row.transform, m.Merc.Appearance, 44);
            var pr = (RectTransform)portrait.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0f, 0.5f);
            pr.pivot = new Vector2(0f, 0.5f);
            pr.anchoredPosition = new Vector2(8f, 0f);
            pr.sizeDelta = new Vector2(44f, 44f);
            if (m.Fallen)
                foreach (var img in portrait.GetComponentsInChildren<Image>()) img.color = new Color(0.45f, 0.45f, 0.45f, img.color.a);

            bool leader = m.Merc == _leaderMerc;
            var name = CreateText(row.transform, leader ? $"★ {m.Merc.Name}" : m.Merc.Name, 18, TextAnchor.MiddleLeft,
                m.Fallen ? new Color(0.6f, 0.55f, 0.55f) : leader ? LeaderColor : UITheme.BodyText);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var nameRect = name.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 0.5f);
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.anchoredPosition = new Vector2(64f, 0f);
            nameRect.sizeDelta = new Vector2(160f, 30f);

            // 체력 막대: 남은 체력(색) + 이번 전투에서 잃은 부분(옅은 빨강)
            float max = Math.Max(1, m.MaxHealth);
            float before = Mathf.Clamp01(m.HealthBefore / max), after = Mathf.Clamp01(m.HealthAfter / max);
            var frame = CreateImage(row.transform, "HpFrame", new Color(0f, 0f, 0f, 0.95f));
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0.5f);
            frameRect.pivot = new Vector2(0f, 0.5f);
            frameRect.anchoredPosition = new Vector2(236f, 8f);
            frameRect.sizeDelta = new Vector2(180f, 14f);
            var bg = CreateImage(frame.transform, "Bg", new Color(0.18f, 0.06f, 0.06f, 1f));
            Inset((RectTransform)bg.transform, 2f);
            var lost = CreateBarFill(bg.transform, new Color(1f, 0.45f, 0.4f, 0.55f));
            SetLength((RectTransform)lost.transform, before);
            var hp = CreateBarFill(bg.transform, after > 0.5f ? HpHigh : after > 0.25f ? HpMid : HpLow);
            SetLength((RectTransform)hp.transform, after);

            string hpLine = m.Fallen ? $"{m.HealthBefore} → 0"
                : m.HealthAfter < m.HealthBefore ? $"{m.HealthBefore} → {m.HealthAfter} / {m.MaxHealth}"
                : $"{m.HealthAfter} / {m.MaxHealth}";
            var hpText = CreateText(row.transform, hpLine, 13, TextAnchor.MiddleLeft, UITheme.MutedText);
            var hpRect = hpText.rectTransform;
            hpRect.anchorMin = hpRect.anchorMax = new Vector2(0f, 0.5f);
            hpRect.pivot = new Vector2(0f, 0.5f);
            hpRect.anchoredPosition = new Vector2(236f, -12f);
            hpRect.sizeDelta = new Vector2(180f, 20f);

            string result = m.Fallen ? "전사"
                : m.Gains.Count > 0 ? "▲ " + string.Join(", ", m.Gains)
                : "성장 없음";
            var resultText = CreateText(row.transform, result, m.Fallen ? 20 : 16, TextAnchor.MiddleLeft,
                m.Fallen ? DefeatColor : m.Gains.Count > 0 ? GainColor : UITheme.MutedText);
            if (m.Fallen || m.Gains.Count > 0) resultText.fontStyle = FontStyle.Bold;
            var resultRect = resultText.rectTransform;
            resultRect.anchorMin = new Vector2(0f, 0f);
            resultRect.anchorMax = new Vector2(1f, 1f);
            resultRect.offsetMin = new Vector2(440f, 0f);
            resultRect.offsetMax = new Vector2(-8f, 0f);
        }

        /// <summary>결과 창이 살짝 커지며 나타난다.</summary>
        private IEnumerator PopIn(RectTransform panel)
        {
            const float duration = 0.25f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                panel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, k);
                yield return null;
            }
            panel.localScale = Vector3.one;
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
