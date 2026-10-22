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
    /// 받아들이거나(합의) · 조금 내려온 값으로 역제안하거나 · 불쾌해한다. 밀고 당기기 규칙은 Negotiation.
    /// 용병이 처음 부르는 값 아래에 성향별 숨은 마지노선이 있어, 기분 게이지·대사·성향 귀띔으로 읽어 내야 한다.
    /// 심리전: [최종 제안](허세) · [자리 뜨기](밀어내기) · 설득 화법 3종 · 용병의 "다른 길드 제의"를 믿을지 말지.
    /// 기분이 바닥나거나 허세가 들통나면 협상이 깨진다(시장 용병은 이번 목록에서 다시 협상할 수 없고, 재계약은 다음 날까지 못 한다).
    /// 같은 날 창을 닫았다 열어도 협상 상태는 그대로다(닫기로 초기화하는 꼼수 방지).
    /// 시장 [협상](OpenHire) · 파티 창·용병 정보 [재계약](OpenRenewal)이 연다. 정렬 2: 시장(0)·상세 창(1) 위.
    /// </summary>
    public class ContractNegotiationPanel : MonoBehaviour
    {
        private const float Width = 940f, Height = 660f;
        private const float StartRatio = 0.9f; // 첫 제안은 요구보다 조금 낮게(협상할 여지 — 대개 역제안이 온다)

        private static ContractNegotiationPanel _open;
        // 협상이 깨진 용병 id → 그날. 시장 용병은 시장을 새로 뽑을 때까지, 재계약은 그날만 다시 못 한다.
        private static readonly Dictionary<string, int> BrokenOff = new Dictionary<string, int>();
        // 진행 중인 협상(용병 id → 그날·재계약 여부·상태·마지막 제안). 날이 바뀌면 새로 시작한다.
        private static readonly Dictionary<string, (int day, bool renewal, Negotiation negotiation, ContractOffer offer)> Sessions =
            new Dictionary<string, (int, bool, Negotiation, ContractOffer)>();

        private Mercenary _merc;
        private bool _renewal;
        private System.Action _onSigned;
        private System.Action _onClosed;
        private Negotiation _neg;
        private ContractDemand _demand;
        private ContractOffer _offer;
        private ContractOffer? _agreed; // 용병이 받아들인 조건 — [계약 확정하기]를 눌러야 서명된다
        private bool _closed;

        private Text _wageText, _feeText, _bonusText, _summaryText, _replyText, _moodText;
        private RectTransform _moodFill;
        private Image _moodFillImage;
        private readonly List<(int days, Button button)> _lengthButtons = new List<(int, Button)>();
        private readonly List<(TalkTopic topic, Button button)> _talkButtons = new List<(TalkTopic, Button)>();
        private Button _proposeButton, _finalButton, _acceptCounterButton, _confirmButton, _walkAwayButton, _doubtButton, _acknowledgeButton;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _open = null;
            BrokenOff.Clear();
            Sessions.Clear();
            Suitors.Clear();
        }

        /// <summary>시장 목록을 새로 뽑았다: 깨졌던·하다 만 시장 협상은 잊는다(재계약 결렬은 그날까지 유지).</summary>
        public static void ForgetMarketBreakdowns()
        {
            var hired = new HashSet<string>();
            foreach (var m in PlayerParty.Instance.Members) hired.Add(m.Id);
            foreach (var id in BrokenOff.Keys.Where(id => !hired.Contains(id)).ToList()) BrokenOff.Remove(id);
            foreach (var id in Sessions.Keys.Where(id => !hired.Contains(id)).ToList()) Sessions.Remove(id);
        }

        /// <summary>지금 이 용병과 협상 창이 열려 있거나 오늘 하다 만 협상이 있다(근처 길드가 가로채지 않는다).</summary>
        /// <summary>협상 중 "다른 길드 제의"에 나온 근처 길드(용병 id → 길드 id). 진짜 제의면 결렬될 때 그 길드로 간다.</summary>
        private static readonly Dictionary<string, string> Suitors = new Dictionary<string, string>();

        public static bool IsNegotiating(Mercenary merc) =>
            merc != null && ((_open != null && _open._merc == merc) || Sessions.ContainsKey(merc.Id));

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
            bool resumed = Sessions.TryGetValue(merc.Id, out var session) && session.day == GameClock.CurrentDay && session.renewal == renewal;
            if (resumed)
            {
                _neg = session.negotiation;
                _offer = session.offer;
            }
            else
            {
                _neg = new Negotiation(demand, ContractTerms.Seed(merc.Id));
                var fair = ContractTerms.Fair(demand);
                _offer = new ContractOffer
                {
                    WeeklyWage = Mathf.Max(1, Mathf.RoundToInt(fair.WeeklyWage * StartRatio)),
                    LengthDays = renewal && Contracts.Get(merc) != null ? ContractTuning.DefaultLengthDays : fair.LengthDays,
                    SigningFee = Mathf.RoundToInt(fair.SigningFee * StartRatio / 5f) * 5,
                    WinBonus = 0,
                };
            }
            _demand = _neg.Demand;
            Build();
            Say(resumed ? "또 오셨군요. 아까 하던 이야기를 계속하지요." : Greeting());
            Refresh();
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_open == this) _open = null;
            if (!_neg.Broken) Sessions[_merc.Id] = (GameClock.CurrentDay, _renewal, _neg, _offer);
            Destroy(gameObject);
            _onClosed?.Invoke();
        }

        // ---------- 협상 ----------

        private void Propose() => Handle(_neg.Propose(_offer), _offer);
        private void FinalOffer() => Handle(_neg.FinalOffer(_offer), _offer);
        private void WalkAway() => Handle(_neg.WalkAway(), _offer);
        private void DoubtRival() => Handle(_neg.DoubtRival(), _offer);
        private void AcknowledgeRival() => Handle(_neg.AcknowledgeRival(), _offer);
        private void Talk(TalkTopic topic) => Handle(_neg.Talk(topic), _offer, topic);

        private void Handle(NegotiationEvent e, ContractOffer offer, TalkTopic topic = TalkTopic.Praise)
        {
            if (e == NegotiationEvent.Accepted || e == NegotiationEvent.ClosedGap || e == NegotiationEvent.FinalAccepted)
            {
                Agree(offer, Line(e, topic));
                return;
            }
            string line = Line(e, topic);
            if (e == NegotiationEvent.Countered && _neg.RivalPending) line += " " + RivalClaim();
            Say(line);
            if (_neg.Broken)
            {
                if (e == NegotiationEvent.RivalLeft && Suitors.TryGetValue(_merc.Id, out var suitor))
                    RivalGuilds.SignAway(_merc, suitor, _renewal); // 시장 용병은 바로 그 길드로, 우리 용병은 계약이 끝나면
                BrokenOff[_merc.Id] = GameClock.CurrentDay;
                Sessions.Remove(_merc.Id);
                ToastLog.Show($"{Josa.WaGwa(_merc.Name)}의 협상이 깨졌습니다.", false);
            }
            Refresh();
        }

        private void AcceptCounter()
        {
            if (_neg.Counter == null) return;
            _offer = _neg.Counter.Value;
            Agree(_offer, "그럼 그 조건으로 하지요. 서명만 남았습니다.");
        }

        /// <summary>합의: 아직 서명 전. 조건을 다시 확인하고 [계약 확정하기]를 눌러야 계약이 맺어진다.</summary>
        private void Agree(ContractOffer offer, string reply)
        {
            _agreed = offer;
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
            // 협상 끝 기분이 남는다: 기분 좋게 서명하면 사기 +, 앙금이 남으면 사기 −
            if (_neg.Mood >= ContractTuning.SignedHappyMood) _merc.AddMorale(ContractTuning.SignedMorale);
            else if (_neg.Mood <= ContractTuning.SignedGrudgeMood) _merc.AddMorale(-ContractTuning.SignedMorale);
            Sessions.Remove(_merc.Id);
            string line = $"{_merc.Name} {(_renewal ? "재계약" : "계약")}: 주급 {offer.WeeklyWage}G · {ContractTuning.LengthLabel(offer.LengthDays)}" +
                          (offer.SigningFee > 0 ? $" · 계약금 -{offer.SigningFee}G" : "") + (offer.WinBonus > 0 ? $" · 성공 보너스 {offer.WinBonus}G" : "");
            GN3.Quests.DailyLog.Add(line);
            // 우편함에 기록(게임을 멈추지 않는 편지). 화면에는 토스트 대신 축하 카드.
            Mailbox.Post(MailKind.Notice, _renewal ? $"{_merc.Name} 재계약" : $"{_merc.Name} 영입",
                $"{_merc.Class.ClassName} · {_merc.Age}세 · 전투력 {_merc.CombatPower}\n" +
                $"주급 {offer.WeeklyWage}G · {ContractTuning.LengthLabel(offer.LengthDays)}({GameCalendar.Format(GameClock.CurrentDay + offer.LengthDays)}까지)" +
                (offer.SigningFee > 0 ? $"\n계약금 {offer.SigningFee}G" : "") + (offer.WinBonus > 0 ? $"\n성공 보너스 {offer.WinBonus}G" : ""));
            _onSigned?.Invoke(); // 용병 정보 창은 스스로 매 프레임 새로 그린다
            var merc = _merc;
            bool renewal = _renewal;
            Close();
            RecruitCelebration.Show(merc, offer, renewal);
        }

        // ---------- 대사 ----------

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

        /// <summary>결과 × 성향 → 대사. 같은 결과도 성향마다 말투가 달라 속마음을 짐작하는 단서가 된다.</summary>
        private string Line(NegotiationEvent e, TalkTopic topic)
        {
            var style = _demand.Style;
            int w = _neg.Counter?.WeeklyWage ?? 0;
            switch (e)
            {
                case NegotiationEvent.Accepted: return "좋습니다. 이 조건이면 서명하겠습니다.";
                case NegotiationEvent.ClosedGap: return "...좋습니다. 그쯤에서 하지요.";
                case NegotiationEvent.Countered:
                    return style switch
                    {
                        NegotiationStyle.Stubborn => $"그 정도로는 부족합니다. 주급 {w}G. 그 이하는 생각 없습니다.",
                        NegotiationStyle.Volatile => $"흥, 좀 낫군. 주급 {w}G면 생각해 보지!",
                        NegotiationStyle.Timid => $"저, 저기... 주급 {w}G 정도면 안 될까요?",
                        NegotiationStyle.Calculating => $"계산해 보면 주급 {w}G가 맞습니다.",
                        NegotiationStyle.Straight => $"솔직히 말하면 주급 {w}G는 받아야 합니다.",
                        _ => $"음~ 주급 {w}G 정도면 좋겠는데요.",
                    };
                case NegotiationEvent.Insulted:
                    return style switch
                    {
                        NegotiationStyle.Stubborn => "제 가치를 모르시는군요. 그 금액은 받아들일 수 없습니다.",
                        NegotiationStyle.Volatile => "그 돈으로 날 부리겠다고? 다시 생각해 오시오!",
                        NegotiationStyle.Timid => "그, 그건 좀... 너무 적은 것 같아요.",
                        NegotiationStyle.Calculating => "시세를 모르시는군요. 숫자를 다시 보시죠.",
                        NegotiationStyle.Straight => "그 조건으로는 어렵습니다. 한참 모자랍니다.",
                        _ => "에이, 그건 너무하잖아요.",
                    };
                case NegotiationEvent.FinalAccepted:
                    return style switch
                    {
                        NegotiationStyle.Stubborn => "...좋습니다. 이번만 양보하지요.",
                        NegotiationStyle.Volatile => "하! 배짱 한번 좋군. 좋소!",
                        NegotiationStyle.Timid => "아, 알겠어요... 그걸로 할게요.",
                        NegotiationStyle.Calculating => "그 숫자면 받아들일 수 있습니다.",
                        _ => "알겠습니다. 그 조건으로 하지요.",
                    };
                case NegotiationEvent.FinalCalled:
                    return style == NegotiationStyle.Stubborn ? "허세는 통하지 않습니다. 진짜 마지막이 아니잖습니까?"
                        : "마지막이라고요? 글쎄요, 아직 이야기할 여지는 있어 보입니다.";
                case NegotiationEvent.FinalWalkout:
                    return style == NegotiationStyle.Volatile ? "마지막? 좋소, 그럼 여기서 끝이오!" : "그게 마지막이라면 여기까지군요.";
                case NegotiationEvent.CalledBack:
                    return style switch
                    {
                        NegotiationStyle.Timid => $"자, 잠깐만요! 조금만 더 이야기해요... 주급 {w}G면 어때요?",
                        NegotiationStyle.Stubborn => $"...앉으시죠. 주급 {w}G. 이게 제 양보입니다.",
                        NegotiationStyle.Volatile => $"어이, 잠깐! 주급 {w}G. 이걸로 합시다!",
                        _ => $"잠깐, 그렇게 가실 것까진 없지요. 주급 {w}G로 하죠.",
                    };
                case NegotiationEvent.LetGo:
                    return style switch
                    {
                        NegotiationStyle.Stubborn => "가실 거면 가십시오. 저를 원하는 길드는 많습니다.",
                        NegotiationStyle.Volatile => "가든 말든 마음대로 하시오!",
                        _ => "그렇다면 어쩔 수 없군요.",
                    };
                case NegotiationEvent.RivalExposed:
                    return style == NegotiationStyle.Stubborn ? $"...좋습니다, 허세였습니다. 주급 {w}G."
                        : $"...사실 그런 제의는 없었습니다. 주급 {w}G로 하죠.";
                case NegotiationEvent.RivalDoubted: return "절 거짓말쟁이로 보시는 겁니까? 다음 제안이 마지막 기회입니다. 그쪽에 답을 줘야 하니까요.";
                case NegotiationEvent.RivalNoted: return "그 점은 염두에 두십시오.";
                case NegotiationEvent.RivalLeft: return $"기다릴 만큼 기다렸습니다. {SuitorName()} 쪽으로 가겠습니다.";
                case NegotiationEvent.MoodGone:
                    return style == NegotiationStyle.Volatile ? "더는 못 참겠군! 이 얘긴 끝이오." : "장난하십니까? 더 이야기할 것 없습니다.";
                case NegotiationEvent.TalkLiked:
                    return topic switch
                    {
                        TalkTopic.Praise => style switch
                        {
                            NegotiationStyle.Stubborn => "...뭐, 보는 눈은 있으시군요.",
                            NegotiationStyle.Volatile => "하하! 그렇지, 내가 좀 하지!",
                            NegotiationStyle.Timid => "제, 제가요? 감사합니다...",
                            NegotiationStyle.Easygoing => "헤헤, 칭찬은 언제 들어도 좋네요.",
                            _ => "인정해 주시니 고맙습니다.",
                        },
                        TalkTopic.Vision => ContractTerms.ReputationProgress() >= 0.5f ? "이 길드라면 이름을 날릴 수 있겠군요." : "꿈은 크시군요. 나쁘지 않습니다.",
                        _ => style == NegotiationStyle.Timid ? "오래 함께할 수 있다면... 그게 제일 좋아요." : "안정적인 건 좋지요.",
                    };
                case NegotiationEvent.PraiseBackfired:
                    return style switch
                    {
                        NegotiationStyle.Calculating => "입에 발린 말은 계약서에 안 적힙니다.",
                        NegotiationStyle.Volatile => "아부 떨지 마쇼. 속 보이니까.",
                        NegotiationStyle.Stubborn => "...그런 말로 넘어갈 줄 알았습니까?",
                        NegotiationStyle.Timid => "저, 저를 놀리시는 거죠...?",
                        _ => "그런 아첨은 별로 듣고 싶지 않군요.",
                    };
                case NegotiationEvent.TalkDisliked:
                    return topic == TalkTopic.Stability ? "지루하군. 난 모험을 하러 왔소." : "명성도 없는 길드가 비전을 말하긴 이르군요.";
                default: // TalkFlat
                    return topic == TalkTopic.Praise ? "말보다는 숫자로 보여 주시죠." : "그건 제게 중요하지 않습니다.";
            }
        }

        private string SuitorName() =>
            Suitors.TryGetValue(_merc.Id, out var id) && RivalGuilds.Get(id) is RivalGuild g ? g.Name : "다른 길드";

        /// <summary>"다른 길드 제의" 허세. 거짓말이 서툰 성향은 가짜일 때 티가 난다.</summary>
        private string RivalClaim()
        {
            // 진짜면 이 용병을 데려갈 만한 근처 길드, 가짜면 아무 길드 이름이나 댄다.
            if (!Suitors.TryGetValue(_merc.Id, out var id) || RivalGuilds.Get(id) == null)
            {
                var rng = new System.Random(_merc.Id.GetHashCode() ^ GameClock.CurrentDay);
                var all = RivalGuilds.All;
                id = (_neg.RivalReal ? RivalGuilds.PickSuitor(_merc, rng) : all[rng.Next(all.Count)]).Id;
                Suitors[_merc.Id] = id;
            }
            string rival = RivalGuilds.Get(id).Name;
            string claim = _demand.Style == NegotiationStyle.Stubborn
                ? $"참고로 {rival}에서는 주급 {_neg.RivalWage}G를 불렀습니다."
                : $"그런데... 사실 {rival}에서 주급 {_neg.RivalWage}G를 제안받았습니다.";
            return !_neg.RivalReal && _neg.Profile.BluffTell ? claim + " (눈을 피한다)" : claim;
        }

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
            // 합의한 뒤 조건을 바꾸면 합의가 풀린다
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
            string deadline = _neg.RivalDeadline > 0 ? $"<color=#e07050>  다른 길드 기한: 제안 {_neg.RivalDeadline}번</color>" : "";
            _summaryText.text = (_agreed != null ? "<color=#f0c050>[합의 완료]</color>  " : "") + $"지금 낼 돈: 계약금 {_offer.SigningFee}G (보유 {Wallet.Gold}G){deadline}\n" +
                                $"계약 기간 주급 합계 약 {_offer.WeeklyWage * weeks}G ({weeks}주) · {GameCalendar.Format(GameClock.CurrentDay + _offer.LengthDays)}까지";
            RefreshMood();

            bool open = !_neg.Broken;
            bool agreed = _agreed != null;
            bool rival = open && !agreed && _neg.RivalPending; // 허세에 먼저 대답해야 한다
            bool bargaining = open && !agreed && !rival;
            _proposeButton.gameObject.SetActive(bargaining);
            _finalButton.gameObject.SetActive(bargaining && !_neg.FinalUsed);
            _acceptCounterButton.gameObject.SetActive(bargaining && _neg.Counter != null);
            if (_neg.Counter != null) _acceptCounterButton.GetComponentInChildren<Text>().text = $"역제안 받아들이기 (주급 {_neg.Counter.Value.WeeklyWage}G)";
            _doubtButton.gameObject.SetActive(rival);
            _acknowledgeButton.gameObject.SetActive(rival);
            _walkAwayButton.gameObject.SetActive(open && !agreed);
            _walkAwayButton.interactable = _neg.CanWalkAway && !rival;
            foreach (var (topic, button) in _talkButtons) button.interactable = bargaining && !_neg.HasTalked(topic);
            _confirmButton.gameObject.SetActive(agreed);
            if (agreed)
                _confirmButton.GetComponentInChildren<Text>().text = _agreed.Value.SigningFee > 0 ? $"계약 확정하기 (-{_agreed.Value.SigningFee}G)" : "계약 확정하기";
        }

        /// <summary>기분 게이지: 막대 길이와 색, 한 단어 상태.</summary>
        private void RefreshMood()
        {
            int mood = _neg.Mood;
            var (label, color) = mood >= 70 ? ("흡족", new Color(0.45f, 0.8f, 0.4f))
                : mood >= 45 ? ("보통", new Color(0.9f, 0.8f, 0.4f))
                : mood >= 20 ? ("언짢음", new Color(0.95f, 0.55f, 0.25f))
                : ("폭발 직전", new Color(0.9f, 0.3f, 0.25f));
            if (_neg.Broken) label = "결렬";
            _moodText.text = $"기분: {label}";
            _moodText.color = color;
            _moodFill.anchorMax = new Vector2(Mathf.Clamp01(mood / 100f), 1f);
            _moodFillImage.color = color;
        }

        /// <summary>귀띔: 처음 부를 값의 ±15% 범위(정확한 값은 숨긴다)와 성향.</summary>
        private string AgentHint()
        {
            float s = ContractTuning.AgentHintSpread;
            int wLow = Mathf.Max(1, Mathf.FloorToInt(_demand.WeeklyWage * (1f - s))), wHigh = Mathf.CeilToInt(_demand.WeeklyWage * (1f + s));
            int fee = Mathf.RoundToInt(_demand.SigningFee / 10f) * 10;
            string length = _demand.LengthPreference > 0 ? "긴 계약을 좋아함" : _demand.LengthPreference < 0 ? "짧은 계약을 좋아함" : "기간은 상관없음";
            string bonus = _demand.BonusValue >= ContractTuning.HighBonusValue ? " · 성공 보너스를 좋아함" : "";
            return $"에이전트 귀띔: 주급 {wLow}~{wHigh}G, 계약금 약 {fee}G 정도 · {length}{bonus}\n" +
                   $"협상 성향: {_neg.Profile.Label} — {_neg.Profile.Hint}";
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
            Label(p, $"성격: {PersonalityTable.Get(_merc.Personality).Label} ({_neg.Profile.Label})", UITheme.BodySize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(lx, -300f), new Vector2(lw, 24f));
            var interest = Label(p, (_renewal ? "남을 생각: " : "관심도: ") + ContractTerms.InterestLabel(_demand.Interest), UITheme.BodySize,
                ContractTerms.InterestColor(_demand.Interest), TextAnchor.UpperLeft, new Vector2(lx, -328f), new Vector2(lw, 24f));
            interest.name = "Interest";
            if (_renewal)
                Label(p, $"지금 계약: {Contracts.Describe(_merc)}", UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(lx, -356f), new Vector2(lw, 44f));
            BuildMoodGauge(p, lx, -410f, lw);

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
                var b = MakeButton(p, ContractTuning.LengthLabel(days), () => SetLength(d), new Vector2(bx, -164f), new Vector2(84f, 34f));
                _lengthButtons.Add((days, b));
                bx += 92f;
            }

            Label(p, AgentHint(), UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperLeft, new Vector2(cx, -322f), new Vector2(Width - cx - 30f, 48f));

            // 설득 화법(각 한 번)
            Label(p, "설득", UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(cx, -376f), new Vector2(140f, 34f));
            float tx = cx + 150f;
            foreach (var (topic, label) in new[] { (TalkTopic.Praise, "실력 칭찬"), (TalkTopic.Vision, "길드 비전"), (TalkTopic.Stability, "안정성 강조") })
            {
                var t = topic;
                _talkButtons.Add((topic, MakeButton(p, label, () => Talk(t), new Vector2(tx, -376f), new Vector2(140f, 34f))));
                tx += 150f;
            }

            _summaryText = Label(p, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.UpperLeft, new Vector2(cx, -424f), new Vector2(Width - cx - 30f, 50f));
            _summaryText.supportRichText = true;

            // 아래: 용병의 대답
            var bubble = new GameObject("Reply", typeof(RectTransform), typeof(Image));
            bubble.transform.SetParent(p, false);
            var bubbleRect = (RectTransform)bubble.transform;
            bubbleRect.anchorMin = bubbleRect.anchorMax = bubbleRect.pivot = new Vector2(0f, 1f);
            bubbleRect.anchoredPosition = new Vector2(30f, -486f);
            bubbleRect.sizeDelta = new Vector2(Width - 60f, 74f);
            var bubbleImage = bubble.GetComponent<Image>();
            bubbleImage.sprite = UITheme.Inset;
            bubbleImage.type = Image.Type.Sliced;
            _replyText = Label(bubble.transform, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(16f, 0f), new Vector2(Width - 92f, 74f));
            _replyText.rectTransform.anchorMin = _replyText.rectTransform.anchorMax = _replyText.rectTransform.pivot = new Vector2(0f, 1f);

            // 버튼
            float by = -582f;
            MakeButton(p, "그만두기", Close, new Vector2(30f, by), new Vector2(120f, 44f));
            _walkAwayButton = MakeButton(p, "자리 뜨기", WalkAway, new Vector2(160f, by), new Vector2(120f, 44f));
            _acceptCounterButton = MakeButton(p, "역제안 받아들이기", AcceptCounter, new Vector2(Width - 650f, by), new Vector2(310f, 44f));
            _finalButton = MakeButton(p, "최종 제안", FinalOffer, new Vector2(Width - 330f, by), new Vector2(140f, 44f));
            _proposeButton = MakeButton(p, "제안하기", Propose, new Vector2(Width - 180f, by), new Vector2(150f, 44f));
            _doubtButton = MakeButton(p, "증거를 대 보시죠", DoubtRival, new Vector2(Width - 530f, by), new Vector2(240f, 44f));
            _acknowledgeButton = MakeButton(p, "그렇군요", AcknowledgeRival, new Vector2(Width - 280f, by), new Vector2(250f, 44f));
            _confirmButton = MakeButton(p, "계약 확정하기", Confirm, new Vector2(Width - 300f, by), new Vector2(270f, 44f));

            UIThemeApplier.ApplyNow(transform);
            _confirmButton.GetComponent<Image>().color = new Color(1f, 0.8f, 0.4f); // 확정 버튼은 금빛으로 눈에 띄게(테마를 입힌 뒤)
            // 기분 막대는 단색이어야 색이 보인다(테마가 입힌 어두운 inset 스프라이트를 벗긴다)
            var moodBack = _moodFill.parent.GetComponent<Image>();
            moodBack.sprite = null;
            moodBack.color = new Color(0f, 0f, 0f, 0.45f);
            _moodFillImage.sprite = null;
        }

        /// <summary>"기분: 보통" + 그 아래 가로 막대.</summary>
        private void BuildMoodGauge(Transform parent, float x, float y, float width)
        {
            _moodText = Label(parent, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.UpperLeft, new Vector2(x, y), new Vector2(width, 24f));
            _moodText.font = UITheme.BoldFont;

            var back = new GameObject("MoodBar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(parent, false);
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(x, y - 30f);
            backRect.sizeDelta = new Vector2(width - 20f, 14f);
            back.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            _moodFill = (RectTransform)fill.transform;
            _moodFill.anchorMin = Vector2.zero;
            _moodFill.anchorMax = Vector2.one;
            _moodFill.offsetMin = _moodFill.offsetMax = Vector2.zero;
            _moodFillImage = fill.GetComponent<Image>();
            _moodFillImage.raycastTarget = false;
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
