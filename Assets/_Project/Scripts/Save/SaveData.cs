using System;
using System.Collections.Generic;

namespace GN3.Save
{
    /// <summary>save.json 한 파일의 내용(JsonUtility용 직렬화 클래스). 시장·게시판·하루 보고서는 저장하지 않는다.</summary>
    [Serializable]
    public class SaveData
    {
        // 2: 파견 남은 시간을 시간 단위로(ExpeditionSave.remainingHours 등). 1이면 일 단위 값 × 24로 읽는다.
        // 3: 레벨·경험치 → 나이·성장 횟수(MercenarySave.age 등). 2 이하면 level-1을 네 능력치 성장 횟수로(능력치 그대로), 나이는 20대 무작위.
        // 4: 등급 = 전투력 구간, 저장의 grade는 재능. 3 이하면 재능 능력치 배율이 빠진 만큼 성장을 더하거나 빼 예전 전투력에 맞춘다.
        public const int CurrentVersion = 4;

        public int version = CurrentVersion;
        public string savedAt;

        public int gold;
        public int day;
        public float hour;
        public int reputation;

        public List<MercenarySave> party = new List<MercenarySave>();
        public List<ExpeditionSave> expeditions = new List<ExpeditionSave>();
        public List<string> trainees = new List<string>(); // 훈련소에 맡긴 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> patients = new List<string>(); // 병원에 입원한 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> bathers = new List<string>();  // 온천에서 쉬는 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> gamblers = new List<string>(); // 도박장에 있는 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> researchers = new List<string>(); // 마법 연구소에 있는 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> prayers = new List<string>();     // 성당에 있는 용병 id(예전 저장엔 없어 빈 목록)
        public List<string> storageWeapons = new List<string>(); // 길드 창고 무기 이름(예전 저장엔 없어 빈 창고)
        public List<string> storageItems = new List<string>();   // 길드 창고 약 이름
        public List<string> leaving = new List<string>(); // 파견 중 길드를 떠나기로 결심한 용병 "id|이유"(예전 저장엔 없어 빈 목록)
        public List<AffinitySave> affinities = new List<AffinitySave>(); // 용병 쌍 친밀도(예전 저장엔 없어 성격 궁합 기본값으로 시작)
    }

    [Serializable]
    public class AffinitySave
    {
        public string a;
        public string b;
        public int value;
    }

    [Serializable]
    public class MercenarySave
    {
        public string id;
        public string name;
        public string className;
        public int grade; // 재능(타고난 등급, F=0 ~ S=6)
        public int level;      // 버전 2 이하(레벨제). 버전 3부터는 아래 나이·성장을 읽는다
        public int experience; // 버전 2 이하. 읽지 않는다
        public int age;
        public int birthday;   // 한 해(360일)의 몇째 날(0~359)
        public int growthAttack;
        public int growthDefense;
        public int growthHealth;
        public int growthSpeed;
        public float growthProgress;
        public int personality;
        public bool rarePassive;
        public int health;
        public string weapon; // 무기 이름(없으면 빈 문자열)
        public int fatigue;
        public int morale = 70; // 예전 저장엔 없어 기본값으로 읽힌다
        public List<AilmentSave> ailments = new List<AilmentSave>(); // 부상·질병(예전 저장엔 없어 빈 목록)
        public float researchHours; // 수련: 다음 단계까지 쌓인 시간(예전 저장엔 없어 0)
        public int researchBonus;  // 수련 단계(마법 연구소·성당, 직업별 능력치 보너스)

        // 외형: 파츠 이름 ↔ 출처 캐릭터(사전은 JsonUtility가 못 써서 두 목록으로)
        public string bodyCharacter;
        public List<string> partNames = new List<string>();
        public List<string> partSources = new List<string>();
    }

    [Serializable]
    public class AilmentSave
    {
        public string id;      // 부상·질병 에셋 이름(Resources/Ailments)
        public int kind;       // 예전 저장용(0 타박상 · 1 골절 · 2 감기 · 3 열병). id가 비어 있을 때만 읽는다
        public float progress; // 0~1
        public int daysUntreated;
    }

    [Serializable]
    public class ExpeditionSave
    {
        public string title;
        public int region;
        public string location;
        public string enemy;
        public int enemyCount;
        public int difficulty;
        public int durationDays;
        public int remainingDays;   // 버전 1(일 단위). 버전 2부터는 아래 시간 값을 읽는다
        public int outboundDays;
        public bool returning;
        public int returnDaysLeft;
        public float remainingHours;
        public float outboundHours;
        public float returnHoursLeft;
        public List<string> memberIds = new List<string>();
    }
}
