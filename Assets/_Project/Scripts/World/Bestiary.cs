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
        public string FallbackSheet; // Sheet가 아직 없을 때 대신 쓸 시트(고블린 목수 → 검 고블린)
        public bool Portrait;      // true면 BattleMonsterSprite.PortraitFor(Ent)
        public string Habitat;
        public string Description;
        public string[] Traits;
        public LootItem[] Loot;    // 흔한 것 3 + 드문 것 1(LootCatalog 표)
        public string LootNote;    // 전리품 줄 덧말(앤트킹: 심장 확정)
    }

    /// <summary>
    /// 몬스터 도감: 은월의 숲 몬스터 9종과 지금까지 쓰러뜨린 수. 한 마리라도 쓰러뜨리면 그 칸의 정보가 열린다(BestiaryPanel).
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

        public static readonly BestiaryEntry GoblinCarpenter = new BestiaryEntry
        {
            Region = Forest, Id = "고블린 목수", Name = "고블린 목수", Sheet = "goblin_carpenter_idle",
            Habitat = "은월의 숲 Lv1~",
            Description = "고블린 무리의 울타리·덫을 만드는 일꾼. 늘 목재와 연장을 짊어지고 다닌다.",
            Traits = new[] { "망치로 내려찍는다", "목재·쇠못·밧줄·밀랍을 꼭 떨어뜨린다" },
            Loot = LootCatalog.TableFor("고블린 목수"),
        };

        public static readonly BestiaryEntry GoblinShield = new BestiaryEntry
        {
            Region = Forest, Id = "고블린 방패병", Name = "고블린 방패병", Sheet = "goblin_shield_idle", Habitat = "은월의 숲 Lv1~",
            Description = "녹슨 투구와 둥근 나무 방패로 무장한 고블린. 무리 맨 앞에 서서 동료들의 방패가 되어 준다.",
            Traits = new[] { "무리 맨 앞에 선다", "공격을 방패로 막아 피해를 줄인다", "방패로 밀어친다(힘은 약하다)" },
            Loot = LootCatalog.TableFor("고블린 방패병"),
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

        public static readonly BestiaryEntry StumpEnt = new BestiaryEntry
        {
            Region = Forest, Id = "그루터기 엔트", Name = "그루터기 엔트", Sheet = "stump_ent_idle", Habitat = "은월의 숲 Lv2~",
            Description = "베어 낸 나무 그루터기에 숲의 기운이 깃들어 일어난 것. 느리지만 이끼 낀 몸이 바위처럼 단단하다.",
            Traits = new[] { "느리게 다가온다", "아주 단단하다", "긴 팔을 휘둘러 친다" },
            Loot = LootCatalog.TableFor("그루터기 엔트"),
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
        public static readonly BestiaryEntry[] All = { Goblin, GoblinArcher, GoblinCarpenter, GoblinShield, GoblinRider, Wolf, Mushroom, StumpEnt, EntKing };

        /// <summary>도감 탭이 될 지역(All 순서대로, 중복 없이). 새 지역 몬스터를 All에 더하면 탭이 늘어난다.</summary>
        public static string[] Regions => All.Select(e => e.Region).Distinct().ToArray();
        public static BestiaryEntry[] EntriesIn(string region) => All.Where(e => e.Region == region).ToArray();
        public static int DiscoveredIn(string region) => All.Count(e => e.Region == region && IsDiscovered(e));

        private static readonly Dictionary<string, int> _kills = new Dictionary<string, int>();
        private static readonly Dictionary<string, Combat.MonsterRank> _best = new Dictionary<string, Combat.MonsterRank>(); // 칸마다 쓰러뜨린 가장 높은 개체 등급

        public static Combat.MonsterRank BestRank(BestiaryEntry entry) =>
            _best.TryGetValue(entry.Id, out var rank) ? rank : Combat.MonsterRank.Normal;

        public static event Action OnChanged;

        public static int Kills(BestiaryEntry entry) => _kills.TryGetValue(entry.Id, out int n) ? n : 0;
        public static bool IsDiscovered(BestiaryEntry entry) => Kills(entry) > 0;
        public static int DiscoveredCount => All.Count(IsDiscovered);

        /// <summary>적 이름 → 도감 칸. 판별 순서는 전리품 표(LootCatalog.TableFor)·전투 그림과 같다.
        /// 자동 전투엔 고블린 근접·궁수·목수·방패병 구분이 없어 전리품처럼 나눠 친다(LootCatalog.SplitGoblin).</summary>
        public static BestiaryEntry KindOf(string enemyName, System.Random rng)
        {
            string name = enemyName ?? "";
            if (name.Contains("늑대기수")) return GoblinRider;
            if (RegionInfo.IsStump(name)) return StumpEnt; // 이름에 "엔트"가 있어도 보스가 아니다
            if (RegionInfo.IsEnt(name)) return EntKing;
            if (name.Contains("늑대") || name.Contains("울프")) return Wolf;
            if (name.Contains("버섯")) return Mushroom;
            if (name.Contains("목수")) return GoblinCarpenter;
            if (name.Contains("방패병")) return GoblinShield;
            if (name.Contains("궁수")) return GoblinArcher;
            if (rng == null) return Goblin;
            var (archer, carpenter, shield) = LootCatalog.SplitGoblin(name, rng);
            return archer ? GoblinArcher : carpenter ? GoblinCarpenter : shield ? GoblinShield : Goblin;
        }

        private static readonly System.Random Rng = new System.Random();

        /// <summary>쓰러뜨린 적들을 센다(퀘스트 전투 결과·이동 중 습격). archerSplit: 이름에 궁수 구분이 없는 자동 전투면 true.</summary>
        public static void RecordKills(IEnumerable<string> killedEnemyNames, bool archerSplit)
        {
            bool any = false;
            foreach (var name in killedEnemyNames)
            {
                var entry = KindOf(name, archerSplit ? Rng : null); // 이름에 궁수·목수가 있으면 KindOf가 굴리지 않는다
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
