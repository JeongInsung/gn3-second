using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Traits;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을에서 클릭한 용병의 특성(클래스·성격·패시브)과 상태(레벨·체력·능력치)를 보여 주는 우상단 창.
    /// 전용 Overlay Canvas(sortingOrder 1)라 메뉴 패널 위에 뜨고, X 버튼이나 ESC로 닫는다.
    /// 열려 있는 동안 0.5초마다 수치를 다시 읽어 체력 등이 바뀌어도 맞춘다. VillagePartyPresenter가 만든다.
    /// </summary>
    public class MercenaryInfoPanel : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;
        private const float DefaultRight = -16f; // 화면 오른쪽 끝에서
        private const float DefaultTop = -120f;  // 위쪽 버튼·시간 바 아래
        private const float PanelWidth = 340f; // 화면 오른쪽 빈 곳에 들어가게(시장·퀘스트 창 오른쪽 끝을 덮지 않게) — 퀘스트 상세 창과 같은 폭
        private const float PanelHeight = 382f; // 최소 높이. 아래 30px은 버튼 자리(관계 보기·개발 빌드 테스트)
        private const float BottomSpace = 58f; // 마지막 줄 아래 ~ 창 아래(버튼 자리 포함)
        private const int PortraitSize = 110;

        private GameObject _panel;
        private RectTransform _panelRect;
        private RectTransform _healthBarRect;
        private float _infoX;
        private float _infoWidth;
        private Transform _portraitSlot;
        private Text _nameText;
        private Text _classText;
        private Text _statusText;
        private Image _healthFill;
        private Text _healthText;
        private Text _growthText; // "23세 (5월 12일생) · 성장 빠름 · 성장 공격 +6 · 체력 +12"
        private Text _statsText;
        private Text _weaponText;
        private Text _personalityText;
        private Text _passiveText;
        private TooltipTrigger _weaponTip;
        private TooltipTrigger _personalityTip;
        private TooltipTrigger _passiveTip;
        private Text _friendsText;
        private TooltipTrigger _friendsTip;
        private TooltipTrigger _statusTip;
        private Text _healthStatusText;     // "건강: 골절 45% · 감기 10%" (고용된 용병만)
        private TooltipTrigger _healthStatusTip; // 떨어진 능력치·입원 정보
        private TooltipTrigger _statsTip;
        private GameObject _relationsButton;

        private Mercenary _shown;
        private bool _shownWasHired; // 열 때 파티원이었는지(그 뒤 이탈·사망하면 창을 닫는다)
        private GameObject _debugButton;
        private float _refreshTimer;

        public Mercenary Shown => _shown;

        /// <summary>마지막으로 만든 창(MainScene에 하나). 시장 줄 클릭 등 어디서든 연다.</summary>
        public static MercenaryInfoPanel Instance { get; private set; }

        public static void ShowGlobal(Mercenary mercenary)
        {
            if (Instance != null && mercenary != null) Instance.Show(mercenary);
        }

        public static MercenaryInfoPanel Create(Transform parent)
        {
            var go = new GameObject("MercenaryInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MercenaryInfoPanel));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panel = go.GetComponent<MercenaryInfoPanel>();
            panel.Build();
            Instance = panel;
            return panel;
        }

        private void Build()
        {
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            _panelRect = rect;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; // 화면 오른쪽 위(글이 길면 아래로 늘어난다 — LayoutRows)
            rect.anchoredPosition = new Vector2(DefaultRight, DefaultTop); // 오른쪽 위 진행·저장·불러오기 버튼과 시간 바 아래(퀘스트 상세 창과 같은 높이)
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.92f);
            DraggablePanel.Attach(rect); // 잡고 끌어 옮길 수 있다

            // 왼쪽: 전신 초상 칸
            var slot = new GameObject("Portrait", typeof(RectTransform));
            slot.transform.SetParent(_panel.transform, false);
            var slotRect = slot.GetComponent<RectTransform>();
            slotRect.anchorMin = slotRect.anchorMax = slotRect.pivot = new Vector2(0f, 1f);
            slotRect.anchoredPosition = new Vector2(14f, -14f);
            slotRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _portraitSlot = slot.transform;

            // 오른쪽 위: 이름·클래스·상태
            float infoX = _infoX = 14f + PortraitSize + 14f;
            float infoWidth = _infoWidth = PanelWidth - infoX - 44f;
            _nameText = CreateText("Name", 20, FontStyle.Bold, new Vector2(infoX, -14f), new Vector2(infoWidth, 26f));
            _classText = CreateText("Class", 15, FontStyle.Normal, new Vector2(infoX, -42f), new Vector2(infoWidth, 20f));
            _statusText = CreateText("Status", 14, FontStyle.Normal, new Vector2(infoX, -64f), new Vector2(infoWidth, 20f));

            // 체력 막대
            _healthFill = CreateBar("HealthBar", new Vector2(infoX, -92f), new Vector2(infoWidth + 30f, 12f));
            _healthBarRect = (RectTransform)_healthFill.transform.parent;
            _healthText = CreateText("Health", 13, FontStyle.Normal, new Vector2(infoX, -106f), new Vector2(infoWidth + 30f, 18f));

            // 나이·생일 한 줄(예전 경험치 막대 자리)
            _growthText = CreateText("Growth", 13, FontStyle.Normal, new Vector2(infoX, -128f), new Vector2(infoWidth + 30f, 18f));

            // 아래: 능력치·성격·패시브 (전체 폭)
            float bottomWidth = PanelWidth - 28f;
            // 능력치 두 줄: 공격·방어·속도 / 전투력 — 괄호(이번 달 변화)가 붙어도 잘리지 않게
            _statsText = CreateText("Stats", 15, FontStyle.Normal, new Vector2(14f, -150f), new Vector2(bottomWidth, 38f));
            // 무기·성격·패시브는 이름만 쓰고, 마우스를 올리면 설명이 툴팁으로 뜬다.
            _weaponText = CreateText("Weapon", 15, FontStyle.Normal, new Vector2(14f, -190f), new Vector2(bottomWidth, 22f));
            _personalityText = CreateText("Personality", 15, FontStyle.Normal, new Vector2(14f, -218f), new Vector2(bottomWidth, 22f));
            _passiveText = CreateText("Passive", 15, FontStyle.Normal, new Vector2(14f, -246f), new Vector2(bottomWidth, 22f));
            _statusTip = AddTooltip(_statusText);
            _statsTip = AddTooltip(_statsText);
            _weaponTip = AddTooltip(_weaponText);
            _personalityTip = AddTooltip(_personalityText);
            _passiveTip = AddTooltip(_passiveText);
            _friendsText = CreateText("Friends", 15, FontStyle.Normal, new Vector2(14f, -274f), new Vector2(bottomWidth, 22f));
            _friendsTip = AddTooltip(_friendsText);
            _healthStatusText = CreateText("HealthStatus", 15, FontStyle.Normal, new Vector2(14f, -302f), new Vector2(bottomWidth, 22f));
            _healthStatusText.supportRichText = true; // 상태이상 이름 색(에셋 이름에는 태그가 없다)
            _healthStatusTip = AddTooltip(_healthStatusText);
            _relationsButton = CreateRelationsButton();

            CreateCloseButton();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CreateDebugDamageButton();
#endif
            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide, WindowRole.Linked); // 목록 줄을 눌러 여는 창: 부모 창을 닫지 않는다
        }

        /// <summary>어두운 바탕 + 가로로 채워지는 막대. 채움 Image를 돌려준다.</summary>
        private Image CreateBar(string name, Vector2 topLeft, Vector2 size)
        {
            var barBack = new GameObject(name, typeof(RectTransform), typeof(Image));
            barBack.transform.SetParent(_panel.transform, false);
            var barRect = barBack.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = topLeft;
            barRect.sizeDelta = size;
            barBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBack.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            return image;
        }

        private Text CreateText(string name, int size, FontStyle style, Vector2 topLeft, Vector2 boxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = boxSize;
            var text = go.GetComponent<Text>();
            text.font = UITheme.Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.supportRichText = false; // 패시브 이름의 "<...>"가 태그로 읽히지 않게
            text.raycastTarget = false;
            return text;
        }

        /// <summary>왼쪽 아래 "관계 보기": 이 용병을 고른 채로 관계 창(RelationsPanel)을 연다. 고용된 용병만 보인다.</summary>
        private GameObject CreateRelationsButton()
        {
            var go = new GameObject("RelationsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(14f, 8f);
            rect.sizeDelta = new Vector2(100f, 24f);
            go.GetComponent<Image>().color = new Color(0.3f, 0.35f, 0.5f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_shown != null) RelationsPanel.ShowGlobal(_shown);
            });

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>();
            text.text = "관계 보기";
            text.font = UITheme.Font;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return go;
        }

        /// <summary>[테스트] 보고 있는 용병 체력 -30%. 치료 아이템 시험용(에디터·개발 빌드에서만).</summary>
        private void CreateDebugDamageButton()
        {
            var go = new GameObject("DebugDamageButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel.transform, false);
            _debugButton = go;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-10f, 8f);
            rect.sizeDelta = new Vector2(150f, 24f);
            go.GetComponent<Image>().color = new Color(0.45f, 0.45f, 0.45f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_shown == null) return;
                int lost = DebugHotkeys.Damage(_shown);
                ToastLog.Show($"[테스트] {_shown.Name} 체력 -{lost}");
                Refresh();
            });

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>();
            text.text = "[테스트] 체력 -30%";
            text.font = UITheme.Font;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private void CreateCloseButton()
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-8f, -8f);
            rect.sizeDelta = new Vector2(28f, 28f);
            go.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            go.GetComponent<Button>().onClick.AddListener(Hide);

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>();
            text.text = "X";
            text.font = UITheme.Font;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        /// <summary>마을에서 캐릭터를 클릭해 연 창인지. 그 캐릭터가 마을에서 사라지면(여관·파견·밤) 이런 창만 자동으로 닫는다.</summary>
        public bool OpenedFromVillage { get; private set; }

        public void Show(Mercenary mercenary, bool fromVillage = false)
        {
            OpenedFromVillage = fromVillage;
            if (_shown != mercenary)
            {
                foreach (Transform child in _portraitSlot) Destroy(child.gameObject);
                var portrait = CharacterPortraitUI.Create(_portraitSlot, mercenary.Appearance, PortraitSize, PortraitCrop.FullBody);
                var portraitRect = portrait.GetComponent<RectTransform>();
                portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0f, 1f);
                portraitRect.anchoredPosition = Vector2.zero;
                portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize); // 레이아웃 그룹 밖이라 크기를 직접 준다
            }
            _shown = mercenary;
            _shownWasHired = PlayerParty.Instance.Members.Contains(mercenary);
            _panel.SetActive(true);
            PlaceBesideParty();
            Refresh();
        }

        /// <summary>
        /// 기본 자리는 화면 오른쪽 위(버튼·시간 바 아래). 파티 창(오른쪽)이 열려 있으면 그 왼쪽에 붙여 파티 목록을 덮지 않는다.
        /// </summary>
        private void PlaceBesideParty()
        {
            var parent = (RectTransform)_panelRect.parent;
            float x = DefaultRight;
            var partyPanel = GameObject.Find("PartyPanel"); // 씬의 파티 창 판(꺼져 있으면 못 찾는다)
            if (partyPanel != null && partyPanel.transform is RectTransform partyRect)
            {
                var corners = new Vector3[4];
                partyRect.GetWorldCorners(corners);
                float partyLeft = parent.InverseTransformPoint(corners[0]).x;
                x = Mathf.Min(DefaultRight, partyLeft - parent.rect.xMax - 12f);
            }
            _panelRect.anchoredPosition = new Vector2(x, DefaultTop);
        }

        public void Hide()
        {
            _shown = null;
            OpenedFromVillage = false;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_shown == null) return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f) Refresh();
        }

        private void Refresh()
        {
            _refreshTimer = RefreshInterval;
            var merc = _shown;
            bool hired = PlayerParty.Instance.Members.Contains(merc);
            if (_shownWasHired && !hired)
            {
                // 보던 파티원이 이탈·사망으로 떠났다 — "고용 전 · 고용비"로 잘못 보이지 않게 닫는다
                Hide();
                return;
            }
            if (hired) _shownWasHired = true; // 시장에서 보다가 고용한 경우
            var stats = merc.CurrentStats;

            _nameText.supportRichText = true; // 등급 글자 색
            _nameText.text = $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}";
            _classText.text = $"{merc.Class.ClassName}  {merc.Age}세";

            bool wounded = merc.CurrentHealth < stats.MaxHealth;
            if (_debugButton != null) _debugButton.SetActive(hired); // 고용 전 용병은 테스트로 다치게 하지 않는다
            if (!hired)
                _statusText.text = $"상태: 고용 전 · 고용비 {Pricing.HireCost(merc)}G";
            else if (ExpeditionLog.Instance.IsOnExpedition(merc))
                _statusText.text = $"상태: 파견 중 ({ExpeditionLog.Instance.FindQuest(merc)?.Title})";
            else if (TrainingHall.IsTraining(merc))
                _statusText.text = $"상태: 훈련소에서 훈련 중 (남은 약 {TrainingHall.RemainingHours(merc):0.#}시간)";
            else if (RestVenues.Find(merc) is RestVenue venue)
                _statusText.text = $"상태: {venue.Name}에서 쉬는 중 (남은 약 {venue.RemainingHours(merc):0.#}시간)";
            else if (Hospital.IsAdmitted(merc))
                _statusText.text = $"상태: 병원에서 치료 중 (남은 약 {Hospital.RemainingHours(merc):0.#}시간)";
            else if (merc.HasAilment)
                _statusText.text = "상태: 마을에서 쉬는 중" + (merc.IsSeverelyAiling ? " (파견 불가)" : ""); // 무엇을 앓는지는 아래 건강 줄
            else
                _statusText.text = merc.IsExhausted ? "상태: 지쳐서 쉬는 중 (파견 불가)"
                    : wounded ? "상태: 다침 (마을에서 쉬는 중)" : "상태: 마을에서 쉬는 중";
            if (hired) _statusText.text += $"\n주급 {MercenaryCondition.WeeklyWage(merc)}G"; // 좁은 칸에서 "주급 / 22G"처럼 끊기지 않게 줄을 나눈다
            string ailmentTip = Ailments.EffectTooltip(merc); // 효과는 글에 쓰지 않고 마우스를 올리면 보여 준다(건강 줄·능력치 줄)
            SetTipLine(_statusText, _statusTip, _statusText.text, null);

            float ratio = stats.MaxHealth > 0 ? (float)merc.CurrentHealth / stats.MaxHealth : 0f;
            _healthFill.fillAmount = ratio;
            _healthFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.3f, 0.8f, 0.35f), ratio);
            _healthText.text = $"체력 {merc.CurrentHealth} / {stats.MaxHealth}";
            if (hired)
            {
                // 피로·사기(능력치가 깎였으면 몇 %인지)
                _healthText.text += $"\n피로 {merc.Fatigue} · 사기 {merc.Morale}"; // 좁은 칸에서 "사기 / 70"처럼 끊기지 않게 줄을 나눈다
                float condition = merc.ConditionMultiplier;
                string hit = merc.UsesMagic ? "마·방" : stats.Holy > 0 ? "공·방·신" : "공·방";
                if (condition < 1f) _healthText.text += $"\n{hit} -{Mathf.RoundToInt((1f - condition) * 100f)}%"; // 깎인 능력치는 따로 한 줄
            }

            // 나이 칸: 나이와 생일만(성장 속도·재능은 숨은 값)
            _growthText.text = $"{merc.Age}세 · {MercenaryAge.BirthdayText(merc)}생";

            // 능력치: "공격 23 (+3 −2)"(마법사는 "마력", 힐러는 "신성력"도) — 23 = 지금 영구 수치(성장·쇠퇴·무기·수련이 바로 반영),
            // +3/−1 = 이번 달 오른·내린 양(참고용, 왼쪽에 이미 들어 있음, 매달 1일 Mercenary.SnapshotMonth로 0부터),
            // −2 = 부상·질병·피로·사기로 잠깐 깎인 양(빨강, 나으면 돌아옴).
            var permanent = merc.PermanentStats;
            int permanentPower = GradeTable.CombatPower(permanent);
            string Stat(string label, int monthBase, int perm, int now)
            {
                int grew = perm - monthBase, temp = now - perm;
                var parts = new List<string>();
                if (grew != 0) parts.Add(grew > 0 ? $"<color=#7fd36a>+{grew}</color>" : $"<color=#e06050>−{-grew}</color>");
                if (temp != 0) parts.Add(temp > 0 ? $"<color=#7fd36a>+{temp}</color>" : $"<color=#e06050>−{-temp}</color>");
                return parts.Count > 0 ? $"{label} {perm} ({string.Join(" ", parts)})" : $"{label} {perm}";
            }
            _statsText.supportRichText = true;
            int grewSum = permanentPower - merc.MonthBasePower, tempSum = merc.CombatPower - permanentPower;
            string monthTip = "괄호: 이번 달 변화(왼쪽 수치에 이미 반영)" + (grewSum != 0 ? $" · 성장·무기 등 전투력 {grewSum:+0;−0}" : "")
                              + (tempSum != 0 ? $" · 부상·피로 등으로 잠깐 전투력 {tempSum:+0;−0}(나으면 돌아옴)" : "")
                              + "\n매달 1일에 괄호가 0부터 다시 셉니다.";
            // 연구소(마법사)·성당(힐러) 수련 보너스는 수치에 이미 들어 있다 — 마우스를 올리면 단계와 다음 단계까지 남은 시간
            string bonusText = merc.ResearchBonusText();
            bool studies = merc.Class.Kind == MercenaryClassKind.Mage || merc.Class.Kind == MercenaryClassKind.Healer;
            string place = merc.Class.Kind == MercenaryClassKind.Healer ? "성당" : "마법 연구소";
            string researchTip = !studies ? null
                : (merc.CanResearchMore
                    ? $"{place} {merc.ResearchLabel} {merc.ResearchLevel}/{Mercenary.MaxResearchLevel}단계 · 다음 단계까지 {Mercenary.ResearchHoursPerBonus - merc.ResearchHours:0.#}시간"
                    : $"{place} {merc.ResearchLabel} {merc.ResearchLevel}/{Mercenary.MaxResearchLevel}단계 (최대)")
                  + (bonusText.Length > 0 ? $" · {bonusText}" : "");
            string statsTip = monthTip;
            if (!string.IsNullOrEmpty(ailmentTip)) statsTip += "\n" + ailmentTip;
            if (researchTip != null) statsTip += "\n" + researchTip;
            SetTipLine(_statsText, _statsTip,
                $"{Stat(merc.OffenseLabel, merc.MonthBaseAttack, permanent.Offense, stats.Offense)}  ·  {Stat("방어", merc.MonthBaseDefense, permanent.Defense, stats.Defense)}  ·  "
                + $"{Stat("속도", merc.MonthBaseSpeed, permanent.MoveSpeed, stats.MoveSpeed)}\n"
                + (permanent.Holy > 0 ? $"{Stat("신성력", merc.MonthBaseHoly, permanent.Holy, stats.Holy)}  ·  " : "")
                + Stat("전투력", merc.MonthBasePower, permanentPower, merc.CombatPower),
                statsTip);
            if (merc.Weapon != null)
                SetTipLine(_weaponText, _weaponTip, $"무기: {merc.Weapon.Name}", merc.Weapon.DescribeFor(merc.Class.Kind));
            else
                SetTipLine(_weaponText, _weaponTip, "무기: 맨손", null);

            var personality = PersonalityTable.Get(merc.Personality);
            SetTipLine(_personalityText, _personalityTip, $"성격 [{personality.Label}]", personality.Description);

            var passives = ClassPassiveFactory.Create(merc.Class.Kind, merc.HasRarePassive);
            _passiveText.gameObject.SetActive(passives.Count > 0);
            if (passives.Count > 0)
            {
                string rare = merc.HasRarePassive ? " (레어)" : "";
                SetTipLine(_passiveText, _passiveTip, $"패시브 <{passives[0].Name}>{rare}", passives[0].Description);
            }

            // 친한 동료(고용된 용병만): 지인 이상 상위 3명
            _friendsText.gameObject.SetActive(hired);
            _relationsButton.SetActive(hired);
            if (hired)
            {
                var friends = Affinity.Friends(merc, Affinity.Acquaintance.MinValue).Take(3).ToList();
                string label = friends.Count == 0
                    ? "친한 동료: 없음"
                    : "친한 동료: " + string.Join(" · ", friends.Select(f => $"{f.merc.Name}({Affinity.TierLabel(f.value)} {f.value})"));
                SetTipLine(_friendsText, _friendsTip, label, null); // 툴팁 없음(자세한 건 "관계 보기")
            }

            // 건강(고용된 용병만): 앓는 것 이름·진행도 한 줄, 마우스를 올리면 떨어진 능력치
            _healthStatusText.gameObject.SetActive(hired);
            if (hired) SetTipLine(_healthStatusText, _healthStatusTip, HealthLine(merc), HealthTip(merc, ailmentTip));

            LayoutRows();
        }

        /// <summary>
        /// 글이 창 폭을 넘으면 줄을 바꾸고, 줄 높이만큼 아래 줄들을 내려 쌓은 뒤 창 높이를 늘린다(최소 PanelHeight).
        /// 짧은 글이 한 줄일 때의 자리는 예전과 같다.
        /// </summary>
        private void LayoutRows()
        {
            // 오른쪽 위 칸(초상 옆): 이름·클래스·상태·체력 막대·체력·나이
            float rightWidth = _infoWidth + 30f; // 체력 막대와 같은 폭(오른쪽 여백 14)
            float y = -14f;
            y = Place(_nameText, _infoX, y, _infoWidth, 26f, false) - 2f; // 이름 줄 오른쪽은 X 버튼
            y = Place(_classText, _infoX, y, _infoWidth, 20f, false) - 2f;
            y = Place(_statusText, _infoX, y, rightWidth, 20f, true) - 8f;
            _healthBarRect.anchoredPosition = new Vector2(_infoX, y);
            y -= _healthBarRect.sizeDelta.y + 2f;
            y = Place(_healthText, _infoX, y, rightWidth, 18f, false) - 4f;
            y = Place(_growthText, _infoX, y, rightWidth, 18f, false) - 4f;

            // 아래 칸(전체 폭): 초상 아래부터
            y = Mathf.Min(y, -14f - PortraitSize - 26f);
            float bottomWidth = PanelWidth - 28f;
            y = Place(_statsText, 14f, y, bottomWidth, 38f, true) - 2f;
            foreach (var row in new[] { _weaponText, _personalityText, _passiveText, _friendsText, _healthStatusText })
            {
                if (!row.gameObject.activeSelf) continue;
                y = Place(row, 14f, y, bottomWidth, 22f, true) - 6f;
            }
            _panelRect.sizeDelta = new Vector2(PanelWidth, Mathf.Max(PanelHeight, -(y + 6f) + BottomSpace));
        }

        /// <summary>
        /// 줄 하나를 (x, top)에 둔다. 최대 폭을 넘으면 줄을 바꾸고 높이를 늘린다. 이 줄 아래 끝 y를 돌려준다.
        /// tight: 툴팁 줄은 마우스 판정이 글자 위에서만 되도록 짧으면 칸 폭을 글자 폭에 맞춘다.
        /// </summary>
        private static float Place(Text text, float x, float top, float maxWidth, float minHeight, bool tight)
        {
            var rect = text.rectTransform;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            float width = text.preferredWidth;
            bool wraps = width > maxWidth;
            if (wraps) text.horizontalOverflow = HorizontalWrapMode.Wrap;
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = new Vector2(wraps || !tight ? maxWidth : width + 4f, minHeight);
            float height = Mathf.Max(minHeight, text.preferredHeight); // 폭을 정한 뒤에 재야 줄바꿈 높이가 나온다
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            return top - height;
        }

        /// <summary>"건강: 양호" 또는 "건강: 골절 45% · 감기 10% (입원 중)". 이름은 에셋의 표시 색, 중하면 빨강.</summary>
        private static string HealthLine(Mercenary merc)
        {
            if (!merc.HasAilment) return "건강: <color=#a3937c>양호</color>";
            var parts = merc.Ailments.Select(a =>
            {
                string color = a.Def.isSevere ? "e06050" : ColorUtility.ToHtmlStringRGB(a.Def.color);
                return $"<color=#{color}>{a.Def.displayName}</color> {Mathf.RoundToInt(a.Progress * 100f)}%";
            });
            string tail = Hospital.IsAdmitted(merc) ? " <color=#a3937c>(입원 중)</color>"
                : merc.IsSeverelyAiling ? " <color=#a3937c>(파견 불가)</color>" : "";
            return "건강: " + string.Join(" · ", parts) + tail;
        }

        private static string HealthTip(Mercenary merc, string effectTip)
        {
            if (effectTip == null) return null;
            if (!Hospital.IsAdmitted(merc)) return effectTip;
            return $"{effectTip}\n입원 중 · 남은 약 {Hospital.RemainingHours(merc):0.#}시간 · 쌓인 치료비 {Hospital.Fee(merc)}G";
        }

        private static TooltipTrigger AddTooltip(Text text)
        {
            text.raycastTarget = true; // 마우스를 받아야 툴팁이 뜬다
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text.gameObject.AddComponent<TooltipTrigger>();
        }

        /// <summary>이름만 쓰고, 설명은 툴팁으로. 칸 폭·줄바꿈은 LayoutRows(Place)가 맞춘다.</summary>
        private static void SetTipLine(Text text, TooltipTrigger tip, string label, string tooltip)
        {
            text.text = label;
            tip.Text = tooltip;
            tip.enabled = !string.IsNullOrEmpty(tooltip);
            text.raycastTarget = tip.enabled;
        }
    }
}
