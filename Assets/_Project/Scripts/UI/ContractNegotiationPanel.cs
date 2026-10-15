using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Traits;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 풋볼매니저식 계약 협상 창. 주급·계약 기간·계약금·성공 보너스를 정해 제안하면 용병이
    /// 받아들이거나(서명) · 역제안하거나(모자란 만큼 주급을 올린 조건) · 불쾌해한다. 기분 나쁜 제안이 쌓여 인내심이 바닥나면 협상이 깨진다
    /// (시장 용병은 이번 목록에서 다시 협상할 수 없고, 재계약은 다음 날까지 못 한다).
    /// 용병이 바라는 조건(ContractTerms.For)은 숨은 값이라 "에이전트 귀띔" 범위와 대답으로만 짐작한다.
    /// 시장 [협상](OpenHire) · 파티 창·용병 정보 [재계약](OpenRenewal)이 연다. 정렬 2: 시장(0)·상세 창(1) 위.
    /// </summary>
    public class ContractNegotiationPanel : MonoBehaviour
    {
        private const float Width = 940f, Height = 600f;
        private const float StartRatio = 0.9f; // 첫 제안은 요구보다 조금 낮게(협상할 여지 — 대개 역제안이 온다)

        private static ContractNegotiationPanel _open;
        // 협상이 깨진 용병 id → 그날. 시장 용병은 시장을 새로 뽑을 때까지, 재계약은 그날만 다시 못 한다.
        private static readonly Dictionary<string, int> BrokenOff = new Dictionary<string, int>();

        private Mercenary _merc;
        private bool _renewal;
        private System.Action _onSigned;
        private System.Action _onClosed;
        private ContractDemand _demand;
        private ContractOffer _offer;
        private ContractOffer? _counter;
        private ContractOffer? _agreed; // 용병이 받아들인 조건 — [계약 확정하기]를 눌러야 서명된다
        private int _patience;
        private bool _closed;

        private Text _wageText, _feeText, _bonusText, _summaryText, _replyText, _patienceText;
        private readonly List<(int days, Button button)> _lengthButtons = new List<(int, Button)>();
        private Button _proposeButton, _acceptCounterButton, _confirmButton;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _open = null;
            BrokenOff.Clear();
        }

        /// <summary>시장 목록을 새로 뽑았다: 깨졌던 시장 협상은 잊는다(재계약 결렬은 그날까지 유지).</summary>
        public static void ForgetMarketBreakdowns()
        {
            var hired = new HashSet<string>();
            foreach (var m in PlayerParty.Instance.Members) hired.Add(m.Id);
            var forget = new List<string>();
            foreach (var id in BrokenOff.Keys) if (!hired.Contains(id)) forget.Add(id);
            foreach (var id in forget) BrokenOff.Remove(id);
        }

        public static bool IsBrokenOff(Mercenary merc) =>
            merc != null && BrokenOff.TryGetValue(merc.Id, out int day)
            && (!PlayerParty.Instance.Members.Contains(merc) || day == GameClock.CurrentDay);

        /// <summary>시장 용병과 계약 협상. 서명하면 onSigned(시장 줄 지우기), 창을 닫으면 onClosed(결렬 표시).</summary>
        public static void OpenHire(Mercenary merc, System.Action onSigned, System.Action onClosed = null) => Open(merc, false, onSigned, onClosed);

        /// <summary>고용한 용병과 재계약 협상.</summary>
        public static void OpenRenewal(Mercenary merc) => Open(merc, true, null, null);

        private static void Open(Mercenary merc, bool renewal, System.Action onSigned, System.Action onClosed)
        {
            if (merc == null) return;
            if (IsBrokenOff(merc))
            {
                ToastLog.Show(renewal ? $"{Josa.EunNeun(merc.Name)} 오늘은 더 이야기하고 싶어 하지 않습니다." : $"{Josa.WaGwa(merc.Name)}의 협상은 이미 깨졌습니다.", false);
                return;
            }
            var demand = ContractTerms.For(merc, renewal);
            if (demand.Interest == Interest.NotInterested)
            {
                ToastLog.Show(renewal ? $"{Josa.EunNeun(merc.Name)} 재계약할 생각이 없습니다 (사기가 바닥)." : $"{Josa.EunNeun(merc.Name)} 우리 길드에 관심이 없습니다.", false);
                return;
            }
            if (_open != null) _open.Close();

            var go = new GameObject("ContractNegotiation", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ContractNegotiationPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _open = go.GetComponent<ContractNegotiationPanel>();
            _open._onClosed = onClosed;
            _open.Init(merc, renewal, onSigned, demand);
            EscapeCloser.Register(go, _open.Close, WindowRole.Popup);
        }

        private void Init(Mercenary merc, bool renewal, System.Action onSigned, ContractDemand demand)
        {
            _merc = merc;
            _renewal = renewal;
            _onSigned = onSigned;
            _demand = demand;
            _patience = demand.Patience;
            var fair = ContractTerms.Fair(demand);
            _offer = new ContractOffer
            {
                WeeklyWage = Mathf.Max(1, Mathf.RoundToInt(fair.WeeklyWage * StartRatio)),
                LengthDays = renewal && Contracts.Get(merc) != null ? ContractTuning.DefaultLengthDays : fair.LengthDays,
                SigningFee = Mathf.RoundToInt(fair.SigningFee * StartRatio / 5f) * 5,
                WinBonus = 0,
            };
            Build();
            Say(Greeting());
            Refresh();
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_open == this) _open = null;
            Destroy(gameObject);
            _onClosed?.Invoke();
        }

        // ---------- 협상 ----------

        private void Propose()
        {
            if (_patience <= 0) return;
            switch (ContractTerms.Evaluate(_demand, _offer))
            {
                case OfferResult.Accepted:
                    Agree(_offer, "좋습니다. 이 조건이면 서명하겠습니다.");
                    return;
                case OfferResult.Counter:
                    _patience -= 1;
                    _counter = ContractTerms.Counter(_demand, _offer);
                    Say(_patience > 0
                        ? $"나쁘지 않군요. 주급을 {_counter.Value.WeeklyWage}G로 맞춰 주면 서명하겠습니다."
                        : "이 정도로는 어렵겠군요. 다른 길드를 알아보겠습니다.");
                    break;
                default:
                    // 조금 모자라면 인내심 1, 터무니없이 낮으면(LowballBelow 미만) 2를 잃는다
                    bool lowball = ContractTerms.Satisfaction(_demand, _offer) < ContractTuning.LowballBelow;
                    _patience -= lowball ? 2 : 1;
                    _counter = null;
                    Say(_patience <= 0 ? "장난하십니까? 더 이야기할 것 없습니다."
                        : lowball ? Insulted() : "그 조건으로는 어렵습니다. 조금 더 생각해 주십시오.");
                    break;
            }
            if (_patience <= 0)
            {
                _patience = 0;
                _counter = null;
                BrokenOff[_merc.Id] = GameClock.CurrentDay;
                ToastLog.Show($"{Josa.WaGwa(_merc.Name)}의 협상이 깨졌습니다.", false);
            }
            Refresh();
        }

        private void AcceptCounter()
        {
            if (_counter == null) return;
            _offer = _counter.Value;
            Agree(_offer, "그럼 그 조건으로 하지요. 서명만 남았습니다.");
        }

        /// <summary>합의: 아직 서명 전. 조건을 다시 확인하고 [계약 확정하기]를 눌러야 계약이 맺어진다.</summary>
        private void Agree(ContractOffer offer, string reply)
        {
            _agreed = offer;
            _counter = null;
            Say(reply);
            Refresh();
        }

        private void Confirm()
        {
            if (_agreed != null) Sign(_agreed.Value); // 골드·정원이 모자라 막히면 합의는 그대로 두고 대답으로 알린다
        }

        private void Sign(ContractOffer offer)
        {
            var party = PlayerParty.Instance;
            if (!_renewal && party.Members.Count >= party.MaxSize)
            {
                Say("자리가 없는 길드와는 계약할 수 없군요. (파티 정원이 가득 찼습니다)");
                return;
            }
            if (!Wallet.TrySpend(offer.SigningFee))
            {
                Say($"계약금 {offer.SigningFee}G를 먼저 준비해 오십시오. (보유 {Wallet.Gold}G)");
                return;
            }
            if (!_renewal && !party.TryAdd(_merc))
            {
                Wallet.Add(offer.SigningFee);
                return;
            }
            Contracts.Sign(_merc, offer);
            if (_renewal)
            {
                MercenaryCondition.CancelContractLeaving(_merc);
                _merc.AddMorale(5); // 다시 믿어 준 길드
            }
            string line = $"{_merc.Name} {(_renewal ? "재계약" : "계약")}: 주급 {offer.WeeklyWage}G · {offer.LengthDays}일" +
                          (offer.SigningFee > 0 ? $" · 계약금 -{offer.SigningFee}G" : "") + (offer.WinBonus > 0 ? $" · 성공 보너스 {offer.WinBonus}G" : "");
            GN3.Quests.DailyLog.Add(line);
            // 우편함에 기록(게임을 멈추지 않는 편지). 화면에는 토스트 대신 축하 카드.
            Mailbox.Post(MailKind.Notice, _renewal ? $"{_merc.Name} 재계약" : $"{_merc.Name} 영입",
                $"{_merc.Class.ClassName} · {_merc.Age}세 · 전투력 {_merc.CombatPower}\n" +
                $"주급 {offer.WeeklyWage}G · {offer.LengthDays}일({GameCalendar.Format(GameClock.CurrentDay + offer.LengthDays)}까지)" +
                (offer.SigningFee > 0 ? $"\n계약금 {offer.SigningFee}G" : "") + (offer.WinBonus > 0 ? $"\n성공 보너스 {offer.WinBonus}G" : ""));
            _onSigned?.Invoke(); // 용병 정보 창은 스스로 매 프레임 새로 그린다
            var merc = _merc;
            bool renewal = _renewal;
            Close();
            RecruitCelebration.Show(merc, offer, renewal);
        }

        private string Greeting()
        {
            if (_renewal)
                return _demand.Interest == Interest.Keen ? "이 길드가 마음에 듭니다. 조건만 맞으면 남겠습니다."
                    : _demand.Interest == Interest.Reluctant ? "솔직히 요즘은 마음이 떠나 있습니다. 확실한 조건이 필요합니다."
                    : "계약 이야기를 할 때가 됐군요.";
            return _demand.Interest switch
            {
                Interest.Keen => "평판을 들었습니다. 함께 일하고 싶습니다.",
                Interest.Reluctant => "제가 왜 이 길드에 가야 하는지 설득해 보시죠.",
                _ => "조건을 들어 보겠습니다.",
            };
        }

        private string Insulted() => _merc.Personality switch
        {
            Personality.Hotblooded or Personality.Reckless => "그 돈으로 날 부리겠다고? 다시 생각해 오시오!",
            Personality.Confident => "제 가치를 모르시는군요. 그 금액은 받아들일 수 없습니다.",
            Personality.Coward or Personality.Nervous => "그, 그건 좀... 너무 적은 것 같아요.",
            _ => "그 조건으로는 어렵습니다. 한참 모자랍니다.",
        };

        private void Say(string text)
        {
            if (_replyText != null) _replyText.text = $"\"{text}\"";
        }

        // ---------- 값 바꾸기 ----------

        private void ChangeWage(int delta) { _offer.WeeklyWage = Mathf.Max(1, _offer.WeeklyWage + delta); OfferChanged(); }
        private void ChangeFee(int delta) { _offer.SigningFee = Mathf.Max(0, _offer.SigningFee + delta); OfferChanged(); }
        private void ChangeBonus(int delta) { _offer.WinBonus = Mathf.Max(0, _offer.WinBonus + delta); OfferChanged(); }
        private void SetLength(int days) { _offer.LengthDays = days; OfferChanged(); }

        private void OfferChanged()
        {
            // 합의한 뒤 조건을 바꾸면 합의가 풀린다(인내심은 그대로)
            if (_agreed != null && !_agreed.Value.Equals(_offer))
            {
                _agreed = null;
                Say("조건이 바뀌었군요. 다시 제안해 주십시오.");
            }
            Refresh();
        }

        private void Refresh()
        {
            _wageText.text = $"{_offer.WeeklyWage}G";
            _feeText.text = $"{_offer.SigningFee}G";
            _bonusText.text = $"{_offer.WinBonus}G";
            foreach (var (days, button) in _lengthButtons)
            {
                bool chosen = days == _offer.LengthDays;
                button.GetComponent<Image>().color = chosen ? new Color(1f, 0.75f, 0.3f) : new Color(0.75f, 0.72f, 0.7f);
                var label = button.GetComponentInChildren<Text>();
                label.color = chosen ? UITheme.TitleText : UITheme.MutedText;
                label.fontStyle = chosen ? FontStyle.Bold : FontStyle.Normal;
            }
            int weeks = Mathf.CeilToInt(_offer.LengthDays / 7f);
            _summaryText.text = (_agreed != null ? "<color=#f0c050>[합의 완료]</color>  " : "") + $"지금 낼 돈: 계약금 {_offer.SigningFee}G (보유 {Wallet.Gold}G)\n" +
                                $"계약 기간 주급 합계 약 {_offer.WeeklyWage * weeks}G ({weeks}주) · {GameCalendar.Format(GameClock.CurrentDay + _offer.LengthDays)}까지";
            _patienceText.text = "인내심 " + new string('●', Mathf.Max(0, _patience)) + new string('○', Mathf.Max(0, _demand.Patience - _patience));
            bool open = _patience > 0;
            bool agreed = _agreed != null;
            _proposeButton.interactable = open;
            _proposeButton.gameObject.SetActive(!agreed);
            _confirmButton.gameObject.SetActive(agreed);
            if (agreed)
                _confirmButton.GetComponentInChildren<Text>().text = _agreed.Value.SigningFee > 0 ? $"계약 확정하기 (-{_agreed.Value.SigningFee}G)" : "계약 확정하기";
            _acceptCounterButton.gameObject.SetActive(open && !agreed && _counter != null);
            if (_counter != null) _acceptCounterButton.GetComponentInChildren<Text>().text = $"역제안 받아들이기 (주급 {_counter.Value.WeeklyWage}G)";
        }

        /// <summary>귀띔: 실제 요구의 ±15% 범위(정확한 값은 숨긴다).</summary>
        private string AgentHint()
        {
            float s = ContractTuning.AgentHintSpread;
            int wLow = Mathf.Max(1, Mathf.FloorToInt(_demand.WeeklyWage * (1f - s))), wHigh = Mathf.CeilToInt(_demand.WeeklyWage * (1f + s));
            int fee = Mathf.RoundToInt(_demand.SigningFee / 10f) * 10;
            string length = _demand.LengthPreference > 0 ? "긴 계약을 좋아함" : _demand.LengthPreference < 0 ? "짧은 계약을 좋아함" : "기간은 상관없음";
            string bonus = _demand.BonusValue >= ContractTuning.HighBonusValue ? " · 성공 보너스를 좋아함" : "";
            return $"에이전트 귀띔: 주급 {wLow}~{wHigh}G, 계약금 약 {fee}G 정도 · {length}{bonus}";
        }

        // ---------- 화면 ----------

        private void Build()
        {
            // 뒤를 어둡게(클릭은 막는다 — 협상 중엔 다른 걸 못 누르게)
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(transform, false);
            Stretch((RectTransform)dim.transform);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.sizeDelta = new Vector2(Width, Height);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;
            var p = panel.transform;

            var title = Label(p, _renewal ? "재계약 협상" : "계약 협상", UITheme.TitleSize, UITheme.TitleText, TextAnchor.MiddleCenter, new Vector2(0f, -18f), new Vector2(Width - 120f, 40f), centerX: true);
            title.name = "Title";
            title.font = UITheme.BoldFont;
            MakeButton(p, "X", Close, new Vector2(Width - 52f, -16f), new Vector2(36f, 32f));

            // 왼쪽: 용병
            var portrait = CharacterPortraitUI.Create(p, _merc.Appearance, 150, PortraitCrop.FullBody);
            var portraitRect = (RectTransform)portrait.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(40f, -76f);
            portraitRect.sizeDelta = new Vector2(150f, 150f);

            float lx = 30f, lw = 250f;
            var name = Label(p, $"{GradeTable.RichLabel(_merc.Grade)} {_merc.Name}", UITheme.HeadingSize, UITheme.BodyText, TextAnchor.UpperLeft, new Vector2(lx, -240f), new Vector2(lw, 28f));
            name.supportRichText = true;
            name.font = UITheme.BoldFont;
            Label(p, $"{_merc.Class.ClassName} · {_merc.Age}세 · 전투력 {_merc.CombatPower}", UITheme.BodySize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(lx, -272f), new Vector2(lw, 24f));
            Label(p, $"성격: {PersonalityTable.Get(_merc.Personality).Label}", UITheme.BodySize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(lx, -300f), new Vector2(lw, 24f));
            var interest = Label(p, (_renewal ? "남을 생각: " : "관심도: ") + ContractTerms.InterestLabel(_demand.Interest), UITheme.BodySize,
                ContractTerms.InterestColor(_demand.Interest), TextAnchor.UpperLeft, new Vector2(lx, -328f), new Vector2(lw, 24f));
            interest.name = "Interest";
            if (_renewal)
                Label(p, $"지금 계약: {Contracts.Describe(_merc)}", UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(lx, -356f), new Vector2(lw, 44f));
            _patienceText = Label(p, "", UITheme.BodySize, new Color(0.95f, 0.75f, 0.4f), TextAnchor.UpperLeft, new Vector2(lx, -404f), new Vector2(lw, 24f));

            // 가운데: 제안
            float cx = 310f;
            Label(p, "우리 제안", UITheme.HeadingSize, UITheme.TitleText, TextAnchor.UpperLeft, new Vector2(cx, -76f), new Vector2(300f, 28f)).font = UITheme.BoldFont;
            _wageText = StepperRow(p, "주급", -116f, new[] { -5, -1 }, new[] { 1, 5 }, ChangeWage);
            _feeText = StepperRow(p, _renewal ? "재계약금" : "계약금", -212f, new[] { -10 }, new[] { 10 }, ChangeFee);
            _bonusText = StepperRow(p, "성공 보너스", -260f, new[] { -5 }, new[] { 5 }, ChangeBonus);
            Label(p, "퀘스트 성공 때마다 살아 돌아온 용병에게 지급", UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(cx + 150f, -294f), new Vector2(460f, 20f));

            Label(p, "계약 기간", UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(cx, -164f), new Vector2(140f, 34f));
            float bx = cx + 150f;
            foreach (int days in ContractTuning.LengthOptions)
            {
                int d = days;
                var b = MakeButton(p, $"{days}일", () => SetLength(d), new Vector2(bx, -164f), new Vector2(84f, 34f));
                _lengthButtons.Add((days, b));
                bx += 92f;
            }

            Label(p, AgentHint(), UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(cx, -326f), new Vector2(Width - cx - 30f, 40f));
            _summaryText = Label(p, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.UpperLeft, new Vector2(cx, -372f), new Vector2(Width - cx - 30f, 50f));

            // 아래: 용병의 대답
            var bubble = new GameObject("Reply", typeof(RectTransform), typeof(Image));
            bubble.transform.SetParent(p, false);
            var bubbleRect = (RectTransform)bubble.transform;
            bubbleRect.anchorMin = bubbleRect.anchorMax = bubbleRect.pivot = new Vector2(0f, 1f);
            bubbleRect.anchoredPosition = new Vector2(30f, -440f);
            bubbleRect.sizeDelta = new Vector2(Width - 60f, 64f);
            var bubbleImage = bubble.GetComponent<Image>();
            bubbleImage.sprite = UITheme.Inset;
            bubbleImage.type = Image.Type.Sliced;
            _replyText = Label(bubble.transform, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(16f, 0f), new Vector2(Width - 92f, 64f));
            _replyText.rectTransform.anchorMin = _replyText.rectTransform.anchorMax = _replyText.rectTransform.pivot = new Vector2(0f, 1f);

            // 버튼
            _proposeButton = MakeButton(p, "제안하기", Propose, new Vector2(Width - 180f, -526f), new Vector2(150f, 44f));
            _acceptCounterButton = MakeButton(p, "역제안 받아들이기", AcceptCounter, new Vector2(Width - 520f, -526f), new Vector2(330f, 44f));
            _confirmButton = MakeButton(p, "계약 확정하기", Confirm, new Vector2(Width - 300f, -526f), new Vector2(270f, 44f));
            MakeButton(p, "그만두기", Close, new Vector2(30f, -526f), new Vector2(130f, 44f));

            UIThemeApplier.ApplyNow(transform);
            _confirmButton.GetComponent<Image>().color = new Color(1f, 0.8f, 0.4f); // 확정 버튼은 금빛으로 눈에 띄게(테마를 입힌 뒤)
        }

        /// <summary>"주급   [-5][-1]  22G  [+1][+5]" 한 줄. 값 글자를 돌려준다.</summary>
        private Text StepperRow(Transform parent, string label, float y, int[] minus, int[] plus, System.Action<int> change)
        {
            float x = 310f;
            Label(parent, label, UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(x, y), new Vector2(140f, 34f));
            x += 150f;
            foreach (int step in minus)
            {
                int s = step;
                MakeButton(parent, s.ToString(), () => change(s), new Vector2(x, y), new Vector2(52f, 34f));
                x += 58f;
            }
            var value = Label(parent, "", UITheme.HeadingSize, UITheme.TitleText, TextAnchor.MiddleCenter, new Vector2(x, y), new Vector2(90f, 34f));
            value.font = UITheme.BoldFont;
            x += 96f;
            foreach (int step in plus)
            {
                int s = step;
                MakeButton(parent, "+" + s, () => change(s), new Vector2(x, y), new Vector2(52f, 34f));
                x += 58f;
            }
            return value;
        }

        private static Text Label(Transform parent, string content, int size, Color color, TextAnchor anchor, Vector2 topLeft, Vector2 box, bool centerX = false)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = centerX ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = box;
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = UITheme.Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Vector2 topLeft, Vector2 size)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            var button = go.GetComponent<Button>();
            UITheme.StyleButton(button);
            button.onClick.AddListener(onClick);
            var text = Label(go.transform, label, UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleCenter, Vector2.zero, size);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
