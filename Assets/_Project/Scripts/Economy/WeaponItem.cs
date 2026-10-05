using System.Collections.Generic;
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

        private Sprite _icon;
        public Sprite Icon => _icon != null ? _icon : _icon = Resources.Load<Sprite>(IconPath);

        public WeaponItem(string name, int price, int attack, int defense, int speed, MercenaryClassKind? affinity, int iconNumber)
        {
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

        /// <summary>"공격 +6(적성) · 속도 +1" (특정 용병 기준)</summary>
        public string DescribeFor(MercenaryClassKind kind)
        {
            var parts = new List<string> { $"공격 +{AttackFor(kind)}" + (Suits(kind) ? "(적성)" : "") };
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
            _ => kind.ToString(),
        };
    }

    public static class WeaponCatalog
    {
        /// <summary>시트 순서(왼쪽 위부터 4열×3줄)와 아이콘 번호가 같다.</summary>
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
        };
    }
}
