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

        private readonly List<GameObject> _memberRows = new List<GameObject>();
        private Font _font;
        private Transform _panelTransform;
        private GameObject _battleButtonGO;
        private Quest _activeQuest;
        private Coroutine _battlePlayback;

        private void Awake()
        {
            // ScrollListWrapper가 listContainer를 새 계층으로 옮기기 전에, 원래 부모(PartyPanel)를 기억해둔다.
            _panelTransform = listContainer.parent;

            // PartyPanel(640x780) 안에서 제목 아래 ~ 결과 텍스트 위까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 170f), new Vector2(-20f, -105f));

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            PlayerParty.Instance.OnChanged += RefreshList;
        }

        /// <summary>퀘스트 수락 시 호출됨. 이 퀘스트를 대상으로 하는 전투 시작 버튼을 활성화한다.</summary>
        public void ActivateQuest(Quest quest)
        {
            _activeQuest = quest;
            if (_battleButtonGO == null)
                _battleButtonGO = CreateBattleButton(_panelTransform);
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshList;
        }

        private void Start()
        {
            RefreshList();
        }

        private void RefreshList()
        {
            foreach (var row in _memberRows)
                Destroy(row);
            _memberRows.Clear();

            foreach (var merc in PlayerParty.Instance.Members)
                _memberRows.Add(CreateMemberRow(merc));

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
        }

        private GameObject CreateMemberRow(Mercenary merc)
        {
            bool active = PlayerParty.Instance.IsActive(merc);

            var row = new GameObject(merc.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);

            row.GetComponent<Image>().color = active ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.15f);

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
            Color textColor = active ? Color.white : new Color(1f, 1f, 1f, 0.4f);

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

            string statsInfo = $"공{stats.Attack} 방{stats.Defense} 체{stats.MaxHealth} 속{stats.MoveSpeed}";
            var statsText = CreateText(infoColumnGO.transform, statsInfo, 14, TextAnchor.MiddleLeft);
            statsText.color = textColor;

            CreateActionButton(row.transform, active ? "제외" : "참가",
                active ? new Color(0.5f, 0.45f, 0.2f, 1f) : new Color(0.25f, 0.5f, 0.3f, 1f),
                () => PlayerParty.Instance.SetActive(merc, !PlayerParty.Instance.IsActive(merc)));

            CreateActionButton(row.transform, "해고", new Color(0.55f, 0.25f, 0.25f, 1f),
                () => PlayerParty.Instance.Remove(merc));

            return row;
        }

        private void CreateActionButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
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

            buttonGO.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private GameObject CreateBattleButton(Transform parent)
        {
            var buttonGO = new GameObject("BattleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-44f, -51f);
            rect.sizeDelta = new Vector2(140f, 44f);

            buttonGO.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.35f, 1f);

            var text = CreateText(buttonGO.transform, "전투 시작", 18, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            buttonGO.GetComponent<Button>().onClick.AddListener(StartBattle);
            return buttonGO;
        }

        private void StartBattle()
        {
            if (_activeQuest == null)
            {
                SetResultText("진행 중인 퀘스트가 없습니다. 퀘스트를 수락하면 전투를 시작할 수 있습니다.");
                return;
            }

            var party = PlayerParty.Instance.ToCombatants();
            if (party.Count == 0)
            {
                SetResultText("전투에 참가하는 용병이 없습니다. '참가' 버튼으로 용병을 전투에 포함시키세요.");
                return;
            }

            var quest = _activeQuest;
            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(quest.EnemyCount, quest.Difficulty, rng);

            var simulator = new AutoBattleSimulator();
            var result = simulator.Simulate(party, enemies);

            if (_battleButtonGO != null)
            {
                var button = _battleButtonGO.GetComponent<Button>();
                if (button != null)
                    button.interactable = false;
            }

            var host = CoroutineHost.Instance;
            if (_battlePlayback != null)
                host.StopCoroutine(_battlePlayback);
            _battlePlayback = host.StartCoroutine(PlayBattle(quest, result));
        }

        private IEnumerator PlayBattle(Quest quest, BattleResult result)
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

            AppendLine("전투 진행 중...");

            int eventCount = Mathf.Max(1, result.Log.Count);
            float delayPerEvent = quest.EstimatedDurationSeconds / eventCount;

            foreach (var evt in result.Log)
            {
                string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
                AppendLine($"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage}{suffix}");
                yield return new WaitForSeconds(delayPerEvent);
            }

            string questOutcome = result.Outcome == BattleOutcome.TeamAVictory ? "임무 완료" : "임무 실패";
            SetResultText($"{questOutcome}\n{BuildResultSummary(result)}");

            _battlePlayback = null;
            EndQuest();
        }

        private void EndQuest()
        {
            _activeQuest = null;
            if (_battleButtonGO != null)
            {
                Destroy(_battleButtonGO);
                _battleButtonGO = null;
            }
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
