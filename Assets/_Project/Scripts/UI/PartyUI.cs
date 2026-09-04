using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Combat;
using GN3.Mercenaries;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public class PartyUI : MonoBehaviour
    {
        [SerializeField] private Transform listContainer;
        [SerializeField] private Button battleButton;
        [SerializeField] private Text resultText;
        [SerializeField] private int enemyCount = 3;
        [SerializeField] private int enemyDifficulty = 1;

        private readonly List<GameObject> _memberRows = new List<GameObject>();
        private Font _font;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (battleButton != null)
                battleButton.onClick.AddListener(StartBattle);

            PlayerParty.Instance.OnChanged += RefreshList;
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

        private void StartBattle()
        {
            var party = PlayerParty.Instance.ToCombatants();
            if (party.Count == 0)
            {
                SetResultText("전투에 참가하는 용병이 없습니다. '참가' 버튼으로 용병을 전투에 포함시키세요.");
                return;
            }

            var rng = new System.Random();
            var enemies = EnemySquadGenerator.Generate(enemyCount, enemyDifficulty, rng);

            var simulator = new AutoBattleSimulator();
            var result = simulator.Simulate(party, enemies);

            SetResultText(BuildResultSummary(result));
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
