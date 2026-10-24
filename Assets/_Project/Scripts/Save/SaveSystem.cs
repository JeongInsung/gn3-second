using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GN3.CharacterAnim;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Traits;
using GN3.World;
using UnityEngine;

namespace GN3.Save
{
    /// <summary>
    /// 진행 상황을 Application.persistentDataPath/save.json에 저장·불러온다(JsonUtility).
    /// 저장할 때마다 같은 내용을 saves/에 시간 붙은 기록으로도 남긴다(자동·직접 각각 최근 HistoryKeep개). 불러오기 창(LoadGamePanel)이 그 목록에서 고른다.
    /// 저장: 골드·일차·시각·명성·파티 용병(외형 포함)·진행 중인 파견. 시장·게시판은 불러온 뒤 새로 뽑는다.
    /// </summary>
    public static class SaveSystem
    {
        private static readonly System.Random LegacyRng = new System.Random(); // 레벨제(버전 2 이하) 저장의 나이·생일 정하기

        private const int HistoryKeep = 10;
        private const string AutoTag = "auto";
        private const string ManualTag = "manual";

        public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");
        public static string HistoryFolder => Path.Combine(Application.persistentDataPath, "saves");

        public static bool HasSave => File.Exists(FilePath);

        /// <summary>불러오기가 끝났을 때(화면 쪽이 시계·시장·게시판을 다시 맞춘다).</summary>
        public static event Action OnLoaded;

        /// <summary>불러오기 목록의 한 줄: 기록 파일 하나.</summary>
        public sealed class SaveEntry
        {
            public string Path;
            public bool IsAuto;
            public DateTime SavedAt;
            public string Description;
        }

        // ---------- 저장 ----------

        /// <summary>save.json에 쓰고, 같은 내용을 기록(saves/)으로도 남긴다. auto = 자정 자동 저장.</summary>
        public static bool Save(bool auto = false)
        {
            // 기록 기능 이전의 save.json은 덮어쓰기 전에 기록으로 옮겨 둔다(처음 한 번).
            try { ImportCurrentSaveOnce(); }
            catch (Exception e) { Debug.LogError($"[SaveSystem] 기존 저장을 기록으로 옮기지 못함: {e}"); }

            string json;
            try
            {
                json = JsonUtility.ToJson(Capture(), true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 실패: {e}");
                return false;
            }
            try
            {
                WriteHistory(json, auto);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 기록을 남기지 못함(저장 자체는 됨): {e}");
            }
            return true;
        }

        public static void Delete()
        {
            if (HasSave) File.Delete(FilePath);
        }

        private static void WriteHistory(string json, bool auto)
        {
            Directory.CreateDirectory(HistoryFolder);
            string tag = auto ? AutoTag : ManualTag;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(HistoryFolder, $"save_{stamp}_{tag}.json");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(HistoryFolder, $"save_{stamp}_{tag}_{n}.json");
            File.WriteAllText(path, json);

            // 같은 종류는 최근 HistoryKeep개만 남긴다.
            var old = new DirectoryInfo(HistoryFolder).GetFiles($"save_*_{tag}*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(HistoryKeep);
            foreach (var file in old) file.Delete();
        }

        /// <summary>기록 기능 이전의 save.json도 목록에 나오게: saves/가 비어 있으면 지금 save.json을 기록 하나로 복사한다(파일 시각 유지).</summary>
        private static void ImportCurrentSaveOnce()
        {
            if (!HasSave) return;
            if (Directory.Exists(HistoryFolder) && Directory.EnumerateFiles(HistoryFolder, "save_*.json").Any()) return;
            Directory.CreateDirectory(HistoryFolder);
            var time = File.GetLastWriteTime(FilePath);
            string path = Path.Combine(HistoryFolder, $"save_{time:yyyyMMdd_HHmmss}_{ManualTag}.json");
            File.Copy(FilePath, path, true);
            File.SetLastWriteTime(path, time);
        }

        /// <summary>불러올 수 있는 기록(최신순). 읽지 못하는 파일은 뺀다.</summary>
        public static List<SaveEntry> ListSaves()
        {
            try { ImportCurrentSaveOnce(); }
            catch (Exception e) { Debug.LogError($"[SaveSystem] 기존 저장을 기록으로 옮기지 못함: {e}"); }

            var list = new List<SaveEntry>();
            if (!Directory.Exists(HistoryFolder)) return list;
            foreach (var file in new DirectoryInfo(HistoryFolder).GetFiles("save_*.json").OrderByDescending(f => f.LastWriteTime))
            {
                string description = Describe(file.FullName);
                if (string.IsNullOrEmpty(description)) continue;
                list.Add(new SaveEntry
                {
                    Path = file.FullName,
                    IsAuto = file.Name.Contains("_" + AutoTag),
                    SavedAt = file.LastWriteTime,
                    Description = description,
                });
            }
            return list;
        }

        /// <summary>저장 파일의 짧은 설명("650년 3월 12일 · 은빛 길드 · 용병 4명 · 300G"). 이어하기 창·불러오기 창에 쓴다.
        /// path가 없으면 save.json, savedAt이 있으면 둘째 줄에 "저장: …".</summary>
        public static string Describe(string path = null)
        {
            var data = Read(path);
            if (data == null) return "";
            // 길드 홀이 없으면 "… 용병단"이고 동빛까지만 오른다(Guild.RankName). 건물 목록이 없던 예전 저장(버전 9 이하)은 길드 홀이 있던 마을이다.
            var hall = BuildingCatalog.Get("guildhall");
            bool isGuild = data.version < 10 || (data.buildings != null && data.buildings.Any(b => hall != null && hall.Matches(b)));
            int rank = Guild.RankFor(data.reputation);
            if (!isGuild) rank = Mathf.Min(rank, Guild.MercenaryBandMaxRank);
            string rankName = Guild.Ranks[rank].Title + (isGuild ? " 길드" : " 용병단");
            return $"{GameCalendar.Format(data.day)} · {rankName} · 용병 {data.party.Count}명 · {data.gold}G" +
                   (string.IsNullOrEmpty(data.savedAt) ? "" : $"\n저장: {data.savedAt}");
        }

        private static SaveData Capture()
        {
            var data = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                gold = Wallet.Gold,
                day = GameClock.CurrentDay,
                hour = GameClock.CurrentHour,
                reputation = Guild.Reputation,
            };

            foreach (var merc in PlayerParty.Instance.Members)
                data.party.Add(ToSave(merc));

            // 게시판·시장 목록(불러와도 같은 목록 — 불러오기로 다시 뽑는 꼼수 방지). 아직 안 뽑았으면 비워 둔다.
            if (QuestBoardListings.Current != null)
            {
                data.hasQuestBoard = true;
                data.questBoardNextDay = QuestBoardListings.NextRefreshDay;
                foreach (var q in QuestBoardListings.Current)
                {
                    var b = new BoardQuestSave
                    {
                        title = q.Title, region = (int)q.Region, location = q.LocationName, enemy = q.EnemyName, enemyCount = q.EnemyCount,
                        difficulty = q.Difficulty, durationDays = q.DurationDays, requester = q.Requester, requestText = q.RequestText,
                        challenge = q.IsChallenge, allyGuildId = q.AllyGuildId, allyCount = q.AllyCount,
                    };
                    foreach (var w in q.Waves)
                        b.waves.Add(new WaveSave { enemy = w.EnemyName, count = w.Count, tier = w.Tier });
                    data.questBoard.Add(b);
                }
            }
            if (MarketListings.Current != null)
            {
                data.hasMarket = true;
                data.marketNextDay = MarketListings.NextRefreshDay;
                foreach (var merc in MarketListings.Current)
                    data.market.Add(ToSave(merc));
                data.marketOrigins = MarketListings.SaveOrigins();
            }
            CaptureRest(data);
            return data;
        }

        private static MercenarySave ToSave(Mercenary merc)
        {
            var m = new MercenarySave
            {
                id = merc.Id,
                name = merc.Name,
                className = merc.Class != null ? merc.Class.ClassName : "",
                grade = (int)merc.Talent,
                age = merc.Age,
                birthday = merc.BirthdayIndex,
                growthAttack = merc.GrowthAttack,
                growthDefense = merc.GrowthDefense,
                growthHealth = merc.GrowthHealth,
                growthSpeed = merc.GrowthSpeed,
                growthHoly = merc.GrowthHoly,
                growthProgress = merc.GrowthProgress,
                hasMonthBase = true,
                monthAttack = merc.MonthBaseAttack,
                monthDefense = merc.MonthBaseDefense,
                monthSpeed = merc.MonthBaseSpeed,
                monthHoly = merc.MonthBaseHoly,
                monthPower = merc.MonthBasePower,
                personality = (int)merc.Personality,
                rarePassive = merc.HasRarePassive,
                health = merc.CurrentHealth,
                weapon = merc.Weapon != null ? merc.Weapon.Name : "",
                fatigue = merc.Fatigue,
                morale = merc.Morale,
                researchHours = merc.ResearchHours,
                researchBonus = merc.ResearchLevel,
                bodyCharacter = merc.Appearance.BodyCharacter,
            };
            foreach (var pair in merc.Appearance.PartSources)
            {
                m.partNames.Add(pair.Key);
                m.partSources.Add(pair.Value);
            }
            foreach (var ailment in merc.Ailments)
                m.ailments.Add(new AilmentSave { id = ailment.Def.Id, progress = ailment.Progress, daysUntreated = ailment.DaysUntreated });
            return m;
        }

        private static void CaptureRest(SaveData data)
        {
            foreach (var expedition in ExpeditionLog.Instance.Active)
            {
                var q = expedition.Quest;
                var e = new ExpeditionSave
                {
                    title = q.Title,
                    region = (int)q.Region,
                    location = q.LocationName,
                    enemy = q.EnemyName,
                    enemyCount = q.EnemyCount,
                    difficulty = q.Difficulty,
                    durationDays = q.DurationDays,
                    remainingDays = q.RemainingDays,
                    outboundDays = (int)System.Math.Ceiling(expedition.OutboundHours / Quest.HoursPerDay),
                    returning = expedition.IsReturning,
                    returnDaysLeft = expedition.ReturnDaysLeft,
                    remainingHours = q.RemainingHours,
                    outboundHours = expedition.OutboundHours,
                    returnHoursLeft = expedition.ReturnHoursLeft,
                };
                foreach (var m in expedition.Members.Where(m => m.IsAlive))
                {
                    e.memberIds.Add(m.Id);
                    e.memberSquads.Add(expedition.SquadOf(m));
                }
                e.leaderId = expedition.Leader?.Id;
                e.leaderId2 = expedition.Quest.SquadCount > 1 ? expedition.LeaderOf(1)?.Id : null;
                e.allyGuildId = q.AllyGuildId;
                e.allyCount = q.AllyCount;
                foreach (var w in q.Waves)
                    e.waves.Add(new WaveSave { enemy = w.EnemyName, count = w.Count, tier = w.Tier });
                data.expeditions.Add(e);
            }
            data.trainees = TrainingHall.SaveIds();
            data.patients = Hospital.SaveIds();
            data.bathers = RestVenues.HotSpring.SaveIds();
            data.gamblers = RestVenues.GamblingDen.SaveIds();
            data.researchers = RestVenues.MagicLab.SaveIds();
            data.prayers = RestVenues.Cathedral.SaveIds();
            data.affinities = Affinity.SaveEntries();
            data.questClears = QuestClearHistory.SaveEntries();
            data.forestLevel = ForestProgress.UnlockedLevel;
            data.leaving = MercenaryCondition.SaveLeaving();
            data.contracts = Contracts.SaveEntries();
            data.storageWeapons = GuildStorage.WeaponNames();
            data.storageItems = GuildStorage.ItemNames();
            data.loot = LootBag.SaveEntries();
            data.bestiaryKills = Bestiary.SaveEntries();
            data.bestiaryBest = Bestiary.SaveBest();
            data.facilities = GuildFacilities.SaveEntries();
            data.buildings = VillageLayout.SaveEntries();
            CaptureRivals(data);
        }

        /// <summary>근처 길드: 소속 용병은 파티·시장과 같은 MercenarySave로.</summary>
        private static void CaptureRivals(SaveData data)
        {
            foreach (var g in RivalGuilds.All)
            {
                var s = new RivalGuildSave
                {
                    id = g.Id, name = g.Name, temper = (int)g.Temper, reputation = g.Reputation, gold = g.Gold,
                    relation = g.Relation, lastGiftDay = g.LastGiftDay,
                };
                foreach (var merc in g.Roster)
                {
                    s.roster.Add(ToSave(merc));
                    s.rosterEnd.Add(g.RosterEnd.TryGetValue(merc.Id, out int end) ? end : GameClock.CurrentDay + 180);
                }
                foreach (var j in g.Jobs)
                    s.jobs.Add(new RivalJobSave { title = j.Title, endDay = j.EndDay, chance = j.Chance, reward = j.Reward, reputation = j.Reputation });
                foreach (var (day, text) in g.News)
                {
                    s.newsDays.Add(day);
                    s.newsTexts.Add(text);
                }
                data.rivals.Add(s);
            }
            foreach (var (merc, until) in RivalGuilds.FreeAgents)
            {
                data.freeAgents.Add(ToSave(merc));
                data.freeAgentDays.Add(until);
            }
            foreach (var pair in RivalGuilds.Promised) data.rivalPromised.Add(pair.Key + "|" + pair.Value);
            foreach (var l in Loans.All)
                data.loans.Add(new LoanSave
                {
                    mercId = l.Merc.Id, merc = l.Inbound ? null : ToSave(l.Merc), guildId = l.GuildId, inbound = l.Inbound, endDay = l.EndDay,
                    dailyFee = l.DailyFee, joint = l.Joint, questId = l.QuestId, rosterEnd = l.RosterEnd,
                });
            var target = RivalGuilds.TargetQuest;
            data.rivalTargetIndex = target != null && QuestBoardListings.Current != null ? QuestBoardListings.Current.IndexOf(target) : -1;
            data.rivalTargetGuild = RivalGuilds.TargetGuild?.Id;
        }

        private static void RestoreRivals(SaveData data, MercenaryClassSO[] classes)
        {
            var guilds = new List<RivalGuild>();
            foreach (var s in data.rivals ?? new List<RivalGuildSave>())
            {
                var g = new RivalGuild
                {
                    Id = s.id, Name = s.name, Temper = (RivalTemper)s.temper, Reputation = s.reputation, Gold = s.gold,
                    Relation = s.relation, LastGiftDay = s.lastGiftDay,
                };
                for (int i = 0; i < s.roster.Count; i++)
                {
                    var merc = FromSave(s.roster[i], classes, data.version);
                    if (merc == null) continue;
                    g.Roster.Add(merc);
                    g.RosterEnd[merc.Id] = i < s.rosterEnd.Count ? s.rosterEnd[i] : data.day + 180;
                }
                foreach (var j in s.jobs)
                    g.Jobs.Add(new RivalJob { Title = j.title, EndDay = j.endDay, Chance = j.chance, Reward = j.reward, Reputation = j.reputation });
                for (int i = 0; i < s.newsDays.Count && i < s.newsTexts.Count; i++)
                    g.News.Add((s.newsDays[i], s.newsTexts[i]));
                guilds.Add(g);
            }
            var free = new List<(Mercenary, int)>();
            for (int i = 0; i < data.freeAgents.Count; i++)
            {
                var merc = FromSave(data.freeAgents[i], classes, data.version);
                if (merc != null) free.Add((merc, i < data.freeAgentDays.Count ? data.freeAgentDays[i] : data.day + 7));
            }
            var promised = new List<KeyValuePair<string, string>>();
            foreach (var entry in data.rivalPromised)
            {
                int bar = entry.IndexOf('|');
                if (bar > 0) promised.Add(new KeyValuePair<string, string>(entry.Substring(0, bar), entry.Substring(bar + 1)));
            }
            RivalGuilds.Restore(guilds, free, promised);
            RivalGuilds.RestoreTarget(data.rivalTargetIndex, data.rivalTargetGuild);

            var loans = new List<Loan>();
            foreach (var l in data.loans)
            {
                var merc = l.inbound ? PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == l.mercId)
                    : l.merc != null && !string.IsNullOrEmpty(l.merc.className) ? FromSave(l.merc, classes, data.version) : null;
                if (merc == null) continue;
                loans.Add(new Loan { Merc = merc, GuildId = l.guildId, Inbound = l.inbound, EndDay = l.endDay, DailyFee = l.dailyFee, Joint = l.joint, QuestId = l.questId, RosterEnd = l.rosterEnd });
            }
            Loans.Restore(loans);
        }

        // ---------- 불러오기 ----------

        /// <summary>path가 없으면 save.json.</summary>
        private static SaveData Read(string path = null)
        {
            path ??= FilePath;
            try
            {
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null || data.version > SaveData.CurrentVersion) return null;
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 파일을 읽지 못함: {e}");
                return null;
            }
        }

        /// <summary>저장 파일(path가 없으면 save.json, 있으면 그 기록)을 읽어 지금 게임 상태를 그것으로 바꾼다. 실패하면 false(상태는 그대로).</summary>
        public static bool Load(string path = null)
        {
            var data = Read(path);
            if (data == null) return false;

            var classes = Resources.LoadAll<MercenaryClassSO>("MercenaryClasses");
            var mercs = new List<Mercenary>();
            foreach (var m in data.party)
            {
                var merc = FromSave(m, classes, data.version);
                if (merc != null) mercs.Add(merc);
            }

            // 지금 상태를 비우고 채운다. 파견을 먼저 넣어야 마을이 파견 중인 용병을 걸어 다니게 만들지 않는다.
            ExpeditionLog.Instance.Clear();
            PlayerParty.Instance.Clear();

            Wallet.Restore(data.gold);
            Guild.Restore(data.reputation);
            GameClock.Restore(data.day, data.hour);

            RestoreRest(data, mercs, classes);

            OnLoaded?.Invoke();
            return true;
        }

        /// <summary>용병 하나를 되살린다(파티·시장 공용). 직업 에셋을 못 찾으면 null.</summary>
        private static Mercenary FromSave(MercenarySave m, MercenaryClassSO[] classes, int version)
        {
            var cls = classes.FirstOrDefault(c => c.ClassName == m.className);
            if (cls == null)
            {
                Debug.LogWarning($"[SaveSystem] 클래스 '{m.className}'를 찾지 못해 {m.name}을(를) 건너뜀");
                return null;
            }
            var sources = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < m.partNames.Count && i < m.partSources.Count; i++)
                sources.Add(new KeyValuePair<string, string>(m.partNames[i], m.partSources[i]));
            var appearance = RandomCharacterComposer.Rebuild(sources, m.bodyCharacter);

            var merc = new Mercenary(m.name, cls, m.age, m.birthday, appearance, (Personality)m.personality, m.rarePassive,
                (MercenaryGrade)m.grade, m.id);
            var weapon = string.IsNullOrEmpty(m.weapon) ? null : WeaponCatalog.All.FirstOrDefault(w => w.Name == m.weapon);
            // 성장·수련은 최대 체력을 바꾸므로 체력보다 먼저
            if (version >= 3)
            {
                // 버전 5 이하 힐러: 신성력 성장이 없었다 → 다른 네 능력치 성장 평균만큼 준다(오래 키운 힐러가 새 힐러보다 약하지 않게).
                int holy = version >= 6 ? m.growthHoly
                    : cls.HolyGrowth > 0 ? (int)Math.Round((m.growthAttack + m.growthDefense + m.growthHealth + m.growthSpeed) / 4.0) : 0;
                merc.RestoreGrowth(m.age, m.birthday, m.growthAttack, m.growthDefense, m.growthHealth, m.growthSpeed, holy, m.growthProgress);
            }
            else
            {
                // 레벨제 저장: Lv N = 다섯 능력치 모두 N-1회 성장(신성력은 힐러만 의미 있음). 나이·생일은 새로 정한다.
                int grown = Math.Max(1, m.level) - 1;
                merc.RestoreGrowth(LegacyRng.Next(20, 30), LegacyRng.Next(GN3.World.GameCalendar.DaysPerYear), grown, grown, grown, grown,
                    cls.HolyGrowth > 0 ? grown : 0, 0f);
            }
            merc.RestoreResearch(m.researchHours, m.researchBonus);
            merc.RestoreState(m.health, weapon);
            if (version < 4)
            {
                merc.MatchLegacyPower(LegacyRng); // 재능 능력치 배율이 빠진 만큼 성장으로 메운다
                merc.SetHealth(m.health);
            }
            if (m.hasMonthBase && version >= 6) merc.RestoreMonthBase(m.monthAttack, m.monthDefense, m.monthSpeed, m.monthHoly, m.monthPower);
            else merc.SnapshotMonth(); // 예전 저장: 지금 값을 이번 달 기준으로(버전 5는 힐러 신성력·전투력이 바뀌어 다시 찍는다)
            merc.RestoreCondition(m.fatigue, m.morale);
            if (m.ailments != null)
                foreach (var ailment in m.ailments)
                {
                    // 에셋을 지웠으면 그 상태이상은 건너뛴다.
                    var def = string.IsNullOrEmpty(ailment.id) ? AilmentCatalog.FindLegacy(ailment.kind) : AilmentCatalog.Find(ailment.id);
                    if (def != null) merc.AddAilment(def, ailment.progress, ailment.daysUntreated);
                }
            return merc;
        }

        private static void RestoreRest(SaveData data, List<Mercenary> mercs, MercenaryClassSO[] classes)
        {
            foreach (var e in data.expeditions)
            {
                var members = new List<Mercenary>();
                var squads = new List<int>();
                for (int i = 0; i < e.memberIds.Count; i++)
                {
                    var merc = mercs.FirstOrDefault(m => m.Id == e.memberIds[i]);
                    if (merc == null) continue;
                    members.Add(merc);
                    squads.Add(e.memberSquads != null && i < e.memberSquads.Count ? e.memberSquads[i] : 0);
                }
                if (members.Count == 0) continue;
                var waves = e.waves != null && e.waves.Count > 0
                    ? e.waves.Select(w => new QuestWave(w.enemy, w.count, w.tier)).ToList()
                    : null; // 예전 저장: enemy × enemyCount 한 라운드
                var quest = new Quest(e.title, (Region)e.region, e.location, e.enemy, e.enemyCount, e.difficulty, e.durationDays, waves)
                    { AllyGuildId = e.allyGuildId, AllyCount = e.allyCount };
                // 버전 1 저장은 일 단위뿐이라 × 24시간으로 읽는다.
                bool hours = data.version >= 2;
                quest.SetRemainingHours(hours ? e.remainingHours : e.remainingDays * Quest.HoursPerDay);
                var leader = string.IsNullOrEmpty(e.leaderId) ? null : members.FirstOrDefault(m => m.Id == e.leaderId);
                var leader2 = string.IsNullOrEmpty(e.leaderId2) ? null : members.FirstOrDefault(m => m.Id == e.leaderId2);
                var expedition = new Expedition(quest, members, leader, squads, leader2);
                expedition.RestoreState(hours ? e.outboundHours : e.outboundDays * Quest.HoursPerDay, e.returning,
                    hours ? e.returnHoursLeft : e.returnDaysLeft * Quest.HoursPerDay);
                ExpeditionLog.Instance.Restore(expedition);
            }

            foreach (var merc in mercs)
                PlayerParty.Instance.RestoreAdd(merc);
            TrainingHall.Restore(data.trainees);
            Hospital.Restore(data.patients);
            RestVenues.HotSpring.Restore(data.bathers);
            RestVenues.GamblingDen.Restore(data.gamblers);
            RestVenues.MagicLab.Restore(data.researchers);
            RestVenues.Cathedral.Restore(data.prayers);
            Affinity.Restore(data.affinities);
            QuestClearHistory.Restore(data.questClears);

            // 게시판·시장 목록: 레벨 개방 이벤트(ForestProgress.OnChanged)보다 먼저 되돌린다. 예전 저장엔 없어 처음 열 때 새로 뽑는다.
            List<Quest> board = null;
            if (data.hasQuestBoard)
                board = data.questBoard.Select(b => new Quest(b.title, (Region)b.region, b.location, b.enemy, b.enemyCount, b.difficulty, b.durationDays,
                    b.waves != null && b.waves.Count > 0 ? b.waves.Select(w => new QuestWave(w.enemy, w.count, w.tier)).ToList() : null,
                    b.requester, b.requestText) { IsChallenge = b.challenge, AllyGuildId = b.allyGuildId, AllyCount = b.allyCount }).ToList();
            QuestBoardListings.Restore(board, data.questBoardNextDay);
            List<Mercenary> market = null;
            if (data.hasMarket)
                market = data.market.Select(m => FromSave(m, classes, data.version)).Where(m => m != null).ToList();
            MarketListings.Restore(market, data.marketNextDay, data.marketOrigins);

            ForestProgress.Restore(data.forestLevel);
            MercenaryCondition.RestoreLeaving(data.leaving);
            Contracts.Restore(data.contracts);
            GuildStorage.Restore(data.storageWeapons, data.storageItems);
            LootBag.Restore(data.loot);
            if (data.version < 11) LootBag.Add(BuildMaterials.StartingKit); // 재료가 생기기 전 저장: 길드 천막 한 채 분
            Bestiary.Restore(data.bestiaryKills, data.bestiaryBest);
            GuildFacilities.Restore(data.facilities);
            VillageLayout.Restore(data.buildings, data.version < 10);
            RestoreRivals(data, classes);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => OnLoaded = null;
    }
}
