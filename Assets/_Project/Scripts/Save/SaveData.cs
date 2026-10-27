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
        // 5: 정보창 '이번 달 기준값'(MercenarySave.month*). 없으면 불러올 때 지금 값으로 찍는다.
        // 6: 힐러 신성력(MercenarySave.growthHoly·monthHoly). 5 이하 힐러는 다른 네 능력치 성장 평균만큼 신성력 성장을 준다.
        //    마법사 growthAttack·monthAttack은 이제 마력(값은 그대로).
        // 7: 용병 계약(contracts: 주급·만료일·성공 보너스). 6 이하는 불러올 때 기본 계약(예전 주급 공식, 60일)을 받는다.
        // 8: 근처 길드(rivals 등). 7 이하는 불러올 때 길드를 새로 만든다.
        // 9: 길드 시설 단계(facilities). 예전 저장은 0단계.
        // 10: 지은 마을 건물과 자리(buildings). 9 이하는 건물이 다 있던 시절이라 프리팹 자리 그대로 전부 지은 마을이 된다.
        // 11: 건설 재료(loot에 같이). 10 이하는 재료가 없던 시절이라 불러올 때 시작 재료(BuildMaterials.StartingKit)를 받는다.
        public const int CurrentVersion = 11;

        public int version = CurrentVersion;
        public string savedAt;
        public List<string> lootSeen = new List<string>(); // 한 번이라도 얻은 아이템 이름(LootBag.HasSeen — 도감 "획득한 아이템")
        public string gameId; // 게임(새로 시작한 판)마다 하나 — 저장 기록을 게임별로 나눈다(SaveSystem.GameId). 예전 저장은 비어 있다

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
        public List<QuestClearSave> questClears = new List<QuestClearSave>(); // 지역·레벨별 퀘스트 클리어 횟수(예전 저장엔 없어 0부터 시작)
        public int forestLevel; // 은월의 숲 열린 최고 레벨 1~5(예전 저장엔 없어 0 → Lv1부터)
        // 게시판·시장 목록(새로고침 없이 시간 따라 바뀜 — 불러와도 같은 목록). 예전 저장엔 없어 처음 열 때 새로 뽑는다.
        public bool hasQuestBoard;
        public List<BoardQuestSave> questBoard = new List<BoardQuestSave>();
        public int questBoardNextDay;
        public bool hasMarket;
        public List<MercenarySave> market = new List<MercenarySave>();
        public int marketNextDay;
        public List<LootSave> loot = new List<LootSave>(); // 전리품 가방(예전 저장엔 없어 빈 가방)
        public List<LootSave> bestiaryKills = new List<LootSave>(); // 도감 처치 수(이름 = 도감 칸, 예전 저장엔 없어 전부 미발견)
        public List<LootSave> bestiaryBest = new List<LootSave>(); // 도감 칸마다 쓰러뜨린 가장 높은 개체 등급(count = 등급 번호)
        public List<ContractSave> contracts = new List<ContractSave>(); // 용병 계약(예전 저장엔 없어 기본 계약을 준다)
        // 근처 길드(버전 8). 예전 저장엔 없어 불러올 때 새로 만든다.
        public List<RivalGuildSave> rivals = new List<RivalGuildSave>();
        public List<MercenarySave> freeAgents = new List<MercenarySave>(); // 우리가 내보내 아직 아무 데도 안 간 용병
        public List<int> freeAgentDays = new List<int>();                  // freeAgents와 같은 순서: 떠나는 날
        public List<string> rivalPromised = new List<string>();            // 재계약 맞제의로 약속한 "용병id|길드id"
        public List<string> marketOrigins = new List<string>();            // 시장 이적 매물 "용병id|길드id"
        public List<LoanSave> loans = new List<LoanSave>();                // 근처 길드와 주고받은 용병(대여·연합 손님)
        public List<string> facilities = new List<string>();               // 길드 시설 "Training|2"(버전 9, 예전 저장엔 없어 전부 0단계)
        public List<string> buildings = new List<string>();                // 지은 마을 건물 "이름#번째|x|y"(버전 10, VillageLayout)
        public List<string> felledTrees = new List<string>();              // 벤 숲 물체 "이름#번째"(ForestLayout)
        public int fellsUsed;                                              // 그날 벤 횟수와 그 날짜
        public int fellsDay = -1;
        public int merchantHereDay = -1;                                   // 떠돌이 상인이 와 있는 날(-1 없음)·다음에 올 날·남은 물건(TravelingMerchant)
        public int merchantNextDay = -1;
        public List<LootSave> merchantStock = new List<LootSave>();
        public int blackMarketHereDay = -1;                                // 암거래 상인이 와 있는 날(-1 없음)·다음에 올 날·남은 돈(BlackMarket, 예전 저장엔 없어 -1 → 새로 정함)
        public int blackMarketNextDay = -1;
        public int blackMarketPurse;
        public int brokerHereDay = -1;                                     // 의뢰 중개인이 와 있는 날(-1 없음)·다음에 올 날·남은 의뢰(QuestBroker)
        public int brokerNextDay = -1;
        public List<BoardQuestSave> brokerQuests = new List<BoardQuestSave>();
        public int rivalTargetIndex = -1;                                  // 근처 길드가 노리는 게시판 의뢰(questBoard 몇 번째)
        public string rivalTargetGuild;
    }

    [Serializable]
    public class RivalGuildSave
    {
        public string id;
        public string name;
        public int temper;
        public int reputation;
        public int gold;
        public int relation;
        public int lastGiftDay = -1;
        public int barterDay = -1;
        public int barterCount;
        public List<MercenarySave> roster = new List<MercenarySave>();
        public List<int> rosterEnd = new List<int>(); // roster와 같은 순서: 계약 끝나는 날
        public List<RivalJobSave> jobs = new List<RivalJobSave>();
        public List<int> newsDays = new List<int>();
        public List<string> newsTexts = new List<string>();
    }

    [Serializable]
    public class LoanSave
    {
        public string mercId;      // 빌려 온 손님: 파티(party)에 있는 용병 id
        public MercenarySave merc; // 빌려 준 우리 용병(파티에 없어 여기 저장)
        public string guildId;
        public bool inbound;
        public int endDay;
        public int dailyFee;
        public bool joint;
        public string questId;
        public int rosterEnd;
    }

    [Serializable]
    public class RivalJobSave
    {
        public string title;
        public int endDay;
        public float chance;
        public int reward;
        public int reputation;
    }

    [Serializable]
    public class ContractSave
    {
        public string id;
        public int wage;
        public int endDay;
        public int winBonus;
        public int signedDay;
    }

    [Serializable]
    public class QuestClearSave
    {
        public int region;
        public int tier;
        public int count;
    }

    /// <summary>전리품 가방 한 종류(LootBag).</summary>
    [Serializable]
    public class LootSave
    {
        public string name;
        public int count;
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
        public int growthHoly; // 신성력(힐러, 버전 6)
        public float growthProgress;
        public bool hasMonthBase; // 이번 달 기준값(정보창 괄호 정산용, 버전 5)
        public int monthAttack;
        public int monthDefense;
        public int monthSpeed;
        public int monthHoly; // 버전 6
        public int monthPower;
        public int personality;
        public bool rarePassive;
        public int health;
        public string weapon; // 무기 이름(없으면 빈 문자열)
        public int bag;       // 가방 번호(BagCatalog, 예전 저장엔 없어 0 = 낡은 주머니)
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
        public string leaderId; // 분대장(예전 저장엔 없어 비어 있음 → 첫 파견원)
        public List<int> memberSquads = new List<int>(); // memberIds와 같은 순서의 분대(0·1). 예전 저장엔 없어 전원 1분대
        public string leaderId2; // 2분대 분대장(두 분대 퀘스트만)
        public string allyGuildId; // 연합 의뢰(보상을 나눌 근처 길드)
        public int allyCount;
        public List<WaveSave> waves = new List<WaveSave>(); // 라운드 구성(예전 저장엔 없어 enemy × enemyCount 한 라운드)
        public List<LootSave> materialReward = new List<LootSave>(); // 성공하면 받는 건설 재료(Quest.MaterialReward)
    }

    /// <summary>게시판에 붙어 있는(아직 수락 안 한) 퀘스트.</summary>
    [Serializable]
    public class BoardQuestSave
    {
        public string title;
        public int region;
        public string location;
        public string enemy;
        public int enemyCount;
        public int difficulty;
        public int durationDays;
        public string requester;
        public string requestText;
        public bool challenge; // 맨 위 도전 퀘스트(근처 길드가 가져가지 않는다)
        public string allyGuildId; // 연합 의뢰(함께하는 근처 길드)
        public int allyCount;
        public List<WaveSave> waves = new List<WaveSave>();
        public List<LootSave> materialReward = new List<LootSave>(); // 성공하면 받는 건설 재료(Quest.MaterialReward)
        public int expireDay = -1; // 의뢰 중개인 의뢰: 이 날까지 받을 수 있다(QuestBroker). 게시판 의뢰는 -1
    }

    [Serializable]
    public class WaveSave
    {
        public string enemy;
        public int count;
        public int tier;
    }
}
