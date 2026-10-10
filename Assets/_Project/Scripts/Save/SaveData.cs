using System;
using System.Collections.Generic;

namespace GN3.Save
{
    /// <summary>save.json 한 파일의 내용(JsonUtility용 직렬화 클래스). 시장·게시판·하루 보고서는 저장하지 않는다.</summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

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
        public int grade;
        public int level;
        public int experience;
        public int personality;
        public bool rarePassive;
        public int health;
        public string weapon; // 무기 이름(없으면 빈 문자열)
        public int fatigue;
        public int morale = 70; // 예전 저장엔 없어 기본값으로 읽힌다
        public List<AilmentSave> ailments = new List<AilmentSave>(); // 부상·질병(예전 저장엔 없어 빈 목록)

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
        public int remainingDays;
        public int outboundDays;
        public bool returning;
        public int returnDaysLeft;
        public List<string> memberIds = new List<string>();
    }
}
