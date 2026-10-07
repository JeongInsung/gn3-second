namespace GN3.World
{
    public enum Season { Spring, Summer, Autumn, Winter }

    /// <summary>
    /// GameClock.CurrentDay(1일차부터)를 달력 날짜로 바꾼다. 1일차 = 650년 3월 1일(봄).
    /// 1년 = 12달 × 30일 = 360일. 계절은 3달씩: 봄 3~5월, 여름 6~8월, 가을 9~11월, 겨울 12~2월.
    /// 날짜는 일차에서 계산만 하므로 저장 파일에는 따로 넣지 않는다.
    /// </summary>
    public static class GameCalendar
    {
        public const int StartYear = 650;
        public const int StartMonth = 3;
        public const int DaysPerMonth = 30;
        public const int MonthsPerYear = 12;
        public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

        public readonly struct Date
        {
            public readonly int Year, Month, Day;
            public Season Season => SeasonOf(Month);

            public Date(int year, int month, int day)
            {
                Year = year;
                Month = month;
                Day = day;
            }
        }

        public static Date Today => FromDay(GameClock.CurrentDay);

        public static Date FromDay(int day)
        {
            int abs = System.Math.Max(0, day - 1) + (StartMonth - 1) * DaysPerMonth;
            return new Date(StartYear + abs / DaysPerYear, abs % DaysPerYear / DaysPerMonth + 1, abs % DaysPerMonth + 1);
        }

        /// <summary>FromDay의 역산: 년·월·일 → 일차. 650년 3월 1일보다 앞이면 1.</summary>
        public static int ToDay(int year, int month, int day)
        {
            int abs = (year - StartYear) * DaysPerYear + (month - 1) * DaysPerMonth + (day - 1);
            return System.Math.Max(1, abs - (StartMonth - 1) * DaysPerMonth + 1);
        }

        /// <summary>360일 달력의 날짜를 현실 1월 1일 = 1 ~ 365로 바꾼다(해 위치 계산용). 3/21 ≈ 80, 6/21 ≈ 172, 12/21 ≈ 355.</summary>
        public static int DayOfYear(int day)
        {
            var d = FromDay(day);
            int index = (d.Month - 1) * DaysPerMonth + (d.Day - 1);
            return (int)System.Math.Round(index * 365.0 / DaysPerYear) + 1;
        }

        public static Season SeasonOf(int month)
        {
            if (month >= 3 && month <= 5) return Season.Spring;
            if (month >= 6 && month <= 8) return Season.Summer;
            if (month >= 9 && month <= 11) return Season.Autumn;
            return Season.Winter;
        }

        public static string SeasonName(Season season) => season switch
        {
            Season.Spring => "봄",
            Season.Summer => "여름",
            Season.Autumn => "가을",
            _ => "겨울",
        };

        /// <summary>"650년 3월 1일"</summary>
        public static string Format(int day)
        {
            var d = FromDay(day);
            return $"{d.Year}년 {d.Month}월 {d.Day}일";
        }

        /// <summary>"650년 3월 1일 · 봄"</summary>
        public static string FormatWithSeason(int day) => $"{Format(day)} · {SeasonName(FromDay(day).Season)}";
    }
}
