using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Characters;
using GN3.Combat;
using GN3.Mercenaries;
using GN3.Quests;
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

        private void Awake()
        {
            // ScrollListWrapper가 listContainer를 새 계층으로 옮기기 전에, 원래 부모(PartyPanel)를 기억해둔다.
            _panelTransform = listContainer.parent;

            // PartyPanel(480x680) 안에서 제목 아래 ~ 결과 텍스트 위까지의 고정 영역
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

            CreatePortrait(row.transform, merc.Appearance, 40);

            var stats = merc.CurrentStats;
            string info = $"{merc.Name} {merc.Class.ClassName} Lv.{merc.Level}\n공{stats.Attack} 방{stats.Defense} 체{stats.MaxHealth}";
            var infoText = CreateText(row.transform, info, 14, TextAnchor.MiddleLeft);
            infoText.color = active ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 90;

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

            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(_activeQuest.EnemyCount, _activeQuest.Difficulty, rng);

            var simulator = new AutoBattleSimulator();
            var result = simulator.Simulate(party, enemies);

            string questOutcome = result.Outcome == BattleOutcome.TeamAVictory ? "임무 완료" : "임무 실패";
            SetResultText($"{questOutcome}\n{BuildResultSummary(result)}");

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
                string suffix = evt.TargetDefeated ? " (처치)" : "";
                sb.AppendLine($"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage}{suffix}");
            }

            return sb.ToString();
        }

        private void SetResultText(string text)
        {
            if (resultText != null)
                resultText.text = text;
            Debug.Log(text);
        }

        private void CreatePortrait(Transform parent, CharacterAppearance appearance, int size)
        {
            var portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(LayoutElement));
            portraitGO.transform.SetParent(parent, false);

            var layout = portraitGO.GetComponent<LayoutElement>();
            layout.minWidth = size;
            layout.minHeight = size;
            layout.preferredWidth = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0;

            var background = AddPortraitLayer(portraitGO.transform, null);
            background.color = new Color(1f, 1f, 1f, 0.08f);

            // 뒤에서 앞으로 겹쳐 그림: 몸통 -> 다리 -> 팔 -> 무기 -> 머리
            AddPortraitLayer(portraitGO.transform, appearance.Body);
            AddPortraitLayer(portraitGO.transform, appearance.Leg);
            AddPortraitLayer(portraitGO.transform, appearance.Arm);
            AddPortraitLayer(portraitGO.transform, appearance.Weapon);
            AddPortraitLayer(portraitGO.transform, appearance.Head);
        }

        private Image AddPortraitLayer(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Part", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            return image;
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
