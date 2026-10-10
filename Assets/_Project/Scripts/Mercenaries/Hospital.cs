using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 마을 병원. 부상·질병(Ailments)이 있는 용병이 마을에 있으면 스스로 입원한다(중한 사람 먼저, 최대 3명, 밤에도 받음).
    /// 입원 중에는 상태이상마다 시간당 5% × 에셋의 hospitalSpeed씩 나아(기본 완치 20시간) 다 나으면 퇴원하며, 입원한 시간 × 5G를 치료비로 낸다.
    /// 골드가 모자라면 돈을 받지 않고 내보낸다. 입원 중에는 병원 안에 있어 파견·훈련·마을 산책을 하지 않는다.
    /// 시간은 GameClock.OnHoursPassed로 받는다(TrainingHall과 같은 방식). 병원 건물이 마을에 있을 때만(Available) 받는다.
    /// </summary>
    public static class Hospital
    {
        public const int Capacity = 3;
        public const float ProgressPerHour = 0.05f;
        public const int FeePerHour = 5;

        /// <summary>마을에 병원 건물이 있으면 MainMenuBootstrapper가 켠다. 꺼져 있으면 아무도 입원하지 않는다.</summary>
        public static bool Available { get; set; }

        private static readonly List<string> _patientIds = new List<string>();
        private static readonly Dictionary<string, float> _admittedHours = new Dictionary<string, float>();

        public static event Action OnChanged;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(TrainingHall과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _patientIds.Clear();
            _admittedHours.Clear();
            Available = false;
            OnChanged = null;
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnHoursPassed += Treat;

        /// <summary>지금 입원 중인 용병(들어온 순서).</summary>
        public static IReadOnlyList<Mercenary> Patients
        {
            get
            {
                Cleanup();
                return _patientIds.Select(Find).Where(m => m != null).ToList();
            }
        }

        /// <summary>자리가 없어 마을에서 기다리는 환자(중한 사람 먼저).</summary>
        public static IReadOnlyList<Mercenary> Waiting =>
            PlayerParty.Instance.Members.Where(CanAdmit).OrderByDescending(m => m.IsSeverelyAiling).ToList();

        public static bool IsAdmitted(Mercenary merc) => merc != null && _patientIds.Contains(merc.Id);

        /// <summary>입원할 수 있는 사람: 살아 있고, 상태이상이 있고, 파견·훈련·입원 중이 아님.</summary>
        public static bool CanAdmit(Mercenary merc) =>
            merc != null && merc.IsAlive && merc.HasAilment && !IsAdmitted(merc)
            && !ExpeditionLog.Instance.IsOnExpedition(merc) && !TrainingHall.IsTraining(merc) && !HotSpring.IsBathing(merc);

        /// <summary>지금까지 쌓인 치료비(퇴원할 때 낸다).</summary>
        public static int Fee(Mercenary merc) =>
            merc != null && _admittedHours.TryGetValue(merc.Id, out float h) ? Mathf.CeilToInt(h * FeePerHour) : 0;

        /// <summary>남은 치료 시간(가장 오래 걸리는 상태이상 기준, 게임 시간).</summary>
        public static float RemainingHours(Mercenary merc) =>
            merc == null || !merc.HasAilment ? 0f : merc.Ailments.Max(a => (1f - a.Progress) / (ProgressPerHour * a.Def.hospitalSpeed));

        /// <summary>빈자리에 기다리는 환자를 들인다. 시간이 흐를 때 저절로 불리고, 처음 켜질 때도 부를 수 있다.</summary>
        public static void AdmitWaiting()
        {
            if (!Available) return;
            Cleanup();
            bool changed = false;
            foreach (var merc in Waiting)
            {
                if (_patientIds.Count >= Capacity) break;
                _patientIds.Add(merc.Id);
                _admittedHours[merc.Id] = 0f;
                ToastLog.Show($"{merc.Name}이(가) 병원에 입원했다 ({merc.AilmentSummary()})");
                changed = true;
            }
            if (changed) OnChanged?.Invoke();
        }

        /// <summary>저장 파일에서 불러올 때(파티·상태이상 복원 뒤). 쌓인 치료비는 저장하지 않아 0부터 다시 센다.</summary>
        public static void Restore(IEnumerable<string> ids)
        {
            _patientIds.Clear();
            _admittedHours.Clear();
            if (ids != null)
                foreach (var id in ids)
                {
                    var merc = Find(id);
                    if (_patientIds.Count >= Capacity || _patientIds.Contains(id) || merc == null || !merc.HasAilment) continue;
                    _patientIds.Add(id);
                    _admittedHours[id] = 0f;
                }
            OnChanged?.Invoke();
        }

        /// <summary>저장용: 입원 중인 용병 id.</summary>
        public static List<string> SaveIds()
        {
            Cleanup();
            return new List<string>(_patientIds);
        }

        private static void Treat(float hours)
        {
            Cleanup();
            var discharged = new List<Mercenary>();
            foreach (var merc in _patientIds.Select(Find).Where(m => m != null).ToList())
            {
                // 이번에 실제로 치료한 시간만 요금을 센다(큰 건너뛰기에도 다 나은 뒤로는 받지 않는다).
                float treated = Mathf.Min(hours, RemainingHours(merc));
                _admittedHours[merc.Id] = (_admittedHours.TryGetValue(merc.Id, out float h) ? h : 0f) + treated;
                foreach (var ailment in merc.Ailments.ToList())
                    Ailments.Heal(merc, ailment, hours * ProgressPerHour * ailment.Def.hospitalSpeed, "병원에서");
                if (!merc.HasAilment) discharged.Add(merc);
            }

            foreach (var merc in discharged)
            {
                int fee = Fee(merc);
                _patientIds.Remove(merc.Id);
                _admittedHours.Remove(merc.Id);
                if (fee <= 0 || Wallet.TrySpend(fee))
                {
                    ToastLog.Show($"{merc.Name}이(가) 퇴원했다 (치료비 -{fee}G)");
                    DailyLog.Add($"{merc.Name}이(가) 병원에서 퇴원했다. 치료비 {fee}G");
                }
                else
                {
                    ToastLog.Show($"{merc.Name}이(가) 퇴원했다. 골드가 모자라 치료비 {fee}G를 받지 않았다.");
                    DailyLog.Add($"{merc.Name}이(가) 퇴원했다(치료비 {fee}G 미납).");
                }
            }
            if (discharged.Count > 0) OnChanged?.Invoke();
            AdmitWaiting();
        }

        /// <summary>해고·사망·파견(불러오기 등)·저절로 나음으로 더는 입원할 이유가 없는 사람을 뺀다(치료비는 받지 않음).</summary>
        private static void Cleanup()
        {
            var gone = _patientIds.Where(id =>
            {
                var merc = Find(id);
                return merc == null || !merc.IsAlive || !merc.HasAilment || ExpeditionLog.Instance.IsOnExpedition(merc);
            }).ToList();
            if (gone.Count == 0) return;
            foreach (var id in gone)
            {
                _patientIds.Remove(id);
                _admittedHours.Remove(id);
            }
            OnChanged?.Invoke();
        }

        private static Mercenary Find(string id) => PlayerParty.Instance.Members.FirstOrDefault(m => m.Id == id);
    }
}
