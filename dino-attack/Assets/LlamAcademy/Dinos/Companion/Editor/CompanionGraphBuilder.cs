using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using BehaviorNodeRegistry = Unity.Behavior.NodeRegistry;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.Companion.Editor
{
    /// <summary>
    /// Creates the companion graph as a real Unity Behavior authoring asset.  The
    /// asset deliberately uses ordinary nodes and a Switch over the companion
    /// activity enum; designers can open and edit the result in the Behavior
    /// window after the builder runs.
    /// </summary>
    public static class CompanionGraphBuilder
    {
        public const string GraphPath = "Assets/LlamAcademy/Dinos/Companion/Companion Behavior Graph.asset";

        private static readonly (string[] Types, string[] ActivityNames)[] ActionBranches =
        {
            (new[] { "LlamAcademy.Dinos.Companion.CompanionFollowAction", "LlamAcademy.Dinos.Companion.CompanionGraphTickAction" }, new[] { "FollowPlayer", "Follow" }),
            (new[] { "LlamAcademy.Dinos.Companion.CompanionCombatAction", "LlamAcademy.Dinos.Companion.CompanionGraphTickAction" }, new[] { "ProtectPlayer", "Combat" }),
            (new[] { "LlamAcademy.Dinos.Companion.CompanionRecoverAction", "LlamAcademy.Dinos.Companion.CompanionGraphTickAction" }, new[] { "Regroup", "Recover" }),
            (new[] { "LlamAcademy.Dinos.Companion.CompanionAutonomousDefenseAction", "LlamAcademy.Dinos.Companion.CompanionGraphTickAction" }, new[] { "AutonomousDefense", "Autonomous" }),
            (new[] { "LlamAcademy.Dinos.Companion.CompanionHoldAction", "LlamAcademy.Dinos.Companion.CompanionGraphTickAction" }, new[] { "HoldPosition", "Hold", "Guard" }),
        };

        [MenuItem("Dino Attack/Companion/Build Behavior Graph")]
        public static void BuildMenu() => BuildOrUpdate();

        public static BehaviorGraph BuildOrUpdate()
        {
            EnsureFolder("Assets/LlamAcademy/Dinos/Companion");
            BehaviorAuthoringGraph authoring = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
            if (authoring == null)
            {
                authoring = ScriptableObject.CreateInstance<BehaviorAuthoringGraph>();
                authoring.name = Path.GetFileNameWithoutExtension(GraphPath);
                AssetDatabase.CreateAsset(authoring, GraphPath);
                AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
                authoring = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
            }

            // A graph created from code must go through the same validation path
            // as one created in the Behavior window.  This creates its embedded
            // authoring blackboard and Start node when necessary.
            authoring.ValidateAsset();
            VariableModel activityVariable = EnsureActivityVariable(authoring);
            EnsureCompanionVariable(authoring);
            Type activityType = activityVariable.Type;
            NodeModel start = authoring.Roots.FirstOrDefault();
            if (start == null)
            {
                start = authoring.CreateNode(typeof(StartNodeModel), new Vector2(-760f, 0f), null,
                        new object[] { BehaviorNodeRegistry.GetInfo(typeof(Start)) });
            }

            // Avoid multiplying nodes when the batch builder is run repeatedly.
            // The older generated graph used a Switch node with dynamically
            // named output ports. Those ports could be regenerated after the
            // editor connected them, producing a graph whose branches were all
            // uninitialized at runtime. Keep the graph deterministic: one
            // always-connected action ticks the controller, while the
            // controller applies the approved Follow/Protect/Hold/Autonomous
            // mode selected by the local rules and JEV response.
            authoring.Nodes.Clear();
            start = authoring.CreateNode(typeof(StartNodeModel), new Vector2(-760f, 0f), null,
                new object[] { BehaviorNodeRegistry.GetInfo(typeof(Start)) });
            Type tickType = FindRuntimeType("LlamAcademy.Dinos.Companion.CompanionGraphTickAction");
            BehaviorGraphNodeModel tick = null;
            if (tickType != null)
            {
                NodeInfo info = BehaviorNodeRegistry.GetInfo(tickType);
                if (info != null)
                {
                    tick = authoring.CreateNode(info.ModelType, new Vector2(-360f, 0f), null,
                        new object[] { info }) as BehaviorGraphNodeModel;
                    LinkActionFields(tick, authoring, activityVariable);
                    Connect(start, tick);
                }
            }

            authoring.Description = "Companion behavior graph tick; the controller applies the approved Follow/Protect/Hold/Autonomous mode.";
            authoring.SetAssetDirty(true);
            authoring.ValidateAsset();
            BehaviorGraph runtime = authoring.BuildRuntimeGraph(forceRebuild: true);
            authoring.SaveAsset();
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
            return runtime;
        }

        private static BehaviorGraphNodeModel CreateSwitch(BehaviorAuthoringGraph graph, VariableModel activityVariable, Vector2 position)
        {
            Type switchType = FindRuntimeType("Unity.Behavior.SwitchComposite");
            if (switchType == null) return null;
            NodeInfo info = BehaviorNodeRegistry.GetInfo(switchType);
            if (info == null) return null;
            BehaviorGraphNodeModel node = graph.CreateNode(info.ModelType, position, null, new object[] { info }) as BehaviorGraphNodeModel;
            node?.SetField("EnumVariable", activityVariable, activityVariable.Type);
            return node;
        }

        private static BehaviorGraphNodeModel CreateFirstAvailableAction(BehaviorAuthoringGraph graph, string[] typeNames, Vector2 position)
        {
            Type type = FindRuntimeType(typeNames);
            if (type == null) return null;
            NodeInfo info = BehaviorNodeRegistry.GetInfo(type);
            return info == null ? null : graph.CreateNode(info.ModelType, position, null, new object[] { info }) as BehaviorGraphNodeModel;
        }

        private static void LinkActionFields(BehaviorGraphNodeModel action, BehaviorAuthoringGraph graph, VariableModel activityVariable)
        {
            foreach (string fieldName in new[] { "Self", "Controller", "Companion", "Player", "Activity", "RequestedActivity" })
            {
                Type fieldType = ResolveFieldType(action.NodeType.Type, fieldName);
                if (fieldType == null) continue;
                VariableModel variable = graph.Blackboard.Variables.FirstOrDefault(value =>
                    string.Equals(value.Name, fieldName, StringComparison.OrdinalIgnoreCase) && value.Type == fieldType);
                if (variable != null)
                {
                    action.SetField(fieldName, variable, fieldType);
                }
                else if (fieldName is "Activity" or "RequestedActivity" && fieldType == activityVariable.Type)
                {
                    action.SetField(fieldName, activityVariable, fieldType);
                }
            }
        }

        private static Type ResolveFieldType(Type nodeType, string fieldName)
        {
            System.Reflection.FieldInfo field = nodeType.GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field == null) return null;
            Type fieldType = field.FieldType;
            return fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(BlackboardVariable<>)
                ? fieldType.GetGenericArguments()[0]
                : null;
        }

        private static VariableModel EnsureActivityVariable(BehaviorAuthoringGraph graph)
        {
            Type enumType = FindActivityEnumType();
            VariableModel existing = graph.Blackboard.Variables.FirstOrDefault(variable => variable.Type == enumType);
            if (existing != null)
            {
                existing.Name = "Activity";
                return existing;
            }

            Type modelType = typeof(TypedVariableModel<>).MakeGenericType(enumType);
            VariableModel variable = (VariableModel)Activator.CreateInstance(modelType);
            variable.Name = "Activity";
            variable.IsExposed = true;
            variable.ObjectValue = Enum.Parse(enumType, ResolveEnumName(enumType, "Follow"));
            graph.Blackboard.Variables.Add(variable);
            graph.Blackboard.SetAssetDirty();
            return variable;
        }

        private static VariableModel EnsureCompanionVariable(BehaviorAuthoringGraph graph)
        {
            VariableModel existing = graph.Blackboard.Variables.FirstOrDefault(variable =>
                string.Equals(variable.Name, "Companion", StringComparison.OrdinalIgnoreCase) &&
                variable.Type == typeof(GameObject));
            if (existing != null)
            {
                existing.IsExposed = true;
                return existing;
            }

            TypedVariableModel<GameObject> variable = new() { Name = "Companion", IsExposed = true, ObjectValue = null };
            graph.Blackboard.Variables.Add(variable);
            graph.Blackboard.SetAssetDirty();
            return variable;
        }

        private static string ResolveEnumName(Type enumType, string preferred)
        {
            if (Enum.GetNames(enumType).Contains(preferred)) return preferred;
            return Enum.GetNames(enumType).FirstOrDefault() ?? preferred;
        }

        private static Type FindActivityEnumType()
        {
            string[] names = { "LlamAcademy.Dinos.Companion.CompanionActivity", "LlamAcademy.Dinos.Companion.CompanionMode", "LlamAcademy.Dinos.Companion.CompanionState" };
            Type found = FindRuntimeType(names);
            if (found != null && found.IsEnum) return found;
            throw new InvalidOperationException("CompanionActivity/CompanionMode enum is not available. Compile Companion runtime scripts before building the graph.");
        }

        private static Type FindRuntimeType(params string[] names)
        {
            foreach (string name in names)
            {
                Type direct = Type.GetType(name + ", Assembly-CSharp") ?? Type.GetType(name);
                if (direct != null) return direct;
            }
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string name in names)
                {
                    Type found = assembly.GetType(name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void Connect(NodeModel from, NodeModel to)
        {
            if (from == null || to == null || !from.TryDefaultOutputPortModel(out PortModel output) ||
                !to.TryDefaultInputPortModel(out PortModel input)) return;
            from.Asset.ConnectEdge(output, input);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
