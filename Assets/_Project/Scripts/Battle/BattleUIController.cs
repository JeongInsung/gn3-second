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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 직접전투 화면 전체(유닛 배치/스킬·대상 선택/전투 로그/결과창)를 코드로 짜 넣는 단일 컨트롤러.
    /// 전투 규칙 자체는 ManualBattleSession이 맡고, 이 클래스는 그 상태를 화면에 비추고 입력을 돌려주기만 한다.
    /// 몬스터 쪽은 아직 그림이 없어 기본 도형(BattlePlaceholderSprites)으로 표시한다 - 나중에 그림이 생기면
    /// CreateUnitView의 enemy 분기만 바꾸면 된다.
    /// </summary>
    public class BattleUIController : MonoBehaviour
    {
        private class UnitView
        {
            public Combatant Combatant;
            public GameObject Root;
            public RectTransform AnimRoot; // 공격 동작·대기 중 흔들림을 여기에만 적용(Root 자체는 레이아웃이 관리)
            public CanvasGroup Group;
            public Image Frame;
            public Image HitFlash;
            public Image HpFill;
            public Text HpText;
            public Image ReadinessFill;
            public GameObject TurnMarker;
            public Button Button;
            public bool IsPlayerSide;
            public bool IsMelee; // 근접(전사·암살자 등)만 대상 자리까지 다가가서 때린다 - 원거리는 제자리에서 공격
            public float BobPhase;   // 대기 중 흔들림이 유닛마다 어긋나 보이도록
            public Vector2 LungeOffset; // 공격 동작(대상 쪽으로 다가갔다 돌아옴) 중 오프셋, 코루틴이 갱신
        }

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private ManualBattleSession _session;
        private readonly Dictionary<Combatant, UnitView> _views = new Dictionary<Combatant, UnitView>();
        private bool _pickingSkillTarget;
        private Combatant _selectedMerc; // 플레이어가 클릭해서 고른 내 용병(다음에 적을 클릭하면 그 용병의 공격 대상이 된다)
        private Combatant _actionPanelActor; // 행동 패널을 지금 누구 기준으로 띄워뒀는지(새 대기자로 바뀔 때만 다시 그림)
        private bool _resultShown;

        private Canvas _canvas;
        private Transform _enemyRow;
        private Transform _playerRow;
        private Text _turnLabel;
        private Text _logText;
        private readonly LinkedList<string> _logLines = new LinkedList<string>();
        private Button _skillButton;
        private Text _skillButtonText;

        private const int MaxLogLines = 9;

        public void Init(Expedition expedition)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _expedition = expedition;
            var quest = expedition.Quest;

            _fighters = expedition.Members.Where(m => m.IsAlive).ToList();
            var combatants = _fighters.Select(m => m.ToCombatant()).ToList();
            var skills = new Dictionary<Combatant, SkillBase>();
            for (int i = 0; i < _fighters.Count; i++)
            {
                var skill = ClassSkillFactory.Create(_fighters[i].Class.Kind);
                if (skill != null) skills[combatants[i]] = skill;
            }

            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(quest.Region, quest.EnemyCount, quest.Difficulty, rng);
            int tier = Math.Clamp(quest.Difficulty - 1, 0, 2);

            _session = new ManualBattleSession(combatants, skills, enemies, rng);

            BuildUI(quest);
            for (int i = 0; i < _fighters.Count; i++)
            {
                bool isMelee = IsMeleeClass(_fighters[i].Class.Kind);
                _views[combatants[i]] = CreateUnitView(_playerRow, _fighters[i].Name, combatants[i], _fighters[i].Appearance, -1, isMelee);
            }
            // 몬스터는 아직 직업 개념이 없어 전부 근접(달려들어 때림)으로 둔다.
            foreach (var enemy in enemies)
                _views[enemy] = CreateUnitView(_enemyRow, enemy.Name, enemy, null, tier, isMelee: true);

            HideActionPanel(); // 스킬 버튼 초기 상태(비활성)까지 맞춰둔다
        }

        /// <summary>전사·암살자처럼 몸으로 부딪히는 직업만 근접(대상 자리까지 다가가 때림), 나머지는 원거리(제자리에서 공격).</summary>
        private static bool IsMeleeClass(MercenaryClassKind kind) =>
            kind == MercenaryClassKind.Warrior || kind == MercenaryClassKind.Assassin;

        // ---------- 전투 진행(ATB - 매 프레임 게이지를 돌린다) ----------

        private void Update()
        {
            if (_session == null || _resultShown) return;

            var events = _session.Tick(Time.deltaTime);
            if (events.Count > 0) ApplyEventsVisual(events);

            RefreshUI();
        }

        private void RefreshUI()
        {
            foreach (var v in _views.Values)
                UpdateUnitView(v);

            if (_session.IsOver)
            {
                ShowResult();
                return;
            }

            var waiting = _session.WaitingPlayerActor;
            if (waiting == null)
            {
                if (_actionPanelActor != null) HideActionPanel();
                return;
            }

            if (_actionPanelActor != waiting)
            {
                _actionPanelActor = waiting;
                _pickingSkillTarget = false;
                ShowActionPanel(waiting);
            }
            else if (!_pickingSkillTarget)
            {
                // 스킬을 고르지 않고 있는 동안엔 남은 자동공격 시간을 계속 갱신해서 보여준다.
                UpdateCountdownLabel(waiting);
            }

            RefreshInteractable();
        }

        private void HideActionPanel()
        {
            _actionPanelActor = null;
            _pickingSkillTarget = false;
            _session.Deciding = false;
            _skillButton.interactable = false;
            _turnLabel.text = "전투 진행 중...";
            RefreshInteractable();
        }

        private void ShowActionPanel(Combatant actor)
        {
            var skill = _session.SkillOf(actor);
            float cooldown = _session.CooldownRemaining(actor);
            if (skill == null)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = "전투 스킬 없음";
            }
            else if (cooldown > 0f)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = $"{skill.Name} (쿨타임 {Mathf.CeilToInt(cooldown)})";
            }
            else
            {
                _skillButton.interactable = true;
                _skillButtonText.text = skill.Name;
            }

            UpdateCountdownLabel(actor);
        }

        private void UpdateCountdownLabel(Combatant actor)
        {
            float countdown = _session.AutoAttackCountdown;
            _turnLabel.text = countdown > 0f
                ? $"{actor.Name}의 차례! (자동공격 {countdown:0.0}초)"
                : $"{actor.Name}의 차례!";
        }

        private void OnSkillClicked()
        {
            var actor = _session.WaitingPlayerActor;
            if (actor == null) return;
            var skill = _session.SkillOf(actor);
            if (skill == null || _session.CooldownRemaining(actor) > 0f) return;
            _selectedMerc = null;
            _pickingSkillTarget = true;
            _session.Deciding = true;
            _turnLabel.text = $"{actor.Name} - {skill.Name} 대상을 고르세요";
            RefreshInteractable();
        }

        /// <summary>
        /// 유닛 카드 클릭. 세 가지 뜻 중 하나:
        /// 1) 스킬 대상을 고르는 중이면 - 그 대상에게 스킬 사용(기존 동작).
        /// 2) 내 용병을 클릭 - 그 용병을 선택(다시 클릭하면 선택 해제, 다른 용병 클릭하면 선택 바뀜).
        /// 3) 용병을 선택해둔 채 적을 클릭 - 그 용병에게 "이 적을 공격해라" 지정. 근접이면 즉시 그 자리로 다가가고,
        ///    마침 그 용병이 입력을 기다리던 참이면(WaitingPlayerActor) 지금 바로 공격까지 수행한다.
        /// </summary>
        private void OnUnitClicked(Combatant clicked)
        {
            if (_pickingSkillTarget)
            {
                if (_session.WaitingPlayerActor == null || !clicked.IsAlive) return;
                _pickingSkillTarget = false;
                var events = _session.PlayerAct(true, clicked);
                ApplyEventsVisual(events);
                RefreshInteractable();
                return;
            }

            if (!clicked.IsAlive) return;

            if (_session.PlayerTeam.Contains(clicked))
            {
                _selectedMerc = _selectedMerc == clicked ? null : clicked;
                RefreshInteractable();
                return;
            }

            if (_selectedMerc != null)
            {
                var merc = _selectedMerc;
                _selectedMerc = null;
                _session.SetEngagedTarget(merc, clicked);
                if (_session.WaitingPlayerActor == merc)
                {
                    var events = _session.PlayerAct(false, clicked);
                    ApplyEventsVisual(events);
                }
                RefreshInteractable();
            }
        }

        private void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;

            var result = new BattleResult
            {
                Outcome = _session.Outcome,
                Rounds = _session.ActionCount,
                Log = _session.Log,
                TeamA = _session.PlayerTeam,
                TeamB = _session.EnemyTeam,
            };
            var battle = new ExpeditionBattle.Battle { Result = result, Fighters = _fighters, Combatants = _session.PlayerTeam };
            string summary = ExpeditionBattle.Conclude(_expedition, battle);

            HideActionPanel();
            bool victory = _session.Outcome == BattleOutcome.TeamAVictory;
            _turnLabel.text = victory ? "승리!" : "패배...";
            BuildResultOverlay(victory, summary);
        }

        // ---------- 유닛 표시 ----------

        private static readonly Color TransparentFrame = new Color(0f, 0f, 0f, 0f);
        private static readonly Color TargetableFrame = new Color(0.9f, 0.85f, 0.3f, 0.35f); // 지금 클릭하면 뜻이 있는 대상(노랑)
        private static readonly Color SelectedFrame = new Color(0.3f, 0.7f, 1f, 0.45f);       // 내가 선택해둔 용병(파랑)

        /// <summary>
        /// 모든 유닛 카드의 "지금 클릭 가능한가/어떤 색으로 강조할까"를 한 곳에서 매 프레임 다시 계산한다.
        /// - 스킬 대상을 고르는 중: 스킬의 TargetType에 맞는 생존 유닛만 클릭 가능(노랑)
        /// - 그 외: 내 용병은 항상 클릭 가능(선택 중이면 파랑), 적은 용병을 선택해둔 동안에만 클릭 가능(노랑)
        /// </summary>
        private void RefreshInteractable()
        {
            if (_pickingSkillTarget)
            {
                var waiting = _session.WaitingPlayerActor;
                var skill = waiting != null ? _session.SkillOf(waiting) : null;
                if (waiting == null || skill == null)
                {
                    _pickingSkillTarget = false;
                }
                else
                {
                    var pool = skill.TargetType == SkillTargetType.Enemy ? _session.EnemyTeam : _session.PlayerTeam;
                    var targets = new HashSet<Combatant>(pool.Where(c => c.IsAlive));
                    foreach (var kv in _views)
                        SetFrame(kv.Value, targets.Contains(kv.Key), TargetableFrame);
                    return;
                }
            }

            if (_selectedMerc != null && !_selectedMerc.IsAlive) _selectedMerc = null;

            foreach (var kv in _views)
            {
                bool alive = kv.Key.IsAlive;
                if (_session.PlayerTeam.Contains(kv.Key))
                {
                    kv.Value.Button.interactable = alive; // 내 용병은 언제든 선택 가능
                    kv.Value.Frame.color = kv.Key == _selectedMerc ? SelectedFrame : TransparentFrame;
                }
                else
                {
                    SetFrame(kv.Value, alive && _selectedMerc != null, TargetableFrame); // 적은 용병을 선택해둔 동안에만
                }
            }
        }

        private static void SetFrame(UnitView v, bool interactable, Color highlightColor)
        {
            v.Button.interactable = interactable;
            v.Frame.color = interactable ? highlightColor : TransparentFrame;
        }

        private void UpdateUnitView(UnitView v)
        {
            bool alive = v.Combatant.IsAlive;
            v.Group.alpha = alive ? 1f : 0.35f;
            float ratio = alive ? (float)v.Combatant.CurrentHealth / Math.Max(1, v.Combatant.Stats.MaxHealth) : 0f;
            v.HpFill.fillAmount = Mathf.Clamp01(ratio);
            v.HpFill.color = ratio > 0.5f ? new Color(0.4f, 0.75f, 0.35f) : ratio > 0.25f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.75f, 0.25f, 0.2f);
            v.HpText.text = alive ? $"{v.Combatant.CurrentHealth}/{v.Combatant.Stats.MaxHealth}" : "전사";
            v.ReadinessFill.fillAmount = alive ? _session.ReadinessRatioOf(v.Combatant) : 0f;
            v.TurnMarker.SetActive(alive && _session.WaitingPlayerActor == v.Combatant);

            // 내 쪽 근접 유닛은 "붙어서 싸우는 대상"이 살아있는 동안 계속 그 자리에 머문다(안 돌아옴) -
            // 매 프레임 목표 오프셋으로 부드럽게 다가가고, 대상이 죽으면(또는 아직 없으면) 서서히 제자리로.
            // 적(원거리는 아예 호출 안 되는 Lunge 코루틴)은 그대로 한 번 다가갔다 돌아오는 연출을 쓴다.
            if (v.IsPlayerSide && v.IsMelee && alive)
            {
                var engaged = v.Combatant != null ? _session.EngagedTargetOf(v.Combatant) : null;
                Vector2 goal = (engaged != null && _views.TryGetValue(engaged, out var targetView2))
                    ? ApproachOffset(v, targetView2)
                    : Vector2.zero;
                v.LungeOffset = Vector2.Lerp(v.LungeOffset, goal, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }

            float bob = alive ? Mathf.Sin(Time.time * 1.6f + v.BobPhase) * 3f : 0f;
            v.AnimRoot.anchoredPosition = new Vector2(0f, bob) + v.LungeOffset;
        }

        private void ApplyEventsVisual(List<BattleEvent> events)
        {
            foreach (var evt in events)
            {
                AppendLog(FormatEvent(evt));

                // 내 쪽 근접 유닛은 UpdateUnitView가 매 프레임 "붙어서 싸우는 대상" 쪽으로 자연스럽게 따라가므로
                // 여기서 따로 움직이지 않는다 - 적 쪽 근접만 기존처럼 한 번 다가갔다 돌아오는 연출을 쓴다.
                _views.TryGetValue(evt.Target, out var targetView);
                if (_views.TryGetValue(evt.Attacker, out var attackerView) && attackerView.IsMelee
                    && !attackerView.IsPlayerSide && targetView != null)
                    StartCoroutine(Lunge(attackerView, targetView));

                if (targetView != null)
                {
                    UpdateUnitView(targetView);
                    string label = evt.Damage < 0 ? $"+{-evt.Damage}" : evt.Evaded ? "회피" : $"-{evt.Damage}";
                    Color color = evt.Damage < 0 ? new Color(0.45f, 0.9f, 0.5f)
                        : evt.Evaded ? Color.white : new Color(1f, 0.45f, 0.4f);
                    StartCoroutine(FloatText(targetView, label, color));
                    if (evt.Damage > 0 && !evt.Evaded)
                        StartCoroutine(HitFlash(targetView));
                }
            }
        }

        /// <summary>대상 쪽으로 다가가는 데 필요한 AnimRoot 오프셋. Root는 Row 레이아웃이 관리하므로 그대로 두고,
        /// 두 Root의 월드(=캔버스) 좌표 차이를 캔버스 배율로 나눠 anchoredPosition 오프셋으로 바꾼다
        /// - 행(위/아래)이 달라도 정확한 방향·거리가 나온다. 거리의 55%까지만 다가가 대상과 완전히 겹치지 않게 한다.</summary>
        private Vector2 ApproachOffset(UnitView attacker, UnitView target)
        {
            const float approachFraction = 0.55f;
            Vector3 worldDelta = target.Root.transform.position - attacker.Root.transform.position;
            float scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
            return (Vector2)worldDelta / scale * approachFraction;
        }

        /// <summary>근접 공격 연출(적 전용): 대상 자리까지 다가갔다가 원래 자리로 돌아온다(원거리는 아예 호출 안 됨 - 제자리).
        /// 내 쪽 근접 유닛은 UpdateUnitView가 "붙어서 싸우는 대상" 쪽으로 계속 따라가는 방식을 쓰므로 이 코루틴을 안 쓴다.</summary>
        private IEnumerator Lunge(UnitView attacker, UnitView target)
        {
            const float outDur = 0.14f, holdDur = 0.05f, backDur = 0.2f;
            Vector2 approach = ApproachOffset(attacker, target);

            float t = 0f;
            while (t < outDur)
            {
                t += Time.deltaTime;
                attacker.LungeOffset = approach * Mathf.SmoothStep(0f, 1f, t / outDur);
                yield return null;
            }
            attacker.LungeOffset = approach;
            yield return new WaitForSeconds(holdDur);

            t = 0f;
            while (t < backDur)
            {
                t += Time.deltaTime;
                attacker.LungeOffset = approach * (1f - Mathf.SmoothStep(0f, 1f, t / backDur));
                yield return null;
            }
            attacker.LungeOffset = Vector2.zero;
        }

        /// <summary>피격 연출: 포트레이트 위로 붉은 막이 잠깐 번쩍였다 사라진다.</summary>
        private IEnumerator HitFlash(UnitView v)
        {
            const float duration = 0.3f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (v.HitFlash == null) yield break;
                v.HitFlash.color = new Color(1f, 0.2f, 0.15f, 0.5f * (1f - t / duration));
                yield return null;
            }
            if (v.HitFlash != null) v.HitFlash.color = new Color(1f, 0.2f, 0.15f, 0f);
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

        private IEnumerator FloatText(UnitView v, string text, Color color)
        {
            var go = new GameObject("Float", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
            go.transform.SetParent(v.Root.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, 6f);
            rect.sizeDelta = new Vector2(150f, 30f);

            var text2 = go.GetComponent<Text>();
            text2.font = _font;
            text2.fontSize = 22;
            text2.fontStyle = FontStyle.Bold;
            text2.alignment = TextAnchor.MiddleCenter;
            text2.color = color;
            text2.text = text;
            text2.raycastTarget = false;

            var group = go.GetComponent<CanvasGroup>();
            const float duration = 0.9f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                rect.anchoredPosition = new Vector2(0f, 6f + 44f * (t / duration));
                group.alpha = 1f - t / duration;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ---------- 화면 생성 ----------

        private void BuildUI(Quest quest)
        {
            var canvasGO = new GameObject("BattleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas = canvas;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var root = canvasGO.transform;

            if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            BuildBackground(root);

            var title = CreateText(root, $"{QuestDifficulty.RichTitle(quest)}", 24, TextAnchor.MiddleCenter, UITheme.TitleText);
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(1200f, 40f);

            _enemyRow = CreateRow(root, new Vector2(0.5f, 1f), new Vector2(0f, -140f));
            _playerRow = CreateRow(root, new Vector2(0.5f, 0f), new Vector2(0f, 300f));

            BuildActionPanel(root);
        }

        private void BuildBackground(Transform root)
        {
            var sky = new GameObject("Sky", typeof(RectTransform), typeof(Image));
            sky.transform.SetParent(root, false);
            var skyRect = (RectTransform)sky.transform;
            skyRect.anchorMin = new Vector2(0f, 0.32f);
            skyRect.anchorMax = new Vector2(1f, 1f);
            skyRect.offsetMin = skyRect.offsetMax = Vector2.zero;
            sky.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.15f, 1f);

            var ground = new GameObject("Ground", typeof(RectTransform), typeof(Image));
            ground.transform.SetParent(root, false);
            var groundRect = (RectTransform)ground.transform;
            groundRect.anchorMin = Vector2.zero;
            groundRect.anchorMax = new Vector2(1f, 0.32f);
            groundRect.offsetMin = groundRect.offsetMax = Vector2.zero;
            ground.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.13f, 1f);
        }

        private Transform CreateRow(Transform parent, Vector2 anchor, Vector2 anchoredPosition)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(1700f, 190f);

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            // 주의(DEVLOG 2026-09-17): childControlWidth/Height를 false로 두면 LayoutElement 값이 실제 크기에
            // 반영되지 않고 항상 기본 100x100으로 남는 버그가 있다. 반드시 true로 켜서 LayoutElement가 먹게 한다.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return go.transform;
        }

        private UnitView CreateUnitView(Transform parent, string displayName, Combatant combatant, ComposedCharacter appearance, int tier, bool isMelee)
        {
            var root = new GameObject(displayName, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var layout = root.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 160f;
            layout.minHeight = layout.preferredHeight = 198f;
            var group = root.GetComponent<CanvasGroup>();

            // 하이라이트/대상 선택 테두리(평소엔 투명) + 클릭(대상 지정)
            var frameGO = new GameObject("Frame", typeof(RectTransform), typeof(Image), typeof(Button));
            frameGO.transform.SetParent(root.transform, false);
            var frameRect = (RectTransform)frameGO.transform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            var frame = frameGO.GetComponent<Image>();
            frame.sprite = UITheme.Inset;
            frame.type = Image.Type.Sliced;
            frame.color = new Color(0f, 0f, 0f, 0f);
            var button = frameGO.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.interactable = false;
            button.onClick.AddListener(() => OnUnitClicked(combatant));

            // 공격 동작·대기 중 흔들림은 전부 이 안쪽 컨테이너에만 적용한다(Root 자체는 Row의 레이아웃이 관리하므로 건드리지 않음).
            var animRootGO = new GameObject("AnimRoot", typeof(RectTransform));
            animRootGO.transform.SetParent(root.transform, false);
            var animRoot = (RectTransform)animRootGO.transform;
            animRoot.anchorMin = Vector2.zero;
            animRoot.anchorMax = Vector2.one;
            animRoot.offsetMin = animRoot.offsetMax = Vector2.zero;

            // 초상화 또는 기본 도형
            GameObject portraitGO;
            if (appearance != null)
            {
                portraitGO = CharacterPortraitUI.Create(animRoot, appearance, 110, PortraitCrop.FullBody);
                ((RectTransform)portraitGO.transform).sizeDelta = new Vector2(110f, 110f);
            }
            else
            {
                portraitGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Image));
                portraitGO.transform.SetParent(animRoot, false);
                var img = portraitGO.GetComponent<Image>();
                img.sprite = BattlePlaceholderSprites.Circle;
                img.color = BattlePlaceholderSprites.ColorForTier(tier);
                img.raycastTarget = false;
                float s = 86f * BattlePlaceholderSprites.SizeForTier(tier);
                ((RectTransform)portraitGO.transform).sizeDelta = new Vector2(s, s);
            }
            var portraitRect = (RectTransform)portraitGO.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0.5f, 1f);
            portraitRect.anchoredPosition = new Vector2(0f, -8f);

            var turnMarker = CreateText(animRoot, "▼", 20, TextAnchor.MiddleCenter, UITheme.TitleText);
            turnMarker.raycastTarget = false;
            var markerRect = turnMarker.rectTransform;
            markerRect.anchorMin = markerRect.anchorMax = markerRect.pivot = new Vector2(0.5f, 1f);
            markerRect.anchoredPosition = new Vector2(0f, 14f);
            markerRect.sizeDelta = new Vector2(60f, 22f);
            turnMarker.gameObject.SetActive(false);

            var nameText = CreateText(animRoot, displayName, 15, TextAnchor.MiddleCenter, UITheme.BodyText);
            nameText.raycastTarget = false;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -122f);
            nameRect.sizeDelta = new Vector2(156f, 22f);

            var hpBg = new GameObject("HpBg", typeof(RectTransform), typeof(Image));
            hpBg.transform.SetParent(animRoot, false);
            var hpBgRect = (RectTransform)hpBg.transform;
            hpBgRect.anchorMin = hpBgRect.anchorMax = hpBgRect.pivot = new Vector2(0.5f, 1f);
            hpBgRect.anchoredPosition = new Vector2(0f, -144f);
            hpBgRect.sizeDelta = new Vector2(140f, 16f);
            var hpBgImage = hpBg.GetComponent<Image>();
            hpBgImage.sprite = UITheme.Inset;
            hpBgImage.type = Image.Type.Sliced;
            hpBgImage.raycastTarget = false;

            var hpFillGO = new GameObject("HpFill", typeof(RectTransform), typeof(Image));
            hpFillGO.transform.SetParent(hpBg.transform, false);
            var hpFillRect = (RectTransform)hpFillGO.transform;
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.offsetMin = new Vector2(2f, 2f);
            hpFillRect.offsetMax = new Vector2(-2f, -2f);
            var hpFill = hpFillGO.GetComponent<Image>();
            hpFill.color = new Color(0.4f, 0.75f, 0.35f);
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFill.fillAmount = 1f;
            hpFill.raycastTarget = false;

            var hpText = CreateText(hpBg.transform, "", 11, TextAnchor.MiddleCenter, Color.white);
            hpText.raycastTarget = false;
            var hpTextRect = hpText.rectTransform;
            hpTextRect.anchorMin = Vector2.zero;
            hpTextRect.anchorMax = Vector2.one;
            hpTextRect.offsetMin = hpTextRect.offsetMax = Vector2.zero;

            // 준비 게이지(ATB) - 체력바 바로 아래, 다 차면 그 유닛이 행동한다.
            var readyBg = new GameObject("ReadinessBg", typeof(RectTransform), typeof(Image));
            readyBg.transform.SetParent(animRoot, false);
            var readyBgRect = (RectTransform)readyBg.transform;
            readyBgRect.anchorMin = readyBgRect.anchorMax = readyBgRect.pivot = new Vector2(0.5f, 1f);
            readyBgRect.anchoredPosition = new Vector2(0f, -162f);
            readyBgRect.sizeDelta = new Vector2(140f, 9f);
            // 9px짜리 얇은 바라 9-slice(Inset)는 모서리가 두꺼워 뭉개져 보이므로 민무늬 색으로.
            var readyBgImage = readyBg.GetComponent<Image>();
            readyBgImage.color = new Color(0.05f, 0.05f, 0.06f, 1f);
            readyBgImage.raycastTarget = false;

            var readyFillGO = new GameObject("ReadinessFill", typeof(RectTransform), typeof(Image));
            readyFillGO.transform.SetParent(readyBg.transform, false);
            var readyFillRect = (RectTransform)readyFillGO.transform;
            readyFillRect.anchorMin = Vector2.zero;
            readyFillRect.anchorMax = Vector2.one;
            readyFillRect.offsetMin = new Vector2(1.5f, 1.5f);
            readyFillRect.offsetMax = new Vector2(-1.5f, -1.5f);
            var readyFill = readyFillGO.GetComponent<Image>();
            readyFill.color = new Color(0.35f, 0.75f, 0.9f);
            readyFill.type = Image.Type.Filled;
            readyFill.fillMethod = Image.FillMethod.Horizontal;
            readyFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            readyFill.fillAmount = 0f;
            readyFill.raycastTarget = false;

            // 피격 막(평소엔 완전 투명) - AnimRoot보다 위에 그려야 하므로 root의 마지막 자식으로 둔다.
            var hitFlashGO = new GameObject("HitFlash", typeof(RectTransform), typeof(Image));
            hitFlashGO.transform.SetParent(root.transform, false);
            var hitFlashRect = (RectTransform)hitFlashGO.transform;
            hitFlashRect.anchorMin = Vector2.zero;
            hitFlashRect.anchorMax = Vector2.one;
            hitFlashRect.offsetMin = hitFlashRect.offsetMax = Vector2.zero;
            var hitFlash = hitFlashGO.GetComponent<Image>();
            hitFlash.color = new Color(1f, 0.2f, 0.15f, 0f);
            hitFlash.raycastTarget = false;

            var view = new UnitView
            {
                Combatant = combatant,
                Root = root,
                AnimRoot = animRoot,
                Group = group,
                Frame = frame,
                HitFlash = hitFlash,
                HpFill = hpFill,
                HpText = hpText,
                ReadinessFill = readyFill,
                TurnMarker = turnMarker.gameObject,
                Button = button,
                IsPlayerSide = tier < 0,
                IsMelee = isMelee,
                BobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            };
            UpdateUnitView(view);
            return view;
        }

        private void BuildActionPanel(Transform root)
        {
            var panel = new GameObject("ActionPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 230f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(1f, 1f, 1f, 0.97f);

            _turnLabel = CreateText(panel.transform, "", 18, TextAnchor.UpperLeft, UITheme.TitleText);
            _turnLabel.fontStyle = FontStyle.Bold;
            var turnRect = _turnLabel.rectTransform;
            turnRect.anchorMin = turnRect.anchorMax = turnRect.pivot = new Vector2(0f, 1f);
            turnRect.anchoredPosition = new Vector2(28f, -16f);
            turnRect.sizeDelta = new Vector2(280f, 72f);
            _turnLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 기본공격은 자동(랜덤 대상)이라 버튼이 필요 없다 - 스킬 쓰고 싶을 때만 이 버튼으로 개입한다.
            _skillButton = CreateActionButton(panel.transform, new Vector2(28f, -100f), out _skillButtonText);
            _skillButton.onClick.AddListener(OnSkillClicked);

            var logBg = new GameObject("LogPanel", typeof(RectTransform), typeof(Image));
            logBg.transform.SetParent(panel.transform, false);
            var logRect = (RectTransform)logBg.transform;
            logRect.anchorMin = new Vector2(0f, 0f);
            logRect.anchorMax = new Vector2(1f, 1f);
            logRect.offsetMin = new Vector2(320f, 16f);
            logRect.offsetMax = new Vector2(-28f, -16f);
            var logBgImage = logBg.GetComponent<Image>();
            logBgImage.sprite = UITheme.Inset;
            logBgImage.type = Image.Type.Sliced;

            _logText = CreateText(logBg.transform, "", 14, TextAnchor.LowerLeft, UITheme.MutedText);
            var logTextRect = _logText.rectTransform;
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(12f, 8f);
            logTextRect.offsetMax = new Vector2(-12f, -8f);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private Button CreateActionButton(Transform parent, Vector2 anchoredPosition, out Text label)
        {
            var go = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(260f, 52f);

            var image = go.GetComponent<Image>();
            image.sprite = UITheme.Button;
            image.type = Image.Type.Sliced;

            label = CreateText(go.transform, "", 17, TextAnchor.MiddleCenter, UITheme.BodyText);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            label.raycastTarget = false;

            return go.GetComponent<Button>();
        }

        private void BuildResultOverlay(bool victory, string summary)
        {
            var overlay = new GameObject("ResultOverlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(_turnLabel.transform.root, false);
            var overlayRect = (RectTransform)overlay.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            overlay.transform.SetAsLastSibling();

            var panel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlay.transform, false);
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
