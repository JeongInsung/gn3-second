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
    /// 마을 온천(청록 지붕의 포렴 찻집 건물). 피로가 쌓인 용병이 마을을 돌아다니다 가끔 스스로 찾아와(VillagePartyPresenter가 무작위로 정함)
    /// 게임 시간 1~2시간 몸을 담그고 나온다. 안에 있는 동안 1시간마다 피로 -15, 사기 +1. 무료, 최대 3명, 밤(GameClock.IsNight)엔 새로 들어가지 않는다.
    /// 피로가 높을수록 자주 온다(VisitChance). 온천 중에는 안에 있어 파견·훈련·마을 산책을 하지 않는다.
    /// 시간은 GameClock.OnHoursPassed로 받는다(TrainingHall과 같은 방식). 소수점 피로·사기는 사람마다 쌓아 두었다가 1이 될 때 더한다.
    /// 같은 시간에 몸을 담근 사람끼리는 함께 있던 시간만큼 친밀도가 오른다(Affinity).
    /// </summary>
    public static class HotSpring
    {
        public const int Capacity = 3;
        public const float FatigueReliefPerHour = 15f;
        public const float MoralePerHour = 1f;
        public const float MinSessionHours = 1f;
        public const float MaxSessionHours = 2f;
        public const int MinFatigueToVisit = 30;

        private static readonly List<string> _batherIds = new List<string>();
        private static readonly Dictionary<string, float> _remainingHours = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _pendingRelief = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _pendingMorale = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _sessionRelief = new Dictionary<string, int>();

        public static event Action OnChanged;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(TrainingHall과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _batherIds.Clear();
            _remainingHours.Clear();
            _pendingRelief.Clear();
            _pendingMorale.Clear();
            _sessionRelief.Clear();
            OnChanged = null;
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnHoursPassed += Soak;

        /// <summary>지금 온천에 있는 용병(들어온 순서). 파티에서 빠졌거나 죽은 사람은 빠진다.</summary>
        public static IReadOnlyList<Mercenary> Bathers
        {
            get
            {
                Cleanup();
                return _batherIds.Select(Find).Where(m => m != null).ToList();
            }
        }

        public static bool IsBathing(Mercenary merc) => merc != null && _batherIds.Contains(merc.Id);

        public static bool IsFull
        {
            get
            {
                Cleanup();
                return _batherIds.Count >= Capacity;
            }
        }

        /// <summary>온천에 갈 수 있는 사람: 살아 있고, 피로가 있고, 파견·훈련·입원·온천 중이 아님. 지친 사람도 간다.</summary>
        public static bool CanBathe(Mercenary merc) =>
            merc != null && merc.IsAlive && merc.Fatigue > 0
            && !ExpeditionLog.Instance.IsOnExpedition(merc) && !IsBathing(merc) && !TrainingHall.IsTraining(merc) && !Hospital.IsAdmitted(merc);

        /// <summary>스스로 온천에 갈 확률(한 번 굴릴 때). 피로 30 미만이면 0, 그 이상은 피로가 높을수록 크다(피로 100 → 50%).</summary>
        public static float VisitChance(Mercenary merc) =>
            merc == null || merc.Fatigue < MinFatigueToVisit ? 0f : 0.1f + 0.4f * merc.Fatigue / 100f;

        /// <summary>온천에 들어간다(hours = 이번에 머물 게임 시간). 밤이거나 정원이 차면 거절.</summary>
        public static bool TryAdd(Mercenary merc, float hours)
        {
            if (!CanBathe(merc) || IsFull || GameClock.IsNight) return false;
            _batherIds.Add(merc.Id);
            _remainingHours[merc.Id] = Mathf.Max(0.1f, hours);
            _sessionRelief[merc.Id] = 0;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>온천에서 내보낸다(정리용).</summary>
        public static bool Remove(Mercenary merc)
        {
            if (merc == null || !_batherIds.Remove(merc.Id)) return false;
            Forget(merc.Id);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>이번에 남은 시간(게임 시간).</summary>
        public static float RemainingHours(Mercenary merc) =>
            merc != null && _remainingHours.TryGetValue(merc.Id, out float h) ? h : 0f;

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤). 남은 시간은 저장하지 않으므로 사람마다 1~2시간을 새로 정한다.</summary>
        public static void Restore(IEnumerable<string> ids)
        {
            _batherIds.Clear();
            _remainingHours.Clear();
            _pendingRelief.Clear();
            _pendingMorale.Clear();
            _sessionRelief.Clear();
            if (ids != null)
                foreach (var id in ids)
                {
                    if (_batherIds.Count >= Capacity || _batherIds.Contains(id) || Find(id) == null) continue;
                    _batherIds.Add(id);
                    _remainingHours[id] = UnityEngine.Random.Range(MinSessionHours, MaxSessionHours);
                    _sessionRelief[id] = 0;
                }
            OnChanged?.Invoke();
        }

        /// <summary>저장용: 온천에 있는 용병 id.</summary>
        public static List<string> SaveIds()
        {
            Cleanup();
            return new List<string>(_batherIds);
        }

        private static void Soak(float hours)
        {
            Cleanup();
            var finished = new List<Mercenary>();
            var soakedHours = new List<(Mercenary merc, float hours)>(); // 같이 있던 시간만큼 친밀도
            foreach (var merc in _batherIds.Select(Find).Where(m => m != null).ToList())
            {
                // 이번에 남은 시간만큼만 쉰다(큰 건너뛰기에도 1~2시간 몫).
                float remaining = _remainingHours.TryGetValue(merc.Id, out float r) ? r : 0f;
                float soaked = Mathf.Min(hours, remaining);
                _remainingHours[merc.Id] = remaining - hours;
                if (remaining - hours <= 0f) finished.Add(merc);
                if (soaked <= 0f) continue;
                soakedHours.Add((merc, soaked));

                int relief = TakeWhole(_pendingRelief, merc.Id, soaked * FatigueReliefPerHour);
                if (relief > 0)
                {
                    int before = merc.Fatigue;
                    merc.AddFatigue(-relief);
                    _sessionRelief[merc.Id] = (_sessionRelief.TryGetValue(merc.Id, out int s) ? s : 0) + (before - merc.Fatigue);
                }
                int morale = TakeWhole(_pendingMorale, merc.Id, soaked * MoralePerHour);
                if (morale > 0) merc.AddMorale(morale);
            }

            for (int i = 0; i < soakedHours.Count; i++)
                for (int j = i + 1; j < soakedHours.Count; j++)
                    Affinity.AddAmong(new[] { soakedHours[i].merc, soakedHours[j].merc },
                        Mathf.Min(soakedHours[i].hours, soakedHours[j].hours) * Affinity.HotSpringGainPerHour);

            if (finished.Count == 0) return;
            foreach (var merc in finished)
            {
                int relief = _sessionRelief.TryGetValue(merc.Id, out int s) ? s : 0;
                _batherIds.Remove(merc.Id);
                Forget(merc.Id);
                ToastLog.Show($"{merc.Name} 온천에서 나왔다 (피로 -{relief})");
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

        /// <summary>해고·사망·파견(불러오기 등)으로 더는 온천에 있을 수 없는 사람을 뺀다.</summary>
        private static void Cleanup()
        {
            var gone = _batherIds.Where(id =>
            {
                var merc = Find(id);
                return merc == null || !merc.IsAlive || ExpeditionLog.Instance.IsOnExpedition(merc);
            }).ToList();
            if (gone.Count == 0) return;
            foreach (var id in gone)
            {
                _batherIds.Remove(id);
                Forget(id);
            }
            OnChanged?.Invoke();
        }

        private static void Forget(string id)
        {
            _remainingHours.Remove(id);
            _pendingRelief.Remove(id);
            _pendingMorale.Remove(id);
            _sessionRelief.Remove(id);
        }

        private static Mercenary Find(string id) => PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
    }
}
