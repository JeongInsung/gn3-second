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
    /// <summary>
    /// 길드 연대기: 업적(달성하면 편지·금화 보상) · 길드 등급이 오를 때 축하 선물 · 명예의 전당(은퇴·전사·병사한 용병) ·
    /// 전체 기간 통계(의뢰 기록은 최근 100건·장부는 14일만 남지만 누적은 여기서 센다). 업적 창(AchievementsPanel, Y)이 읽는다.
    /// 판정: 하루가 지날 때 · 의뢰를 끝냈을 때 · 등급이 바뀔 때. 저장은 SaveData(버전 15).
    /// </summary>
    public static class Achievements
    {
        public class Def
        {
            public string Id, Title, Description;
            public int Reward;
            public Func<bool> Done;
            public Func<string> Progress; // "7/10"(없으면 null)
        }

        /// <summary>명예의 전당 한 줄.</summary>
        public class Memorial
        {
            public string Name, ClassName, Reason;
            public int Day, Age, Battles, Wins, Kills, BestScore, Clears;
        }

        private static readonly HashSet<string> _done = new HashSet<string>();
        private static readonly List<Memorial> _memorials = new List<Memorial>();
        private static int _giftedRank = -1; // 축하 선물을 이미 준 등급(이보다 높아질 때만)
        private static bool _subscribed;

        // 전체 기간 통계
        public static int LifeQuests { get; private set; }
        public static int LifeWins { get; private set; }
        public static int LifeReward { get; private set; }
        public static long LifeGoldIn { get; private set; }
        public static long LifeGoldOut { get; private set; }

        public static IReadOnlyList<Memorial> Memorials => _memorials;
        public static bool IsDone(Def def) => _done.Contains(def.Id);
        public static int DoneCount => All.Count(IsDone);

        public static event Action OnChanged;

        /// <summary>불러오는 중(SaveSystem): 복원하며 오르는 등급·값으로 업적·축하금이 나가지 않게 판정을 멈춘다.</summary>
        public static bool Suspended { get; set; }

        private static string Of(long value, long goal) => $"{Math.Min(value, goal):N0}/{goal:N0}";

        public static readonly Def[] All =
        {
            new Def { Id = "first_win", Title = "첫 의뢰 성공", Description = "의뢰를 처음으로 성공한다", Reward = 100, Done = () => LifeWins >= 1, Progress = () => Of(LifeWins, 1) },
            new Def { Id = "quests_10", Title = "부지런한 용병단", Description = "의뢰를 10건 끝낸다", Reward = 300, Done = () => LifeQuests >= 10, Progress = () => Of(LifeQuests, 10) },
            new Def { Id = "quests_50", Title = "이름난 길드", Description = "의뢰를 50건 끝낸다", Reward = 1000, Done = () => LifeQuests >= 50, Progress = () => Of(LifeQuests, 50) },
            new Def { Id = "quests_100", Title = "백 번의 원정", Description = "의뢰를 100건 끝낸다", Reward = 2500, Done = () => LifeQuests >= 100, Progress = () => Of(LifeQuests, 100) },
            new Def { Id = "wins_25", Title = "승리의 깃발", Description = "의뢰를 25번 성공한다", Reward = 800, Done = () => LifeWins >= 25, Progress = () => Of(LifeWins, 25) },
            new Def { Id = "forest_3", Title = "숲 깊숙이", Description = "은월의 숲 Lv3을 연다", Reward = 300, Done = () => ForestProgress.UnlockedLevel >= 3, Progress = () => Of(ForestProgress.UnlockedLevel, 3) },
            new Def { Id = "forest_5", Title = "앤트킹의 문턱", Description = "은월의 숲 Lv5를 연다", Reward = 600, Done = () => ForestProgress.UnlockedLevel >= 5, Progress = () => Of(ForestProgress.UnlockedLevel, 5) },
            new Def { Id = "antking", Title = "숲의 왕을 쓰러뜨리다", Description = "앤트킹을 토벌한다", Reward = 2000, Done = () => ForestProgress.BossDefeated },
            new Def { Id = "rank_3", Title = "자리 잡은 길드", Description = $"길드 등급 {Guild.Ranks[Math.Min(2, Guild.Ranks.Length - 1)].Title}에 오른다", Reward = 300, Done = () => Guild.Rank >= Math.Min(2, Guild.Ranks.Length - 1) },
            new Def { Id = "rank_max", Title = "왕국 제일의 길드", Description = "길드 등급 최고 단계에 오른다", Reward = 3000, Done = () => Guild.IsMaxRank },
            new Def { Id = "party_5", Title = "다섯 개의 칼", Description = "용병 5명을 거느린다", Reward = 200, Done = () => PartySize >= 5, Progress = () => Of(PartySize, 5) },
            new Def { Id = "party_10", Title = "대부대", Description = "용병 10명을 거느린다", Reward = 800, Done = () => PartySize >= 10, Progress = () => Of(PartySize, 10) },
            new Def { Id = "income_10k", Title = "두둑한 금고", Description = "누적 수입 10,000G", Reward = 500, Done = () => LifeGoldIn >= 10000, Progress = () => Of(LifeGoldIn, 10000) },
            new Def { Id = "income_100k", Title = "황금 길드", Description = "누적 수입 100,000G", Reward = 3000, Done = () => LifeGoldIn >= 100000, Progress = () => Of(LifeGoldIn, 100000) },
            new Def { Id = "bestiary", Title = "숲의 박물학자", Description = "도감의 몬스터를 모두 발견한다", Reward = 1000, Done = () => Bestiary.DiscoveredCount >= Bestiary.All.Length, Progress = () => Of(Bestiary.DiscoveredCount, Bestiary.All.Length) },
            new Def { Id = "facility_max", Title = "최고의 시설", Description = "길드 시설 하나를 최고 단계까지 강화한다", Reward = 800, Done = () => GuildFacilities.All.Any(f => GuildFacilities.Level(f) >= GuildFacilities.MaxLevel) },
        };

        private static int PartySize => PlayerParty.Instance.Members.Count(m => m.IsAlive && !Loans.IsGuest(m));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _done.Clear();
            _memorials.Clear();
            _giftedRank = -1;
            LifeQuests = LifeWins = LifeReward = 0;
            LifeGoldIn = LifeGoldOut = 0;
            _subscribed = false;
            Suspended = false;
            OnChanged = null;
        }

        // GameClock·Guild 등의 이벤트가 Play마다 비워진 뒤에 구독한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            if (_giftedRank < 0) _giftedRank = Guild.Rank; // 새 게임: 지금 등급(처음 단계)은 선물 없이
            Wallet.OnLedger += (amount, _) =>
            {
                if (amount > 0) LifeGoldIn += amount;
                else LifeGoldOut += -amount;
            };
            QuestHistory.OnRecorded += entry =>
            {
                LifeQuests++;
                if (entry.Win) LifeWins++;
                LifeReward += entry.Reward;
                Check();
            };
            GameClock.OnDayAdvanced += Check;
            Guild.OnChanged += rankUp =>
            {
                if (rankUp) GiftRank();
                Check();
            };
        }

        /// <summary>새로 이룬 업적마다 보상·편지. silent면 보상 없이 이룬 것으로만(예전 저장을 처음 불러올 때).</summary>
        public static void Check() => Check(false);

        private static void Check(bool silent)
        {
            if (Suspended && !silent) return;
            bool changed = false;
            foreach (var def in All)
            {
                if (_done.Contains(def.Id) || !SafeDone(def)) continue;
                _done.Add(def.Id);
                changed = true;
                if (silent) continue;
                Wallet.Add(def.Reward, "업적 보상");
                string line = $"업적 달성: {def.Title} — {def.Description} (+{def.Reward}G)";
                ToastLog.Show(line, false, ToastKind.Good);
                Mailbox.Post(MailKind.Notice, $"업적 달성 — {def.Title}", $"{def.Description}\n보상으로 {def.Reward}G를 받았습니다.\n\n업적 창(Y)에서 모든 업적을 볼 수 있습니다.");
            }
            if (changed) OnChanged?.Invoke();
        }

        private static bool SafeDone(Def def)
        {
            try { return def.Done(); }
            catch (Exception e) { Debug.LogWarning($"[Achievements] {def.Id} 판정 실패: {e.Message}"); return false; }
        }

        /// <summary>길드 등급이 처음 오를 때마다: 축하 편지 + 금화(등급 × 200G).</summary>
        private static void GiftRank()
        {
            if (Suspended || Guild.Rank <= _giftedRank) return;
            _giftedRank = Guild.Rank;
            int gift = 200 * Guild.Rank;
            Wallet.Add(gift, "등급 축하금");
            Mailbox.Post(MailKind.Notice, $"{Guild.CurrentName}이 되었습니다",
                $"마을 사람들이 길드의 성장을 축하하며 축하금 {gift}G를 모아 보냈습니다.\n\n{Guild.Tooltip()}", important: true);
            OnChanged?.Invoke();
        }

        /// <summary>은퇴·전사·병사한 용병을 명예의 전당에 남긴다(파티에서 빼기 직전에 부른다).</summary>
        public static void Memorialize(Mercenary merc, string reason)
        {
            if (merc == null || Loans.IsGuest(merc)) return; // 빌려 온 손님은 우리 길드 사람이 아니다
            var r = merc.Record;
            _memorials.Add(new Memorial
            {
                Name = merc.Name, ClassName = merc.Class.ClassName, Reason = reason, Day = GameClock.CurrentDay, Age = merc.Age,
                Battles = r.Battles, Wins = r.Wins, Kills = r.Kills, BestScore = r.BestScore, Clears = r.TotalClears,
            });
            OnChanged?.Invoke();
        }

        // ---------- 저장 ----------

        public static List<string> SaveDone() => _done.ToList();
        public static int SaveGiftedRank() => _giftedRank;

        public static List<Save.MemorialSave> SaveMemorials() => _memorials.Select(m => new Save.MemorialSave
        {
            name = m.Name, className = m.ClassName, reason = m.Reason, day = m.Day, age = m.Age,
            battles = m.Battles, wins = m.Wins, kills = m.Kills, bestScore = m.BestScore, clears = m.Clears,
        }).ToList();

        /// <summary>
        /// 저장에서 되돌린다. legacy(버전 14 이하)면 누적 통계를 남아 있는 기록(의뢰 기록·장부)에서 어림하고,
        /// 이미 이룬 업적은 보상 없이 이룬 것으로만 친다(불러오자마자 편지가 쏟아지지 않게). 등급 선물도 지금 등급까지는 준 것으로.
        /// </summary>
        public static void Restore(IEnumerable<string> done, int giftedRank, int quests, int wins, int reward, long goldIn, long goldOut,
            IEnumerable<Save.MemorialSave> memorials, bool legacy)
        {
            _done.Clear();
            if (done != null) foreach (var id in done) _done.Add(id);
            _memorials.Clear();
            if (memorials != null)
                _memorials.AddRange(memorials.Select(m => new Memorial
                {
                    Name = m.name, ClassName = m.className, Reason = m.reason, Day = m.day, Age = m.age,
                    Battles = m.battles, Wins = m.wins, Kills = m.kills, BestScore = m.bestScore, Clears = m.clears,
                }));
            if (legacy)
            {
                var history = QuestHistory.Entries;
                LifeQuests = history.Count;
                LifeWins = history.Count(e => e.Win);
                LifeReward = history.Sum(e => e.Reward);
                LifeGoldIn = GoldLedger.Entries.Where(e => e.Amount > 0).Sum(e => (long)e.Amount);
                LifeGoldOut = GoldLedger.Entries.Where(e => e.Amount < 0).Sum(e => (long)-e.Amount);
                _giftedRank = Guild.Rank;
                Check(silent: true);
            }
            else
            {
                LifeQuests = quests;
                LifeWins = wins;
                LifeReward = reward;
                LifeGoldIn = goldIn;
                LifeGoldOut = goldOut;
                _giftedRank = giftedRank < 0 ? Guild.Rank : giftedRank;
            }
            OnChanged?.Invoke();
        }
    }
}
