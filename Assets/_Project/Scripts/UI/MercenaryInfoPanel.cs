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
        private const float PanelWidth = 400f;
        private const float PanelHeight = 382f; // 아래 30px은 버튼 자리(관계 보기·개발 빌드 테스트)
        private const int PortraitSize = 110;

        private GameObject _panel;
        private Transform _portraitSlot;
        private Text _nameText;
        private Text _classText;
        private Text _statusText;
        private Image _healthFill;
        private Text _healthText;
        private Image _xpFill;
        private Text _xpText;
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
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; // 화면 오른쪽 위
            rect.anchoredPosition = new Vector2(-16f, -16f);
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
            float infoX = 14f + PortraitSize + 14f;
            float infoWidth = PanelWidth - infoX - 44f;
            _nameText = CreateText("Name", 20, FontStyle.Bold, new Vector2(infoX, -14f), new Vector2(infoWidth, 26f));
            _classText = CreateText("Class", 15, FontStyle.Normal, new Vector2(infoX, -42f), new Vector2(infoWidth, 20f));
            _statusText = CreateText("Status", 14, FontStyle.Normal, new Vector2(infoX, -64f), new Vector2(infoWidth, 20f));

            // 체력 막대
            _healthFill = CreateBar("HealthBar", new Vector2(infoX, -92f), new Vector2(infoWidth + 30f, 12f));
            _healthText = CreateText("Health", 13, FontStyle.Normal, new Vector2(infoX, -106f), new Vector2(infoWidth + 30f, 18f));

            // 경험치 막대(파랑)
            _xpFill = CreateBar("XpBar", new Vector2(infoX, -128f), new Vector2(infoWidth + 30f, 8f));
            _xpFill.color = new Color(0.35f, 0.6f, 0.95f);
            _xpText = CreateText("Xp", 13, FontStyle.Normal, new Vector2(infoX, -138f), new Vector2(infoWidth + 30f, 18f));

            // 아래: 능력치·성격·패시브 (전체 폭)
            float bottomWidth = PanelWidth - 28f;
            _statsText = CreateText("Stats", 15, FontStyle.Normal, new Vector2(14f, -162f), new Vector2(bottomWidth, 22f));
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            _panel.SetActive(true);
            Refresh();
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
            var stats = merc.CurrentStats;

            _nameText.supportRichText = true; // 등급 글자 색
            _nameText.text = $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}";
            _classText.text = $"{merc.Class.ClassName}  Lv.{merc.Level}";

            bool wounded = merc.CurrentHealth < stats.MaxHealth;
            bool hired = PlayerParty.Instance.Members.Contains(merc);
            if (_debugButton != null) _debugButton.SetActive(hired); // 고용 전 용병은 테스트로 다치게 하지 않는다
            if (!hired)
                _statusText.text = $"상태: 고용 전 · 고용비 {Pricing.HireCost(merc)}G";
            else if (ExpeditionLog.Instance.IsOnExpedition(merc))
                _statusText.text = $"상태: 파견 중 ({ExpeditionLog.Instance.FindQuest(merc)?.Title})";
            else if (TrainingHall.IsTraining(merc))
                _statusText.text = $"상태: 훈련소에서 훈련 중 (남은 약 {TrainingHall.RemainingHours(merc):0.#}시간)";
            else if (Hospital.IsAdmitted(merc))
                _statusText.text = $"상태: 병원에서 치료 중 (남은 약 {Hospital.RemainingHours(merc):0.#}시간)";
            else if (merc.HasAilment)
                _statusText.text = "상태: 마을에서 쉬는 중" + (merc.IsSeverelyAiling ? " (파견 불가)" : ""); // 무엇을 앓는지는 아래 건강 줄
            else
                _statusText.text = merc.IsExhausted ? "상태: 지쳐서 쉬는 중 (파견 불가)"
                    : wounded ? "상태: 다침 (마을에서 쉬는 중)" : "상태: 마을에서 쉬는 중";
            if (hired) _statusText.text += $" · 주급 {MercenaryCondition.WeeklyWage(merc)}G";
            string ailmentTip = Ailments.EffectTooltip(merc); // 효과는 글에 쓰지 않고 마우스를 올리면 보여 준다(건강 줄·능력치 줄)
            SetTipLine(_statusText, _statusTip, _statusText.text, null);

            float ratio = stats.MaxHealth > 0 ? (float)merc.CurrentHealth / stats.MaxHealth : 0f;
            _healthFill.fillAmount = ratio;
            _healthFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.3f, 0.8f, 0.35f), ratio);
            _healthText.text = $"체력 {merc.CurrentHealth} / {stats.MaxHealth}";
            if (hired)
            {
                // 피로·사기(능력치가 깎였으면 몇 %인지)
                _healthText.text += $"  ·  피로 {merc.Fatigue} · 사기 {merc.Morale}";
                float condition = merc.ConditionMultiplier;
                if (condition < 1f) _healthText.text += $" (공·방 -{Mathf.RoundToInt((1f - condition) * 100f)}%)";
            }

            if (merc.Level >= Mercenary.MaxLevel)
            {
                _xpFill.fillAmount = 1f;
                _xpText.text = "경험치 최대 레벨";
            }
            else
            {
                _xpFill.fillAmount = (float)merc.Experience / merc.XpToNext;
                _xpText.text = $"경험치 {merc.Experience} / {merc.XpToNext}";
            }

            // 부상·질병으로 떨어진 능력치는 빨간색, 마우스를 올리면 얼마에서 떨어졌는지
            var healthy = merc.StatsWithoutAilments;
            string Stat(string label, int value, int normal) => value < normal ? $"{label} <color=#e06050>{value}</color>" : $"{label} {value}";
            _statsText.supportRichText = true;
            SetTipLine(_statsText, _statsTip,
                $"{Stat("공격", stats.Attack, healthy.Attack)}  ·  {Stat("방어", stats.Defense, healthy.Defense)}  ·  {Stat("속도", stats.MoveSpeed, healthy.MoveSpeed)}  ·  전투력 {merc.CombatPower}",
                ailmentTip);
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

        /// <summary>이름만 쓰고, 설명은 툴팁으로. 마우스 판정이 글자 위에서만 되도록 칸 폭을 글자 폭에 맞춘다.</summary>
        private static void SetTipLine(Text text, TooltipTrigger tip, string label, string tooltip)
        {
            text.text = label;
            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(text.preferredWidth + 4f, rect.sizeDelta.y);
            tip.Text = tooltip;
            tip.enabled = !string.IsNullOrEmpty(tooltip);
            text.raycastTarget = tip.enabled;
        }
    }
}
