using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 근처 길드 창(왼쪽 위 "근처 길드" 버튼): 왼쪽에 명성 순위(우리 길드 포함), 오른쪽에 고른 길드의 성향·단계·관계·소속 용병·소식.
    /// 줄에 마우스를 올리면 자세한 설명(GuildPanel처럼 짧은 줄 + 툴팁). 선물·영입·판매·대여는 여기서 한다.
    /// 끌어 옮길 수 있고 X·ESC로 닫는다. BestiaryPanel과 같은 틀.
    /// </summary>
    public class RivalGuildsPanel : MonoBehaviour
    {
        private const float Width = 1040f;
        private const float Height = 690f;
        private const float ListX = 20f, ListWidth = 340f, ListTop = 64f, RowHeight = 80f;
        private const float DetailX = 380f, DetailWidth = 640f;
        private const float RosterTop = 262f, RosterBottom = 512f;
        private const float BarWidth = 260f;
        private const string Muted = "#a3937c";
        private const string Hint = "  <color=#7a7064>(?)</color>";
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color RowSelected = new Color(0.95f, 0.72f, 0.3f, 0.22f);
        private static readonly Color OurRow = new Color(0.4f, 0.6f, 0.9f, 0.16f);

        public static RivalGuildsPanel Instance { get; private set; }

        private GameObject _panel;
        private Font _font;
        private Sprite _white;
        private Text _place;
        private readonly List<(Image back, Text label, TooltipTrigger tip, Button button)> _rows = new List<(Image, Text, TooltipTrigger, Button)>();
        private Text _name, _info, _relationLabel, _rosterTitle, _news;
        private TooltipTrigger _infoTip, _relationTip;
        private RectTransform _relationFill;
        private Image _relationFillImage;
        private Button _giftButton;
        private Text _giftText;
        private RectTransform _roster;
        private readonly List<GameObject> _rosterRows = new List<GameObject>();
        private readonly List<(Button button, Text label)> _actions = new List<(Button, Text)>();
        private string _selectedId;

        /// <summary>오른쪽 목록에 무엇을 보이나: 그 길드 소속 / 팔 우리 용병 / 빌려줄 우리 용병 / 맞바꿀 우리 용병.</summary>
        private enum ListMode { Roster, Sell, Lend, Trade, Barter }
        private ListMode _mode;
        private Mercenary _tradeTarget;

        public static RivalGuildsPanel Create()
        {
            var go = new GameObject("RivalGuildsInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RivalGuildsPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<RivalGuildsPanel>();
            panel.Build();
            Instance = panel;
            RivalGuilds.OnChanged += panel.RefreshIfOpen;
            Guild.OnChanged += panel.OnGuildChanged;
            Wallet.OnChanged += panel.OnGoldChanged;
            return panel;
        }

        private void OnDestroy()
        {
            RivalGuilds.OnChanged -= RefreshIfOpen;
            Guild.OnChanged -= OnGuildChanged;
            Wallet.OnChanged -= OnGoldChanged;
        }

        private void OnGuildChanged(bool _) => RefreshIfOpen();
        private void OnGoldChanged(int _) => RefreshIfOpen();

        public static void ShowGlobal()
        {
            if (Instance != null) Instance.Toggle();
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

        public void RefreshIfOpen()
        {
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        // ---------- 그리기 ----------

        private RivalGuild Selected => RivalGuilds.Get(_selectedId ?? "") ?? RivalGuilds.All.FirstOrDefault();

        private void Refresh()
        {
            var ranking = RivalGuilds.Ranking();
            _place.text = $"우리 길드 {RivalGuilds.OurPlace}위 / {ranking.Count}";
            var selected = Selected;
            _selectedId = selected?.Id;

            for (int i = 0; i < _rows.Count; i++)
            {
                var (back, label, tip, button) = _rows[i];
                back.gameObject.SetActive(i < ranking.Count);
                if (i >= ranking.Count) continue;
                var (name, reputation, g) = ranking[i];
                var rank = Guild.Ranks[Guild.RankFor(reputation)];
                button.onClick.RemoveAllListeners();
                if (g == null)
                {
                    back.color = OurRow;
                    label.text = $"<b>{i + 1}. {name}</b>\n<color={Muted}>{Guild.CurrentName} · 명성 {reputation} · 용병 {PlayerParty.Instance.Members.Count}명</color>";
                    tip.Text = $"<b>우리 길드</b> — {Guild.CurrentName}\n임무에 성공해 명성을 올리면 순위가 오른다.\n근처 길드도 매일 의뢰를 맡아 명성이 오르내린다.";
                    continue;
                }
                back.color = g == selected ? RowSelected : RowColor;
                label.text = $"<b>{i + 1}. {g.Name}</b>  <color={RivalGuilds.RelationColor(g.Relation)}>{RivalGuilds.RelationLabel(g.Relation)}</color>\n" +
                             $"<color={Muted}>{rank.Name} · 명성 {reputation} · 용병 {g.Roster.Count}명</color>";
                tip.Text = GuildTooltip(g);
                var id = g.Id;
                button.onClick.AddListener(() => Select(id));
            }

            if (selected != null) ShowDetail(selected);
        }

        private static string GuildTooltip(RivalGuild g)
        {
            string text = $"<b>{g.Name}</b> — 성향 {g.Info.Label}\n{g.Info.Description}\n\n관계 {g.Relation} {RivalGuilds.RelationLabel(g.Relation)}: {RivalGuilds.RelationEffect(g.Relation)}";
            if (g.News.Count > 0)
                text += "\n\n<b>최근 소식</b>\n" + string.Join("\n", g.News.Take(3).Select(n => $"{RivalGuilds.ShortDate(n.Day)}  {n.Text}"));
            return text;
        }

        private void ShowDetail(RivalGuild g)
        {
            var rank = g.RankInfo;
            _name.text = g.Name;
            _info.text = $"성향 {g.Info.Label} · {rank.Name} · 명성 {g.Reputation} · 금화 약 {Mathf.RoundToInt(g.Gold / 100f) * 100}G{Hint}\n" +
                         $"<color={Muted}>소속 용병 {g.Roster.Count}명 · 전투력 합 {g.TotalPower} · 진행 중 의뢰 {g.Jobs.Count}건</color>";
            string jobs = g.Jobs.Count == 0 ? "지금 맡은 의뢰가 없다."
                : string.Join("\n", g.Jobs.Select(j => $"· {j.Title} — {RivalGuilds.ShortDate(j.EndDay)}에 끝남"));
            _infoTip.Text = $"<b>성향 {g.Info.Label}</b>\n{g.Info.Description}\n\n<b>단계</b> {rank.Name}: {Guild.UnlockText(rank)}\n\n<b>진행 중 의뢰</b>\n{jobs}";

            float t = (g.Relation - RivalGuilds.MinRelation) / (float)(RivalGuilds.MaxRelation - RivalGuilds.MinRelation);
            _relationFill.sizeDelta = new Vector2(BarWidth * t, _relationFill.sizeDelta.y);
            _relationFillImage.color = HexColor(RivalGuilds.RelationColor(g.Relation));
            _relationLabel.text = $"관계  <color={RivalGuilds.RelationColor(g.Relation)}><b>{RivalGuilds.RelationLabel(g.Relation)}</b> {g.Relation}</color>{Hint}";
            _relationTip.Text = $"<b>관계 {RivalGuilds.RelationLabel(g.Relation)}</b>\n{RivalGuilds.RelationEffect(g.Relation)}\n\n" +
                                "적대 -50 · 경쟁 -15 · 중립 · 우호 15 · 동맹 50\n선물·용병 판매·대여·연합 의뢰로 오르고, 용병을 빼 오면 내려간다.\n일주일마다 1씩 중립 쪽으로 돌아간다.";

            bool canGift = RivalGuilds.CanGift(g);
            _giftButton.interactable = canGift && Wallet.Gold >= RivalGuilds.GiftCost;
            _giftText.text = canGift ? $"선물 {RivalGuilds.GiftCost}G" : "오늘 선물함";

            DrawActions(g);

            foreach (var row in _rosterRows) Destroy(row);
            _rosterRows.Clear();
            if (_mode == ListMode.Trade && (_tradeTarget == null || !g.Roster.Contains(_tradeTarget))) _mode = ListMode.Roster;
            if (_mode == ListMode.Roster)
            {
                var lent = Loans.LentOut.Where(l => l.GuildId == g.Id).Select(l => $"{l.Merc.Name} {Mathf.Max(0, l.EndDay - GameClock.CurrentDay)}일").ToList();
                _rosterTitle.text = $"<b>소속 용병</b>  <color={Muted}>{g.Roster.Count}명 · 줄을 누르면 정보</color>" +
                                    (lent.Count > 0 ? $"  <color=#6ab8ff>빌려준 우리 용병: {string.Join(", ", lent)}</color>" : "");
                foreach (var merc in g.Roster.OrderByDescending(m => m.CombatPower))
                    _rosterRows.Add(CreateRosterRow(g, merc, true));
            }
            else
            {
                _rosterTitle.text = _mode switch
                {
                    ListMode.Barter => $"<b>{Josa.WaGwa(g.Name)} 교역</b>  <color={Muted}>전리품 하나 → {RivalDeals.BarterMaterial(g).Name} · 오늘 남은 {RivalDeals.BartersLeft(g)}번</color>",
                    ListMode.Sell => $"<b>{g.Name}에 팔 우리 용병</b>  <color={Muted}>위약금 없이 계약째 넘긴다</color>",
                    ListMode.Lend => $"<b>{g.Name}에 빌려줄 우리 용병</b>  <color={Muted}>{Loans.LoanDays}일 · 그동안 파티 자리는 비워 둔다</color>",
                    _ => $"<b>{_tradeTarget.Name}({_tradeTarget.Class.ClassName} · 전투력 {_tradeTarget.CombatPower})와 맞바꿀 우리 용병</b>",
                };
                if (_mode == ListMode.Barter)
                {
                    var loot = LootBag.Entries.Where(e => !e.item.Material && e.count > 0).ToList();
                    if (loot.Count == 0) _rosterRows.Add(CreateBarterRow(g, null, 0));
                    foreach (var (item, count) in loot) _rosterRows.Add(CreateBarterRow(g, item, count));
                }
                else
                    foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive && !Loans.IsGuest(m)).OrderByDescending(m => m.CombatPower))
                        _rosterRows.Add(CreateRosterRow(g, merc, false));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_roster);

            _news.text = "<b>소식</b>\n" + (g.News.Count == 0 ? $"<color={Muted}>아직 소식이 없다.</color>"
                : string.Join("\n", g.News.Take(5).Select(n => $"<color={Muted}>{RivalGuilds.ShortDate(n.Day)}</color>  {n.Text}")));
        }

        private void Select(string id)
        {
            if (_selectedId != id)
            {
                _mode = ListMode.Roster;
                _tradeTarget = null;
            }
            _selectedId = id;
            Refresh();
        }

        private void SetMode(ListMode mode, Mercenary tradeTarget = null)
        {
            _mode = mode;
            _tradeTarget = tradeTarget;
            Refresh();
        }

        private void Gift()
        {
            var g = Selected;
            if (g == null) return;
            if (RivalGuilds.Gift(g)) ToastLog.Show($"{g.Name}에 {RivalGuilds.GiftCost}G를 선물했습니다.", false);
            else ToastLog.Show(RivalGuilds.CanGift(g) ? "골드가 부족합니다." : "오늘은 이미 선물했습니다.", false);
            Refresh();
        }

        // ---------- 거래 버튼(관계·이적·대여) ----------

        /// <summary>상세 위 버튼 줄(선물 오른쪽): 길드와의 거래.</summary>
        private void DrawActions(RivalGuild g)
        {
            bool hostile = RivalGuilds.IsHostile(g);
            SetAction(0, _mode == ListMode.Sell ? "> 팔기 목록" : "우리 용병 팔기", () => SetMode(_mode == ListMode.Sell ? ListMode.Roster : ListMode.Sell), !hostile,
                hostile ? "적대 관계라 거래하지 않는다." : "우리 용병을 이 길드에 판다(위약금 없음, 관계 +3).\n소속 용병 줄의 [맞교환]으로 1:1로 바꿀 수도 있다.");
            bool canLend = g.Relation > -15;
            SetAction(1, _mode == ListMode.Lend ? "> 빌려주기 목록" : "우리 용병 빌려주기", () => SetMode(_mode == ListMode.Lend ? ListMode.Roster : ListMode.Lend), canLend,
                canLend ? $"우리 용병을 {Loans.LoanDays}일 빌려주고 하루 대여료를 받는다(관계 +4). 돌아올 때 조금 자라 있다." : "경쟁·적대 관계라 우리 용병을 받지 않는다.");
            // 소속 용병 목록일 때는 그 자리가 [교역](전리품 → 재료), 다른 목록일 때는 [소속 용병 보기]
            if (_mode == ListMode.Roster)
            {
                string barter = RivalDeals.BarterBlock(g);
                var material = RivalDeals.BarterMaterial(g);
                SetAction(2, "전리품 교역", () => SetMode(ListMode.Barter), barter == null,
                    barter ?? $"남는 전리품을 이 길드의 건설 재료로 바꾼다(오늘 {material.Name}, 남은 {RivalDeals.BartersLeft(g)}번 · 관계 +{RivalDeals.BarterRelation}).");
            }
            else SetAction(2, "소속 용병 보기", () => SetMode(ListMode.Roster), true, "이 길드 소속 용병 목록으로 돌아간다.");
        }

        private void SetAction(int i, string label, UnityEngine.Events.UnityAction onClick, bool interactable, string tooltip, bool visible = true)
        {
            var (button, text) = _actions[i];
            button.gameObject.SetActive(visible);
            text.text = label;
            button.interactable = interactable;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
            button.GetComponent<TooltipTrigger>().Text = tooltip;
        }

        /// <summary>목록 한 줄. theirs면 그 길드 소속(영입·대여·맞교환), 아니면 우리 용병(지금 모드의 팔기·빌려주기·맞교환).</summary>
        private GameObject CreateRosterRow(RivalGuild g, Mercenary merc, bool theirs)
        {
            var row = new GameObject(merc.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_roster, false);
            var image = row.GetComponent<Image>();
            image.sprite = _white;
            image.color = RowColor;
            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = layoutElement.preferredHeight = 46f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 8, 4, 4);
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // 두 줄: [등급] 이름 · 전투력 / 직업 · 나이(버튼 셋이 들어갈 폭을 남긴다)
            var label = CreateFlowText(row.transform, $"{GradeTable.RichLabel(merc.Grade)} <b>{merc.Name}</b>  <color=#f0a54a>전투력 {merc.CombatPower}</color>\n" +
                                                      $"<size=13><color={Muted}>{merc.Class.ClassName} · {merc.Age}세</color></size>", 15);
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.minWidth = 200f;
            labelLayout.flexibleWidth = 1f;

            if (theirs) AddRosterButtons(row.transform, g, merc);
            else AddOurButtons(row.transform, g, merc);
            RowClickHandler.Attach(row, () => MercenaryInfoPanel.ShowGlobal(merc));
            return row;
        }

        /// <summary>교역 줄: 전리품 이름·개수·값 / 받을 재료 + [바꾸기]. item이 null이면 "바꿀 전리품이 없다" 안내 줄.</summary>
        private GameObject CreateBarterRow(RivalGuild g, LootItem item, int count)
        {
            var row = new GameObject(item != null ? item.Name : "Empty", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_roster, false);
            var image = row.GetComponent<Image>();
            image.sprite = _white;
            image.color = RowColor;
            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = layoutElement.preferredHeight = 46f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 8, 4, 4);
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            string text = item == null ? $"<color={Muted}>바꿀 전리품이 없다. 원정에서 몬스터를 쓰러뜨리면 생긴다.</color>"
                : $"<b>{item.Name}</b> ×{count}  <color={Muted}>개당 {item.Price}G</color>\n" +
                  $"<size=13><color=#f0a54a>→ {RivalDeals.BarterMaterial(g).Name} {RivalDeals.BarterAmount(g, item)}개</color></size>";
            var label = CreateFlowText(row.transform, text, 15);
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.minWidth = 200f;
            labelLayout.flexibleWidth = 1f;
            if (item == null) return row;

            string block = RivalDeals.BarterBlock(g, item);
            CreateRowButton(row.transform, "바꾸기", 90f, () => { RivalDeals.Barter(g, item); Refresh(); },
                block ?? $"{item.Name} 하나를 {RivalDeals.BarterMaterial(g).Name} {RivalDeals.BarterAmount(g, item)}개로 바꾼다.").interactable = block == null;
            return row;
        }

        /// <summary>소속 용병 줄 오른쪽 버튼(영입 제의·대여).</summary>
        private void AddRosterButtons(Transform row, RivalGuild g, Mercenary merc)
        {
            int fee = RivalDeals.TransferFee(g, merc);
            string poach = RivalDeals.PoachBlock(g, merc);
            int poachRelation = RivalGuilds.IsFriendly(g) ? RivalDeals.PoachFriendlyRelation : RivalDeals.PoachRelation;
            CreateRowButton(row, $"영입 {fee}G", 104f, () => { RivalDeals.BeginPoach(g, merc); Refresh(); },
                poach ?? $"이적료 {fee}G를 내고 계약 협상을 한다(주급·계약금은 따로).\n협상이 깨지면 이적료는 돌려받는다. 관계 {poachRelation}").interactable = poach == null;

            int daily = Loans.BorrowFee(g, merc);
            string borrow = Loans.BorrowBlock(g, merc);
            CreateRowButton(row, $"대여 {daily}G/일", 104f, () => { Loans.Borrow(g, merc); Refresh(); },
                borrow ?? $"{Loans.LoanDays}일 빌린다(정원 밖 손님, 계약·주급 없음, 하루 {daily}G).\n죽으면 보상금 {Pricing.HireCost(merc)}G와 관계 {Loans.DeathRelation}.").interactable = borrow == null;

            bool core = RivalDeals.IsCore(g, merc);
            bool hostile = RivalGuilds.IsHostile(g);
            CreateRowButton(row, "맞교환", 70f, () => SetMode(ListMode.Trade, merc),
                hostile ? "적대 관계라 거래하지 않는다." : core ? "길드의 핵심이라 내주지 않는다." : "우리 용병 한 명과 맞바꾼다(차액은 금화로). 누르면 우리 용병별 차액이 보인다.").interactable = !hostile && !core;
        }

        private void AddOurButtons(Transform row, RivalGuild g, Mercenary merc)
        {
            switch (_mode)
            {
                case ListMode.Sell:
                {
                    int offer = RivalDeals.SellOffer(g, merc);
                    string block = RivalDeals.SellBlock(g, merc) ?? (offer <= 0 ? "그 길드가 살 여유(정원·금화)가 없다." : null);
                    CreateRowButton(row, offer > 0 ? $"팔기 +{offer}G" : "팔기", 120f, () => { RivalDeals.Sell(g, merc); Refresh(); },
                        block ?? $"{offer}G를 받고 보낸다(위약금 없음). 관계 +{RivalDeals.SellRelation}").interactable = block == null;
                    break;
                }
                case ListMode.Lend:
                {
                    int daily = Loans.LendFee(g, merc);
                    string block = Loans.LendBlock(g, merc);
                    CreateRowButton(row, $"빌려주기 +{daily}G/일", 150f, () => { Loans.Lend(g, merc); Refresh(); },
                        block ?? $"{Loans.LoanDays}일 동안 {g.Name}에서 일한다. 하루 {daily}G를 받는다. 주급은 그대로 나간다.").interactable = block == null;
                    break;
                }
                case ListMode.Trade:
                {
                    var target = _tradeTarget;
                    int diff = RivalDeals.TradeDifference(g, merc, target);
                    string block = RivalDeals.TradeBlock(g, merc, target);
                    string money = diff > 0 ? $"-{diff}G" : diff < 0 ? $"+{-diff}G" : "±0";
                    CreateRowButton(row, $"맞교환 {money}", 130f, () => { if (RivalDeals.Trade(g, merc, target)) SetMode(ListMode.Roster); else Refresh(); },
                        block ?? $"{Josa.EulReul(merc.Name)} 보내고 {Josa.EulReul(target.Name)} 받는다.\n{Josa.EunNeun(target.Name)} 남은 계약을 이어받는다(주급은 자기 시세).").interactable = block == null;
                    break;
                }
            }
        }

        private Button CreateRowButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction onClick, string tooltip)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(TooltipTrigger));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = width;
            layout.flexibleWidth = 0f;
            go.GetComponent<TooltipTrigger>().Text = tooltip;
            var text = CreateFlowText(go.transform, label, 15);
            text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        // ---------- 틀 ----------

        private void Build()
        {
            _font = UITheme.Font;
            _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _white.name = "RivalWhite"; // 자기 그림으로 보여 테마가 색을 바꾸지 않는다

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var panelRect = _panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(Width, Height); // 화면 가운데
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(panelRect);

            var title = CreateText("Title", 22, _panel.transform, "근처 길드", 20f, -16f, 300f, 32f);
            title.fontStyle = FontStyle.Bold;
            title.color = UITheme.TitleText;
            _place = CreateText("Place", 16, _panel.transform, "", Width - 300f, -22f, 240f, 24f);
            _place.alignment = TextAnchor.UpperRight;
            _place.color = UITheme.MutedText;

            // 왼쪽 순위(근처 길드 4 + 우리 길드)
            for (int i = 0; i < 5; i++)
            {
                var row = new GameObject("Row" + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger));
                row.transform.SetParent(_panel.transform, false);
                Place(row.GetComponent<RectTransform>(), ListX, -(ListTop + i * RowHeight), ListWidth, RowHeight - 6f);
                var back = row.GetComponent<Image>();
                back.sprite = _white;
                back.color = RowColor;
                var label = CreateText("Label", 17, row.transform, "", 12f, -12f, ListWidth - 24f, RowHeight - 20f);
                label.lineSpacing = 1.15f;
                _rows.Add((back, label, row.GetComponent<TooltipTrigger>(), row.GetComponent<Button>()));
            }
            var help = CreateText("Help", 15, _panel.transform,
                "명성은 의뢰에 성공하면 오르고 실패하면 깎인다.\n근처 길드도 매일 의뢰를 맡고, 같은 시장에서 용병을 고용하며, 우리 게시판 의뢰를 가로채기도 한다.",
                ListX, -(ListTop + 5 * RowHeight + 8f), ListWidth, 160f);
            help.color = UITheme.MutedText;

            // 오른쪽 상세
            _name = CreateText("Name", 24, _panel.transform, "", DetailX, -ListTop, DetailWidth, 34f);
            _name.fontStyle = FontStyle.Bold;
            _name.color = UITheme.TitleText;
            _info = CreateText("Info", 16, _panel.transform, "", DetailX, -(ListTop + 40f), DetailWidth, 48f);
            _info.raycastTarget = true;
            _infoTip = _info.gameObject.AddComponent<TooltipTrigger>();

            // 관계 막대
            _relationLabel = CreateText("Relation", 16, _panel.transform, "", DetailX, -(ListTop + 96f), 260f, 24f);
            _relationLabel.raycastTarget = true;
            _relationTip = _relationLabel.gameObject.AddComponent<TooltipTrigger>();
            var barBack = new GameObject("RelationBar", typeof(RectTransform), typeof(Image));
            barBack.transform.SetParent(_panel.transform, false);
            Place(barBack.GetComponent<RectTransform>(), DetailX + 270f, -(ListTop + 101f), BarWidth, 14f);
            var barImage = barBack.GetComponent<Image>();
            barImage.sprite = _white;
            barImage.color = new Color(0f, 0f, 0f, 0.45f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBack.transform, false);
            _relationFill = fill.GetComponent<RectTransform>();
            Place(_relationFill, 0f, 0f, BarWidth * 0.5f, 14f);
            _relationFillImage = fill.GetComponent<Image>();
            _relationFillImage.sprite = _white;
            var mid = new GameObject("Middle", typeof(RectTransform), typeof(Image));
            mid.transform.SetParent(barBack.transform, false);
            Place(mid.GetComponent<RectTransform>(), BarWidth * 0.5f - 1f, 0f, 2f, 14f);
            var midImage = mid.GetComponent<Image>();
            midImage.sprite = _white;
            midImage.color = new Color(1f, 1f, 1f, 0.5f);

            // 버튼 줄: 선물 + 거래
            _giftButton = CreateButton("Gift", DetailX, -(ListTop + 132f), 140f, Gift, out _giftText,
                $"{RivalGuilds.GiftCost}G를 보내 관계를 올린다(하루 한 번). 성향에 따라 오르는 폭이 다르다.");
            for (int i = 0; i < 3; i++)
            {
                var button = CreateButton("Action" + i, DetailX + 150f + i * 160f, -(ListTop + 132f), 150f, null, out var label, "");
                button.gameObject.SetActive(false);
                _actions.Add((button, label));
            }

            // 소속 용병(스크롤)
            _rosterTitle = CreateText("RosterTitle", 16, _panel.transform, "", DetailX, -(RosterTop - 30f), DetailWidth, 24f);
            var content = new GameObject("Roster", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(_panel.transform, false);
            _roster = content.GetComponent<RectTransform>();
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childControlWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollListWrapper.Wrap(_roster, new Vector2(DetailX, Height - RosterBottom), new Vector2(-(Width - DetailX - DetailWidth), -RosterTop));

            // 소식
            _news = CreateText("News", 15, _panel.transform, "", DetailX, -(RosterBottom + 12f), DetailWidth, Height - RosterBottom - 24f);
            _news.lineSpacing = 1.1f;

            // 닫기
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            close.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Hide);
            var x = CreateText("Text", 16, close.transform, "X", 0f, 0f, 0f, 0f);
            x.alignment = TextAnchor.MiddleCenter;
            Stretch(x.rectTransform);

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        private Button CreateButton(string name, float x, float y, float width, UnityEngine.Events.UnityAction onClick, out Text label, string tooltip)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger));
            go.transform.SetParent(_panel.transform, false);
            Place(go.GetComponent<RectTransform>(), x, y, width, 38f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            go.GetComponent<TooltipTrigger>().Text = tooltip;
            label = CreateText("Label", 16, go.transform, "", 0f, 0f, 0f, 0f);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        private static Color HexColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private Text CreateText(string name, int size, Transform parent, string content, float x, float y, float width, float height)
        {
            var text = CreateFlowText(parent, content, size);
            text.gameObject.name = name;
            Place(text.rectTransform, x, y, width, height);
            text.alignment = TextAnchor.UpperLeft;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>레이아웃 그룹 안에 넣는 글자(위치는 레이아웃이 정한다).</summary>
        private Text CreateFlowText(Transform parent, string content, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = UITheme.BodyText;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }
    }
}
