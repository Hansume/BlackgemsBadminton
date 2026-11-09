using Badminton.Core;
using NUnit.Framework;
using UnityEngine;

namespace Badminton.Tests
{
    /// <summary>GĐ5: luật giao cầu đánh đôi + sân đôi.</summary>
    public class DoublesRulesTests
    {
        // Đội 0 = A (thành viên 0) & B (thành viên 1); Đội 1 = C (0) & D (1). Ban đầu A, C ở ô phải.
        static string Who(int team, int member) => team == 0 ? (member == 0 ? "A" : "B") : (member == 0 ? "C" : "D");

        [Test]
        public void ServeRotation_FollowsLawsExample()
        {
            var m = new MatchState(new MatchRulesConfig(), firstServer: 0);
            var d = new DoublesServeState();

            void Expect(string server, ServiceCourt court, string receiver)
            {
                Assert.AreEqual(court, m.ServeCourt, "ô giao");
                Assert.AreEqual(server, Who(m.Server, d.ServerMember(m)), "người giao");
                Assert.AreEqual(receiver, Who(m.Receiver, d.ReceiverMember(m)), "người nhận");
            }
            void Point(int team)
            {
                int before = m.Server;
                var o = m.AwardRally(team);
                d.OnRally(team, before, o.GameWon);
            }

            Expect("A", ServiceCourt.Right, "C");   // 0-0
            Point(0); Expect("A", ServiceCourt.Left, "D");    // 1-0: bên giao thắng ⇒ A đổi ô, giao từ ô trái
            Point(1); Expect("D", ServiceCourt.Left, "A");    // 1-1: bên nhận thắng ⇒ D (đang ở ô trái) giao
            Point(0); Expect("B", ServiceCourt.Right, "C");   // 2-1: AB giành giao; 2 điểm ⇒ ô phải = B
            Point(1); Expect("C", ServiceCourt.Right, "B");   // 2-2: CD giành giao; 2 điểm ⇒ ô phải = C
            Point(1); Expect("C", ServiceCourt.Left, "A");    // 2-3: C thắng khi giao ⇒ đổi ô, giao từ trái
            Point(1); Expect("C", ServiceCourt.Right, "B");   // 2-4
        }

        [Test]
        public void NewGame_ResetsPositions()
        {
            var m = new MatchState(new MatchRulesConfig(), firstServer: 0);
            var d = new DoublesServeState();
            for (int i = 0; i < 21; i++)
            {
                int before = m.Server;
                var o = m.AwardRally(1);
                d.OnRally(1, before, o.GameWon);
            }
            Assert.AreEqual(2, m.GameNumber);
            Assert.AreEqual(1, m.Server, "Đội thắng game giao trước");
            Assert.AreEqual(0, d.ServerMember(m), "Về vị trí ban đầu: thành viên 0 ở ô phải");
        }

        [Test]
        public void DoublesServiceCourt_IsShorterAndWider()
        {
            // Người nhận ở sân xa (Far), ô phải của họ là phía X âm.
            float z = 5.2f;   // trong ô giao đôi
            Assert.IsTrue(CourtDimensions.IsInsideDoublesServiceCourt(-1.0f, z, CourtSide.Far, ServiceCourt.Right));
            // Rộng hơn: 2.9 m ra ngoài biên đơn (2.59) vẫn TRONG ô giao đôi.
            Assert.IsTrue(CourtDimensions.IsInsideDoublesServiceCourt(-2.9f, z, CourtSide.Far, ServiceCourt.Right));
            Assert.IsFalse(CourtDimensions.IsInsideSinglesServiceCourt(-2.9f, z, CourtSide.Far, ServiceCourt.Right));
            // Ngắn hơn: 6.2 m (sau vạch giao cầu dài 5.94) là NGOÀI ô giao đôi nhưng TRONG ô giao đơn.
            Assert.IsFalse(CourtDimensions.IsInsideDoublesServiceCourt(-1.0f, 6.2f, CourtSide.Far, ServiceCourt.Right));
            Assert.IsTrue(CourtDimensions.IsInsideSinglesServiceCourt(-1.0f, 6.2f, CourtSide.Far, ServiceCourt.Right));
        }

        [Test]
        public void Judge_Doubles_WrongReceiver_LosesPoint()
        {
            var sides = new[] { CourtSide.Near, CourtSide.Far };
            var j = new RallyJudge();
            // Đội 0 giao, người nhận được chỉ định là thành viên 1 của đội 1.
            j.BeginRally(0, ServiceCourt.Right, sides, doubles: true, receiverMember: 1);
            Assert.IsNull(j.OnHit(0, 0), "cú giao cầu");
            var r = j.OnHit(1, 0); // đồng đội của người nhận đỡ giao cầu ⇒ lỗi
            Assert.IsNotNull(r);
            Assert.AreEqual(RallyEndReason.WrongReceiver, r.Value.Reason);
            Assert.AreEqual(0, r.Value.Winner, "Đội giao được điểm");

            j.BeginRally(0, ServiceCourt.Right, sides, doubles: true, receiverMember: 1);
            j.OnHit(0, 0);
            Assert.IsNull(j.OnHit(1, 1), "Đúng người nhận ⇒ hợp lệ");
            Assert.IsNull(j.OnHit(0, 1), "Từ cú thứ 3 ai đánh cũng được");
        }

        [Test]
        public void Judge_Doubles_UsesOuterSidelines()
        {
            var j = new RallyJudge();
            j.BeginRally(0, ServiceCourt.Right, new[] { CourtSide.Near, CourtSide.Far }, doubles: true);
            j.OnHit(0);
            Assert.AreEqual(RallyEndReason.In, j.OnLanded(new Vector3(-1.2f, 0f, 4.5f)).Value.Reason, "giao vào ô đôi");
            j.BeginRally(0, ServiceCourt.Right, new[] { CourtSide.Near, CourtSide.Far }, doubles: true);
            j.OnHit(0);
            Assert.AreEqual(RallyEndReason.ServiceFault, j.OnLanded(new Vector3(-1.2f, 0f, 6.3f)).Value.Reason, "giao quá vạch giao cầu dài đôi");

            j.BeginRally(0, ServiceCourt.Right, new[] { CourtSide.Near, CourtSide.Far }, doubles: true);
            j.OnHit(0); j.OnHit(1); j.OnHit(0);
            var r = j.OnLanded(new Vector3(2.9f, 0f, 3f)).Value; // ngoài biên đơn, trong biên đôi
            Assert.AreEqual(RallyEndReason.In, r.Reason);
            Assert.AreEqual(0, r.Winner);
        }
    }
}
