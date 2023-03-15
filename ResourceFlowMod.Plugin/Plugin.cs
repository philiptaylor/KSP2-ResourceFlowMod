// Copyright (c) 2023 Philip Taylor
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using Newtonsoft.Json;
using Shapes;
using UnityEngine;

using ResourceFlowMod.Lib;

namespace ResourceFlowMod.Plugin
{
    class UpdateTimer
    {
        System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
        internal TimeSpan LastElapsed;

        internal void Start()
        {
            _stopwatch.Start();
        }

        internal void Stop()
        {
            _stopwatch.Stop();
        }

        internal void FixedUpdate()
        {
            LastElapsed = _stopwatch.Elapsed;
            _stopwatch.Reset();
        }
    }

    class LogWrapper : ResourceFlowMod.Lib.ILogger
    {
        ManualLogSource _logger;

        internal LogWrapper(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void LogDebug(string data) => _logger.LogDebug(data);
        public void LogError(string data) => _logger.LogError(data);
        public void LogFatal(string data) => _logger.LogFatal(data);
        public void LogInfo(string data) => _logger.LogInfo(data);
        public void LogMessage(string data) => _logger.LogMessage(data);
        public void LogWarning(string data) => _logger.LogWarning(data);
    }

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static LogWrapper Log;

        // TODO: This is a mess
        internal static UpdateTimer SetCommandsOldTimer = new UpdateTimer();
        internal static UpdateTimer SetCommandsNewTimer = new UpdateTimer();
        internal static UpdateTimer UpdateFlowRequestsOldTimer = new UpdateTimer();
        internal static UpdateTimer UpdateFlowRequestsNewTimer = new UpdateTimer();
        internal static UpdateTimer RequestsUpdatedTimer = new UpdateTimer();
        internal static UpdateTimer ProcessActiveRequestsTimer = new UpdateTimer();
        internal static UpdateTimer CopyContainersTimer = new UpdateTimer();

        internal static bool ResourceManagerIsVisible = false;

        private Rect _debugWindowRect = new Rect(20, 20, 580, 1040);
        private bool _debugEnableContainerOverlay = false;
        private bool _debugEnableScOverlay = false;
        private Vector2 _debugRequestScroll;

        private void Awake()
        {
            Log = new LogWrapper(base.Logger);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Logger.LogInfo($"Game version {VersionID.VERSION_TEXT}");

            Harmony.CreateAndPatchAll(typeof(Patches_FlowGraph));
            Harmony.CreateAndPatchAll(typeof(Patches_PartComponent));
            Harmony.CreateAndPatchAll(typeof(Patches_ResourceContainer));
            Harmony.CreateAndPatchAll(typeof(Patches_ResourceFlowRequestManager));
            Harmony.CreateAndPatchAll(typeof(Patches_ResourceManagerUI));
        }

        public void FixedUpdate()
        {
            SetCommandsOldTimer.FixedUpdate();
            SetCommandsNewTimer.FixedUpdate();
            UpdateFlowRequestsOldTimer.FixedUpdate();
            UpdateFlowRequestsNewTimer.FixedUpdate();
            RequestsUpdatedTimer.FixedUpdate();
            ProcessActiveRequestsTimer.FixedUpdate();
            CopyContainersTimer.FixedUpdate();
        }

        public void OnGUI()
        {
            // if (ResourceManagerIsVisible)
            {
                _debugWindowRect = GUI.Window(0, _debugWindowRect, DrawDebugWindow, "Resource Flow Mod");
            }

            if (_debugEnableContainerOverlay)
            {
                DrawContainerOverlay();
            }
        }

        static GUILayoutOption[] s_debugRequestWidths = (new int[] { 15, 35, 40, 25, 25, 60, 140, 120 }).Select(i => GUILayout.Width(i + 5)).ToArray();
        static Dictionary<string, bool> s_partToggles = new Dictionary<string, bool>();

        void DrawDebugWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.BeginVertical(GUI.skin.box);
            _debugEnableContainerOverlay = GUILayout.Toggle(_debugEnableContainerOverlay, "Container overlay");
            _debugEnableScOverlay = GUILayout.Toggle(_debugEnableScOverlay, "Connected component overlay");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Performance (time per FixedUpdate)");
            GUILayout.Label($"(old) SetCommands: {SetCommandsOldTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(new) SetCommands: {SetCommandsNewTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(old) UpdateFlowRequests: {UpdateFlowRequestsOldTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(old) ProcessActiveRequests: {ProcessActiveRequestsTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(new) UpdateFlowRequests: {UpdateFlowRequestsNewTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(new) CopyContainers: {CopyContainersTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.Label($"(new) RequestsUpdated: {RequestsUpdatedTimer.LastElapsed.TotalMilliseconds:F3} ms");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"<b><size=14>Requests</size></b>");

            GUILayout.BeginHorizontal();
            var widths = s_debugRequestWidths;
            GUILayout.Label(new GUIContent("<b>Act</b>", "Active"), widths[0]);
            GUILayout.Label(new GUIContent("<b>Min</b>", "Minimum flow percentage"), widths[1]);
            GUILayout.Label(new GUIContent("<b>Accept</b>", "Percentage of flow request accepted in the last tick"), widths[2]);
            GUILayout.Label(new GUIContent("<b>Res</b>", "Resource type"), widths[3]);
            GUILayout.Label(new GUIContent("<b>Dir</b>", "Direction of flow (in or out of vessel)"), widths[4]);
            GUILayout.Label(new GUIContent("<b>Rate</b>", "Rate of flow (units per second)"), widths[5]);
            GUILayout.Label(new GUIContent("<b>Usable</b>", "Usable resources/space (% of capacity)"), widths[6]);
            GUILayout.Label(new GUIContent("<b>Mode</b>", "Flow mode"), widths[7]);
            GUILayout.EndHorizontal();

            _debugRequestScroll = GUILayout.BeginScrollView(_debugRequestScroll, GUI.skin.box);

            var currentVessel = KSP.Game.GameManager.Instance?.Game?.ViewController?.GetActiveSimVessel();
            if (currentVessel == null)
            {
                GUILayout.Label("<i>No active vessel</i>");
            }
            else
            {
                DrawDebugWindowVessel(currentVessel, true);
            }

            var vessels = KSP.Game.GameManager.Instance?.Game?.UniverseModel?.GetAllVessels();
            if (vessels != null)
            {
                foreach (var vessel in vessels)
                {
                    if (vessel != currentVessel)
                    {
                        DrawDebugWindowVessel(vessel, false);
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUI.tooltip != "")
            {
                var style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                GUI.Label(new Rect(Event.current.mousePosition + new Vector2(10, 10), new Vector2(350, 25)), GUI.tooltip, style);
            }
        }

        void DrawDebugWindowVessel(VesselComponent vessel, bool isCurrent)
        {
            var vesselMan = vessel?.SimulationObject?.PartOwner?.ResourceFlowRequestManager;
            if (vesselMan == null || !Patches_ResourceFlowRequestManager.s_requestManagers.TryGetValue(vesselMan, out RequestManager requestManager))
            {
                GUILayout.Label("<i>Error querying vessel</i>");
                return;
            }

            var db = KSP.Game.GameManager.Instance.Game.ResourceDefinitionDatabase;
            var widths = s_debugRequestWidths;

            GUILayout.BeginHorizontal();
            var vesselGuid = vessel.GlobalId.ToString();
            if (!s_partToggles.TryGetValue(vesselGuid, out bool vesselToggle))
                vesselToggle = true;
            s_partToggles[vesselGuid] = GUILayout.Toggle(vesselToggle, $"<b>{(isCurrent ? "Current vessel" : "Vessel")}:</b> {vessel.DisplayName}");
            GUILayout.EndHorizontal();

            if (!s_partToggles[vesselGuid])
            {
                return;
            }

            var requestsByPart = new Dictionary<IFlowNode, List<RequestProcessor.Request>>();
            foreach (var request in requestManager._requests.Values)
            {
                if (!requestsByPart.TryGetValue(request.Node, out var requests))
                {
                    requests = new List<RequestProcessor.Request>();
                    requestsByPart.Add(request.Node, requests);
                }
                requests.Add(request.Request);
            }

            foreach (var item in requestsByPart)
            {
                var name = "?";
                var guid = item.Key.GlobalId.ToString();
                if (item.Key is PartComponent part)
                {
                    name = part.Name;
                }
                GUILayout.BeginHorizontal();
                if (!s_partToggles.TryGetValue(guid, out bool toggle))
                    toggle = true;
                s_partToggles[guid] = GUILayout.Toggle(toggle, $"{name}");
                GUILayout.EndHorizontal();

                if (toggle)
                {
                    foreach (var request in item.Value)
                    {
                        for (int i = 0; i < request.Instructions.Count; ++i)
                        {
                            GUILayout.BeginHorizontal();
                            var instr = request.Instructions[i];
                            if (i == 0)
                            {
                                GUILayout.Label(request.Active ? "<color=#00ff00ff>Y</color>" : "<color=#ff0000ff>N</color>", widths[0]);
                                GUILayout.Label($"{request.MinimalFraction:P0}", widths[1]);
                                GUILayout.Label(request.WasLastTickDeliveryAccepted ? $"{request.LastTickDeliveryNormalized:P0}" : "-", widths[2]);
                            }
                            else
                            {
                                GUILayout.Label("\"", widths[0]);
                                GUILayout.Label("\"", widths[1]);
                                GUILayout.Label("\"", widths[2]);
                            }

                            var abbrev = db.GetDefinitionData(new ResourceDefinitionID(instr.ResourceId)).DisplayAbbreviation;
                            abbrev = abbrev.Substring(abbrev.LastIndexOf("/") + 1);

                            GUILayout.Label(abbrev, widths[3]);
                            GUILayout.Label(instr.IsInbound ? "In" : "Out", widths[4]);
                            GUILayout.Label($"{instr.Optimal:g3}", widths[5]);

                            var resources = requestManager.GetInstructionResources(request, instr);
                            var usable = instr.IsInbound ? resources.CapacityUnits - resources.StoredUnits : resources.StoredUnits;
                            GUILayout.Label($"{usable:f1} / {resources.CapacityUnits:f1} ({usable / resources.CapacityUnits:P0})", widths[6]);

                            string mode;
                            switch (instr.Mode)
                            {
                                case ResourceFlowMod.Lib.ResourceFlowMode.NULL: mode = "NULL"; break;
                                case ResourceFlowMod.Lib.ResourceFlowMode.NO_FLOW: mode = "NoFlow"; break;
                                case ResourceFlowMod.Lib.ResourceFlowMode.ALL_VESSEL: mode = "AllVessel"; break;
                                case ResourceFlowMod.Lib.ResourceFlowMode.STAGE_PRIORITY_FLOW: mode = "StagePriorityFlow"; break;
                                case ResourceFlowMod.Lib.ResourceFlowMode.STACK_PRIORITY_SEARCH: mode = "StackPrioritySearch"; break;
                                case ResourceFlowMod.Lib.ResourceFlowMode.STAGE_STACK_FLOW_BALANCE: mode = "StageStackFlowBalance"; break;
                                default: mode = "?"; break;
                            }
                            GUILayout.Label(mode, widths[7]);
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
        }

        class PartContainerLabel
        {
            public Vector3 Position;
            public int Priority;
            public List<(string, double)> Containers = new List<(string, double)>();
        }

        void DrawContainerOverlay()
        {
            var flowGraph = KSP.Game.GameManager.Instance?.Game?.ViewController?.GetActiveSimVessel()?.SimulationObject?.PartOwner?.FlowGraph;
            if (flowGraph == null)
                return;
            var extra = Plugin.GetFlowGraphExtra(flowGraph);

            var db = KSP.Game.GameManager.Instance.Game.ResourceDefinitionDatabase;

            var partContainers = new Dictionary<int, PartContainerLabel>();
            foreach (var container in extra.BasicFlowGraph.Containers)
            {
                var part = extra.VesselState.PartMapping[container.Part] as PartComponent;
                if (part == null)
                    continue;

                var simObjView = KSP.Game.GameManager.Instance.Game.SpaceSimulation.ModelViewMap.FromModel(part.SimulationObject);
                if (simObjView == null)
                    continue;

                PartContainerLabel label;
                if (!partContainers.TryGetValue(container.Part, out label))
                {
                    partContainers[container.Part] = label = new PartContainerLabel
                    {
                        Position = simObjView.position,
                        Priority = container.Priority,
                    };
                }
                var abbrev = db.GetDefinitionData(new ResourceDefinitionID(container.ResourceID)).DisplayAbbreviation;
                label.Containers.Add((
                    abbrev.Substring(abbrev.LastIndexOf("/") + 1),
                    container.StoredUnits / container.CapacityUnits
                ));
            }

            var style = new GUIStyle(GUI.skin.box);
            style.fontSize = 10;
            style.clipping = TextClipping.Overflow;
            style.wordWrap = false;
            style.padding = new RectOffset(2, 2, 2, 2);

            foreach (var part in partContainers.Keys)
            {
                var label = partContainers[part];

                var pos = UnityEngine.Camera.main.WorldToViewportPoint(label.Position);
                float width = 48.0f;
                float rowHeight = 13.0f;
                float height = rowHeight * (float)(1 + label.Containers.Count);
                var rect = new Rect(
                    (float)Math.Round(Camera.main.pixelWidth * pos.x - width / 2.0f),
                    (float)Math.Round(Camera.main.pixelHeight * (1.0f - pos.y) - height / 2.0f + 2.0f),
                    (float)Math.Round(width),
                    (float)Math.Round(height)
                );
                var text = $"Pri {label.Priority}\n" + String.Join("\n", label.Containers.Select(c => $"{c.Item1} {c.Item2:P0}"));
                GUI.Box(rect, text, style);
            }
        }

        static UnityEngine.Color[] _scColours = {
            new UnityEngine.Color(1, 0, 0, 1),
            new UnityEngine.Color(0, 1, 0, 1),
            new UnityEngine.Color(0, 0, 1, 1),
            new UnityEngine.Color(1, 1, 0, 1),
            new UnityEngine.Color(1, 0, 1, 1),
            new UnityEngine.Color(0, 1, 1, 1),

            new UnityEngine.Color(0.3f, 0.3f, 0.3f, 1),

            new UnityEngine.Color(0.3f, 0, 0, 1),
            new UnityEngine.Color(0, 0.3f, 0, 1),
            new UnityEngine.Color(0, 0, 0.3f, 1),
            new UnityEngine.Color(0.3f, 0.3f, 0, 1),
            new UnityEngine.Color(0.3f, 0, 0.3f, 1),
            new UnityEngine.Color(0, 0.3f, 0.3f, 1),

            new UnityEngine.Color(1.0f, 0.3f, 0, 1),
            new UnityEngine.Color(1.0f, 0, 0.3f, 1),
            new UnityEngine.Color(0, 1.0f, 0.3f, 1),

            new UnityEngine.Color(0.3f, 1.0f, 0, 1),
            new UnityEngine.Color(1.0f, 0, 1.0f, 1),
            new UnityEngine.Color(0, 1.0f, 1.0f, 1),
        };

        private void DrawScOverlay(UnityEngine.Camera cam)
        {
            var flowGraph = KSP.Game.GameManager.Instance?.Game?.ViewController?.GetActiveSimVessel()?.SimulationObject?.PartOwner?.FlowGraph;
            if (flowGraph == null)
                return;
            var extra = Plugin.GetFlowGraphExtra(flowGraph);

            using (Draw.Command(cam, UnityEngine.Rendering.CameraEvent.BeforeImageEffects))
            {
                Draw.LineGeometry = LineGeometry.Volumetric3D;
                Draw.LineThicknessSpace = ThicknessSpace.Pixels;
                Draw.LineThickness = 4;

                Draw.ResetMatrix();

                var basicFlowGraph = extra.BasicFlowGraph;

                for (int part1 = 0; part1 < basicFlowGraph.Parts.Count; ++part1)
                {
                    for (int i = 0; i < basicFlowGraph.Parts[part1].NumEdges; ++i)
                    {
                        var part2 = basicFlowGraph.Edges[basicFlowGraph.Parts[part1].EdgeOffset + i];

                        var comp1 = extra.VesselState.PartMapping[part1] as PartComponent;
                        var comp2 = extra.VesselState.PartMapping[part2] as PartComponent;
                        if (comp1 == null || comp2 == null)
                            continue;

                        var simObjView1 = KSP.Game.GameManager.Instance.Game.SpaceSimulation.ModelViewMap.FromModel(comp1.SimulationObject);
                        var simObjView2 = KSP.Game.GameManager.Instance.Game.SpaceSimulation.ModelViewMap.FromModel(comp2.SimulationObject);
                        if (simObjView1 == null || simObjView2 == null)
                            continue;

                        Draw.Line(
                            cam.ViewportToWorldPoint(UnityEngine.Camera.main.WorldToViewportPoint(simObjView1.position)),
                            cam.ViewportToWorldPoint(UnityEngine.Camera.main.WorldToViewportPoint(simObjView2.position)),
                            _scColours[basicFlowGraph.Parts[part1].Index % _scColours.Length],
                            _scColours[basicFlowGraph.Parts[part2].Index % _scColours.Length]);
                    }
                }
            }
        }

        private void OnCameraPreRender(UnityEngine.Camera cam)
        {
            if (_debugEnableScOverlay && cam.name == "UI Camera")
            {
                DrawScOverlay(cam);
            }
        }

        public virtual void OnEnable()
        {
            UnityEngine.Camera.onPreRender = (UnityEngine.Camera.CameraCallback)System.Delegate.Combine(UnityEngine.Camera.onPreRender, new UnityEngine.Camera.CameraCallback(OnCameraPreRender));
        }

        public virtual void OnDisable()
        {
            UnityEngine.Camera.onPreRender = (UnityEngine.Camera.CameraCallback)System.Delegate.Remove(UnityEngine.Camera.onPreRender, new UnityEngine.Camera.CameraCallback(OnCameraPreRender));
        }

        internal static ResourceFlowMod.Lib.FlowDirection ConvertFlowDirection(KSP.Sim.ResourceSystem.FlowDirection mode)
        {
            switch (mode)
            {
                case KSP.Sim.ResourceSystem.FlowDirection.FLOW_INBOUND: return ResourceFlowMod.Lib.FlowDirection.FLOW_INBOUND;
                case KSP.Sim.ResourceSystem.FlowDirection.FLOW_OUTBOUND: return ResourceFlowMod.Lib.FlowDirection.FLOW_OUTBOUND;
                default: throw new Exception("Invalid flow direction");
            }
        }

        internal static ResourceFlowMod.Lib.ResourceFlowMode ConvertFlowMode(KSP.Sim.ResourceSystem.ResourceFlowMode mode)
        {
            switch (mode)
            {
                case KSP.Sim.ResourceSystem.ResourceFlowMode.NULL: return ResourceFlowMod.Lib.ResourceFlowMode.NULL;
                case KSP.Sim.ResourceSystem.ResourceFlowMode.NO_FLOW: return ResourceFlowMod.Lib.ResourceFlowMode.NO_FLOW;
                case KSP.Sim.ResourceSystem.ResourceFlowMode.ALL_VESSEL: return ResourceFlowMod.Lib.ResourceFlowMode.ALL_VESSEL;
                case KSP.Sim.ResourceSystem.ResourceFlowMode.STAGE_PRIORITY_FLOW: return ResourceFlowMod.Lib.ResourceFlowMode.STAGE_PRIORITY_FLOW;
                case KSP.Sim.ResourceSystem.ResourceFlowMode.STACK_PRIORITY_SEARCH: return ResourceFlowMod.Lib.ResourceFlowMode.STACK_PRIORITY_SEARCH;
                case KSP.Sim.ResourceSystem.ResourceFlowMode.STAGE_STACK_FLOW_BALANCE: return ResourceFlowMod.Lib.ResourceFlowMode.STAGE_STACK_FLOW_BALANCE;
                default: throw new Exception("Invalid mode");
            }
        }

        private void DumpResourceDb()
        {
            var db = KSP.Game.GameManager.Instance?.Game?.ResourceDefinitionDatabase;
            if (db == null)
                return;

            var defs = new ResourceDefDatabase();
            foreach (var id in db.GetAllResourceIDs())
            {
                var res = db.GetDefinitionData(id);
                ResourceProperties resProps = null;
                RecipeProperties recProps = null;
                if (res.resourceProperties != null)
                {
                    resProps = new ResourceProperties
                    {
                        FlowMode = ConvertFlowMode(res.resourceProperties.flowMode),
                        NonStageable = res.resourceProperties.NonStageable,
                    };
                }
                if (res.recipeProperties != null)
                {
                    recProps = new RecipeProperties
                    {
                        Ingredients = res.recipeProperties.ingredients.Select(x => new Lib.ResourceUnitsPair { ResourceId = x.resourceID.Value, Units = x.units }).ToList(),
                    };
                }
                defs.Resources.Add(new ResourceDef
                {
                    ResourceId = id.Value,
                    Name = res.name,
                    Abbreviation = res.abbreviationKey,
                    IsRecipeInDatabase = res.isRecipeInDatabase,
                    IsRecipe = res.IsRecipe,
                    ResourceProperties = resProps,
                    RecipeProperties = recProps,
                });
            }

            Plugin.Log.LogInfo($"\n<<<<ResourceDefinitionDatabase\n{JsonConvert.SerializeObject(defs, Formatting.Indented)}\n>>>>");
        }

        static ConditionalWeakTable<FlowGraph, FlowGraphExtra> _flowGraphExtra = new ConditionalWeakTable<FlowGraph, FlowGraphExtra>();

        internal static FlowGraphExtra GetFlowGraphExtra(FlowGraph flowGraph)
        {
            return _flowGraphExtra.GetValue(flowGraph, (_) => new FlowGraphExtra());
        }
    }
}
