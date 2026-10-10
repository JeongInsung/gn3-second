using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 마을 쉼터(온천·도박장·마법 연구소). 용병이 마을을 돌아다니다 가끔 스스로 찾아와(VillagePartyPresenter가 VisitChance로 정함)
    /// 게임 시간 몇 시간 머물며 피로·사기가 바뀌고(연구소는 피로가 오름) 쉼터마다의 효과(onStayed)를 받은 뒤 나온다. 무료.
    /// eligible이 있으면 그 조건을 채운 사람만 들어간다(연구소 = 마법사만). 쉼터 안에 있는 동안은 파견·훈련·마을 산책을 하지 않는다.
    /// 시간은 GameClock.OnHoursPassed로 받는다(RestVenues가 구독, TrainingHall과 같은 방식). 소수점 피로·사기는 사람마다 쌓아 두었다가 1이 될 때 더한다.
    /// 같은 쉼터에 같이 있던 사람끼리는 함께 있던 시간만큼 친밀도가 오른다(Affinity).
    /// </summary>
    public sealed class RestVenue
    {
        public string Name { get; }               // 건물 패널 이름 겸 표시 이름("온천", "도박장")
        public string SpritePrefix { get; }       // 마을 그림 이름
        public float DoorU { get; }               // 그림에서 문 가운데(가로 비율)
        public int Capacity { get; }
        public float MinSessionHours { get; }
        public float MaxSessionHours { get; }
        public float FatigueReliefPerHour { get; }
        public float MoralePerHour { get; }
        public bool OpenAtNight { get; }
        public string Description { get; }
        public string Summary { get; }            // 패널 머리줄용 짧은 효과 문구("피로 -15 · 사기 +1")

        private readonly Func<Mercenary, float> _visitChance;
        private readonly Func<Mercenary, bool> _eligible;
        private readonly Action<Mercenary, float> _onStayed;
        private readonly List<string> _guestIds = new List<string>();
        private readonly Dictionary<string, float> _remainingHours = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _pendingRelief = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _pendingMorale = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _sessionRelief = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _sessionMorale = new Dictionary<string, int>();

        public event Action OnChanged;

        public RestVenue(string name, string spritePrefix, float doorU, int capacity, float minHours, float maxHours,
            float fatigueReliefPerHour, float moralePerHour, bool openAtNight, Func<Mercenary, float> visitChance, string description,
            string summary, Func<Mercenary, bool> eligible = null, Action<Mercenary, float> onStayed = null)
        {
            Name = name;
            SpritePrefix = spritePrefix;
            DoorU = doorU;
            Capacity = capacity;
            MinSessionHours = minHours;
            MaxSessionHours = maxHours;
            FatigueReliefPerHour = fatigueReliefPerHour;
            MoralePerHour = moralePerHour;
            OpenAtNight = openAtNight;
            _visitChance = visitChance;
            Description = description;
            Summary = summary;
            _eligible = eligible;
            _onStayed = onStayed;
        }

        /// <summary>지금 쉼터에 있는 용병(들어온 순서). 파티에서 빠졌거나 죽은 사람은 빠진다.</summary>
        public IReadOnlyList<Mercenary> Guests
        {
            get
            {
                Cleanup();
                return _guestIds.Select(Find).Where(m => m != null).ToList();
            }
        }

        public bool IsInside(Mercenary merc) => merc != null && _guestIds.Contains(merc.Id);

        public bool IsFull
        {
            get
            {
                Cleanup();
                return _guestIds.Count >= Capacity;
            }
        }

        /// <summary>지금 새로 받을 수 있는 시간인지(밤에 닫는 곳은 밤엔 안 받는다).</summary>
        public bool IsOpen => OpenAtNight || !GameClock.IsNight;

        /// <summary>갈 수 있는 사람: 살아 있고, 쉼터 조건(eligible)을 채우고, 파견·훈련·입원·다른 쉼터 중이 아님.</summary>
        public bool CanEnter(Mercenary merc) =>
            merc != null && merc.IsAlive && (_eligible == null || _eligible(merc)) && !ExpeditionLog.Instance.IsOnExpedition(merc)
            && !RestVenues.IsResting(merc) && !TrainingHall.IsTraining(merc) && !Hospital.IsAdmitted(merc);

        /// <summary>스스로 찾아올 확률(한 번 굴릴 때). 쉼터 조건을 못 채우면 0.</summary>
        public float VisitChance(Mercenary merc) => merc == null || (_eligible != null && !_eligible(merc)) ? 0f : _visitChance(merc);

        /// <summary>쉼터에 들어간다(hours = 이번에 머물 게임 시간). 닫혔거나 정원이 차면 거절.</summary>
        public bool TryAdd(Mercenary merc, float hours)
        {
            if (!CanEnter(merc) || IsFull || !IsOpen) return false;
            _guestIds.Add(merc.Id);
            _remainingHours[merc.Id] = Mathf.Max(0.1f, hours);
            _sessionRelief[merc.Id] = 0;
            _sessionMorale[merc.Id] = 0;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>이번에 남은 시간(게임 시간).</summary>
        public float RemainingHours(Mercenary merc) =>
            merc != null && _remainingHours.TryGetValue(merc.Id, out float h) ? h : 0f;

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤). 남은 시간은 저장하지 않으므로 사람마다 새로 정한다.</summary>
        public void Restore(IEnumerable<string> ids)
        {
            Clear();
            if (ids != null)
                foreach (var id in ids)
                {
                    if (_guestIds.Count >= Capacity || _guestIds.Contains(id) || Find(id) == null) continue;
                    _guestIds.Add(id);
                    _remainingHours[id] = UnityEngine.Random.Range(MinSessionHours, MaxSessionHours);
                    _sessionRelief[id] = 0;
                    _sessionMorale[id] = 0;
                }
            OnChanged?.Invoke();
        }

        /// <summary>저장용: 쉼터에 있는 용병 id.</summary>
        public List<string> SaveIds()
        {
            Cleanup();
            return new List<string>(_guestIds);
        }

        internal void Clear()
        {
            _guestIds.Clear();
            _remainingHours.Clear();
            _pendingRelief.Clear();
            _pendingMorale.Clear();
            _sessionRelief.Clear();
            _sessionMorale.Clear();
        }

        internal void ClearEvents() => OnChanged = null;

        internal void Pass(float hours)
        {
            Cleanup();
            var finished = new List<Mercenary>();
            var stayedHours = new List<(Mercenary merc, float hours)>(); // 같이 있던 시간만큼 친밀도
            foreach (var merc in _guestIds.Select(Find).Where(m => m != null).ToList())
            {
                // 이번에 남은 시간만큼만 쉰다(큰 건너뛰기에도 한 번 몫).
                float remaining = _remainingHours.TryGetValue(merc.Id, out float r) ? r : 0f;
                float stayed = Mathf.Min(hours, remaining);
                _remainingHours[merc.Id] = remaining - hours;
                if (remaining - hours <= 0f) finished.Add(merc);
                if (stayed <= 0f) continue;
                stayedHours.Add((merc, stayed));

                // 피로 변화는 부호를 떼고 쌓아 정수만큼 적용한다(연구소처럼 음수면 피로가 오른다).
                int relief = TakeWhole(_pendingRelief, merc.Id, stayed * Mathf.Abs(FatigueReliefPerHour)) * (FatigueReliefPerHour < 0f ? -1 : 1);
                if (relief != 0)
                {
                    int before = merc.Fatigue;
                    merc.AddFatigue(-relief);
                    _sessionRelief[merc.Id] = (_sessionRelief.TryGetValue(merc.Id, out int s) ? s : 0) + (before - merc.Fatigue);
                }
                int morale = TakeWhole(_pendingMorale, merc.Id, stayed * MoralePerHour);
                if (morale > 0)
                {
                    int before = merc.Morale;
                    merc.AddMorale(morale);
                    _sessionMorale[merc.Id] = (_sessionMorale.TryGetValue(merc.Id, out int s) ? s : 0) + (merc.Morale - before);
                }
                _onStayed?.Invoke(merc, stayed);
            }

            for (int i = 0; i < stayedHours.Count; i++)
                for (int j = i + 1; j < stayedHours.Count; j++)
                    Affinity.AddAmong(new[] { stayedHours[i].merc, stayedHours[j].merc },
                        Mathf.Min(stayedHours[i].hours, stayedHours[j].hours) * Affinity.RestGainPerHour);

            if (finished.Count == 0) return;
            foreach (var merc in finished)
            {
                int relief = _sessionRelief.TryGetValue(merc.Id, out int f) ? f : 0;
                int morale = _sessionMorale.TryGetValue(merc.Id, out int m) ? m : 0;
                _guestIds.Remove(merc.Id);
                Forget(merc.Id);
                var parts = new List<string>();
                if (morale > 0) parts.Add($"사기 +{morale}");
                if (relief > 0) parts.Add($"피로 -{relief}");
                else if (relief < 0) parts.Add($"피로 +{-relief}");
                ToastLog.Show($"{merc.Name} {Name}에서 나왔다" + (parts.Count > 0 ? $" ({string.Join(" · ", parts)})" : ""));
            }
            OnChanged?.Invoke();
        }

        /// <summary>쌓아 둔 소수점 값에 amount를 더하고 정수 부분만 꺼낸다.</summary>
        private static int TakeWhole(Dictionary<string, float> pending, string id, float amount)
        {
            pending.TryGetValue(id, out float value);
            value += amount;
            int whole = Mathf.FloorToInt(value);
            pending[id] = value - whole;
            return whole;
        }

        /// <summary>해고·사망·파견(불러오기 등)으로 더는 쉼터에 있을 수 없는 사람을 뺀다.</summary>
        private void Cleanup()
        {
            var gone = _guestIds.Where(id =>
            {
                var merc = Find(id);
                return merc == null || !merc.IsAlive || ExpeditionLog.Instance.IsOnExpedition(merc);
            }).ToList();
            if (gone.Count == 0) return;
            foreach (var id in gone)
            {
                _guestIds.Remove(id);
                Forget(id);
            }
            OnChanged?.Invoke();
        }

        private void Forget(string id)
        {
            _remainingHours.Remove(id);
            _pendingRelief.Remove(id);
            _pendingMorale.Remove(id);
            _sessionRelief.Remove(id);
            _sessionMorale.Remove(id);
        }

        private static Mercenary Find(string id) => PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
    }

    /// <summary>
    /// 마을 쉼터 목록.
    /// 온천(청록 지붕의 포렴 찻집): 피로 위주. 1시간마다 피로 -15 · 사기 +1, 1~2시간, 최대 3명, 밤엔 안 받음. 피로 30 이상일 때 피로가 높을수록 자주 온다.
    /// 도박장(주사위 간판의 뒷골목 도박장): 사기 위주. 1시간마다 사기 +4 · 피로 -5, 1~3시간, 최대 4명, 밤에도 받음. 사기 70 미만이거나 피로 30 이상이면 온다.
    /// 마법 연구소(터키석 수정의 마법사 연구소): 마법사 전용. 2~4시간 연구, 4시간마다 공격 +1 영구(최대 10단계, Mercenary.AddResearch).
    ///   1시간마다 피로 +2(훈련과 같음), 최대 2명, 밤엔 안 받음. 지치지 않은 마법사가 25% 확률로 온다.
    /// 성당(장미창과 종탑의 중세 성당): 힐러 전용. 2~4시간 기도, 4시간마다 방어 +1 · 최대 체력 +4 영구(최대 10단계).
    ///   1시간마다 피로 +2 · 사기 +1, 최대 2명, 밤엔 안 받음. 지치지 않은 힐러가 25% 확률로 온다.
    /// </summary>
    public static class RestVenues
    {
        public static readonly RestVenue HotSpring = new RestVenue(
            "온천", "청록 지붕의 포렴 찻집", 0.48f, 3, 1f, 2f, 15f, 1f, false,
            merc => merc.Fatigue < 30 ? 0f : 0.1f + 0.4f * merc.Fatigue / 100f,
            "지친 용병들이 가끔 스스로 찾아와 1~2시간 쉬며 피로를 풉니다. 1시간마다 피로 -15, 사기 +1. (최대 3명)",
            "피로 -15 · 사기 +1");

        public static readonly RestVenue GamblingDen = new RestVenue(
            "도박장", "주사위 간판의 뒷골목 도박장", 0.57f, 4, 1f, 3f, 5f, 4f, true,
            merc => merc.Morale >= 70 && merc.Fatigue < 30 ? 0f
                : 0.1f + 0.4f * Mathf.Max((100f - merc.Morale) / 100f, merc.Fatigue / 100f),
            "사기가 떨어지거나 지친 용병들이 가끔 스스로 찾아와 1~3시간 놀다 갑니다. 1시간마다 사기 +4, 피로 -5. 밤에도 엽니다. (최대 4명)",
            "사기 +4 · 피로 -5");

        public static readonly RestVenue MagicLab = new RestVenue(
            "마법 연구소", "터키석 수정의 마법사 연구소", 0.41f, 2, 2f, 4f, -MercenaryCondition.TrainingFatiguePerHour, 0f, false,
            merc => 0.25f,
            $"마법사들이 가끔 스스로 찾아와 2~4시간 연구합니다. 연구 {Mercenary.ResearchHoursPerBonus:0}시간마다 공격 +1 영구 상승(최대 +{Mercenary.MaxResearchLevel}). " +
            $"1시간마다 피로 +{MercenaryCondition.TrainingFatiguePerHour}. 마법사만 이용할 수 있습니다. (최대 2명)",
            $"{Mercenary.ResearchHoursPerBonus:0}시간마다 공격 +1 · 피로 +{MercenaryCondition.TrainingFatiguePerHour}",
            eligible: merc => merc.Class.Kind == MercenaryClassKind.Mage && merc.CanResearchMore && !merc.IsExhausted,
            onStayed: Research);

        public static readonly RestVenue Cathedral = new RestVenue(
            "성당", "장미창과 종탑의 중세 성당", 0.49f, 2, 2f, 4f, -MercenaryCondition.TrainingFatiguePerHour, 1f, false,
            merc => 0.25f,
            $"힐러들이 가끔 스스로 찾아와 2~4시간 기도합니다. 기도 {Mercenary.ResearchHoursPerBonus:0}시간마다 방어 +1, 최대 체력 +{Mercenary.HealerHealthPerResearchLevel} 영구 상승(최대 {Mercenary.MaxResearchLevel}단계). " +
            $"1시간마다 피로 +{MercenaryCondition.TrainingFatiguePerHour}, 사기 +1. 힐러만 이용할 수 있습니다. (최대 2명)",
            $"{Mercenary.ResearchHoursPerBonus:0}시간마다 방어 +1 · 체력 +{Mercenary.HealerHealthPerResearchLevel} · 피로 +{MercenaryCondition.TrainingFatiguePerHour}",
            eligible: merc => merc.Class.Kind == MercenaryClassKind.Healer && merc.CanResearchMore && !merc.IsExhausted,
            onStayed: Research);

        public static readonly IReadOnlyList<RestVenue> All = new[] { HotSpring, GamblingDen, MagicLab, Cathedral };

        /// <summary>머문 시간만큼 수련 단계가 쌓이는 곳(연구소·성당).</summary>
        public static bool IsStudyVenue(RestVenue venue) => venue == MagicLab || venue == Cathedral;

        /// <summary>연구소·성당에 머문 시간만큼 수련을 쌓고, 단계가 오르면 직업에 맞는 능력치로 알린다.</summary>
        private static void Research(Mercenary merc, float hours)
        {
            int gained = merc.AddResearch(hours);
            if (gained <= 0) return;
            string label = merc.ResearchLabel;
            ToastLog.Show($"{label}: {merc.Name} {merc.ResearchLevel}단계 달성 (총 {merc.ResearchBonusText()})");
            DailyLog.Add($"{merc.Name}이(가) {label}로 능력치가 올랐다. ({label} {merc.ResearchLevel}단계 · {merc.ResearchBonusText()})");
        }

        /// <summary>어느 쉼터든 들어가 있거나 나가면 울린다.</summary>
        public static event Action OnAnyChanged;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(TrainingHall과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            OnAnyChanged = null;
            foreach (var venue in All)
            {
                venue.Clear();
                venue.ClearEvents();
                venue.OnChanged += () => OnAnyChanged?.Invoke();
            }
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnHoursPassed += PassAll;

        private static void PassAll(float hours)
        {
            foreach (var venue in All) venue.Pass(hours);
        }

        /// <summary>어느 쉼터에든 들어가 있는지.</summary>
        public static bool IsResting(Mercenary merc) => Find(merc) != null;

        /// <summary>merc가 들어가 있는 쉼터. 없으면 null.</summary>
        public static RestVenue Find(Mercenary merc) => All.FirstOrDefault(v => v.IsInside(merc));
    }
}
