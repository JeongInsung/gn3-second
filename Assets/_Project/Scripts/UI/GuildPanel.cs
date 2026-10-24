using System.Linq;
using System.Text;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을의 길드 건물(또는 왼쪽 위 길드 글자)을 클릭하면 뜨는 창: 길드 티어(몇 / 10단계)·명성 막대 아래에 짧은 줄 다섯 개
    /// (인원·주급·지금 혜택·다음 단계·전체 단계) — 자세한 내용은 줄에 마우스를 올리면 툴팁으로 나온다. 아래에 "창고" 버튼(GuildStorageUI).
    /// 명성·파티·파견이 바뀌면 열린 채로 다시 그린다. 끌어 옮길 수 있고 X·ESC로 닫는다.
    /// </summary>
    public class GuildPanel : MonoBehaviour
    {
        private const float Width = 680f;
        private const float Height = 420f;
        private const float BodyTop = 144f;   // 판 위에서 첫 줄까지
        private const float RowHeight = 36f;
        private const int RowCount = 5;       // 인원 · 주급 · 지금 혜택 · 다음 단계 · 전체 단계
        private const string Hint = "  <color=#7a7064>(?)</color>"; // 마우스를 올리면 설명이 있다는 표시
        private const string Gold = "#f0c060";
        private const string Dim = "#7a7064";
        private const string Red = "#e05a4a";

        public static GuildPanel Instance { get; private set; }

        private GameObject _panel;
        private Text _title;
        private Text _tier;
        private Image _repFill;
        private Text _repText;
        private TooltipTrigger _repTip;
        private readonly Text[] _rows = new Text[RowCount];
        private readonly TooltipTrigger[] _tips = new TooltipTrigger[RowCount];
        private GameObject _storageButton;
        private GameObject _facilityButton;
        private Text _storageLabel;
        private Font _font;
        private static System.Action _openStorage;

        /// <summary>"창고" 버튼이 할 일(길드 건물로 건물 패널을 연다). 연결이 없으면 버튼을 숨긴다.</summary>
        public static void SetStorageOpener(System.Action open) => _openStorage = open;

        public static GuildPanel Create()
        {
            var go = new GameObject("GuildInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GuildPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<GuildPanel>();
            panel.Build();
            Instance = panel;

            Guild.OnChanged += panel.OnGuildChanged;
            PlayerParty.Instance.OnChanged += panel.RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged += panel.RefreshIfOpen;
            GuildStorage.OnChanged += panel.RefreshIfOpen;
            return panel;
        }

        private void OnDestroy()
        {
            Guild.OnChanged -= OnGuildChanged;
            PlayerParty.Instance.OnChanged -= RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged -= RefreshIfOpen;
            GuildStorage.OnChanged -= RefreshIfOpen;
        }

        private void OpenStorage()
        {
            Hide();
            _openStorage?.Invoke();
        }

        /// <summary>[시설]: 같은 길드 건물 패널을 "길드 시설" 목록으로 연다(GuildStorageUI.ShowFacilities).</summary>
        private void OpenFacilities()
        {
            Hide();
            GuildStorageUI.RequestFacilities();
            _openStorage?.Invoke();
        }

        /// <summary>창 판(옮기기 버튼을 붙일 곳 — BuildingMoveButton).</summary>
        public GameObject PanelRoot => _panel;

        public static void ShowGlobal()
        {
            if (Instance != null) Instance.Show();
        }

        public void Show()
        {
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
        }

        public void Hide() => _panel.SetActive(false);

        private void OnGuildChanged(bool rankUp) => RefreshIfOpen();

        private void RefreshIfOpen()
        {
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        private void Refresh()
        {
            var ranks = Guild.Ranks;
            int rank = Guild.Rank;
            var current = Guild.Current;

            _title.text = $"모험가 길드 — <color={Gold}>{Guild.CurrentName}</color>";

            string pips = new string('●', rank + 1) + new string('○', ranks.Length - rank - 1);
            _tier.text = $"길드 티어 <color={Gold}><b>{rank + 1}</b></color> / {ranks.Length}단계   <color={Gold}>{pips}</color>";

            // 명성 막대: 현재 단계 시작 ~ 다음 단계 조건 사이 진행도
            if (Guild.IsMaxRank)
            {
                _repFill.fillAmount = 1f;
                _repText.text = $"명성 {Guild.Reputation} · 최고 단계";
            }
            else
            {
                var next = ranks[rank + 1];
                int from = current.Required;
                _repFill.fillAmount = Mathf.Clamp01((float)(Guild.Reputation - from) / (next.Required - from));
                _repText.text = $"명성 {Guild.Reputation} / {next.Required}  (다음 단계까지 {next.Required - Guild.Reputation})";
            }

            _repTip.Text = $"<b>명성</b>\n임무 성공 시 +10×(퀘스트 등급+1)\n실패 시 −{Guild.FailPenalty}";

            // 1. 인원
            var party = PlayerParty.Instance;
            int total = party.Members.Count;
            int away = party.Members.Count(m => ExpeditionLog.Instance.IsOnExpedition(m));
            bool full = total >= party.MaxSize;
            SetRow(0, $"<b>인원</b>   {total} / {party.MaxSize}명" + (full ? $"  <color={Red}>· 새 고용 불가</color>" : ""),
                $"<b>수용 인원</b>\n마을 {total - away}명 · 파견 중 {away}명" +
                (full ? "\n정원이 가득 차 새로 고용할 수 없습니다" : $"\n빈자리 {party.MaxSize - total}명") +
                "\n\n정원은 길드 단계가 오르면 늘어납니다");

            // 2. 주급
            SetRow(1, $"<b>주급</b>   {MercenaryCondition.TotalWeeklyWage()}G / 주",
                $"<b>주급</b>\n계약한 용병들의 주급 합계\n다음 지급: {GN3.World.GameCalendar.Format(MercenaryCondition.NextPayday)} 자정");

            // 3. 지금 혜택
            SetRow(2, $"<b>지금 혜택</b>   정원 {current.PartySize}명 · 창고 {current.StorageSlots}칸",
                $"<b>{Guild.CurrentName}</b>\n{UnlockLines(current)}");

            // 4. 다음 단계
            if (Guild.IsMaxRank)
                SetRow(3, $"<b>다음 단계</b>   <color={Gold}>최고 단계</color>", "더 오를 단계가 없습니다");
            else
            {
                var next = ranks[rank + 1];
                string nextName = Guild.RankName(rank + 1);
                // 길드 홀이 없으면 동빛 용병단 위로는 못 오른다(Guild.MercenaryBandMaxRank)
                string condition = Guild.IsCapped ? "길드 홀 필요 (명성은 충분)"
                    : $"명성 {next.Required} (앞으로 {next.Required - Guild.Reputation})" + (!Guild.IsGuild && rank + 1 > Guild.MercenaryBandMaxRank ? " · 길드 홀 필요" : "");
                SetRow(3, $"<b>다음 단계</b>   {nextName} — {condition}",
                    $"<b>{nextName}</b>{Josa.IGa(nextName).Substring(nextName.Length)} 되면\n{UnlockLines(next)}");
            }

            // 5. 전체 단계
            var table = new StringBuilder("<b>전체 단계</b>");
            for (int i = 0; i < ranks.Length; i++)
            {
                var r = ranks[i];
                string line = $"{i + 1}. {Guild.RankName(i)} · 명성 {r.Required} · 정원 {r.PartySize}명";
                if (i == rank) line = $"<color={Gold}>{line}  ◀ 현재</color>";
                else if (i > rank) line = $"<color={Dim}>{line}</color>";
                table.Append('\n').Append(line);
            }
            SetRow(4, $"<b>전체 단계</b>   {ranks.Length}단계 중 {rank + 1}단계", table.ToString());

            _storageButton.SetActive(_openStorage != null);
            _facilityButton.SetActive(_openStorage != null && GuildFacilities.Enabled);
            _storageLabel.text = $"창고 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)";
        }

        private void SetRow(int i, string text, string tooltip)
        {
            _rows[i].text = text + Hint;
            _tips[i].Text = tooltip;
        }

        /// <summary>단계 해금 내용을 툴팁용 여러 줄로.</summary>
        private static string UnlockLines(Guild.RankInfo info) =>
            $"퀘스트 {info.MaxQuestGrade}급까지\n용병 재능 {info.MaxMercGrade}급까지\n시장 경력 +{info.MarketMinGrowth}~{info.MarketMaxGrowth}\n파티 정원 {info.PartySize}명\n창고 {info.StorageSlots}칸";

        /// <summary>마우스를 올리면 설명이 뜨는 줄(글자에 레이캐스트를 켠다).</summary>
        private TooltipTrigger WithTooltip(Text text)
        {
            text.raycastTarget = true;
            return text.gameObject.AddComponent<TooltipTrigger>();
        }

        // ---------- 화면 ----------

        private void Build()
        {
            _font = UITheme.Font;

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height); // 화면 가운데
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(rect);

            _title = CreateText("Title", 22, FontStyle.Bold, new Vector2(20f, -16f), new Vector2(Width - 70f, 32f));
            _title.color = UITheme.TitleText;
            _tier = CreateText("Tier", 18, FontStyle.Normal, new Vector2(20f, -56f), new Vector2(Width - 40f, 26f));

            // 명성 막대
            var barBack = new GameObject("RepBar", typeof(RectTransform), typeof(Image));
            barBack.transform.SetParent(_panel.transform, false);
            var barRect = barBack.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = new Vector2(20f, -92f);
            barRect.sizeDelta = new Vector2(Width - 40f, 12f);
            barBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBack.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            _repFill = fill.GetComponent<Image>();
            _repFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _repFill.type = Image.Type.Filled;
            _repFill.fillMethod = Image.FillMethod.Horizontal;
            _repFill.color = new Color(0.94f, 0.72f, 0.3f);

            _repText = CreateText("Rep", 14, FontStyle.Normal, new Vector2(20f, -108f), new Vector2(Width - 40f, 20f));
            _repTip = WithTooltip(_repText);

            // 짧은 줄들(자세한 건 툴팁). 줄 전체 폭에 마우스가 걸리게 해 올리기 쉽게.
            for (int i = 0; i < RowCount; i++)
            {
                _rows[i] = CreateText("Row" + i, UITheme.BodySize, FontStyle.Normal, new Vector2(20f, -BodyTop - i * RowHeight), new Vector2(Width - 40f, RowHeight));
                _rows[i].alignment = TextAnchor.MiddleLeft;
                _tips[i] = WithTooltip(_rows[i]);
            }

            // 창고(아래 가운데): 길드 건물 패널을 "길드 창고"로 연다
            _storageButton = new GameObject("StorageButton", typeof(RectTransform), typeof(Image), typeof(Button));
            _storageButton.transform.SetParent(_panel.transform, false);
            var storageRect = _storageButton.GetComponent<RectTransform>();
            storageRect.anchorMin = storageRect.anchorMax = storageRect.pivot = new Vector2(0.5f, 0f);
            storageRect.anchoredPosition = new Vector2(GuildFacilities.Enabled ? -110f : 0f, 16f); // 시설이 꺼져 있으면 가운데
            storageRect.sizeDelta = new Vector2(200f, 40f);
            _storageButton.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 테마 버튼으로
            _storageButton.GetComponent<Button>().onClick.AddListener(OpenStorage);
            _storageLabel = CreateText("Text", 16, FontStyle.Normal, Vector2.zero, Vector2.zero, _storageButton.transform);
            _storageLabel.alignment = TextAnchor.MiddleCenter;
            _storageLabel.rectTransform.anchorMin = Vector2.zero;
            _storageLabel.rectTransform.anchorMax = Vector2.one;
            _storageLabel.rectTransform.offsetMin = _storageLabel.rectTransform.offsetMax = Vector2.zero;

            // 시설(창고 오른쪽): 훈련소·병원·온천·숙소·창고 증축
            _facilityButton = new GameObject("FacilityButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger));
            _facilityButton.transform.SetParent(_panel.transform, false);
            var facilityRect = _facilityButton.GetComponent<RectTransform>();
            facilityRect.anchorMin = facilityRect.anchorMax = facilityRect.pivot = new Vector2(0.5f, 0f);
            facilityRect.anchoredPosition = new Vector2(110f, 16f);
            facilityRect.sizeDelta = new Vector2(200f, 40f);
            _facilityButton.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);
            _facilityButton.GetComponent<Button>().onClick.AddListener(OpenFacilities);
            _facilityButton.GetComponent<TooltipTrigger>().Text = "훈련소·병원·온천·숙소·창고를 금화와 마정석으로 증축한다(각 3단계).";
            var facilityLabel = CreateText("Text", 16, FontStyle.Normal, Vector2.zero, Vector2.zero, _facilityButton.transform);
            facilityLabel.text = "시설";
            facilityLabel.alignment = TextAnchor.MiddleCenter;
            facilityLabel.rectTransform.anchorMin = Vector2.zero;
            facilityLabel.rectTransform.anchorMax = Vector2.one;
            facilityLabel.rectTransform.offsetMin = facilityLabel.rectTransform.offsetMax = Vector2.zero;

            // 닫기
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            close.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Hide);
            var x = CreateText("Text", 16, FontStyle.Normal, Vector2.zero, Vector2.zero, close.transform);
            x.text = "X";
            x.color = Color.white;
            x.alignment = TextAnchor.MiddleCenter;
            x.rectTransform.anchorMin = Vector2.zero;
            x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        private Text CreateText(string name, int size, FontStyle style, Vector2 topLeft, Vector2 boxSize, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent != null ? parent : _panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = boxSize;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = UITheme.BodyText;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
