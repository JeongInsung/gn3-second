using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 의뢰 중개인(2026-10-26): 며칠(MinGapDays~MaxGapDays)마다 아침에 마을에 와서 그날 하루 머물며 의뢰를 소개하고 간다(TravelingMerchant와 같은 틀).
    ///  - 퀘스트 게시판이 없으면(돌빛부터 지을 수 있다) 의뢰는 중개인에게서만 받는다: 맨 위 도전 퀘스트 + 일반 BoardlessCount−1개.
    ///  - 게시판을 지은 뒤에도 와서 특별 의뢰 하나를 두고 간다(부탁한 사람의 건설 재료 보상을 더 얹는다).
    /// 소개한 의뢰는 의뢰마다 기한(MinValidDays~MaxValidDays일)이 있어, 중개인이 떠나도 기한까지는 퀘스트 창 맨 위에 남는다(QuestBoardUI).
    /// 마을 가운데쯤에 서 있는 중개인을 누르면 퀘스트 창이 열린다(VillagePartyPresenter). 저장 파일에 같이 들어간다(SaveSystem).
    /// </summary>
    public static class QuestBroker
    {
        private const int MinGapDays = 3;
        private const int MaxGapDays = 5;
        private const int MinValidDays = 3;       // 의뢰 기한(받은 날 포함 며칠 동안 받을 수 있나)
        private const int MaxValidDays = 6;
        private const int MaxHeld = 6;            // 남아 있는 의뢰가 이보다 많으면 기한이 가장 짧은 것부터 버린다
        private const int BoardlessCount = 3;     // 게시판이 없을 때 들고 오는 의뢰 수(도전 포함)
        private const int BonusMaterialKinds = 2; // 특별 의뢰에 더 얹는 건설 재료 종류·개수
        private const int BonusMaterialCount = 2;
        private const string BoardId = "board";

        private static int _hereDay = -1;   // 와 있는 날(-1이면 없음)
        private static int _nextDay = -1;   // 다음에 올 날(-1이면 아직 안 정함 — 새 게임은 바로 온다)
        private static readonly List<Quest> _quests = new List<Quest>();
        private static readonly Dictionary<Quest, int> _expireDay = new Dictionary<Quest, int>(); // 이 날까지 받을 수 있다
        private static readonly System.Random _rng = new System.Random();

        /// <summary>왔다·떠났다·의뢰를 받았다·기한이 지났다(중개인 인형·퀘스트 창이 다시 맞춘다).</summary>
        public static event Action OnChanged;

        public static bool IsHere => _hereDay >= 0 && _hereDay == GameClock.CurrentDay;
        public static int HereDay => _hereDay;
        /// <summary>중개인이 소개해 아직 기한이 남은 의뢰(중개인이 떠나도 남는다). 기한이 짧은 것부터.</summary>
        public static IReadOnlyList<Quest> Quests => _quests;
        /// <summary>다음 방문까지 남은 날(모르면 -1).</summary>
        public static int DaysUntilNext => _nextDay < 0 ? -1 : Math.Max(0, _nextDay - GameClock.CurrentDay);

        /// <summary>그 의뢰를 받을 수 있는 마지막 날(중개인 의뢰가 아니면 -1).</summary>
        public static int ExpireDayOf(Quest quest) => quest != null && _expireDay.TryGetValue(quest, out int day) ? day : -1;

        /// <summary>오늘을 포함해 그 의뢰를 받을 수 있는 남은 날(1이면 오늘까지). 중개인 의뢰가 아니면 -1.</summary>
        public static int DaysLeft(Quest quest)
        {
            int day = ExpireDayOf(quest);
            return day < 0 ? -1 : Math.Max(0, day - GameClock.CurrentDay + 1);
        }

        /// <summary>퀘스트 게시판이 있는가. 없으면 의뢰는 중개인에게서만 받는다.
        /// (정착민 시스템이 생기면 게시판 짓기 조건에 정착민도 붙인다 — BuildingCatalog.Block)</summary>
        public static bool HasBoard => VillageLayout.HasBuilding(BoardId) || VillageLayout.PendingFullVillage;

        /// <summary>수락한 의뢰를 뗀다(저장에서도 빠진다).</summary>
        public static bool Remove(Quest quest)
        {
            if (!_quests.Remove(quest)) return false;
            _expireDay.Remove(quest);
            OnChanged?.Invoke();
            return true;
        }

        public static bool Contains(Quest quest) => quest != null && _quests.Contains(quest);

        /// <summary>새 게임(아직 한 번도 안 옴)이면 오늘 바로 오게 한다 — 게시판 없는 초반에 의뢰가 끊기지 않게. 퀘스트 창을 열 때·날이 바뀔 때.</summary>
        public static void EnsureFirstVisit()
        {
            if (_nextDay < 0 && _hereDay < 0) Arrive(GameClock.CurrentDay);
        }

        private static void HandleNewDay() => OnDay(GameClock.CurrentDay);

        /// <summary>날이 바뀌었다: 기한이 지난 의뢰를 떼고, 머문 날이 지났으면 떠나고, 올 날이면 온다.</summary>
        private static void OnDay(int today)
        {
            bool expired = _quests.RemoveAll(q => _expireDay.TryGetValue(q, out int day) && today > day) > 0;
            foreach (var q in _expireDay.Keys.Where(q => !_quests.Contains(q)).ToList()) _expireDay.Remove(q);
            if (_hereDay >= 0 && today > _hereDay) Leave(today);
            if (_hereDay < 0 && (_nextDay < 0 || today >= _nextDay)) { Arrive(today); return; }
            if (expired) OnChanged?.Invoke();
        }

        private static void Arrive(int today)
        {
            _hereDay = today;
            _nextDay = today; // 떠날 때 다음 날짜를 굴린다
            var board = new QuestBoard { MaxGrade = Guild.Current.MaxQuestGrade };
            bool hasBoard = HasBoard;
            var brought = new List<Quest>();
            if (!hasBoard)
            {
                brought.AddRange(board.Refresh().Take(BoardlessCount)); // 맨 위 도전 퀘스트(레벨 진행)를 늘 포함
            }
            else
            {
                var quest = board.Refresh().Skip(1).First(); // 도전은 게시판에 있으니 일반 하나
                foreach (var item in LootCatalog.Materials.OrderBy(_ => _rng.Next()).Take(BonusMaterialKinds))
                {
                    int i = quest.MaterialReward.FindIndex(m => m.item == item); // 원래 보상에 같은 재료가 있으면 개수만 늘린다
                    if (i >= 0) quest.MaterialReward[i] = (item, quest.MaterialReward[i].count + BonusMaterialCount);
                    else quest.MaterialReward.Add((item, BonusMaterialCount));
                }
                brought.Add(quest);
            }
            foreach (var quest in brought)
            {
                _quests.Add(quest);
                _expireDay[quest] = today + _rng.Next(MinValidDays, MaxValidDays + 1) - 1; // 받은 날 포함 MinValidDays~MaxValidDays일
            }
            SortAndTrim();

            Mailbox.Post(MailKind.Event, "의뢰 중개인이 왔습니다",
                hasBoard ? "특별 의뢰 하나를 가져왔습니다." : $"의뢰 {brought.Count}건을 가져왔습니다.");
            ToastLog.Show("의뢰 중개인이 마을에 왔습니다.", true);
            OnChanged?.Invoke();
        }

        /// <summary>기한이 짧은 것부터 늘어놓고, MaxHeld를 넘으면 기한이 가장 짧은 것부터 버린다.</summary>
        private static void SortAndTrim()
        {
            _quests.Sort((a, b) => ExpireDayOf(a).CompareTo(ExpireDayOf(b)));
            while (_quests.Count > MaxHeld)
            {
                _expireDay.Remove(_quests[0]);
                _quests.RemoveAt(0);
            }
        }

        private static void Leave(int today)
        {
            _hereDay = -1; // 소개한 의뢰는 기한까지 남는다
            _nextDay = today + _rng.Next(MinGapDays, MaxGapDays + 1);
            OnChanged?.Invoke();
        }

        /// <summary>테스트·이벤트용: 지금 바로 오게 한다.</summary>
        public static void ArriveNow() => Arrive(GameClock.CurrentDay);

        // ---------- 저장 ----------

        public static int SavedNextDay => _nextDay;

        /// <summary>expireDays는 quests와 같은 순서(예전 저장은 없거나 -1 → 불러온 날부터 MinValidDays일).</summary>
        public static void Restore(int hereDay, int nextDay, IList<Quest> quests, IList<int> expireDays = null)
        {
            _hereDay = hereDay;
            _nextDay = nextDay;
            _quests.Clear();
            _expireDay.Clear();
            if (quests != null)
                for (int i = 0; i < quests.Count; i++)
                {
                    int day = expireDays != null && i < expireDays.Count ? expireDays[i] : -1;
                    _quests.Add(quests[i]);
                    _expireDay[quests[i]] = day >= 0 ? day : GameClock.CurrentDay + MinValidDays - 1;
                }
            SortAndTrim();
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _hereDay = -1;
            _nextDay = -1;
            _quests.Clear();
            _expireDay.Clear();
            OnChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += HandleNewDay;
    }
}
