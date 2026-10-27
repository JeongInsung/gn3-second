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
        // 용병 줄과 그 위치·상태 글자. 글자가 줄바꿈돼 넘치면 RefreshList가 줄 높이를 늘린다.
        private readonly List<(LayoutElement layout, Text text)> _wrappedRows = new List<(LayoutElement, Text)>();
        private const int MemberRowPadding = 8;
        private readonly HashSet<string> _selectedForDispatch = new HashSet<string>();
        // 분대별 분대장(직접 진행 전투에서 플레이어가 조종). 분대에 처음 들어간 사람이 맡고 '분대장' 버튼으로 바꾼다.
        // 두 분대 퀘스트(Lv5 앤트킹)만 2분대를 쓴다.
        private readonly string[] _leaderIds = new string[2];
        private readonly Dictionary<string, int> _squadOf = new Dictionary<string, int>(); // 고른 용병 id → 분대(0·1)
        private int SquadCount => _pendingQuest?.SquadCount ?? 1;
        private int SquadMembers(int squad) => _selectedForDispatch.Count(id => _squadOf.TryGetValue(id, out int s) && s == squad);

        /// <summary>출발할 수 있는 편성인지. 안 되면 이유(버튼·토스트에 쓴다).</summary>
        private string DispatchBlockReason()
        {
            if (_pendingQuest == null || _selectedForDispatch.Count == 0) return "용병을 고르세요";
            int need = _pendingQuest.RequiredDispatchSize;
            if (need > 0 && _selectedForDispatch.Count != need) return $"정확히 {need}명 필요";
            if (SquadCount > 1)
                for (int s = 0; s < SquadCount; s++)
                    if (SquadMembers(s) != _pendingQuest.SquadSize) return $"분대마다 {_pendingQuest.SquadSize}명 필요";
            return null;
        }
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
            UITheme.StyleWindowTitle(_panelTransform.Find("Title")?.GetComponent<Text>()); // 시장·퀘스트·파티 제목 줄을 같게

            // PartyPanel(640x780) 안에서 제목 아래 ~ 결과 텍스트 위까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, 170f), new Vector2(-20f, -105f));

            _font = UITheme.Font;
            if (resultText != null && resultText.text.StartsWith("전투 결과가")) resultText.text = ""; // 빈 안내 문구는 지운다(결과가 생기면 채워진다)

            PlayerParty.Instance.OnChanged += RefreshList;
            ExpeditionLog.Instance.OnChanged += RefreshList;
            ExpeditionLog.Instance.OnProgress += RefreshList; // 남은 시간(게임 1시간마다)
            TrainingHall.OnChanged += RefreshList;
            RestVenues.OnAnyChanged += RefreshList;
            Hospital.OnChanged += RefreshList;
            Contracts.OnChanged += RefreshList; // 재계약·만료 표시
            ExpeditionLog.Instance.OnTravelEvent += HandleTravelEvent;
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshList;
            ExpeditionLog.Instance.OnChanged -= RefreshList;
            ExpeditionLog.Instance.OnProgress -= RefreshList;
            TrainingHall.OnChanged -= RefreshList;
            RestVenues.OnAnyChanged -= RefreshList;
            Hospital.OnChanged -= RefreshList;
            Contracts.OnChanged -= RefreshList;
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
            _squadOf.Clear();
            _leaderIds[0] = _leaderIds[1] = null;
            // 연합 의뢰: 그 길드 용병이 손님으로 합류해 미리 뽑혀 있다(두 분대면 2분대). 빼거나 다른 의뢰에 보낼 수 없다.
            if (quest != null && quest.IsJoint)
                foreach (var guest in Loans.JoinQuest(quest))
                {
                    int squad = SquadCount > 1 ? 1 : 0;
                    _selectedForDispatch.Add(guest.Id);
                    _squadOf[guest.Id] = squad;
                    if (_leaderIds[squad] == null) _leaderIds[squad] = guest.Id;
                }
            RenderResultText(string.Empty);
            RefreshList();
        }

        private bool IsLockedGuest(Mercenary merc) => _pendingQuest != null && Loans.GuestLoan(merc) is Loan loan && loan.Joint && loan.QuestId == _pendingQuest.Id;

        private void RefreshList()
        {
            foreach (var row in _rows)
                Destroy(row);
            _rows.Clear();
            _wrappedRows.Clear();

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
                string power = $"{QuestDifficulty.DescribeTeam(_pendingQuest, teamPower)} · {QuestDifficulty.DurationText(_pendingQuest, teamPower)}"
                    + $" · 가방 {BagCatalog.CapacityOf(selected)}칸"; // 전리품을 들고 올 칸(BagCatalog)
                if (SquadCount > 1)
                {
                    for (int s = 0; s < SquadCount; s++)
                    {
                        var squadLeader = selected.FirstOrDefault(m => m.Id == _leaderIds[s]);
                        power += $" · {s + 1}분대 {SquadMembers(s)}/{_pendingQuest.SquadSize}" + (squadLeader != null ? $" ★{squadLeader.Name}" : "");
                    }
                }
                else
                {
                    var leader = selected.FirstOrDefault(m => m.Id == _leaderIds[0]);
                    if (leader != null) power += $" · 분대장 {leader.Name}";
                }
                string size = _pendingQuest.RequiredDispatchSize > 0
                    ? $"{_selectedForDispatch.Count}/{_pendingQuest.RequiredDispatchSize}명 — 정확히 {_pendingQuest.RequiredDispatchSize}명" + (SquadCount > 1 ? $", {_pendingQuest.SquadSize}명씩 두 분대" : "")
                    : $"{_selectedForDispatch.Count}/{_pendingQuest.MaxDispatchSize}명";
                // 한 줄에 다 넣으면 창 폭을 넘어 난이도·소요가 잘린다 → 두 줄
                _rows.Add(CreateSectionHeader($"파견 편성 — {QuestDifficulty.RichTitle(_pendingQuest)} ({size})\n{power}", 2));
            }

            _rows.Add(CreateSectionHeader("보유 용병"));
            foreach (var merc in PlayerParty.Instance.Members)
                _rows.Add(CreateMemberRow(merc));

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
            // 위치·상태 글자가 줄바꿈돼 줄 안에 안 들어가면 줄을 늘리고 다시 배치한다
            bool grew = false;
            foreach (var (layout, text) in _wrappedRows)
            {
                float needed = text.preferredHeight + MemberRowPadding * 2;
                if (needed > layout.preferredHeight)
                {
                    layout.minHeight = layout.preferredHeight = Mathf.Ceil(needed);
                    grew = true;
                }
            }
            if (grew) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
            RefreshDispatchButton();
        }

        /// <summary>굵은 머리줄. lines는 label의 줄 수(높이를 맞춘다).</summary>
        private GameObject CreateSectionHeader(string label, int lines = 1)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(listContainer, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = 26 + 18 * (lines - 1);
            layout.preferredHeight = layout.minHeight;

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

            // ★ = 분대장. 두 분대 퀘스트는 "1분대: … / 2분대: …"
            string Names(IEnumerable<Mercenary> list) => string.Join(", ", list.Select(m =>
                !m.IsAlive ? $"{m.Name}(사망)" : m == expedition.LeaderOf(expedition.SquadOf(m)) ? $"★{m.Name}" : m.Name));
            string members = quest.SquadCount > 1
                ? string.Join(" / ", Enumerable.Range(0, quest.SquadCount).Select(s => $"{s + 1}분대: {Names(expedition.Members.Where(m => expedition.SquadOf(m) == s))}"))
                : Names(expedition.Members);
            string status = expedition.IsReturning ? $"귀환 중 (남은 {GameClock.FormatDuration(expedition.ReturnHoursLeft)})"
                : expedition.IsReady ? "도착 완료" : $"이동 중 (남은 {GameClock.FormatDuration(quest.RemainingHours)})";
            if (expedition.AwaitingEvent) status = "<color=#f0a54a>사건 선택 대기</color> · " + status; // 우편함·알림창에서 고를 때까지 멈춤
            string info = $"{QuestDifficulty.RichTitle(quest)}    {status}    파견: {members}    {QuestDifficulty.DescribeTeam(quest, QuestDifficulty.TeamPower(expedition.Members))}";
            var infoText = CreateText(row.transform, info, 15, TextAnchor.MiddleLeft);
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 100;
            infoLayout.preferredWidth = 0; // 버튼 앞까지만 쓰고 줄을 바꾼다(넘치면 RefreshList가 줄 높이를 늘림)
            infoText.verticalOverflow = VerticalWrapMode.Overflow;
            _wrappedRows.Add((layoutElement, infoText));

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
                : new Color(1f, 1f, 1f, 0.06f); // 파견에 고른 줄은 바탕색 대신 흰 테두리(다른 창과 같은 표시)
            row.GetComponent<Image>().color = rowColor;
            if (selected && !onExpedition) UITheme.CreateSelectFrame(row.transform);

            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = 56;
            layoutElement.preferredHeight = 56;

            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(12, 12, MemberRowPadding, MemberRowPadding);
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

            string guestTag = Loans.GuestTag(merc) is string tag ? $" <size=13><color=#6ab8ff>({tag})</color></size>" : ""; // 근처 길드에서 빌려 온 손님
            var nameText = CreateText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}{guestTag}", 17, TextAnchor.MiddleLeft);
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = textColor;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.gameObject.AddComponent<LayoutElement>().minWidth = 150;

            var classText = CreateText(row.transform, $"{merc.Class.ClassName}  {merc.Age}세", 15, TextAnchor.MiddleLeft);
            classText.color = textColor;
            classText.horizontalOverflow = HorizontalWrapMode.Overflow;
            classText.gameObject.AddComponent<LayoutElement>().minWidth = 120; // "마법사  39세"와 전투력 칸이 붙지 않게

            // 위치(항상): 마을 / 훈련소 / 병원 / 쉼터 이름 / 파견: 퀘스트 / 귀환 중 — 마을 캐릭터 이동은 알림 대신 여기서 본다
            // (onExpedition은 훈련·입원·쉼터까지 '마을에 없음'으로 묶은 값이라 실제 파견은 따로 본다)
            string where = ExpeditionLog.Instance.IsOnExpedition(merc)
                ? (ExpeditionLog.Instance.IsReturning(merc) ? "귀환 중" : $"파견: {ExpeditionLog.Instance.FindQuest(merc)?.Title}")
                : training ? "훈련소" : hospitalized ? "병원" : bathing ? venue.Name : "마을";
            string status = where; // 둘째 줄(좁은 칸에서 "전투력 233 ·"처럼 어중간하게 끊기지 않게)
            if (merc.CurrentHealth < stats.MaxHealth) status += $"  · 체력 {merc.CurrentHealth}/{stats.MaxHealth}";
            // 피로·사기: 지치면 파견 불가, 피로 50↑·사기 30↓은 능력치가 깎이니 눈에 띄게
            if (merc.IsExhausted) status += $"  · 지침(피로 {merc.Fatigue})";
            else if (merc.Fatigue >= Mercenary.TiredFatigue) status += $"  · 피로 {merc.Fatigue}";
            if (merc.Morale < Mercenary.LowMorale) status += "  · 사기 낮음";
            if (Contracts.NeedsRenewal(merc)) status += $"  · <color=#f0a54a>계약 {Contracts.RemainingDays(merc)}일 남음</color>";
            foreach (var ailment in merc.Ailments) status += $"  · {ailment.Def.displayName}"; // 부상·질병(중하면 파견 불가)
            var powerText = CreateText(row.transform, $"전투력 {merc.CombatPower}\n<color=#a3937c>{status}</color>", 15, TextAnchor.MiddleLeft);
            powerText.color = onExpedition ? new Color(UITheme.TitleText.r, UITheme.TitleText.g, UITheme.TitleText.b, 0.5f) : UITheme.TitleText;
            powerText.horizontalOverflow = HorizontalWrapMode.Wrap; // 길면 버튼 앞에서 줄을 바꾼다(줄 높이는 RefreshList가 늘림)
            powerText.verticalOverflow = VerticalWrapMode.Overflow;
            var powerLayout = powerText.gameObject.AddComponent<LayoutElement>();
            powerLayout.minWidth = 100;
            powerLayout.preferredWidth = 0; // 글자 길이로 폭을 요구하지 않는다(버튼이 눌려 줄지 않게)
            powerLayout.flexibleWidth = 1; // 남는 폭을 차지해 버튼을 오른쪽 끝으로 민다
            _wrappedRows.Add((layoutElement, powerText));
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
                    () => ToggleSelection(merc), interactable: !IsLockedGuest(merc) && Loans.CanDispatchFor(merc, _pendingQuest)
                        && !atCap && (selected || (!merc.IsExhausted && !merc.IsSeverelyAiling)));
            }

            // 파견에 고른 용병은 해고 대신 분대장 버튼(직접 진행 전투에서 조종할 사람). 두 분대 퀘스트는 분대 버튼도(누르면 다른 분대로).
            if (_pendingQuest != null && selected)
            {
                int squad = _squadOf.TryGetValue(merc.Id, out int sq) ? sq : 0;
                if (SquadCount > 1)
                {
                    int other = 1 - squad;
                    CreateActionButton(row.transform, $"{squad + 1}분대",
                        squad == 0 ? new Color(0.25f, 0.4f, 0.6f, 1f) : new Color(0.55f, 0.35f, 0.6f, 1f),
                        () => MoveSquad(merc, other), interactable: SquadMembers(other) < _pendingQuest.SquadSize);
                }
                bool isLeader = merc.Id == _leaderIds[squad];
                CreateActionButton(row.transform, isLeader ? "★분대장" : "분대장",
                    isLeader ? new Color(0.62f, 0.5f, 0.2f, 1f) : new Color(0.3f, 0.3f, 0.35f, 1f),
                    () => { _leaderIds[squad] = merc.Id; RefreshList(); });
            }
            else if (!onExpedition && Loans.IsGuest(merc))
            {
                CreateActionButton(row.transform, "손님", new Color(0.25f, 0.4f, 0.6f, 1f), () => { }, interactable: false); // 계약·해고 없음
            }
            else if (!onExpedition)
            {
                if (Contracts.NeedsRenewal(merc)) // 만료가 가까우면 재계약 협상
                    CreateActionButton(row.transform, "재계약", new Color(0.62f, 0.5f, 0.2f, 1f),
                        () => ContractNegotiationPanel.OpenRenewal(merc));
                // 해고: 첫 클릭에 위약금을 보여 주고, 한 번 더 눌러야 낸다(남은 계약 주급의 절반)
                bool armed = _fireArmedId == merc.Id;
                int release = Contracts.ReleaseCost(merc);
                CreateActionButton(row.transform, armed ? $"해고 -{release}G?" : "해고", new Color(0.55f, 0.25f, 0.25f, 1f),
                    () => Fire(merc, armed, release));
            }

            return row;
        }

        private string _fireArmedId; // 해고 버튼을 한 번 누른 용병(다시 누르면 위약금을 내고 해고)

        private void Fire(Mercenary merc, bool armed, int release)
        {
            if (!armed)
            {
                _fireArmedId = merc.Id;
                if (release > 0) ToastLog.Show($"{merc.Name} 해고 위약금 {release}G (남은 계약 {Contracts.RemainingDays(merc)}일 주급의 절반) — 한 번 더 누르면 해고", false);
                RefreshList();
                return;
            }
            _fireArmedId = null;
            if (!Wallet.TrySpend(release))
            {
                ToastLog.Show($"위약금을 낼 골드가 부족합니다 (필요 {release}G, 보유 {Wallet.Gold}G)", false);
                RefreshList();
                return;
            }
            PlayerParty.Instance.Remove(merc);
            Contracts.Remove(merc);
            RivalGuilds.OnPlayerReleased(merc); // 며칠 안에 근처 길드가 데려갈 수 있다
            ToastLog.Show(release > 0 ? $"{merc.Name} 해고 (위약금 -{release}G)" : $"{merc.Name} 해고", false);
        }

        private void ToggleSelection(Mercenary merc)
        {
            if (IsLockedGuest(merc)) return; // 연합 손님은 빠지지 않는다
            if (_selectedForDispatch.Contains(merc.Id))
            {
                int squad = _squadOf.TryGetValue(merc.Id, out int s) ? s : 0;
                _selectedForDispatch.Remove(merc.Id);
                _squadOf.Remove(merc.Id);
                if (_leaderIds[squad] == merc.Id) PassLeader(squad);
            }
            else
            {
                if (_pendingQuest != null && _selectedForDispatch.Count >= _pendingQuest.MaxDispatchSize)
                    return;
                // 두 분대면 사람이 적은 분대(같으면 1분대)에 넣는다
                int squad = SquadCount > 1 && SquadMembers(1) < SquadMembers(0) ? 1 : 0;
                _selectedForDispatch.Add(merc.Id);
                _squadOf[merc.Id] = squad;
                if (_leaderIds[squad] == null) _leaderIds[squad] = merc.Id;
            }
            RefreshList();
        }

        /// <summary>분대장이 빠지면 그 분대에 남은 사람 중 먼저 줄에 있는 사람이 이어받는다.</summary>
        private void PassLeader(int squad) =>
            _leaderIds[squad] = PlayerParty.Instance.Members
                .FirstOrDefault(m => _selectedForDispatch.Contains(m.Id) && _squadOf.TryGetValue(m.Id, out int s) && s == squad)?.Id;

        /// <summary>두 분대 퀘스트: 고른 용병을 다른 분대로 옮긴다(그 분대에 자리가 있을 때만).</summary>
        private void MoveSquad(Mercenary merc, int to)
        {
            if (_pendingQuest == null || SquadMembers(to) >= _pendingQuest.SquadSize) return;
            int from = _squadOf.TryGetValue(merc.Id, out int s) ? s : 0;
            _squadOf[merc.Id] = to;
            if (_leaderIds[from] == merc.Id) PassLeader(from);
            if (_leaderIds[to] == null) _leaderIds[to] = merc.Id;
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
            string blocked = _selectedForDispatch.Count > 0 ? DispatchBlockReason() : null; // 인원이 안 맞으면 버튼에 이유
            _dispatchButtonGO.GetComponent<Button>().interactable = _selectedForDispatch.Count > 0 && !night && blocked == null;
            if (_dispatchButtonText != null)
            {
                _dispatchButtonText.text = night ? "밤에는 출전 불가\n(오전 5시부터)" : blocked ?? "파견 시작";
                _dispatchButtonText.fontSize = night || blocked != null ? 13 : 18;
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

            string blocked = DispatchBlockReason();
            if (blocked != null)
            {
                ToastLog.Show($"{_pendingQuest.Title}: {blocked}");
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
            var squads = SquadCount > 1 ? members.Select(m => _squadOf.TryGetValue(m.Id, out int s) ? s : 0).ToList() : null;
            ExpeditionLog.Instance.Dispatch(_pendingQuest, members, members.FirstOrDefault(m => m.Id == _leaderIds[0]),
                squads, SquadCount > 1 ? members.FirstOrDefault(m => m.Id == _leaderIds[1]) : null);

            _pendingQuest = null;
            _selectedForDispatch.Clear();
            _squadOf.Clear();
            _leaderIds[0] = _leaderIds[1] = null;
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

            AppendLine($"{expedition.Quest.Title} — 전투 진행 중...");

            int eventCount = Mathf.Max(1, result.Log.Count);
            float delayPerEvent = expedition.Quest.EstimatedDurationSeconds / eventCount;

            foreach (var evt in result.Log)
            {
                AppendLine(FormatLogLine(evt));
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
                sb.AppendLine(FormatLogLine(evt));
            }

            return sb.ToString();
        }

        /// <summary>전투 기록 한 줄. 회복(스킬 치유)은 피해를 음수로 담아 오므로 "+N 회복"으로.</summary>
        private static string FormatLogLine(BattleEvent evt)
        {
            if (evt.Damage < 0) return $"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : +{-evt.Damage} 회복";
            string suffix = evt.Evaded ? " (회피)" : evt.TargetDefeated ? " (처치)" : "";
            return $"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage}{suffix}";
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
