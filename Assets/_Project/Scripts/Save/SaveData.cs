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

        // 외형: 파츠 이름 ↔ 출처 캐릭터(사전은 JsonUtility가 못 써서 두 목록으로)
        public string bodyCharacter;
        public List<string> partNames = new List<string>();
        public List<string> partSources = new List<string>();
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
        public List<string> memberIds = new List<string>();
    }
}
