using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Traits;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 선택 사건. 중요 편지(Mailbox.PostChoice)로 와서 날짜 넘기기를 멈추고 알림창에 선택지를 띄운다(나중에 우편함에서 골라도 된다).
    ///  - 원정 이동 중: 하루치 이동마다(습격 판정 뒤) TravelChance. 고를 때까지 그 파견대는 멈춘다(Expedition.AwaitingEvent).
    ///  - 마을·길드: 자정마다, 지난 마을 사건 뒤 VillageGapDays일이 지났으면 VillageChance.
    /// 결과는 기존 시스템(사기·피로·체력·부상·친밀도·금화·명성·근처 길드 관계·전리품·창고·이동 시간)으로 처리한다.
    /// 저장하지 않는다(불러오면 기다리던 파견대는 다시 움직이고 사건은 사라진다).
    /// </summary>
    public static class ChoiceEvents
    {
        public const float TravelChance = 0.12f;
        public const float VillageChance = 0.2f;
        public const int VillageGapDays = 3;

        private static readonly System.Random Rng = new System.Random();
        private static int _lastVillageDay = -999;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _lastVillageDay = -999;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            GameClock.OnDayAdvanced += VillageTick;
            ExpeditionLog.TravelEventHook = TryTravelEvent;
        }

        // ---------- 원정 이동 중 ----------

        private static bool TryTravelEvent(Expedition e)
        {
            if (e.AwaitingEvent || e.Members.All(m => !m.IsAlive) || Rng.NextDouble() >= TravelChance) return false;
            TriggerTravel(e);
            return true;
        }

        /// <summary>이 파견대에 이동 사건 하나를 일으킨다(디버그 창도 쓴다).</summary>
        public static void TriggerTravel(Expedition e, int index = -1)
        {
            var builders = new List<Func<Expedition, (string title, string body, List<MailChoice> choices)>>
            {
                Crossroads, WoundedTraveler, SuspiciousChest, AbandonedCamp, WolfHowl, HerbField,
            };
            if (e.IsReturning) builders.Remove(Crossroads); // 지름길은 가는 길에만
            var build = index >= 0 && index < builders.Count ? builders[index] : builders[Rng.Next(builders.Count)];
            var (title, body, choices) = build(e);
            e.AwaitingEvent = true;
            Mailbox.PostChoice(title, $"<color=#a3937c>{e.Quest.Title} · {(e.IsReturning ? "귀환" : "이동")} 중</color>\n\n{body}", choices, e);
            ExpeditionLog.Instance.NotifyChanged();
        }

        /// <summary>원정 사건 선택지: 고르면 파견대가 다시 움직인다.</summary>
        private static MailChoice Go(Expedition e, string label, string tooltip, Func<string> act, Func<bool> available = null) => new MailChoice
        {
            Label = label,
            Tooltip = tooltip,
            Available = available,
            Resolve = () =>
            {
                e.AwaitingEvent = false;
                string result = ExpeditionLog.Instance.Active.Contains(e) ? act() : "파견대가 이미 없다.";
                ExpeditionLog.Instance.NotifyChanged();
                return result;
            },
        };

        private static List<Mercenary> Alive(Expedition e) => e.Members.Where(m => m.IsAlive).ToList();
        private static Mercenary AnyOf(IReadOnlyList<Mercenary> list) => list[Rng.Next(list.Count)];

        private static (string, string, List<MailChoice>) Crossroads(Expedition e) => ("갈림길",
            "길이 둘로 갈린다. 숲을 가로지르는 좁은 지름길은 하루쯤 빠르지만 짐승과 도적의 흔적이 보인다.",
            new List<MailChoice>
            {
                Go(e, "지름길", "도착이 하루 빨라지지만, 도착할 때까지 습격 확률이 두 배.", () =>
                {
                    e.Delay(-Quest.HoursPerDay);
                    e.RiskyRoute = true;
                    return "지름길로 접어들었다. 하루 일찍 닿겠지만 사방을 경계해야 한다.";
                }),
                Go(e, "큰길", "그대로 간다.", () => "안전한 큰길을 따라 계속 간다."),
            });

        private static (string, string, List<MailChoice>) WoundedTraveler(Expedition e)
        {
            var members = Alive(e);
            return ("다친 나그네",
                "길가에 다리를 다친 나그네가 쓰러져 있다. 가까운 마을까지 데려다 주면 반나절이 더 걸린다.",
                new List<MailChoice>
                {
                    Go(e, "돕는다", "반나절 늦어지지만 명성 +5, 파견대 사기 +5.", () =>
                    {
                        e.Delay(Quest.HoursPerDay / 2f);
                        Guild.Add(5);
                        foreach (var m in members) m.AddMorale(5);
                        return "나그네를 마을까지 데려다 주었다. 소문이 퍼져 명성이 조금 올랐다(+5). 대원들도 뿌듯해한다.";
                    }),
                    Go(e, "지나친다", "시간은 아끼지만 마음 약한 대원은 사기 −4(용감·침착한 대원은 그대로).", () =>
                    {
                        var upset = members.Where(m => m.Personality != Personality.Brave && m.Personality != Personality.Composed).ToList();
                        foreach (var m in upset) m.AddMorale(-4);
                        return upset.Count > 0 ? $"그냥 지나쳤다. {string.Join(", ", upset.Select(m => m.Name))}의 표정이 어둡다(사기 −4)." : "그냥 지나쳤다. 아무도 말이 없다.";
                    }),
                });
        }

        private static (string, string, List<MailChoice>) SuspiciousChest(Expedition e)
        {
            var members = Alive(e);
            return ("수상한 상자",
                "덤불 속에 자물쇠가 녹슨 상자가 놓여 있다. 누가 일부러 둔 것 같기도 하다.",
                new List<MailChoice>
                {
                    Go(e, "열어 본다", "60%로 금화, 40%로 함정(한 명이 다친다).", () =>
                    {
                        if (Rng.NextDouble() < 0.6)
                        {
                            int gold = Rng.Next(50, 151) * Math.Max(1, e.Quest.Difficulty);
                            Wallet.Add(gold);
                            return $"상자 안에 금화가 들어 있었다! (+{gold}G)";
                        }
                        var victim = AnyOf(members);
                        int max = victim.CurrentStats.MaxHealth;
                        victim.SetHealth(Math.Max(1, victim.CurrentHealth - Mathf.RoundToInt(max * 0.25f)));
                        var injury = AilmentCatalog.All.Where(a => a.IsInjury && !a.isSevere).OrderBy(_ => Rng.Next()).FirstOrDefault();
                        bool hurt = injury != null && Ailments.Inflict(victim, injury, "함정에 걸려");
                        return $"함정이었다! {Josa.IGa(victim.Name)} 다쳤다(체력 −25%{(hurt ? $", {injury.displayName}" : "")}).";
                    }),
                    Go(e, "그냥 둔다", "아무 일도 없다.", () => "괜한 위험은 피하기로 했다."),
                });
        }

        private static (string, string, List<MailChoice>) AbandonedCamp(Expedition e)
        {
            var members = Alive(e);
            return ("버려진 야영지",
                "아직 온기가 남은 모닥불 자리와 마른 장작이 있다. 쉬어 가기 좋아 보인다.",
                new List<MailChoice>
                {
                    Go(e, "쉬어 간다", "반나절 늦어지지만 피로 −20, 체력 +20%.", () =>
                    {
                        e.Delay(Quest.HoursPerDay / 2f);
                        foreach (var m in members)
                        {
                            m.AddFatigue(-20);
                            m.SetHealth(m.CurrentHealth + Mathf.RoundToInt(m.CurrentStats.MaxHealth * 0.2f));
                        }
                        return "모닥불 곁에서 푹 쉬었다. 피로가 풀리고 상처도 조금 아물었다.";
                    }),
                    Go(e, "지나친다", "그대로 간다.", () => "쉬지 않고 길을 재촉했다."),
                });
        }

        private static (string, string, List<MailChoice>) WolfHowl(Expedition e) => ("늑대 울음소리",
            "앞쪽 숲에서 늑대 무리의 울음소리가 들린다. 길을 돌아가면 하루가 더 걸린다.",
            new List<MailChoice>
            {
                Go(e, "돌아간다", "하루 늦어진다.", () =>
                {
                    e.Delay(Quest.HoursPerDay);
                    return "숲을 크게 돌아갔다. 하루가 더 걸리겠지만 무사하다.";
                }),
                Go(e, "정면 돌파", "바로 습격 전투를 치른다(길잡이도 피할 수 없다). 이기면 조금 성장한다.", () =>
                {
                    ExpeditionLog.Instance.ForceAmbush(e);
                    return ExpeditionLog.Instance.Active.Contains(e)
                        ? "정면으로 뚫고 지나갔다. (전투 결과는 알림·하루 보고서 참고)"
                        : "무리하게 돌파하다 파견대가 무너졌다...";
                }),
            });

        private static (string, string, List<MailChoice>) HerbField(Expedition e)
        {
            var herb = HealingItemCatalog.All.FirstOrDefault();
            return ("약초밭",
                "길가 비탈에 약초가 무성하게 자라 있다. 캐려면 반나절쯤 걸린다.",
                new List<MailChoice>
                {
                    Go(e, "캔다", $"반나절 늦어지고 {herb?.Name ?? "약초"} 하나를 창고에(가득 차면 팔아 금화로).", () =>
                    {
                        e.Delay(Quest.HoursPerDay / 2f);
                        if (herb != null && GuildStorage.TryAdd(herb)) return $"{Josa.EulReul(herb.Name)} 캐서 길드 창고로 보냈다.";
                        int gold = Math.Max(10, (herb?.Price ?? 20) / 2);
                        Wallet.Add(gold);
                        return $"창고가 가득 차 약초를 팔았다(+{gold}G).";
                    }),
                    Go(e, "지나친다", "그대로 간다.", () => "약초는 다음 기회에."),
                });
        }

        // ---------- 마을·길드 ----------

        private static void VillageTick()
        {
            int today = GameClock.CurrentDay;
            if (today - _lastVillageDay < VillageGapDays || Rng.NextDouble() >= VillageChance) return;
            if (TriggerVillage()) _lastVillageDay = today;
        }

        private static List<Mercenary> InVillage() => PlayerParty.Instance.Members.Where(m => Loans.InVillage(m) && !Loans.IsGuest(m)).ToList();

        /// <summary>마을 사건 하나(조건이 맞는 것 중에서). 일어났으면 true(디버그 창도 쓴다).</summary>
        public static bool TriggerVillage(int index = -1)
        {
            var village = InVillage();
            var candidates = new List<Func<(string title, string body, List<MailChoice> choices)>>();
            if (village.Count >= 2) candidates.Add(() => Quarrel(village));
            candidates.Add(Peddler);
            candidates.Add(Festival);
            if (RivalGuilds.All.Count > 0 && village.Count >= 1) candidates.Add(() => Sparring(village));
            if (village.Count >= 1) candidates.Add(() => LeaveRequest(village));
            candidates.Add(Investment);
            if (candidates.Count == 0) return false;
            var build = index >= 0 && index < candidates.Count ? candidates[index] : candidates[Rng.Next(candidates.Count)];
            var (title, body, choices) = build();
            Mailbox.PostChoice(title, body, choices);
            return true;
        }

        private static MailChoice Pick(string label, string tooltip, Func<string> act, Func<bool> available = null) =>
            new MailChoice { Label = label, Tooltip = tooltip, Available = available, Resolve = act };

        private static (string, string, List<MailChoice>) Quarrel(List<Mercenary> village)
        {
            // 사이가 가장 나쁜 둘
            var pair = village.SelectMany((a, i) => village.Skip(i + 1).Select(b => (a, b))).OrderBy(p => Affinity.Get(p.a, p.b)).First();
            var (a, b) = pair;
            return ("용병 다툼",
                $"{Josa.WaGwa(a.Name)} {Josa.IGa(b.Name)} 길드 마당에서 언성을 높이며 다투고 있다. 둘 다 길드장의 판단을 기다린다.",
                new List<MailChoice>
                {
                    Pick($"{a.Name} 편", $"{a.Name} 사기 +8, {b.Name} 사기 −8, 둘 사이 친밀도 하락.", () => Side(a, b)),
                    Pick($"{b.Name} 편", $"{b.Name} 사기 +8, {a.Name} 사기 −8, 둘 사이 친밀도 하락.", () => Side(b, a)),
                    Pick("중재한다 (50G)", "50G로 술자리를 마련해 화해시킨다. 둘 사이 친밀도 상승, 둘 다 사기 +3.", () =>
                    {
                        Wallet.TrySpend(50);
                        Affinity.Add(a, b, 10);
                        a.AddMorale(3);
                        b.AddMorale(3);
                        return $"술 한잔에 앙금이 풀렸다. {Josa.WaGwa(a.Name)} {b.Name} 사이가 조금 가까워졌다.";
                    }, () => Wallet.Gold >= 50),
                });
        }

        private static string Side(Mercenary winner, Mercenary loser)
        {
            winner.AddMorale(8);
            loser.AddMorale(-8);
            Affinity.Add(winner, loser, -10);
            return $"{winner.Name}의 손을 들어 주었다. {Josa.EunNeun(loser.Name)} 불만스러운 얼굴로 자리를 떴다.";
        }

        private static (string, string, List<MailChoice>) Peddler()
        {
            // 보랏빛~황금빛 마정석 하나(몬스터는 아무거나), 값은 시세의 70~130%
            int color = Rng.NextDouble() < 0.6 ? 1 : Rng.NextDouble() < 0.7 ? 2 : 3;
            var sets = new[] { LootCatalog.GoblinMana, LootCatalog.WolfMana, LootCatalog.MushroomMana, LootCatalog.RiderMana };
            var stone = sets[Rng.Next(sets.Length)][color];
            int price = Mathf.RoundToInt(stone.Price * (0.7f + (float)Rng.NextDouble() * 0.6f));
            string deal = price < stone.Price ? "시세보다 싸다" : "시세보다 비싸다";
            return ("떠돌이 상인",
                $"낡은 수레를 끄는 상인이 길드 문을 두드린다. \"{stone.Name}, 단돈 {price}G!\" ({deal} — 시세 {stone.Price}G)",
                new List<MailChoice>
                {
                    Pick($"산다 ({price}G)", $"{Josa.EulReul(stone.Name)} 전리품 가방에 넣는다.", () =>
                    {
                        Wallet.TrySpend(price);
                        LootBag.Add(new List<(LootItem, int)> { (stone, 1) });
                        return $"{Josa.EulReul(stone.Name)} 샀다. 각성이나 시설 증축에 쓸 수 있다.";
                    }, () => Wallet.Gold >= price),
                    Pick("거절한다", "아무 일도 없다.", () => "상인은 어깨를 으쓱하고 다음 마을로 떠났다."),
                });
        }

        private static (string, string, List<MailChoice>) Festival() => ("마을 축제",
            "마을 촌장이 다가오는 수확 축제에 길드도 힘을 보태 달라고 부탁한다.",
            new List<MailChoice>
            {
                Pick("기부한다 (100G)", "명성 +10.", () =>
                {
                    Wallet.TrySpend(100);
                    Guild.Add(10);
                    return "길드 깃발이 축제 한가운데 걸렸다. 명성이 올랐다(+10).";
                }, () => Wallet.Gold >= 100),
                Pick("거절한다", "아무 일도 없다.", () => "형편이 어렵다며 정중히 거절했다."),
            });

        private static (string, string, List<MailChoice>) Sparring(List<Mercenary> village)
        {
            var g = RivalGuilds.All[Rng.Next(RivalGuilds.All.Count)];
            var team = village.OrderByDescending(m => m.CombatPower).Take(3).ToList();
            return ("친선 대련 제안",
                $"{g.Name}에서 친선 대련을 하자는 전갈이 왔다. 우리 쪽 출전: {string.Join(", ", team.Select(m => m.Name))}",
                new List<MailChoice>
                {
                    Pick("수락한다", $"{g.Name} 관계 +8, 나선 용병 피로 +15 · 조금 성장.", () =>
                    {
                        RivalGuilds.AddRelation(g, 8, "우리 길드와 친선 대련을 했다");
                        var gains = new List<string>();
                        foreach (var m in team)
                        {
                            m.AddFatigue(15);
                            var g2 = m.AddGrowth(0.5f);
                            if (g2.Count > 0) gains.Add($"{m.Name} {string.Join(", ", g2)}");
                        }
                        return $"땀 흘린 대련 끝에 {Josa.WaGwa(g.Name)} 사이가 가까워졌다." + (gains.Count > 0 ? $"\n성장: {string.Join(" · ", gains)}" : "");
                    }),
                    Pick("거절한다", $"{g.Name} 관계 −3.", () =>
                    {
                        RivalGuilds.AddRelation(g, -3, "친선 대련을 거절당했다");
                        return $"{Josa.EunNeun(g.Name)} 조금 서운해하는 눈치다.";
                    }),
                });
        }

        private static (string, string, List<MailChoice>) LeaveRequest(List<Mercenary> village)
        {
            var merc = village.OrderBy(m => m.Morale).First();
            return ("용병의 부탁",
                $"{Josa.IGa(merc.Name)} 조심스럽게 찾아왔다. \"며칠 고향에 다녀와도 되겠습니까? 휴가비가 조금 필요합니다.\"",
                new List<MailChoice>
                {
                    Pick("허락한다 (30G)", "휴가비 30G. 사기 +20, 피로 −30.", () =>
                    {
                        Wallet.TrySpend(30);
                        merc.AddMorale(20);
                        merc.AddFatigue(-30);
                        return $"{Josa.IGa(merc.Name)} 환하게 웃으며 고개를 숙였다. 한결 가벼운 얼굴로 돌아왔다.";
                    }, () => Wallet.Gold >= 30),
                    Pick("거절한다", "사기 −10.", () =>
                    {
                        merc.AddMorale(-10);
                        return $"{Josa.IGa(merc.Name)} 말없이 돌아섰다.";
                    }),
                });
        }

        private static (string, string, List<MailChoice>) Investment() => ("수상한 투자 제안",
            "번드르르한 차림의 남자가 \"200G만 맡기면 열흘 안에 두 배로 불려 드리지요\"라며 계약서를 내민다.",
            new List<MailChoice>
            {
                Pick("투자한다 (200G)", "50%로 400G를 돌려받고, 50%로 날린다.", () =>
                {
                    Wallet.TrySpend(200);
                    if (Rng.NextDouble() < 0.5)
                    {
                        Wallet.Add(400);
                        return "놀랍게도 약속은 지켜졌다! 400G가 돌아왔다(+200G).";
                    }
                    return "남자는 다시 나타나지 않았다… 200G를 잃었다.";
                }, () => Wallet.Gold >= 200),
                Pick("거절한다", "아무 일도 없다.", () => "수상한 제안은 정중히 돌려보냈다."),
            });
    }
}
