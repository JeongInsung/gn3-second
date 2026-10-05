using System.Linq;
using System.Text;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을의 길드 건물(또는 왼쪽 위 길드 글자)을 클릭하면 뜨는 창: 길드 티어(몇 / 5단계)·명성 막대·파티 수용 인원·
    /// 지금 해금 내용·다음 단계 조건·전체 단계표. 명성·파티·파견이 바뀌면 열린 채로 다시 그린다. 끌어 옮길 수 있고 X·ESC로 닫는다.
    /// </summary>
    public class GuildPanel : MonoBehaviour
    {
        private const float Width = 560f;
        private const float Height = 600f;
        private const string Gold = "#f0c060";
        private const string Dim = "#7a7064";
        private const string Red = "#e05a4a";

        public static GuildPanel Instance { get; private set; }

        private GameObject _panel;
        private Text _title;
        private Text _tier;
        private Image _repFill;
        private Text _repText;
        private Text _body;
        private Font _font;

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
            return panel;
        }

        private void OnDestroy()
        {
            Guild.OnChanged -= OnGuildChanged;
            PlayerParty.Instance.OnChanged -= RefreshIfOpen;
            ExpeditionLog.Instance.OnChanged -= RefreshIfOpen;
        }

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

        private void Update()
        {
            if (_panel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Hide();
        }

        private void Refresh()
        {
            var ranks = Guild.Ranks;
            int rank = Guild.Rank;
            var current = Guild.Current;

            _title.text = $"모험가 길드 — <color={Gold}>{current.Name}</color>";

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

            var party = PlayerParty.Instance;
            int total = party.Members.Count;
            int away = party.Members.Count(m => ExpeditionLog.Instance.IsOnExpedition(m));
            string count = total > party.MaxSize ? $"<color={Red}>{total}</color>" : total.ToString();
            string full = total >= party.MaxSize ? $"  <color={Red}>(새 고용 불가)</color>" : $"  (빈자리 {party.MaxSize - total})";

            var sb = new StringBuilder();
            sb.Append($"<b>수용 인원</b>   파티 {count} / {party.MaxSize}명  (마을 {total - away} · 파견 중 {away}){full}\n\n");

            sb.Append("<b>지금 해금</b>\n");
            sb.Append($"  퀘스트 최고 {current.MaxQuestGrade}급 · 용병 최고 {current.MaxMercGrade}급\n");
            sb.Append($"  시장 Lv.{current.MarketMinLevel}~{current.MarketMaxLevel} · 파티 정원 {current.PartySize}명\n\n");

            if (!Guild.IsMaxRank)
            {
                var next = ranks[rank + 1];
                sb.Append($"<b>다음 단계</b>   {next.Name} — 명성 {next.Required} 필요\n");
                sb.Append($"  {Guild.UnlockText(next)}\n\n");
            }

            sb.Append("<b>전체 단계</b>\n");
            for (int i = 0; i < ranks.Length; i++)
            {
                var r = ranks[i];
                string line = $"  {i + 1}단계  {r.Name}   명성 {r.Required}   정원 {r.PartySize}명   퀘스트 {r.MaxQuestGrade} · 용병 {r.MaxMercGrade}";
                if (i == rank) line = $"<color={Gold}>{line}  ◀ 현재</color>";
                else if (i > rank) line = $"<color={Dim}>{line}</color>";
                sb.Append(line).Append('\n');
            }

            sb.Append($"\n<color={Dim}>임무 성공 시 명성 +10×(퀘스트 등급+1), 실패 시 −{Guild.FailPenalty}</color>");
            _body.text = sb.ToString();
        }

        // ---------- 화면 ----------

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            _body = CreateText("Body", 15, FontStyle.Normal, new Vector2(20f, -144f), new Vector2(Width - 40f, Height - 160f));
            _body.lineSpacing = 1.15f;

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
