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
            public Image HpFill;
            public Image ReadyFill;
            public Image TargetRing;        // 분대장이 노리는 적
            public Image ClickArea;
            public BattlePortraitAnimator Animator; // 용병만(파츠 조합 캐릭터)
            public float HopPhase;
        }

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private FieldBattleSession _session;
        private readonly Dictionary<FieldUnit, UnitView> _views = new Dictionary<FieldUnit, UnitView>();
        private readonly List<UnitView> _drawOrder = new List<UnitView>();
        private bool _picking;      // 스킬 대상 고르는 중
        private bool _resultShown;
        private bool _leaderDownNoticed;

        private Canvas _canvas;
        private RectTransform _field;
        // 필드 안의 층: 땅 표시(이동 지점) → 유닛(앞뒤 정렬) → 효과(투사체·숫자). 모두 필드와 같은 좌표.
        private RectTransform _groundLayer;
        private RectTransform _unitLayer;
        private RectTransform _fxLayer;
        private Text _leaderLabel;
        private Text _hintLabel;
        private Text _timerLabel;
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

            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(quest.Region, quest.EnemyCount, quest.Difficulty, rng);
            int tier = Math.Clamp(quest.Difficulty - 1, 0, 2);

            _session = new FieldBattleSession(combatants, melee, skills, leaderIndex, enemies, rng);
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
                    : CreateUnitView(unit, null, tier);
                _views[unit] = view;
                _drawOrder.Add(view);
            }

            PlayMusic();

            var leader = _session.Leader;
            AppendLog(leader != null
                ? $"분대장 {leader.Combatant.Name} 지휘 시작 — 땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬"
                : "전투 시작");
            RefreshPanel();
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
        }

        // ---------- 입력 ----------

        /// <summary>스킬 버튼·Q: 공격 스킬은 지금 노리는 적이 있으면 바로 그 적에게, 없으면 대상 고르기.
        /// 회복 스킬은 대상 고르기(고르는 중에 한 번 더 누르면 자기 자신).</summary>
        private void PressSkill()
        {
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
            _timerLabel.text = $"{secs / 60}:{secs % 60:00}";

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
                : "땅 클릭: 이동 · 적 클릭: 공격 · Q: 스킬";
        }

        private void UpdateUnitView(UnitView v)
        {
            var u = v.Unit;
            bool alive = u.IsAlive;
            v.Root.anchoredPosition = new Vector2(u.Position.X, u.Position.Y);

            v.Group.alpha = alive ? 1f : 0.3f;
            v.ClickArea.raycastTarget = alive;
            float ratio = alive ? (float)u.Combatant.CurrentHealth / Math.Max(1, u.Combatant.Stats.MaxHealth) : 0f;
            v.HpFill.fillAmount = Mathf.Clamp01(ratio);
            v.HpFill.color = !u.IsPlayerSide ? new Color(0.8f, 0.3f, 0.25f)
                : ratio > 0.5f ? new Color(0.4f, 0.75f, 0.35f) : ratio > 0.25f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.75f, 0.25f, 0.2f);
            v.ReadyFill.fillAmount = alive ? u.AttackReadyRatio : 0f;

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
            _picking = false;

            var result = new BattleResult
            {
                Outcome = _session.Outcome,
                Rounds = _session.ActionCount,
                Log = _session.Log,
                TeamA = _session.PlayerTeam,
                TeamB = _session.EnemyTeam,
            };
            var battle = new ExpeditionBattle.Battle { Result = result, Fighters = _fighters, Combatants = _session.PlayerTeam };
            string summary = ExpeditionBattle.Conclude(_expedition, battle, alertShown: true); // 이 씬의 결과 화면이 보여 준다

            _skillButton.interactable = false;
            bool victory = _session.Outcome == BattleOutcome.TeamAVictory;
            BuildResultOverlay(victory, summary);
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

            _timerLabel = CreateText(root, "0:00", 22, TextAnchor.MiddleRight, UITheme.MutedText);
            var timerRect = _timerLabel.rectTransform;
            timerRect.anchorMin = timerRect.anchorMax = timerRect.pivot = new Vector2(1f, 1f);
            timerRect.anchoredPosition = new Vector2(-40f, -24f);
            timerRect.sizeDelta = new Vector2(160f, 40f);

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
                var portrait = BattlePortraitAnimator.Create(body, appearance, (int)PortraitSize);
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
            PlaceAbove(name.rectTransform, 150f, 150f, 20f);

            var hpBg = CreateImage(root, "HpBg", new Color(0.05f, 0.05f, 0.06f, 0.9f));
            PlaceAbove((RectTransform)hpBg.transform, 132f, 80f, 9f);
            var hpFill = CreateFill(hpBg.transform, new Color(0.4f, 0.75f, 0.35f));

            var readyBg = CreateImage(root, "ReadyBg", new Color(0.05f, 0.05f, 0.06f, 0.9f));
            PlaceAbove((RectTransform)readyBg.transform, 124f, 80f, 4f);
            var readyFill = CreateFill(readyBg.transform, new Color(0.35f, 0.75f, 0.9f));

            // 클릭 영역(그림 크기만큼, 투명)
            var clickGO = CreateImage(root, "Click", new Color(0f, 0f, 0f, 0f));
            var clickRect = (RectTransform)clickGO.transform;
            clickRect.anchorMin = clickRect.anchorMax = new Vector2(0.5f, 0f);
            clickRect.pivot = new Vector2(0.5f, 0f);
            clickRect.sizeDelta = new Vector2(80f, 120f);
            var clickImage = clickGO.GetComponent<Image>();
            clickImage.raycastTarget = true;
            clickGO.AddComponent<BattleClickTarget>().OnClick = e => OnUnitClicked(unit, e);

            var view = new UnitView
            {
                Unit = unit,
                Root = root,
                Body = body,
                Group = rootGO.GetComponent<CanvasGroup>(),
                Tintables = tintables,
                BaseColors = tintables.Select(i => i.color).ToArray(),
                HpFill = hpFill,
                ReadyFill = readyFill,
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

        private Image CreateFill(Transform parent, Color color)
        {
            var go = CreateImage(parent, "Fill", color);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(1f, 1f);
            rect.offsetMax = new Vector2(-1f, -1f);
            var img = go.GetComponent<Image>();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
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
            _skillButton.onClick.AddListener(PressSkill);

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

        private void BuildResultOverlay(bool victory, string summary)
        {
            var overlay = CreateImage(_canvas.transform, "ResultOverlay", new Color(0f, 0f, 0f, 0.6f));
            Stretch((RectTransform)overlay.transform);
            overlay.transform.SetAsLastSibling();

            var panel = CreateImage(overlay.transform, "ResultPanel", Color.white);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620f, 420f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;

            var title = CreateText(panel.transform, victory ? "승리!" : "패배...", 32, TextAnchor.MiddleCenter,
                victory ? new Color(0.5f, 0.85f, 0.45f) : new Color(0.85f, 0.4f, 0.35f));
            title.fontStyle = FontStyle.Bold;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -32f);
            titleRect.sizeDelta = new Vector2(560f, 48f);

            var body = CreateText(panel.transform, summary.Replace(" · ", "\n"), 17, TextAnchor.UpperCenter, UITheme.BodyText);
            var bodyRect = body.rectTransform;
            bodyRect.anchorMin = bodyRect.anchorMax = bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.anchoredPosition = new Vector2(0f, -96f);
            bodyRect.sizeDelta = new Vector2(560f, 230f);

            var backButton = CreateActionButton(panel.transform, Vector2.zero, out var backLabel);
            backLabel.text = "돌아가기";
            var backRect = (RectTransform)backButton.transform;
            backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 28f);
            backRect.sizeDelta = new Vector2(220f, 52f);
            backButton.onClick.AddListener(() => SceneManager.LoadScene("MainScene"));
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
