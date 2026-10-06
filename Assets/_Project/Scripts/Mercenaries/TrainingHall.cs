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
    /// 훈련 중에는 1시간마다 경험치 5를 얻어 레벨이 오른다(레벨업이 곧 능력치 상승). 최대 3명, 밤(GameClock.IsNight)엔 새로 들어가지 않는다.
    /// 훈련 중에는 훈련소 안에 있어 파견·마을 산책을 하지 않는다. 시간은 GameClock.OnHoursPassed로 받는다
    /// (저절로 흐르기·진행·건너뛰기 모두). 소수점 경험치는 사람마다 쌓아 두었다가 1이 될 때 더한다.
    /// </summary>
    public static class TrainingHall
    {
        public const int Capacity = 3;
        public const float XpPerHour = 5f;
        public const float MinSessionHours = 1f;
        public const float MaxSessionHours = 3f;

        private static readonly List<string> _traineeIds = new List<string>();
        private static readonly Dictionary<string, float> _pendingXp = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _remainingHours = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _sessionXp = new Dictionary<string, int>();

        public static event Action OnChanged;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _traineeIds.Clear();
            _pendingXp.Clear();
            _remainingHours.Clear();
            _sessionXp.Clear();
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

        /// <summary>훈련하러 갈 수 있는 사람: 살아 있고, 파견 중이 아니고, 아직 훈련 중이 아니고, 최대 레벨이 아님.</summary>
        public static bool CanTrain(Mercenary merc) =>
            merc != null && merc.IsAlive && merc.Level < Mercenary.MaxLevel && !merc.IsExhausted
            && !ExpeditionLog.Instance.IsOnExpedition(merc) && !IsTraining(merc);

        /// <summary>훈련을 시작한다(hours = 이번에 훈련할 게임 시간). 밤이거나 정원이 차면 거절.</summary>
        public static bool TryAdd(Mercenary merc, float hours)
        {
            if (!CanTrain(merc) || IsFull || GameClock.IsNight) return false;
            _traineeIds.Add(merc.Id);
            _remainingHours[merc.Id] = Mathf.Max(0.1f, hours);
            _sessionXp[merc.Id] = 0;
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

        /// <summary>다음 레벨까지 남은 훈련 시간(게임 시간). 최대 레벨이면 0.</summary>
        public static float HoursToNextLevel(Mercenary merc)
        {
            if (merc.Level >= Mercenary.MaxLevel) return 0f;
            _pendingXp.TryGetValue(merc.Id, out float pending);
            return Mathf.Max(0f, (merc.XpToNext - merc.Experience - pending) / XpPerHour);
        }

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤). 남은 시간은 저장하지 않으므로 사람마다 1~3시간을 새로 정한다.</summary>
        public static void Restore(IEnumerable<string> ids)
        {
            _traineeIds.Clear();
            _pendingXp.Clear();
            _remainingHours.Clear();
            _sessionXp.Clear();
            if (ids != null)
                foreach (var id in ids)
                {
                    if (_traineeIds.Count >= Capacity || _traineeIds.Contains(id) || Find(id) == null) continue;
                    _traineeIds.Add(id);
                    _remainingHours[id] = UnityEngine.Random.Range(MinSessionHours, MaxSessionHours);
                    _sessionXp[id] = 0;
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
            foreach (var merc in _traineeIds.Select(Find).Where(m => m != null).ToList())
            {
                // 이번 훈련의 남은 시간만큼만 경험치를 준다(큰 건너뛰기에도 1~3시간 몫).
                float remaining = _remainingHours.TryGetValue(merc.Id, out float r) ? r : 0f;
                float trained = Mathf.Min(hours, remaining);
                _remainingHours[merc.Id] = remaining - hours;
                if (remaining - hours <= 0f) finished.Add(merc);
                if (trained > 0f) merc.AddFatigue(Mathf.RoundToInt(trained * MercenaryCondition.TrainingFatiguePerHour));
                if (merc.Level >= Mercenary.MaxLevel || trained <= 0f) continue;

                _pendingXp.TryGetValue(merc.Id, out float pending);
                pending += trained * XpPerHour;
                int whole = Mathf.FloorToInt(pending);
                _pendingXp[merc.Id] = pending - whole;
                if (whole <= 0) continue;

                _sessionXp[merc.Id] = (_sessionXp.TryGetValue(merc.Id, out int s) ? s : 0) + whole;
                int gained = merc.AddExperience(whole);
                if (gained > 0)
                {
                    ToastLog.Show($"훈련: {merc.Name} Lv {merc.Level} 달성!");
                    DailyLog.Add($"{merc.Name}이(가) 훈련소에서 Lv {merc.Level}이(가) 되었다.");
                }
            }

            if (finished.Count == 0) return;
            foreach (var merc in finished)
            {
                int xp = _sessionXp.TryGetValue(merc.Id, out int s) ? s : 0;
                _traineeIds.Remove(merc.Id);
                Forget(merc.Id);
                ToastLog.Show($"{merc.Name} 훈련을 마쳤다 (+{xp} 경험치)");
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
            _pendingXp.Remove(id);
            _remainingHours.Remove(id);
            _sessionXp.Remove(id);
        }

        private static Mercenary Find(string id) => PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
    }
}
