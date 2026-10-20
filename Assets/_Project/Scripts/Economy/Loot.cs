using System;
using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>몬스터 전리품 하나. 팔면 Price골드(LootBag). 아이콘은 Resources/Loot/loot_NN(tools/bake_loot_icons.py가 굽는다).</summary>
    public class LootItem
    {
        public string Name { get; }
        public int Price { get; }
        public int IconIndex { get; }
        /// <summary>드문 전리품(가끔만 나오고 비싸다).</summary>
        public bool Rare { get; }

        public string IconPath => $"Loot/loot_{IconIndex:00}";
        public Sprite Icon
        {
            get
            {
                if (_icon != null) return _icon;
                _icon = Resources.Load<Sprite>(IconPath);
                if (_icon == null)
                {
                    var texture = Resources.Load<Texture2D>(IconPath);
                    if (texture != null) _icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
                return _icon;
            }
        }
        private Sprite _icon;

        public LootItem(string name, int price, int iconIndex, bool rare = false)
        {
            Name = name;
            Price = price;
            IconIndex = iconIndex;
            Rare = rare;
        }
    }

    /// <summary>
    /// 전리품 목록과 몬스터 종류별 떨어뜨리는 표. 표마다 흔한 것 3개 + 드문 것 1개(아이콘 시트 한 줄과 같은 순서).
    /// 쓰러뜨린 적 하나마다 DropChance로 1개, 그중 RareChance는 드문 것(개체 등급이 높으면 더 잘 — ChancesFor). 앤트킹은 엔트 전리품 여럿 + 엔트킹의 심장 확정.
    /// 그와 따로 한 마리마다 다섯 빛깔 마정석을 한 번 굴린다(푸른 20% ~ 붉은 1%, 개체 등급과 상관없이 — RollManaStone).
    /// </summary>
    public static class LootCatalog
    {
        public const float DropChance = 0.65f;
        public const float RareChance = 0.1f;
        public const float AutoArcherChance = 1f / 3f; // 자동 전투엔 고블린 근접·궁수 구분이 없어 셋 중 하나를 궁수로 친다(직접 전투와 같은 비율)

        // 몬스터 전리품 아틀라스(아이콘 0~15)
        public static readonly LootItem RiderDagger = new LootItem("낡은 단검", 10, 0);
        public static readonly LootItem CoinPouch = new LootItem("동전 주머니", 15, 1);
        public static readonly LootItem RiderArrows = new LootItem("화살 묶음", 8, 2);
        public static readonly LootItem WolfBridle = new LootItem("늑대 고삐", 55, 3, rare: true);
        public static readonly LootItem Fang = new LootItem("송곳니", 10, 4);
        public static readonly LootItem Claw = new LootItem("발톱", 10, 5);
        public static readonly LootItem Pelt = new LootItem("가죽", 18, 6);
        public static readonly LootItem ManaFang = new LootItem("마력이 깃든 송곳니", 70, 7, rare: true);
        public static readonly LootItem MushroomCap = new LootItem("독버섯 갓", 8, 8);
        public static readonly LootItem SporeSac = new LootItem("포자 주머니", 12, 9);
        public static readonly LootItem Venom = new LootItem("독액", 16, 10);
        public static readonly LootItem MyceliumCrystal = new LootItem("균사 결정", 75, 11, rare: true);
        public static readonly LootItem LifeSeed = new LootItem("생명의 씨앗", 40, 12);
        public static readonly LootItem MossyBark = new LootItem("이끼 낀 수피", 30, 13);
        public static readonly LootItem RootThorn = new LootItem("뿌리 가시", 35, 14);
        public static readonly LootItem EntKingHeart = new LootItem("엔트킹의 심장", 300, 15, rare: true);
        // 고블린 전사·궁수 시트(아이콘 16~23)
        public static readonly LootItem OldSword = new LootItem("낡은 검", 12, 16);
        public static readonly LootItem WoodenShield = new LootItem("나무 방패", 14, 17);
        public static readonly LootItem LeatherBracer = new LootItem("가죽 팔보호대", 10, 18);
        public static readonly LootItem RedSash = new LootItem("붉은 허리띠", 50, 19, rare: true);
        public static readonly LootItem ShortBow = new LootItem("짧은 활", 12, 20);
        public static readonly LootItem ArcherArrows = new LootItem("궁수의 화살", 8, 21); // 늑대기수의 "화살 묶음"과 따로 쌓이게
        public static readonly LootItem Quiver = new LootItem("가죽 화살통", 14, 22);
        public static readonly LootItem ShootingGlove = new LootItem("사격용 장갑", 50, 23, rare: true);

        // 다섯 빛깔 마정석(아이콘 24~28 — tools/bake_mana_stones.py). 싼 것부터 파·보·초·금·빨.
        // 이름은 "고블린의 푸른 마정석"처럼 몬스터마다 따로, 값 = 색 기본값 × 몬스터 배율(앤트킹 ×3).
        public static readonly string[] ManaColorNames = { "푸른", "보랏빛", "초록빛", "황금빛", "붉은" };
        public static readonly int[] ManaBasePrices = { 20, 45, 90, 180, 400 };
        public static readonly float[] ManaChances = { 0.20f, 0.10f, 0.05f, 0.025f, 0.01f }; // 한 마리당(합 38.5%)
        public const int ManaIconStart = 24;

        private static LootItem[] ManaSet(string monster, float priceScale) =>
            Enumerable.Range(0, ManaColorNames.Length)
                .Select(c => new LootItem($"{monster}의 {ManaColorNames[c]} 마정석", Mathf.RoundToInt(ManaBasePrices[c] * priceScale), ManaIconStart + c, rare: c >= 3))
                .ToArray();

        public static readonly LootItem[] GoblinMana = ManaSet("고블린", 1f);
        public static readonly LootItem[] ArcherMana = ManaSet("고블린 궁수", 1f);
        public static readonly LootItem[] RiderMana = ManaSet("고블린 늑대기수", 1.2f);
        public static readonly LootItem[] WolfMana = ManaSet("야생늑대", 1.1f);
        public static readonly LootItem[] MushroomMana = ManaSet("독버섯", 1.1f);
        public static readonly LootItem[] EntMana = ManaSet("앤트킹", 3f);

        public static readonly LootItem[] All = new[]
        {
            RiderDagger, CoinPouch, RiderArrows, WolfBridle, Fang, Claw, Pelt, ManaFang,
            MushroomCap, SporeSac, Venom, MyceliumCrystal, LifeSeed, MossyBark, RootThorn, EntKingHeart,
            OldSword, WoodenShield, LeatherBracer, RedSash, ShortBow, ArcherArrows, Quiver, ShootingGlove,
        }.Concat(GoblinMana).Concat(ArcherMana).Concat(RiderMana).Concat(WolfMana).Concat(MushroomMana).Concat(EntMana).ToArray();

        private static readonly LootItem[] Rider = { RiderDagger, CoinPouch, RiderArrows, WolfBridle };
        private static readonly LootItem[] Wolf = { Fang, Claw, Pelt, ManaFang };
        private static readonly LootItem[] Mushroom = { MushroomCap, SporeSac, Venom, MyceliumCrystal };
        private static readonly LootItem[] Ent = { LifeSeed, MossyBark, RootThorn, EntKingHeart };
        private static readonly LootItem[] Warrior = { OldSword, WoodenShield, LeatherBracer, RedSash };
        private static readonly LootItem[] Archer = { ShortBow, ArcherArrows, Quiver, ShootingGlove };

        public static LootItem Find(string name) => All.FirstOrDefault(i => i.Name == name);

        /// <summary>적 이름 → 전리품 표(흔한 것 3 + 드문 것 1). 이름 판별은 전투 그림(BattleMonsterSprite.KindFor)과 같은 순서.
        /// archerGoblin: 자동 전투에서 이 고블린을 궁수로 칠지(직접 전투는 이름에 "궁수"가 붙어 온다).</summary>
        public static LootItem[] TableFor(string enemyName, bool archerGoblin = false)
        {
            string name = enemyName ?? "";
            if (name.Contains("늑대기수")) return Rider; // 이름에 "늑대"도 있어 늑대보다 먼저
            if (RegionInfo.IsEnt(name)) return Ent;
            if (name.Contains("늑대") || name.Contains("울프")) return Wolf;
            if (name.Contains("버섯")) return Mushroom;
            if (name.Contains("궁수") || archerGoblin) return Archer;
            return Warrior; // 고블린(예전 저장의 고블린 족장 포함)
        }

        /// <summary>마정석이면 색 번호(0 푸른 ~ 4 붉은), 아니면 -1.</summary>
        public static int ManaColorOf(LootItem item)
        {
            if (item == null || !item.Name.EndsWith("마정석")) return -1;
            int c = item.IconIndex - ManaIconStart;
            return c >= 0 && c < ManaColorNames.Length ? c : -1;
        }

        /// <summary>적 이름 → 그 몬스터의 마정석 5개(파·보·초·금·빨). 판별은 TableFor와 같다.</summary>
        public static LootItem[] ManaStonesFor(string enemyName, bool archerGoblin = false) => ManaFor(TableFor(enemyName, archerGoblin));

        private static LootItem[] ManaFor(LootItem[] table) =>
            table == Rider ? RiderMana : table == Ent ? EntMana : table == Wolf ? WolfMana
            : table == Mushroom ? MushroomMana : table == Archer ? ArcherMana : GoblinMana;

        /// <summary>마정석 한 번 굴림: 붉은 1% · 황금빛 2.5% · 초록빛 5% · 보랏빛 10% · 푸른 20%, 나머지는 없음(null).</summary>
        public static LootItem RollManaStone(LootItem[] set, System.Random rng)
        {
            double roll = rng.NextDouble();
            for (int c = ManaChances.Length - 1; c >= 0; c--)
            {
                roll -= ManaChances[c];
                if (roll < 0) return set[c];
            }
            return null;
        }

        /// <summary>쓰러뜨린 적들에게서 나온 전리품(같은 건 묶어 개수로).
        /// archerSplit: 자동 전투처럼 이름에 궁수 구분이 없으면 true(고블린 셋 중 하나를 궁수로). 직접 전투는 이름에 "궁수"가 붙어 온다.</summary>
        public static List<(LootItem item, int count)> Roll(IEnumerable<string> killedEnemyNames, System.Random rng, bool archerSplit = true)
        {
            var drops = new List<LootItem>();
            foreach (var name in killedEnemyNames)
            {
                bool autoArcher = archerSplit && !name.Contains("궁수") && rng.NextDouble() < AutoArcherChance;
                var table = TableFor(name, autoArcher);
                var mana = RollManaStone(ManaFor(table), rng); // 전리품과 따로, 한 마리마다 한 번
                if (mana != null) drops.Add(mana);
                if (table == Ent)
                {
                    // 숲의 주인: 엔트 전리품 2~3개 + 심장 확정
                    int n = rng.Next(2, 4);
                    for (int i = 0; i < n; i++) drops.Add(table[rng.Next(3)]);
                    drops.Add(EntKingHeart);
                    continue;
                }
                // 개체 등급(이름 앞말)이 높을수록 잘 떨어뜨리고 드문 것도 잘 나온다
                var (dropChance, rareChance) = ChancesFor(GN3.Combat.MonsterIV.RankOf(name));
                if (rng.NextDouble() >= dropChance) continue;
                drops.Add(rng.NextDouble() < rareChance ? table[3] : table[rng.Next(3)]);
            }
            return drops.GroupBy(d => d).Select(g => (g.Key, g.Count())).OrderBy(d => Array.IndexOf(All, d.Item1)).ToList();
        }

        /// <summary>개체 등급별 (전리품이 나올 확률, 그중 드문 것일 확률). 보통은 DropChance·RareChance.</summary>
        public static (float drop, float rare) ChancesFor(GN3.Combat.MonsterRank rank) => rank switch
        {
            GN3.Combat.MonsterRank.Weak => (0.5f, 0.05f),
            GN3.Combat.MonsterRank.Strong => (0.85f, 0.2f),
            GN3.Combat.MonsterRank.Alpha => (1f, 0.4f),
            _ => (DropChance, RareChance),
        };

        /// <summary>"송곳니 ×2, 가죽 ×1" (없으면 빈 문자열).</summary>
        public static string Describe(IEnumerable<(LootItem item, int count)> loot) =>
            string.Join(", ", loot.Select(l => $"{l.item.Name} ×{l.count}"));
    }

    /// <summary>
    /// 전리품 가방: 길드 창고와 따로, 칸 제한 없이 같은 것은 개수로 쌓는다. 길드 창고 화면에서 팔아 골드로 바꾼다.
    /// QuestClearHistory와 같은 패턴(static + 저장/불러오기).
    /// </summary>
    public static class LootBag
    {
        private static readonly Dictionary<LootItem, int> _counts = new Dictionary<LootItem, int>();

        public static event Action OnChanged;

        /// <summary>카탈로그 순서대로 (전리품, 개수).</summary>
        public static IEnumerable<(LootItem item, int count)> Entries =>
            LootCatalog.All.Where(i => Count(i) > 0).Select(i => (i, Count(i)));

        public static int Count(LootItem item) => _counts.TryGetValue(item, out int n) ? n : 0;
        public static int KindCount => _counts.Count(kv => kv.Value > 0);

        /// <summary>이 색 마정석이 몬스터 상관없이 모두 몇 개(마정 무기 제작 재료 — Crafting).</summary>
        public static int CountManaColor(int color) =>
            _counts.Where(kv => LootCatalog.ManaColorOf(kv.Key) == color).Sum(kv => kv.Value);

        /// <summary>이 색 마정석 n개를 값이 싼 것부터 쓴다. 모자라면 아무것도 쓰지 않고 false.</summary>
        public static bool ConsumeManaColor(int color, int n)
        {
            if (n <= 0) return true;
            if (CountManaColor(color) < n) return false;
            foreach (var item in _counts.Keys.Where(i => LootCatalog.ManaColorOf(i) == color).OrderBy(i => i.Price).ToList())
            {
                int take = Math.Min(n, _counts[item]);
                if (_counts[item] == take) _counts.Remove(item);
                else _counts[item] -= take;
                n -= take;
                if (n == 0) break;
            }
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>전리품 n개를 쓴다(제작 재료). 모자라면 아무것도 쓰지 않고 false.</summary>
        public static bool Consume(LootItem item, int n)
        {
            if (n <= 0) return true;
            if (Count(item) < n) return false;
            if (Count(item) == n) _counts.Remove(item);
            else _counts[item] -= n;
            OnChanged?.Invoke();
            return true;
        }
        public static int TotalValue => _counts.Sum(kv => kv.Key.Price * kv.Value);

        public static void Add(IEnumerable<(LootItem item, int count)> loot)
        {
            bool any = false;
            foreach (var (item, count) in loot)
            {
                if (count <= 0) continue;
                _counts[item] = Count(item) + count;
                any = true;
            }
            if (any) OnChanged?.Invoke();
        }

        /// <summary>n개 판다(가진 만큼만). 받은 골드.</summary>
        public static int Sell(LootItem item, int n)
        {
            n = Math.Min(n, Count(item));
            if (n <= 0) return 0;
            if (Count(item) == n) _counts.Remove(item);
            else _counts[item] -= n;
            int gold = item.Price * n;
            Wallet.Add(gold);
            OnChanged?.Invoke();
            return gold;
        }

        public static int SellAll()
        {
            int gold = TotalValue;
            if (gold <= 0) return 0;
            _counts.Clear();
            Wallet.Add(gold);
            OnChanged?.Invoke();
            return gold;
        }

        // ---------- 저장 ----------

        public static List<Save.LootSave> SaveEntries() =>
            Entries.Select(e => new Save.LootSave { name = e.item.Name, count = e.count }).ToList();

        /// <summary>저장 파일에서 불러올 때. 없어진 이름은 건너뛴다.</summary>
        public static void Restore(IEnumerable<Save.LootSave> entries)
        {
            _counts.Clear();
            if (entries != null)
                foreach (var e in entries)
                {
                    var item = LootCatalog.Find(e.name);
                    if (item != null && e.count > 0) _counts[item] = Count(item) + e.count;
                }
            OnChanged?.Invoke();
        }

        // Domain Reload가 꺼져 있어 Play마다 비운다(QuestClearHistory와 같은 사정).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _counts.Clear();
            OnChanged = null;
        }
    }
}
