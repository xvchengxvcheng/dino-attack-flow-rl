using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class DinoCombatBehaviorPlayModeTests
    {
        private const string VelociraptorPrefabPath =
            "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab";
        private const string ArcherPrefabPath =
            "Assets/LlamAcademy/Dinos/Enemy AI/Archer/Archer.prefab";

        private static int enteredTargets;
        private static int exitedTargets;
        private static int trackerChanges;
        private static int deathEvents;
        private static int damageEvents;

        [UnityTest]
        public IEnumerator DinoTargetTracker_DeduplicatesAndRemovesTheTargetOnDeath()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            object tracker = Activator.CreateInstance(RuntimeType("LlamAcademy.Dinos.Unit.DinoTargetTracker"));

            Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.True);
            Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.False,
                "a second collider or trigger callback must not create a duplicate logical target");
            Assert.That(CopyTargets(tracker), Is.EqualTo(new[] { target }));

            TakeLethalDamage(damageable);
            yield return null;

            Assert.That(ReadProperty<bool>(tracker, "HasLiveTarget"), Is.False);
            Assert.That(CopyTargets(tracker), Is.Empty);
            InvokeVoid(tracker, "Dispose");
            yield return Cleanup(target);
        }

        [UnityTest]
        public IEnumerator ThreeDinoTrackers_SharedTargetDeathClearsEveryTargetSet()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            Type trackerType = RuntimeType("LlamAcademy.Dinos.Unit.DinoTargetTracker");
            object[] trackers = Enumerable.Range(0, 3)
                .Select(_ => Activator.CreateInstance(trackerType))
                .ToArray();

            foreach (object tracker in trackers)
            {
                Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.True);
                Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.False);
                Assert.That(CopyTargets(tracker), Is.EqualTo(new[] { target }));
            }

            TakeLethalDamage(damageable);
            yield return null;

            foreach (object tracker in trackers)
            {
                Assert.That(ReadProperty<bool>(tracker, "HasLiveTarget"), Is.False);
                Assert.That(CopyTargets(tracker), Is.Empty,
                    "one shared target death must independently clear every dinosaur's target set");
                InvokeVoid(tracker, "Dispose");
            }

            yield return Cleanup(target);
        }

        [UnityTest]
        public IEnumerator DinoTargetTracker_ClearIsIdempotentAfterTargetDestruction()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            object tracker = Activator.CreateInstance(RuntimeType("LlamAcademy.Dinos.Unit.DinoTargetTracker"));
            Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.True);

            trackerChanges = 0;
            SubscribeToActionEvent(tracker, "Changed", CaptureTrackerChange);
            InvokeVoid(tracker, "Clear");
            InvokeVoid(tracker, "Clear");
            Assert.That(trackerChanges, Is.EqualTo(1), "only the first clear can change membership");

            UnityEngine.Object.Destroy(target);
            yield return null;

            Assert.DoesNotThrow(() => ReadProperty<bool>(tracker, "HasLiveTarget"),
                "cleanup must not dereference a Unity-destroyed target");
            Assert.That(trackerChanges, Is.EqualTo(1),
                "a destroyed target already removed by Clear cannot emit another tracker change");
            InvokeVoid(tracker, "Dispose");
        }

        [UnityTest]
        public IEnumerator AttackRadius_MultiplePhysicalEntriesEmitOneLogicalEnterAndOneDeathExit()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            Collider targetCollider = target.GetComponent<Collider>() ?? target.AddComponent<BoxCollider>();
            GameObject sensorObject = new GameObject("AttackRadiusSensor");
            SphereCollider sensorCollider = sensorObject.AddComponent<SphereCollider>();
            sensorCollider.isTrigger = true;
            Component sensor = sensorObject.AddComponent(RuntimeType("LlamAcademy.Dinos.Unit.AttackRadius"));

            enteredTargets = 0;
            exitedTargets = 0;
            SubscribeToTargetEvent(sensor, "OnTargetEnter", CaptureTargetEnter);
            SubscribeToTargetEvent(sensor, "OnTargetExit", CaptureTargetExit);

            InvokeVoid(sensor, "OnTriggerEnter", targetCollider);
            InvokeVoid(sensor, "OnTriggerEnter", targetCollider);
            Assert.That(enteredTargets, Is.EqualTo(1),
                "two physical overlap callbacks for the same target must yield one logical enter");

            TakeLethalDamage(damageable);
            yield return null;

            Assert.That(exitedTargets, Is.EqualTo(1),
                "a target death must yield one logical exit even if it owns several colliders");
            yield return Cleanup(target, sensorObject);
        }

        [UnityTest]
        public IEnumerator AttackRadius_ReplaysLiveOverlapWhenUnitSubscribesAfterThePhysicsEnter()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            Collider targetCollider = target.GetComponent<Collider>() ?? target.AddComponent<BoxCollider>();
            GameObject sensorObject = new GameObject("LateSubscriberAttackRadiusSensor");
            SphereCollider sensorCollider = sensorObject.AddComponent<SphereCollider>();
            sensorCollider.isTrigger = true;
            Component sensor = sensorObject.AddComponent(RuntimeType("LlamAcademy.Dinos.Unit.AttackRadius"));

            enteredTargets = 0;
            InvokeVoid(sensor, "OnTriggerEnter", targetCollider);
            Assert.That(enteredTargets, Is.Zero,
                "the fixed-step overlap happens before Unit.Start subscribes in the production race");

            Delegate replayReceiver = CreateTargetEventDelegate(sensor, "OnTargetEnter", CaptureTargetEnter);
            InvokeVoid(sensor, "ReplayCurrentTargets", replayReceiver);

            Assert.That(enteredTargets, Is.EqualTo(1),
                "a late subscriber must receive every currently alive logical overlap before the graph starts");
            yield return Cleanup(target, sensorObject);
        }

        [UnityTest]
        public IEnumerator DamageableState_RejectsInactiveAndDestroyedUnityTargets()
        {
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            Type stateType = RuntimeType("LlamAcademy.Dinos.Unit.DamageableState");
            MethodInfo isAlive = stateType.GetMethod("IsAlive", BindingFlags.Public | BindingFlags.Static);
            Assert.That(isAlive, Is.Not.Null);
            Assert.That((bool)isAlive.Invoke(null, new object[] { damageable }), Is.True);

            target.SetActive(false);
            Assert.That((bool)isAlive.Invoke(null, new object[] { damageable }), Is.False);

            UnityEngine.Object.Destroy(target);
            yield return null;

            Assert.DoesNotThrow(() => isAlive.Invoke(null, new object[] { damageable }));
            Assert.That((bool)isAlive.Invoke(null, new object[] { damageable }), Is.False);
        }

        [UnityTest]
        public IEnumerator Defender_LethalDamageAndDestroyRaiseDeathExactlyOnce()
        {
            InputTestFixture input = new();
            input.Setup();
            try
            {
                InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component defender = FindRuntimeComponents("LlamAcademy.Dinos.Enemy.Defender").First();
                deathEvents = 0;
                SubscribeToDeathEvent(defender, CaptureDeath);

                TakeLethalDamage(defender);
                TakeLethalDamage(defender);
                yield return new WaitForSeconds(3f);

                Assert.That(deathEvents, Is.EqualTo(1),
                    "one defender lifecycle must publish death once even when lethal damage and delayed cleanup repeat");
            }
            finally
            {
                DisableRuntimeBehaviour("LlamAcademy.Dinos.UI.RuntimeUI");
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Player.DinoSpawner");
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Defender_DisableWithoutSceneManagersCleansUpWithoutException()
        {
            List<string> exceptions = new();
            Application.LogCallback captureException = (condition, _, type) =>
            {
                if (type == LogType.Exception) exceptions.Add(condition);
            };
            Application.logMessageReceived += captureException;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                GameObject defender = UnityEngine.Object.Instantiate(LoadAssetAtPath(ArcherPrefabPath) as GameObject);
                UnityEngine.Object.Destroy(defender);
                yield return null;
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Application.logMessageReceived -= captureException;
            }
            Assert.That(exceptions, Is.Empty,
                "disabling a defender without scene managers must not throw during cleanup");
        }

        [UnityTest]
        public IEnumerator Defender_StartupReplaysDinoAlreadyInsideAttackRadius()
        {
            InputTestFixture input = new();
            input.Setup();
            GameObject spawnedDefender = null;
            GameObject spawnedDino = null;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component existingDefender = FindRuntimeComponents(
                    "LlamAcademy.Dinos.Enemy.Defender").First();
                GameObject defenderPrefab = LoadAssetAtPath(ArcherPrefabPath) as GameObject;
                spawnedDefender = UnityEngine.Object.Instantiate(
                    defenderPrefab,
                    existingDefender.transform.position,
                    existingDefender.transform.rotation);
                spawnedDino = UnityEngine.Object.Instantiate(
                    LoadAssetAtPath(VelociraptorPrefabPath) as GameObject,
                    existingDefender.transform.position + Vector3.right,
                    Quaternion.identity);
                Component defender = FindDamageable(spawnedDefender);
                Component dino = FindDamageable(spawnedDino);
                MakeDamageableAlive(dino);
                Collider dinoCollider = spawnedDino.GetComponentsInChildren<Collider>(true)
                    .First(collider => !collider.isTrigger);
                Component sensor = spawnedDefender.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.AttackRadius");

                InvokeVoid(sensor, "OnTriggerEnter", dinoCollider);
                Assert.That(ReadGraphGameObjectList(spawnedDefender, "NearbyAttackables"), Is.Empty,
                    "the regression setup must place the Dino in the sensor before Defender.Start subscribes");

                InvokeVoid(defender, "Start");

                Assert.That(ReadGraphGameObjectList(spawnedDefender, "NearbyAttackables"),
                    Is.EqualTo(new[] { spawnedDino }),
                    "Defender startup must replay a Dino that entered before its sensor subscription");
                Assert.That(ReadProperty<object>(defender, "State").ToString(), Is.EqualTo("Attacking"));
            }
            finally
            {
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Utility.HealthBarCanvas");
                if (spawnedDino != null) UnityEngine.Object.Destroy(spawnedDino);
                if (spawnedDefender != null) UnityEngine.Object.Destroy(spawnedDefender);
                DisableRuntimeBehaviour("LlamAcademy.Dinos.UI.RuntimeUI");
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Player.DinoSpawner");
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Defender_DoesNotLoseDinoThatOverlapsBeforeDinoStart()
        {
            InputTestFixture input = new();
            input.Setup();
            GameObject spawnedDino = null;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component defender = FindRuntimeComponents("LlamAcademy.Dinos.Enemy.Defender")
                    .First(component => component.name.StartsWith("Ground", StringComparison.Ordinal));
                Component sensor = defender.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.AttackRadius");
                spawnedDino = UnityEngine.Object.Instantiate(
                    LoadAssetAtPath(VelociraptorPrefabPath) as GameObject,
                    defender.transform.position + Vector3.forward,
                    Quaternion.identity);
                Component dino = FindDamageable(spawnedDino);
                Collider dinoCollider = spawnedDino.GetComponentsInChildren<Collider>(true)
                    .First(collider => !collider.isTrigger);

                InvokeVoid(sensor, "OnTriggerEnter", dinoCollider);
                InvokeVoid(dino, "Start");

                Assert.That(ReadGraphGameObjectList(defender.gameObject, "NearbyAttackables"),
                    Is.EqualTo(new[] { spawnedDino }),
                    "a guard must not discard an AI-deployed Dino just because its Start runs after the first overlap");
                Assert.That(ReadProperty<object>(defender, "State").ToString(), Is.EqualTo("Attacking"));
                Behaviour dinoGraph = (Behaviour)spawnedDino.GetComponent(
                    RuntimeType("Unity.Behavior.BehaviorGraphAgent"));
                dinoGraph.enabled = false;
                int healthBeforeGuardAttack = ReadProperty<int>(dino, "Health");

                yield return new WaitForSeconds(3f);

                Assert.That(ReadProperty<int>(dino, "Health"), Is.LessThan(healthBeforeGuardAttack),
                    "the guard must advance from target registration to a real Enemy Graph attack");
            }
            finally
            {
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Utility.HealthBarCanvas");
                if (spawnedDino != null) UnityEngine.Object.Destroy(spawnedDino);
                DisableRuntimeBehaviour("LlamAcademy.Dinos.UI.RuntimeUI");
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Player.DinoSpawner");
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Defender_FirstTargetDeathRetainsSecondThenLeavesAttackingState()
        {
            InputTestFixture input = new();
            input.Setup();
            GameObject firstDino = null;
            GameObject secondDino = null;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component defender = FindRuntimeComponents(
                    "LlamAcademy.Dinos.Enemy.Defender").First();
                Component sensor = defender.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.AttackRadius");
                GameObject dinoPrefab = LoadAssetAtPath(VelociraptorPrefabPath) as GameObject;
                firstDino = UnityEngine.Object.Instantiate(
                    dinoPrefab, defender.transform.position + Vector3.right, Quaternion.identity);
                secondDino = UnityEngine.Object.Instantiate(
                    dinoPrefab, defender.transform.position + Vector3.left, Quaternion.identity);
                Component firstDamageable = FindDamageable(firstDino);
                Component secondDamageable = FindDamageable(secondDino);
                MakeDamageableAlive(firstDamageable);
                MakeDamageableAlive(secondDamageable);

                InvokeVoid(sensor, "OnTriggerEnter", firstDino.GetComponentsInChildren<Collider>(true)
                    .First(collider => !collider.isTrigger));
                InvokeVoid(sensor, "OnTriggerEnter", secondDino.GetComponentsInChildren<Collider>(true)
                    .First(collider => !collider.isTrigger));
                Assert.That(ReadGraphGameObjectList(defender.gameObject, "NearbyAttackables"),
                    Is.EquivalentTo(new[] { firstDino, secondDino }));

                TakeLethalDamage(firstDamageable);

                Assert.That(ReadGraphGameObjectList(defender.gameObject, "NearbyAttackables"),
                    Is.EqualTo(new[] { secondDino }),
                    "one target death must not remove another live Dino from the guard blackboard");
                Assert.That(ReadProperty<object>(defender, "State").ToString(), Is.EqualTo("Attacking"));

                TakeLethalDamage(secondDamageable);
                for (int fixedStep = 0; fixedStep < 3; fixedStep++)
                {
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(ReadGraphGameObjectList(defender.gameObject, "NearbyAttackables"), Is.Empty);
                Assert.That(ReadProperty<object>(defender, "State").ToString(), Is.Not.EqualTo("Attacking"),
                    "a guard with no live targets must leave its attacking state");
            }
            finally
            {
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Utility.HealthBarCanvas");
                if (firstDino != null) UnityEngine.Object.Destroy(firstDino);
                if (secondDino != null) UnityEngine.Object.Destroy(secondDino);
                DisableRuntimeBehaviour("LlamAcademy.Dinos.UI.RuntimeUI");
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Player.DinoSpawner");
                input.TearDown();
            }
        }

        [Test]
        public void TargetEvaluation_EmptyCandidatesPublishesNoLiveTargetWithoutFailure()
        {
            GameObject self = new GameObject("TargetEvaluationSelf");
            UnityEngine.AI.NavMeshAgent agent = self.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.enabled = false;
            GameObject stale = new GameObject("StaleClosestTarget");
            try
            {
                object action = Activator.CreateInstance(
                    RuntimeType("LlamAcademy.Dinos.Behavior.SortAttackablesAction"));
                Type attackConfigType = RuntimeType("LlamAcademy.Dinos.Config.AttackConfigSO");
                SetActionField(action, "NearbyAttackables", CreateBlackboardVariable(
                    typeof(List<GameObject>), new List<GameObject>()));
                SetActionField(action, "Self", CreateBlackboardVariable(typeof(GameObject), self));
                SetActionField(action, "AttackConfig", CreateBlackboardVariable(
                    attackConfigType,
                    LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset")));
                SetActionField(action, "ClosestAttackable", CreateBlackboardVariable(typeof(GameObject), stale));
                SetActionField(action, "HasLiveTarget", CreateBlackboardVariable(typeof(bool), true));

                Assert.That(InvokeAction(action, "OnStart").ToString(), Is.EqualTo("Success"));
                Assert.That(ReadActionBlackboardValue<bool>(action, "HasLiveTarget"), Is.False);
                Assert.That(ReadActionBlackboardValue<GameObject>(action, "ClosestAttackable"), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stale);
                UnityEngine.Object.DestroyImmediate(self);
            }
        }

        [Test]
        public void DinoGraph_ExposesHasLiveTargetBranchVariable()
        {
            Assert.That(GraphAssetHasVariable<bool>(
                    "Assets/LlamAcademy/Dinos/Dinos/Dino Graph.asset", "HasLiveTarget"),
                Is.True,
                "the rebuilt graph asset needs an explicit target-state variable instead of failure-driven control flow");
        }

        [Test]
        public void DinoFixedStepTime_ConvertsGameplayDurationsToWholeFixedTicks()
        {
            Type fixedStepTime = RuntimeType("LlamAcademy.Dinos.Simulation.DinoFixedStepTime");
            MethodInfo secondsToTicks = fixedStepTime
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(method => method.Name == "SecondsToTicks" &&
                                  method.GetParameters().Length == 2);

            Assert.That(secondsToTicks, Is.Not.Null);
            Assert.That(secondsToTicks.Invoke(null, new object[] { 0f, 0.02f }), Is.EqualTo(0L));
            Assert.That(secondsToTicks.Invoke(null, new object[] { 0.75f, 0.02f }), Is.EqualTo(38L));
            Assert.That(secondsToTicks.Invoke(null, new object[] { 5f, 0.02f }), Is.EqualTo(250L));
        }

        [Test]
        public void DinoTrainingClock_UsesOneFixedStepOfScaledTimePerRenderedFrame()
        {
            Type bootstrap = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
            MethodInfo calculateCaptureDeltaTime = bootstrap.GetMethod(
                "CalculateCaptureDeltaTime", BindingFlags.Static | BindingFlags.Public);
            MethodInfo calculateCaptureFrameRate = bootstrap.GetMethod(
                "CalculateCaptureFrameRate", BindingFlags.Static | BindingFlags.Public);
            MethodInfo synchronizeCaptureDeltaTime = bootstrap.GetMethod(
                "SynchronizeCaptureDeltaTimeWithCurrentTimeScale",
                BindingFlags.Static | BindingFlags.Public);

            Assert.That(calculateCaptureDeltaTime, Is.Not.Null);
            Assert.That(calculateCaptureFrameRate, Is.Not.Null);
            Assert.That(synchronizeCaptureDeltaTime, Is.Not.Null,
                "training must be able to repair EngineConfigurationChannel's captureFramerate=0 override");
            Assert.That(
                (float)calculateCaptureDeltaTime.Invoke(null, new object[] { 0.02f, 20f }),
                Is.EqualTo(0.001f).Within(0.0000001f));
            Assert.That(
                (float)calculateCaptureDeltaTime.Invoke(null, new object[] { 0.02f, 5f }),
                Is.EqualTo(0.004f).Within(0.0000001f));
            Assert.That(
                (int)calculateCaptureFrameRate.Invoke(null, new object[] { 0.02f, 5f }),
                Is.EqualTo(250));

            float originalTimeScale = Time.timeScale;
            float originalCaptureDeltaTime = Time.captureDeltaTime;
            try
            {
                Time.timeScale = 5f;
                Time.captureFramerate = 0;
                Assert.That(Time.captureDeltaTime, Is.Zero,
                    "the regression precondition must match Python's current capture_frame_rate=0 message");

                synchronizeCaptureDeltaTime.Invoke(null, null);

                Assert.That(Time.timeScale, Is.EqualTo(5f));
                Assert.That(Time.captureFramerate, Is.EqualTo(250));
                Assert.That(Time.captureDeltaTime, Is.EqualTo(0.004f).Within(0.0000001f));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                Time.captureDeltaTime = originalCaptureDeltaTime;
            }
        }

        [Test]
        public void AttackCooldowns_StoreFixedTicksWithoutFloatingPointPrecisionLoss()
        {
            Type action = RuntimeType("LlamAcademy.Dinos.Behavior.AttackClosestObjectAction");
            FieldInfo attackTimes = action.GetField(
                "AttackTimes", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(attackTimes, Is.Not.Null);
            Assert.That(attackTimes.FieldType.GetGenericArguments()[1], Is.EqualTo(typeof(long)),
                "fixed-tick cooldown state must not lose integer precision during long training runs");
        }

        [UnityTest]
        public IEnumerator UnitPrefab_DisablesFrameDrivenGraphAndInstallsFixedStepDriver()
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                LoadAssetAtPath(VelociraptorPrefabPath) as GameObject);
            try
            {
                yield return null;
                Component graph = instance.GetComponent(RuntimeType("Unity.Behavior.BehaviorGraphAgent"));
                Component driver = instance.GetComponent(RuntimeType(
                    "LlamAcademy.Dinos.Simulation.DinoFixedBehaviorGraphDriver"));

                Assert.That(graph, Is.Not.Null);
                Assert.That(driver, Is.Not.Null);
                Assert.That(((Behaviour)graph).enabled, Is.False,
                    "the package Update loop must not remain authoritative");
                Assert.That(((Behaviour)driver).enabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [UnityTest]
        public IEnumerator AttackAction_ImmediateParticleAttackAppliesExactlyOneEffect()
        {
            GameObject attacker = new GameObject("ImmediateAttackAttacker");
            GameObject target = InstantiateDamageableTarget();
            Component damageable = FindDamageable(target);
            MakeDamageableAlive(damageable);
            GameObject archerPrefab = LoadAssetAtPath(ArcherPrefabPath) as GameObject;
            object archerUnitType = FindDamageable(archerPrefab).GetType().GetProperty("UnitType").GetValue(FindDamageable(archerPrefab));
            damageable.GetType().GetProperty("UnitType").SetValue(damageable, archerUnitType);
            damageable.GetType().GetProperty("MaxHealth").SetValue(damageable, 1000);
            damageable.GetType().GetProperty("Health").SetValue(damageable, 1000);
            attacker.AddComponent<UnityEngine.AI.NavMeshAgent>();
            GameObject helper = new GameObject("ParticleSystemHelper");
            helper.AddComponent(RuntimeType("LlamAcademy.Dinos.Utility.ParticleSystemHelper"));

            object action = CreateAttackAction(attacker, target,
                "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset");
            damageEvents = 0;
            SubscribeToDamageEvent(damageable, CaptureDamage);

            Assert.That(InvokeAction(action, "OnStart").ToString(), Is.EqualTo("Running"));
            ForceAttackCooldownReady(action);
            InvokeAction(action, "OnUpdate");

            Assert.That(damageEvents, Is.EqualTo(1),
                "one ready attack must submit one gameplay effect even when it also spawns an immediate particle");
            yield return Cleanup(attacker, target, helper);
        }

        [Test]
        public void AttackAction_DelayedHitNoOpsWhenIntentTargetIsNoLongerAlive()
        {
            GameObject targetObject = InstantiateDamageableTarget();
            Component realDamageable = FindDamageable(targetObject);
            MakeDamageableAlive(realDamageable);
            object unitType = realDamageable.GetType().GetProperty("UnitType").GetValue(realDamageable);

            Type damageableType = RuntimeType("LlamAcademy.Dinos.Unit.IDamageable");
            MethodInfo createProxy = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "Create" && method.IsGenericMethodDefinition);
            object proxyObject = createProxy.MakeGenericMethod(damageableType, typeof(RecordingDamageableProxy))
                .Invoke(null, null);
            RecordingDamageableProxy proxy = (RecordingDamageableProxy)proxyObject;
            proxy.Health = 0;
            proxy.MaxHealth = 100;
            proxy.UnitType = unitType;
            proxy.TargetTransform = targetObject.transform;

            UnityEngine.Object attackConfig = LoadAssetAtPath(
                "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaur Attack Config.asset");
            Array attacks = (Array)attackConfig.GetType().GetProperty("AttackTypes").GetValue(attackConfig);
            object delayedAttack = attacks.Cast<object>().First(attack =>
                (bool)attack.GetType().GetProperty("DelayDamage").GetValue(attack));
            object action = Activator.CreateInstance(RuntimeType("LlamAcademy.Dinos.Behavior.AttackClosestObjectAction"));
            SetActionStatus(action, "Running");

            MethodInfo scheduleEffect = action.GetType().GetMethod(
                "ScheduleEffect", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo applyPending = action.GetType().GetMethod(
                "ApplyPendingEffects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(scheduleEffect, Is.Not.Null);
            Assert.That(applyPending, Is.Not.Null);
            scheduleEffect.Invoke(action, new[] { delayedAttack, proxyObject, null, 0.75f, false });
            applyPending.Invoke(action, new object[] { long.MaxValue });

            Assert.That(proxy.TakeDamageCalls, Is.Zero,
                "a delayed attack intent must not call ApplyEffect after another attacker has already killed the target");
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        [UnityTest]
        public IEnumerator Dino_StartupReplaysSensorOverlapsBeforeGraphCanAdvanceToVillage()
        {
            InputTestFixture input = new();
            input.Setup();
            GameObject spawnedDino = null;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component defender = FindRuntimeComponents("LlamAcademy.Dinos.Enemy.Defender").First();
                Collider defenderCollider = defender.GetComponentInChildren<Collider>();
                GameObject dinoPrefab = LoadAssetAtPath(VelociraptorPrefabPath) as GameObject;
                spawnedDino = UnityEngine.Object.Instantiate(
                    dinoPrefab,
                    defender.transform.position + Vector3.right,
                    Quaternion.identity);
                Component dino = FindDamageable(spawnedDino);
                Component sensor = spawnedDino.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.AttackRadius");

                InvokeVoid(sensor, "OnTriggerEnter", defenderCollider);
                Assert.That(ReadGraphGameObjectList(spawnedDino, "NearbyAttackables"), Is.Empty,
                    "the regression setup must reproduce the pre-Start physics overlap with no subscriber");

                InvokeVoid(dino, "Start");
                Component roundManager = FindRuntimeComponents(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager").Single();
                InvokeVoid(roundManager, "StartRound");

                Assert.That(ReadGraphGameObjectList(spawnedDino, "NearbyAttackables"),
                    Is.EqualTo(new[] { defender.gameObject }),
                    "Dino startup must synchronize current sensor overlaps before its graph can select the village path");
            }
            finally
            {
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Utility.HealthBarCanvas");
                if (spawnedDino != null)
                {
                    UnityEngine.Object.Destroy(spawnedDino);
                }
                DisableRuntimeBehaviour("LlamAcademy.Dinos.UI.RuntimeUI");
                DisableRuntimeBehaviour("LlamAcademy.Dinos.Player.DinoSpawner");
                input.TearDown();
            }
        }

        private static GameObject InstantiateDamageableTarget(string prefabPath = VelociraptorPrefabPath)
        {
            GameObject prefab = LoadAssetAtPath(prefabPath) as GameObject;
            Assert.That(prefab, Is.Not.Null, $"Missing test fixture prefab at {prefabPath}.");
            return UnityEngine.Object.Instantiate(prefab);
        }

        private static Component FindDamageable(GameObject target)
        {
            Component damageable = target.GetComponents<Component>().Single(IsDamageable);
            Assert.That(damageable, Is.Not.Null);
            return damageable;
        }

        private static bool IsDamageable(Component component) => component != null &&
            component.GetType().GetInterfaces().Any(contract =>
                contract.FullName == "LlamAcademy.Dinos.Unit.IDamageable");

        private static void TakeLethalDamage(Component damageable)
        {
            MethodInfo takeDamage = damageable.GetType().GetMethod("TakeDamage", new[] { typeof(int) });
            Assert.That(takeDamage, Is.Not.Null);
            takeDamage.Invoke(damageable, new object[] { int.MaxValue });
        }

        private static void MakeDamageableAlive(Component damageable)
        {
            PropertyInfo health = damageable.GetType().GetProperty("Health", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo maxHealth = damageable.GetType().GetProperty("MaxHealth", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(health, Is.Not.Null);
            Assert.That(maxHealth, Is.Not.Null);
            maxHealth.SetValue(damageable, 100);
            health.SetValue(damageable, 100);
        }

        private static GameObject[] CopyTargets(object tracker)
        {
            List<GameObject> targets = new();
            int count = Invoke<int>(tracker, "CopyLiveTargets", targets);
            Assert.That(count, Is.EqualTo(targets.Count));
            return targets.ToArray();
        }

        private static T Invoke<T>(object target, string methodName, params object[] arguments)
        {
            return (T)InvokeMethod(target, methodName, arguments);
        }

        private static void InvokeVoid(object target, string methodName, params object[] arguments)
        {
            InvokeMethod(target, methodName, arguments);
        }

        private static object InvokeMethod(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            return method.Invoke(target, arguments);
        }

        private static T ReadProperty<T>(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Missing property {propertyName}.");
            return (T)property.GetValue(target);
        }

        private static void SubscribeToActionEvent(object target, string eventName, Action handler)
        {
            EventInfo eventInfo = target.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(eventInfo, Is.Not.Null, $"Missing event {eventName}.");
            eventInfo.AddEventHandler(target, handler);
        }

        private static void SubscribeToDeathEvent(Component target, Action handler)
        {
            EventInfo eventInfo = target.GetType().GetEvent("OnDeath", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(eventInfo, Is.Not.Null, "Missing IDamageable.OnDeath event.");
            MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
            ParameterExpression damageableParameter = Expression.Parameter(invoke.GetParameters()[0].ParameterType, "target");
            MethodInfo callback = typeof(DinoCombatBehaviorPlayModeTests).GetMethod(
                nameof(InvokeDeathCallback), BindingFlags.Static | BindingFlags.NonPublic);
            Delegate listener = Expression.Lambda(
                eventInfo.EventHandlerType,
                Expression.Call(callback, Expression.Constant(handler)),
                damageableParameter).Compile();
            eventInfo.AddEventHandler(target, listener);
        }

        private static void SubscribeToDamageEvent(Component target, Action handler)
        {
            EventInfo eventInfo = target.GetType().GetEvent("OnTakeDamage", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(eventInfo, Is.Not.Null, "Missing IDamageable.OnTakeDamage event.");
            ParameterInfo[] parameters = eventInfo.EventHandlerType.GetMethod("Invoke").GetParameters();
            ParameterExpression targetParameter = Expression.Parameter(parameters[0].ParameterType, "target");
            ParameterExpression damageParameter = Expression.Parameter(parameters[1].ParameterType, "damage");
            MethodInfo callback = typeof(DinoCombatBehaviorPlayModeTests).GetMethod(
                nameof(InvokeDamageCallback), BindingFlags.Static | BindingFlags.NonPublic);
            Delegate listener = Expression.Lambda(
                eventInfo.EventHandlerType,
                Expression.Call(callback, Expression.Constant(handler)),
                targetParameter,
                damageParameter).Compile();
            eventInfo.AddEventHandler(target, listener);
        }

        private static void SubscribeToTargetEvent(Component target, string fieldName, Action<object> handler)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing target delegate field {fieldName}.");
            Delegate listener = CreateTargetEventDelegate(field.FieldType, handler);
            field.SetValue(target, Delegate.Combine((Delegate)field.GetValue(target), listener));
        }

        private static Delegate CreateTargetEventDelegate(Component target, string fieldName, Action<object> handler)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing target delegate field {fieldName}.");
            return CreateTargetEventDelegate(field.FieldType, handler);
        }

        private static Delegate CreateTargetEventDelegate(Type targetEventType, Action<object> handler)
        {
            MethodInfo invoke = targetEventType.GetMethod("Invoke");
            ParameterExpression targetParameter = Expression.Parameter(invoke.GetParameters()[0].ParameterType, "target");
            MethodInfo callback = typeof(DinoCombatBehaviorPlayModeTests).GetMethod(
                nameof(InvokeTargetCallback), BindingFlags.Static | BindingFlags.NonPublic);
            return Expression.Lambda(
                targetEventType,
                Expression.Call(callback, Expression.Constant(handler), Expression.Convert(targetParameter, typeof(object))),
                targetParameter).Compile();
        }

        private static void InvokeTargetCallback(Action<object> handler, object target) => handler(target);
        private static void InvokeDeathCallback(Action handler) => handler();
        private static void InvokeDamageCallback(Action handler) => handler();
        private static void CaptureTargetEnter(object _) => enteredTargets++;
        private static void CaptureTargetExit(object _) => exitedTargets++;
        private static void CaptureTrackerChange() => trackerChanges++;
        private static void CaptureDeath() => deathEvents++;
        private static void CaptureDamage() => damageEvents++;

        private static Component[] FindRuntimeComponents(string fullName)
        {
            Type type = RuntimeType(fullName);
            return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None)
                .Where(component => type.IsInstanceOfType(component))
                .Cast<Component>()
                .ToArray();
        }

        private static void DisableRuntimeBehaviour(string fullName)
        {
            Component component = FindRuntimeComponents(fullName).FirstOrDefault();
            if (component is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }
        }

        private static object CreateAttackAction(GameObject attacker, GameObject target, string attackConfigPath)
        {
            object action = Activator.CreateInstance(RuntimeType("LlamAcademy.Dinos.Behavior.AttackClosestObjectAction"));
            Type attackConfigType = RuntimeType("LlamAcademy.Dinos.Config.AttackConfigSO");
            Type attackEventChannelType = RuntimeType("LlamAcademy.Dinos.Behavior.AttackEventChannel");
            Type deathEventChannelType = RuntimeType("LlamAcademy.Dinos.Behavior.DeathEventChannel");
            SetActionField(action, "Self", CreateBlackboardVariable(typeof(GameObject), attacker));
            SetActionField(action, "ClosestAttackable", CreateBlackboardVariable(typeof(GameObject), target));
            SetActionField(action, "AttackConfig", CreateBlackboardVariable(attackConfigType, LoadAssetAtPath(attackConfigPath)));
            SetActionField(action, "RotationSpeed", CreateBlackboardVariable(typeof(float), 5f));
            SetActionField(action, "LastAttackTime", CreateBlackboardVariable(typeof(float), 0f));
            SetActionField(action, "AttackEventChannel", CreateBlackboardVariable(attackEventChannelType, null));
            SetActionField(action, "DeathEventChannel", CreateBlackboardVariable(deathEventChannelType, null));
            return action;
        }

        private static object CreateBlackboardVariable(Type valueType, object value)
        {
            Type variableType = RuntimeType("Unity.Behavior.BlackboardVariable`1").MakeGenericType(valueType);
            object variable = Activator.CreateInstance(variableType);
            variableType.GetProperty("Value").SetValue(variable, value);
            return variable;
        }

        private static void SetActionField(object action, string fieldName, object value)
        {
            FieldInfo field = action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing action field {fieldName}.");
            field.SetValue(action, value);
        }

        private static object InvokeAction(object action, string methodName)
        {
            MethodInfo method = action.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing action method {methodName}.");
            return method.Invoke(action, null);
        }

        private static T ReadActionBlackboardValue<T>(object action, string fieldName)
        {
            FieldInfo field = action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing action field {fieldName}.");
            object variable = field.GetValue(action);
            return (T)variable.GetType().GetProperty("Value").GetValue(variable);
        }

        private static bool GraphAssetHasVariable<T>(string assetPath, string variableName)
        {
            Type assetDatabase = RuntimeType("UnityEditor.AssetDatabase");
            MethodInfo loadAll = assetDatabase.GetMethod(
                "LoadAllAssetsAtPath", BindingFlags.Public | BindingFlags.Static);
            UnityEngine.Object[] assets = (UnityEngine.Object[])loadAll.Invoke(null, new object[] { assetPath });
            foreach (UnityEngine.Object asset in assets.Where(asset =>
                         asset != null && asset.GetType().Name == "RuntimeBlackboardAsset"))
            {
                object blackboard = asset.GetType().GetProperty("Blackboard").GetValue(asset);
                IEnumerable variables = (IEnumerable)blackboard.GetType()
                    .GetProperty("Variables").GetValue(blackboard);
                foreach (object variable in variables)
                {
                    if (variable != null
                        && variable.GetType().IsGenericType
                        && variable.GetType().GetGenericArguments()[0] == typeof(T)
                        && string.Equals((string)variable.GetType().GetField("Name").GetValue(variable),
                            variableName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ForceAttackCooldownReady(object action)
        {
            IDictionary attackTimes = (IDictionary)action.GetType()
                .GetField("AttackTimes", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(action);
            foreach (object attack in attackTimes.Keys.Cast<object>().ToArray())
            {
                attackTimes[attack] = -100L;
            }
        }

        private static void SetActionStatus(object action, string statusName)
        {
            MethodInfo setStatus = action.GetType().BaseType.BaseType.GetMethod(
                "SetCurrentStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setStatus, Is.Not.Null, "Missing Unity Behavior Node.SetCurrentStatus.");
            object status = Enum.Parse(setStatus.GetParameters()[0].ParameterType, statusName);
            setStatus.Invoke(action, new[] { status });
        }

        private static List<GameObject> ReadGraphGameObjectList(GameObject owner, string variableName)
        {
            Component graphAgent = owner.GetComponent(RuntimeType("Unity.Behavior.BehaviorGraphAgent"));
            Assert.That(graphAgent, Is.Not.Null);
            MethodInfo getVariable = graphAgent.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == "GetVariable"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(string));
            object[] arguments = { variableName, null };
            bool found = (bool)getVariable.MakeGenericMethod(typeof(List<GameObject>)).Invoke(graphAgent, arguments);
            Assert.That(found, Is.True, $"Missing graph variable {variableName}.");
            object variable = arguments[1];
            return new List<GameObject>((List<GameObject>)variable.GetType().GetProperty("Value").GetValue(variable));
        }

        public class RecordingDamageableProxy : DispatchProxy
        {
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public int TakeDamageCalls { get; private set; }
            public object UnitType { get; set; }
            public Transform TargetTransform { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case "get_Health": return Health;
                    case "set_Health": Health = (int)args[0]; return null;
                    case "get_MaxHealth": return MaxHealth;
                    case "set_MaxHealth": MaxHealth = (int)args[0]; return null;
                    case "get_UnitType": return UnitType;
                    case "set_UnitType": UnitType = args[0]; return null;
                    case "get_Transform": return TargetTransform;
                    case "TakeDamage":
                        TakeDamageCalls++;
                        Health = Mathf.Max(0, Health - (int)args[0]);
                        return null;
                    case "Die":
                    case "add_OnTakeDamage":
                    case "remove_OnTakeDamage":
                    case "add_OnDeath":
                    case "remove_OnDeath":
                        return null;
                    default:
                        throw new NotSupportedException($"Unexpected IDamageable member {targetMethod.Name}.");
                }
            }
        }

        private static UnityEngine.Object LoadAssetAtPath(string path)
        {
            Type databaseType = RuntimeType("UnityEditor.AssetDatabase");
            MethodInfo loadMethod = databaseType.GetMethod("LoadMainAssetAtPath", BindingFlags.Public | BindingFlags.Static);
            return (UnityEngine.Object)loadMethod.Invoke(null, new object[] { path });
        }

        private static Type RuntimeType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Missing runtime type {fullName}.");
            return type;
        }

        private static IEnumerator Cleanup(params GameObject[] gameObjects)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.Destroy(gameObject);
                }
            }

            yield return null;
        }
    }
}
