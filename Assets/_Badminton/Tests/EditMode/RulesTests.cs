using Badminton.Core;
using NUnit.Framework;
using UnityEngine;

namespace Badminton.Tests
{
    /// <summary>Kiểm tra luật tính điểm (MatchState) – thuần C#, chạy offline không cần scene.</summary>
    public class MatchStateTests
    {
        static MatchState New() => new MatchState(new MatchRulesConfig(), firstServer: 0);

        static void Win(MatchState m, int player, int times)
        {
            for (int i = 0; i < times; i++) m.AwardRally(player);
        }

        [Test]
        public void Game_IsWonAt21_WithTwoPointLead()
        {
            var m = New();
            Win(m, 0, 20);
            Win(m, 1, 18);
            var o = m.AwardRally(0); // 21-18
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, m.Games[0]);
            Assert.AreEqual(0, m.Score[0], "Điểm reset khi sang game mới");
        }

        [Test]
        public void Deuce_RequiresTwoPointLead()
        {
            var m = New();
            Win(m, 0, 20); Win(m, 1, 20);       // 20-20
            Assert.IsFalse(m.AwardRally(0).GameWon); // 21-20
            Assert.IsFalse(m.AwardRally(1).GameWon); // 21-21
            Assert.IsFalse(m.AwardRally(1).GameWon); // 21-22
            Assert.IsTrue(m.AwardRally(1).GameWon);  // 21-23
        }

        [Test]
        public void At29All_NextPointWins()
        {
            var m = New();
            Win(m, 0, 20); Win(m, 1, 20);
            for (int i = 0; i < 9; i++) { m.AwardRally(0); m.AwardRally(1); } // 29-29
            Assert.AreEqual(29, m.Score[0]);
            var o = m.AwardRally(1); // 29-30
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, o.GameWinner);
        }

        [Test]
        public void ServeCourt_FollowsServerScoreParity_AndWinnerServes()
        {
            var m = New();
            Assert.AreEqual(ServiceCourt.Right, m.ServeCourt, "0 điểm (chẵn) ⇒ ô phải");
            m.AwardRally(0);                     // 1-0, người 0 giao
            Assert.AreEqual(0, m.Server);
            Assert.AreEqual(ServiceCourt.Left, m.ServeCourt, "1 điểm (lẻ) ⇒ ô trái");
            m.AwardRally(1);                     // 1-1, người 1 giành quyền giao
            Assert.AreEqual(1, m.Server);
            Assert.AreEqual(ServiceCourt.Left, m.ServeCourt);
            m.AwardRally(1);                     // 1-2
            Assert.AreEqual(ServiceCourt.Right, m.ServeCourt);
        }

        [Test]
        public void Interval_At11_OnlyOncePerGame()
        {
            var m = New();
            Win(m, 0, 10);
            Assert.IsTrue(m.AwardRally(0).Interval);   // 11-0
            Assert.IsFalse(m.AwardRally(1).Interval);
            Win(m, 1, 10);                             // 11-11
            Assert.IsFalse(m.AwardRally(1).Interval);
        }

        [Test]
        public void Ends_ChangeAfterEachGame_AndAt11InDecider()
        {
            var m = new MatchState(new MatchRulesConfig { swapSides = true });
            Assert.AreEqual(CourtSide.Near, m.Side[0]);
            Win(m, 0, 20);
            Assert.IsTrue(m.AwardRally(0).ChangeEnds); // hết game 1
            Assert.AreEqual(CourtSide.Far, m.Side[0]);
            Assert.AreEqual(0, m.Server, "Người thắng game giao trước");

            Win(m, 1, 21);                             // người 1 thắng game 2 ⇒ 1-1, sang game quyết định
            Assert.AreEqual(CourtSide.Near, m.Side[0]);
            Assert.IsTrue(m.IsDecidingGame);

            Win(m, 1, 10);
            var o = m.AwardRally(1);                   // 0-11 ở game 3
            Assert.IsTrue(o.Interval);
            Assert.IsTrue(o.ChangeEnds);
            Assert.AreEqual(CourtSide.Far, m.Side[0]);
        }

        [Test]
        public void Ends_AnnouncedButSidesKept_WhenSwapDisabled()
        {
            var m = new MatchState(new MatchRulesConfig { swapSides = false });
            Win(m, 0, 20);
            Assert.IsTrue(m.AwardRally(0).ChangeEnds, "Vẫn báo đổi sân để UI thông báo");
            Assert.AreEqual(CourtSide.Near, m.Side[0], "Không đổi vị trí thật");
            Assert.AreEqual(CourtSide.Far, m.Side[1]);

            Win(m, 1, 21);
            Win(m, 1, 10);
            var o = m.AwardRally(1);                   // 0-11 ở game quyết định
            Assert.IsTrue(o.ChangeEnds);
            Assert.AreEqual(CourtSide.Near, m.Side[0]);
        }

        [Test]
        public void Match_IsBestOfThree()
        {
            var m = New();
            Win(m, 1, 21);
            Assert.IsFalse(m.IsOver);
            Win(m, 1, 20);
            var o = m.AwardRally(1);
            Assert.IsTrue(o.MatchWon);
            Assert.AreEqual(1, m.MatchWinner);
            Assert.Throws<System.InvalidOperationException>(() => m.AwardRally(0));
        }
    }

    /// <summary>Kiểm tra trọng tài pha cầu (RallyJudge) và xét ô giao cầu.</summary>
    public class RallyJudgeTests
    {
        static readonly CourtSide[] Sides = { CourtSide.Near, CourtSide.Far }; // người 0 ở Near

        static RallyJudge Serve(ServiceCourt court)
        {
            var j = new RallyJudge();
            j.BeginRally(0, court, Sides);
            j.OnHit(0); // cú giao cầu của người 0
            return j;
        }

        [Test]
        public void ServiceCourt_RightForReceiverOnFarSide_IsNegativeX()
        {
            // Người nhận ở Far nhìn về −Z ⇒ bên phải của họ là −X.
            Assert.IsTrue(CourtDimensions.IsInsideSinglesServiceCourt(-1.2f, 4f, CourtSide.Far, ServiceCourt.Right));
            Assert.IsFalse(CourtDimensions.IsInsideSinglesServiceCourt(1.2f, 4f, CourtSide.Far, ServiceCourt.Right));
            Assert.IsTrue(CourtDimensions.IsInsideSinglesServiceCourt(1.2f, -4f, CourtSide.Near, ServiceCourt.Right));
        }

        [Test]
        public void Serve_IntoDiagonalCourt_IsIn()
        {
            var r = Serve(ServiceCourt.Right).OnLanded(new Vector3(-1.3f, 0f, 5f));
            Assert.AreEqual(0, r.Value.Winner);
            Assert.AreEqual(RallyEndReason.In, r.Value.Reason);
        }

        [Test]
        public void Serve_IntoWrongCourt_IsServiceFault()
        {
            var r = Serve(ServiceCourt.Right).OnLanded(new Vector3(1.3f, 0f, 5f));
            Assert.AreEqual(1, r.Value.Winner);
            Assert.AreEqual(RallyEndReason.ServiceFault, r.Value.Reason);
        }

        [Test]
        public void Serve_ShortOfShortServiceLine_IsFault_LineCountsIn()
        {
            Assert.AreEqual(RallyEndReason.ServiceFault, Serve(ServiceCourt.Right).OnLanded(new Vector3(-1f, 0f, 1.9f)).Value.Reason);
            Assert.AreEqual(RallyEndReason.In, Serve(ServiceCourt.Right).OnLanded(new Vector3(-1f, 0f, 1.98f)).Value.Reason);
        }

        [Test]
        public void Rally_LandingOnLine_IsIn_BeyondIsOut()
        {
            var j = Serve(ServiceCourt.Right);
            j.OnHit(1);                                        // người 1 trả cầu
            var r = j.OnLanded(new Vector3(2.59f, 0f, -6.7f)); // đúng góc vạch biên đơn
            Assert.AreEqual(1, r.Value.Winner);
            Assert.AreEqual(RallyEndReason.In, r.Value.Reason);

            j = Serve(ServiceCourt.Right);
            j.OnHit(1);
            r = j.OnLanded(new Vector3(2.62f, 0f, -3f));       // ngoài biên dọc đơn 3 cm
            Assert.AreEqual(0, r.Value.Winner);
            Assert.AreEqual(RallyEndReason.Out, r.Value.Reason);
        }

        [Test]
        public void Shuttle_FallingBackOnHittersSide_IsNotOverNet()
        {
            var j = Serve(ServiceCourt.Right);
            j.OnHit(1);
            var r = j.OnLanded(new Vector3(0f, 0f, 0.3f)); // người 1 (Far) đánh vào lưới, rơi bên sân mình
            Assert.AreEqual(0, r.Value.Winner);
            Assert.AreEqual(RallyEndReason.NotOverNet, r.Value.Reason);
        }

        [Test]
        public void UnderNet_IsFaultOfLastHitter()
        {
            var j = Serve(ServiceCourt.Left);
            j.OnHit(1);
            Assert.AreEqual(0, j.OnUnderNet().Value.Winner);
            Assert.IsTrue(j.Finished);
            Assert.IsNull(j.OnLanded(Vector3.zero), "Pha cầu đã kết thúc thì bỏ qua sự kiện sau");
        }
    }
}
