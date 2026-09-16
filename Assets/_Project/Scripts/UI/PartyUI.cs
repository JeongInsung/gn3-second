using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Combat;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Traits;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public class PartyUI : MonoBehaviour
    {
        [SerializeField] private Transform listContainer;
        [SerializeField] private Text resultText;

        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly HashSet<string> _selectedForDispatch = new HashSet<string>();
        private Font _font;
        private Transform _panelTransform;
        private GameObject _dispatchButtonGO;
        private Quest _pendingQuest;
        private Expedition _battlingExpedition;
        private Coroutine _battlePlayback;

        private void Awake()
        {
            // ScrollListWrapper가 listContainer를 새 계층으로 옮기기 전에, 원래 부모(PartyPanel)를 기억해둔다.
            _panelTransform = listContainer.parent;

            // PartyPanel(640x780) 안에서 제목 아래 ~ 결과 텍스트 위까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 170f), new Vector2(-20f, -105f));

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            PlayerParty.Instance.OnChanged += RefreshList;
            ExpeditionLog.Instance.OnChanged += RefreshList;
            ExpeditionLog.Instance.OnTravelEvent += HandleTravelEvent;
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshList;
            ExpeditionLog.Instance.OnChanged -= RefreshList;
            ExpeditionLog.Instance.OnTravelEvent -= HandleTravelEvent;
        }

        private void HandleTravelEvent(string message)
        {
            SetResultText(message);
        }

        private void Start()
        {
            RefreshList();
        }

        /// <summary>퀘스트 수락 시 호출됨. 이 퀘스트에 보낼 용병을 고르는 선택 모드로 전환한다.</summary>
        public void BeginDispatch(Quest quest)
        {
            _pendingQuest = quest;
            _selectedForDispatch.Clear();
            RenderResultText(string.Empty);
            RefreshList();
        }

        private void RefreshList()
        {
            foreach (var row in _rows)
                Destroy(row);
            _rows.Clear();

            var expeditions = ExpeditionLog.Instance.Active;
            if (expeditions.Count > 0)
            {
                _rows.Add(CreateSectionHeader("진행 중인 파견"));
                foreach (var expedition in expeditions)
                    _rows.Add(CreateExpeditionRow(expedition));
            }

            if (_pendingQuest != null)
                _rows.Add(CreateSectionHeader($"파견 편성 — {_pendingQuest.Title} ({_selectedForDispatch.Count}/{_pendingQuest.MaxDispatchSize}명)"));

            _rows.Add(CreateSectionHeader("보유 용병"));
            foreach (var merc in PlayerParty.Instance.Members)
                _rows.Add(CreateMemberRow(merc));

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
            RefreshDispatchButton();
        }

        private GameObject CreateSectionHeader(string label)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(listContainer, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = 26;
            layout.preferredHeight = 26;

            var text = CreateText(go.transform, label, 15, TextAnchor.MiddleLeft);
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 1f, 1f, 0.7f);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = Vector2.zero;

            return go;
        }

        private GameObject CreateExpeditionRow(Expedition expedition)
        {
            var quest = expedition.Quest;

            var row = new GameObject(quest.Title, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);
            row.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.25f, 0.4f);

            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = 56;
            layoutElement.preferredHeight = 56;

            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(12, 12, 6, 6);
            hLayout.spacing = 8;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            string members = string.Join(", ", expedition.Members.Select(m => m.IsAlive ? m.Name : $"{m.Name}(사망)"));
            string status = expedition.IsReady ? "도착 완료" : $"이동 중 (남은 {quest.RemainingDays}일)";
            string info = $"{quest.Title}    {status}    파견: {members}";
            var infoText = CreateText(row.transform, info, 15, TextAnchor.MiddleLeft);
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 100;

            bool anyWounded = expedition.Members.Any(m => m.IsAlive && m.CurrentHealth < m.CurrentStats.MaxHealth);

            var restButtonGO = new GameObject("RestButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            restButtonGO.transform.SetParent(row.transform, false);
            restButtonGO.GetComponent<Image>().color = anyWounded ? new Color(0.3f, 0.45f, 0.5f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            var restLayout = restButtonGO.GetComponent<LayoutElement>();
            restLayout.minWidth = 90;
            restLayout.minHeight = 40;
            restLayout.flexibleWidth = 0;

            var restText = CreateText(restButtonGO.transform, "휴식(+1일)", 14, TextAnchor.MiddleCenter);
            var restTextRect = restText.GetComponent<RectTransform>();
            restTextRect.anchorMin = Vector2.zero;
            restTextRect.anchorMax = Vector2.one;
            restTextRect.offsetMin = Vector2.zero;
            restTextRect.offsetMax = Vector2.zero;

            var restButton = restButtonGO.GetComponent<Button>();
            restButton.interactable = anyWounded;
            restButton.onClick.AddListener(() => ExpeditionLog.Instance.Rest(expedition));

            bool canFight = expedition.IsReady && _battlingExpedition == null;

            var buttonGO = new GameObject("BattleButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(row.transform, false);
            buttonGO.GetComponent<Image>().color = canFight ? new Color(0.25f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            var btnLayout = buttonGO.GetComponent<LayoutElement>();
            btnLayout.minWidth = 90;
            btnLayout.minHeight = 40;
            btnLayout.flexibleWidth = 0;

            var btnText = CreateText(buttonGO.transform, "전투 시작", 15, TextAnchor.MiddleCenter);
            var btnTextRect = btnText.GetComponent<RectTransform>();
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            btnTextRect.offsetMin = Vector2.zero;
            btnTextRect.offsetMax = Vector2.zero;

            var button = buttonGO.GetComponent<Button>();
            button.interactable = canFight;
            button.onClick.AddListener(() => StartExpeditionBattle(expedition));

            return row;
        }

        private GameObject CreateMemberRow(Mercenary merc)
        {
            bool onExpedition = ExpeditionLog.Instance.IsOnExpedition(merc);
            bool selected = _selectedForDispatch.Contains(merc.Id);

            var row = new GameObject(merc.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);

            Color rowColor = onExpedition ? new Color(0f, 0f, 0f, 0.25f)
                : selected ? new Color(0.25f, 0.4f, 0.3f, 0.5f)
                : new Color(1f, 1f, 1f, 0.06f);
            row.GetComponent<Image>().color = rowColor;

            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = 56;
            layoutElement.preferredHeight = 56;

            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(12, 12, 8, 8);
            hLayout.spacing = 8;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);

            var stats = merc.CurrentStats;
            var personalityMod = PersonalityTable.Get(merc.Personality);
            var passives = ClassPassiveFactory.Create(merc.Class.Kind, merc.HasRarePassive);
            string rareMark = merc.HasRarePassive ? " (레어)" : "";
            Color textColor = onExpedition ? new Color(1f, 1f, 1f, 0.4f) : Color.white;

            var infoColumnGO = new GameObject("InfoColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            infoColumnGO.transform.SetParent(row.transform, false);
            var infoColumnLayout = infoColumnGO.GetComponent<LayoutElement>();
            infoColumnLayout.flexibleWidth = 1;
            infoColumnLayout.minWidth = 90;
            var infoColumnVLayout = infoColumnGO.GetComponent<VerticalLayoutGroup>();
            infoColumnVLayout.spacing = 2;
            infoColumnVLayout.childAlignment = TextAnchor.MiddleLeft;
            infoColumnVLayout.childForceExpandWidth = true;
            infoColumnVLayout.childForceExpandHeight = false;
            infoColumnVLayout.childControlWidth = true;
            infoColumnVLayout.childControlHeight = true;

            var topRowGO = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            topRowGO.transform.SetParent(infoColumnGO.transform, false);
            var topRowLayout = topRowGO.GetComponent<HorizontalLayoutGroup>();
            topRowLayout.spacing = 4;
            topRowLayout.childAlignment = TextAnchor.MiddleLeft;
            topRowLayout.childForceExpandWidth = false;
            topRowLayout.childForceExpandHeight = true;
            topRowLayout.childControlWidth = true;
            topRowLayout.childControlHeight = true;

            string nameInfo = $"{merc.Name} {merc.Class.ClassName} Lv.{merc.Level}";
            var nameText = CreateText(topRowGO.transform, nameInfo, 14, TextAnchor.MiddleLeft);
            nameText.color = textColor;
            var nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1;

            CreateTaggedLabel(topRowGO.transform, $"[{personalityMod.Label}]", 14, personalityMod.Description, textColor);

            if (passives.Count > 0)
                CreateTaggedLabel(topRowGO.transform, $"<{passives[0].Name}{rareMark}>", 14, passives[0].Description, textColor);

            string statsInfo = $"공{stats.Attack} 방{stats.Defense} 체{merc.CurrentHealth}/{stats.MaxHealth} 속{stats.MoveSpeed}";
            if (onExpedition)
                statsInfo += $"    (파견 중: {ExpeditionLog.Instance.FindQuest(merc)?.Title})";
            var statsText = CreateText(infoColumnGO.transform, statsInfo, 14, TextAnchor.MiddleLeft);
            statsText.color = textColor;

            if (_pendingQuest != null && !onExpedition)
            {
                bool atCap = !selected && _selectedForDispatch.Count >= _pendingQuest.MaxDispatchSize;
                CreateActionButton(row.transform, selected ? "선택됨" : "선택",
                    selected ? new Color(0.3f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.35f, 1f),
                    () => ToggleSelection(merc), interactable: !atCap);
            }

            if (!onExpedition)
            {
                CreateActionButton(row.transform, "해고", new Color(0.55f, 0.25f, 0.25f, 1f),
                    () => PlayerParty.Instance.Remove(merc));
            }

            return row;
        }

        private void ToggleSelection(Mercenary merc)
        {
            if (_selectedForDispatch.Contains(merc.Id))
            {
                _selectedForDispatch.Remove(merc.Id);
            }
            else
            {
                if (_pendingQuest != null && _selectedForDispatch.Count >= _pendingQuest.MaxDispatchSize)
                    return;
                _selectedForDispatch.Add(merc.Id);
            }
            RefreshList();
        }

        private void RefreshDispatchButton()
        {
            if (_pendingQuest == null)
            {
                if (_dispatchButtonGO != null)
                {
                    Destroy(_dispatchButtonGO);
                    _dispatchButtonGO = null;
                }
                return;
            }

            if (_dispatchButtonGO == null)
                _dispatchButtonGO = CreateDispatchButton(_panelTransform);

            _dispatchButtonGO.GetComponent<Button>().interactable = _selectedForDispatch.Count > 0;
        }

        private GameObject CreateDispatchButton(Transform parent)
        {
            var buttonGO = new GameObject("DispatchButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-44f, -51f);
            rect.sizeDelta = new Vector2(140f, 44f);

            buttonGO.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.35f, 1f);

            var text = CreateText(buttonGO.transform, "파견 시작", 18, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            buttonGO.GetComponent<Button>().onClick.AddListener(ConfirmDispatch);
            return buttonGO;
        }

        private void ConfirmDispatch()
        {
            if (_pendingQuest == null || _selectedForDispatch.Count == 0)
                return;

            var members = PlayerParty.Instance.Members.Where(m => _selectedForDispatch.Contains(m.Id)).ToList();
            ExpeditionLog.Instance.Dispatch(_pendingQuest, members);

            _pendingQuest = null;
            _selectedForDispatch.Clear();
            RefreshList();
        }

        private void StartExpeditionBattle(Expedition expedition)
        {
            if (_battlingExpedition != null || !expedition.IsReady)
                return;

            var combatants = expedition.Members.Where(m => m.IsAlive).Select(m => m.ToCombatant()).ToList();
            var quest = expedition.Quest;
            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(quest.Region, quest.EnemyCount, quest.Difficulty, rng);

            var simulator = new AutoBattleSimulator();
            var result = simulator.Simulate(combatants, enemies);

            _battlingExpedition = expedition;
            RefreshList();

            var host = CoroutineHost.Instance;
            _battlePlayback = host.StartCoroutine(PlayBattle(expedition, result));
        }

        private IEnumerator PlayBattle(Expedition expedition, BattleResult result)
        {
            const int MaxVisibleLines = 8;
            var lines = new LinkedList<string>();

            void AppendLine(string line)
            {
                lines.AddLast(line);
                while (lines.Count > MaxVisibleLines)
                    lines.RemoveFirst();
                RenderResultText(string.Join("\n", lines));
            }

            AppendLine($"[{expedition.Quest.Title}] 전투 진행 중...");

            int eventCount = Mathf.Max(1, result.Log.Count);
            float delayPerEvent = expedition.Quest.EstimatedDurationSeconds / eventCount;

            foreach (var evt in result.Log)
            {
                string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
                AppendLine($"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage}{suffix}");
                yield return new WaitForSeconds(delayPerEvent);
            }

            string questOutcome = result.Outcome == BattleOutcome.TeamAVictory ? "임무 완료" : "임무 실패";
            SetResultText($"{questOutcome}\n{BuildResultSummary(result)}");

            _battlePlayback = null;
            _battlingExpedition = null;
            ExpeditionLog.Instance.Complete(expedition);
        }

        private string BuildResultSummary(BattleResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"결과: {result.Outcome} ({result.Rounds}라운드)");

            foreach (var evt in result.Log.TakeLast(10))
            {
                string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
                sb.AppendLine($"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage}{suffix}");
            }

            return sb.ToString();
        }

        private void RenderResultText(string text)
        {
            if (resultText != null)
                resultText.text = text;
        }

        private void SetResultText(string text)
        {
            RenderResultText(text);
            Debug.Log(text);
        }

        private void CreateTaggedLabel(Transform parent, string label, int fontSize, string tooltip, Color color)
        {
            var text = CreateText(parent, label, fontSize, TextAnchor.MiddleLeft);
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 0;
            var trigger = text.gameObject.AddComponent<TooltipTrigger>();
            trigger.Text = tooltip;
        }

        private void CreateActionButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, bool interactable = true)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(parent, false);
            buttonGO.GetComponent<Image>().color = color;
            var layout = buttonGO.GetComponent<LayoutElement>();
            layout.minWidth = 64;
            layout.minHeight = 36;
            layout.flexibleWidth = 0;

            var buttonText = CreateText(buttonGO.transform, label, 15, TextAnchor.MiddleCenter);
            var buttonTextRect = buttonText.GetComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            var button = buttonGO.GetComponent<Button>();
            button.interactable = interactable;
            button.onClick.AddListener(onClick);
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
