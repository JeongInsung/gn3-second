namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병 피로·사기·회복 수치를 한곳에 모은 표. 규칙은 각자 자리(MercenaryCondition·RestVenues·TrainingHall·InnRest·Mercenary)에 있고
    /// 숫자만 여기서 읽는다. 바꾼 결과는 에디터 메뉴 GN3/Balance/용병 상태 시뮬레이션으로 확인한다.
    /// 친밀도 단계별 사기(앙숙 -3 ~ 절친 +3, 전사 슬픔 -3/-10/-15)는 Affinity.Tiers, 병의 사기 감소는 각 AilmentSO에 있다.
    /// </summary>
    public static class ConditionTuning
    {
        // ---------- 기준선 (Mercenary) ----------
        public const int MaxCondition = 100;
        public const int StartingMorale = 70;
        public const int TiredFatigue = 50;      // 이상이면 공격·방어 × TiredMultiplier
        public const int ExhaustedFatigue = 80;  // 이상이면 × ExhaustedMultiplier, 파견·훈련 불가
        public const int LowMorale = 30;         // 미만이면 × LowMoraleMultiplier
        public const float TiredMultiplier = 0.9f;
        public const float ExhaustedMultiplier = 0.75f;
        public const float LowMoraleMultiplier = 0.9f;

        // ---------- 하루(자정)마다 (MercenaryCondition) ----------
        // 2026-10-12 조정: 가는 길 12→8, 귀환길 따로 6, 밤 휴식 25→20 (3단계 보스 왕복이 피로 100에 꽉 차던 것을 80 안팎으로)
        public const int ExpeditionFatiguePerDay = 8;    // 가는 길(목적지 도착 전)
        public const int ReturnFatiguePerDay = 6;        // 귀환길
        public const int RestFatiguePerNight = 20;       // 마을에서 잔 밤
        public const int ExhaustedMoralePerDay = 5;      // 지친 채 하루를 보내면
        // 마을에서 파견 없이 IdleGraceDays일을 넘기면 하루 IdleMoralePerDay씩 사기가 준다(지루함). 파견을 다녀오면 다시 센다.
        public const int IdleGraceDays = 2;
        public const int IdleMoralePerDay = 2;
        // 사기 끌림: 자정마다 사기가 MoraleBaseline보다 높으면 -MoraleDriftPerDay, 낮으면 +(0이면 끔). 높은 사기는 유지에 힘이 들고 낮은 사기는 천천히 돌아온다.
        public const int MoraleBaseline = 60;
        public const int MoraleDriftPerDay = 1;          // 2026-10-12 0(끔)→1
        public const float InnHealRatio = 0.5f;          // 마을에서 자면 최대 체력의 이만큼 회복(InnRest)

        // ---------- 전투·습격 (MercenaryCondition) ----------
        public const int BattleFatigue = 20;
        public const int AmbushFatigue = 10;
        // 2026-10-12 조정: 승리 10→5, 패배 15→25 (이기기만 하면 사기 100에 붙고, 연패해도 30 아래로 안 내려가던 것)
        public const int VictoryMorale = 5;
        public const int DefeatMorale = 25;
        public const int FallenComradeMorale = 10;       // 전사자 1명마다
        public const int MaxAffinityBattleMorale = 9;    // 동료 관계로 오르내리는 전투 사기 한도(±)

        // ---------- 주급 (MercenaryCondition) ----------
        public const float WageRatio = 0.2f;             // 고용비 대비
        public const int PayIntervalDays = 7;
        // 길드를 떠날 결심: 친한 동료의 죽음·주급 미지급으로 사기가 LowMorale 아래로 떨어진 순간 한 번 굴린다(MercenaryCondition.ConsiderLeaving).
        // 사기 LowMorale-1일 때 LeaveChanceAtLowMorale, 0일 때 1까지 직선(사기 15면 약 60%). 사기 0은 원래대로 자정에 무조건 떠난다.
        public const float LeaveChanceAtLowMorale = 0.2f;

        public static float LeaveChance(int morale)
        {
            if (morale >= LowMorale) return 0f;
            if (morale <= 0) return 1f;
            float t = (float)(LowMorale - 1 - morale) / (LowMorale - 1); // 29 → 0, 0 → 1
            return LeaveChanceAtLowMorale + (1f - LeaveChanceAtLowMorale) * t;
        }

        public const int PaidMorale = 5;
        public const int UnpaidMorale = 30;

        // ---------- 훈련·수련 (TrainingHall · 연구소 · 성당) ----------
        public const int TrainingFatiguePerHour = 2;
        public const float StudyMoralePerHour = 1f;      // 성당 기도(연구소는 0)

        // ---------- 쉼터 방문 확률 (RestVenues) ----------
        // 온천: 피로 HotSpringMinFatigue 미만이면 안 감, 그 위로는 VisitBase + VisitScale × 피로/100.
        // 도박장: 사기 GamblingContentMorale 이상이고 피로 GamblingMinFatigue 미만이면 안 감,
        //        그 외 VisitBase + VisitScale × max((100 - 사기)/100, 피로/100).
        public const float VisitBaseChance = 0.1f;
        public const float VisitScaleChance = 0.4f;
        public const int HotSpringMinFatigue = 30;
        public const int GamblingContentMorale = 70;
        public const int GamblingMinFatigue = 30;
        public const float StudyVisitChance = 0.25f;     // 지치지 않은 마법사·힐러
        public const float TrainingVisitChance = 0.15f;  // 훈련소(VillagePartyPresenter가 굴림)

        // 방문을 굴리는 간격(게임 시간). 실시간은 VillagePartyPresenter가 실제 초로 굴린다(쉼터 40~90초·훈련 60~120초, 1배속 30초 = 1시간).
        // '진행'으로 건너뛸 때는 VillageSkipVisits가 같은 평균 간격으로 굴린다. 마을 사람은 VillageOutHour시부터 밖에 나와 다닌다.
        public const float VenueRollHours = 65f / 30f;
        public const float TrainingRollHours = 90f / 30f;
        public const float VillageOutHour = 9f;

        public static float HotSpringVisitChance(int fatigue) =>
            fatigue < HotSpringMinFatigue ? 0f : VisitBaseChance + VisitScaleChance * fatigue / 100f;

        public static float GamblingVisitChance(int fatigue, int morale) =>
            morale >= GamblingContentMorale && fatigue < GamblingMinFatigue ? 0f
                : VisitBaseChance + VisitScaleChance * System.Math.Max((100f - morale) / 100f, fatigue / 100f);

        // ---------- 쉼터 효과 (RestVenues) ----------
        public const float HotSpringFatigueReliefPerHour = 10f; // 2026-10-12 15→10
        public const float HotSpringMoralePerHour = 1f;
        public const float HotSpringMinHours = 1f;
        public const float HotSpringMaxHours = 2f;
        public const int HotSpringCapacity = 3;

        public const float GamblingFatigueReliefPerHour = 5f;
        public const float GamblingMoralePerHour = 2f;  // 2026-10-12 4→2
        public const int GamblingMoraleCap = 70;           // 사기가 이 값 이상이면 도박장에서 사기는 안 오르고 피로만 풀린다(100 = 제한 없음, 2026-10-12 100→70)
        public const float GamblingMinHours = 1f;
        public const float GamblingMaxHours = 3f;
        public const int GamblingCapacity = 4;

        public const float StudyMinHours = 2f;
        public const float StudyMaxHours = 4f;
        public const int StudyCapacity = 2;
    }
}
