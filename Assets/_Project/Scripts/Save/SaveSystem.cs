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
            int rank = 0;
            for (int i = 0; i < Guild.Ranks.Length; i++)
                if (data.reputation >= Guild.Ranks[i].Required) rank = i;
            return $"{GameCalendar.Format(data.day)} · {Guild.Ranks[rank].Name} · 용병 {data.party.Count}명 · {data.gold}G" +
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
                data.party.Add(m);
            }

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
                e.memberIds.AddRange(expedition.Members.Where(m => m.IsAlive).Select(m => m.Id));
                e.leaderId = expedition.Leader?.Id;
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
            data.leaving = MercenaryCondition.SaveLeaving();
            data.storageWeapons = GuildStorage.WeaponNames();
            data.storageItems = GuildStorage.ItemNames();
            return data;
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
                var cls = classes.FirstOrDefault(c => c.ClassName == m.className);
                if (cls == null)
                {
                    Debug.LogWarning($"[SaveSystem] 클래스 '{m.className}'를 찾지 못해 {m.name}을(를) 건너뜀");
                    continue;
                }
                var sources = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < m.partNames.Count && i < m.partSources.Count; i++)
                    sources.Add(new KeyValuePair<string, string>(m.partNames[i], m.partSources[i]));
                var appearance = RandomCharacterComposer.Rebuild(sources, m.bodyCharacter);

                var merc = new Mercenary(m.name, cls, m.age, m.birthday, appearance, (Personality)m.personality, m.rarePassive,
                    (MercenaryGrade)m.grade, m.id);
                var weapon = string.IsNullOrEmpty(m.weapon) ? null : WeaponCatalog.All.FirstOrDefault(w => w.Name == m.weapon);
                // 성장·수련은 최대 체력을 바꾸므로 체력보다 먼저
                if (data.version >= 3)
                {
                    // 버전 5 이하 힐러: 신성력 성장이 없었다 → 다른 네 능력치 성장 평균만큼 준다(오래 키운 힐러가 새 힐러보다 약하지 않게).
                    int holy = data.version >= 6 ? m.growthHoly
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
                if (data.version < 4)
                {
                    merc.MatchLegacyPower(LegacyRng); // 재능 능력치 배율이 빠진 만큼 성장으로 메운다
                    merc.SetHealth(m.health);
                }
                if (m.hasMonthBase && data.version >= 6) merc.RestoreMonthBase(m.monthAttack, m.monthDefense, m.monthSpeed, m.monthHoly, m.monthPower);
                else merc.SnapshotMonth(); // 예전 저장: 지금 값을 이번 달 기준으로(버전 5는 힐러 신성력·전투력이 바뀌어 다시 찍는다)
                merc.RestoreCondition(m.fatigue, m.morale);
                if (m.ailments != null)
                    foreach (var ailment in m.ailments)
                    {
                        // 에셋을 지웠으면 그 상태이상은 건너뛴다.
                        var def = string.IsNullOrEmpty(ailment.id) ? AilmentCatalog.FindLegacy(ailment.kind) : AilmentCatalog.Find(ailment.id);
                        if (def != null) merc.AddAilment(def, ailment.progress, ailment.daysUntreated);
                    }
                mercs.Add(merc);
            }

            // 지금 상태를 비우고 채운다. 파견을 먼저 넣어야 마을이 파견 중인 용병을 걸어 다니게 만들지 않는다.
            ExpeditionLog.Instance.Clear();
            PlayerParty.Instance.Clear();

            Wallet.Restore(data.gold);
            Guild.Restore(data.reputation);
            GameClock.Restore(data.day, data.hour);

            foreach (var e in data.expeditions)
            {
                var members = e.memberIds.Select(id => mercs.FirstOrDefault(m => m.Id == id)).Where(m => m != null).ToList();
                if (members.Count == 0) continue;
                var quest = new Quest(e.title, (Region)e.region, e.location, e.enemy, e.enemyCount, e.difficulty, e.durationDays);
                // 버전 1 저장은 일 단위뿐이라 × 24시간으로 읽는다.
                bool hours = data.version >= 2;
                quest.SetRemainingHours(hours ? e.remainingHours : e.remainingDays * Quest.HoursPerDay);
                var leader = string.IsNullOrEmpty(e.leaderId) ? null : members.FirstOrDefault(m => m.Id == e.leaderId);
                var expedition = new Expedition(quest, members, leader);
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
            MercenaryCondition.RestoreLeaving(data.leaving);
            GuildStorage.Restore(data.storageWeapons, data.storageItems);

            OnLoaded?.Invoke();
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => OnLoaded = null;
    }
}
