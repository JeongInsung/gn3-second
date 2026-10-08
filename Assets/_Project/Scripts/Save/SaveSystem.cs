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
    /// 저장: 골드·일차·시각·명성·파티 용병(외형 포함)·진행 중인 파견. 시장·게시판은 불러온 뒤 새로 뽑는다.
    /// </summary>
    public static class SaveSystem
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static bool HasSave => File.Exists(FilePath);

        /// <summary>불러오기가 끝났을 때(화면 쪽이 시계·시장·게시판을 다시 맞춘다).</summary>
        public static event Action OnLoaded;

        // ---------- 저장 ----------

        public static bool Save()
        {
            try
            {
                var data = Capture();
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 실패: {e}");
                return false;
            }
        }

        public static void Delete()
        {
            if (HasSave) File.Delete(FilePath);
        }

        /// <summary>저장 파일의 짧은 설명("650년 3월 12일 · 은빛 길드 · 용병 4명"). 이어하기 창에 쓴다.</summary>
        public static string Describe()
        {
            var data = Read();
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
                    grade = (int)merc.Grade,
                    level = merc.Level,
                    experience = merc.Experience,
                    personality = (int)merc.Personality,
                    rarePassive = merc.HasRarePassive,
                    health = merc.CurrentHealth,
                    weapon = merc.Weapon != null ? merc.Weapon.Name : "",
                    fatigue = merc.Fatigue,
                    morale = merc.Morale,
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
                };
                e.memberIds.AddRange(expedition.Members.Where(m => m.IsAlive).Select(m => m.Id));
                data.expeditions.Add(e);
            }
            data.trainees = TrainingHall.SaveIds();
            data.patients = Hospital.SaveIds();
            data.affinities = Affinity.SaveEntries();
            return data;
        }

        // ---------- 불러오기 ----------

        private static SaveData Read()
        {
            try
            {
                if (!HasSave) return null;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                if (data == null || data.version > SaveData.CurrentVersion) return null;
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 파일을 읽지 못함: {e}");
                return null;
            }
        }

        /// <summary>저장 파일을 읽어 지금 게임 상태를 그것으로 바꾼다. 실패하면 false(상태는 그대로).</summary>
        public static bool Load()
        {
            var data = Read();
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

                var merc = new Mercenary(m.name, cls, Math.Max(1, m.level), appearance, (Personality)m.personality, m.rarePassive,
                    (MercenaryGrade)m.grade, m.id);
                var weapon = string.IsNullOrEmpty(m.weapon) ? null : WeaponCatalog.All.FirstOrDefault(w => w.Name == m.weapon);
                merc.RestoreState(m.experience, m.health, weapon);
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
                quest.SetRemainingDays(e.remainingDays);
                ExpeditionLog.Instance.Restore(new Expedition(quest, members));
            }

            foreach (var merc in mercs)
                PlayerParty.Instance.RestoreAdd(merc);
            TrainingHall.Restore(data.trainees);
            Hospital.Restore(data.patients);
            Affinity.Restore(data.affinities);

            OnLoaded?.Invoke();
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => OnLoaded = null;
    }
}
