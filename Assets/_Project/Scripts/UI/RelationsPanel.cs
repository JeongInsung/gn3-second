using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 용병 관계(친밀도) 창. 위에 파티 용병 이름 탭, 고른 용병과 나머지 동료 한 명 한 명의 친밀도를
    /// 가운데가 0인 막대·값·단계(색)·성격 궁합으로 보여 준다.
    /// 줄에 마우스를 올리면 그 단계의 효과가 툴팁으로 뜬다. 오른쪽 아래 "관계" 버튼(우편함 왼쪽)이나
    /// 용병 정보창의 "관계 보기"로 연다. 파티·친밀도가 바뀌면 열린 채로 다시 그린다. 끌어 옮길 수 있고 X·ESC로 닫는다.
    /// </summary>
    public class RelationsPanel : MonoBehaviour
    {
        private const float Width = 640f;
        private const float Pad = 20f;
        private const float TabWidth = 104f;
        private const float TabHeight = 30f;
        private const float TabGap = 6f;
        private const float RowHeight = 34f;
        private const float RowGap = 4f;
        private const float BarWidth = 200f;
        private const float BarHeight = 12f;
        private const float ButtonWidth = 80f;
        private const float ButtonHeight = 40f;
        private const string Dim = "#a3937c";

        public static RelationsPanel Instance { get; private set; }

        private GameObject _panel;
        private RectTransform _panelRect;
        private Transform _tabs;
        private Transform _rows;
        private Text _header;
        private Font _font;
        private Sprite _white;
        private string _selectedId;

        public static RelationsPanel Create()
        {
            var go = new GameObject("RelationsInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RelationsPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<RelationsPanel>();
            panel.Build();
            Instance = panel;

            PlayerParty.Instance.OnChanged += panel.RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged += panel.RefreshIfOpen;
            TrainingHall.OnChanged += panel.RefreshIfOpen;
            Affinity.OnChanged += panel.RefreshIfOpen;
            return panel;
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged -= RefreshIfOpen;
            TrainingHall.OnChanged -= RefreshIfOpen;
            Affinity.OnChanged -= RefreshIfOpen;
        }

        /// <summary>화면 오른쪽 아래, 우편함 아이콘 왼쪽의 "관계" 버튼. 누르면 관계 창을 열고 닫는다.</summary>
        public static void CreateButton(Transform parent)
        {
            var go = new GameObject("RelationsButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-(MailboxButton.Margin + MailboxButton.Size + 10f),
                MailboxButton.Margin + (MailboxButton.Size - ButtonHeight) * 0.5f);
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            go.GetComponent<TooltipTrigger>().Text = "용병 관계(친밀도)";
            go.GetComponent<Button>().onClick.AddListener(() => { if (Instance != null) Instance.Toggle(); });

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>());
            var text = label.GetComponent<Text>();
            text.text = "관계";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        public static void ShowGlobal(Mercenary merc = null)
        {
            if (Instance != null) Instance.Show(merc);
        }

        public void Show(Mercenary merc = null)
        {
            if (merc != null) _selectedId = merc.Id;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
        }

        public void Hide() => _panel.SetActive(false);

        public void Toggle()
        {
            if (_panel.activeSelf) Hide();
            else Show();
        }

        private void RefreshIfOpen()
        {
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        // ---------- 그리기 ----------

        private void Refresh()
        {
            foreach (Transform child in _tabs) Destroy(child.gameObject);
            foreach (Transform child in _rows) Destroy(child.gameObject);

            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            var selected = members.FirstOrDefault(m => m.Id == _selectedId) ?? members.FirstOrDefault();
            _selectedId = selected?.Id;

            // 이름 탭(한 줄에 들어가는 만큼, 넘치면 다음 줄)
            int perRow = Mathf.Max(1, Mathf.FloorToInt((Width - Pad * 2f + TabGap) / (TabWidth + TabGap)));
            for (int i = 0; i < members.Count; i++)
                CreateTab(members[i], members[i] == selected, i % perRow, i / perRow);
            int tabRows = Mathf.Max(1, Mathf.CeilToInt(members.Count / (float)perRow));
            float y = 56f + tabRows * (TabHeight + TabGap) + 6f;

            SetTop(_header.rectTransform, y, 24f);
            y += 30f;

            var others = selected == null ? new List<(Mercenary merc, int value)>() : Affinity.Friends(selected);
            if (selected == null || others.Count == 0)
            {
                _header.text = selected == null ? "용병이 없습니다." : $"{selected.Name}의 관계";
                var empty = CreateText("Empty", 15, _rows, $"<color={Dim}>관계를 볼 동료가 없습니다. 용병을 더 고용해 보세요.</color>");
                SetTop(empty.rectTransform, y, 24f);
                y += 30f;
            }
            else
            {
                _header.text = $"<b>{selected.Name}</b>의 관계";
                foreach (var (other, value) in others)
                {
                    CreateRow(selected, other, value, y);
                    y += RowHeight + RowGap;
                }
            }

            y += Pad;

            _panelRect.sizeDelta = new Vector2(Width, y);
            UIThemeApplier.ApplyNow(_panel.transform);
        }

        private void CreateTab(Mercenary merc, bool selected, int column, int row)
        {
            var go = new GameObject("Tab", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_tabs, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad + column * (TabWidth + TabGap), -(56f + row * (TabHeight + TabGap)));
            rect.sizeDelta = new Vector2(TabWidth, TabHeight);
            go.GetComponent<Image>().color = selected ? new Color(0.3f, 0.6f, 0.3f, 1f) : new Color(0.5f, 0.25f, 0.25f, 1f);
            string id = merc.Id;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                _selectedId = id;
                Refresh();
            });

            var label = CreateText("Text", 14, go.transform, merc.Name);
            Stretch(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            if (!selected) return;
            // 고른 탭 아래 금색 밑줄(자기 그림이라 테마가 바꾸지 않는다)
            var line = CreateBox("Selected", go.transform, Affinity.Tiers[Affinity.Tiers.Length - 1].Color);
            var lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(1f, 0f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = new Vector2(0f, -1f);
            lineRect.sizeDelta = new Vector2(0f, 3f);
        }

        /// <summary>동료 한 명: 이름(+상태) | 가운데 0 막대 | 값 | 단계 | 궁합. 줄 전체에 단계 효과 툴팁.</summary>
        private void CreateRow(Mercenary self, Mercenary other, int value, float top)
        {
            var tier = Affinity.TierOf(value);
            var row = CreateBox("Row", _rows, new Color(0f, 0f, 0f, 0.35f));
            row.raycastTarget = true; // 툴팁
            var rowRect = row.rectTransform;
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(Pad, -top);
            rowRect.sizeDelta = new Vector2(Width - Pad * 2f, RowHeight);

            int compat = Affinity.Compatibility(self.Personality, other.Personality);
            string speed = compat > 0 ? "오르는 속도 ×1.5" : compat < 0 ? "오르는 속도 ×0.5 · 실패하면 서로 탓함" : "오르는 속도 ×1";
            row.gameObject.AddComponent<TooltipTrigger>().Text =
                $"{self.Name} ↔ {other.Name}: {value} ({tier.Name}, {Affinity.RangeText(tier)})\n{tier.EffectText}\n성격 궁합 {Affinity.CompatibilityLabel(compat)} · {speed}";

            // 이름 + 상태
            string status = ExpeditionLog.Instance.IsOnExpedition(other) ? " <size=12><color=" + Dim + ">(파견 중)</color></size>"
                : TrainingHall.IsTraining(other) ? " <size=12><color=" + Dim + ">(훈련 중)</color></size>" : "";
            var name = CreateText("Name", 15, row.transform, other.Name + status);
            Place(name.rectTransform, 10f, 160f);

            // 가운데가 0인 막대: 양수는 오른쪽, 음수는 왼쪽으로 단계 색이 찬다.
            const float barX = 175f;
            var back = CreateBox("BarBack", row.transform, new Color(0f, 0f, 0f, 0.6f));
            Place(back.rectTransform, barX, BarWidth, BarHeight);
            float half = BarWidth * 0.5f;
            float length = half * Mathf.Abs(value) / Affinity.Max;
            if (length > 0.5f)
            {
                var fill = CreateBox("BarFill", back.transform, tier.Color);
                var fillRect = fill.rectTransform;
                fillRect.anchorMin = fillRect.anchorMax = new Vector2(0.5f, 0.5f);
                fillRect.pivot = new Vector2(value >= 0 ? 0f : 1f, 0.5f);
                fillRect.anchoredPosition = Vector2.zero;
                fillRect.sizeDelta = new Vector2(length, BarHeight);
            }
            var zero = CreateBox("Zero", back.transform, new Color(1f, 1f, 1f, 0.5f));
            var zeroRect = zero.rectTransform;
            zeroRect.anchorMin = zeroRect.anchorMax = new Vector2(0.5f, 0.5f);
            zeroRect.sizeDelta = new Vector2(2f, BarHeight + 4f);

            var number = CreateText("Value", 15, row.transform, value > 0 ? $"+{value}" : value.ToString());
            Place(number.rectTransform, barX + BarWidth + 8f, 44f);
            number.alignment = TextAnchor.MiddleRight;

            var tierText = CreateText("Tier", 15, row.transform, $"<color={tier.ColorHex}><b>{tier.Name}</b></color>");
            Place(tierText.rectTransform, barX + BarWidth + 64f, 50f);

            var compatText = CreateText("Compat", 13, row.transform, $"<color={Dim}>궁합</color> {Affinity.CompatibilityLabel(compat)}");
            Place(compatText.rectTransform, barX + BarWidth + 116f, 100f);
        }

        // ---------- 틀 ----------

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _white.name = "RelationsWhite"; // 자기 그림으로 보여 테마가 색을 바꾸지 않는다

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            _panelRect = _panel.GetComponent<RectTransform>();
            _panelRect.sizeDelta = new Vector2(Width, 400f); // 화면 가운데, 높이는 Refresh가 맞춘다
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(_panelRect);

            var title = CreateText("Title", 22, _panel.transform, "용병 관계");
            title.fontStyle = FontStyle.Bold;
            title.color = UITheme.TitleText;
            SetTop(title.rectTransform, 16f, 32f);

            _tabs = CreateLayer("Tabs");
            _rows = CreateLayer("Rows");
            _header = CreateText("Header", 17, _panel.transform, "");

            // 닫기
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            close.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Hide);
            var x = CreateText("Text", 16, close.transform, "X");
            x.alignment = TextAnchor.MiddleCenter;
            Stretch(x.rectTransform);

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        /// <summary>탭·줄을 다시 그릴 때 통째로 비우는 빈 묶음(패널 전체 크기).</summary>
        private Transform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_panel.transform, false);
            Stretch(go.GetComponent<RectTransform>());
            return go.transform;
        }

        private Image CreateBox(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = _white;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string name, int size, Transform parent, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        /// <summary>패널 왼쪽 위 기준 top 위치에 전체 폭(좌우 Pad)으로 놓는다.</summary>
        private static void SetTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(Width - Pad * 2f - 40f, height);
        }

        /// <summary>줄 안에서 왼쪽 x 위치, 세로 가운데에 놓는다.</summary>
        private static void Place(RectTransform rect, float x, float width, float height = RowHeight)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
            if (rect.TryGetComponent<Text>(out var text) && text.alignment == TextAnchor.UpperLeft)
                text.alignment = TextAnchor.MiddleLeft;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
