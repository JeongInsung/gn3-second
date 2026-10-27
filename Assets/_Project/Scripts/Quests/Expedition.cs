using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.Quests
{
    /// <summary>퀘스트 하나에 파견된 용병 조합. 여러 파견대가 동시에 진행될 수 있다.
    /// 목적지로 가는 중 → 도착(전투 대기) → 전투 후 귀환 중 → 마을 도착(ExpeditionLog.Complete) 순서로 진행된다.</summary>
    public class Expedition
    {
        public Quest Quest { get; }
        public IReadOnlyList<Mercenary> Members { get; }

        /// <summary>파견원마다 분대(0=1분대, 1=2분대). Members와 같은 순서. 한 분대 퀘스트·예전 저장은 전원 0.</summary>
        public IReadOnlyList<int> Squads { get; }

        /// <summary>1분대 분대장: 직접 진행 전투에서 플레이어가 조종하는 용병(나머지는 자동). 파견 편성 때 정한다.
        /// 예전 저장처럼 없거나 쓰러졌으면 살아 있는 첫 파견원이 맡는다.</summary>
        public Mercenary Leader => LeaderOf(0);
        private readonly Mercenary[] _leaders;

        public int SquadOf(Mercenary merc)
        {
            for (int i = 0; i < Members.Count; i++)
                if (Members[i] == merc) return Squads[i];
            return 0;
        }

        /// <summary>그 분대의 분대장. 없거나 쓰러졌으면 그 분대에서 살아 있는 첫 사람(없으면 null).</summary>
        public Mercenary LeaderOf(int squad)
        {
            var leader = squad < _leaders.Length ? _leaders[squad] : null;
            if (leader != null && leader.IsAlive && Members.Contains(leader) && SquadOf(leader) == squad) return leader;
            return Members.FirstOrDefault(m => m.IsAlive && SquadOf(m) == squad);
        }

        /// <summary>목적지까지 실제로 걸린 게임 시간. 귀환 시간의 기준.</summary>
        public float OutboundHours { get; private set; }

        /// <summary>전투를 마치고 마을로 돌아오는 중.</summary>
        public bool IsReturning { get; private set; }
        /// <summary>마을까지 남은 귀환 시간.</summary>
        public float ReturnHoursLeft { get; private set; }
        /// <summary>표시용: 남은 귀환 시간을 일로 올림.</summary>
        public int ReturnDaysLeft => (int)System.Math.Ceiling(ReturnHoursLeft / Quest.HoursPerDay);

        /// <summary>마지막 습격 판정 뒤로 이동한 시간(ExpeditionLog가 24시간마다 한 번 굴린다).</summary>
        public float HoursSinceAmbushRoll { get; set; }

        /// <summary>이동 중 사건(ChoiceEvents)의 선택을 기다리는 중 — 고를 때까지 멈춰 있다(저장하지 않는다).</summary>
        public bool AwaitingEvent { get; set; }
        /// <summary>사건에서 지름길을 골랐다: 이번 길(가는 길)의 습격 확률이 두 배.</summary>
        public bool RiskyRoute { get; set; }

        /// <summary>도착하면 묻지 않고 자동 전투(편성 때 고른다 — Mailbox.PostArrival). 결과 편지만 온다.</summary>
        public bool AutoOnArrival { get; set; }

        /// <summary>사건으로 이동 시간을 늘리거나(+) 줄인다(−, 남은 시간은 최소 1시간). 가는 길·돌아오는 길 모두.</summary>
        public void Delay(float hours)
        {
            if (IsReturning) ReturnHoursLeft = System.Math.Max(1f, ReturnHoursLeft + hours);
            else Quest.SetRemainingHours(System.Math.Max(1f, Quest.RemainingHours + hours));
        }

        /// <summary>목적지에 도착해 전투를 기다리는 상태. 귀환 중에는 false.</summary>
        public bool IsReady => !IsReturning && Quest.RemainingDays <= 0;

        /// <param name="squads">파견원마다 분대(Members와 같은 순서). 없거나 길이가 맞지 않으면 전원 1분대.</param>
        /// <param name="leader2">2분대 분대장(두 분대 퀘스트만).</param>
        public Expedition(Quest quest, IReadOnlyList<Mercenary> members, Mercenary leader = null,
            IReadOnlyList<int> squads = null, Mercenary leader2 = null)
        {
            Quest = quest;
            Members = members;
            Squads = squads != null && squads.Count == members.Count ? squads : members.Select(_ => 0).ToList();
            _leaders = new[] { leader, leader2 };
        }

        /// <summary>저장 파일에서 불러올 때 이동·귀환 상태를 되돌린다.</summary>
        public void RestoreState(float outboundHours, bool returning, float returnHoursLeft)
        {
            OutboundHours = System.Math.Max(0f, outboundHours);
            IsReturning = returning;
            ReturnHoursLeft = returning ? System.Math.Max(1f, returnHoursLeft) : 0f;
        }

        public void CountOutbound(float hours) => OutboundHours += hours;

        /// <summary>귀환 시간: 갈 때 걸린 시간의 절반을 일 단위로 올림(최소 1일, 예전 '일수 절반 올림'과 같음). 전투 결과 문구에도 쓴다.</summary>
        public float ReturnHoursFor =>
            System.Math.Max(1, (int)System.Math.Ceiling(OutboundHours / (2f * Quest.HoursPerDay) - 0.001f)) * Quest.HoursPerDay;

        /// <summary>귀환길에 오른다.</summary>
        public void BeginReturn()
        {
            IsReturning = true;
            ReturnHoursLeft = ReturnHoursFor;
            HoursSinceAmbushRoll = 0f;
        }

        public void AdvanceReturn(float hours)
        {
            if (hours > 0f)
                ReturnHoursLeft = System.Math.Max(0f, ReturnHoursLeft - hours);
        }

        /// <summary>하루를 더 들여 생존 인원 전원을 완전히 회복시킨다.</summary>
        public void Rest()
        {
            if (IsReturning) ReturnHoursLeft += Quest.HoursPerDay;
            else Quest.Delay(1);
            foreach (var member in Members.Where(m => m.IsAlive))
                member.HealFully();
        }
    }
}
