using System;
using System.Collections.Generic;
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

        /// <summary>클래스·레벨 → 등급 배율 → 성격 보정 → 무기 보너스(고정값) → 피로·사기 배율(공격·방어만) → 부상·질병 순으로 계산한 현재 능력치.</summary>
        public CombatStats CurrentStats => ComputeStats(_ailments);

        /// <summary>부상·질병이 없었다면의 능력치(툴팁에서 "공격 24 → 18"처럼 비교할 때).</summary>
        public CombatStats StatsWithoutAilments => ComputeStats(null);

        /// <summary>이 상태이상들만 적용한 능력치(null이면 부상·질병 없이).</summary>
        public CombatStats ComputeStats(IEnumerable<Ailment> ailments)
        {
            var graded = GradeTable.Apply(MercenaryStatCalculator.Calculate(Class, Level), Grade);
            var stats = PersonalityTable.Apply(graded, Personality);
            if (Weapon != null)
            {
                stats.Attack += Weapon.AttackFor(Class.Kind);
                stats.Defense += Weapon.Defense;
                stats.MoveSpeed = Math.Max(0, stats.MoveSpeed + Weapon.Speed);
            }
            // 연구소·성당에서 쌓은 영구 보너스(직업별)
            var research = ResearchStatBonus();
            stats.Attack += research.attack;
            stats.Defense += research.defense;
            stats.MaxHealth += research.health;
            // 지치거나 사기가 낮으면 공격·방어가 깎인다(최대 체력은 그대로라 체력 계산이 꼬이지 않는다).
            float condition = ConditionMultiplier;
            if (condition < 1f)
            {
                stats.Attack = (int)Math.Round(stats.Attack * condition);
                stats.Defense = (int)Math.Round(stats.Defense * condition);
            }
            // 부상·질병은 공격·방어·이동 속도를 깎는다(최대 체력은 그대로).
            if (ailments == null) return stats;
            foreach (var ailment in ailments)
            {
                var def = ailment.Def;
                stats.Attack = (int)Math.Round(stats.Attack * (1f - def.attackPenalty));
                stats.Defense = (int)Math.Round(stats.Defense * (1f - def.defensePenalty));
                stats.MoveSpeed = (int)Math.Round(stats.MoveSpeed * (1f - def.moveSpeedPenalty));
            }
            return stats;
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

        // ---------- 수련 단계 (마법사 = 마법 연구소, 힐러 = 성당 — RestVenues) ----------

        public const float ResearchHoursPerBonus = 4f;
        public const int MaxResearchLevel = 10;
        public const int HealerHealthPerResearchLevel = 4;

        /// <summary>연구소·성당에서 쌓은 수련 단계(4시간마다 +1, 최대 10). 단계가 어떤 능력치가 되는지는 직업이 정한다(ResearchStatBonus).</summary>
        public int ResearchLevel { get; private set; }
        /// <summary>다음 단계까지 쌓인 수련 시간(0~4).</summary>
        public float ResearchHours { get; private set; }

        public bool CanResearchMore => ResearchLevel < MaxResearchLevel;

        /// <summary>수련 단계가 주는 능력치: 마법사 공격 +1/단계, 힐러 방어 +1 · 최대 체력 +4/단계, 그 외 없음.</summary>
        public (int attack, int defense, int health) ResearchStatBonus() => Class.Kind switch
        {
            MercenaryClassKind.Mage => (ResearchLevel, 0, 0),
            MercenaryClassKind.Healer => (0, ResearchLevel, ResearchLevel * HealerHealthPerResearchLevel),
            _ => (0, 0, 0),
        };

        /// <summary>UI용: "공격 +3" / "방어 +2 · 체력 +8" (보너스가 없으면 빈 문자열).</summary>
        public string ResearchBonusText()
        {
            var (attack, defense, health) = ResearchStatBonus();
            var parts = new List<string>();
            if (attack > 0) parts.Add($"공격 +{attack}");
            if (defense > 0) parts.Add($"방어 +{defense}");
            if (health > 0) parts.Add($"체력 +{health}");
            return string.Join(" · ", parts);
        }

        /// <summary>UI용 수련 이름: 마법사 "연구", 힐러 "기도".</summary>
        public string ResearchLabel => Class.Kind == MercenaryClassKind.Healer ? "기도" : "연구";

        /// <summary>수련 시간을 더하고 새로 오른 단계 수를 돌려준다. 최대 체력이 오르면 현재 체력도 그만큼 오른다. 최대가 되면 더 쌓지 않는다.</summary>
        public int AddResearch(float hours)
        {
            if (hours <= 0f || !CanResearchMore) return 0;
            ResearchHours += hours;
            int gained = 0;
            while (CanResearchMore && ResearchHours >= ResearchHoursPerBonus)
            {
                ResearchHours -= ResearchHoursPerBonus;
                int oldMax = CurrentStats.MaxHealth;
                ResearchLevel++;
                gained++;
                if (IsAlive)
                    CurrentHealth = Math.Min(CurrentStats.MaxHealth, CurrentHealth + (CurrentStats.MaxHealth - oldMax));
            }
            if (!CanResearchMore) ResearchHours = 0f;
            return gained;
        }

        /// <summary>저장 파일에서 불러올 때 수련 단계·시간을 되돌린다(체력보다 먼저 — 힐러는 최대 체력이 달라진다).</summary>
        public void RestoreResearch(float hours, int level)
        {
            ResearchLevel = Math.Clamp(level, 0, MaxResearchLevel);
            ResearchHours = CanResearchMore ? Math.Clamp(hours, 0f, ResearchHoursPerBonus) : 0f;
        }

        // ---------- 부상·질병 (규칙·수치는 Ailments, 입원은 Hospital) ----------

        private readonly List<Ailment> _ailments = new List<Ailment>();

        /// <summary>지금 앓는 부상·질병(부상 하나·질병 하나까지).</summary>
        public IReadOnlyList<Ailment> Ailments => _ailments;
        public bool HasAilment => _ailments.Count > 0;
        /// <summary>골절·열병처럼 중한 상태이상이 있으면 파견할 수 없다.</summary>
        public bool IsSeverelyAiling => _ailments.Exists(a => a.Def.isSevere);
        public bool HasInjury => _ailments.Exists(a => a.Def.IsInjury);
        public bool HasIllness => _ailments.Exists(a => !a.Def.IsInjury);

        /// <summary>같은 갈래(부상/질병)가 이미 있으면 걸리지 않는다. 걸렸으면 true.</summary>
        public bool AddAilment(AilmentSO def, float progress = 0f, int daysUntreated = 0)
        {
            if (def == null || _ailments.Exists(a => a.Def.IsInjury == def.IsInjury)) return false;
            _ailments.Add(new Ailment(def, Math.Clamp(progress, 0f, 0.99f), Math.Max(0, daysUntreated)));
            return true;
        }

        public void RemoveAilment(Ailment ailment) => _ailments.Remove(ailment);

        /// <summary>UI용: "골절 45% · 감기 10%" (없으면 빈 문자열).</summary>
        public string AilmentSummary() =>
            string.Join(" · ", _ailments.ConvertAll(a => $"{a.Def.displayName} {Math.Round(a.Progress * 100f)}%"));

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
