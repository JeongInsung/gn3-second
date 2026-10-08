using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Quests;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    public enum MailKind { Notice, QuestResult, Arrival, Report, Alert }

    /// <summary>
    /// 우편함(받은편지함). 일반 알림·퀘스트 결과·파견대 도착(자동/직접 선택)·하루 보고서·중요 소식을 모두 편지로 모은다.
    /// Important 편지는 ImportantAlertPanel이 게임을 멈추고 하나씩 띄운다(Acknowledged가 될 때까지).
    /// 도착 편지는 자동/직접을 고를 때까지 "선택 대기"로 남아 우편함에서 언제든 처리할 수 있다.
    /// 저장하지 않는다(불러오면 도착 대기 파견은 ExpeditionLog.Restore가 OnArrived를 다시 불러 편지가 다시 생긴다).
    /// </summary>
    public static class Mailbox
    {
        private const int MaxMails = 300;
        private static readonly Color AlertColor = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color ArrivedColor = new Color(0.6f, 0.85f, 0.45f);

        public class Mail
        {
            public int Id;
            public MailKind Kind;
            public int Day;
            public float Hour;
            public string Title;
            /// <summary>목록에 보이는 짧은 제목("용병 고용", "임무 완료!"). 누르면 Title·Body 전체가 펼쳐진다.</summary>
            public string ShortTitle;
            public string Body;
            public bool Read;
            public bool Important;
            /// <summary>중요 알림창에서 확인했다(다시 띄우지 않는다). 도착은 [나중에]도 확인으로 친다.</summary>
            public bool Acknowledged;
            public Expedition Expedition;
            public bool Resolved;

            /// <summary>자동/직접을 아직 고르지 않았고 그 파견대가 도착지에서 기다리는 중.</summary>
            public bool IsPendingChoice => Kind == MailKind.Arrival && !Resolved && Expedition != null
                                           && Expedition.IsReady && ExpeditionLog.Instance.Active.Contains(Expedition);
        }

        private static readonly List<Mail> _mails = new List<Mail>();
        private static int _nextId;
        private static bool _subscribed;

        /// <summary>오래된 것부터.</summary>
        public static IReadOnlyList<Mail> All => _mails;
        public static event System.Action Changed;

        public static bool HasUnread => _mails.Any(m => !m.Read);
        public static int UnreadCount => _mails.Count(m => !m.Read);
        public static int PendingChoiceCount => _mails.Count(m => m.IsPendingChoice);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _mails.Clear();
            _nextId = 0;
            _subscribed = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            ExpeditionLog.Instance.OnArrived += PostArrival;
        }

        /// <summary>편지를 넣는다. shortTitle을 안 주면 종류·제목으로 짧은 제목을 만든다(ShortTitleFor).</summary>
        public static Mail Post(MailKind kind, string title, string body = "", bool important = false, string shortTitle = null)
        {
            var mail = new Mail
            {
                Id = ++_nextId,
                Kind = kind,
                Day = GameClock.CurrentDay,
                Hour = GameClock.CurrentHour,
                Title = title,
                ShortTitle = string.IsNullOrEmpty(shortTitle) ? ShortTitleFor(kind, title) : shortTitle,
                Body = body ?? "",
                Important = important,
            };
            _mails.Add(mail);
            Trim();
            Changed?.Invoke();
            return mail;
        }

        /// <summary>파견대 도착: 자동/직접 선택이 필요한 중요 편지. 같은 파견에 처리 안 된 편지가 있으면 다시 만들지 않는다.</summary>
        public static void PostArrival(Expedition expedition)
        {
            if (expedition == null || _mails.Any(m => m.Kind == MailKind.Arrival && m.Expedition == expedition && !m.Resolved)) return;
            var quest = expedition.Quest;
            var members = expedition.Members.Where(m => m.IsAlive).ToList();
            string body = $"{QuestDifficulty.RichTitle(quest)}\n" +
                          $"파견대: {string.Join(", ", members.Select(m => m.Name))}\n" +
                          $"{QuestDifficulty.DescribeTeam(quest, QuestDifficulty.TeamPower(members))}\n\n" +
                          "전투를 어떻게 진행할까요?";
            var mail = Post(MailKind.Arrival, $"[{quest.Title}] 목적지 도착", body, important: true);
            mail.Expedition = expedition;
        }

        /// <summary>파견 전투가 끝났다(어디서 끝냈든). 그 파견의 도착 편지를 처리 완료로.</summary>
        public static void ResolveArrival(Expedition expedition)
        {
            bool changed = false;
            foreach (var mail in _mails)
            {
                if (mail.Kind != MailKind.Arrival || mail.Expedition != expedition || mail.Resolved) continue;
                mail.Resolved = mail.Acknowledged = mail.Read = true;
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>하루 보고서 편지: 아직 보고하지 않은 DailyLog 줄을 날짜별로 엮는다(습격·사망은 빨강, 도착은 초록).</summary>
        public static void PostReport(int fromDay, int toDay)
        {
            if (toDay < fromDay) return;
            fromDay = System.Math.Min(fromDay, DailyLog.FirstPendingDay);
            var pending = DailyLog.TakePending();
            var sb = new StringBuilder();
            for (int day = fromDay; day <= toDay; day++)
            {
                sb.Append($"<b>■ {GameCalendar.Format(day)}</b>\n");
                if (!pending.TryGetValue(day, out var lines) || lines.Count == 0)
                {
                    sb.Append($"· {DailyLog.QuietDay}\n\n");
                    continue;
                }
                foreach (var line in lines)
                {
                    bool alert = line.Contains("습격") || line.Contains("사망") || line.Contains("전멸");
                    bool arrived = line.Contains("도착!");
                    string text = "· " + line;
                    if (alert) text = Colored(text, AlertColor);
                    else if (arrived) text = Colored(text, ArrivedColor);
                    sb.Append(text).Append('\n');
                }
                sb.Append('\n');
            }
            string title = fromDay == toDay
                ? $"{GameCalendar.Format(fromDay)} 보고"
                : $"{GameCalendar.Format(fromDay)} ~ {GameCalendar.Format(toDay)} 보고";
            Post(MailKind.Report, title, sb.ToString().TrimEnd());
        }

        public static void MarkRead(Mail mail)
        {
            if (mail == null || mail.Read) return;
            mail.Read = true;
            Changed?.Invoke();
        }

        /// <summary>kind가 있으면 그 종류만.</summary>
        public static void MarkAllRead(MailKind? kind = null)
        {
            bool changed = false;
            foreach (var mail in _mails)
            {
                if (mail.Read || (kind.HasValue && mail.Kind != kind.Value)) continue;
                mail.Read = true;
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        public static void Acknowledge(Mail mail)
        {
            if (mail == null) return;
            mail.Acknowledged = mail.Read = true;
            Changed?.Invoke();
        }

        /// <summary>아직 알림창에 안 띄운 중요 편지 중 가장 오래된 것(처리가 끝난 도착은 건너뛴다).</summary>
        public static Mail NextUnacknowledgedImportant()
        {
            foreach (var mail in _mails)
            {
                if (!mail.Important || mail.Acknowledged) continue;
                if (mail.Kind == MailKind.Arrival && !mail.IsPendingChoice)
                {
                    mail.Acknowledged = true; // 이미 다른 데서 처리했거나 사라진 파견
                    continue;
                }
                return mail;
            }
            return null;
        }

        // 일반 알림(토스트 문장 전체가 제목)의 짧은 제목: 위에서부터 처음 맞는 낱말.
        private static readonly (string[] Keys, string Short)[] NoticeTitles =
        {
            (new[] { "[테스트]" }, "테스트"),
            (new[] { "관계:" }, "관계 변화"),
            (new[] { "같이 훈련" }, "같이 훈련"),
            (new[] { "훈련을 마쳤다" }, "훈련 완료"),
            (new[] { "레벨 업", "달성" }, "레벨 업"),
            (new[] { "파견대 출발" }, "파견대 출발"),
            (new[] { "습격" }, "습격"),
            (new[] { "도착" }, "도착"),
            (new[] { "고용 (" }, "용병 고용"),
            (new[] { "골드가 부족" }, "골드 부족"),
            (new[] { "가득 찼" }, "파티 정원 초과"),
            (new[] { "주급" }, "주급"),
            (new[] { "길드를 떠났" }, "용병 이탈"),
            (new[] { "출전할 수 없" }, "출전 불가"),
            (new[] { "장착" }, "무기 장착"),
            (new[] { "여관" }, "여관 휴식"),
            (new[] { "체력", "회복", "다친" }, "치료"),
            (new[] { "불러" }, "불러오기"),       // "저장된 게임을 불러왔습니다"가 "저장"에 걸리지 않게 먼저
            (new[] { "새로 시작" }, "새 게임"),
            (new[] { "저장" }, "저장"),
        };

        private const int ShortTitleFallbackLength = 14;

        /// <summary>
        /// 목록용 짧은 제목. 퀘스트 결과·도착은 앞의 "[퀘스트명] "을 떼고, 일반 알림은 낱말 표(NoticeTitles)로,
        /// 맞는 게 없으면 앞 14자 + "…". 보고·중요 소식은 이미 짧아 그대로.
        /// </summary>
        public static string ShortTitleFor(MailKind kind, string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            switch (kind)
            {
                case MailKind.QuestResult:
                case MailKind.Arrival:
                    int close = title.StartsWith("[") ? title.IndexOf("] ", System.StringComparison.Ordinal) : -1;
                    return close >= 0 ? title.Substring(close + 2) : title;
                case MailKind.Notice:
                    foreach (var (keys, shortTitle) in NoticeTitles)
                        if (keys.Any(title.Contains)) return shortTitle;
                    return title.Length <= ShortTitleFallbackLength ? title : title.Substring(0, ShortTitleFallbackLength) + "…";
                default:
                    return title;
            }
        }

        /// <summary>펼쳤을 때 보이는 내용: 짧은 제목과 다르면 원래 제목(굵게) + 본문. 본문이 없으면 제목만.</summary>
        public static string FullText(Mail mail)
        {
            if (string.IsNullOrEmpty(mail.Body)) return mail.Title;
            return mail.Title == mail.ShortTitle ? mail.Body : $"<b>{mail.Title}</b>\n{mail.Body}";
        }

        public static string KindTag(MailKind kind) => kind switch
        {
            MailKind.QuestResult => "퀘스트",
            MailKind.Arrival => "도착",
            MailKind.Report => "보고",
            MailKind.Alert => "중요",
            _ => "알림",
        };

        public static Color KindColor(MailKind kind) => kind switch
        {
            MailKind.QuestResult => new Color(0.95f, 0.78f, 0.35f),
            MailKind.Arrival => new Color(1f, 0.6f, 0.2f),
            MailKind.Report => new Color(0.55f, 0.75f, 0.95f),
            MailKind.Alert => AlertColor,
            _ => new Color(0.75f, 0.75f, 0.75f),
        };

        public static string Colored(string text, Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        public static string FormatWhen(Mail mail) => $"{GameCalendar.Format(mail.Day)} {DayNightCycle.FormatTime(mail.Hour)}";

        // 오래된 것부터, 처리가 끝난(선택 대기·미확인 중요가 아닌) 편지를 지운다.
        private static void Trim()
        {
            for (int i = 0; _mails.Count > MaxMails && i < _mails.Count;)
            {
                var mail = _mails[i];
                bool keep = mail.IsPendingChoice || (mail.Important && !mail.Acknowledged);
                if (keep) i++;
                else _mails.RemoveAt(i);
            }
        }
    }
}
