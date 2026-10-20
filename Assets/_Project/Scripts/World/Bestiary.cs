using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using UnityEngine;

namespace GN3.World
{
    /// <summary>도감 한 칸: 은월의 숲 몬스터 한 종류.</summary>
    public class BestiaryEntry
    {
        public string Region;      // 사는 지역(도감 탭) — 지금은 모두 은월의 숲
        public string Id;          // 저장 키(이름과 같다)
        public string Name;
        public string Title;       // 보스 칭호(없으면 null)
        public string Sheet;       // 전투 그림(Resources/Battle/Monsters) — 대기 시트 첫 장을 쓴다. 초상이 있으면 Portrait.
        public bool Portrait;      // true면 BattleMonsterSprite.PortraitFor(Ent)
        public string Habitat;
        public string Description;
        public string[] Traits;
        public LootItem[] Loot;    // 흔한 것 3 + 드문 것 1(LootCatalog 표)
        public string LootNote;    // 전리품 줄 덧말(앤트킹: 심장 확정)
    }

    /// <summary>
    /// 몬스터 도감: 은월의 숲 몬스터 6종과 지금까지 쓰러뜨린 수. 한 마리라도 쓰러뜨리면 그 칸의 정보가 열린다(BestiaryPanel).
    /// 퀘스트 전투(승패 상관없이)와 이동 중 습격에서 쓰러진 적을 센다. QuestClearHistory와 같은 패턴(static + 저장/불러오기).
    /// </summary>
    public static class Bestiary
    {
        public const string Forest = "은월의 숲";

        public static readonly BestiaryEntry Goblin = new BestiaryEntry
        {
            Region = Forest, Id = "고블린", Name = "고블린", Sheet = "goblin_idle", Habitat = "은월의 숲 Lv1~",
            Description = "숲 가장자리에 무리 지어 사는 작은 약탈자. 혼자서는 약하지만 늘 떼로 몰려온다.",
            Traits = new[] { "무리 지어 싸운다", "녹슨 검으로 근접 공격" },
            Loot = LootCatalog.TableFor("고블린"),
        };

        public static readonly BestiaryEntry GoblinArcher = new BestiaryEntry
        {
            Region = Forest, Id = "고블린 궁수", Name = "고블린 궁수", Sheet = "goblin_archer_idle", Habitat = "은월의 숲 Lv1~",
            Description = "고블린 무리 셋 중 하나꼴로 섞여 있는 활잡이. 무리 뒤에 숨어 화살을 날린다.",
            Traits = new[] { "뒷줄에서 멀리 쏜다", "가까이 붙으면 약하다" },
            Loot = LootCatalog.TableFor("고블린 궁수"),
        };

        public static readonly BestiaryEntry GoblinRider = new BestiaryEntry
        {
            Region = Forest, Id = "고블린 늑대기수", Name = "고블린 늑대기수", Sheet = "goblin_rider_idle", Habitat = "은월의 숲 Lv1~ · 이동 중 습격",
            Description = "늑대를 길들여 올라탄 고블린. 숲길을 지나는 행상과 원정대를 노린다.",
            Traits = new[] { "아주 빠르게 달려든다", "몸은 조금 덜 단단하다", "원정 길에 습격해 온다" },
            Loot = LootCatalog.TableFor("고블린 늑대기수"),
        };

        public static readonly BestiaryEntry Wolf = new BestiaryEntry
        {
            Region = Forest, Id = "야생늑대", Name = "야생늑대", Sheet = "wolf_idle", Habitat = "은월의 숲 Lv2~",
            Description = "요즘 들어 유난히 사나워진 숲의 늑대. 무리로 사냥하며 사냥개도 물어 죽인다.",
            Traits = new[] { "무리로 사냥한다", "빠르고 사납게 문다" },
            Loot = LootCatalog.TableFor("야생늑대"),
        };

        public static readonly BestiaryEntry Mushroom = new BestiaryEntry
        {
            Region = Forest, Id = "독버섯", Name = "독버섯", Sheet = "mushroom_idle", Habitat = "은월의 숲 Lv2~",
            Description = "걸어 다니며 독 포자를 뿜는 버섯. 약초 길을 막고 마을 우물까지 독기를 퍼뜨린다.",
            Traits = new[] { "뒷줄에서 포자를 쏜다", "맞으면 중독된다", "몸은 약하다" },
            Loot = LootCatalog.TableFor("독버섯"),
        };

        public static readonly BestiaryEntry EntKing = new BestiaryEntry
        {
            Region = Forest, Id = "앤트킹", Name = "앤트킹", Title = "은월의 숲의 주인", Sheet = "ent_idle", Portrait = true,
            Habitat = "은월의 숲 깊은 곳 · Lv5",
            Description = "숲 깊은 곳에 뿌리내린 고대의 나무 왕. 깨어나면 숲 전체가 마을을 향해 걸어 내려온다.",
            Traits = new[] { "뿌리박혀 움직이지 않는다", "세 번째 공격마다 뿌리 내려찍기(예고 원을 피하라)",
                             "망치 강타에 맞으면 잠깐 기절", "포효로 앞쪽을 기절시킨다 — 뒤는 안전", "6명이 두 분대로 맞서야 한다" },
            Loot = LootCatalog.TableFor("앤트킹"),
            LootNote = "엔트킹의 심장은 쓰러뜨리면 반드시 얻는다",
        };

        /// <summary>도감 순서.</summary>
        public static readonly BestiaryEntry[] All = { Goblin, GoblinArcher, GoblinRider, Wolf, Mushroom, EntKing };

        /// <summary>도감 탭이 될 지역(All 순서대로, 중복 없이). 새 지역 몬스터를 All에 더하면 탭이 늘어난다.</summary>
        public static string[] Regions => All.Select(e => e.Region).Distinct().ToArray();
        public static BestiaryEntry[] EntriesIn(string region) => All.Where(e => e.Region == region).ToArray();
        public static int DiscoveredIn(string region) => All.Count(e => e.Region == region && IsDiscovered(e));

        private static readonly Dictionary<string, int> _kills = new Dictionary<string, int>();
        private static readonly Dictionary<string, Combat.MonsterRank> _best = new Dictionary<string, Combat.MonsterRank>(); // 칸마다 쓰러뜨린 가장 높은 개체 등급

        /// <summary>도감 공통 한 줄(개체값 설명).</summary>
        public const string IndividualNote = "같은 몬스터라도 개체마다 세기가 다르다 — 강한·우두머리 개체는 더 세고 전리품을 잘 떨어뜨린다.";

        public static Combat.MonsterRank BestRank(BestiaryEntry entry) =>
            _best.TryGetValue(entry.Id, out var rank) ? rank : Combat.MonsterRank.Normal;

        public static event Action OnChanged;

        public static int Kills(BestiaryEntry entry) => _kills.TryGetValue(entry.Id, out int n) ? n : 0;
        public static bool IsDiscovered(BestiaryEntry entry) => Kills(entry) > 0;
        public static int DiscoveredCount => All.Count(IsDiscovered);

        /// <summary>적 이름 → 도감 칸. 판별 순서는 전리품 표(LootCatalog.TableFor)·전투 그림과 같다.
        /// 자동 전투엔 고블린 근접·궁수 구분이 없어 전리품처럼 셋 중 하나를 궁수로 친다.</summary>
        public static BestiaryEntry KindOf(string enemyName, System.Random rng)
        {
            string name = enemyName ?? "";
            if (name.Contains("늑대기수")) return GoblinRider;
            if (RegionInfo.IsEnt(name)) return EntKing;
            if (name.Contains("늑대") || name.Contains("울프")) return Wolf;
            if (name.Contains("버섯")) return Mushroom;
            if (name.Contains("궁수")) return GoblinArcher;
            return rng != null && rng.NextDouble() < LootCatalog.AutoArcherChance ? GoblinArcher : Goblin;
        }

        private static readonly System.Random Rng = new System.Random();

        /// <summary>쓰러뜨린 적들을 센다(퀘스트 전투 결과·이동 중 습격). archerSplit: 이름에 궁수 구분이 없는 자동 전투면 true.</summary>
        public static void RecordKills(IEnumerable<string> killedEnemyNames, bool archerSplit)
        {
            bool any = false;
            foreach (var name in killedEnemyNames)
            {
                bool hasArcherName = name != null && name.Contains("궁수");
                var entry = KindOf(name, archerSplit && !hasArcherName ? Rng : null);
                var rank = Combat.MonsterIV.RankOf(name);
                if (Kills(entry) == 0 || rank > BestRank(entry)) _best[entry.Id] = rank;
                _kills[entry.Id] = Kills(entry) + 1;
                any = true;
            }
            if (any) OnChanged?.Invoke();
        }

        // ---------- 저장 ----------

        public static List<Save.LootSave> SaveEntries() =>
            _kills.Where(kv => kv.Value > 0).Select(kv => new Save.LootSave { name = kv.Key, count = kv.Value }).ToList();

        /// <summary>칸마다 가장 높은 개체 등급(count = MonsterRank 번호).</summary>
        public static List<Save.LootSave> SaveBest() =>
            _best.Select(kv => new Save.LootSave { name = kv.Key, count = (int)kv.Value }).ToList();

        /// <summary>best가 없으면(예전 저장) 잡은 칸은 보통으로 친다.</summary>
        public static void Restore(IEnumerable<Save.LootSave> entries, IEnumerable<Save.LootSave> best = null)
        {
            _kills.Clear();
            _best.Clear();
            if (entries != null)
                foreach (var e in entries)
                    if (e.count > 0 && All.Any(a => a.Id == e.name)) _kills[e.name] = e.count;
            if (best != null)
                foreach (var e in best)
                    if (_kills.ContainsKey(e.name) && Enum.IsDefined(typeof(Combat.MonsterRank), e.count)) _best[e.name] = (Combat.MonsterRank)e.count;
            OnChanged?.Invoke();
        }

        // Domain Reload가 꺼져 있어 Play마다 비운다(QuestClearHistory와 같은 사정).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _kills.Clear();
            _best.Clear();
            OnChanged = null;
        }
    }
}
