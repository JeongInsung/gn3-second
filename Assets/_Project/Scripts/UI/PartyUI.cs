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

        private static readonly Vector2 PanelSize = new Vector2(980f, 860f);
        private const float ResultHeight = 180f; // 아래 전투 결과 글 칸(씬 값 140 → 결과 줄이 잘리지 않게)

        private void Awake()
        {
            // ScrollListWrapper가 listContainer를 새 계층으로 옮기기 전에, 원래 부모(PartyPanel)를 기억해둔다.
            _panelTransform = listContainer.parent;
            UITheme.StyleWindowTitle(_panelTransform.Find("Title")?.GetComponent<Text>()); // 시장·퀘스트·파티 제목 줄을 같게

            // 창을 넓힌다(씬 값 640x780 → PanelSize): 좁으면 용병 줄 상태 글이 여러 줄로 접혀 칸이 어색했다. 오른쪽 끝 위치는 그대로.
            var panelRect = (RectTransform)_panelTransform;
            float right = panelRect.anchoredPosition.x + panelRect.sizeDelta.x * (1f - panelRect.pivot.x);
            float growY = PanelSize.y - panelRect.sizeDelta.y;
            panelRect.sizeDelta = PanelSize;
            panelRect.anchoredPosition = new Vector2(right - PanelSize.x * (1f - panelRect.pivot.x), panelRect.anchoredPosition.y - growY * 0.5f);
            if (resultText != null) resultText.rectTransform.sizeDelta = new Vector2(resultText.rectTransform.sizeDelta.x, ResultHeight);

            // 창 안에서 제목 아래 ~ 결과 텍스트 위까지의 고정 영역
            ScrollListWrapper.Wrap((RectTransform)listContainer, new Vector2(20f, ResultHeight + 30f), new Vector2(-20f, -105f));

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
            GN3.Save.SaveSystem.OnLoaded += HandleLoaded;
        }

        /// <summary>불러오기: 재생 중이던 전투·기다리던 전투 줄·편성 중 의뢰는 옛 게임 것이라 모두 버린다.</summary>
        private void HandleLoaded()
        {
            StopBattlePlayback();
            if (_pendingQuest != null) CancelDispatch(); // 고른 대원·분대장도 함께 푼다(목록도 다시 그린다)
            else RefreshList();
        }

        private void StopBattlePlayback()
        {
            if (_battlePlayback != null && CoroutineHost.Instance != null) CoroutineHost.Instance.StopCoroutine(_battlePlayback);
            _battlePlayback = null;
            _battlingExpedition = null;
            _battleQueue.Clear();
        }

        private void OnDestroy()
        {
            GN3.Save.SaveSystem.OnLoaded -= HandleLoaded;
            StopBattlePlayback(); // 씬이 바뀌면 이 창의 재생도 끝난다(오래 사는 CoroutineHost에서 돌던 것)
            if (_pendingQuest != null && PendingQuest == _pendingQuest) PendingQuest = null; // 씬이 바뀌면(불러오기) 편성도 끝난다
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
        /// <summary>지금 편성 중인 의뢰(없으면 null). 게시판 카드가 "편성 중"으로 보인다(QuestBoardUI).</summary>
        public static Quest PendingQuest { get; private set; }
        /// <summary>편성 중인 의뢰가 바뀌었을 때(수락·취소·출발).</summary>
        public static event System.Action PendingChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            PendingQuest = null;
            PendingChanged = null;
        }

        private void SetPending(Quest quest)
        {
            _pendingQuest = quest;
            PendingQuest = quest;
            PendingChanged?.Invoke();
        }

        /// <summary>그 의뢰가 아직 게시판(또는 중개인)에 붙어 있는가.</summary>
        private static bool IsListed(Quest quest) =>
            quest != null && (QuestBroker.Quests.Contains(quest) || (QuestBoardListings.Current != null && QuestBoardListings.Current.Contains(quest)));

        /// <summary>[편성 취소]: 고른 것을 모두 풀고 의뢰는 게시판에 그대로 둔다.</summary>
        private void CancelDispatch()
        {
            _selectedForDispatch.Clear();
            _squadOf.Clear();
            _leaderIds[0] = _leaderIds[1] = null;
            SetPending(null);
            RefreshList();
        }

        public void BeginDispatch(Quest quest)
        {
            SetPending(quest);
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
            _riskArmed = false; // 편성이 바뀌면 위험 출발 확인을 다시 받는다
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
                string power = $"{QuestDifficulty.DescribeTeam(_pendingQuest, teamPower)} · {QuestDifficulty.DurationText(_pendingQuest, teamPower)}"
                    + $" · 가방 빈 칸 {BagCatalog.CapacityOf(selected)}칸"; // 전리품을 들고 올 칸(BagCatalog — 가방에 남은 것만큼 준다)
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
                _rows.Add(CreateDispatchToolsRow());
            }

            _rows.Add(CreateSectionHeader("보유 용병"));
            _rows.Add(CreateSortRow());
            // 고른 정렬. 편성 중에는 그보다 먼저 고를 수 있는 사람을 위로(지치거나 다친 사람, 마을에 없는 사람 순).
            var sorted = SortMembers(PlayerParty.Instance.Members);
            var members = _pendingQuest == null ? sorted
                : sorted.OrderBy(m => !Loans.InVillage(m)).ThenBy(m => !_selectedForDispatch.Contains(m.Id) && !CanPick(m)).ToList();
            foreach (var merc in members)
                _rows.Add(CreateMemberRow(merc));

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
            // 줄바꿈된 글에 줄 높이를 맞춘다(FitRowHeight — 폭이 바뀌면 다음 프레임에도 다시 맞춘다)
            foreach (var fit in listContainer.GetComponentsInChildren<FitRowHeight>()) fit.Fit();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
            RefreshDispatchButton();
        }

        /// <summary>굵은 머리줄. lines는 label의 줄 수(높이를 맞춘다).</summary>
        private GameObject CreateSectionHeader(string label, int lines = 1)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(listContainer, false);

            var text = CreateText(go.transform, label, 15, TextAnchor.MiddleLeft);
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 1f, 1f, 0.7f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = Vector2.zero;
            // 글이 길어 더 접히면 줄이 늘어난다(잘리지 않게)
            FitRowHeight.Attach(go, 26 + 18 * (lines - 1), 6f).AddColumn(0f, text);

            return go;
        }

        // ---------- 보유 용병 정렬(이번 실행 동안 기억) ----------
        private static readonly string[] SortNames = { "고용순", "전투력", "직업", "나이", "계약", "상태" };
        private static int _sort;

        /// <summary>고른 정렬로(편성 중이 아니면 이 순서 그대로). 상태 = 지침·중상·피로·체력이 나쁜 사람부터.</summary>
        private static List<Mercenary> SortMembers(IEnumerable<Mercenary> list) => (_sort switch
        {
            1 => list.OrderByDescending(m => m.CombatPower),
            2 => list.OrderBy(m => m.Class.Kind).ThenByDescending(m => m.CombatPower),
            3 => list.OrderBy(m => m.Age),
            4 => list.OrderBy(m => Loans.IsGuest(m) ? int.MaxValue : Contracts.RemainingDays(m)),
            5 => list.OrderByDescending(m => m.IsExhausted || m.IsSeverelyAiling).ThenByDescending(m => m.Fatigue)
                     .ThenBy(m => m.CurrentHealth / (float)System.Math.Max(1, m.CurrentStats.MaxHealth)),
            _ => list,
        }).ToList();

        /// <summary>"보유 용병" 아래 정렬 버튼 줄. 고른 것은 밝은 색.</summary>
        private GameObject CreateSortRow()
        {
            var row = new GameObject("Sort", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 34;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 6;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            h.childControlWidth = h.childControlHeight = true;
            var label = CreateText(row.transform, "정렬", 16, TextAnchor.MiddleLeft); // 시장·게시판 정렬 줄과 같은 모양
            label.color = UITheme.MutedText;
            label.gameObject.AddComponent<LayoutElement>().minWidth = 44;
            for (int i = 0; i < SortNames.Length; i++)
            {
                int mode = i;
                var button = ShopListUI.CreateButton(row.transform, SortNames[i], () => { _sort = mode; RefreshList(); }, 72f);
                if (i == _sort) UITheme.MarkSelected(button);
            }
            UIThemeApplier.ApplyNow(row.transform);
            return row;
        }

        /// <summary>편성 머리줄 아래 버튼 줄: [편성 취소](의뢰는 게시판에 남는다) · [지난 편성](지난번 보낸 팀을 한 번에 고른다).</summary>
        private GameObject CreateDispatchToolsRow()
        {
            var row = new GameObject("LastTeam", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 40;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 2, 2);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            h.childControlWidth = h.childControlHeight = true;
            var cancel = CreateActionButton(row.transform, "편성 취소", new Color(0.45f, 0.3f, 0.3f, 1f), CancelDispatch);
            cancel.GetComponent<LayoutElement>().minWidth = 100;
            cancel.gameObject.AddComponent<TooltipTrigger>().Text = "고른 용병을 풀고 편성을 그만둔다 (의뢰는 게시판에 그대로 남는다)";
            // 도착하면 자동 전투(켜면 도착 편지로 묻지 않는다) — 마지막 선택을 기억
            var auto = CreateActionButton(row.transform, _autoOnArrival ? "도착 시 자동: 켬" : "도착 시 자동: 끔",
                _autoOnArrival ? new Color(0.3f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.35f, 1f),
                () => { _autoOnArrival = !_autoOnArrival; RefreshList(); });
            auto.GetComponent<LayoutElement>().minWidth = 140;
            auto.gameObject.AddComponent<TooltipTrigger>().Text = "켜면 이 파견대는 목적지에 도착하자마자 자동으로 싸우고 결과 편지만 온다(직접 전투 보너스 없음)";
            var autoTeam = CreateActionButton(row.transform, "자동 편성", new Color(0.3f, 0.4f, 0.5f, 1f), ApplyAutoTeam,
                interactable: PlayerParty.Instance.Members.Any(m => !_selectedForDispatch.Contains(m.Id) && CanPick(m)));
            autoTeam.GetComponent<LayoutElement>().minWidth = 100;
            autoTeam.gameObject.AddComponent<TooltipTrigger>().Text = "지금 갈 수 있는 용병을 전투력 높은 순으로 빈자리만큼 고른다(분대장은 분대마다 가장 강한 사람)";
            if (_lastTeam.Count == 0) return row;
            var names = PlayerParty.Instance.Members.Where(m => _lastTeam.ContainsKey(m.Id)).Select(m => m.Name).ToList();
            var button = CreateActionButton(row.transform, "지난 편성", new Color(0.3f, 0.4f, 0.5f, 1f), ApplyLastTeam,
                interactable: PlayerParty.Instance.Members.Any(m => _lastTeam.ContainsKey(m.Id) && !_selectedForDispatch.Contains(m.Id) && CanPick(m)));
            button.GetComponent<LayoutElement>().minWidth = 100;
            var label = CreateText(row.transform, names.Count > 0 ? string.Join(", ", names) : "지난 팀이 모두 떠났습니다", 14, TextAnchor.MiddleLeft);
            label.color = new Color(1f, 1f, 1f, 0.6f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap; // 길면 줄을 바꾸고 줄이 늘어난다
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1;
            labelLayout.preferredWidth = 0;
            FitRowHeight.Attach(row, 40, 4f).AddColumn(0f, label);
            button.gameObject.AddComponent<TooltipTrigger>().Text = "지난번 보낸 용병 중 지금 갈 수 있는 사람을 고르고 분대·분대장을 되살린다";
            return row;
        }

        private GameObject CreateExpeditionRow(Expedition expedition)
        {
            var quest = expedition.Quest;

            var row = new GameObject(quest.Title, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContainer, false);
            row.GetComponent<Image>().color = expedition.IsReturning ? new Color(0.2f, 0.28f, 0.4f, 0.4f) : new Color(0.2f, 0.35f, 0.25f, 0.4f);

            // 높이는 세 줄 글에 맞춘다(FitRowHeight — 폭이 바뀌어도 다시 맞춰 글이 넘치거나 칸이 남지 않게)
            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(12, 12, 6, 6);
            hLayout.spacing = 8;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false; // 버튼은 40px로 가운데, 글 칸만 flexibleHeight로 줄 높이를 채운다
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
            // 세 줄: 제목·상태 / 대원 / 전투력 판정. 길면(두 분대 이름 등) 넘치는 대신 글씨가 조금 작아지고, 전문은 툴팁으로.
            string info = $"{QuestDifficulty.RichTitle(quest)}    {status}\n파견: {members}\n" +
                          $"<color=#a3937c>{QuestDifficulty.DescribeTeam(quest, QuestDifficulty.TeamPower(expedition.Members))}</color>";
            var infoText = CreateText(row.transform, info, 15, TextAnchor.MiddleLeft);
            infoText.supportRichText = true;
            infoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            infoText.verticalOverflow = VerticalWrapMode.Overflow;
            var infoLayout = infoText.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1;
            infoLayout.minWidth = 100;
            infoLayout.preferredWidth = 0; // 버튼 앞까지만 쓰고 줄을 바꾼다
            FitRowHeight.Attach(row, 64, 12f).AddColumn(0f, infoText);

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
            ShopListUI.SetBlocked(restButton, anyWounded ? null : "다친 대원이 없어 쉴 필요가 없습니다.");
            restButton.onClick.AddListener(() => ExpeditionLog.Instance.Rest(expedition));

            // 가는 중이면 [불러들이기](두 번 눌러 확인): 의뢰 실패·명성 절반 벌점, 지나온 길의 절반만큼 걸려 돌아온다
            if (ExpeditionLog.Instance.CanRecall(expedition))
            {
                bool armed = _recallArmed == expedition;
                var recall = CreateActionButton(row.transform, armed ? "정말 철수?" : "불러들이기", new Color(0.45f, 0.3f, 0.3f, 1f), () =>
                {
                    if (_recallArmed != expedition) { _recallArmed = expedition; RefreshList(); return; }
                    _recallArmed = null;
                    ExpeditionLog.Instance.Recall(expedition);
                    RefreshList();
                });
                recall.GetComponent<LayoutElement>().minWidth = 100;
                recall.gameObject.AddComponent<TooltipTrigger>().Text =
                    $"목적지에 가기 전에 파견대를 돌려보낸다 — 의뢰 실패(명성 -{System.Math.Max(1, Guild.FailPenalty / 2)}), 싸우지 않으니 다치지 않는다";
            }

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
            ShopListUI.SetBlocked(button, canFight ? null
                : !expedition.IsReady ? (expedition.IsReturning ? "임무를 마치고 돌아오는 중입니다." : "아직 목적지에 도착하지 않았습니다.")
                : "다른 전투가 진행 중입니다. 끝나면 시작할 수 있습니다.");
            button.onClick.AddListener(() => StartBattle(expedition));

            return row;
        }

        private const string DangerHex = "#e05a4a";  // 파견 불가(지침·중한 부상/병)
        private const string CautionHex = "#f0a54a"; // 능력치 감소(피로·사기 낮음)·계약 갱신

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

            var hLayout = row.GetComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(12, 12, MemberRowPadding, MemberRowPadding);
            hLayout.spacing = 8;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false; // 줄이 글에 맞춰 커져도 초상·버튼은 제 크기로 가운데
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);

            // 시장과 같은 형태: [등급] 이름 · 직업 Lv · 전투력 (+ 꼭 필요한 상태만). 성격·패시브·능력치는 줄을 클릭해 캐릭터 창에서 본다.
            var stats = merc.CurrentStats;
            Color textColor = onExpedition ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

            string guestTag = Loans.GuestTag(merc) is string tag ? $" <size=13><color=#6ab8ff>({tag})</color></size>" : ""; // 근처 길드에서 빌려 온 손님
            string lockTag = merc.Locked ? " <size=13><color=#f0c050>[잠금]</color></size>" : ""; // 해고·팔기 막힘
            var nameText = CreateText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}{guestTag}{lockTag}", 17, TextAnchor.MiddleLeft);
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = textColor;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap; // 손님·잠금 표시까지 붙어 길면 줄을 바꾼다(옆 칸을 덮지 않게)
            var nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
            nameLayout.minWidth = nameLayout.preferredWidth = 200;

            var classText = CreateText(row.transform, $"{merc.Class.ClassName}  {merc.Age}세", 15, TextAnchor.MiddleLeft);
            classText.color = textColor;
            classText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var classLayout = classText.gameObject.AddComponent<LayoutElement>();
            classLayout.minWidth = classLayout.preferredWidth = 130; // "마법사  39세"와 전투력 칸이 붙지 않게

            // 위치(항상): 마을 / 훈련소 / 병원 / 쉼터 이름 / 파견 중 / 귀환 중 — 마을 캐릭터 이동은 알림 대신 여기서 본다
            // (onExpedition은 훈련·입원·쉼터까지 '마을에 없음'으로 묶은 값이라 실제 파견은 따로 본다)
            string where = ExpeditionLog.Instance.IsOnExpedition(merc)
                ? (ExpeditionLog.Instance.IsReturning(merc) ? "귀환 중" : "파견 중") // 어느 의뢰인지는 위 '진행 중인 파견' 칸에 있다(줄마다 긴 제목을 되풀이하지 않는다)
                : training ? "훈련소" : hospitalized ? "병원" : bathing ? venue.Name : "마을";
            string status = where; // 둘째 줄(좁은 칸에서 "전투력 233 ·"처럼 어중간하게 끊기지 않게)
            if (merc.CurrentHealth < stats.MaxHealth) status += $"  · 체력 {merc.CurrentHealth}/{stats.MaxHealth}";
            // 피로·사기: 지치면 파견 불가, 피로 50↑·사기 30↓은 능력치가 깎이니 눈에 띄게
            // 파견을 막는 것은 빨강 "!", 능력치가 깎이는 것은 주황 — 색만이 아니라 "!"·글자로도 알 수 있게(글꼴에 없는 기호는 안 쓴다)
            if (merc.IsExhausted) status += $"  · <color={DangerHex}>! 지침(피로 {merc.Fatigue})</color>";
            else if (merc.Fatigue >= Mercenary.TiredFatigue) status += $"  · <color={CautionHex}>피로 {merc.Fatigue}</color>";
            if (merc.Morale < Mercenary.LowMorale) status += $"  · <color={CautionHex}>사기 낮음</color>";
            if (Contracts.NeedsRenewal(merc)) status += $"  · <color={CautionHex}>계약 {Contracts.RemainingDays(merc)}일 남음</color>";
            foreach (var ailment in merc.Ailments) // 부상·질병(중하면 파견 불가)
                status += ailment.Def.isSevere ? $"  · <color={DangerHex}>! {ailment.Def.displayName}(중)</color>" : $"  · {ailment.Def.displayName}";
            var powerText = CreateText(row.transform, $"전투력 {merc.CombatPower}  <color=#a3937c>· 가방 {merc.BagContents.Count}/{merc.Bag.Slots}칸</color>\n<color=#a3937c>{status}</color>", 15, TextAnchor.MiddleLeft);
            powerText.color = onExpedition ? new Color(UITheme.TitleText.r, UITheme.TitleText.g, UITheme.TitleText.b, 0.5f) : UITheme.TitleText;
            powerText.horizontalOverflow = HorizontalWrapMode.Wrap; // 길면 버튼 앞에서 줄을 바꾼다(줄 높이는 FitRowHeight가 맞춤)
            powerText.verticalOverflow = VerticalWrapMode.Overflow;
            var powerLayout = powerText.gameObject.AddComponent<LayoutElement>();
            powerLayout.minWidth = 100;
            powerLayout.preferredWidth = 0; // 글자 길이로 폭을 요구하지 않는다(버튼이 눌려 줄지 않게)
            powerLayout.flexibleWidth = 1; // 남는 폭을 차지해 버튼을 오른쪽 끝으로 민다
            FitRowHeight.Attach(row, 56, MemberRowPadding * 2).AddColumn(0f, nameText).AddColumn(0f, classText).AddColumn(0f, powerText);
            string ailmentTip = Ailments.EffectTooltip(merc); // 부상·질병: 떨어진 능력치는 마우스를 올리면
            if (ailmentTip != null)
            {
                powerText.raycastTarget = true; // 클릭은 줄(RowClickHandler)로 올라간다
                powerText.gameObject.AddComponent<TooltipTrigger>().Text = ailmentTip;
            }

            RowClickHandler.Attach(row, () => MercenaryInfoPanel.ShowGlobal(merc));

            if (_pendingQuest != null && !onExpedition)
            {
                string block = SelectBlock(merc, selected);
                var select = CreateActionButton(row.transform, selected ? "선택됨" : "선택",
                    selected ? new Color(0.3f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.35f, 1f),
                    () => ToggleSelection(merc), interactable: block == null);
                if (block != null) select.gameObject.AddComponent<TooltipTrigger>().Text = block; // 왜 못 고르는지(누르기 전에)
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
                var fire = CreateActionButton(row.transform, armed ? $"해고 -{release}G?" : "해고", new Color(0.55f, 0.25f, 0.25f, 1f),
                    () => Fire(merc, armed, release));
                ShopListUI.SetBlocked(fire, merc.Locked ? "잠긴 용병입니다 — 용병 정보 창에서 잠금을 풀어야 해고할 수 있습니다." : null);
            }

            return row;
        }

        private string _fireArmedId; // 해고 버튼을 한 번 누른 용병(다시 누르면 위약금을 내고 해고)

        private void Fire(Mercenary merc, bool armed, int release)
        {
            if (merc.Locked) return; // 잠긴 용병(버튼도 막혀 있다)
            if (!armed)
            {
                _fireArmedId = merc.Id;
                if (release > 0) ToastLog.Show($"{merc.Name} 해고 위약금 {release}G (남은 계약 {Contracts.RemainingDays(merc)}일 주급의 절반) — 한 번 더 누르면 해고", false);
                RefreshList();
                return;
            }
            _fireArmedId = null;
            if (!Wallet.TrySpend(release, "방출"))
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

        /// <summary>파견 편성에서 이 용병의 [선택]을 누를 수 없으면 이유, 되면 null.</summary>
        private string SelectBlock(Mercenary merc, bool selected)
        {
            if (IsLockedGuest(merc)) return "연합 의뢰에 함께 가는 손님이라 뺄 수 없습니다.";
            if (!Loans.CanDispatchFor(merc, _pendingQuest)) return "다른 연합 의뢰에 합류한 손님입니다.";
            if (selected) return null;
            if (_selectedForDispatch.Count >= _pendingQuest.MaxDispatchSize) return $"인원이 다 찼습니다 (최대 {_pendingQuest.MaxDispatchSize}명).";
            if (merc.IsExhausted) return $"지쳐서 출전할 수 없습니다 (피로 {merc.Fatigue}) — 마을에서 쉬면 회복합니다.";
            if (merc.IsSeverelyAiling) return $"크게 다치거나 앓아 출전할 수 없습니다 ({merc.AilmentSummary()}) — 병원에서 치료하세요.";
            return null;
        }

        /// <summary>지금 이 편성에 고를 수 있는가(마을에 있고 SelectBlock이 없음). 정렬·지난 편성에 쓴다.</summary>
        private bool CanPick(Mercenary merc) => Loans.InVillage(merc) && SelectBlock(merc, false) == null;

        private static bool _autoOnArrival; // 편성 화면 "도착 시 자동" 토글(이번 실행 동안 기억, 파견마다 저장됨)
        private bool _riskArmed; // '어려움' 이상 판정에서 출발을 한 번 눌렀다(한 번 더 누르면 출발)
        private Expedition _recallArmed; // [불러들이기]를 한 번 누른 파견(한 번 더 누르면 철수)

        // 지난 편성: 마지막으로 보낸 용병 id → 분대, 분대장(저장에도 남는다 — SaveLastTeam/RestoreLastTeam)
        private static readonly Dictionary<string, int> _lastTeam = new Dictionary<string, int>();
        private static readonly string[] _lastLeaders = new string[2];

        public static void SaveLastTeam(GN3.Save.SaveData data)
        {
            data.lastTeam = _lastTeam.Select(kv => new GN3.Save.LootSave { name = kv.Key, count = kv.Value }).ToList();
            data.lastLeader0 = _lastLeaders[0];
            data.lastLeader1 = _lastLeaders[1];
        }

        /// <summary>불러오기: 저장된 지난 편성으로 바꾼다(옛 저장이면 비운다 — 다른 게임의 팀이 남지 않게).</summary>
        public static void RestoreLastTeam(GN3.Save.SaveData data)
        {
            _lastTeam.Clear();
            if (data.lastTeam != null)
                foreach (var e in data.lastTeam)
                    if (!string.IsNullOrEmpty(e.name)) _lastTeam[e.name] = e.count;
            _lastLeaders[0] = string.IsNullOrEmpty(data.lastLeader0) ? null : data.lastLeader0;
            _lastLeaders[1] = string.IsNullOrEmpty(data.lastLeader1) ? null : data.lastLeader1;
        }

        /// <summary>[지난 편성]: 지난번 보낸 사람 중 지금 고를 수 있는 사람을 인원 한도까지 고르고 분대·분대장을 되살린다.</summary>
        private void ApplyLastTeam()
        {
            if (_pendingQuest == null) return;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => _lastTeam.ContainsKey(m.Id)).ToList())
            {
                if (_selectedForDispatch.Contains(merc.Id) || !CanPick(merc)) continue;
                int squad = SquadCount > 1 ? _lastTeam[merc.Id] : 0;
                if (SquadCount > 1 && SquadMembers(squad) >= _pendingQuest.SquadSize) squad = 1 - squad;
                if (SquadCount > 1 && SquadMembers(squad) >= _pendingQuest.SquadSize) continue;
                _selectedForDispatch.Add(merc.Id);
                _squadOf[merc.Id] = squad;
            }
            for (int s = 0; s < 2; s++)
            {
                string leader = _lastLeaders[s];
                if (leader != null && _selectedForDispatch.Contains(leader) && _squadOf.TryGetValue(leader, out int sq) && sq == s) _leaderIds[s] = leader;
                else if (_leaderIds[s] == null || !_selectedForDispatch.Contains(_leaderIds[s])) PassLeader(s);
            }
            RefreshList();
        }

        /// <summary>[자동 편성]: 지금 고를 수 있는 사람을 전투력 순으로 빈자리만큼 고른다. 두 분대면 번갈아 나누고, 분대장은 분대마다 가장 강한 사람.</summary>
        private void ApplyAutoTeam()
        {
            if (_pendingQuest == null) return;
            int cap = _pendingQuest.RequiredDispatchSize > 0 ? _pendingQuest.RequiredDispatchSize : _pendingQuest.MaxDispatchSize;
            var picks = PlayerParty.Instance.Members
                .Where(m => !_selectedForDispatch.Contains(m.Id) && CanPick(m)) // 인원이 차기 전에 후보를 먼저 뽑는다(CanPick은 다 차면 막는다)
                .OrderByDescending(m => m.CombatPower)
                .Take(System.Math.Max(0, cap - _selectedForDispatch.Count))
                .ToList();
            foreach (var merc in picks)
            {
                int squad = SquadCount > 1 && SquadMembers(1) < SquadMembers(0) ? 1 : 0;
                if (SquadCount > 1 && SquadMembers(squad) >= _pendingQuest.SquadSize) squad = 1 - squad;
                if (SquadCount > 1 && SquadMembers(squad) >= _pendingQuest.SquadSize) break;
                _selectedForDispatch.Add(merc.Id);
                _squadOf[merc.Id] = squad;
            }
            for (int s = 0; s < SquadCount; s++)
                _leaderIds[s] = PlayerParty.Instance.Members
                    .Where(m => _selectedForDispatch.Contains(m.Id) && _squadOf.TryGetValue(m.Id, out int sq) && sq == s && !IsLockedGuest(m))
                    .OrderByDescending(m => m.CombatPower).FirstOrDefault()?.Id ?? _leaderIds[s];
            RefreshList();
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
            if (_riskArmed && !GameClock.IsNight) return; // "정말 출발?" 글자를 그대로 둔다

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

            // 편성하는 동안 근처 길드가 가져갔거나 기한이 지났으면 출발하지 않는다(의뢰는 출발할 때 게시판에서 뗀다)
            if (!IsListed(_pendingQuest))
            {
                ToastLog.Show($"{_pendingQuest.Title}: 의뢰가 사라졌습니다 (근처 길드가 가져갔거나 기한이 지남)");
                CancelDispatch();
                return;
            }

            var members = PlayerParty.Instance.Members.Where(m => _selectedForDispatch.Contains(m.Id)).ToList();
            // 판정이 '어려움' 이상이면 한 번 더 눌러야 출발(첫 번째는 버튼 글자만 바꾼다)
            var rating = QuestDifficulty.Rate(QuestDifficulty.TeamPower(members), _pendingQuest);
            if (rating >= QuestDifficulty.Rating.Hard && !_riskArmed)
            {
                _riskArmed = true;
                if (_dispatchButtonText != null)
                {
                    _dispatchButtonText.text = $"정말 출발?\n({QuestDifficulty.Label(rating)})";
                    _dispatchButtonText.fontSize = 13;
                }
                return;
            }
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
            // 지난 편성으로 기억(손님은 빼고) — 다음 편성의 [지난 편성]
            _lastTeam.Clear();
            foreach (var sent in members.Where(m => !Loans.IsGuest(m)))
                _lastTeam[sent.Id] = _squadOf.TryGetValue(sent.Id, out int sq) ? sq : 0;
            _lastLeaders[0] = _leaderIds[0];
            _lastLeaders[1] = _leaderIds[1];
            var sentOut = ExpeditionLog.Instance.Dispatch(_pendingQuest, members, members.FirstOrDefault(m => m.Id == _leaderIds[0]),
                squads, SquadCount > 1 ? members.FirstOrDefault(m => m.Id == _leaderIds[1]) : null);
            if (sentOut != null) sentOut.AutoOnArrival = _autoOnArrival;
            if (!QuestBroker.Remove(_pendingQuest)) QuestBoardListings.Remove(_pendingQuest); // 출발하면 게시판에서 뗀다(저장에서도 빠진다)

            SetPending(null);
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
            // 재생하는 동안 불러오기·새 게임으로 이 파견이 사라졌으면 결과를 내지 않는다
            // (옛 파견 결과가 불러온 게임에 섞이면 보상이 들어오고, 쓰러진 용병과 같은 Id의 용병이 파티에서 빠져 그 파견원이 돌아오지 않았다).
            if (!ExpeditionLog.Instance.Active.Contains(expedition)) yield break;
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
            if (evt.Proc != null && evt.Damage == 0) return $"[R{evt.Round}] {evt.Attacker.Name} — {evt.Proc.TrimEnd('!')}"; // 포효·격노 등
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

        private Button CreateActionButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, bool interactable = true)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(parent, false);
            buttonGO.GetComponent<Image>().color = color;
            var layout = buttonGO.GetComponent<LayoutElement>();
            layout.minWidth = 64;
            layout.minHeight = 36;
            layout.flexibleWidth = 0;

            var buttonText = CreateText(buttonGO.transform, label, 15, TextAnchor.MiddleCenter);
            layout.preferredWidth = Mathf.Max(64f, Mathf.Ceil(buttonText.preferredWidth) + 20f); // "해고 -120G?"처럼 긴 글도 버튼 안에
            var buttonTextRect = buttonText.GetComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            var button = buttonGO.GetComponent<Button>();
            button.interactable = interactable;
            button.onClick.AddListener(onClick);
            return button;
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
