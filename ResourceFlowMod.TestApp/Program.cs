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
using System.Text;
using System.Collections.Generic;

using Newtonsoft.Json;

using ResourceFlowMod.Lib;

namespace ResourceFlowMod.TestApp
{
    class LogWrapper : ResourceFlowMod.Lib.ILogger
    {
        public void LogDebug(string data) => System.Console.WriteLine($"DEBUG: {data}");
        public void LogError(string data) => System.Console.WriteLine($"\u001b[1;31mERROR: {data}\u001b[0m");
        public void LogFatal(string data) => System.Console.WriteLine($"\u001b[1;31mFATAL: {data}\u001b[0m");
        public void LogInfo(string data) => System.Console.WriteLine($"INFO: {data}");
        public void LogMessage(string data) => System.Console.WriteLine($"MSG: {data}");
        public void LogWarning(string data) => System.Console.WriteLine($"\u001b[1;33mWARN: {data}\u001b[0m");
    }

    class App
    {
        static string ToGraphviz(VesselGraph graph)
        {
            var s = new StringBuilder();

            s.AppendLine("digraph G {");
            s.AppendLine("rankdir=LR;");
            s.AppendLine("concentrate=true;");
            s.AppendLine("node[shape=rect];");
            s.AppendLine("newrank=true;");

            var tags = new List<string>();
            foreach (var part in graph.Parts)
            {
                tags.Add($"\"{part.Guid}\"");
            }

            for (var i = 0; i < graph.Parts.Count; ++i)
            {
                var part = graph.Parts[i];

                var label = $"{part.Name} Stage={part.ActivationStage} ";
                if (part.IsDecoupler)
                    label += "D";
                if (!part.FuelCrossfeed)
                    label += "X";
                if (part.IsFuelLine)
                    label += "F";
                if (part.IsFirstAnchor)
                    label += "1";

                s.AppendLine($"{tags[i]} [label=\"{label}\"];");

                if (part.OtherEndAnchor != -1)
                    s.AppendLine($"{tags[i]} -> {tags[part.OtherEndAnchor]} [constraint=false color=red];");
            }

            foreach (var attachment in graph.Attachments)
            {
                s.AppendLine($"{tags[attachment.From]} -> {tags[attachment.To]} [label=\"{(attachment.AllowCrossfeed ? "" : "X")}\"];");
            }

            for (var i = 0; i < graph.Resources.Count; ++i)
            {
                var resource = graph.Resources[i];
                var tag = $"\"resource_{i}\"";
                var label = $"R={resource.ResourceID} CU={resource.CapacityUnits}";
                s.AppendLine($"{tag} [label=\"{label}\" color=green];");
                s.AppendLine($"{tags[resource.Node]} -> {tag};");
            }

            s.AppendLine("}");

            return s.ToString();
        }

        static string ToGraphviz(BasicFlowGraph graph)
        {
            var s = new StringBuilder();

            s.AppendLine("digraph G {");
            s.AppendLine("rankdir=LR;");
            s.AppendLine("concentrate=true;");
            s.AppendLine("node[shape=rect];");
            s.AppendLine("newrank=true;");
            s.AppendLine("compound=true;");

            var tags = new List<string>();
            foreach (var part in graph.Parts)
            {
                tags.Add($"\"{part.Guid}\"");
            }

            var containerTags = new List<string>();
            for (int i = 0; i < graph.Containers.Count; ++i)
            {
                containerTags.Add($"container_{i}");
            }

            for (int i = 0; i < graph.StrongComponents.Count; ++i)
            {
                var sc = graph.StrongComponents[i];

                bool useCluster = (sc.NumParts > 1 || sc.NumContainers > 0 || true);
                if (useCluster)
                {
                    s.AppendLine($"subgraph cluster_comp{i} {{");
                    if (sc.NumReachableContainers > 0)
                        s.AppendLine($"cluster_reachable_{i} [label=\"Reachable\" color=blue];");
                    s.AppendLine($"color=red;");
                }

                for (int j = 0; j < sc.NumParts; ++j)
                {
                    int k = graph.StrongComponentParts[sc.PartOffset + j];
                    var part = graph.Parts[k];
                    if (part.Index != i)
                        throw new Exception("Invalid component index");
                    var label = $"{part.Name} Stage={part.Stage}";
                    s.AppendLine($"{tags[k]} [label=\"{label}\"];");
                }

                for (int j = 0; j < sc.NumContainers; ++j)
                {
                    int k = graph.StrongComponentContainers[sc.ContainerOffset + j];
                    var c = graph.Containers[k];
                    var label = $"R={c.ResourceID} CU={c.CapacityUnits:F3} SU={c.StoredUnits:F3} P={c.Priority}";
                    s.AppendLine($"{containerTags[k]} [label=\"{label}\" color=green];");
                    s.AppendLine($"{tags[c.Part]} -> {containerTags[k]} [color=green];");
                }

                if (useCluster)
                    s.AppendLine("}");
            }

            for (int i = 0; i < graph.StrongComponents.Count; ++i)
            {
                var sc = graph.StrongComponents[i];

                // for (int j = 0; j < sc.NumScEdges; ++j)
                // {
                //     s.AppendLine($"cluster_comp{i} -> cluster_comp{graph.StrongComponentEdges[sc.ScEdgeOffset + j]} [color=red];");
                // }

                for (int j = 0; j < sc.NumReachableContainers; ++j)
                {
                    //s.AppendLine($"cluster_reachable_{i} -> {containerTags[graph.StrongComponentReachableContainers[(int)sc.ReachableContainerOffset + j]]} [ltail=cluster_comp{i} color=blue];");
                    s.AppendLine($"cluster_reachable_{i} -> {containerTags[graph.StrongComponentReachableContainers[(int)sc.ReachableContainerOffset + j]]} [color=blue];");
                }
            }

            for (var i = 0; i < graph.Parts.Count; ++i)
            {
                var part = graph.Parts[i];

                for (int j = 0; j < part.NumEdges; ++j)
                {
                    s.AppendLine($"{tags[i]} -> {tags[graph.Edges[part.EdgeOffset + j]]};");
                }
            }

            s.AppendLine("}");

            return s.ToString();
        }

        // XXX: This all needs to be rewritten to use RequestProcessor etc.
        // Also need to add an export-to-JSON button in the mod GUI.
        static void Main(string[] args)
        {
            var logger = new LogWrapper();

            var resourceDb = JsonConvert.DeserializeObject<ResourceDefDatabase>(System.IO.File.ReadAllText("testdata/resources.json"));

            var frameState = JsonConvert.DeserializeObject<FrameState>(System.IO.File.ReadAllText("testdata/eight-boosters.json"));
            var vessel = frameState.Vessel;

            Console.WriteLine(ToGraphviz(vessel));

            var flowGraph = new BasicFlowGraph();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            flowGraph.FromVessel(vessel);
            stopwatch.Stop();
            Console.WriteLine($"FromVessel took {stopwatch.Elapsed.TotalMilliseconds:F3} msecs");
            Console.WriteLine(ToGraphviz(flowGraph));

            var guidToPart = new Dictionary<string, ushort>();
            for (int i = 0; i < flowGraph.Parts.Count; ++i)
            {
                guidToPart[flowGraph.Parts[i].Guid] = (ushort)i;
            }

            var resourceById = new Dictionary<ushort, ResourceDef>();
            foreach (var resource in resourceDb.Resources)
            {
                resourceById[resource.ResourceId] = resource;
            }

            var proc = new RequestProcessor(logger);
            var requests = new List<RequestProcessor.Request>();
            foreach (var req in frameState.Requests)
            {
                var request = new RequestProcessor.Request();

                request.Part = guidToPart[req.RequestTargetGuid];
                request.MinimalFraction = 0.1; // TODO: extract this from the game
                request.Active = req.Active;
                request.Priority = (float)req.FlowPriorityOffset;

                Console.WriteLine(JsonConvert.SerializeObject(req, Formatting.None));
                foreach (var cmd in req.Commands)
                {
                    Console.WriteLine("  " + JsonConvert.SerializeObject(cmd, Formatting.None));

                    var instr = new RequestProcessor.Instruction();
                    instr.IsInbound = (cmd.FlowDirection == ResourceFlowMod.Lib.FlowDirection.FLOW_INBOUND);

                    var resource = resourceById[cmd.FlowResource];
                    if (resource.IsRecipe)
                    {
                        foreach (var ingredient in resource.RecipeProperties.Ingredients)
                        {
                            instr.ResourceId = ingredient.ResourceId;
                            instr.Mode = resourceById[ingredient.ResourceId].ResourceProperties.FlowMode;

                            double ratio = ingredient.Units;
                            foreach (var over in cmd.IngredientOverrides)
                            {
                                if (over.name == resourceById[ingredient.ResourceId].Name)
                                {
                                    instr.Mode = over.flowMode;
                                    ratio = over.unitsPerRecipeUnit;
                                }
                            }

                            instr.Optimal = Math.Max(cmd.FlowUnits * ratio, 0.0);
                            instr.TargetUnits = cmd.TargetUnits * ratio;

                            request.Instructions.Add(instr);
                        }
                    }
                    else
                    {
                        instr.ResourceId = resource.ResourceId;
                        instr.Mode = (cmd.FlowModeOverride == ResourceFlowMod.Lib.ResourceFlowMode.NULL ? resource.ResourceProperties.FlowMode : cmd.FlowModeOverride);

                        instr.Optimal = Math.Max(cmd.FlowUnits, 0.0);
                        instr.TargetUnits = cmd.TargetUnits;

                        request.Instructions.Add(instr);
                    }
                }

                foreach (var instr in request.Instructions)
                {
                    Console.WriteLine("    " + JsonConvert.SerializeObject(instr, Formatting.None));
                }

                requests.Add(request);
            }

            var stopwatch2 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 50; ++i)
            {
                stopwatch = System.Diagnostics.Stopwatch.StartNew();
                proc.Process(flowGraph, requests, frameState.DeltaTime * 100.0);
                stopwatch.Stop();
                logger.LogInfo($"Processed in {stopwatch.Elapsed.TotalMilliseconds:F3} msecs");
            }
            stopwatch2.Stop();
        }
    }
}
