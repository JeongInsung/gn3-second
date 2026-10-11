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
    /// 마을 훈련소. 용병들이 마을을 돌아다니다 가끔 스스로 찾아와(VillagePartyPresenter가 무작위로 정함) 게임 시간 1~3시간 훈련하고 나온다.
    /// 훈련 중에는 시간만큼 성장이 쌓여(GrowthTuning.TrainingGrowthPerHour × 나이 배율) 직업 성향대로 능력치가 오른다. 최대 3명, 밤(GameClock.IsNight)엔 새로 들어가지 않는다.
    /// 훈련 중에는 훈련소 안에 있어 파견·마을 산책을 하지 않는다. 시간은 GameClock.OnHoursPassed로 받는다
    /// (저절로 흐르기·진행·건너뛰기 모두). 성장의 소수점은 용병(Mercenary.GrowthProgress)에 쌓인다. 45세 이상(성장 멈춤)은 훈련하지 않는다.
    /// 같은 시간에 훈련한 사람끼리는 함께 훈련한 시간만큼 친밀도가 오른다(Affinity).
    /// </summary>
    public static class TrainingHall
    {
        public const int Capacity = 3;
        public const float MinSessionHours = 1f;
        public const float MaxSessionHours = 3f;

        private static readonly List<string> _traineeIds = new List<string>();
        // 피로는 시간이 잘게 흐를 때(실시간 매 프레임) 반올림으로 사라지지 않게 소수점을 쌓아 두었다가 정수만 더한다(RestVenue와 같은 방식).
        private static readonly Dictionary<string, float> _pendingFatigue = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _remainingHours = new Dictionary<string, float>();
        private static readonly Dictionary<string, List<string>> _sessionGains = new Dictionary<string, List<string>>(); // 이번 훈련에서 오른 능력치

        public static event Action OnChanged;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _traineeIds.Clear();
            _pendingFatigue.Clear();
            _remainingHours.Clear();
            _sessionGains.Clear();
            OnChanged = null;
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnHoursPassed += Train;

        /// <summary>지금 훈련 중인 용병(들어온 순서). 파티에서 빠졌거나 죽은 사람은 빠진다.</summary>
        public static IReadOnlyList<Mercenary> Trainees
        {
            get
            {
                Cleanup();
                return _traineeIds.Select(Find).Where(m => m != null).ToList();
            }
        }

        public static bool IsTraining(Mercenary merc) => merc != null && _traineeIds.Contains(merc.Id);

        public static bool IsFull
        {
            get
            {
                Cleanup();
                return _traineeIds.Count >= Capacity;
            }
        }

        /// <summary>훈련하러 갈 수 있는 사람: 살아 있고, 파견 중이 아니고, 아직 훈련 중이 아니고, 최대 레벨이 아니고, 부상·질병이 없음(그러면 병원으로).</summary>
        public static bool CanTrain(Mercenary merc) =>
            merc != null && merc.IsAlive && merc.CanGrow && !merc.IsExhausted && !merc.HasAilment
            && !ExpeditionLog.Instance.IsOnExpedition(merc) && !IsTraining(merc) && !Hospital.IsAdmitted(merc) && !RestVenues.IsResting(merc);

        /// <summary>훈련을 시작한다(hours = 이번에 훈련할 게임 시간). 밤이거나 정원이 차면 거절.</summary>
        public static bool TryAdd(Mercenary merc, float hours)
        {
            if (!CanTrain(merc) || IsFull || GameClock.IsNight) return false;
            _traineeIds.Add(merc.Id);
            _remainingHours[merc.Id] = Mathf.Max(0.1f, hours);
            _sessionGains[merc.Id] = new List<string>();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>훈련을 끝내고 내보낸다(시간이 다 됐을 때·정리용).</summary>
        public static bool Remove(Mercenary merc)
        {
            if (merc == null || !_traineeIds.Remove(merc.Id)) return false;
            Forget(merc.Id);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>이번 훈련에서 남은 시간(게임 시간).</summary>
        public static float RemainingHours(Mercenary merc) =>
            merc != null && _remainingHours.TryGetValue(merc.Id, out float h) ? h : 0f;

        /// <summary>다음 성장까지 남은 훈련 시간(게임 시간). 성장이 멈췄으면 0.</summary>
        public static float HoursToNextGrowth(Mercenary merc)
        {
            if (!merc.CanGrow) return 0f;
            return Mathf.Max(0f, (1f - merc.GrowthProgress) / (GrowthTuning.TrainingGrowthPerHour * merc.GrowthRate));
        }

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤). 남은 시간은 저장하지 않으므로 사람마다 1~3시간을 새로 정한다.</summary>
        public static void Restore(IEnumerable<string> ids)
        {
            _traineeIds.Clear();
            _pendingFatigue.Clear();
            _remainingHours.Clear();
            _sessionGains.Clear();
            if (ids != null)
                foreach (var id in ids)
                {
                    if (_traineeIds.Count >= Capacity || _traineeIds.Contains(id) || Find(id) == null) continue;
                    _traineeIds.Add(id);
                    _remainingHours[id] = UnityEngine.Random.Range(MinSessionHours, MaxSessionHours);
                    _sessionGains[id] = new List<string>();
                }
            OnChanged?.Invoke();
        }

        /// <summary>저장용: 훈련 중인 용병 id.</summary>
        public static List<string> SaveIds()
        {
            Cleanup();
            return new List<string>(_traineeIds);
        }

        private static void Train(float hours)
        {
            Cleanup();
            var finished = new List<Mercenary>();
            var trainedHours = new List<(Mercenary merc, float hours)>(); // 같이 훈련한 시간만큼 친밀도
            foreach (var merc in _traineeIds.Select(Find).Where(m => m != null).ToList())
            {
                // 이번 훈련의 남은 시간만큼만 경험치를 준다(큰 건너뛰기에도 1~3시간 몫).
                float remaining = _remainingHours.TryGetValue(merc.Id, out float r) ? r : 0f;
                float trained = Mathf.Min(hours, remaining);
                _remainingHours[merc.Id] = remaining - hours;
                if (remaining - hours <= 0f) finished.Add(merc);
                if (trained > 0f)
                {
                    _pendingFatigue.TryGetValue(merc.Id, out float fatigue);
                    fatigue += trained * MercenaryCondition.TrainingFatiguePerHour;
                    int wholeFatigue = Mathf.FloorToInt(fatigue);
                    _pendingFatigue[merc.Id] = fatigue - wholeFatigue;
                    if (wholeFatigue > 0) merc.AddFatigue(wholeFatigue);
                    trainedHours.Add((merc, trained));
                }
                if (trained <= 0f) continue;

                var gains = merc.AddGrowth(trained * GrowthTuning.TrainingGrowthPerHour);
                if (gains.Count == 0) continue;
                if (!_sessionGains.TryGetValue(merc.Id, out var session)) _sessionGains[merc.Id] = session = new List<string>();
                session.AddRange(gains);
                ToastLog.Show($"훈련: {merc.Name} {string.Join(", ", gains)}");
                DailyLog.Add($"{merc.Name}이(가) 훈련소에서 강해졌다 ({string.Join(", ", gains)}).");
            }

            for (int i = 0; i < trainedHours.Count; i++)
                for (int j = i + 1; j < trainedHours.Count; j++)
                    Affinity.AddAmong(new[] { trainedHours[i].merc, trainedHours[j].merc },
                        Mathf.Min(trainedHours[i].hours, trainedHours[j].hours) * Affinity.TrainingGainPerHour);

            if (finished.Count == 0) return;
            foreach (var merc in finished)
            {
                string gained = _sessionGains.TryGetValue(merc.Id, out var session) && session.Count > 0 ? string.Join(", ", session) : "오른 능력치 없음";
                _traineeIds.Remove(merc.Id);
                Forget(merc.Id);
                DailyLog.Add($"{merc.Name}이(가) 훈련을 마쳤다 ({gained})."); // 이동 알림은 띄우지 않는다
            }
            OnChanged?.Invoke();
        }

        /// <summary>해고·사망·파견(불러오기 등)으로 더는 훈련할 수 없는 사람을 뺀다.</summary>
        private static void Cleanup()
        {
            var gone = _traineeIds.Where(id =>
            {
                var merc = Find(id);
                return merc == null || !merc.IsAlive || ExpeditionLog.Instance.IsOnExpedition(merc);
            }).ToList();
            if (gone.Count == 0) return;
            foreach (var id in gone)
            {
                _traineeIds.Remove(id);
                Forget(id);
            }
            OnChanged?.Invoke();
        }

        private static void Forget(string id)
        {
            _pendingFatigue.Remove(id);
            _remainingHours.Remove(id);
            _sessionGains.Remove(id);
        }

        private static Mercenary Find(string id) => PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
    }
}
