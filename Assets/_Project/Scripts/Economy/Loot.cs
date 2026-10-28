using System;
using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>전리품 등급(값으로 정해진다 — LootItem.Grade). 낮은 것부터.</summary>
    public enum LootGrade { Lowest, Low, Mid, High, Highest }

    public static class LootGrades
    {
        // 등급마다 값 구간의 아래 끝(하급 15G~, 중급 40G~, 상급 150G~, 최상급 500G~, 그 아래는 최하급)
        private static readonly int[] MinPrices = { 0, 15, 40, 150, 500 };
        private static readonly string[] Names = { "최하급", "하급", "중급", "상급", "최상급" };
        private static readonly string[] Hexes = { "#9a9a9a", "#e8dcc8", "#7ec46a", "#6aa8f0", "#f0b040" };

        public static LootGrade ForPrice(int price)
        {
            for (int g = MinPrices.Length - 1; g > 0; g--)
                if (price >= MinPrices[g]) return (LootGrade)g;
            return LootGrade.Lowest;
        }

        public static string NameOf(LootGrade grade) => Names[(int)grade];
        public static string HexOf(LootGrade grade) => Hexes[(int)grade];

        public static Color ColorOf(LootGrade grade) =>
            ColorUtility.TryParseHtmlString(Hexes[(int)grade], out var color) ? color : Color.white;

        /// <summary>높은 등급부터(도감 줄 순서).</summary>
        public static readonly LootGrade[] HighestFirst = { LootGrade.Highest, LootGrade.High, LootGrade.Mid, LootGrade.Low, LootGrade.Lowest };
    }

    /// <summary>몬스터 전리품 하나. 팔면 Price골드(LootBag). 아이콘은 Resources/Loot/loot_NN(tools/bake_loot_icons.py가 굽는다).</summary>
    public class LootItem
    {
        public string Name { get; }
        public int Price { get; }
        public int IconIndex { get; }
        /// <summary>드문 전리품(가끔만 나오고 비싸다).</summary>
        public bool Rare { get; }
        /// <summary>건설 재료(BuildMaterials). 전리품 "모두 팔기"에 섞이지 않는다.</summary>
        public bool Material { get; }
        /// <summary>등급(값으로 자동 — LootGrades). 건설 재료는 등급이 없다(HasGrade).</summary>
        public LootGrade Grade => LootGrades.ForPrice(Price);
        public bool HasGrade => !Material;
        /// <summary>"[상급]"처럼 등급 이름(건설 재료는 빈 문자열).</summary>
        public string GradeTag => HasGrade ? $"[{LootGrades.NameOf(Grade)}]" : "";

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

        public LootItem(string name, int price, int iconIndex, bool rare = false, bool material = false)
        {
            Name = name;
            Price = price;
            IconIndex = iconIndex;
            Rare = rare;
            Material = material;
        }
    }

    /// <summary>
    /// 전리품 목록과 몬스터 종류별 떨어뜨리는 표. 표마다 흔한 것 3개 + 드문 것 1개(아이콘 시트 한 줄과 같은 순서).
    /// 쓰러뜨린 적 하나마다 DropChance로 1개, 그중 RareChance는 드문 것(개체 등급이 높으면 더 잘 — ChancesFor). 앤트킹은 엔트 전리품 여럿 + 엔트킹의 심장 확정.
    /// 그와 따로 한 마리마다 다섯 빛깔 마정석을 한 번 굴린다(푸른 20% ~ 붉은 1%, 개체 등급과 상관없이 — RollManaStone).
    /// 고블린 종류는 또 따로 고블린 보물 7종을 한 번 굴리고(합 1% — RollTreasure), 잡동사니 36종도 한 번 굴린다(40% — RollJunk).
    /// 건설 재료도 한 마리마다 MaterialChance로 몬스터별 재료표에서 하나(고블린 목수는 하나 확정 + 가끔 하나 더, 앤트킹은 통나무·말뚝 여럿 확정).
    /// </summary>
    public static class LootCatalog
    {
        public const float DropChance = 0.65f;
        public const float RareChance = 0.1f;
        public const float AutoArcherChance = 1f / 3f; // 자동 전투엔 고블린 근접·궁수 구분이 없어 셋 중 하나를 궁수로 친다(직접 전투와 같은 비율)
        public const float AutoCarpenterChance = 0.15f; // 자동 전투: 궁수가 아닌 고블린 중 이만큼을 목수로 친다(직접 전투는 4마리 이상 무리에 하나)
        public const float AutoShieldChance = 0.15f; // 자동 전투: 그다음 이만큼을 방패병으로 친다(직접 전투는 3마리 이상 무리의 맨 앞)

        // 몬스터 전리품 아틀라스(아이콘 0~15)
        public static readonly LootItem RiderDagger = new LootItem("낡은 단검", 10, 0);
        public static readonly LootItem CoinPouch = new LootItem("동전 주머니", 20, 1);
        public static readonly LootItem RiderArrows = new LootItem("늑대기수 화살", 8, 2); // 2026-10-26 "화살 묶음"에서 이름만 바꿈(궁수의 화살과 헷갈려서, 예전 이름은 Find 별칭)
        public static readonly LootItem WolfBridle = new LootItem("늑대 고삐", 55, 3, rare: true);
        public static readonly LootItem Fang = new LootItem("송곳니", 10, 4);
        public static readonly LootItem Claw = new LootItem("발톱", 10, 5);
        public static readonly LootItem Pelt = new LootItem("늑대 가죽", 18, 6); // 2026-10-26 "가죽"에서 이름만 바꿈(건설 재료 무두질한 가죽과 헷갈려서)
        public static readonly LootItem ManaFang = new LootItem("마력이 깃든 송곳니", 70, 7, rare: true);
        public static readonly LootItem MushroomCap = new LootItem("독버섯 갓", 8, 8);
        public static readonly LootItem SporeSac = new LootItem("포자 주머니", 12, 9);
        public static readonly LootItem Venom = new LootItem("독액", 16, 10);
        public static readonly LootItem MyceliumCrystal = new LootItem("균사 결정", 75, 11, rare: true);
        public static readonly LootItem LifeSeed = new LootItem("생명의 씨앗", 38, 12);
        public static readonly LootItem MossyBark = new LootItem("이끼 낀 수피", 30, 13);
        public static readonly LootItem RootThorn = new LootItem("뿌리 가시", 35, 14);
        public static readonly LootItem EntKingHeart = new LootItem("엔트킹의 심장", 300, 15, rare: true);
        // 고블린 전사·궁수 시트(아이콘 16~23)
        public static readonly LootItem OldSword = new LootItem("낡은 검", 12, 16);
        public static readonly LootItem WoodenShield = new LootItem("나무 방패", 20, 17);
        public static readonly LootItem LeatherBracer = new LootItem("가죽 팔보호대", 10, 18);
        public static readonly LootItem RedSash = new LootItem("붉은 허리띠", 50, 19, rare: true);
        public static readonly LootItem ShortBow = new LootItem("짧은 활", 12, 20);
        public static readonly LootItem ArcherArrows = new LootItem("궁수의 화살", 8, 21); // 늑대기수 화살과 따로 쌓이게
        public static readonly LootItem Quiver = new LootItem("가죽 화살통", 20, 22);
        public static readonly LootItem ShootingGlove = new LootItem("사격용 장갑", 50, 23, rare: true);
        // 2026-10-26 몬스터별 전용 전리품(목수·방패병·그루터기 엔트가 다른 몬스터 표를 빌려 쓰던 것을 나눔).
        // 아이콘 시트가 아직 없어 비슷한 기존 아이콘 번호를 임시로 쓴다(시트가 오면 84번부터 구워 바꾼다 — 41~47은 고블린 보물, 48~83은 고블린 잡동사니).
        public static readonly LootItem CarpenterHammer = new LootItem("목수의 망치", 20, 16);
        public static readonly LootItem ToolPouch = new LootItem("연장 주머니", 12, 1);
        public static readonly LootItem Plane = new LootItem("대패", 10, 17);
        public static readonly LootItem MasterChisel = new LootItem("명장의 끌", 55, 0, rare: true);
        public static readonly LootItem RustyHelm = new LootItem("녹슨 투구", 20, 18);
        public static readonly LootItem ShieldRim = new LootItem("방패 테두리", 10, 17);
        public static readonly LootItem Pauldron = new LootItem("어깨 보호대", 12, 18);
        public static readonly LootItem ShieldBoss = new LootItem("강철 방패 돌기", 55, 17, rare: true);
        public static readonly LootItem TreeRing = new LootItem("나이테 조각", 22, 13);
        public static readonly LootItem MossClump = new LootItem("이끼 덩이", 12, 13);
        public static readonly LootItem RootVine = new LootItem("뿌리 덩굴", 14, 14);
        public static readonly LootItem LivingKnot = new LootItem("살아있는 옹이", 70, 12, rare: true);

        // 다섯 빛깔 마정석(아이콘 24~28 — tools/bake_mana_stones.py). 싼 것부터 파·보·초·금·빨.
        // 이름은 "고블린의 푸른 마정석"처럼 몬스터마다 따로, 값 = 색 기본값 × 몬스터 배율(앤트킹 ×1.4 — 배율이 커도 등급 구간을 넘지 않게).
        public static readonly string[] ManaColorNames = { "푸른", "보랏빛", "초록빛", "황금빛", "붉은" };
        public static readonly int[] ManaBasePrices = { 10, 25, 60, 200, 600 }; // 색 = 등급(푸른 최하급 ~ 붉은 최상급 — LootGrades 값 구간 안)
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
        public static readonly LootItem[] EntMana = ManaSet("앤트킹", 1.4f);
        public static readonly LootItem[] StumpMana = ManaSet("그루터기 엔트", 1.2f);

        // 건설 재료(아이콘 29~40 — tools/bake_material_icons.py). 건물을 짓는 데 든다(BuildingCatalog).
        public const float MaterialChance = 0.3f;
        public const float CarpenterExtraMaterialChance = 0.5f; // 고블린 목수: 재료 1개 확정 + 이 확률로 하나 더
        public static readonly LootItem Cloth = new LootItem("천", 6, 29, material: true);
        public static readonly LootItem Log = new LootItem("통나무", 5, 30, material: true);
        public static readonly LootItem Rope = new LootItem("밧줄", 4, 31, material: true);
        public static readonly LootItem Leather = new LootItem("무두질한 가죽", 10, 32, material: true);
        public static readonly LootItem Nail = new LootItem("쇠못", 3, 33, material: true);
        public static readonly LootItem IronRing = new LootItem("쇠고리", 6, 34, material: true);
        public static readonly LootItem Stake = new LootItem("나무 말뚝", 2, 35, material: true);
        public static readonly LootItem Thread = new LootItem("실타래", 4, 36, material: true);
        public static readonly LootItem Needle = new LootItem("바늘", 5, 37, material: true);
        public static readonly LootItem Strap = new LootItem("가죽끈", 5, 38, material: true);
        public static readonly LootItem Wax = new LootItem("밀랍", 8, 39, material: true);
        public static readonly LootItem Dye = new LootItem("푸른 염료", 12, 40, material: true);

        public static readonly LootItem[] Materials = { Cloth, Log, Rope, Leather, Nail, IronRing, Stake, Thread, Needle, Strap, Wax, Dye };

        // 고블린 보물(아이콘 41~47 — tools/bake_treasure_icons.py): 고블린이 훔쳐 모은 값비싼 물건. 고블린 종류(전사·궁수·목수·방패병·늑대기수)
        // 모두가 전리품·마정석과 따로 한 마리마다 한 번 굴려 아주 드물게 떨어뜨린다(개체 등급과 상관없이 — RollTreasure). 팔기만 한다.
        public static readonly LootItem Emerald = new LootItem("에메랄드 원석", 450, 41, rare: true);
        public static readonly LootItem GoldTooth = new LootItem("금니", 300, 42, rare: true);
        public static readonly LootItem SilverChalice = new LootItem("은 성배", 400, 43, rare: true);
        public static readonly LootItem RubyBrooch = new LootItem("루비 브로치", 900, 44, rare: true);
        public static readonly LootItem OldGoldCoins = new LootItem("옛 왕국 금화", 350, 45, rare: true);
        public static readonly LootItem Pearl = new LootItem("진주", 800, 46, rare: true);
        public static readonly LootItem SapphirePendant = new LootItem("사파이어 펜던트", 1500, 47, rare: true);

        public static readonly LootItem[] GoblinTreasures = { Emerald, GoldTooth, SilverChalice, RubyBrooch, OldGoldCoins, Pearl, SapphirePendant };
        public static readonly float[] TreasureChances = { 0.0015f, 0.0015f, 0.0015f, 0.0015f, 0.0015f, 0.0015f, 0.001f }; // 한 마리당(합 1%)

        // 고블린 잡동사니(아이콘 48~83 — tools/bake_goblin_junk_icons.py, 시트 6장 × 6): 고블린이 들고 다니는 싸구려 물건.
        // 고블린 종류 모두가 한 마리마다 JunkChance로 하나(RollJunk). 대부분 최하급, 그보다 값진 것(하급 3·중급 3)은 덜 나온다.
        public const float JunkChance = 0.4f;
        private const int JunkLowWeight = 1, JunkLowestWeight = 3; // 하급 : 최하급 뽑기 가중치

        private static LootItem J(string name, int price, int icon) => new LootItem(name, price, icon);

        public static readonly LootItem[] GoblinJunk =
        {
            // 고철
            J("굽은 쇠못", 4, 48), J("녹슨 버클", 6, 49), J("끊어진 쇠사슬", 8, 50), J("부러진 화살촉", 5, 51), J("부러진 편자", 7, 52), J("구리 철사 뭉치", 15, 53),
            // 낡은 잡동사니
            J("이 빠진 나무 숟가락", 3, 54), J("금 간 찻잔", 5, 55), J("녹아내린 양초", 4, 56), J("녹슨 열쇠", 9, 57), J("부러진 빗", 3, 58), J("찌그러진 양철 컵", 6, 59),
            // 식량
            J("질긴 육포", 6, 60), J("마른 버섯", 4, 61), J("곰팡이 핀 빵", 3, 62), J("시든 순무", 3, 63), J("소금 주머니", 18, 64), J("말린 생선 꾸러미", 7, 65),
            // 소박한 잡동사니
            J("나무 호루라기", 6, 66), J("뼈 주사위", 40, 67), J("소용돌이 조약돌", 5, 68), J("나무 구슬 팔찌", 42, 69), J("해진 가죽 주머니", 5, 70), J("고블린 목각상", 48, 71),
            // 재료
            J("잔가지 묶음", 3, 72), J("깃털 다발", 8, 73), J("짐승 털 뭉치", 7, 74), J("송진 덩어리", 16, 75), J("거친 노끈 타래", 5, 76), J("부싯돌", 10, 77),
            // 폐품 장비
            J("낡은 새총", 8, 78), J("쪼개진 판자 방패", 6, 79), J("해진 장갑", 5, 80), J("붕대 감은 발싸개", 3, 81), J("날 빠진 손도끼", 10, 82), J("녹슨 창촉", 7, 83),
        };

        private static int JunkWeight(LootItem item) => item.Grade == LootGrade.Lowest ? JunkLowestWeight : JunkLowWeight;
        private static readonly int JunkTotalWeight = GoblinJunk.Sum(JunkWeight);

        public static readonly LootItem[] All = new[]
        {
            RiderDagger, CoinPouch, RiderArrows, WolfBridle, Fang, Claw, Pelt, ManaFang,
            MushroomCap, SporeSac, Venom, MyceliumCrystal, LifeSeed, MossyBark, RootThorn, EntKingHeart,
            OldSword, WoodenShield, LeatherBracer, RedSash, ShortBow, ArcherArrows, Quiver, ShootingGlove,
            CarpenterHammer, ToolPouch, Plane, MasterChisel, RustyHelm, ShieldRim, Pauldron, ShieldBoss,
            TreeRing, MossClump, RootVine, LivingKnot,
        }.Concat(GoblinTreasures).Concat(GoblinJunk).Concat(GoblinMana).Concat(ArcherMana).Concat(RiderMana).Concat(WolfMana).Concat(MushroomMana).Concat(EntMana).Concat(StumpMana).Concat(Materials).ToArray();

        private static readonly LootItem[] Rider = { RiderDagger, CoinPouch, RiderArrows, WolfBridle };
        private static readonly LootItem[] Wolf = { Fang, Claw, Pelt, ManaFang };
        private static readonly LootItem[] Mushroom = { MushroomCap, SporeSac, Venom, MyceliumCrystal };
        private static readonly LootItem[] Ent = { LifeSeed, MossyBark, RootThorn, EntKingHeart };
        private static readonly LootItem[] Warrior = { OldSword, WoodenShield, LeatherBracer, RedSash };
        private static readonly LootItem[] Archer = { ShortBow, ArcherArrows, Quiver, ShootingGlove };
        private static readonly LootItem[] Carpenter = { CarpenterHammer, ToolPouch, Plane, MasterChisel };
        private static readonly LootItem[] Shield = { RustyHelm, ShieldRim, Pauldron, ShieldBoss };
        private static readonly LootItem[] Stump = { TreeRing, MossClump, RootVine, LivingKnot };

        // 몬스터별 건설 재료표(고블린은 쇠붙이, 궁수는 바느질감, 늑대는 가죽, 버섯은 염료, 목수는 목재·쇠못·밧줄·밀랍, 그루터기는 목재)
        private static readonly LootItem[] WarriorMaterials = { IronRing, Rope };
        private static readonly LootItem[] ArcherMaterials = { Thread, Needle, Cloth };
        private static readonly LootItem[] RiderMaterials = { Rope, Strap, IronRing };
        private static readonly LootItem[] WolfMaterials = { Leather, Strap };
        private static readonly LootItem[] MushroomMaterials = { Dye };
        private static readonly LootItem[] CarpenterMaterials = { Log, Stake, Nail, Rope, Wax };
        private static readonly LootItem[] StumpMaterials = { Log, Stake };
        private static readonly LootItem[] ShieldMaterials = { IronRing, Nail, Strap }; // 방패·투구의 쇠붙이와 손잡이 끈

        private static LootItem[] MaterialsFor(LootItem[] table) =>
            table == Rider ? RiderMaterials : table == Wolf ? WolfMaterials : table == Mushroom ? MushroomMaterials
            : table == Archer ? ArcherMaterials : table == Carpenter ? CarpenterMaterials : table == Stump ? StumpMaterials : table == Shield ? ShieldMaterials : WarriorMaterials;

        /// <summary>
        /// 그 적에게서 나올 수 있는 건설 재료(의뢰 게시판·상세에 "얻는 재료"로 보인다). 그냥 고블린 무리엔 궁수·목수가 섞여
        /// 전사·궁수·목수 재료를 다 보이고, 엔트(숲의 주인)는 통나무·말뚝.
        /// </summary>
        public static LootItem[] MaterialsForEnemy(string enemyName)
        {
            var table = TableFor(enemyName);
            if (table == Ent) return new[] { Log, Stake };
            if (table == Warrior) return WarriorMaterials.Concat(ArcherMaterials).Concat(CarpenterMaterials).Concat(ShieldMaterials).Distinct().ToArray();
            return MaterialsFor(table);
        }

        // 이름을 바꾼 전리품의 예전 이름(저장·상인 재고를 읽을 때)
        private static readonly Dictionary<string, string> OldNames = new Dictionary<string, string>
        {
            ["가죽"] = "늑대 가죽",
            ["화살 묶음"] = "늑대기수 화살",
        };

        /// <summary>그 몬스터 자신이 떨어뜨리는 건설 재료만(도감 "획득한 아이템"). MaterialsForEnemy는 게시판용이라 고블린 무리의 궁수·목수·방패병 몫까지 합친다.</summary>
        public static LootItem[] OwnMaterialsFor(string enemyName)
        {
            var table = TableFor(enemyName);
            return table == Ent ? new[] { Log, Stake } : MaterialsFor(table);
        }

        public static LootItem Find(string name)
        {
            if (name != null && OldNames.TryGetValue(name, out var renamed)) name = renamed;
            return All.FirstOrDefault(i => i.Name == name);
        }

        /// <summary>적 이름 → 전리품 표(흔한 것 3 + 드문 것 1). 이름 판별은 전투 그림(BattleMonsterSprite.KindFor)과 같은 순서.
        /// archerGoblin·carpenterGoblin: 자동 전투에서 이 고블린을 궁수·목수로 칠지(직접 전투는 이름에 "궁수"·"목수"가 붙어 온다).</summary>
        public static LootItem[] TableFor(string enemyName, bool archerGoblin = false, bool carpenterGoblin = false, bool shieldGoblin = false)
        {
            string name = enemyName ?? "";
            if (name.Contains("늑대기수")) return Rider; // 이름에 "늑대"도 있어 늑대보다 먼저
            if (RegionInfo.IsStump(name)) return Stump; // 이름에 "엔트"가 있어도 앤트킹 전리품(심장)은 없다
            if (RegionInfo.IsEnt(name)) return Ent;
            if (name.Contains("늑대") || name.Contains("울프")) return Wolf;
            if (name.Contains("버섯")) return Mushroom;
            if (name.Contains("목수") || carpenterGoblin) return Carpenter;
            if (name.Contains("방패병") || shieldGoblin) return Shield;
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
            : table == Mushroom ? MushroomMana : table == Stump ? StumpMana : table == Archer ? ArcherMana : GoblinMana; // 목수도 고블린 마정석

        /// <summary>고블린 종류의 전리품 표인지(전사·궁수·목수·방패병·늑대기수) — 고블린 보물을 떨어뜨린다.</summary>
        private static bool IsGoblinTable(LootItem[] table) =>
            table == Warrior || table == Archer || table == Carpenter || table == Shield || table == Rider;

        /// <summary>그 적이 떨어뜨릴 수 있는 고블린 보물(고블린이 아니면 빈 배열). 도감 "획득한 아이템"이 쓴다.</summary>
        public static LootItem[] TreasuresFor(string enemyName) =>
            IsGoblinTable(TableFor(enemyName)) ? GoblinTreasures : Array.Empty<LootItem>();

        /// <summary>한 마리당 따로 굴리는 확률이 정해진 것(마정석·고블린 보물)이면 그 확률(0~1), 아니면 -1. 도감 툴팁이 쓴다.</summary>
        public static float PerKillChance(LootItem item)
        {
            int color = ManaColorOf(item);
            if (color >= 0) return ManaChances[color];
            int treasure = Array.IndexOf(GoblinTreasures, item);
            if (treasure >= 0) return TreasureChances[treasure];
            return Array.IndexOf(GoblinJunk, item) >= 0 ? JunkChance * JunkWeight(item) / JunkTotalWeight : -1f;
        }

        /// <summary>그 적이 떨어뜨릴 수 있는 고블린 잡동사니(고블린이 아니면 빈 배열). 도감 "획득한 아이템"이 쓴다.</summary>
        public static LootItem[] JunkFor(string enemyName) =>
            IsGoblinTable(TableFor(enemyName)) ? GoblinJunk : Array.Empty<LootItem>();

        /// <summary>고블린 잡동사니 한 번 굴림: JunkChance로 하나(하급은 최하급의 1/3 가중치), 아니면 null.</summary>
        public static LootItem RollJunk(System.Random rng)
        {
            if (rng.NextDouble() >= JunkChance) return null;
            int pick = rng.Next(JunkTotalWeight);
            foreach (var item in GoblinJunk)
            {
                pick -= JunkWeight(item);
                if (pick < 0) return item;
            }
            return GoblinJunk[GoblinJunk.Length - 1];
        }

        /// <summary>고블린 보물 한 번 굴림(TreasureChances, 합 1%). 대부분 없음(null).</summary>
        public static LootItem RollTreasure(System.Random rng)
        {
            double roll = rng.NextDouble();
            for (int i = 0; i < GoblinTreasures.Length; i++)
            {
                roll -= TreasureChances[i];
                if (roll < 0) return GoblinTreasures[i];
            }
            return null;
        }

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
        /// archerSplit: 자동 전투처럼 이름에 궁수·목수 구분이 없으면 true(고블린 셋 중 하나를 궁수로, 남은 것 중 일부를 목수로).
        /// 직접 전투는 이름에 "궁수"·"목수"가 붙어 온다.</summary>
        public static List<(LootItem item, int count)> Roll(IEnumerable<string> killedEnemyNames, System.Random rng, bool archerSplit = true)
        {
            var drops = new List<LootItem>();
            foreach (var name in killedEnemyNames)
            {
                var (autoArcher, autoCarpenter, autoShield) = archerSplit ? SplitGoblin(name, rng) : (false, false, false);
                var table = TableFor(name, autoArcher, autoCarpenter, autoShield);
                var mana = RollManaStone(ManaFor(table), rng); // 전리품과 따로, 한 마리마다 한 번
                if (mana != null) drops.Add(mana);
                if (IsGoblinTable(table))
                {
                    var treasure = RollTreasure(rng); // 고블린이 모아 둔 보물(아주 드물게)
                    if (treasure != null) drops.Add(treasure);
                    var junk = RollJunk(rng); // 들고 다니던 잡동사니(흔하게)
                    if (junk != null) drops.Add(junk);
                }
                if (table == Ent)
                {
                    // 숲의 주인: 엔트 전리품 2~3개 + 심장 확정
                    int n = rng.Next(2, 4);
                    for (int i = 0; i < n; i++) drops.Add(table[rng.Next(3)]);
                    drops.Add(EntKingHeart);
                    int logs = rng.Next(3, 6); // 쓰러진 숲의 주인에게서 목재
                    for (int i = 0; i < logs; i++) drops.Add(rng.NextDouble() < 0.6 ? Log : Stake);
                    continue;
                }
                var materials = MaterialsFor(table);
                if (table == Carpenter)
                {
                    // 목재·연장을 짊어진 일꾼: 하나는 꼭, 가끔 하나 더
                    drops.Add(materials[rng.Next(materials.Length)]);
                    if (rng.NextDouble() < CarpenterExtraMaterialChance) drops.Add(materials[rng.Next(materials.Length)]);
                }
                else if (rng.NextDouble() < MaterialChance)
                    drops.Add(materials[rng.Next(materials.Length)]);
                // 개체 등급(이름 앞말)이 높을수록 잘 떨어뜨리고 드문 것도 잘 나온다
                var (dropChance, rareChance) = ChancesFor(GN3.Combat.MonsterIV.RankOf(name));
                if (rng.NextDouble() >= dropChance) continue;
                drops.Add(rng.NextDouble() < rareChance ? table[3] : table[rng.Next(3)]);
            }
            return drops.GroupBy(d => d).Select(g => (g.Key, g.Count())).OrderBy(d => Array.IndexOf(All, d.Item1)).ToList();
        }

        /// <summary>자동 전투: 이름에 궁수·목수·방패병 구분이 없는 그냥 고블린을 한 번 굴려 (궁수인지, 목수인지, 방패병인지). 1/3 궁수, 그다음 AutoCarpenterChance 목수, AutoShieldChance 방패병.
        /// 고블린이 아니면 TableFor가 이 값을 보지 않는다(도감 Bestiary.KindOf도 같은 비율).</summary>
        public static (bool archer, bool carpenter, bool shield) SplitGoblin(string name, System.Random rng)
        {
            if (name == null || name.Contains("궁수") || name.Contains("목수") || name.Contains("방패병")) return (false, false, false);
            double roll = rng.NextDouble();
            const double carpenterEnd = AutoArcherChance + AutoCarpenterChance;
            return (roll < AutoArcherChance, roll >= AutoArcherChance && roll < carpenterEnd,
                roll >= carpenterEnd && roll < carpenterEnd + AutoShieldChance);
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
        private static readonly HashSet<string> _seen = new HashSet<string>(); // 한 번이라도 얻은 아이템 이름(도감 "획득한 아이템")

        public static event Action OnChanged;

        /// <summary>그 아이템을 한 번이라도 얻어 봤는가(팔거나 써서 지금 없어도). 도감이 얻어 본 것만 보여 준다.</summary>
        public static bool HasSeen(LootItem item) => item != null && _seen.Contains(item.Name);

        /// <summary>카탈로그 순서대로 (전리품, 개수).</summary>
        public static IEnumerable<(LootItem item, int count)> Entries =>
            LootCatalog.All.Where(i => Count(i) > 0).Select(i => (i, Count(i)));

        public static int Count(LootItem item) => _counts.TryGetValue(item, out int n) ? n : 0;
        /// <summary>전리품 종류 수(건설 재료는 뺀다 — 창고 화면에서 따로 보인다).</summary>
        public static int KindCount => _counts.Count(kv => kv.Value > 0 && !kv.Key.Material);

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
        /// <summary>[모두 팔기]가 파는 것인가: 건설 재료·제작 재료(마정석·제작법 재료)는 남긴다.</summary>
        public static bool SellsInBulk(LootItem item) => !item.Material && !Crafting.IsIngredient(item);

        /// <summary>전리품을 모두 팔면 받는 골드(건설 재료·제작 재료는 뺀다).</summary>
        public static int TotalValue => _counts.Where(kv => SellsInBulk(kv.Key)).Sum(kv => kv.Key.Price * kv.Value);

        /// <summary>[모두 팔기]에서 남기는 제작 재료 종류 수(건설 재료 제외).</summary>
        public static int KeptIngredientKinds => _counts.Keys.Count(i => !i.Material && Crafting.IsIngredient(i));

        /// <summary>용병 가방을 비워 창고에 넣는다(가방 창·창고의 가방 비우기). 넣은 개수.</summary>
        public static int StashFrom(Mercenaries.Mercenary merc)
        {
            var items = merc.TakeAllFromBag();
            Add(items.GroupBy(i => i).Select(g => (g.Key, g.Count())));
            return items.Count;
        }

        /// <summary>얻어 본 것으로만 적는다(창고에 넣지 않고 용병 가방에 담은 전리품 — 도감).</summary>
        public static void MarkSeen(IEnumerable<LootItem> items)
        {
            foreach (var item in items)
                if (item != null) _seen.Add(item.Name);
        }

        public static void Add(IEnumerable<(LootItem item, int count)> loot)
        {
            bool any = false;
            foreach (var (item, count) in loot)
            {
                if (count <= 0) continue;
                _seen.Add(item.Name);
                if (item == LootCatalog.CoinPouch)
                {
                    Wallet.Add(item.Price * count, "동전 주머니"); // 동전 주머니는 줍는 대로 금화로(가방에 쌓이지 않는다)
                    continue;
                }
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
            Wallet.Add(gold, "전리품 판매");
            OnChanged?.Invoke();
            return gold;
        }

        public static int SellAll()
        {
            int gold = TotalValue;
            if (gold <= 0) return 0;
            foreach (var item in _counts.Keys.Where(SellsInBulk).ToList()) _counts.Remove(item); // 건설 재료·제작 재료(마정석 등)는 남긴다
            Wallet.Add(gold, "전리품 판매");
            OnChanged?.Invoke();
            return gold;
        }

        // ---------- 저장 ----------

        public static List<Save.LootSave> SaveEntries() =>
            Entries.Select(e => new Save.LootSave { name = e.item.Name, count = e.count }).ToList();

        public static List<string> SaveSeen() => _seen.ToList();

        /// <summary>얻어 본 아이템 기록을 되돌린다. 기록이 없던 예전 저장은 지금 가방에 든 것을 얻어 본 것으로 친다(Restore 뒤에 부른다).</summary>
        public static void RestoreSeen(IEnumerable<string> names)
        {
            _seen.Clear();
            if (names != null)
                foreach (var name in names)
                {
                    var item = LootCatalog.Find(name); // 이름을 바꾼 아이템은 새 이름으로
                    if (item != null) _seen.Add(item.Name);
                }
            foreach (var item in _counts.Keys) _seen.Add(item.Name);
        }

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

        // Domain Reload가 꺼져 있어 Play마다 비운다(QuestClearHistory와 같은 사정). 새 게임은 길드 천막 한 채 분 재료를 들고 시작한다
        // (저장을 불러오면 Restore가 덮는다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _counts.Clear();
            _seen.Clear();
            foreach (var (item, count) in BuildMaterials.StartingKit)
            {
                _counts[item] = count;
                _seen.Add(item.Name);
            }
            OnChanged = null;
        }
    }
}
