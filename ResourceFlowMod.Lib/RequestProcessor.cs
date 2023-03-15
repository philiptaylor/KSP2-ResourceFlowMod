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

namespace ResourceFlowMod.Lib
{
    class ContainerList
    {
        ILogger _logger;
        List<ushort> _containers = new List<ushort>();
        BasicFlowGraph _graph;

        internal BasicFlowGraph Graph
        {
            set
            {
                _graph = value;
            }
        }

        internal ContainerList(ILogger logger)
        {
            _logger = logger;
        }

        internal void CopyReachable(ushort part, ushort resourceId, ResourceFlowMode mode)
        {
            _containers.Clear();

            // TODO: Maybe should separate by resourceId earlier, so we don't need to filter here?

            // TODO: Dunno if these modes have the correct semantics
            switch (mode)
            {
                case ResourceFlowMode.ALL_VESSEL:
                case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                    {
                        for (int c = 0; c < _graph.Containers.Count; ++c)
                        {
                            if (_graph.Containers[c].ResourceID == resourceId)
                            {
                                _containers.Add((ushort)c);
                            }
                        }
                        break;
                    }
                case ResourceFlowMode.STACK_PRIORITY_SEARCH:
                case ResourceFlowMode.STAGE_STACK_FLOW_BALANCE:
                    {
                        var sc = _graph.StrongComponents[_graph.Parts[part].Index];
                        for (int i = 0; i < sc.NumReachableContainers; ++i)
                        {
                            var c = _graph.StrongComponentReachableContainers[sc.ReachableContainerOffset + i];
                            if (_graph.Containers[c].ResourceID == resourceId)
                            {
                                _containers.Add(c);
                            }
                        }
                        break;
                    }
                case ResourceFlowMode.NO_FLOW:
                    {
                        // TODO: Should store containers per part somewhere, so we don't have to filter the SC list stupidly like this
                        var sc = _graph.StrongComponents[_graph.Parts[part].Index];
                        for (int i = 0; i < sc.NumReachableContainers; ++i)
                        {
                            var c = _graph.StrongComponentReachableContainers[sc.ReachableContainerOffset + i];
                            if (_graph.Containers[c].ResourceID == resourceId && _graph.Containers[c].Part == part)
                            {
                                _containers.Add(c);
                            }
                        }
                        break;
                    }
                default:
                    {
                        _logger.LogWarning("Invalid FlowMode");
                        break;
                    }
            }
        }

        // Find the highest-priority group of containers where at least one can satisfy an outbound request
        // for the given resource. Compute the sum of StoredUnits, then return the group.
        // This may return some (not necessarily all) of the containers with StoredUnits == 0.
        internal IEnumerable<ushort> MaxPriorityOutbound(out double total)
        {
            // _containers is already sorted by priority, so we just need to find the first
            // element with StoredUnits>0 then return everything up until the next element
            // with a different priority.

            for (int i = 0; i < _containers.Count; ++i)
            {
                var container = _graph.Containers[_containers[i]];
                if (container.StoredUnits > 0.0)
                {
                    var priority = container.Priority;

                    total = container.StoredUnits;

                    // Any containers before this might be the same priority, but StoredUnits=0 so we can ignore them.

                    // Find the end of this priority group
                    int start = i++;
                    for (; i < _containers.Count; ++i)
                    {
                        container = _graph.Containers[_containers[i]];
                        if (container.Priority != priority)
                            break;
                        total += container.StoredUnits;
                    }
                    int end = i;

                    return EnumerateContainers(start, end);
                }
            }

            total = 0.0;
            return EnumerateContainers(0, 0);
        }

        // Find the highest-priority group of containers where at least one can satisfy an inbound request
        // for the given resource. Compute the sum of AvailableUnits, then return the group.
        // This may return some (not necessarily all) of the containers with AvailableUnits == 0.
        internal IEnumerable<ushort> MaxPriorityInbound(out double total)
        {
            // _containers is already sorted by priority, so we just need to find the first
            // element with StoredUnits>0 then return everything up until the next element
            // with a different priority.

            for (int i = 0; i < _containers.Count; ++i)
            {
                var container = _graph.Containers[_containers[i]];
                if (container.AvailableUnits > 0.0)
                {
                    var priority = container.Priority;

                    total = container.AvailableUnits;

                    // Any containers before this might be the same priority, but AvailableUnits=0 so we can ignore them.

                    // Find the end of this priority group
                    int start = i++;
                    for (; i < _containers.Count; ++i)
                    {
                        container = _graph.Containers[_containers[i]];
                        if (container.Priority != priority)
                            break;
                        total += container.AvailableUnits;
                    }
                    int end = i;

                    return EnumerateContainers(start, end);
                }
            }

            total = 0.0;
            return EnumerateContainers(0, 0);
        }

        private IEnumerable<ushort> EnumerateContainers(int start, int end)
        {
            for (int i = start; i < end; ++i)
            {
                yield return _containers[i];
            }
        }

        public List<ushort>.Enumerator GetEnumerator()
        {
            return _containers.GetEnumerator();
        }
    }

    class ContainerLists
    {
        ILogger _logger;
        List<ContainerList> _lists = new List<ContainerList>();

        internal ContainerLists(ILogger logger)
        {
            _logger = logger;
        }

        internal void Reset(int count, BasicFlowGraph graph)
        {
            while (_lists.Count < count)
                _lists.Add(new ContainerList(_logger));

            for (int i = 0; i < count; ++i)
                _lists[i].Graph = graph;
        }

        internal ContainerList this[int index] => _lists[index];
    }

    public class RequestProcessor
    {
        ILogger _logger;

        public RequestProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public class Request
        {
            public ushort Part;
            public double MinimalFraction;
            public bool Active;
            public float Priority; // TODO: implement this

            public List<Instruction> Instructions = new List<Instruction>();

            public bool WasLastTickDeliveryAccepted;
            public double LastTickDeliveryNormalized;
            public ushort ResourceNotProcessed; // the game stores a whole list, but only displays the first one, so don't bother storing the rest
            // Skip the other FlowRequestResolutionState fields, they seem pointless
        }

        public struct Instruction
        {
            public ushort ResourceId;
            public bool IsInbound;
            public ResourceFlowMode Mode;
            public double Optimal; // units per second
            public double TargetUnits; // total amount to transfer (over multiple frames) before stopping; used by Resource Manager UI? (TODO: seems weird)
        }

        public void Process(BasicFlowGraph graph, IEnumerable<Request> requests, double deltaTime)
        {
            var containerLists = new ContainerLists(_logger);

            foreach (var request in requests)
            {
                // Set the default return values for a failed request
                request.ResourceNotProcessed = 0;
                request.WasLastTickDeliveryAccepted = false;
                request.LastTickDeliveryNormalized = 0.0;

                // Skip inactive requests
                if (!request.Active)
                {
                    continue;
                }

                // Find all the reachable containers for each instruction
                containerLists.Reset(request.Instructions.Count, graph);
                for (int i = 0; i < request.Instructions.Count; ++i)
                {
                    var instruction = request.Instructions[i];
                    containerLists[i].CopyReachable(request.Part, instruction.ResourceId, instruction.Mode);
                }

                double requestSatisfiable = 1.0;

                // Compute the total reachable stored/available amount for each instruction,
                // and lower requestSatisfiable to represent the fraction of the request that can be completed
                //
                // XXX: this assumes each ResourceId is unique within a request - need to verify and/or enforce that
                for (int i = 0; i < request.Instructions.Count; ++i)
                {
                    var instruction = request.Instructions[i];
                    var containers = containerLists[i];

                    double optimalUnits = instruction.Optimal * deltaTime;
                    double totalUsable = 0.0;

                    foreach (var c in containers)
                    {
                        if (instruction.IsInbound)
                        {
                            totalUsable += graph.Containers[c].AvailableUnits;
                        }
                        else
                        {
                            totalUsable += graph.Containers[c].StoredUnits;
                        }

                        // Early exit once we've found enough
                        if (totalUsable >= optimalUnits)
                        {
                            break;
                        }
                    }

                    if (optimalUnits > 0.0) // prevent divide-by-zero
                    {
                        var instructionSatisfiable = totalUsable / optimalUnits;
                        if (instructionSatisfiable < 1.0)
                        {
                            request.ResourceNotProcessed = instruction.ResourceId;
                        }

                        requestSatisfiable = Math.Min(requestSatisfiable, instructionSatisfiable);
                    }
                }

                // If we can't meet the min constraint, then fail this request
                if (requestSatisfiable < request.MinimalFraction)
                {
                    continue;
                }

                // Request succeeded
                request.WasLastTickDeliveryAccepted = true;
                request.LastTickDeliveryNormalized = requestSatisfiable;

                // Perform each instruction
                for (int i = 0; i < request.Instructions.Count; ++i)
                {
                    var instruction = request.Instructions[i];
                    var containers = containerLists[i];

                    double remaining = instruction.Optimal * deltaTime * requestSatisfiable;

                    // TODO: Handle instruction.TargetUnits

                    while (true)
                    {
                        double groupUsable;
                        var priorityGroup = instruction.IsInbound ?
                            containers.MaxPriorityInbound(out groupUsable) :
                            containers.MaxPriorityOutbound(out groupUsable);

                        if (groupUsable == 0.0)
                        {
                            // No more usable containers left. Probably we've completed the request successfully and the
                            // discrepancy is just from rounding; warn if that's not the case
                            if (Math.Abs(remaining) > 1e-6) // TODO: smarter tolerances
                            {
                                _logger.LogWarning($"Failed to complete request ({remaining})");
                            }
                            break;
                        }

                        if (groupUsable >= remaining)
                        {
                            // This priority group has enough usable (available/stored) to complete the request.
                            // Balance the remaining amount between all containers, in proportion to their usable amount
                            var perUsable = remaining / groupUsable;

                            foreach (var c in priorityGroup)
                            {
                                var container = graph.Containers[c];

                                if (instruction.IsInbound)
                                {
                                    container.StoredUnits += perUsable * container.AvailableUnits;
                                }
                                else
                                {
                                    container.StoredUnits -= perUsable * container.StoredUnits;
                                }
                                container.Dirty = true;

                                graph.Containers[c] = container;
                            }

                            // Completed this request
                            remaining = 0.0;
                            break;
                        }
                        else
                        {
                            // This priority group has insufficient capacity to complete the request.
                            // Do as much as possible by filling/emptying all containers

                            foreach (var c in priorityGroup)
                            {
                                var container = graph.Containers[c];

                                if (instruction.IsInbound)
                                {
                                    container.StoredUnits = container.CapacityUnits;
                                }
                                else
                                {
                                    container.StoredUnits = 0.0;
                                }
                                container.Dirty = true;

                                graph.Containers[c] = container;
                            }

                            // Loop around to process the remainder in the next priority group
                            remaining -= groupUsable;
                        }
                    }
                }
            }
        }

        public struct RequestResources
        {
            public double CapacityUnits;
            public double StoredUnits;
        }

        public void GetReachableResources(BasicFlowGraph graph, Request request, Dictionary<ushort, RequestResources> resources, bool inbound)
        {
            var containerList = new ContainerList(_logger);
            containerList.Graph = graph;

            foreach (var instruction in request.Instructions)
            {
                if (instruction.IsInbound != inbound)
                {
                    continue;
                }

                if (!resources.TryGetValue(instruction.ResourceId, out var res))
                {
                    res = new RequestResources();
                    resources.Add(instruction.ResourceId, res);
                }

                containerList.CopyReachable(request.Part, instruction.ResourceId, instruction.Mode);
                foreach (var container in containerList)
                {
                    var c = graph.Containers[container];
                    res.CapacityUnits += c.CapacityUnits;
                    res.StoredUnits += c.StoredUnits;
                }
            }
        }

        // Used for debug UI
        public RequestResources GetReachableResources(BasicFlowGraph graph, Request request, Instruction instruction)
        {
            var containerList = new ContainerList(_logger);
            containerList.Graph = graph;

            var res = new RequestResources();

            containerList.CopyReachable(request.Part, instruction.ResourceId, instruction.Mode);
            foreach (var container in containerList)
            {
                var c = graph.Containers[container];
                res.CapacityUnits += c.CapacityUnits;
                res.StoredUnits += c.StoredUnits;
            }

            return res;
        }

        public IEnumerable<ushort> GetReachableContainers(BasicFlowGraph graph, Request request)
        {
            var containerList = new ContainerList(_logger);
            containerList.Graph = graph;

            foreach (var instruction in request.Instructions)
            {
                containerList.CopyReachable(request.Part, instruction.ResourceId, instruction.Mode);
                foreach (var container in containerList)
                {
                    yield return container;
                }
            }
        }
    }
}