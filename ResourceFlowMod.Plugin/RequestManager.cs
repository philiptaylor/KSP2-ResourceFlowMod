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

using System.Collections.Generic;
using System.Linq;

using HarmonyLib;
using KSP.Sim.ResourceSystem;

using ResourceFlowMod.Lib;

namespace ResourceFlowMod.Plugin
{
    class RequestManager
    {
        static AccessTools.FieldRef<ResourceFlowRequestManager, FlowGraph> _flowGraph =
            AccessTools.FieldRefAccess<ResourceFlowRequestManager, FlowGraph>("_flowGraph");

        ResourceFlowRequestManager _oldManager;

        Dictionary<ResourceDefinitionID, ResourceDefinitionData> _resourceById = new Dictionary<ResourceDefinitionID, ResourceDefinitionData>();

        internal struct RequestWrapper
        {
            internal IFlowNode Node;
            internal RequestProcessor.Request Request;
        }

        RequestProcessor _requestProcessor = new RequestProcessor(Plugin.Log);

        internal Dictionary<ResourceFlowRequestHandle, RequestWrapper> _requests = new Dictionary<ResourceFlowRequestHandle, RequestWrapper>();

        internal RequestManager(ResourceFlowRequestManager oldManager, ResourceDefinitionDatabase resourceDefinitionDatabase)
        {
            _oldManager = oldManager;

            if (!resourceDefinitionDatabase.IsDefinitionDataFrozen)
            {
                Plugin.Log.LogWarning("Resource DB not frozen");
            }

            foreach (var resourceId in resourceDefinitionDatabase.GetAllResourceIDs())
            {
                _resourceById[resourceId] = resourceDefinitionDatabase.GetDefinitionData(resourceId);
            }
        }

        internal void OnAllocateRequest(ResourceFlowRequestHandle handle, IFlowNode node)
        {
            _requests[handle] = new RequestWrapper
            {
                Node = node,
                Request = new RequestProcessor.Request(),
            };
        }

        internal void OnRemoveRequest(ResourceFlowRequestHandle handle)
        {
            _requests.Remove(handle);
        }

        internal void MoveRequests(RequestManager destination, IFlowNode node)
        {
            // Not sure from the docs if Remove() invalidates iterators, so for safety make a copy of the keys
            var keys = _requests.Keys.ToList();

            foreach (var key in keys)
            {
                var value = _requests[key];
                if (node == null || node == value.Node)
                {
                    _requests.Remove(key);
                    destination._requests.Add(key, value);
                }
            }
        }

        internal bool HasRequest(ResourceFlowRequestHandle handle)
        {
            return _requests.ContainsKey(handle);
        }

        internal bool IsRequestActive(ResourceFlowRequestHandle handle)
        {
            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("IsRequestActive on unknown handle");
                return false;
            }

            return wrapper.Request.Active;
        }

        internal bool SetRequestActive(ResourceFlowRequestHandle handle, bool active)
        {
            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("SetRequestActive on unknown handle");
                return false;
            }

            if (active && wrapper.Request.Instructions.Count == 0)
            {
                // To match the game, refuse to make active if no instructions
                wrapper.Request.Active = false;
                return false;
            }
            else
            {
                wrapper.Request.Active = active;
                return true;
            }
        }

        internal void SetCommands(ResourceFlowRequestHandle handle, IEnumerable<ResourceFlowRequestCommandConfig> commands, double normalizedFlowMinimum, double flowPriorityOffset)
        {
            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("SetCommands on unknown handle");
                return;
            }

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));

            var request = wrapper.Request;
            request.Instructions.Clear();

            request.Part = ushort.MaxValue; // set to the proper value by RecachePartIds
            request.MinimalFraction = normalizedFlowMinimum;
            request.Priority = (float)flowPriorityOffset;

            foreach (var cmd in commands)
            {
                var instr = new RequestProcessor.Instruction();
                instr.IsInbound = (cmd.FlowDirection == KSP.Sim.ResourceSystem.FlowDirection.FLOW_INBOUND);

                var resource = _resourceById[cmd.FlowResource];
                if (resource.IsRecipe)
                {
                    foreach (var ingredient in resource.recipeProperties.ingredients)
                    {
                        instr.ResourceId = ingredient.resourceID.Value;
                        instr.Mode = Plugin.ConvertFlowMode(_resourceById[ingredient.resourceID].resourceProperties.flowMode);

                        double ratio = ingredient.units;
                        foreach (var over in cmd.IngredientOverrides)
                        {
                            if (over.name == _resourceById[ingredient.resourceID].name)
                            {
                                // KSP2 BUG: it ignores the override if it's STAGE_STACK_FLOW_BALANCE (why?),
                                // which means MethaneAir engines ignore crossfeed when consuming methane
                                instr.Mode = Plugin.ConvertFlowMode(over.flowMode);
                                ratio = over.unitsPerRecipeUnit;
                            }
                        }

                        instr.Optimal = System.Math.Max(cmd.FlowUnits * ratio, 0.0);
                        instr.TargetUnits = cmd.TargetUnits * ratio;

                        request.Instructions.Add(instr);
                    }
                }
                else
                {
                    instr.ResourceId = resource.resourceDatabaseID.Value;
                    instr.Mode = Plugin.ConvertFlowMode(cmd.FlowModeOverride == KSP.Sim.ResourceSystem.ResourceFlowMode.NULL ? resource.resourceProperties.flowMode : cmd.FlowModeOverride);

                    instr.Optimal = System.Math.Max(cmd.FlowUnits, 0.0);
                    instr.TargetUnits = cmd.TargetUnits;

                    request.Instructions.Add(instr);
                }
            }
        }

        internal bool RequestHasInstructions(ResourceFlowRequestHandle handle)
        {
            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("RequestHasInstructions on unknown handle");
                return false;
            }

            return wrapper.Request.Instructions.Count > 0;
        }

        internal RequestProcessor.Request GetRequest(ResourceFlowRequestHandle handle)
        {
            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("GetRequest on unknown handle");
                return null;
            }

            return wrapper.Request;
        }

        internal void GetRequestResources(ResourceFlowRequestHandle handle, Dictionary<ushort, RequestProcessor.RequestResources> resources, bool inbound)
        {
            resources.Clear();

            if (!RecachePartIds())
                return;

            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("GetRequestResources on unknown handle");
                return;
            }

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));
            _requestProcessor.GetReachableResources(extra.BasicFlowGraph, wrapper.Request, resources, inbound);
        }

        internal RequestProcessor.RequestResources GetInstructionResources(RequestProcessor.Request request, RequestProcessor.Instruction instruction)
        {
            if (!RecachePartIds())
                return new RequestProcessor.RequestResources();

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));
            return _requestProcessor.GetReachableResources(extra.BasicFlowGraph, request, instruction);
        }

        internal IEnumerable<(IFlowNode, short)> GetRequestContainers(ResourceFlowRequestHandle handle)
        {
            if (!RecachePartIds())
                yield break;

            if (!_requests.TryGetValue(handle, out RequestWrapper wrapper))
            {
                Plugin.Log.LogError("GetRequestResources on unknown handle");
                yield break;
            }

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));
            foreach (var c in _requestProcessor.GetReachableContainers(extra.BasicFlowGraph, wrapper.Request))
            {
                var container = extra.BasicFlowGraph.Containers[c];
                yield return (extra.VesselState.PartMapping[container.Part], container.Priority);
            }
        }

        // Copy dynamic state from IResourceContainers into BasicFlowGraph
        internal void ReadContainers()
        {
            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));
            System.Diagnostics.Debug.Assert(extra.VesselState.ContainerMapping.Count == extra.BasicFlowGraph.Containers.Count);
            for (int i = 0; i < extra.VesselState.ContainerMapping.Count; ++i)
            {
                var containerObj = extra.VesselState.ContainerMapping[i];

                var container = extra.BasicFlowGraph.Containers[i];
                container.StoredUnits = containerObj.GetResourceStoredUnits(new ResourceDefinitionID(container.ResourceID));
                container.Dirty = false;
                extra.BasicFlowGraph.Containers[i] = container;
            }
        }

        internal void WriteContainers(out int numChanged)
        {
            numChanged = 0;

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));

            System.Diagnostics.Debug.Assert(extra.VesselState.ContainerMapping.Count == extra.BasicFlowGraph.Containers.Count);

            for (int i = 0; i < extra.VesselState.ContainerMapping.Count; ++i)
            {
                var container = extra.BasicFlowGraph.Containers[i];
                if (container.Dirty)
                {
                    var containerObj = extra.VesselState.ContainerMapping[i];
                    containerObj.SetResourceStoredUnits(new ResourceDefinitionID(container.ResourceID), container.StoredUnits);
                    numChanged += 1;
                }
            }
        }

        internal void UpdateFlowRequests(double tickDeltaTime)
        {
            if (!RecachePartIds())
                return;

            var extra = Plugin.GetFlowGraphExtra(_flowGraph(_oldManager));
            _requestProcessor.Process(extra.BasicFlowGraph, _requests.Values.Select(x => x.Request), tickDeltaTime);
        }

        // XXX: This is kind of ugly, and it's wastefully called every frame
        private bool RecachePartIds()
        {
            var flowGraph = _flowGraph(_oldManager);
            if (flowGraph.FlowGraphIsDirty)
            {
                Plugin.Log.LogWarning("UpdatePartIds called with dirty graph");
                return false;
            }

            var extra = Plugin.GetFlowGraphExtra(flowGraph);

            foreach (var request in _requests)
            {
                if (!extra.VesselState.PartMappingRev.TryGetValue(request.Value.Node, out request.Value.Request.Part))
                {
                    Plugin.Log.LogWarning($"Request has unknown part ({request.Value.Node.Name} {request.Value.Node.GlobalId})");
                    continue;
                }
            }

            return true;
        }
    }
}