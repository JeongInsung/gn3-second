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

        /// <summary>분대장: 직접 진행 전투에서 플레이어가 조종하는 용병(나머지는 자동). 파견 편성 때 정한다.
        /// 예전 저장처럼 없거나 쓰러졌으면 살아 있는 첫 파견원이 맡는다.</summary>
        public Mercenary Leader
        {
            get
            {
                if (_leader != null && _leader.IsAlive && Members.Contains(_leader)) return _leader;
                return Members.FirstOrDefault(m => m.IsAlive);
            }
        }
        private readonly Mercenary _leader;

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

        /// <summary>목적지에 도착해 전투를 기다리는 상태. 귀환 중에는 false.</summary>
        public bool IsReady => !IsReturning && Quest.RemainingDays <= 0;

        public Expedition(Quest quest, IReadOnlyList<Mercenary> members, Mercenary leader = null)
        {
            Quest = quest;
            Members = members;
            _leader = leader;
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
