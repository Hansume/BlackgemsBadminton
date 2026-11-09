using Badminton.Core;
using NUnit.Framework;

namespace Badminton.Tests
{
    /// <summary>
    /// Luật 3×21 dùng chung cho mọi chế độ (GĐ1 Giải đấu): lịch sử tỷ số từng game, người thắng game giao trước,
    /// nghỉ ở 11, trần 30, và hàm kiểm tra tỷ số game hợp lệ (dùng cho bộ sinh kết quả trận AI).
    /// </summary>
    public class ScoringRulesTests
    {
        static readonly MatchRulesConfig R = new MatchRulesConfig();

        static void Win(MatchState m, int player, int times)
        {
            for (int i = 0; i < times; i++) m.AwardRally(player);
        }

        /// <summary>Đưa game hiện tại về tỷ số a–b (người 0–người 1) rồi cho winner ghi điểm quyết định.</summary>
        static void PlayGame(MatchState m, int a, int b)
        {
            // Đánh xen kẽ để không game nào kết thúc sớm, điểm cuối thuộc về người thắng.
            int winner = a > b ? 0 : 1;
            int wTarget = winner == 0 ? a : b, lTarget = winner == 0 ? b : a;
            int w = 0, l = 0;
            while (l < lTarget || w < wTarget - 1)
            {
                if (l < lTarget) { m.AwardRally(1 - winner); l++; }
                if (w < wTarget - 1) { m.AwardRally(winner); w++; }
            }
            m.AwardRally(winner);
        }

        [Test]
        public void Defaults_Are3x21_Cap30_IntervalAt11()
        {
            Assert.AreEqual(21, R.pointsToWin);
            Assert.AreEqual(30, R.pointCap);
            Assert.AreEqual(2, R.gamesToWin);
            Assert.AreEqual(11, R.intervalAt);
        }

        [Test]
        public void CompletedGames_RecordEveryGameScore_InPlayerOrder()
        {
            var m = new MatchState(R);
            PlayGame(m, 21, 15);
            PlayGame(m, 19, 21);
            PlayGame(m, 22, 20);

            Assert.IsTrue(m.IsOver);
            Assert.AreEqual(0, m.MatchWinner);
            Assert.AreEqual(3, m.CompletedGames.Count);
            Assert.AreEqual("21-15", m.CompletedGames[0].ToString());
            Assert.AreEqual("19-21", m.CompletedGames[1].ToString());
            Assert.AreEqual("22-20", m.CompletedGames[2].ToString());
            Assert.AreEqual(1, m.CompletedGames[1].Winner);
        }

        [Test]
        public void Cap_29All_Gives30_29_InHistory()
        {
            var m = new MatchState(R);
            Win(m, 0, 20); Win(m, 1, 20);
            for (int i = 0; i < 9; i++) { m.AwardRally(0); m.AwardRally(1); } // 29-29
            Assert.AreEqual(29, m.Score[0]);
            Assert.AreEqual(29, m.Score[1]);
            var o = m.AwardRally(1);
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual("29-30", m.CompletedGames[0].ToString());
        }

        [Test]
        public void GameWinner_ServesFirst_InNextGame()
        {
            var m = new MatchState(R, firstServer: 0);
            PlayGame(m, 17, 21); // người 1 thắng game 1
            Assert.AreEqual(2, m.GameNumber);
            Assert.AreEqual(1, m.Server, "Người thắng game giao cầu trước ở game sau");
            Assert.AreEqual(0, m.Score[0]);
            Assert.AreEqual(0, m.Score[1]);
            Assert.AreEqual(ServiceCourt.Right, m.ServeCourt, "0 điểm ⇒ giao từ ô phải");
        }

        [Test]
        public void Interval_TriggersWhenLeaderReaches11_NotWhenTrailerDoes()
        {
            var m = new MatchState(R);
            Win(m, 1, 10);
            Win(m, 0, 10);                                   // 10-10
            Assert.IsTrue(m.AwardRally(0).Interval, "11-10 ⇒ nghỉ");
            Assert.IsFalse(m.AwardRally(1).Interval, "11-11 ⇒ không nghỉ lần 2");
            Win(m, 1, 5);
            Assert.IsFalse(m.AwardRally(0).Interval);
        }

        [Test]
        public void DecidingGame_ChangeEndsAt11_OnlyInGame3()
        {
            var m = new MatchState(R);
            Win(m, 0, 10);
            Assert.IsFalse(m.AwardRally(0).ChangeEnds, "Game 1: đạt 11 chỉ nghỉ, không đổi sân");
            Win(m, 0, 10);                                   // 21-0, hết game 1
            Assert.AreEqual(2, m.GameNumber);
            PlayGame(m, 10, 21);                             // 1-1
            Assert.AreEqual(3, m.GameNumber);

            Win(m, 1, 10);
            var o = m.AwardRally(1);                         // 0-11 trong game 3
            Assert.IsTrue(o.Interval);
            Assert.IsTrue(o.ChangeEnds, "Game quyết định: đổi sân khi một bên đạt 11");
            Assert.AreEqual("21-0", m.CompletedGames[0].ToString());
            Assert.AreEqual("10-21", m.CompletedGames[1].ToString());
        }

        [TestCase(21, 0)] [TestCase(21, 19)] [TestCase(22, 20)] [TestCase(25, 23)]
        [TestCase(29, 27)] [TestCase(30, 28)] [TestCase(30, 29)] [TestCase(19, 21)]
        public void ValidFinalScores(int a, int b) =>
            Assert.IsTrue(MatchState.IsValidFinalGameScore(R, a, b), $"{a}-{b} phải hợp lệ");

        [TestCase(21, 20)] [TestCase(20, 18)] [TestCase(22, 19)] [TestCase(23, 20)] [TestCase(30, 27)]
        [TestCase(31, 29)] [TestCase(30, 30)] [TestCase(21, 21)] [TestCase(0, 0)] [TestCase(24, 21)]
        public void InvalidFinalScores(int a, int b) =>
            Assert.IsFalse(MatchState.IsValidFinalGameScore(R, a, b), $"{a}-{b} phải sai luật");

        /// <summary>
        /// Đối chiếu toàn bộ: tỷ số (w,l) là kết thúc hợp lệ ⇔ chính MatchState coi là thắng game
        /// VÀ trạng thái ngay trước điểm cuối (w-1,l) chưa kết thúc. Bảo đảm validator khớp 100% với luật.
        /// </summary>
        [Test]
        public void Validator_MatchesMatchStateRules_ForAllScores()
        {
            for (int w = 0; w <= 32; w++)
            for (int l = 0; l <= 32; l++)
            {
                bool expected = w > l
                    && MatchState.IsGameWon(R, w, l)
                    && !MatchState.IsGameWon(R, w - 1, l)
                    && !MatchState.IsGameWon(R, l, w - 1);
                if (w <= l) continue; // validator không phân biệt thứ tự; chỉ xét w > l
                Assert.AreEqual(expected, MatchState.IsValidFinalGameScore(R, w, l), $"{w}-{l}");
            }
        }
    }
}
