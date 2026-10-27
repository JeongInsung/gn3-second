using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Quests
{
    /// <summary>마을 사람 한 명의 부탁(의뢰인과 1인칭 부탁 글). 게시판 제목과 퀘스트 정보창에 나온다.</summary>
    public readonly struct QuestRequest
    {
        public readonly string Requester;
        public readonly string Text;

        public QuestRequest(string requester, string text)
        {
            Requester = requester;
            Text = text;
        }
    }

    /// <summary>
    /// 퀘스트 대표 적(마지막 라운드)별 마을 사람 부탁 글. 같은 적이라도 5가지 중 하나가 무작위로 붙는다(QuestGenerator).
    /// 새 몬스터를 RegionInfo에 더하면 여기에도 5개를 더한다. 없는 이름(예전 저장 등)은 Fallback.
    /// </summary>
    public static class QuestRequests
    {
        private static readonly Dictionary<string, QuestRequest[]> ByEnemy = new Dictionary<string, QuestRequest[]>
        {
            ["고블린"] = new[]
            {
                new QuestRequest("방앗간 주인", "밤마다 고블린 놈들이 방앗간 밀가루 포대를 훔쳐 갑니다. 이대로면 마을이 겨울을 못 나요. 제발 숲으로 쫓아내 주십시오."),
                new QuestRequest("양치기 소녀", "고블린들이 울타리를 부수고 양을 끌고 갔어요. 제일 아끼던 새끼 양도요… 용병님들, 도와주세요!"),
                new QuestRequest("여관 주인", "숲길에 고블린이 진을 쳐서 손님이 뚝 끊겼소. 이러다 여관 문 닫겠구먼. 길 좀 뚫어 주시오."),
                new QuestRequest("대장장이", "고블린 녀석들이 내 대장간 쇠붙이를 몽땅 털어 갔어. 그걸로 칼이라도 만들면 큰일이지. 놈들 소굴을 쓸어 주게."),
                new QuestRequest("빨래터 아주머니", "개울가에서 빨래를 하는데 고블린들이 돌을 던지며 낄낄대지 뭐예요. 무서워서 아이들을 밖에 못 내보내요. 혼쭐 좀 내 주세요."),
            },
            ["고블린 늑대기수"] = new[]
            {
                new QuestRequest("행상인", "늑대를 탄 고블린들이 수레를 쫓아와 물건을 다 빼앗아 갔습니다. 다음 장날까지 길이 안전해야 해요. 부탁드립니다."),
                new QuestRequest("우편 배달부", "숲길을 지날 때마다 늑대기수들이 쫓아와요. 편지 가방까지 찢겼다니까요. 이대로면 마을에 소식이 끊깁니다."),
                new QuestRequest("목장 주인", "늑대 탄 고블린들이 밤마다 목장을 휘젓고 다녀서 소들이 젖을 안 냅니다. 그 빠른 놈들을 잡을 사람은 용병님들뿐이오."),
                new QuestRequest("파수꾼", "망루에서 보니 늑대기수 무리가 마을 둘레를 맴돌고 있소. 정찰하는 것 같단 말이오. 큰일 나기 전에 막아 주시오."),
                new QuestRequest("젊은 기사 지망생", "늑대기수를 혼자 막아 보려다… 이렇게 팔만 다쳤습니다. 부끄럽지만 부탁드립니다. 마을을 지켜 주십시오."),
            },
            ["야생늑대"] = new[]
            {
                new QuestRequest("사냥꾼", "요즘 숲의 야생늑대들이 이상할 만큼 사나워졌네. 내 사냥개도 물려 죽었어. 무리를 흩어 놓지 않으면 사람도 다칠 걸세."),
                new QuestRequest("나무꾼", "도끼질을 하는데 늑대 울음이 사방에서 들려와 일을 못 하겠소. 땔감이 없으면 마을이 얼어 죽어요. 늑대들을 쫓아 주시오."),
                new QuestRequest("양치기 할아버지", "밤새 늑대들이 우리를 빙빙 돌며 노려본다오. 늙은 몸으로는 막을 수가 없구려. 젊은 양반들이 좀 나서 주시게."),
                new QuestRequest("아이 엄마", "우리 아이가 숲 가장자리에서 늑대 눈을 봤대요. 그날부터 밤마다 울어요. 아이가 마음 놓고 잘 수 있게 해 주세요."),
                new QuestRequest("마을 경비대장", "야생늑대 무리가 마을 울타리 근처까지 내려왔다. 경비대만으로는 부족하다. 용병 길드의 도움을 정식으로 요청한다."),
            },
            ["독버섯"] = new[]
            {
                new QuestRequest("약초꾼 할머니", "약초 캐는 길에 걸어 다니는 독버섯이 생겼지 뭐유. 포자를 뿜어 대서 숨도 못 쉬겠어. 약초가 없으면 아픈 사람들이 큰일이야."),
                new QuestRequest("마을 의원", "숲에서 독 포자를 마신 환자가 벌써 셋입니다. 해독약이 바닥나고 있어요. 원인인 독버섯들을 없애 주십시오."),
                new QuestRequest("꿀벌 치는 농부", "독버섯이 퍼지고 나서 벌들이 꿀을 못 모으고 픽픽 쓰러집니다. 올해 꿀은 다 망했어요. 제발 그것들을 태워 주세요."),
                new QuestRequest("신전 사제", "숲에서 퍼지는 독기가 마을 우물까지 닿을까 두렵습니다. 신의 이름으로, 그 저주받은 버섯들을 정화해 주십시오."),
                new QuestRequest("버섯 따는 소년", "버섯 따러 갔다가 버섯이 저를 쫓아왔어요! 진짜예요! 친구가 포자를 마시고 아파요. 도와주세요!"),
            },
            ["그루터기 엔트"] = new[]
            {
                new QuestRequest("나무꾼", "베어 낸 그루터기가 벌떡 일어나 걸어 다니지 뭐요. 도끼로 찍어도 꿈쩍을 안 해요. 벌목장을 되찾게 도와주시오."),
                new QuestRequest("수레꾼", "숲길 한가운데 그루터기가 버티고 서서 수레를 뒤엎었소. 느릿느릿한데 한 대 맞으면 뼈가 울려요. 길을 뚫어 주시오."),
                new QuestRequest("목재상", "쌓아 둔 목재 옆에서 그루터기들이 뿌리를 뻗고 있어요. 저러다 창고까지 집어삼킬까 걱정입니다. 어서 치워 주세요."),
                new QuestRequest("숲지기 노인", "앤트킹이 깨어나려는 걸까, 잘린 나무들까지 들고일어났소. 그루터기 놈들은 단단하니 오래 버틸 각오를 하시오."),
                new QuestRequest("장작 패는 소녀", "장작을 패러 갔는데 그루터기가 저를 노려봤어요! 눈이 주황색으로 빛났어요. 무서워서 도끼를 두고 왔어요…"),
            },
            ["앤트킹"] = new[]
            {
                new QuestRequest("촌장", "숲 깊은 곳의 주인 앤트킹이 깨어났습니다. 나무들이 마을 쪽으로 걸어 내려오고 있어요. 이 마을의 운명을 여러분께 맡기겠습니다."),
                new QuestRequest("숲지기 노인", "백 년을 숲에서 살았지만 이런 건 처음이오. 숲의 왕이 분노해 땅을 뒤흔들고 있소. 이대로면 은월의 숲이 마을을 삼킬 거요."),
                new QuestRequest("신전 대사제", "앤트킹의 포효가 들릴 때마다 신전의 촛불이 꺼집니다. 고대의 힘이 깨어난 것입니다. 가장 강한 용병들만 보내 주십시오."),
                new QuestRequest("영주 전령","영주님의 명을 전합니다. 은월의 숲의 주인을 쓰러뜨리는 자에게 큰 상을 내리겠다 하셨습니다. 마을의 명예가 걸린 일입니다."),
                new QuestRequest("나무꾼 조합장", "우리 조합원들이 앤트킹의 뿌리에 휩쓸려 다쳤소. 숲을 베어 먹고사는 우리가 이제 숲에 먹히게 생겼구려. 원수를 갚아 주시오."),
            },
        };

        private static readonly QuestRequest Fallback =
            new QuestRequest("마을 사람", "숲에서 괴물이 나타나 마을이 불안에 떨고 있습니다. 용병님들, 부디 처리해 주십시오.");

        /// <summary>대표 적 이름에 맞는 부탁 하나를 무작위로. 앤트킹은 예전 이름(거목 엔트)도 같은 목록.</summary>
        public static QuestRequest Pick(string enemyName, Random rng)
        {
            string key = RegionInfo.IsEnt(enemyName) ? "앤트킹" : enemyName ?? "";
            return ByEnemy.TryGetValue(key, out var list) ? list[rng.Next(list.Length)] : Fallback;
        }

        /// <summary>테스트·검수용: 그 적의 부탁 글 수.</summary>
        public static int CountFor(string enemyName) =>
            ByEnemy.TryGetValue(RegionInfo.IsEnt(enemyName) ? "앤트킹" : enemyName ?? "", out var list) ? list.Length : 0;
    }
}
