using GN3.Mercenaries;
using GN3.Quests;
using GN3.Traits;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private const float PanelHeight = 300f;
        private const int PortraitSize = 110;

        private GameObject _panel;
        private Transform _portraitSlot;
        private Text _nameText;
        private Text _classText;
        private Text _statusText;
        private Image _healthFill;
        private Text _healthText;
        private Text _statsText;
        private Text _personalityText;
        private Text _passiveText;

        private Mercenary _shown;
        private float _refreshTimer;

        public Mercenary Shown => _shown;

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
            var barBack = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
            barBack.transform.SetParent(_panel.transform, false);
            var barRect = barBack.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = new Vector2(infoX, -92f);
            barRect.sizeDelta = new Vector2(infoWidth + 30f, 12f);
            barBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBack.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            _healthFill = fill.GetComponent<Image>();
            _healthFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;

            _healthText = CreateText("Health", 13, FontStyle.Normal, new Vector2(infoX, -106f), new Vector2(infoWidth + 30f, 18f));

            // 아래: 능력치·성격·패시브 (전체 폭)
            float bottomWidth = PanelWidth - 28f;
            _statsText = CreateText("Stats", 15, FontStyle.Normal, new Vector2(14f, -136f), new Vector2(bottomWidth, 22f));
            _personalityText = CreateText("Personality", 14, FontStyle.Normal, new Vector2(14f, -164f), new Vector2(bottomWidth, 52f));
            _passiveText = CreateText("Passive", 14, FontStyle.Normal, new Vector2(14f, -220f), new Vector2(bottomWidth, 66f));
            _personalityText.verticalOverflow = _passiveText.verticalOverflow = VerticalWrapMode.Truncate;

            CreateCloseButton();
            _panel.SetActive(false);
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

        public void Show(Mercenary mercenary)
        {
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
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_shown == null) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f) Refresh();
        }

        private void Refresh()
        {
            _refreshTimer = RefreshInterval;
            var merc = _shown;
            var stats = merc.CurrentStats;

            _nameText.text = merc.Name;
            _classText.text = $"{merc.Class.ClassName}  Lv.{merc.Level}";

            bool wounded = merc.CurrentHealth < stats.MaxHealth;
            if (ExpeditionLog.Instance.IsOnExpedition(merc))
                _statusText.text = $"상태: 파견 중 ({ExpeditionLog.Instance.FindQuest(merc)?.Title})";
            else
                _statusText.text = wounded ? "상태: 부상 (마을에서 쉬는 중)" : "상태: 마을에서 쉬는 중";

            float ratio = stats.MaxHealth > 0 ? (float)merc.CurrentHealth / stats.MaxHealth : 0f;
            _healthFill.fillAmount = ratio;
            _healthFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.3f, 0.8f, 0.35f), ratio);
            _healthText.text = $"체력 {merc.CurrentHealth} / {stats.MaxHealth}";

            _statsText.text = $"공격 {stats.Attack}  ·  방어 {stats.Defense}  ·  속도 {stats.MoveSpeed}";

            var personality = PersonalityTable.Get(merc.Personality);
            _personalityText.text = $"성격 [{personality.Label}]  {personality.Description}";

            var passives = ClassPassiveFactory.Create(merc.Class.Kind, merc.HasRarePassive);
            if (passives.Count > 0)
            {
                string rare = merc.HasRarePassive ? " (레어)" : "";
                _passiveText.text = $"패시브 <{passives[0].Name}{rare}>  {passives[0].Description}";
            }
            else
            {
                _passiveText.text = "";
            }
        }
    }
}
