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
        private enum PendingAction { None, Basic, Skill }

        private class UnitView
        {
            public Combatant Combatant;
            public GameObject Root;
            public CanvasGroup Group;
            public Image Frame;
            public Image HpFill;
            public Text HpText;
            public GameObject TurnMarker;
            public Button Button;
        }

        private Font _font;
        private Expedition _expedition;
        private List<Mercenary> _fighters;
        private ManualBattleSession _session;
        private readonly Dictionary<Combatant, UnitView> _views = new Dictionary<Combatant, UnitView>();
        private PendingAction _pendingAction = PendingAction.None;
        private bool _resultShown;

        private Transform _enemyRow;
        private Transform _playerRow;
        private Text _turnLabel;
        private Text _logText;
        private readonly LinkedList<string> _logLines = new LinkedList<string>();
        private Button _basicButton;
        private Text _basicButtonText;
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
                _views[combatants[i]] = CreateUnitView(_playerRow, _fighters[i].Name, combatants[i], _fighters[i].Appearance, -1);
            foreach (var enemy in enemies)
                _views[enemy] = CreateUnitView(_enemyRow, enemy.Name, enemy, null, tier);

            RefreshTurnUI();
        }

        // ---------- 전투 진행 ----------

        private void RefreshTurnUI()
        {
            foreach (var v in _views.Values)
                UpdateUnitView(v);
            ClearTargetable();

            if (_session.IsOver)
            {
                ShowResult();
                return;
            }

            if (_session.CurrentIsPlayer)
            {
                _pendingAction = PendingAction.None;
                ShowActionPanel();
            }
            else
            {
                _basicButton.interactable = false;
                _skillButton.interactable = false;
                _turnLabel.text = $"{_session.CurrentActor.Name}의 차례 (적)";
                StartCoroutine(EnemyTurnRoutine());
            }
        }

        private void ShowActionPanel()
        {
            var actor = _session.CurrentActor;
            _turnLabel.text = $"{actor.Name}의 차례";

            _basicButton.interactable = true;
            _basicButtonText.text = "기본공격";

            var skill = _session.SkillOf(actor);
            int cooldown = _session.CooldownRemaining(actor);
            if (skill == null)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = "전투 스킬 없음";
            }
            else if (cooldown > 0)
            {
                _skillButton.interactable = false;
                _skillButtonText.text = $"{skill.Name} (쿨타임 {cooldown})";
            }
            else
            {
                _skillButton.interactable = true;
                _skillButtonText.text = skill.Name;
            }
        }

        private void OnBasicClicked()
        {
            if (!_session.CurrentIsPlayer) return;
            _pendingAction = PendingAction.Basic;
            HighlightTargetable(SkillTargetType.Enemy);
        }

        private void OnSkillClicked()
        {
            var skill = _session.SkillOf(_session.CurrentActor);
            if (skill == null || !_session.CurrentIsPlayer) return;
            _pendingAction = PendingAction.Skill;
            HighlightTargetable(skill.TargetType);
        }

        private void OnUnitClicked(Combatant target)
        {
            if (_pendingAction == PendingAction.None || !_session.CurrentIsPlayer) return;
            bool useSkill = _pendingAction == PendingAction.Skill;
            _pendingAction = PendingAction.None;

            var events = _session.PlayerAct(useSkill, target);
            ApplyEventsVisual(events);
            RefreshTurnUI();
        }

        private IEnumerator EnemyTurnRoutine()
        {
            yield return new WaitForSeconds(0.5f);
            if (!_session.IsOver)
            {
                var events = _session.EnemyAct();
                ApplyEventsVisual(events);
            }
            yield return new WaitForSeconds(0.5f);
            RefreshTurnUI();
        }

        private void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;

            var result = new BattleResult
            {
                Outcome = _session.Outcome,
                Rounds = _session.Round,
                Log = _session.Log,
                TeamA = _session.PlayerTeam,
                TeamB = _session.EnemyTeam,
            };
            var battle = new ExpeditionBattle.Battle { Result = result, Fighters = _fighters, Combatants = _session.PlayerTeam };
            string summary = ExpeditionBattle.Conclude(_expedition, battle);

            _basicButton.interactable = false;
            _skillButton.interactable = false;
            bool victory = _session.Outcome == BattleOutcome.TeamAVictory;
            _turnLabel.text = victory ? "승리!" : "패배...";
            BuildResultOverlay(victory, summary);
        }

        // ---------- 유닛 표시 ----------

        private void HighlightTargetable(SkillTargetType type)
        {
            var targets = new HashSet<Combatant>(type == SkillTargetType.Enemy ? _session.AliveEnemies : _session.AliveAllies);
            foreach (var kv in _views)
                SetTargetable(kv.Value, targets.Contains(kv.Key));
        }

        private void ClearTargetable()
        {
            foreach (var v in _views.Values)
                SetTargetable(v, false);
        }

        private void SetTargetable(UnitView v, bool targetable)
        {
            v.Button.interactable = targetable && v.Combatant.IsAlive;
            v.Frame.color = targetable ? new Color(0.9f, 0.85f, 0.3f, 0.35f) : new Color(0f, 0f, 0f, 0f);
        }

        private void UpdateUnitView(UnitView v)
        {
            bool alive = v.Combatant.IsAlive;
            v.Group.alpha = alive ? 1f : 0.35f;
            float ratio = alive ? (float)v.Combatant.CurrentHealth / Math.Max(1, v.Combatant.Stats.MaxHealth) : 0f;
            v.HpFill.fillAmount = Mathf.Clamp01(ratio);
            v.HpFill.color = ratio > 0.5f ? new Color(0.4f, 0.75f, 0.35f) : ratio > 0.25f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.75f, 0.25f, 0.2f);
            v.HpText.text = alive ? $"{v.Combatant.CurrentHealth}/{v.Combatant.Stats.MaxHealth}" : "전사";
            v.TurnMarker.SetActive(!_session.IsOver && _session.CurrentActor == v.Combatant);
        }

        private void ApplyEventsVisual(List<BattleEvent> events)
        {
            foreach (var evt in events)
            {
                AppendLog(FormatEvent(evt));
                if (_views.TryGetValue(evt.Target, out var view))
                {
                    UpdateUnitView(view);
                    string label = evt.Damage < 0 ? $"+{-evt.Damage}" : evt.Evaded ? "회피" : $"-{evt.Damage}";
                    Color color = evt.Damage < 0 ? new Color(0.45f, 0.9f, 0.5f)
                        : evt.Evaded ? Color.white : new Color(1f, 0.45f, 0.4f);
                    StartCoroutine(FloatText(view, label, color));
                }
            }
        }

        private static string FormatEvent(BattleEvent evt)
        {
            if (evt.Damage < 0) return $"[R{evt.Round}] {evt.Attacker.Name} → {evt.Target.Name} : +{-evt.Damage} 회복";
            string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
            return $"[R{evt.Round}] {evt.Attacker.Name} → {evt.Target.Name} : {evt.Damage}{suffix}";
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

        private UnitView CreateUnitView(Transform parent, string displayName, Combatant combatant, ComposedCharacter appearance, int tier)
        {
            var root = new GameObject(displayName, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var layout = root.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 160f;
            layout.minHeight = layout.preferredHeight = 180f;
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

            // 초상화 또는 기본 도형
            GameObject portraitGO;
            if (appearance != null)
            {
                portraitGO = CharacterPortraitUI.Create(root.transform, appearance, 110, PortraitCrop.FullBody);
                ((RectTransform)portraitGO.transform).sizeDelta = new Vector2(110f, 110f);
            }
            else
            {
                portraitGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Image));
                portraitGO.transform.SetParent(root.transform, false);
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

            var turnMarker = CreateText(root.transform, "▼", 20, TextAnchor.MiddleCenter, UITheme.TitleText);
            turnMarker.raycastTarget = false;
            var markerRect = turnMarker.rectTransform;
            markerRect.anchorMin = markerRect.anchorMax = markerRect.pivot = new Vector2(0.5f, 1f);
            markerRect.anchoredPosition = new Vector2(0f, 14f);
            markerRect.sizeDelta = new Vector2(60f, 22f);
            turnMarker.gameObject.SetActive(false);

            var nameText = CreateText(root.transform, displayName, 15, TextAnchor.MiddleCenter, UITheme.BodyText);
            nameText.raycastTarget = false;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -122f);
            nameRect.sizeDelta = new Vector2(156f, 22f);

            var hpBg = new GameObject("HpBg", typeof(RectTransform), typeof(Image));
            hpBg.transform.SetParent(root.transform, false);
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

            var view = new UnitView
            {
                Combatant = combatant,
                Root = root,
                Group = group,
                Frame = frame,
                HpFill = hpFill,
                HpText = hpText,
                TurnMarker = turnMarker.gameObject,
                Button = button,
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

            _turnLabel = CreateText(panel.transform, "", 20, TextAnchor.MiddleLeft, UITheme.TitleText);
            _turnLabel.fontStyle = FontStyle.Bold;
            var turnRect = _turnLabel.rectTransform;
            turnRect.anchorMin = turnRect.anchorMax = turnRect.pivot = new Vector2(0f, 1f);
            turnRect.anchoredPosition = new Vector2(28f, -16f);
            turnRect.sizeDelta = new Vector2(400f, 34f);

            _basicButton = CreateActionButton(panel.transform, new Vector2(28f, -56f), out _basicButtonText);
            _basicButton.onClick.AddListener(OnBasicClicked);
            _skillButton = CreateActionButton(panel.transform, new Vector2(28f, -122f), out _skillButtonText);
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
