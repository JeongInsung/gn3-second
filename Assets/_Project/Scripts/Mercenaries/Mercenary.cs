using System;
using GN3.Combat;
using GN3.CharacterAnim;
using GN3.Economy;
using GN3.Traits;

namespace GN3.Mercenaries
{
    public class Mercenary
    {
        public string Id { get; }
        public string Name { get; }
        public MercenaryClassSO Class { get; }
        public int Level { get; private set; }
        public ComposedCharacter Appearance { get; }
        public Personality Personality { get; }
        public bool HasRarePassive { get; }

        /// <summary>타고난 등급(F~S). 클래스·레벨 능력치에 배율로 붙는다(GradeTable).</summary>
        public MercenaryGrade Grade { get; private set; }

        /// <summary>최종 능력치로 계산한 전투력(레벨·등급·성격·무기 모두 반영).</summary>
        public int CombatPower => GradeTable.CombatPower(CurrentStats);

        /// <summary>들고 있는 무기(없으면 맨손). 대장간에서 사서 쥐여 준다.</summary>
        public WeaponItem Weapon { get; private set; }

        /// <summary>클래스·레벨 → 등급 배율 → 성격 보정 → 무기 보너스(고정값) → 피로·사기 배율(공격·방어만) 순으로 계산한 현재 능력치.</summary>
        public CombatStats CurrentStats
        {
            get
            {
                var graded = GradeTable.Apply(MercenaryStatCalculator.Calculate(Class, Level), Grade);
                var stats = PersonalityTable.Apply(graded, Personality);
                if (Weapon != null)
                {
                    stats.Attack += Weapon.AttackFor(Class.Kind);
                    stats.Defense += Weapon.Defense;
                    stats.MoveSpeed = Math.Max(0, stats.MoveSpeed + Weapon.Speed);
                }
                // 지치거나 사기가 낮으면 공격·방어가 깎인다(최대 체력은 그대로라 체력 계산이 꼬이지 않는다).
                float condition = ConditionMultiplier;
                if (condition < 1f)
                {
                    stats.Attack = (int)Math.Round(stats.Attack * condition);
                    stats.Defense = (int)Math.Round(stats.Defense * condition);
                }
                return stats;
            }
        }

        // ---------- 피로·사기 (규칙·수치는 MercenaryCondition) ----------

        public const int MaxCondition = 100;
        public const int StartingMorale = 70;
        public const int TiredFatigue = 50;      // 이상이면 공격·방어 -10%
        public const int ExhaustedFatigue = 80;  // 이상이면 -25%, 파견·훈련 불가
        public const int LowMorale = 30;         // 미만이면 공격·방어 -10%

        /// <summary>피로 0~100. 파견·전투·훈련으로 오르고 마을에서 자면 내린다.</summary>
        public int Fatigue { get; private set; }
        /// <summary>사기 0~100. 승리·급여로 오르고 패배·동료 전사·미지급으로 내린다. 0이면 길드를 떠난다.</summary>
        public int Morale { get; private set; } = StartingMorale;

        public bool IsExhausted => Fatigue >= ExhaustedFatigue;

        /// <summary>피로·사기에 따른 공격·방어 배율(1 = 정상).</summary>
        public float ConditionMultiplier
        {
            get
            {
                float m = Fatigue >= ExhaustedFatigue ? 0.75f : Fatigue >= TiredFatigue ? 0.9f : 1f;
                if (Morale < LowMorale) m *= 0.9f;
                return m;
            }
        }

        public void AddFatigue(int amount) => Fatigue = Math.Clamp(Fatigue + amount, 0, MaxCondition);
        public void AddMorale(int amount) => Morale = Math.Clamp(Morale + amount, 0, MaxCondition);

        /// <summary>저장 파일에서 불러올 때 피로·사기를 되돌린다.</summary>
        public void RestoreCondition(int fatigue, int morale)
        {
            Fatigue = Math.Clamp(fatigue, 0, MaxCondition);
            Morale = Math.Clamp(morale, 0, MaxCondition);
        }

        /// <summary>무기를 든다. 들고 있던 무기는 버린다(되팔기 없음).</summary>
        public void Equip(WeaponItem weapon) => Weapon = weapon;

        /// <summary>전투/이동 중 습격 등으로 깎이고, 회복 수단이 생기기 전까지는 계속 남아있는 실제 체력.</summary>
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int level = 1, ComposedCharacter appearance = null, Personality personality = Personality.Calm, bool hasRarePassive = false,
            MercenaryGrade grade = MercenaryGrade.D, string id = null)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id; // id는 저장 파일에서 복원할 때만 넘긴다
            Grade = grade; // 체력 초기값(최대 체력)이 등급을 반영하도록 먼저 정한다
            Name = name;
            Class = mercenaryClass;
            Level = level;
            Appearance = appearance ?? new ComposedCharacter();
            Personality = personality;
            HasRarePassive = hasRarePassive;
            CurrentHealth = CurrentStats.MaxHealth;
        }

        public void LevelUp()
        {
            Level++;
        }

        public const int MaxLevel = 20;

        /// <summary>현재 레벨에서 쌓은 경험치(레벨이 오르면 넘친 만큼만 남는다).</summary>
        public int Experience { get; private set; }

        /// <summary>다음 레벨까지 필요한 경험치.</summary>
        public int XpToNext => 100 * Level;

        /// <summary>경험치를 더하고 오른 레벨 수를 돌려준다. 오른 최대 체력만큼 현재 체력도 함께 오른다.</summary>
        public int AddExperience(int xp)
        {
            if (xp <= 0 || Level >= MaxLevel) return 0;
            Experience += xp;
            int gained = 0;
            while (Level < MaxLevel && Experience >= XpToNext)
            {
                Experience -= XpToNext;
                int oldMax = CurrentStats.MaxHealth;
                Level++;
                gained++;
                if (IsAlive)
                    CurrentHealth = Math.Min(CurrentStats.MaxHealth, CurrentHealth + (CurrentStats.MaxHealth - oldMax));
            }
            if (Level >= MaxLevel) Experience = 0;
            return gained;
        }

        /// <summary>저장 파일에서 불러올 때: 경험치·체력·무기를 그대로 되돌린다.</summary>
        public void RestoreState(int experience, int health, WeaponItem weapon)
        {
            Experience = Math.Max(0, experience);
            Weapon = weapon;
            CurrentHealth = Math.Clamp(health, 0, CurrentStats.MaxHealth);
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 등급을 바꾸고 체력을 새 최대 체력에 맞춰 자른다.</summary>
        public void SetGrade(MercenaryGrade grade)
        {
            Grade = grade;
            CurrentHealth = Math.Clamp(CurrentHealth, 0, CurrentStats.MaxHealth);
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 레벨을 바꾸고 체력을 새 최대 체력에 맞춰 자른다.</summary>
        public void SetLevel(int level)
        {
            Level = Math.Max(1, level);
            CurrentHealth = Math.Clamp(CurrentHealth, 0, CurrentStats.MaxHealth);
        }

        /// <summary>전투 시뮬레이션이 끝난 뒤 그 결과(Combatant.CurrentHealth)를 그대로 반영할 때 쓴다.</summary>
        public void SetHealth(int value)
        {
            CurrentHealth = Math.Clamp(value, 0, CurrentStats.MaxHealth);
        }

        /// <summary>휴식으로 체력을 완전히 회복한다.</summary>
        public void HealFully()
        {
            CurrentHealth = CurrentStats.MaxHealth;
        }

        public Combatant ToCombatant()
        {
            var combatant = new Combatant(Name, CurrentStats, ClassPassiveFactory.Create(Class.Kind, HasRarePassive));
            int missingHealth = combatant.Stats.MaxHealth - CurrentHealth;
            if (missingHealth > 0)
                combatant.TakeDamage(missingHealth);
            return combatant;
        }
    }
}
