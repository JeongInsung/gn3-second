using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    public enum RivalTemper { Aggressive, Cautious, Wealthy, Cunning }

    /// <summary>근처 길드가 맡아 진행 중인 의뢰(게시판에서 가져간 것 또는 우리 게시판 밖의 의뢰). 끝나는 날 결과만 계산한다.</summary>
    public class RivalJob
    {
        public string Title;
        public int EndDay;
        public float Chance; // 성공 확률
        public int Reward;
        public int Reputation;
    }

    /// <summary>근처 길드 하나. 그림 없이 이름·성향·명성·소속 용병·관계·소식으로만 있다(RivalGuildsPanel).</summary>
    public class RivalGuild
    {
        public string Id;
        public string Name;
        public RivalTemper Temper;
        public int Reputation;
        public int Gold;
        public int Relation; // -100(적대) ~ 100(동맹)
        public int LastGiftDay = -1;
        public int BarterDay = -1;  // 교역(RivalDeals.Barter)한 날과 그날 횟수
        public int BarterCount;
        public readonly List<Mercenary> Roster = new List<Mercenary>();
        public readonly Dictionary<string, int> RosterEnd = new Dictionary<string, int>(); // 용병 id → 계약 끝나는 날
        public readonly List<RivalJob> Jobs = new List<RivalJob>();
        public readonly List<(int Day, string Text)> News = new List<(int, string)>();

        public int Rank => Guild.RankFor(Reputation);
        public Guild.RankInfo RankInfo => Guild.Ranks[Rank];
        public RivalTemperInfo Info => RivalGuilds.TemperOf(Temper);
        /// <summary>정원의 75%쯤을 채워 둔다(우리보다 조금 적게).</summary>
        public int TargetSize => Mathf.Max(3, Mathf.RoundToInt(RankInfo.PartySize * 0.75f));
        public int TotalPower => Roster.Sum(m => m.CombatPower);

        /// <summary>센 순서로 n명의 전투력 합(의뢰를 감당할 수 있는지).</summary>
        public int TopPower(int n) => Roster.Select(m => m.CombatPower).OrderByDescending(p => p).Take(n).Sum();

        public int WeeklyWages => Roster.Sum(RivalGuilds.WageOf);
    }

    public readonly struct RivalTemperInfo
    {
        public readonly string Label;
        public readonly string Description;
        public readonly float HireChance;    // 하루에 시장 용병을 고용하려 할 확률
        public readonly float QuestChance;   // 하루에 우리 게시판 의뢰를 노릴 확률
        public readonly float RelationScale; // 관계가 움직이는 폭
        public readonly int StartGold;

        public RivalTemperInfo(string label, string description, float hire, float quest, float relation, int gold)
        {
            Label = label;
            Description = description;
            HireChance = hire;
            QuestChance = quest;
            RelationScale = relation;
            StartGold = gold;
        }
    }

    /// <summary>
    /// 근처 길드 넷과 그들의 하루(자정마다 RivalSim). 우리와 같은 시장·게시판을 쓴다:
    ///  - 시장 용병을 고용해 간다(MarketListings에서 빠짐), 계약이 끝난 용병을 시장에 이적 매물로 내놓는다.
    ///  - 우리가 내보낸 용병(해고·계약 만료·이탈)은 며칠 안에 어느 길드가 데려간다(FreeAgents).
    ///  - 게시판 의뢰를 하나씩 가져가 맡는다(하루 전에 노리는 중 표시 — TargetQuest).
    ///  - 의뢰는 결과만 계산해 명성·금화가 오르내리고, 순위(우리 포함)가 바뀐다.
    /// 근처 길드가 한 일은 알림 없이 그 길드 소식(근처 길드 창)에만 남긴다(우리가 한 거래의 결과만 알린다 — RivalDeals·Loans).
    /// 길드는 새 게임(또는 길드가 없던 예전 저장)에서 처음 필요할 때 만든다(EnsureCreated).
    /// </summary>
    public static class RivalGuilds
    {
        public const int MinRelation = -100, MaxRelation = 100;
        public const int GiftCost = 100, GiftRelation = 5;
        public const int NewsKeep = 12;
        public const int FreeAgentDays = 14;

        private static readonly RivalTemperInfo[] Tempers =
        {
            new RivalTemperInfo("공격적", "의뢰를 닥치는 대로 맡고 전투력 높은 용병부터 데려간다. 관계가 크게 오르내린다.", 0.07f, 0.10f, 1.3f, 400),
            new RivalTemperInfo("신중", "감당할 만한 의뢰만 맡고 빨리 자라는 젊은 용병을 키운다. 관계가 잘 변하지 않는다.", 0.05f, 0.05f, 0.7f, 600),
            new RivalTemperInfo("부유", "돈이 많아 몸값 비싼 용병도 산다. 거래를 좋아한다.", 0.09f, 0.06f, 1.0f, 1500),
            new RivalTemperInfo("교활", "몸값에 비해 쓸 만한 용병을 고르고, 남이 노리는 의뢰를 가로채는 데 능하다.", 0.06f, 0.09f, 1.2f, 500),
        };

        public static RivalTemperInfo TemperOf(RivalTemper temper) => Tempers[(int)temper];

        private static readonly List<RivalGuild> _all = new List<RivalGuild>();
        public static IReadOnlyList<RivalGuild> All
        {
            get
            {
                EnsureCreated();
                return _all;
            }
        }

        /// <summary>우리가 내보낸 용병: 며칠 안에 어느 길드가 데려가거나, 기한이 지나면 떠난다.</summary>
        public static readonly List<(Mercenary Merc, int UntilDay)> FreeAgents = new List<(Mercenary, int)>();

        /// <summary>재계약 협상에서 "다른 길드로 가겠다"며 깨진 용병 → 계약이 끝나 떠날 때 그 길드로 간다.</summary>
        private static readonly Dictionary<string, string> _promised = new Dictionary<string, string>();

        /// <summary>내일 자정에 이 길드가 가져갈 게시판 의뢰(게시판 툴팁 "다른 길드도 노리는 중").</summary>
        public static Quest TargetQuest { get; private set; }
        public static RivalGuild TargetGuild { get; private set; }

        public static event Action OnChanged;

        internal static readonly System.Random Rng = new System.Random();

        public static RivalGuild Get(string id) => All.FirstOrDefault(g => g.Id == id);

        /// <summary>시장 생성기가 이름이 겹치지 않게 쓴다(만드는 중에도 안전 — EnsureCreated를 부르지 않는다).</summary>
        public static IEnumerable<string> RosterNames() => _all.SelectMany(g => g.Roster).Select(m => m.Name);

        /// <summary>주급(시세의 20% — 우리 계약과 같은 비율).</summary>
        public static int WageOf(Mercenary merc) => Mathf.Max(1, Mathf.RoundToInt(Pricing.HireCost(merc) * 0.2f));

        // ---------- 순위·관계 ----------

        /// <summary>명성 순위(우리 길드 포함). 우리 길드는 Guild가 null.</summary>
        public static List<(string Name, int Reputation, RivalGuild Guild)> Ranking()
        {
            var list = All.Select(g => (g.Name, g.Reputation, (RivalGuild)g)).ToList();
            list.Add(("우리 길드", Guild.Reputation, null));
            return list.OrderByDescending(e => e.Item2).ThenBy(e => e.Item3 == null ? 0 : 1).ToList();
        }

        /// <summary>우리 길드의 순위(1부터).</summary>
        public static int OurPlace => Ranking().FindIndex(e => e.Guild == null) + 1;

        public static string RelationLabel(int relation) =>
            relation <= -50 ? "적대" : relation <= -15 ? "경쟁" : relation < 15 ? "중립" : relation < 50 ? "우호" : "동맹";

        public static string RelationColor(int relation) =>
            relation <= -50 ? "#ff6a5a" : relation <= -15 ? "#f0a54a" : relation < 15 ? "#c8bca8" : relation < 50 ? "#8fd18f" : "#6ab8ff";

        public static bool IsFriendly(RivalGuild g) => g.Relation >= 15;
        public static bool IsAllied(RivalGuild g) => g.Relation >= 50;
        public static bool IsHostile(RivalGuild g) => g.Relation <= -50;

        /// <summary>관계 단계 효과(창 툴팁).</summary>
        public static string RelationEffect(int relation) =>
            relation <= -50 ? "거래·대여를 거절하고 우리 게시판 의뢰를 더 자주 가로챈다."
            : relation <= -15 ? "이적료가 비싸고 의뢰를 자주 가로챈다."
            : relation < 15 ? "보통 조건으로 거래한다."
            : relation < 50 ? "이적료가 싸지고 용병 대여·연합 의뢰가 열린다. 의뢰를 덜 가로챈다."
            : "가장 싼 이적료. 우리 게시판 의뢰를 가로채지 않는다.";

        /// <summary>관계를 바꾼다(성향 폭 반영). reason이 있으면 그 길드 소식에 남긴다.</summary>
        public static void AddRelation(RivalGuild g, int amount, string reason = null)
        {
            if (g == null || amount == 0) return;
            int scaled = Mathf.RoundToInt(amount * g.Info.RelationScale);
            if (scaled == 0) scaled = Math.Sign(amount);
            g.Relation = Mathf.Clamp(g.Relation + scaled, MinRelation, MaxRelation);
            if (reason != null) AddNews(g, $"{reason} (관계 {(scaled > 0 ? "+" : "")}{scaled})");
            OnChanged?.Invoke();
        }

        public static bool CanGift(RivalGuild g) => g.LastGiftDay != GameClock.CurrentDay;

        /// <summary>선물(하루 한 번 100G): 관계 +5.</summary>
        public static bool Gift(RivalGuild g)
        {
            if (!CanGift(g) || !Wallet.TrySpend(GiftCost)) return false;
            g.LastGiftDay = GameClock.CurrentDay;
            g.Gold += GiftCost;
            AddRelation(g, GiftRelation, $"우리 길드가 {GiftCost}G를 선물했다");
            return true;
        }

        public static void AddNews(RivalGuild g, string text)
        {
            g.News.Insert(0, (GameClock.CurrentDay, text));
            if (g.News.Count > NewsKeep) g.News.RemoveRange(NewsKeep, g.News.Count - NewsKeep);
        }

        /// <summary>"3월 1일".</summary>
        public static string ShortDate(int day)
        {
            var d = GameCalendar.FromDay(day);
            return $"{d.Month}월 {d.Day}일";
        }

        /// <summary>근처 길드가 한 일: 알림(토스트·편지) 없이 그 길드 소식(근처 길드 창)에만 남긴다.</summary>
        private static void Announce(RivalGuild g, string text) => AddNews(g, text);

        // ---------- 만들기 ----------

        private static readonly (string Id, string Name, RivalTemper Temper, int Reputation, int Relation)[] Starts =
        {
            ("fang", "붉은 송곳니단", RivalTemper.Aggressive, 120, -10),
            ("shield", "은방패 기사회", RivalTemper.Cautious, 60, 10),
            ("scale", "황금저울 상회", RivalTemper.Wealthy, 30, 5),
            ("raven", "잿빛 까마귀단", RivalTemper.Cunning, 0, -20),
        };

        private static MercenaryClassSO[] _classes;
        internal static MercenaryClassSO[] Classes => _classes ??= Resources.LoadAll<MercenaryClassSO>("MercenaryClasses");

        public static void EnsureCreated()
        {
            if (_all.Count > 0 || Classes.Length == 0) return;
            foreach (var s in Starts)
            {
                var g = new RivalGuild
                {
                    Id = s.Id, Name = s.Name, Temper = s.Temper, Reputation = s.Reputation, Relation = s.Relation,
                    Gold = TemperOf(s.Temper).StartGold,
                };
                _all.Add(g); // 먼저 넣어야 다음 길드 용병 이름이 겹치지 않는다(RosterNames)
                int count = Mathf.RoundToInt(g.TargetSize * (0.6f + (float)Rng.NextDouble() * 0.2f));
                foreach (var merc in Recruit(g, Mathf.Max(2, count)))
                    Join(g, merc, quiet: true);
            }
        }

        /// <summary>길드 단계에 맞는 경력의 새 용병(시장 밖에서 직접 모집).</summary>
        private static List<Mercenary> Recruit(RivalGuild g, int count)
        {
            var info = g.RankInfo;
            return MercenaryMarketGenerator.Generate(Classes, count, info.MarketMinGrowth, info.MarketMaxGrowth, Rng, info.MaxMercGrade);
        }

        /// <summary>길드에 들어온다(계약 180~720일).</summary>
        internal static void Join(RivalGuild g, Mercenary merc, bool quiet = false)
        {
            g.Roster.Add(merc);
            g.RosterEnd[merc.Id] = GameClock.CurrentDay + Rng.Next(180, 721);
            if (!quiet) OnChanged?.Invoke();
        }

        internal static void Leave(RivalGuild g, Mercenary merc)
        {
            g.Roster.Remove(merc);
            g.RosterEnd.Remove(merc.Id);
        }

        // ---------- 우리가 내보낸 용병·맞제의 ----------

        /// <summary>해고·계약 만료·이탈로 우리 길드를 떠난 용병. 맞제의로 약속한 길드가 있으면 그리로, 아니면 자유 계약.</summary>
        public static void OnPlayerReleased(Mercenary merc)
        {
            if (merc == null || !merc.IsAlive) return;
            if (_promised.TryGetValue(merc.Id, out var id) && Get(id) is RivalGuild promised)
            {
                _promised.Remove(merc.Id);
                Join(promised, merc);
                Announce(promised, $"전 소속 {Josa.IGa(merc.Name)} {promised.Name}에 입단했습니다.");
                return;
            }
            FreeAgents.Add((merc, GameClock.CurrentDay + FreeAgentDays));
        }

        /// <summary>협상에서 맞제의한 길드로 가겠다며 깨졌다. 시장 용병이면 바로 그 길드로, 우리 용병이면 계약이 끝날 때 간다.</summary>
        public static void SignAway(Mercenary merc, string guildId, bool renewal)
        {
            var g = Get(guildId);
            if (g == null || merc == null) return;
            if (renewal)
            {
                _promised[merc.Id] = guildId;
                AddNews(g, $"우리 길드의 {merc.Name}에게 이적을 제안했다");
                OnChanged?.Invoke();
                return;
            }
            MarketListings.Remove(merc);
            foreach (var other in _all.Where(o => o.Roster.Contains(merc)).ToList()) Leave(other, merc); // 영입 협상 중이던 다른 길드 소속
            Join(g, merc);
            Announce(g, $"{Josa.IGa(merc.Name)} {g.Name}의 제의를 받아들였습니다.");
        }

        /// <summary>협상 중 "다른 길드 제의": 진짜라면 이 용병을 데려갈 만한 길드(정원·금화 여유, 성향). 없으면 null.</summary>
        public static RivalGuild PickSuitor(Mercenary merc, System.Random rng)
        {
            var fits = All.Where(g => g.Roster.Count < g.TargetSize + 1 && g.Gold >= Pricing.HireCost(merc)).ToList();
            if (fits.Count == 0) fits = All.ToList();
            return fits[rng.Next(fits.Count)];
        }

        // ---------- 하루 ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += DailyTick;

        /// <summary>자정마다(디버그 창에서 여러 날 돌릴 수도 있다).</summary>
        public static void DailyTick() => Tick(GameClock.CurrentDay);

        /// <summary>디버그: n일치를 한꺼번에 돌린다(날짜는 그대로 — 의뢰는 그만큼 일찍 끝난다).</summary>
        public static void DebugSimulate(int days)
        {
            for (int i = 1; i <= days; i++) Tick(GameClock.CurrentDay + i);
        }

        /// <summary>디버그: 관계를 그대로(성향 폭 없이) 바꾼다.</summary>
        public static void DebugAddRelation(RivalGuild g, int amount)
        {
            g.Relation = Mathf.Clamp(g.Relation + amount, MinRelation, MaxRelation);
            OnChanged?.Invoke();
        }

        /// <summary>디버그: 이 길드가 지금 시장 용병을 고용하려 한다(확률 무시).</summary>
        public static void DebugHire(RivalGuild g) => TryHireFromMarket(g, force: true);

        private static void Tick(int today)
        {
            EnsureCreated();
            foreach (var g in _all)
            {
                SettleJobs(g, today);
                ManageRoster(g, today);
                if (g.Jobs.Count == 0 && Rng.NextDouble() < 0.28) StartOutsideJob(g, today);
                TryHireFromMarket(g);
            }
            HandleFreeAgents(today);
            BoardCompetition(today);
            if (today % 7 == 0)
                foreach (var g in _all) g.Relation += Math.Sign(-g.Relation); // 일주일마다 관계가 1씩 중립 쪽으로

            OnChanged?.Invoke();
        }

        private static void SettleJobs(RivalGuild g, int today)
        {
            foreach (var job in g.Jobs.Where(j => j.EndDay <= today).ToList())
            {
                g.Jobs.Remove(job);
                if (Rng.NextDouble() < job.Chance)
                {
                    g.Reputation += job.Reputation;
                    g.Gold += job.Reward;
                    AddNews(g, $"'{job.Title}' 성공 (명성 +{job.Reputation}, {job.Reward}G)");
                    // 이긴 용병들은 조금 자란다
                    foreach (var merc in g.Roster.Where(_ => Rng.NextDouble() < 0.5)) merc.GrantStartingGrowth(1, Rng);
                }
                else
                {
                    g.Reputation = Math.Max(0, g.Reputation - Guild.FailPenalty);
                    string line = $"'{job.Title}' 실패 (명성 -{Guild.FailPenalty})";
                    if (g.Roster.Count > 0 && Rng.NextDouble() < 0.2)
                    {
                        var dead = g.Roster[Rng.Next(g.Roster.Count)];
                        Leave(g, dead);
                        line += $" · {Josa.IGa(dead.Name)} 전사했다";
                    }
                    AddNews(g, line);
                }
            }
        }

        private static void ManageRoster(RivalGuild g, int today)
        {
            int yearDay = GameCalendar.DayOfYear(today);
            foreach (var merc in g.Roster.ToList())
            {
                if (merc.BirthdayIndex == yearDay) merc.GrowOlder();
                if (merc.Age >= GrowthTuning.RetireAge)
                {
                    Leave(g, merc);
                    AddNews(g, $"{Josa.IGa(merc.Name)} 은퇴했다");
                    continue;
                }
                if (g.RosterEnd.TryGetValue(merc.Id, out int end) && end <= today)
                {
                    // 전투력이 길드 평균 이상이고 금화가 있으면 재계약, 아니면 이적 매물로 내놓는다.
                    bool keep = merc.CombatPower >= g.TotalPower / Math.Max(1, g.Roster.Count) && g.Gold > WageOf(merc) * 8 && Rng.NextDouble() < 0.7;
                    if (keep) g.RosterEnd[merc.Id] = today + Rng.Next(180, 721);
                    else Release(g, merc, "계약이 끝나");
                }
            }

            if (today % 7 == 0) g.Gold -= g.WeeklyWages;
            // 금화가 바닥나면 가장 비싼 용병부터 내보낸다
            while (g.Gold < 0 && g.Roster.Count > 2)
            {
                var costly = g.Roster.OrderByDescending(WageOf).First();
                g.Gold += WageOf(costly) * 4; // 남은 주급을 아낀 셈
                Release(g, costly, "주급을 감당하지 못해");
            }
            g.Gold = Math.Max(0, g.Gold);

            // 사람이 모자라면 가끔 시장 밖에서 직접 모집
            if (g.Roster.Count < g.TargetSize - 1 && Rng.NextDouble() < 0.04)
                foreach (var merc in Recruit(g, 1))
                {
                    Join(g, merc, quiet: true);
                    AddNews(g, $"새 용병 {merc.Name}({merc.Class.ClassName}) 모집");
                }
        }

        /// <summary>길드를 나간 용병은 우리 시장에 이적 매물(출신 표시)로 나온다. 시장 목록이 아직 없으면 그냥 떠난다.</summary>
        private static void Release(RivalGuild g, Mercenary merc, string reason)
        {
            Leave(g, merc);
            if (MarketListings.Current == null || !merc.IsAlive)
            {
                AddNews(g, $"{reason} {Josa.IGa(merc.Name)} 떠났다");
                return;
            }
            MarketListings.AddTransfer(merc, g.Id);
            AddNews(g, $"{reason} {Josa.IGa(merc.Name)} 떠났다 → 용병시장 이적 매물");
        }

        /// <summary>우리 게시판 밖의 의뢰(가상): 단계에 맞는 레벨, 2~5일. 우리가 쉬어도 순위가 움직인다.</summary>
        private static void StartOutsideJob(RivalGuild g, int today)
        {
            if (g.Roster.Count == 0) return;
            int level = Mathf.Clamp(1 + g.Rank / 2 + Rng.Next(-1, 2), 1, 5);
            int need = Mathf.RoundToInt(QuestDifficulty.PowerPerEnemy(level) * 4f);
            g.Jobs.Add(new RivalJob
            {
                Title = $"Lv{level} 토벌 의뢰",
                EndDay = today + Rng.Next(2, 6),
                Chance = SuccessChance(g.TopPower(4), need, g.Temper),
                Reward = 40 * level + 60,
                Reputation = 8 * level,
            });
        }

        private static float SuccessChance(int power, int need, RivalTemper temper)
        {
            float ratio = power / (float)Math.Max(1, need);
            float chance = 0.5f + (ratio - 1f) * 0.8f + (temper == RivalTemper.Cautious ? 0.05f : 0f);
            return Mathf.Clamp(chance, 0.15f, 0.95f);
        }

        // ---------- 시장 ----------

        private static void TryHireFromMarket(RivalGuild g, bool force = false)
        {
            var market = MarketListings.Current;
            if (market == null || market.Count == 0 || (!force && g.Roster.Count >= g.TargetSize + 1)) return;
            float chance = g.Info.HireChance * (g.Roster.Count < g.TargetSize ? 1.5f : 0.5f) * (IsFriendly(g) ? 0.7f : 1f);
            if (!force && Rng.NextDouble() >= chance) return;

            var affordable = market.Where(m => Pricing.HireCost(m) + 100 <= g.Gold && !ContractNegotiationPanel.IsNegotiating(m)).ToList();
            if (affordable.Count == 0) return;
            Mercenary pick = g.Temper switch
            {
                RivalTemper.Aggressive => affordable.OrderByDescending(m => m.CombatPower).First(),
                RivalTemper.Cautious => affordable.OrderBy(m => m.Age).First(),
                RivalTemper.Wealthy => affordable.OrderByDescending(Pricing.HireCost).First(),
                _ => affordable.OrderByDescending(m => m.CombatPower / (float)Math.Max(1, Pricing.HireCost(m))).First(),
            };
            g.Gold -= Pricing.HireCost(pick);
            MarketListings.Remove(pick);
            Join(g, pick);
            Announce(g, $"{Josa.IGa(g.Name)} 용병시장의 {pick.Class.ClassName} {Josa.EulReul(pick.Name)} 고용했습니다.");
        }

        private static void HandleFreeAgents(int today)
        {
            for (int i = FreeAgents.Count - 1; i >= 0; i--)
            {
                var (merc, until) = FreeAgents[i];
                if (until <= today)
                {
                    FreeAgents.RemoveAt(i);
                    continue;
                }
                if (Rng.NextDouble() >= 0.25) continue;
                var takers = _all.Where(g => g.Roster.Count < g.TargetSize + 1 && g.Gold >= Pricing.HireCost(merc) / 2).ToList();
                if (takers.Count == 0) continue;
                var g2 = takers[Rng.Next(takers.Count)];
                FreeAgents.RemoveAt(i);
                g2.Gold -= Pricing.HireCost(merc) / 2;
                Join(g2, merc);
                Announce(g2, $"전 소속 {Josa.IGa(merc.Name)} {g2.Name}에 입단했습니다.");
            }
        }

        // ---------- 게시판 ----------

        /// <summary>이 퀘스트를 노리는 길드(없으면 null) — 게시판 툴팁.</summary>
        public static RivalGuild TargetOf(Quest quest) => quest != null && quest == TargetQuest ? TargetGuild : null;

        /// <summary>어제 노리던 의뢰가 아직 게시판에 있으면 가져가고, 내일 노릴 의뢰를 새로 고른다.</summary>
        private static void BoardCompetition(int today)
        {
            var board = QuestBoardListings.Current;
            if (TargetQuest != null && TargetGuild != null && board != null && board.Contains(TargetQuest))
                TakeQuest(TargetGuild, TargetQuest, today);
            TargetQuest = null;
            TargetGuild = null;
            PickTarget();
        }

        /// <summary>내일 가져갈 의뢰를 고른다(모든 길드 합쳐 하루 많아야 하나). 동맹은 노리지 않는다.</summary>
        public static void PickTarget()
        {
            var board = QuestBoardListings.Current;
            if (board == null || board.Count <= 1) return; // 게시판을 비우지 않는다
            foreach (var g in _all.OrderBy(_ => Rng.Next()))
            {
                if (IsAllied(g) || g.Jobs.Count > 1) continue;
                float chance = g.Info.QuestChance * Mathf.Clamp(1f - g.Relation / 100f, 0.3f, 1.6f);
                if (Rng.NextDouble() >= chance) continue;
                var candidates = board.Where(q => !q.IsChallenge && g.TopPower(Math.Max(3, q.RequiredDispatchSize)) >= QuestDifficulty.RecommendedPower(q) * 0.8f).ToList();
                if (candidates.Count == 0) continue;
                TargetQuest = candidates[Rng.Next(candidates.Count)];
                TargetGuild = g;
                return;
            }
        }

        /// <summary>디버그·즉시: 이 길드가 게시판 의뢰를 가져간다.</summary>
        public static void TakeQuest(RivalGuild g, Quest quest, int today)
        {
            QuestBoardListings.Remove(quest);
            int need = QuestDifficulty.RecommendedPower(quest);
            g.Jobs.Add(new RivalJob
            {
                Title = quest.Title.Contains("—") ? quest.Title.Substring(quest.Title.IndexOf('—') + 1).Trim() : quest.Title,
                EndDay = today + Math.Max(1, quest.DurationDays),
                Chance = SuccessChance(g.TopPower(Math.Max(3, quest.RequiredDispatchSize)), need, g.Temper),
                Reward = Pricing.QuestReward(quest),
                Reputation = Guild.VictoryReputation(QuestDifficulty.PowerGrade(quest)),
            });
            if (TargetQuest == quest) { TargetQuest = null; TargetGuild = null; }
            Announce(g, $"{Josa.IGa(g.Name)} 게시판의 '{g.Jobs[g.Jobs.Count - 1].Title}' 의뢰를 맡았습니다.");
        }

        /// <summary>불러오기: 저장된 노리는 의뢰(게시판 몇 번째).</summary>
        public static void RestoreTarget(int boardIndex, string guildId)
        {
            var board = QuestBoardListings.Current;
            TargetQuest = board != null && boardIndex >= 0 && boardIndex < board.Count ? board[boardIndex] : null;
            TargetGuild = TargetQuest != null ? Get(guildId) : null;
            if (TargetGuild == null) TargetQuest = null;
        }

        // ---------- 저장 ----------

        public static IReadOnlyDictionary<string, string> Promised => _promised;

        /// <summary>불러오기: 길드 목록을 통째로 바꾼다(빈 목록이면 새로 만든다 — 예전 저장).</summary>
        public static void Restore(IEnumerable<RivalGuild> guilds, IEnumerable<(Mercenary, int)> freeAgents, IEnumerable<KeyValuePair<string, string>> promised)
        {
            _all.Clear();
            FreeAgents.Clear();
            _promised.Clear();
            TargetQuest = null;
            TargetGuild = null;
            if (guilds != null) _all.AddRange(guilds);
            if (freeAgents != null) FreeAgents.AddRange(freeAgents);
            if (promised != null) foreach (var p in promised) _promised[p.Key] = p.Value;
            EnsureCreated();
            OnChanged?.Invoke();
        }

        /// <summary>디버그: 길드 단계 상한을 넘지 않게 명성을 바꾼다.</summary>
        public static void DebugAddReputation(RivalGuild g, int amount)
        {
            g.Reputation = Math.Max(0, g.Reputation + amount);
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _all.Clear();
            FreeAgents.Clear();
            _promised.Clear();
            TargetQuest = null;
            TargetGuild = null;
            OnChanged = null;
        }
    }
}
