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
    /// 마을 장식에서 하는 일: 쉼터 장식(벤치·모닥불·분수 — BuildingCatalog.Entry.Seats)에 앉거나 둘러서서 쉬고,
    /// 허수아비(TrainingDummy)를 쳐서 조금 훈련한다. 자리는 VillagePartyPresenter가 지은 장식으로 정해 넘기고(SetSpots),
    /// 용병이 스스로 가는 굴림도 거기서 한다. 게임 시간이 흐르면(GameClock.OnHoursPassed) 효과를 주고 시간이 다 되면 끝낸다.
    /// - 쉼터: 피로 −RestFatiguePerHour/시간, 같은 쉼터에 함께 있으면 친밀도(Affinity.RestGainPerHour).
    /// - 허수아비: 성장 TrainingGrowthPerHour × DummyGrowthRatio/시간, 피로 +TrainingFatiguePerHour/시간.
    /// 허수아비가 있으면 훈련소 성장도 조금 빨라진다(TrainingHallBonus). 짧은 일이라 저장하지 않는다.
    /// </summary>
    public static class DecorActivities
    {
        public enum Kind { Rest, Training }

        public const float MinSessionHours = 1f;
        public const float MaxSessionHours = 2f;
        private const float RestFatiguePerHour = 3f;
        private const float DummyGrowthRatio = 0.4f;
        private const float HallBonusPerDummy = 0.1f;
        private const int MaxHallBonusDummies = 3;

        /// <summary>장식 하나의 자리: 서는 곳(Seats개, 허수아비는 1)과 바라볼 방향.</summary>
        public class Spot
        {
            public string Key;
            public Kind Kind;
            public Vector2 Center;
            public Vector2[] Seats;
            public bool[] FaceRight;
        }

        private class Session
        {
            public string SpotKey;
            public int Seat;
            public float HoursLeft;
            public readonly List<string> Gains = new List<string>();
        }

        private static readonly List<Spot> _spots = new List<Spot>();
        private static readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(); // 용병 id →
        private static readonly Dictionary<string, float> _pendingFatigue = new Dictionary<string, float>();

        /// <summary>누가 오고 가서 인형을 다시 맞춰야 할 때(VillagePartyPresenter.Refresh).</summary>
        public static event Action OnChanged;

        public static IReadOnlyList<Spot> Spots => _spots;

        /// <summary>훈련소 성장 배율: 허수아비 하나마다 +10%(MaxHallBonusDummies개까지).</summary>
        public static float TrainingHallBonus => 1f + HallBonusPerDummy * Mathf.Min(MaxHallBonusDummies, _spots.Count(s => s.Kind == Kind.Training));

        /// <summary>불러오기: 저장하지 않는 장식 활동(앉아 있는 사람·피로 소수점)을 비운다 — 옛 게임 용병이 자리를 차지하지 않게.</summary>
        public static void ResetTransient()
        {
            if (_sessions.Count == 0 && _pendingFatigue.Count == 0) return;
            _sessions.Clear();
            _pendingFatigue.Clear();
            OnChanged?.Invoke();
        }

        /// <summary>마을 장식 자리를 새로 정한다(마을을 고칠 때마다). 사라진 자리에 있던 사람은 끝낸다.</summary>
        public static void SetSpots(IEnumerable<Spot> spots)
        {
            _spots.Clear();
            _spots.AddRange(spots);
            var gone = _sessions.Where(kv => _spots.All(s => s.Key != kv.Value.SpotKey)).Select(kv => kv.Key).ToList();
            foreach (var id in gone) Forget(id);
            if (gone.Count > 0) OnChanged?.Invoke();
        }

        public static bool IsBusy(Mercenary merc) => merc != null && _sessions.ContainsKey(merc.Id);

        /// <summary>쉬는 중이면 Rest, 훈련 중이면 Training, 아니면 null.</summary>
        public static Kind? KindOf(Mercenary merc) =>
            merc != null && _sessions.TryGetValue(merc.Id, out var s) ? SpotOf(s)?.Kind : null;

        /// <summary>이 용병이 서 있을 곳(자리 위치·바라볼 방향). 없으면 false.</summary>
        public static bool TryGetSeat(Mercenary merc, out Vector2 seat, out bool faceRight)
        {
            seat = default;
            faceRight = true;
            if (merc == null || !_sessions.TryGetValue(merc.Id, out var session)) return false;
            var spot = SpotOf(session);
            if (spot == null) return false;
            seat = spot.Seats[session.Seat];
            faceRight = spot.FaceRight[session.Seat];
            return true;
        }

        /// <summary>그 자리의 빈자리 번호(없으면 −1).</summary>
        public static int FreeSeat(Spot spot)
        {
            for (int i = 0; i < spot.Seats.Length; i++)
                if (!_sessions.Values.Any(s => s.SpotKey == spot.Key && s.Seat == i)) return i;
            return -1;
        }

        /// <summary>마을에서 놀고 있는(원정·훈련소·쉼터 건물·병원·이미 장식에 있음 아님) 산 용병인가.</summary>
        public static bool IsFree(Mercenary merc) =>
            merc != null && merc.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(merc) && !TrainingHall.IsTraining(merc)
            && !Hospital.IsAdmitted(merc) && !RestVenues.IsResting(merc) && !IsBusy(merc);

        /// <summary>장식에 가서 hours 동안 쉬거나 훈련한다. 빈자리가 없거나 바쁘면 false.</summary>
        public static bool TryStart(Mercenary merc, Spot spot, float hours)
        {
            if (spot == null || !IsFree(merc) || GameClock.IsNight) return false;
            if (spot.Kind == Kind.Training && (!merc.CanGrow || merc.IsExhausted || merc.HasAilment)) return false;
            int seat = FreeSeat(spot);
            if (seat < 0) return false;
            _sessions[merc.Id] = new Session { SpotKey = spot.Key, Seat = seat, HoursLeft = Mathf.Max(0.1f, hours) };
            OnChanged?.Invoke();
            return true;
        }

        public static void End(Mercenary merc)
        {
            if (merc == null || !_sessions.ContainsKey(merc.Id)) return;
            Forget(merc.Id);
            OnChanged?.Invoke();
        }

        private static Spot SpotOf(Session session) => _spots.FirstOrDefault(s => s.Key == session.SpotKey);

        private static void Pass(float hours)
        {
            if (_sessions.Count == 0) return;
            var finished = new List<string>();
            var restedBySpot = new Dictionary<string, List<(Mercenary merc, float hours)>>();
            foreach (var kv in _sessions.ToList())
            {
                var merc = PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == kv.Key);
                var session = kv.Value;
                var spot = SpotOf(session);
                if (merc == null || !merc.IsAlive || spot == null || ExpeditionLog.Instance.IsOnExpedition(merc))
                {
                    finished.Add(kv.Key);
                    continue;
                }
                float spent = Mathf.Min(hours, session.HoursLeft);
                session.HoursLeft -= hours;
                if (session.HoursLeft <= 0f) finished.Add(kv.Key);
                if (spent <= 0f) continue;

                if (spot.Kind == Kind.Rest)
                {
                    AddFatigue(merc, -RestFatiguePerHour * spent);
                    if (!restedBySpot.TryGetValue(spot.Key, out var list)) restedBySpot[spot.Key] = list = new List<(Mercenary, float)>();
                    list.Add((merc, spent));
                }
                else
                {
                    AddFatigue(merc, MercenaryCondition.TrainingFatiguePerHour * spent);
                    var gains = merc.AddGrowth(spent * GrowthTuning.TrainingGrowthPerHour * DummyGrowthRatio);
                    if (gains.Count > 0)
                    {
                        session.Gains.AddRange(gains);
                        ToastLog.Show($"허수아비 훈련: {merc.Name} {string.Join(", ", gains)}");
                    }
                }
            }

            // 같은 쉼터에서 함께 쉰 시간만큼 친해진다
            foreach (var list in restedBySpot.Values)
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        Affinity.AddAmong(new[] { list[i].merc, list[j].merc }, Mathf.Min(list[i].hours, list[j].hours) * Affinity.RestGainPerHour);

            if (finished.Count == 0) return;
            foreach (var id in finished)
            {
                if (_sessions.TryGetValue(id, out var session) && session.Gains.Count > 0)
                {
                    var merc = PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
                    if (merc != null) DailyLog.Add($"{Josa.IGa(merc.Name)} 허수아비를 치며 강해졌다 ({string.Join(", ", session.Gains)}).");
                }
                Forget(id);
            }
            OnChanged?.Invoke();
        }

        // 피로는 잘게 흐르는 시간에 반올림으로 사라지지 않게 소수점을 모았다가 정수만 넣는다(TrainingHall과 같은 방식)
        private static void AddFatigue(Mercenary merc, float amount)
        {
            _pendingFatigue.TryGetValue(merc.Id, out float pending);
            pending += amount;
            int whole = pending >= 0f ? Mathf.FloorToInt(pending) : Mathf.CeilToInt(pending);
            _pendingFatigue[merc.Id] = pending - whole;
            if (whole != 0) merc.AddFatigue(whole);
        }

        private static void Forget(string id)
        {
            _sessions.Remove(id);
            _pendingFatigue.Remove(id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _spots.Clear();
            _sessions.Clear();
            _pendingFatigue.Clear();
            OnChanged = null;
        }

        // GameClock.ResetForPlaySession이 이벤트를 비운 뒤에 구독한다(TrainingHall과 같은 방식)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnHoursPassed += Pass;
    }
}
