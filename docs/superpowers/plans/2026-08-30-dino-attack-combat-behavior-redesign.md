# Dino Attack Combat Behavior Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild dinosaur target lifecycle, attack completion, retargeting, and navigation recovery so multiple dinosaurs can attack one target and all continue correctly after that target dies.

**Architecture:** Unity Behavior Graph remains the high-level orchestration layer. Each `Dino` owns a `DinoTargetTracker` as the authoritative live-target store; C# owns deduplication, death subscriptions, damage idempotence, delayed-hit validity, and the invariant that a dinosaur without a selectable target resumes its village destination. The graph becomes one serial evaluate/attack-or-advance loop and no longer branches on raw list count or a parallel death-event branch.

**Tech Stack:** Unity 6000.3.22f1, Unity Behavior 1.0.13, AI Navigation 2.0.14, C#, NUnit/EditMode/PlayMode tests, NavMeshAgent, existing `Dinos.unity` and `Dino Graph.asset`.

**Spec:** `docs/superpowers/specs/2026-08-30-dino-attack-combat-behavior-redesign-design.md`

## Global Constraints

- Preserve all existing user changes in both the outer `game_project` repository and inner `dino-attack` repository.
- Do not use `git reset --hard`, `git checkout --`, `git clean`, stash, bulk staging, or automatic commits. The project governance rule overrides the generic frequent-commit recommendation; use explicit diff-review checkpoints instead.
- Do not modify Packages, package versions, ProjectSettings, map layout, terrain zones, combat values, target priorities, rewards, observation/action shapes, or training protocol.
- Do not restore line-of-sight, cover, mountain, ruin, or wall attack blocking.
- Keep Velociraptor/Pachycephalosaurus/TRex target priorities, attack intervals, animations, particles, terrain movement effects, and defense rules unchanged.
- Modify `Dino Graph.asset` only through one connected Unity Editor instance; do not hand-edit its YAML.
- Never launch a second Editor while `Library/EditorInstance.json` identifies a live process. Do not kill the user's Editor process; ask for a clean handoff if the existing instance is not controllable.
- Validation must use a clean full Domain Reload and Scene Reload session. Record and restore the previous Enter Play Mode settings; do not retain a ProjectSettings change.
- Use test-first RED → GREEN steps. Run only the focused suites in this plan, plus one final focused scene smoke; do not restore unrelated full suites, a Player build, a 20-reload matrix, or long training.

---

## File and Responsibility Map

### New runtime files

- `dino-attack/Assets/LlamAcademy/Dinos/Unit/DamageableState.cs` — one shared, Unity-null-safe `IDamageable` liveness predicate.
- `dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoTargetTracker.cs` — per-dinosaur deduplicated target store, one death subscription per target, live snapshots, and idempotent cleanup.
- `dino-attack/Assets/LlamAcademy/Dinos/Behavior/AdvanceToVillageAction.cs` — Behavior Action that maintains the village navigation intent and exits as soon as a live local target appears.
- Corresponding Unity-generated `.meta` files for all new assets.

### Modified runtime files

- `dino-attack/Assets/LlamAcademy/Dinos/Unit/AttackRadius.cs` — deduplicate multi-collider overlap bookkeeping and make death/exit cleanup idempotent.
- `dino-attack/Assets/LlamAcademy/Dinos/Unit/Unit.cs` — idempotent lethal transition and balanced sensor subscription lifecycle.
- `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defender.cs` — route off-NavMesh death through the same one-shot death transition; remove the second destroy-time death event.
- `dino-attack/Assets/LlamAcademy/Dinos/Unit/Dino.cs` — own the tracker, synchronize sanitized Blackboard state, expose navigation recovery, and clear subscriptions on disable.
- `dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoGraphConstants.cs` — add `HAS_LIVE_TARGET = "HasLiveTarget"`.
- `dino-attack/Assets/LlamAcademy/Dinos/Utility/DinoTargetSelector.cs` — use the shared liveness predicate before path scoring.
- `dino-attack/Assets/LlamAcademy/Dinos/Behavior/SortAttackablesAction.cs` — become the target-evaluation node and always publish `HasLiveTarget`/`ClosestAttackable` coherently.
- `dino-attack/Assets/LlamAcademy/Dinos/Behavior/MoveToClosestAttackableOnXzPlaneAction.cs` — clear a path only after successful live-target range confirmation.
- `dino-attack/Assets/LlamAcademy/Dinos/Behavior/AttackClosestObjectAction.cs` — one legal effect submission per attack and consistent immediate/delayed completion.
- `dino-attack/Assets/LlamAcademy/Dinos/Utility/ParticleSystemHelper.cs` — moving particles no-op safely when their damageable target dies or is destroyed.
- `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Dino Graph.asset` — serial evaluate → combat/advance loop, `HasLiveTarget` branch, no parallel death-event branch.

### Tests and evidence

- Create `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs` and `.meta` — focused reflection-based tests for Assembly-CSharp behavior, target lifecycle, shared-target concurrency, and runtime graph recovery.
- Modify only if helper reuse is unavoidable: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`; prefer leaving its existing tests unchanged and calling them by filter as regression evidence.
- Create `reports/phase8/dino-combat-behavior/validation.md` — exact RED/GREEN commands, results, Console evidence, Editor settings restoration, and known limitations.
- Update `docs/PROJECT_PLAN.md` after implementation with phase status, decisions, evidence, and any remaining blocker.

---

### Task 1: Establish the live-target invariant

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Unit/DamageableState.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoTargetTracker.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/AttackRadius.cs:26-47`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Utility/DinoTargetSelector.cs:68-91`
- Create: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`
- Test: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`

**Interfaces:**

- Produces: `DamageableState.IsAlive(IDamageable target) : bool`.
- Produces: `DinoTargetTracker.Changed : event Action`.
- Produces: `DinoTargetTracker.Add(IDamageable target) : bool`.
- Produces: `DinoTargetTracker.Remove(IDamageable target) : bool`.
- Produces: `DinoTargetTracker.HasLiveTarget : bool`.
- Produces: `DinoTargetTracker.CopyLiveTargets(List<GameObject> destination) : int`.
- Produces: `DinoTargetTracker.Clear() : void` and `Dispose() : void`.
- Preserves: `AttackRadius.OnTargetEnter`/`OnTargetExit` public delegate fields used by `Unit.Start`.

- [x] **Step 1: Add failing target-store tests**

Add reflection helpers local to the new test class (`RuntimeType`, `LoadAssetAtPath`, `Invoke`, `Cleanup`). Use a real Velociraptor prefab as an `IDamageable` fixture so the test does not create an assembly-definition dependency on Assembly-CSharp.

```csharp
[UnityTest]
public IEnumerator DinoTargetTracker_DeduplicatesAndRemovesTheTargetOnDeath()
{
    GameObject target = UnityEngine.Object.Instantiate(
        LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab") as GameObject);
    Component damageable = target.GetComponents<Component>().Single(IsDamageable);
    object tracker = Activator.CreateInstance(RuntimeType("LlamAcademy.Dinos.Unit.DinoTargetTracker"));

    Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.True);
    Assert.That(Invoke<bool>(tracker, "Add", damageable), Is.False);
    Assert.That(CopyTargets(tracker), Is.EqualTo(new[] { target }));

    damageable.GetType().GetMethod("TakeDamage").Invoke(damageable, new object[] { int.MaxValue });
    yield return null;

    Assert.That(ReadProperty<bool>(tracker, "HasLiveTarget"), Is.False);
    Assert.That(CopyTargets(tracker), Is.Empty);
    InvokeVoid(tracker, "Dispose");
    yield return Cleanup(target);
}
```

Add a second test that calls `Clear()` twice, destroys a tracked target, and verifies no callback or `MissingReferenceException` occurs. Add a third test that invokes `AttackRadius.OnTriggerEnter` twice for the same collider and verifies the sensor emits one logical enter and one logical exit/death removal.

- [x] **Step 2: Run the target-store tests and verify RED**

Run after obtaining a single clean Editor or batchmode handoff:

```powershell
& 'D:\job\6000.3.22f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\hp\Documents\New project 2\game_project\dino-attack' -runTests -testPlatform PlayMode -testFilter 'DinoCombatBehaviorPlayModeTests.DinoTargetTracker_' -testResults 'C:\Users\hp\Documents\New project 2\game_project\reports\phase8\dino-combat-behavior\task1-red.xml' -logFile 'C:\Users\hp\Documents\New project 2\game_project\reports\phase8\dino-combat-behavior\task1-red.log' -quit
```

Expected: FAIL because `DamageableState` and `DinoTargetTracker` do not exist and `AttackRadius` emits duplicate logical entries.

- [x] **Step 3: Implement the liveness predicate and tracker**

Use the following public contract; keep storage private and keyed by target instance ID so repeated colliders and repeated events cannot duplicate entries.

```csharp
public static class DamageableState
{
    public static bool IsAlive(IDamageable target)
    {
        if (target == null) return false;
        if (target is UnityEngine.Object unityObject && unityObject == null) return false;
        Transform targetTransform = target.Transform;
        return targetTransform != null && targetTransform.gameObject.activeInHierarchy && target.Health > 0;
    }
}

public sealed class DinoTargetTracker : IDisposable
{
    private sealed class Entry
    {
        public Entry(IDamageable target, GameObject gameObject)
        {
            Target = target;
            GameObject = gameObject;
        }

        public IDamageable Target { get; }
        public GameObject GameObject { get; }
    }

    private readonly Dictionary<int, Entry> targets = new();
    public event Action Changed;
    public bool HasLiveTarget { get { PurgeInvalid(); return targets.Count > 0; } }

    public bool Add(IDamageable target)
    {
        if (!DamageableState.IsAlive(target)) return false;
        GameObject gameObject = target.Transform.gameObject;
        int instanceId = gameObject.GetInstanceID();
        if (targets.ContainsKey(instanceId)) return false;

        targets.Add(instanceId, new Entry(target, gameObject));
        target.OnDeath += HandleDeath;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(IDamageable target)
    {
        if (target == null) return false;
        int instanceId = TryGetInstanceId(target);
        if (instanceId != 0) return RemoveById(instanceId, true);

        foreach (KeyValuePair<int, Entry> pair in targets)
        {
            if (ReferenceEquals(pair.Value.Target, target))
                return RemoveById(pair.Key, true);
        }
        return false;
    }

    public int CopyLiveTargets(List<GameObject> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        PurgeInvalid();
        foreach (Entry entry in targets.Values)
            destination.Add(entry.GameObject);
        return destination.Count;
    }

    public void Clear()
    {
        if (targets.Count == 0) return;
        foreach (Entry entry in targets.Values)
            TryUnsubscribe(entry.Target);
        targets.Clear();
        Changed?.Invoke();
    }

    public void Dispose() => Clear();

    private void HandleDeath(IDamageable target) => Remove(target);

    private void PurgeInvalid()
    {
        List<int> invalidIds = null;
        foreach (KeyValuePair<int, Entry> pair in targets)
        {
            if (DamageableState.IsAlive(pair.Value.Target)) continue;
            invalidIds ??= new List<int>();
            invalidIds.Add(pair.Key);
        }
        if (invalidIds == null) return;
        foreach (int instanceId in invalidIds)
            RemoveById(instanceId, false);
        Changed?.Invoke();
    }

    private bool RemoveById(int instanceId, bool notify)
    {
        if (!targets.TryGetValue(instanceId, out Entry entry)) return false;
        TryUnsubscribe(entry.Target);
        targets.Remove(instanceId);
        if (notify) Changed?.Invoke();
        return true;
    }

    private static int TryGetInstanceId(IDamageable target)
    {
        if (target is UnityEngine.Object unityObject && unityObject == null) return 0;
        return target.Transform == null ? 0 : target.Transform.gameObject.GetInstanceID();
    }

    private void TryUnsubscribe(IDamageable target)
    {
        if (target == null) return;
        if (target is UnityEngine.Object unityObject && unityObject == null) return;
        target.OnDeath -= HandleDeath;
    }
}
```

`HandleDeath(IDamageable target)` must call the same idempotent `Remove` path. `PurgeInvalid()` must unsubscribe surviving managed targets where possible and must tolerate Unity-destroyed objects.

- [x] **Step 4: Make `AttackRadius` logical-target aware**

Replace synthetic `OnTriggerExit(damageable.Transform.GetComponent<Collider>())` with a dictionary of overlap counts keyed by `IDamageable`. First overlap subscribes and emits enter; last physical exit or death unsubscribes and emits exit. `OnDisable()` unsubscribes all entries and clears the dictionary. Do not add any Physics/NavMesh raycast or LOS rule.

- [x] **Step 5: Reuse `DamageableState` in target selection**

In `DinoTargetSelector.TryDescribeCandidate`, replace its open-coded active/health check with `DamageableState.IsAlive(damageable)` after the component lookup. Preserve category priority, stable ID, path-length calculation, and warning behavior.

- [x] **Step 6: Run Task 1 GREEN and inspect the exact diff**

Run the Task 1 filter again; expected PASS. Then run:

```powershell
git -C 'C:\Users\hp\Documents\New project 2\game_project\dino-attack' diff --check -- 'Assets/LlamAcademy/Dinos/Unit/AttackRadius.cs' 'Assets/LlamAcademy/Dinos/Unit/DamageableState.cs' 'Assets/LlamAcademy/Dinos/Unit/DinoTargetTracker.cs' 'Assets/LlamAcademy/Dinos/Utility/DinoTargetSelector.cs' 'Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs'
```

Expected: focused tests PASS; `git diff --check` emits no errors. Do not stage or commit.

---

### Task 2: Make death and attack-effect submission idempotent

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/Unit.cs:70-104`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defender.cs:41-60,110-125`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Behavior/AttackClosestObjectAction.cs:39-176`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Utility/ParticleSystemHelper.cs:53-86`
- Modify: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`

**Interfaces:**

- Produces: `Unit.IsDead : bool`.
- Produces: `Unit.TryBeginDeath() : bool` as a protected one-shot transition used by `TakeDamage` and `Defender` off-NavMesh death.
- Consumes: `DamageableState.IsAlive` from Task 1.
- Changes: `ParticleSystemHelper.MoveParticleSystemToTarget(IDamageable target, ParticleSystem system, AttackTypeSO attack) : void`.

- [x] **Step 1: Write failing death and delayed-hit tests**

Add three focused cases:

```csharp
[UnityTest]
public IEnumerator Defender_LethalDamageAndDestroy_RaiseDeathExactlyOnce()
{
    yield return LoadDinosScene();
    Component defender = FindRuntimeComponents("LlamAcademy.Dinos.Enemy.Defender").First();
    int deaths = 0;
    Delegate listener = SubscribeDeath(defender, () => deaths++);

    defender.GetType().GetMethod("TakeDamage").Invoke(defender, new object[] { int.MaxValue });
    defender.GetType().GetMethod("TakeDamage").Invoke(defender, new object[] { int.MaxValue });
    yield return new WaitForSeconds(3f);

    Assert.That(deaths, Is.EqualTo(1));
    UnsubscribeDeath(defender, listener);
}
```

Also add `AttackAction_ImmediateParticleAttackAppliesExactlyOneEffect` by measuring a live target's health before/after one forced attack update, and `AttackAction_DelayedHitNoOpsAfterAnotherAttackerKillsTarget` by starting a delayed action, killing the target through `TakeDamage`, waiting beyond `DelayDamageTime`, and asserting health/death count do not change again.

- [x] **Step 2: Run Task 2 tests and verify RED**

Run the PlayMode filter `DinoCombatBehaviorPlayModeTests.Defender_` plus `DinoCombatBehaviorPlayModeTests.AttackAction_`. Expected failures: defender death count is greater than one, or an attack effect is submitted after death/duplicated.

- [x] **Step 3: Implement the one-shot Unit death transition**

Add one private state bit and protected transition:

```csharp
private bool deathStarted;
public bool IsDead => deathStarted || Health <= 0;

protected bool TryBeginDeath()
{
    if (deathStarted) return false;
    deathStarted = true;
    Health = 0;
    RaiseDeathEvent();
    return true;
}
```

`TakeDamage` must return immediately when already dead, clamp the applied damage, update the health bar once, emit `OnTakeDamage` with the applied amount, and call `Die()` only when `TryBeginDeath()` succeeds. Preserve nonlethal behavior.

In `Defender.OnNavMeshUpdated`, replace direct `RaiseDeathEvent(); Die();` with the same guarded transition:

```csharp
if (TryBeginDeath())
{
    GraphAgent.enabled = false;
    Agent.enabled = false;
    if (Rigidbody != null)
    {
        Rigidbody.isKinematic = false;
        Rigidbody.useGravity = true;
    }
    Die();
}
```

Remove the `RaiseDeathEvent()` call from `DestroyGO`; destruction is cleanup, not a second death.

- [x] **Step 4: Unify immediate, delayed, and moving-particle effect submission**

In `AttackClosestObjectAction`, route every effect through one method:

```csharp
private bool TryApplyEffect(AttackTypeSO attack, IDamageable target)
{
    if (attack == null || !DamageableState.IsAlive(target)) return false;
    attack.ApplyEffect(target);
    return true;
}
```

Spawn visuals separately, then call `TryApplyEffect` exactly once. Remove the second immediate `attack.ApplyEffect` after `ApplyDamage`. Delayed damage checks `CurrentStatus == Status.Running` and `DamageableState.IsAlive(target)` at application time. Do not use `DeathEventChannel` to decide completion; target liveness drives the next graph evaluation.

Change the moving-particle helper to accept `IDamageable`, revalidate it before reading the target transform and before applying the effect, and release/finish the visual without damage when the target is invalid.

- [x] **Step 5: Run Task 2 GREEN and existing attack regression**

Run the Task 2 filters plus:

```text
OpenTropicalBattlefieldPlayModeTests.AttackAction_RejectsATargetThatIsAlreadyDead
OpenTropicalBattlefieldPlayModeTests.AttackPath_WithSolidBlocker_StillRaisesTheRealAttackEvent
OpenTropicalBattlefieldPlayModeTests.AttackPath_HasNoOcclusionQueryInvariant
```

Expected: all PASS; no LOS query has been introduced; Console contains no new exception. Review only the Task 2 paths with `git diff --check`; do not stage or commit.

---

### Task 3: Integrate the tracker with `Dino` and enforce navigation recovery

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/Unit.cs:36-65`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defender.cs:25-39`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoGraphConstants.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/Dino.cs:25-181`
- Modify: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`

**Interfaces:**

- Consumes: `DinoTargetTracker` from Task 1.
- Produces: `Dino.HasTrackedLiveTargets : bool`.
- Produces: `Dino.EnsureVillageNavigation() : bool`.
- Produces: Blackboard `NearbyAttackables` as a sanitized compatibility snapshot.
- Prepares: `DinoGraphConstants.HAS_LIVE_TARGET`; `Dino` publishes false during empty-target recovery when the variable exists. Task 4 adds the actual Blackboard variable and evaluation wiring.

- [x] **Step 1: Add failing Dino integration tests**

Cover these cases in the real `Dinos` scene:

1. Two logical enters for one target leave exactly one Blackboard candidate.
2. Target death removes it without waiting for physical destruction.
3. Removing one of two live targets preserves the active combat destination.
4. Removing the final target sets `agent.isStopped == false`, restores a valid path, and aims within 1 m of `RoundManager.DinoTarget`.
5. Disable/enable does not duplicate `AttackRadius` callbacks or tracker subscriptions.

Keep the existing `DinoAfterLosingItsLastNearbyTarget_ContinuesTowardVillageCenter` test unchanged and call it as regression evidence.

- [x] **Step 2: Run Task 3 tests and verify RED**

Expected: duplicate candidates remain possible; death and disable paths do not satisfy the new authoritative tracker/Blackboard contract.

- [x] **Step 3: Balance Unit sensor subscriptions**

Add `protected virtual void OnDisable()` to `Unit` that unsubscribes `AttackRadius.OnTargetEnter/OnTargetExit`. Convert `Dino.OnDisable` and `Defender.OnDisable` to protected overrides that call `base.OnDisable()` after their own cleanup. Preserve existing RoundManager and NavMeshManager unsubscriptions.

- [x] **Step 4: Make `Dino` own and synchronize the tracker**

Create the tracker in `Awake` after `base.Awake()`, subscribe `Changed`, and replace direct list mutation in `OnTargetEnter/OnTargetExit` with tracker calls. In `OnDisable`, unsubscribe `Changed` before calling `Dispose()` so tracker cleanup cannot re-enter Blackboard synchronization while the dinosaur is being disabled.

```csharp
public bool HasTrackedLiveTargets => targetTracker != null && targetTracker.HasLiveTarget;

private void SyncTargetBlackboard()
{
    liveTargetBuffer.Clear();
    targetTracker.CopyLiveTargets(liveTargetBuffer);
    GraphAgent.SetVariableValue(DinoGraphConstants.NEARBY_ATTACKABLES,
        new List<GameObject>(liveTargetBuffer));
    if (liveTargetBuffer.Count == 0)
    {
        GraphAgent.SetVariableValue(DinoGraphConstants.HAS_LIVE_TARGET, false);
        EnsureVillageNavigation();
    }
}
```

`EnsureVillageNavigation()` must return false without throwing if the dino is dead, the game is not Running, the target is absent, the agent is disabled, or the agent is not on NavMesh. Otherwise it updates `EggLocation`, sets `isStopped=false`, and only calls `SetDestinationImmediate` when the current destination/path is missing or materially different.

- [x] **Step 5: Run Task 3 GREEN**

Run the new Task 3 tests and existing `DinoAfterLosingItsLastNearbyTarget_ContinuesTowardVillageCenter`. Expected: PASS, one candidate per logical target, no duplicate callbacks, final-target loss restores village navigation, and losing one of multiple targets preserves combat navigation.

---

### Task 4: Reauthor the Behavior Actions and `Dino Graph`

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Behavior/AdvanceToVillageAction.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Behavior/SortAttackablesAction.cs:14-48`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Behavior/MoveToClosestAttackableOnXzPlaneAction.cs:28-131`
- Modify through Unity Editor: `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Dino Graph.asset`
- Modify: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`

**Interfaces:**

- `SortAttackablesAction` gains `BlackboardVariable<bool> HasLiveTarget`.
- `AdvanceToVillageAction` consumes `BlackboardVariable<GameObject> Self` and `Dino.HasTrackedLiveTargets`/`EnsureVillageNavigation()`.
- `Dino Graph` adds exposed, non-shared Boolean `HasLiveTarget`, default false.
- `Dino Graph` keeps `NearbyAttackables`, `ClosestAttackable`, `AttackConfig`, `PostAttackCooldown`, and `AttackEventChannel`; it no longer needs a runtime parallel `DeathEventChannel` branch.

- [x] **Step 1: Add failing action and runtime-graph tests**

Add tests that instantiate Actions via reflection:

```csharp
[Test]
public void TargetEvaluation_EmptyOrStaleCandidatesPublishesNoLiveTargetWithoutFailure()
{
    object action = CreateTargetEvaluationAction(new List<GameObject> { null });
    object status = InvokeAction(action, "OnStart");
    Assert.That(status.ToString(), Is.EqualTo("Success"));
    Assert.That(ReadBlackboardValue<bool>(action, "HasLiveTarget"), Is.False);
    Assert.That(ReadBlackboardValue<GameObject>(action, "ClosestAttackable"), Is.Null);
}
```

Add a movement-action test that starts a valid path, kills the target before range success, calls `OnUpdate` and `OnEnd`, and verifies `OnEnd` did not clear the unrelated/recovery path. Add a scene test that obtains `BehaviorGraphAgent` and asserts `GetVariable<bool>("HasLiveTarget")` succeeds.

Add `TargetEvaluation_AllCandidatesUnreachablePublishesNoLiveTarget` with a live target outside the current agent NavMesh. It must return Success with `HasLiveTarget=false` and `ClosestAttackable=null`, proving unreachable live objects cannot trap the graph in its combat branch.

- [x] **Step 2: Run Task 4 tests and verify RED**

Expected: `SortAttackablesAction` returns Failure for empty/stale lists, the graph lacks `HasLiveTarget`, and movement teardown clears its path even when attack range was never confirmed.

- [x] **Step 3: Turn `SortAttackablesAction` into a coherent evaluation node**

Set `ClosestAttackable=null` and `HasLiveTarget=false` before selection. Empty/no-selectable candidate is a successful evaluation result, not an Action failure. Invalid configuration (`Self`, `AttackConfig`, or NavMeshAgent missing) remains Failure. On selection, reorder the compatibility snapshot, publish the selected object, and set `HasLiveTarget=true`.

- [ ] **Step 4: Add `AdvanceToVillageAction`**

```csharp
protected override Status OnStart()
{
    if (Self.Value == null || !Self.Value.TryGetComponent(out Dino dino)) return Status.Failure;
    Dino = dino;
    return Dino.EnsureVillageNavigation() ? Status.Running : Status.Failure;
}

protected override Status OnUpdate()
{
    if (Dino == null) return Status.Failure;
    if (Dino.HasTrackedLiveTargets) return Status.Success;
    Dino.EnsureVillageNavigation();
    return Status.Running;
}
```

The Action has no `ResetPath` in `OnEnd`.

- [x] **Step 5: Restrict movement path clearing to confirmed attack range**

Add `ReachedAttackRange=false` at `MoveToClosestAttackableOnXzPlaneAction.OnStart`. Set it true only immediately before returning Success due to attack range/target-location satisfaction. `OnEnd` calls `ResetPath()` only when `ReachedAttackRange` is true and the agent remains valid. A dead/unreachable target returns Failure without erasing the recovery intent.

- [x] **Step 6: Reauthor `Dino Graph.asset` in the Unity Behavior Graph editor**

Record the pre-edit asset SHA-256. In one connected Editor, add Boolean `HasLiveTarget` and replace the runtime graph with this exact loop:

```text
Start
└─ Repeat Forever
   └─ Sequence
      ├─ Sort Attackables / Evaluate Target
      │  ├─ NearbyAttackables
      │  ├─ Self
      │  ├─ AttackConfig
      │  ├─ ClosestAttackable
      │  └─ HasLiveTarget
      └─ Branch on HasLiveTarget
         ├─ true: Sequence
         │  ├─ Move To Closest Attackable On XZ Plane
         │  ├─ Stop NavMeshagent
         │  ├─ Attack Closest Object
         │  └─ Wait(PostAttackCooldown)
         └─ false: Advance To Village
```

Delete the runtime `ParallelAllComposite`, `WaitForEventAction`, and death-event abort path. Keep `AttackEventChannel` for animation/IK notification. Save only `Dino Graph.asset`; do not touch the three Dino Prefabs, Packages, ProjectSettings, or scene.

- [x] **Step 7: Run Task 4 GREEN and inspect graph serialization scope**

Run the Task 4 action/graph tests. Check:

```powershell
git -C 'C:\Users\hp\Documents\New project 2\game_project\dino-attack' diff --stat -- 'Assets/LlamAcademy/Dinos/Dinos/Dino Graph.asset'
git -C 'C:\Users\hp\Documents\New project 2\game_project\dino-attack' diff --check -- 'Assets/LlamAcademy/Dinos/Dinos/Dino Graph.asset'
```

Expected: tests PASS; only the approved graph asset changed during the Editor step; no YAML corruption warning; do not stage or commit.

---

### Task 5: Prove shared-target recovery and close the Behavior teardown blocker

**Files:**

- Modify: `dino-attack/Assets/Tests/PlayMode/DinoCombatBehaviorPlayModeTests.cs`
- Preserve/regression-run: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Create: `reports/phase8/dino-combat-behavior/validation.md`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**

- Consumes the complete Task 1–4 runtime flow.
- Produces auditable evidence for multiple attackers, multiple successive target deaths, delayed attacks, navigation recovery, clean graph teardown, and unchanged existing behavior.

- [ ] **Step 1: Add the real three-dinosaur shared-target regression**

Load `Dinos`, enter Running, deploy three Pachycephalosaurus close to one live defender, and position them so that defender is the only local highest-priority candidate. Capture each dinosaur's `BehaviorGraphAgent`, `NavMeshAgent`, tracker state, and target health. After the shared defender dies, require every living dinosaur within `PostAttackCooldown + 1.0s` to satisfy one of:

```text
A. HasLiveTarget == true and ClosestAttackable is alive;
B. NavMeshAgent.isStopped == false, hasPath == true, and destination is the village target;
C. It has already damaged a different living target.
```

Repeat for three successive target deaths in the same PlayMode test. Capture `Application.logMessageReceived` and fail on `NullReferenceException`, `BehaviorGraphModule.EndBranch`, duplicate-death diagnostics, or an unexpected Action exception.

- [ ] **Step 2: Run the shared-target test and fix only evidence-backed defects**

Run:

```powershell
& 'D:\job\6000.3.22f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\hp\Documents\New project 2\game_project\dino-attack' -runTests -testPlatform PlayMode -testFilter 'DinoCombatBehaviorPlayModeTests.ThreeDinos_' -testResults 'C:\Users\hp\Documents\New project 2\game_project\reports\phase8\dino-combat-behavior\shared-target.xml' -logFile 'C:\Users\hp\Documents\New project 2\game_project\reports\phase8\dino-combat-behavior\shared-target.log' -quit
```

Expected: PASS. If it fails, record the exact dino state (`HasLiveTarget`, candidate count, current target, `isStopped`, `hasPath`, path status, destination, last attack time) before making a minimal correction. Do not change package versions as a workaround.

- [ ] **Step 3: Run the focused regression set in a full-reload session**

Run only:

```text
DinoCombatBehaviorPlayModeTests
OpenTropicalBattlefieldPlayModeTests.AttackAction_RejectsATargetThatIsAlreadyDead
OpenTropicalBattlefieldPlayModeTests.AttackPath_WithSolidBlocker_StillRaisesTheRealAttackEvent
OpenTropicalBattlefieldPlayModeTests.AttackPath_HasNoOcclusionQueryInvariant
OpenTropicalBattlefieldPlayModeTests.DinoAfterKillingATarget_ReselectsAndContinuesTheBattle
OpenTropicalBattlefieldPlayModeTests.DinoAfterLosingItsLastNearbyTarget_ContinuesTowardVillageCenter
OpenTropicalBattlefieldPlayModeTests.SpeedController_UsesExactApprovedMultiplier
```

Also run the EditMode fixture `DinoTargetSelectorTests` to preserve all three approved species priorities and stable path selection. Expected: all PASS; Unity compile error count zero; no new Console exception. Record the previous Enter Play Mode options and prove they were restored exactly after validation.

- [ ] **Step 4: Perform one real-scene manual/runtime smoke**

In `Dinos.unity`, spawn at least three dinosaurs of mixed species, let them share a guard/wall/house target, and observe three target transitions. Record for each transition: target type, attackers, death count, next target or village recovery, and whether any dinosaur remains stopped for more than its configured cooldown plus one second.

Expected: no permanent stop; death/reward once; target priorities and terrain speed remain unchanged; cover still does not block attacks.

- [x] **Step 5: Review the complete changed surface**

Run explicit status and diff checks in both repositories. Confirm no modified Packages, ProjectSettings, scene layout, Prefab, ScriptableObject combat values, observation/action code, or training configuration is part of this task. Do not stage or commit.

- [x] **Step 6: Write validation evidence and update the master plan**

`validation.md` must include:

- baseline root-cause evidence;
- exact RED/GREEN commands and test counts;
- graph asset pre/post SHA-256;
- Unity version and Behavior package version;
- full-reload setting restoration;
- shared-target state table for all three dinosaurs;
- Console exception scan;
- changed-file list and excluded surfaces;
- remaining limitation if `BehaviorGraphModule.EndBranch` persists.

Update `docs/PROJECT_PLAN.md` with the new corrective task status, verification evidence, artifact paths, and decision record. Mark complete only when every completion criterion in the spec is evidenced. If the same package-internal `EndBranch` exception still occurs in a clean simplified graph, mark this corrective task blocked and open a separate package-compatibility investigation; do not silently upgrade/downgrade.

---

## Final Exit Criteria

- `DinoTargetTracker` owns a per-dino deduplicated live-target set and one death subscription per target.
- Sensor and tracker cleanup are idempotent across duplicate collider events, death, disable, and destruction.
- `Unit`/`Defender` death event and death side effects occur once.
- Immediate, delayed, and moving-particle attacks never damage an already dead target and submit one effect per attack.
- `Dino Graph` evaluates a sanitized target state, has no parallel death-event runtime branch, and uses the explicit `HasLiveTarget` branch.
- Losing a target before attack range does not erase the recovery path.
- Three dinosaurs sharing one target all retarget or resume the village after death, across three successive deaths.
- Existing target priorities, attack pacing, terrain effects, no-LOS rule, map layout, v2 observations, 4D actions, rewards, and training lifecycle remain unchanged.
- Focused tests, compile, clean full-reload scene smoke, diff review, and Console scan pass with recorded evidence.
