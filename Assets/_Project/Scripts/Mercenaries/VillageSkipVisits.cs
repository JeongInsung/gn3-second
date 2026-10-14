using System.Collections.Generic;
using System.Linq;
using GN3.Quests;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// '진행'으로 시간을 건너뛸 때도 마을 용병이 쉼터·훈련소에 가게 한다.
    /// 실시간에는 VillagePartyPresenter가 걸어 다니는 용병마다 실제 초 간격으로 방문을 굴리지만, 건너뛰면 그 시간이 없어
    /// 밤 휴식만 받고 온천·도박장·훈련·연구·기도를 통째로 놓쳤다. GameClock.OnSkipStep(1시간마다)에서 같은 평균 간격
    /// (ConditionTuning.VenueRollHours · TrainingRollHours)과 같은 확률로 굴려 등록만 한다. 머무는 동안의 효과는 기존처럼
    /// RestVenues·TrainingHall이 OnHoursPassed로 준다. 걸어 들어가는 연출은 없다(화면이 어두운 동안 바로 안에 있게 된다).
    /// 갈 수 있는 건물은 VillagePartyPresenter가 마을에서 찾은 것만(SetBuildings) — 실시간과 같다. 마을이 없으면 아무 데도 안 간다.
    /// </summary>
    public static class VillageSkipVisits
    {
        private static readonly System.Random Rng = new System.Random();
        private static readonly List<RestVenue> Venues = new List<RestVenue>();
        private static bool _hasTrainingHall;

        /// <summary>마을에 그림이 있는 쉼터·훈련소를 알린다(VillagePartyPresenter.Start / OnDestroy에서 null).</summary>
        public static void SetBuildings(IEnumerable<RestVenue> venues, bool hasTrainingHall)
        {
            Venues.Clear();
            if (venues != null) Venues.AddRange(venues);
            _hasTrainingHall = hasTrainingHall;
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnSkipStep += Roll;

        private static void Roll(float hours)
        {
            if (GameClock.CurrentHour < ConditionTuning.VillageOutHour) return; // 아직 여관 안에 있는 시간
            var idle = PlayerParty.Instance.Members.Where(IsFree).OrderBy(_ => Rng.Next()).ToList();
            foreach (var merc in idle)
            {
                if (_hasTrainingHall && TrainingHall.WantsToTrain(merc) // 다쳤거나 지쳤으면 훈련 대신 쉼터 굴림으로
                    && Rng.NextDouble() < hours / ConditionTuning.TrainingRollHours && Rng.NextDouble() < ConditionTuning.TrainingVisitChance
                    && TrainingHall.TryAdd(merc, Random.Range(TrainingHall.MinSessionHours, TrainingHall.MaxSessionHours)))
                    continue;
                if (Rng.NextDouble() >= hours / ConditionTuning.VenueRollHours) continue;
                var venue = Venues.OrderBy(_ => Rng.Next())
                    .FirstOrDefault(v => v.IsOpen && !v.IsFull && Rng.NextDouble() < v.VisitChance(merc));
                venue?.TryAdd(merc, Random.Range(venue.MinSessionHours, venue.MaxSessionHours));
            }
        }

        /// <summary>마을에서 걸어 다니는 사람(파견·훈련·입원·쉼터 중이 아님).</summary>
        private static bool IsFree(Mercenary merc) =>
            merc.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(merc) && !TrainingHall.IsTraining(merc)
            && !Hospital.IsAdmitted(merc) && !RestVenues.IsResting(merc);
    }
}
