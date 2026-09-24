using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlasmaShared;
using ZkData;
using ZkData.UnitSyncLib;

namespace Tests.Database
{
    /// <summary>
    /// The mission service's JSON messages, through the serializer that carries them.
    ///
    /// **The claim being checked is that the envelope changed and the contract did not.** WCF
    /// serialised `Mission` by its `[DataContract]`/`[DataMember]` attributes and `MissionSlot`
    /// and `Mod` by `[Serializable]`. Json.NET honours the first and has its own rules for the
    /// second, so "the same fields cross" is an assertion rather than an observation - and if it
    /// is wrong, a mission published from the editor loses part of itself on the way in, which is
    /// the kind of thing noticed weeks later by whoever opens it again.
    ///
    /// No database and no web server: this is the wire format, which is what changed.
    /// </summary>
    [TestClass]
    public class MissionServiceRoundTripTests
    {
        private static readonly CommandJsonSerializer Serializer =
            new CommandJsonSerializer(
                Utils.GetAllTypesWithAttribute<ApiMessageAttribute>().Concat(MissionServiceApi.MessageTypes));

        private static T RoundTrip<T>(T message) where T : class =>
            Serializer.DeserializeLine(Serializer.SerializeToLine(message)) as T;

        [TestMethod]
        public void A_mission_keeps_its_contracted_fields()
        {
            var sent = new SendMissionRequest
            {
                Author = "author",
                Password = "secret",
                Mission = new Mission
                {
                    MissionID = 17,
                    Name = "Test Mission",
                    Description = "does things",
                    Script = "GameType=old;",
                    Mutator = new byte[] { 1, 2, 3 },
                    Image = new byte[] { 4, 5 },
                },
            };

            var back = RoundTrip(sent);

            Assert.AreEqual("author", back.Author);
            Assert.AreEqual("secret", back.Password);
            Assert.AreEqual(17, back.Mission.MissionID);
            Assert.AreEqual("Test Mission", back.Mission.Name);
            Assert.AreEqual("does things", back.Mission.Description);
            Assert.AreEqual("GameType=old;", back.Mission.Script);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, back.Mission.Mutator,
                "the mutator is the mission's actual payload");
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, back.Mission.Image);
        }

        [TestMethod]
        public void A_slot_keeps_every_field_it_has()
        {
            // MissionSlot is [Serializable] with public fields and no [DataMember]. WCF wrote all
            // of its fields; this checks Json.NET does too, field by field rather than by spot
            // check, because a silently dropped one is a slot that loses its team or its AI.
            var slot = new MissionSlot
            {
                AiShortName = "CAI", AiVersion = "1.0", AllyID = 2, AllyName = "Allies",
                Color = 4567, IsHuman = true, IsRequired = true, TeamID = 3, TeamName = "Team",
            };

            var back = RoundTrip(new SendMissionRequest { Slots = new List<MissionSlot> { slot } })
                .Slots.Single();

            foreach (var field in typeof(MissionSlot).GetFields())
                Assert.AreEqual(field.GetValue(slot), field.GetValue(back), field.Name + " was lost");
        }

        [TestMethod]
        public void Every_operation_round_trips()
        {
            Assert.AreEqual(9, RoundTrip(new DeleteMissionRequest { MissionID = 9, Delete = true }).MissionID);
            Assert.IsFalse(RoundTrip(new DeleteMissionRequest { MissionID = 9, Delete = false }).Delete,
                "undelete is the same message with the flag off, so the flag has to survive");
            Assert.AreEqual("boom", RoundTrip(new DeleteMissionResponse { Error = "boom" }).Error);

            Assert.AreEqual("by name", RoundTrip(new GetMissionRequest { MissionName = "by name" }).MissionName);
            Assert.AreEqual(4, RoundTrip(new GetMissionRequest { MissionID = 4 }).MissionID);
            Assert.IsNull(RoundTrip(new GetMissionRequest { MissionName = "x" }).MissionID,
                "an absent id must stay absent, not become zero, or GetMission picks the wrong lookup");

            Assert.IsNotNull(RoundTrip(new ListMissionInfosRequest()));
            Assert.AreEqual(2, RoundTrip(new ListMissionInfosResponse
            {
                Missions = new List<Mission> { new Mission { MissionID = 1 }, new Mission { MissionID = 2 } },
            }).Missions.Count);

            Assert.AreEqual("nope", RoundTrip(new SendMissionResponse { Error = "nope" }).Error);
        }

        [TestMethod]
        public void A_response_with_no_error_is_how_success_looks()
        {
            // The WCF operations returned void and threw on failure; these return a response whose
            // Error is null. A caller that checks for null gets the same answer either way.
            Assert.IsNull(RoundTrip(new DeleteMissionResponse()).Error);
            Assert.IsNull(RoundTrip(new SendMissionResponse()).Error);
            Assert.IsNull(RoundTrip(new GetMissionResponse()).Mission, "no mission found is not an error");
        }
    }
}
