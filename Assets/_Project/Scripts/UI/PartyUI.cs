using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Combat;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Traits;
using GN3.World;
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
        private Text _dispatchButtonText;
        private bool _lastNight;
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
            TrainingHall.OnChanged += RefreshList;
            RestVenues.OnAnyChanged += RefreshList;
            Hospital.OnChanged += RefreshList;
            ExpeditionLog.Instance.OnTravelEvent += HandleTravelEvent;
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshList;
            ExpeditionLog.Instance.OnChanged -= RefreshList;
            TrainingHall.OnChanged -= RefreshList;
            RestVenues.OnAnyChanged -= RefreshList;
            Hospital.OnChanged -= RefreshList;
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

        // 시간이 저절로 흐르므로, 편성 중에 밤/낮이 바뀌면 파견 버튼을 잠그거나 푼다.
        private void Update()
        {
            if (_pendingQuest == null) return;
            bool night = GameClock.IsNight;
            if (night == _lastNight) return;
            _lastNight = night;
            RefreshDispatchButton();
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
            {
                var selected = PlayerParty.Instance.Members.Where(m => _selectedForDispatch.Contains(m.Id));
                int teamPower = QuestDifficulty.TeamPower(selected);
                string power = $"{QuestDifficulty.DescribeTeam(_pendingQuest, teamPower)} · {QuestDifficulty.DurationText(_pendingQuest, teamPower)}";
                _rows.Add(CreateSectionHeader($"파견 편성 — {QuestDifficulty.RichTitle(_pendingQuest)} ({_selectedForDispatch.Count}/{_pendingQuest.MaxDispatchSize}명)  {power}"));
            }

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
            row.GetComponent<Image>().color = expedition.IsReturning ? new Color(0.2f, 0.28f, 0.4f, 0.4f) : new Color(0.2f, 0.35f, 0.25f, 0.4f);

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
            string status = expedition.IsReturning ? $"귀환 중 (남은 {expedition.ReturnDaysLeft}일)"
                : expedition.IsReady ? "도착 완료" : $"이동 중 (남은 {quest.RemainingDays}일)";
            string info = $"{QuestDifficulty.RichTitle(quest)}    {status}    파견: {members}    {QuestDifficulty.DescribeTeam(quest, QuestDifficulty.TeamPower(expedition.Members))}";
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
            button.onClick.AddListener(() => StartBattle(expedition));

            return row;
        }

        private GameObject CreateMemberRow(Mercenary merc)
        {
            bool training = TrainingHall.IsTraining(merc);
            bool hospitalized = Hospital.IsAdmitted(merc);
            var venue = RestVenues.Find(merc);
            bool bathing = venue != null;
            bool onExpedition = ExpeditionLog.Instance.IsOnExpedition(merc) || training || hospitalized || bathing; // 훈련·입원·쉼터(온천·도박장) 중도 마을에 없음(선택·해고 불가)
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

            // 시장과 같은 형태: [등급] 이름 · 직업 Lv · 전투력 (+ 꼭 필요한 상태만). 성격·패시브·능력치는 줄을 클릭해 캐릭터 창에서 본다.
            var stats = merc.CurrentStats;
            Color textColor = onExpedition ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

            var nameText = CreateText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}", 17, TextAnchor.MiddleLeft);
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = textColor;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.gameObject.AddComponent<LayoutElement>().minWidth = 150;

            var classText = CreateText(row.transform, $"{merc.Class.ClassName}  Lv.{merc.Level}", 15, TextAnchor.MiddleLeft);
            classText.color = textColor;
            classText.horizontalOverflow = HorizontalWrapMode.Overflow;
            classText.gameObject.AddComponent<LayoutElement>().minWidth = 100;

            string status = training ? "  · 훈련 중" : hospitalized ? "  · 입원 중" : bathing ? $"  · {venue.Name} 중" : onExpedition ? "  · 파견 중"
                : merc.CurrentHealth < stats.MaxHealth ? $"  · 체력 {merc.CurrentHealth}/{stats.MaxHealth}" : "";
            // 피로·사기: 지치면 파견 불가, 피로 50↑·사기 30↓은 능력치가 깎이니 눈에 띄게
            if (merc.IsExhausted) status += $"  · 지침(피로 {merc.Fatigue})";
            else if (merc.Fatigue >= Mercenary.TiredFatigue) status += $"  · 피로 {merc.Fatigue}";
            if (merc.Morale < Mercenary.LowMorale) status += "  · 사기 낮음";
            foreach (var ailment in merc.Ailments) status += $"  · {ailment.Def.displayName}"; // 부상·질병(중하면 파견 불가)
            var powerText = CreateText(row.transform, $"전투력 {merc.CombatPower}<color=#a3937c>{status}</color>", 15, TextAnchor.MiddleLeft);
            powerText.color = onExpedition ? new Color(UITheme.TitleText.r, UITheme.TitleText.g, UITheme.TitleText.b, 0.5f) : UITheme.TitleText;
            powerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var powerLayout = powerText.gameObject.AddComponent<LayoutElement>();
            powerLayout.minWidth = 100;
            powerLayout.flexibleWidth = 1; // 남는 폭을 차지해 버튼을 오른쪽 끝으로 민다
            string ailmentTip = Ailments.EffectTooltip(merc); // 부상·질병: 떨어진 능력치는 마우스를 올리면
            if (ailmentTip != null)
            {
                powerText.raycastTarget = true; // 클릭은 줄(RowClickHandler)로 올라간다
                powerText.gameObject.AddComponent<TooltipTrigger>().Text = ailmentTip;
            }

            RowClickHandler.Attach(row, () => MercenaryInfoPanel.ShowGlobal(merc));

            if (_pendingQuest != null && !onExpedition)
            {
                bool atCap = !selected && _selectedForDispatch.Count >= _pendingQuest.MaxDispatchSize;
                CreateActionButton(row.transform, selected ? "선택됨" : "선택",
                    selected ? new Color(0.3f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.35f, 1f),
                    () => ToggleSelection(merc), interactable: !atCap && (selected || (!merc.IsExhausted && !merc.IsSeverelyAiling)));
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

            // 밤(22시~5시)에는 출전할 수 없다. 편성(용병 고르기)은 그대로 된다.
            bool night = GameClock.IsNight;
            _lastNight = night;
            _dispatchButtonGO.GetComponent<Button>().interactable = _selectedForDispatch.Count > 0 && !night;
            if (_dispatchButtonText != null)
            {
                _dispatchButtonText.text = night ? "밤에는 출전 불가\n(오전 5시부터)" : "파견 시작";
                _dispatchButtonText.fontSize = night ? 13 : 18;
            }
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
            _dispatchButtonText = text;
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
            if (GameClock.IsNight)
            {
                ToastLog.Show("밤에는 출전할 수 없습니다. 오전 5시부터 가능합니다.");
                return;
            }

            var members = PlayerParty.Instance.Members.Where(m => _selectedForDispatch.Contains(m.Id)).ToList();
            var exhausted = members.Where(m => m.IsExhausted).Select(m => m.Name).ToList();
            if (exhausted.Count > 0)
            {
                ToastLog.Show($"지친 용병은 출전할 수 없습니다: {string.Join(", ", exhausted)} (마을에서 쉬면 회복)");
                return;
            }
            var ailing = members.Where(m => m.IsSeverelyAiling).Select(m => $"{m.Name}({m.AilmentSummary()})").ToList();
            if (ailing.Count > 0)
            {
                ToastLog.Show($"크게 다치거나 앓는 용병은 출전할 수 없습니다: {string.Join(", ", ailing)} (병원에서 치료)");
                return;
            }
            ExpeditionLog.Instance.Dispatch(_pendingQuest, members);

            _pendingQuest = null;
            _selectedForDispatch.Clear();
            RefreshList();
        }

        private readonly Queue<Expedition> _battleQueue = new Queue<Expedition>();

        /// <summary>
        /// 도착한 파견의 전투를 로그와 함께 재생하고 결과를 반영한다(ExpeditionBattle). 다른 전투가 재생 중이면
        /// 끝난 뒤 이어서 시작한다(도착 알림창에서 여러 개를 "전투 진행"으로 고른 경우).
        /// </summary>
        public void StartBattle(Expedition expedition)
        {
            if (expedition == null || !expedition.IsReady || !ExpeditionLog.Instance.Active.Contains(expedition)) return;
            if (_battlingExpedition != null)
            {
                if (_battlingExpedition != expedition && !_battleQueue.Contains(expedition)) _battleQueue.Enqueue(expedition);
                return;
            }

            var battle = ExpeditionBattle.Simulate(expedition);
            _battlingExpedition = expedition;
            RefreshList();

            var host = CoroutineHost.Instance;
            _battlePlayback = host.StartCoroutine(PlayBattle(expedition, battle));
        }

        private IEnumerator PlayBattle(Expedition expedition, ExpeditionBattle.Battle battle)
        {
            const int MaxVisibleLines = 8;
            var lines = new LinkedList<string>();
            var result = battle.Result;

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

            _battlePlayback = null;
            _battlingExpedition = null;
            // 체력 반영·전사·보상·알림·귀환은 ExpeditionBattle이 맡는다(자동 진행과 같은 처리).
            string summary = ExpeditionBattle.Conclude(expedition, battle, alertShown: true); // 결과는 이 패널에 바로 보인다
            SetResultText($"{summary}\n{BuildResultSummary(result)}");

            while (_battleQueue.Count > 0)
            {
                var next = _battleQueue.Dequeue();
                if (next.IsReady && ExpeditionLog.Instance.Active.Contains(next))
                {
                    StartBattle(next);
                    break;
                }
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
