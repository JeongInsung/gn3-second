using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Mercenaries
{
    public static class MercenaryNamePool
    {
        private static readonly string[] FantasyNames =
        {
            "카이런", "브렌", "이바", "로한", "셀린", "다르우스", "미라", "가록",
            "아엘라", "토르빈", "네사", "울프릭", "리아나", "그림", "세라핀", "발두르"
        };

        private static readonly string[] WesternNames =
        {
            "잭", "윌리엄", "에드워드", "아서", "헨리", "올리버", "찰스", "토마스", "새뮤얼", "월터",
            "엘리자베스", "샬럿", "마거릿", "엘리너", "빅토리아", "아멜리아", "캐서린", "앨리스", "에마", "그레이스"
        };

        private static readonly string[] KoreanNames =
        {
            "지훈", "서준", "민재", "도현", "하준", "시우", "유준", "은우", "지호", "준서",
            "서연", "지민", "하윤", "서윤", "지우", "수아", "예은", "하은", "다은", "나윤"
        };

        private static readonly string[] JapaneseNames =
        {
            "하야토", "렌", "유토", "소라", "카이토", "리쿠", "아키라", "츠바사", "신지", "하루토",
            "사쿠라", "유이", "아야메", "미유", "나나미", "유즈키", "히나타", "아오이", "코토네", "시즈쿠"
        };

        private static readonly string[] AllNames = FantasyNames.Concat(WesternNames).Concat(KoreanNames).Concat(JapaneseNames).ToArray();

        /// <summary>excluded에 포함된 이름(현재 고용 중인 용병 등)은 제외하고 뽑는다. 제외 대상을 빼면 풀이 없을 경우 전체 풀에서 다시 뽑는다.</summary>
        public static string GetRandom(Random rng, ISet<string> excluded = null)
        {
            var available = excluded == null
                ? AllNames
                : AllNames.Where(n => !excluded.Contains(n)).ToArray();

            if (available.Length == 0)
                available = AllNames;

            return available[rng.Next(available.Length)];
        }
    }
}
