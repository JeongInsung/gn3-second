using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 대장간에서 파는 무기. 용병은 무기 하나를 들고, 그 보너스가 능력치(공격·방어·속도)에 더해진다.
    /// 클래스 적성(Affinity)이 맞으면 공격 보너스 ×1.5. 아이콘은 Resources/WeaponItems/weapon_NN.
    /// </summary>
    public class WeaponItem
    {
        private const float AffinityMultiplier = 1.5f;

        public string Name { get; }
        public int Price { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int Speed { get; }
        public MercenaryClassKind? Affinity { get; }
        public string IconPath { get; }
        /// <summary>대장간 제작으로만 얻는 무기(상점 목록에 안 나온다 — Crafting).</summary>
        public bool CraftOnly { get; }

        private Sprite _icon;
        public Sprite Icon => _icon != null ? _icon : _icon = Resources.Load<Sprite>(IconPath);

        public WeaponItem(string name, int price, int attack, int defense, int speed, MercenaryClassKind? affinity, int iconNumber, bool craftOnly = false)
        {
            CraftOnly = craftOnly;
            Name = name;
            Price = price;
            Attack = attack;
            Defense = defense;
            Speed = speed;
            Affinity = affinity;
            IconPath = $"WeaponItems/weapon_{iconNumber:00}";
        }

        public bool Suits(MercenaryClassKind kind) => Affinity.HasValue && Affinity.Value == kind;

        public int AttackFor(MercenaryClassKind kind) =>
            Suits(kind) ? Mathf.RoundToInt(Attack * AffinityMultiplier) : Attack;

        /// <summary>"공격 +4 · 속도 +1 · 전사 적성" (상점 목록용)</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string> { $"공격 +{Attack}" };
                if (Defense != 0) parts.Add($"방어 {Signed(Defense)}");
                if (Speed != 0) parts.Add($"속도 {Signed(Speed)}");
                parts.Add(Affinity.HasValue ? $"{ClassLabel(Affinity.Value)} 적성" : "누구나");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>"공격 +6(적성) · 속도 +1" (특정 용병 기준, 마법사는 무기 공격이 마력으로 들어가 "마력 +6")</summary>
        public string DescribeFor(MercenaryClassKind kind)
        {
            string offense = kind == MercenaryClassKind.Mage ? "마력" : "공격";
            var parts = new List<string> { $"{offense} +{AttackFor(kind)}" + (Suits(kind) ? "(적성)" : "") };
            if (Defense != 0) parts.Add($"방어 {Signed(Defense)}");
            if (Speed != 0) parts.Add($"속도 {Signed(Speed)}");
            return string.Join(" · ", parts);
        }

        private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

        public static string ClassLabel(MercenaryClassKind kind) => kind switch
        {
            MercenaryClassKind.Warrior => "전사",
            MercenaryClassKind.Archer => "궁수",
            MercenaryClassKind.Healer => "힐러",
            MercenaryClassKind.Assassin => "암살자",
            MercenaryClassKind.Guide => "길잡이",
            MercenaryClassKind.Mage => "마법사",
            _ => kind.ToString(),
        };
    }

    public static class WeaponCatalog
    {
        // 대장간 제작 전용(드문 전리품 — Crafting). 아이콘은 비슷한 상점 무기 것을 쓴다. 저장은 이름이라 All에 들어 있어야 한다.
        public static readonly WeaponItem WolfKingDagger = new WeaponItem("늑대왕의 송곳니 단검", 260, 6, 0, 3, MercenaryClassKind.Assassin, 2, craftOnly: true);
        public static readonly WeaponItem MyceliumStaff = new WeaponItem("균사 결정 지팡이", 300, 8, 1, 0, MercenaryClassKind.Mage, 11, craftOnly: true);
        public static readonly WeaponItem EntHeartBlade = new WeaponItem("엔트 심장 대검", 600, 12, 2, -1, MercenaryClassKind.Warrior, 3, craftOnly: true);

        public static WeaponItem Find(string name) => All.FirstOrDefault(w => w.Name == name);

        // 마정 무기(제작 전용): 직업마다 한 줄 × 마정석 5색(푸른 → 붉은 = 등급 0~4). 아이콘은 같은 계열 상점 무기 것.
        // (직업, 무기 이름, 아이콘, 등급 t → 공격·방어·속도)
        private static readonly (MercenaryClassKind Kind, string Name, int Icon, Func<int, (int atk, int def, int spd)> Stats)[] ManaLines =
        {
            (MercenaryClassKind.Warrior,  "마정 대검",   3,  t => (5 + 3 * t, t / 2, 0)),
            (MercenaryClassKind.Archer,   "마정 장궁",   9,  t => (5 + 3 * t, 0, 1)),
            (MercenaryClassKind.Assassin, "마정 단검",   2,  t => (4 + 2 * t, 0, 2 + t / 2)),
            (MercenaryClassKind.Guide,    "마정 창",     8,  t => (3 + 2 * t, 2 + t, 0)),
            (MercenaryClassKind.Healer,   "마정 홀",     11, t => (3 + 2 * t, 1 + t, 0)),
            (MercenaryClassKind.Mage,     "마정 지팡이", 11, t => (5 + 3 * t, 0, 0)),
        };
        public static readonly int[] ManaTierPrices = { 150, 300, 600, 1200, 2500 };
        public static int ManaLineCount => ManaLines.Length;

        /// <summary>마정 무기 30개: [줄 × 5 + 색]. "푸른 마정 대검" … "붉은 마정 지팡이".</summary>
        public static readonly WeaponItem[] ManaWeapons = ManaLines
            .SelectMany(line => Enumerable.Range(0, 5).Select(t =>
            {
                var (atk, def, spd) = line.Stats(t);
                return new WeaponItem($"{LootCatalog.ManaColorNames[t]} {line.Name}", ManaTierPrices[t], atk, def, spd, line.Kind, line.Icon, craftOnly: true);
            }))
            .ToArray();

        public static WeaponItem ManaWeapon(int line, int color) => ManaWeapons[line * 5 + color];

        /// <summary>시트 순서(왼쪽 위부터 4열×3줄)와 아이콘 번호가 같다. 끝의 셋은 제작 전용.</summary>
        public static readonly IReadOnlyList<WeaponItem> All = new List<WeaponItem>
        {
            new WeaponItem("롱소드",      100, 4, 0, 0,  MercenaryClassKind.Warrior,  1),
            new WeaponItem("단검",         50, 2, 0, 2,  MercenaryClassKind.Assassin, 2),
            new WeaponItem("대검",        220, 7, 0, -1, MercenaryClassKind.Warrior,  3),
            new WeaponItem("시미터",      120, 4, 0, 1,  MercenaryClassKind.Assassin, 4),
            new WeaponItem("전투 도끼",   140, 5, 0, 0,  MercenaryClassKind.Warrior,  5),
            new WeaponItem("모닝스타",    150, 5, 1, 0,  MercenaryClassKind.Warrior,  6),
            new WeaponItem("전쟁 망치",   240, 8, 0, -2, MercenaryClassKind.Warrior,  7),
            new WeaponItem("창",          110, 3, 2, 0,  MercenaryClassKind.Guide,    8),
            new WeaponItem("장궁",        120, 4, 0, 1,  MercenaryClassKind.Archer,   9),
            new WeaponItem("석궁",        180, 6, 0, -1, MercenaryClassKind.Archer,   10),
            new WeaponItem("수정 지팡이", 160, 3, 2, 0,  MercenaryClassKind.Healer,   11),
            new WeaponItem("흑요 마검",   450, 10, 0, 1, null,                        12),
            WolfKingDagger, MyceliumStaff, EntHeartBlade,
        }.Concat(ManaWeapons).ToList();
    }
}
