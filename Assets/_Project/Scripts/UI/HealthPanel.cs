using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 용병 건강 창. 파티 전원을 한 줄씩: 이름 | 체력 막대 | 피로 | 사기 | 부상·질병(진행도 막대) | 위치(마을·파견·훈련·입원).
    /// 위에 입원·대기·부상·질병·지침 인원 요약. 줄에 마우스를 올리면 상태이상 효과·회복 속도·치료비, 클릭하면 용병 정보창.
    /// 오른쪽 아래 "건강" 버튼("관계" 왼쪽)으로 연다. 생김새·여닫기는 RelationsPanel과 같다.
    /// 진행도·체력은 이벤트 없이 바뀌므로 열려 있는 동안 1초마다 내용이 달라졌을 때만 다시 그린다(툴팁이 깜빡이지 않게).
    /// </summary>
    public class HealthPanel : MonoBehaviour
    {
        private const float Width = 820f;
        private const float Pad = 20f;
        private const float RowHeight = 38f;
        private const float RowGap = 4f;
        private const float HealthBarWidth = 120f;
        private const float BarHeight = 12f;
        private const float ChipWidth = 96f;
        private const float ChipGap = 6f;
        private const float ButtonHeight = 40f;
        private const float RefreshSeconds = 1f;
        private const string Dim = "#a3937c";
        private const string Warn = "#e0c060";
        private const string Bad = "#e06050";

        // 열 위치(줄 왼쪽 기준)
        private const float NameX = 10f;
        private const float HealthX = 165f;
        private const float FatigueX = 365f;
        private const float MoraleX = 415f;
        private const float AilmentX = 465f;
        private const float PlaceX = 670f;

        public static HealthPanel Instance { get; private set; }

        private GameObject _panel;
        private RectTransform _panelRect;
        private Transform _rows;
        private Text _summary;
        private Font _font;
        private Sprite _white;
        private float _nextRefresh;
        private string _lastSignature;

        public static HealthPanel Create()
        {
            var go = new GameObject("HealthInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(HealthPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<HealthPanel>();
            panel.Build();
            Instance = panel;

            PlayerParty.Instance.OnChanged += panel.RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged += panel.RefreshIfOpen;
            TrainingHall.OnChanged += panel.RefreshIfOpen;
            RestVenues.OnAnyChanged += panel.RefreshIfOpen;
            Hospital.OnChanged += panel.RefreshIfOpen;
            return panel;
        }

        private void OnDestroy()
        {
            PlayerParty.Instance.OnChanged -= RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged -= RefreshIfOpen;
            TrainingHall.OnChanged -= RefreshIfOpen;
            RestVenues.OnAnyChanged -= RefreshIfOpen;
            Hospital.OnChanged -= RefreshIfOpen;
        }

        /// <summary>화면 오른쪽 아래, "관계" 버튼 왼쪽의 "건강" 버튼. 누르면 건강 창을 열고 닫는다.</summary>
        public static void CreateButton(Transform parent)
        {
            var go = new GameObject("HealthButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-(MailboxButton.Margin + MailboxButton.Size + 10f + RelationsPanel.ButtonWidth + 8f),
                MailboxButton.Margin + (MailboxButton.Size - ButtonHeight) * 0.5f);
            rect.sizeDelta = new Vector2(RelationsPanel.ButtonWidth, ButtonHeight);
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            go.GetComponent<TooltipTrigger>().Text = "용병 건강(체력·부상·질병·입원)";
            go.GetComponent<Button>().onClick.AddListener(() => { if (Instance != null) Instance.Toggle(); });

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>());
            var text = label.GetComponent<Text>();
            text.text = "건강";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        public void Show()
        {
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

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (Signature(Members()) != _lastSignature) Refresh();
        }

        // ---------- 내용 ----------

        /// <summary>입원 중 → 중한 상태이상 → 상태이상 → 체력 비율 낮은 순.</summary>
        private static List<Mercenary> Members() =>
            PlayerParty.Instance.Members.Where(m => m.IsAlive)
                .OrderByDescending(m => Hospital.IsAdmitted(m))
                .ThenByDescending(m => m.IsSeverelyAiling)
                .ThenByDescending(m => m.HasAilment)
                .ThenBy(m => m.CurrentHealth / (float)Mathf.Max(1, m.CurrentStats.MaxHealth))
                .ToList();

        /// <summary>표에 보이는 값을 이은 문자열. 이전과 같으면 다시 그리지 않는다.</summary>
        private static string Signature(List<Mercenary> members)
        {
            var sb = new StringBuilder();
            foreach (var m in members)
                sb.Append(m.Id).Append(m.CurrentHealth).Append('/').Append(m.CurrentStats.MaxHealth)
                  .Append(m.Fatigue).Append(',').Append(m.Morale).Append(m.AilmentSummary()).Append(PlaceText(m)).Append('|');
            return sb.ToString();
        }

        private static string PlaceText(Mercenary merc)
        {
            if (ExpeditionLog.Instance.IsOnExpedition(merc)) return "파견 중";
            if (TrainingHall.IsTraining(merc)) return "훈련 중";
            if (RestVenues.IsResting(merc)) return $"{RestVenues.Find(merc).Name} 중";
            if (Hospital.IsAdmitted(merc)) return $"입원 중 (약 {Hospital.RemainingHours(merc):0}시간)";
            return "마을";
        }

        private static string Tooltip(Mercenary merc)
        {
            var lines = new List<string> { $"{merc.Name} · 체력 {merc.CurrentHealth}/{merc.CurrentStats.MaxHealth} · {MercenaryCondition.Describe(merc)}" };
            // 능력치 하락은 상태이상 칸에 마우스를 올리면(EffectTooltip). 여기는 회복·위험만.
            foreach (var ailment in merc.Ailments)
            {
                var def = ailment.Def;
                if (def.deathAfterDays > 0 && !Hospital.IsAdmitted(merc))
                    lines.Add($"{def.displayName} 방치 {ailment.DaysUntreated}/{def.deathAfterDays}일 — 입원시키지 않으면 위험");
            }
            if (merc.HasAilment)
            {
                lines.Add($"회복: 병원 시간당 {Hospital.ProgressPerHour * 100f:0}% · 마을 시간당 {Ailments.NaturalProgressPerHour * 100f:0}% (종류마다 배율) · 파견 중엔 낫지 않음");
                if (Hospital.IsAdmitted(merc)) lines.Add($"쌓인 치료비 {Hospital.Fee(merc)}G (퇴원할 때 냄)");
            }
            return string.Join("\n", lines);
        }

        // ---------- 그리기 ----------

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            foreach (Transform child in _rows) Destroy(child.gameObject);

            var members = Members();
            _lastSignature = Signature(members);

            // Hospital.Patients는 정리하면서 OnChanged를 울려 그리는 도중 다시 그리게 되므로 직접 센다.
            int admitted = members.Count(Hospital.IsAdmitted);
            int injured = members.Count(m => m.HasInjury), ill = members.Count(m => m.HasIllness), tired = members.Count(m => m.IsExhausted);
            _summary.text = $"<color={Dim}>입원 {admitted}/{Hospital.Capacity} · 대기 {Hospital.Waiting.Count} · 부상 {injured} · 질병 {ill} · 지침 {tired}</color>";

            float y = 86f;
            if (members.Count == 0)
            {
                var empty = CreateText("Empty", 15, _rows, $"<color={Dim}>용병이 없습니다.</color>");
                SetTop(empty.rectTransform, y, 24f);
                y += 30f;
            }
            else
            {
                CreateColumnHeader(y);
                y += 26f;
                foreach (var merc in members)
                {
                    CreateRow(merc, y);
                    y += RowHeight + RowGap;
                }
            }

            y += Pad;
            _panelRect.sizeDelta = new Vector2(Width, y);
            UIThemeApplier.ApplyNow(_panel.transform);
        }

        private void CreateColumnHeader(float top)
        {
            var header = CreateBox("ColumnHeader", _rows, new Color(0f, 0f, 0f, 0f));
            var rect = header.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(Width - Pad * 2f, 22f);
            foreach (var (x, w, title) in new[] { (NameX, 150f, "이름"), (HealthX, 190f, "체력"), (FatigueX, 44f, "피로"),
                         (MoraleX, 44f, "사기"), (AilmentX, 200f, "부상·질병"), (PlaceX, 105f, "위치") })
            {
                var text = CreateText("Col", 13, header.transform, $"<color={Dim}>{title}</color>");
                Place(text.rectTransform, x, w, 22f);
            }
        }

        private void CreateRow(Mercenary merc, float top)
        {
            var row = CreateBox("Row", _rows, new Color(0f, 0f, 0f, 0.35f));
            row.raycastTarget = true; // 툴팁·클릭
            var rowRect = row.rectTransform;
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(Pad, -top);
            rowRect.sizeDelta = new Vector2(Width - Pad * 2f, RowHeight);
            // 툴팁은 줄이 아니라 이름·상태이상 칸에 단다(줄에 달면 안쪽 칸의 툴팁을 덮는다). 클릭은 줄 전체.
            RowClickHandler.Attach(row.gameObject, () => MercenaryInfoPanel.ShowGlobal(merc));

            var name = CreateText("Name", 15, row.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}");
            Place(name.rectTransform, NameX, 150f);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddTip(name, Tooltip(merc));

            // 체력: 빨강→초록 막대 + 값(용병 정보창과 같은 색)
            int max = Mathf.Max(1, merc.CurrentStats.MaxHealth);
            float ratio = Mathf.Clamp01(merc.CurrentHealth / (float)max);
            var back = CreateBox("HealthBack", row.transform, new Color(0f, 0f, 0f, 0.6f));
            Place(back.rectTransform, HealthX, HealthBarWidth, BarHeight);
            Fill(back.transform, ratio, Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.3f, 0.8f, 0.35f), ratio), BarHeight);
            var hp = CreateText("Health", 13, row.transform, $"{merc.CurrentHealth}/{max}");
            Place(hp.rectTransform, HealthX + HealthBarWidth + 6f, 70f);

            string fatigueColor = merc.IsExhausted ? Bad : merc.Fatigue >= Mercenary.TiredFatigue ? Warn : "#ffffff";
            var fatigue = CreateText("Fatigue", 14, row.transform, $"<color={fatigueColor}>{merc.Fatigue}</color>");
            Place(fatigue.rectTransform, FatigueX, 44f);
            var morale = CreateText("Morale", 14, row.transform, $"<color={(merc.Morale < Mercenary.LowMorale ? Bad : "#ffffff")}>{merc.Morale}</color>");
            Place(morale.rectTransform, MoraleX, 44f);

            if (!merc.HasAilment)
            {
                var healthy = CreateText("Healthy", 13, row.transform, $"<color={Dim}>건강함</color>");
                Place(healthy.rectTransform, AilmentX, 200f);
            }
            else
            {
                float x = AilmentX;
                foreach (var ailment in merc.Ailments)
                {
                    CreateChip(row.transform, merc, ailment, x);
                    x += ChipWidth + ChipGap;
                }
            }

            string place = PlaceText(merc);
            string placeColor = Hospital.IsAdmitted(merc) ? "#ffffff" : place == "마을" ? Dim : Warn;
            var placeText = CreateText("Place", 13, row.transform, $"<color={placeColor}>{place}</color>");
            Place(placeText.rectTransform, PlaceX, 105f);
            placeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>상태이상 한 칸: 위에 이름(중하면 빨강) + 진행도 %, 아래에 진행도 막대(에셋의 표시 색).</summary>
        private void CreateChip(Transform row, Mercenary merc, Ailment ailment, float x)
        {
            var def = ailment.Def;
            var label = CreateText("Ailment", 12, row, $"<color={(def.isSevere ? Bad : "#ffffff")}>{def.displayName}</color> <color={Dim}>{Mathf.RoundToInt(ailment.Progress * 100f)}%</color>");
            Place(label.rectTransform, x, ChipWidth, 30f, 0f); // 막대까지 덮는 높이라 칸 어디에 올려도 툴팁이 뜬다
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddTip(label, Ailments.EffectTooltip(merc, ailment)); // 이 상태이상이 깎은 능력치

            var back = CreateBox("AilmentBack", row, new Color(0f, 0f, 0f, 0.6f));
            Place(back.rectTransform, x, ChipWidth, 5f, -9f);
            Fill(back.transform, ailment.Progress, def.color, 5f);
        }

        /// <summary>글자에 툴팁을 단다(마우스를 받게 raycastTarget을 켠다. 클릭은 위의 줄로 올라간다).</summary>
        private static void AddTip(Text text, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            text.raycastTarget = true;
            text.gameObject.AddComponent<TooltipTrigger>().Text = tip;
        }

        /// <summary>막대 바탕 안에 왼쪽부터 ratio만큼 채운다.</summary>
        private void Fill(Transform back, float ratio, Color color, float height)
        {
            if (ratio <= 0.001f) return;
            var fill = CreateBox("Fill", back, color);
            var rect = fill.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        // ---------- 틀 (RelationsPanel과 같다) ----------

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _white.name = "HealthWhite"; // 자기 그림으로 보여 테마가 색을 바꾸지 않는다

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            _panelRect = _panel.GetComponent<RectTransform>();
            _panelRect.sizeDelta = new Vector2(Width, 400f); // 화면 가운데, 높이는 Refresh가 맞춘다
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(_panelRect);

            var title = CreateText("Title", 22, _panel.transform, "용병 건강");
            title.fontStyle = FontStyle.Bold;
            title.color = UITheme.TitleText;
            SetTop(title.rectTransform, 16f, 32f);

            _summary = CreateText("Summary", 15, _panel.transform, "");
            SetTop(_summary.rectTransform, 54f, 24f);
            _rows = CreateLayer("Rows");

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

        private static void SetTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Pad, -top);
            rect.sizeDelta = new Vector2(Width - Pad * 2f - 40f, height);
        }

        /// <summary>줄 안에서 왼쪽 x 위치, 세로 가운데(+offsetY)에 놓는다.</summary>
        private static void Place(RectTransform rect, float x, float width, float height = RowHeight, float offsetY = 0f)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, offsetY);
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
