using LlamAcademy.Dinos.Companion;
using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Companion
{
    public sealed class CompanionCoreTests
    {
        [TestCase(CompanionMode.FollowPlayer, false, true)]
        [TestCase(CompanionMode.HoldPosition, false, true)]
        [TestCase(CompanionMode.HoldPosition, true, true)]
        [TestCase(CompanionMode.HoldPosition, false, false)]
        public void UnacceptedOrderPreservesActiveExecutionState(
            CompanionMode previousMode, bool clarification, bool running)
        {
            WithController((controller, type) =>
            {
                CompanionOrder previous = CompanionOrder.Default(previousMode);
                Apply(controller, type, previous);
                object expires = Field(type, "OrderExpiresAt").GetValue(controller);
                object anchor = Field(type, "HoldPositionAnchor").GetValue(controller);
                object hasAnchor = Field(type, "HasHoldPositionAnchor").GetValue(controller);
                object label = type.GetProperty("BehaviorLabel").GetValue(controller);
                controller.transform.position = new Vector3(20f, 0f, 30f);
                CompanionOrder rejected = CompanionOrderEvaluator.Evaluate(
                    CompanionProfileData.CreateDefault(),
                    new CompanionIntent { Mode = CompanionMode.AutonomousDefense,
                        Confidence = 0.2f, NeedsClarification = clarification },
                    new CompanionSituation { Running = running });
                Assert.That(rejected.Acceptance, Is.EqualTo(running ? OrderAcceptance.Rejected : OrderAcceptance.Deferred));
                int notifications = 0;
                Action changed = () => notifications++;
                type.GetEvent("StateChanged").AddEventHandler(controller, changed);

                Apply(controller, type, rejected);

                Assert.That(type.GetProperty("CurrentOrder").GetValue(controller), Is.SameAs(previous));
                Assert.That(Field(type, "OrderExpiresAt").GetValue(controller), Is.EqualTo(expires));
                Assert.That(Field(type, "HoldPositionAnchor").GetValue(controller), Is.EqualTo(anchor));
                Assert.That(Field(type, "HasHoldPositionAnchor").GetValue(controller), Is.EqualTo(hasAnchor));
                Assert.That(type.GetProperty("BehaviorLabel").GetValue(controller), Is.EqualTo(label));
                Assert.That(type.GetProperty("StatusMessage").GetValue(controller), Is.EqualTo(rejected.Message));
                Assert.That(notifications, Is.EqualTo(1));
            });
        }

        [TestCase(OrderAcceptance.Accepted)]
        [TestCase(OrderAcceptance.Partial)]
        public void ExecutableOrderReplacesActiveOrder(OrderAcceptance acceptance)
        {
            WithController((controller, type) =>
            {
                Apply(controller, type, CompanionOrder.Default(CompanionMode.FollowPlayer));
                controller.transform.position = new Vector3(4f, 0f, 8f);
                CompanionOrder next = CompanionOrder.Default(CompanionMode.HoldPosition);
                next.Acceptance = acceptance;
                Apply(controller, type, next);
                Assert.That(type.GetProperty("CurrentOrder").GetValue(controller), Is.SameAs(next));
                Assert.That(Field(type, "HasHoldPositionAnchor").GetValue(controller), Is.EqualTo(true));
                Assert.That(Field(type, "HoldPositionAnchor").GetValue(controller), Is.EqualTo(controller.transform.position));
            });
        }

        private static FieldInfo Field(Type type, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static void Apply(Component controller, Type type, CompanionOrder order) =>
            type.GetMethod("ApplyOrder").Invoke(controller, new object[] { order, "test" });

        private static void WithController(Action<Component, Type> test)
        {
            // Runtime lives in Assembly-CSharp; keep the Core test assembly independent.
            Type type = Type.GetType("LlamAcademy.Dinos.Companion.CompanionController, Assembly-CSharp", true);
            GameObject host = new GameObject("Companion order regression");
            host.SetActive(false);
            try
            {
                Component controller = host.AddComponent(type);
                type.GetMethod("ApplyProfile").Invoke(controller,
                    new object[] { CompanionProfileData.CreateDefault() });
                test(controller, type);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LocalParserUnderstandsChineseNegativeAndAutonomy()
        {
            CompanionIntent stay = CompanionLocalIntentParser.Parse("不要离开，待在我身边");
            Assert.That(stay.Mode, Is.EqualTo(CompanionMode.FollowPlayer));
            Assert.That(stay.Confidence, Is.GreaterThan(0.8f));

            CompanionIntent autonomy = CompanionLocalIntentParser.Parse("你可以离开，自主防卫村口");
            Assert.That(autonomy.Mode, Is.EqualTo(CompanionMode.AutonomousDefense));
            Assert.That(autonomy.ExplicitAutonomy, Is.True);
        }

        [Test]
        public void LowTrustAutonomyIsPartiallyAcceptedWithinSafetyRadius()
        {
            CompanionProfileData profile = CompanionProfileData.CreateDefault();
            profile.Trust = 15f;
            profile.Familiarity = 8f;
            CompanionIntent intent = new CompanionIntent
            {
                Mode = CompanionMode.AutonomousDefense,
                Confidence = 0.85f,
                ExplicitAutonomy = false,
            };
            CompanionOrder order = CompanionOrderEvaluator.Evaluate(profile, intent, new CompanionSituation { Running = true });
            Assert.That(order.Acceptance, Is.EqualTo(OrderAcceptance.Partial));
            Assert.That(order.LeashRadius, Is.LessThanOrEqualTo(24f));
        }

        [Test]
        public void ExplicitAutonomyDoesNotBypassRelationshipFloor()
        {
            CompanionProfileData profile = CompanionProfileData.CreateDefault();
            profile.Trust = 10f;
            profile.Familiarity = 10f;
            CompanionOrder order = CompanionOrderEvaluator.Evaluate(profile, new CompanionIntent
            {
                Mode = CompanionMode.AutonomousDefense,
                Confidence = 1f,
                ExplicitAutonomy = true,
            }, new CompanionSituation { Running = true });
            Assert.That(order.Acceptance, Is.EqualTo(OrderAcceptance.Partial));
            Assert.That(order.ReasonCode, Is.EqualTo(CompanionOrderEvaluator.LowTrust));
        }

        [Test]
        public void EmergencyPlayerThreatOverridesRequestedMode()
        {
            CompanionOrder order = CompanionOrderEvaluator.Evaluate(
                CompanionProfileData.CreateDefault(),
                new CompanionIntent { Mode = CompanionMode.HoldPosition, Confidence = 1f },
                new CompanionSituation { Running = true, PlayerThreatened = true });
            Assert.That(order.Acceptance, Is.EqualTo(OrderAcceptance.Accepted));
            Assert.That(order.Mode, Is.EqualTo(CompanionMode.ProtectPlayer));
            Assert.That(order.ReasonCode, Is.EqualTo(CompanionOrderEvaluator.PlayerUnderThreat));
        }
    }
}
