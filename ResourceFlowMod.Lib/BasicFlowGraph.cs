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

namespace ResourceFlowMod.Lib
{
    // TODO: Replace all the ushorts with type-safe wrappers
    // TODO: Maybe implement some arena allocator interface (and maybe simplify by using ints everywhere?)
    // or something, because this code is a little confusing and error-prone

    public class BasicFlowGraph
    {
        public struct Part
        {
            public string Guid; // TODO: just for debugging
            public string Name; // TODO: just for debugging
            public int Stage;
            public static int STAGE_UNDECIDED = int.MinValue;
            public static int STAGE_OPENLIST = STAGE_UNDECIDED + 1;

            // To represent the edges leading from this part, we store a pointer to a range in the
            // Edges array. This lets us cheaply iterate over all the edges from a part, while
            // minimising the number of memory allocations.
            public ushort NumEdges;
            public ushort EdgeOffset;

            public ushort Index; // component index
        }

        public struct Edge
        {
            public ushort From;
            public ushort To;
        }

        public struct Container
        {
            public ushort Part;
            public short Priority;
            public ushort ResourceID;
            public bool Dirty;
            public double CapacityUnits;
            public double StoredUnits;

            public double AvailableUnits
            {
                get => CapacityUnits - StoredUnits;
            }
        }

        class EdgeComparer : Comparer<Edge>
        {
            override public int Compare(Edge x, Edge y)
            {
                if (x.From < y.From)
                    return -1;
                if (x.From > y.From)
                    return 1;
                if (x.To < y.To)
                    return -1;
                if (x.To > y.To)
                    return 1;
                return 0;
            }
        }

        class PriorityComparer : Comparer<ushort>
        {
            List<Container> _containers;

            internal PriorityComparer(List<Container> containers)
            {
                _containers = containers;
            }

            override public int Compare(ushort x, ushort y)
            {
                return _containers[y].Priority.CompareTo(_containers[x].Priority);
            }
        }

        public struct StrongComponent
        {
            public ushort NumParts;
            public ushort PartOffset;

            public ushort NumContainers;
            public ushort ContainerOffset;

            public ushort NumScEdges;
            public ushort ScEdgeOffset;

            public ushort NumReachableContainers;
            public int ReachableContainerOffset; // use int because the size of these lists can be O(N^2)
            internal static int REACHABLE_CONTAINER_UNVISITED = -1;
        }

        public List<Part> Parts = new List<Part>();
        public List<ushort> Edges = new List<ushort>();
        public List<Container> Containers = new List<Container>();
        public List<StrongComponent> StrongComponents = new List<StrongComponent>();
        public List<ushort> StrongComponentParts = new List<ushort>();
        public List<ushort> StrongComponentContainers = new List<ushort>();
        public List<ushort> StrongComponentEdges = new List<ushort>();
        public List<ushort> StrongComponentReachableContainers = new List<ushort>();

        public void FromVessel(VesselGraph vessel)
        {
            Parts.Clear();
            Edges.Clear();
            Containers.Clear();
            StrongComponents.Clear();
            StrongComponentParts.Clear();
            StrongComponentContainers.Clear();
            StrongComponentEdges.Clear();
            StrongComponentReachableContainers.Clear();

            var rawEdges = new List<Edge>();

            for (int i = 0; i < vessel.Parts.Count; ++i)
            {
                var part = vessel.Parts[i];

                Parts.Add(new Part
                {
                    Guid = part.Guid,
                    Name = part.Name,
                    Stage = Part.STAGE_UNDECIDED,
                    NumEdges = 0,
                    EdgeOffset = 0,
                    Index = 0,
                });

                // Edges correspond to the path we can search to find a container. For fuel lines, that means
                // the edge goes in the opposite direction to the game's representation of the fuel line
                if (part.IsFuelLine && !part.IsFirstAnchor && part.OtherEndAnchor != -1)
                {
                    rawEdges.Add(new Edge
                    {
                        From = (ushort)i,
                        To = (ushort)part.OtherEndAnchor,
                    });
                }
            }

            BuildStaging(vessel, Parts);

            foreach (var edge in vessel.Attachments)
            {
                if (!edge.AllowCrossfeed)
                    continue;

                var from = vessel.Parts[edge.From];
                var to = vessel.Parts[edge.To];

                if (!from.FuelCrossfeed || !to.FuelCrossfeed)
                    continue;

                rawEdges.Add(new Edge
                {
                    From = (ushort)edge.From,
                    To = (ushort)edge.To,
                });

                rawEdges.Add(new Edge
                {
                    From = (ushort)edge.To,
                    To = (ushort)edge.From,
                });
            }

            // Convert to a sorted, unique list (not hugely efficiently)
            rawEdges = new List<Edge>(new SortedSet<Edge>(rawEdges, new EdgeComparer()));

            // Set up the Part pointers into the Edge list
            int lastFrom = -1;
            for (int i = 0; i < rawEdges.Count; ++i)
            {
                var from = rawEdges[i].From;
                var part = Parts[from];
                if (from != lastFrom)
                {
                    part.EdgeOffset = (ushort)i;
                    lastFrom = from;
                }
                part.NumEdges++;
                Parts[from] = part;
                Edges.Add(rawEdges[i].To);
            }

            BuildStrongComponents(rawEdges);

            BuildContainers(vessel);
        }

        void BuildStaging(VesselGraph vessel, List<Part> parts)
        {
            // To compute staging:
            // * The root part is stage 0.
            // * Depth-first search from there, ignoring fuel lines (i.e. physical attachments only).
            // * When the search passes a decoupler, apply the decoupler's ActivationStage to everything found after that.
            // We assume the structure is fully connected and essentially acyclic (apart from the bidirectional edges
            // between attach parts), so DFS will find every part in an appropriate order.

            // TODO: optimise this
            var attachments = new Dictionary<int, List<int>>();
            foreach (var edge in vessel.Attachments)
            {
                // TODO: Is this the right edge direction? (If we do both then struts connect unrelated
                // stages. Does this break with any radial attachments?)
                if (!attachments.TryGetValue(edge.To, out List<int> entry))
                {
                    entry = new List<int>();
                    attachments[edge.To] = entry;
                }
                entry.Add(edge.From);
            }

            var open = new Stack<(int stage, int part)>();
            open.Push((0, vessel.RootPart));

            while (open.Count > 0)
            {
                var (stage, part) = open.Pop();

                if (vessel.Parts[part].IsDecoupler)
                    stage = vessel.Parts[part].ActivationStage;

                var p = parts[part];
                p.Stage = stage;
                parts[part] = p;

                if (attachments.TryGetValue(part, out List<int> entry))
                {
                    foreach (var attached in entry)
                    {
                        p = parts[attached];
                        if (p.Stage == Part.STAGE_UNDECIDED)
                        {
                            p.Stage = Part.STAGE_OPENLIST;
                            parts[attached] = p;
                            open.Push((stage, attached));
                        }
                    }
                }
            }
        }

        void BuildContainers(VesselGraph vessel)
        {
            // For each component, construct a list of containers

            var componentContainers = new SortedSet<(ushort Component, ushort Container)>();
            for (int i = 0; i < vessel.Resources.Count; ++i)
            {
                var resource = vessel.Resources[i];
                Containers.Add(new Container
                {
                    Part = (ushort)resource.Node,
                    Priority = (short)(Parts[resource.Node].Stage * 10), // TODO: Add some user-controlled priority offset
                    ResourceID = resource.ResourceID,
                    CapacityUnits = resource.CapacityUnits,
                    StoredUnits = resource.StoredUnits,
                });
                componentContainers.Add((Parts[vessel.Resources[i].Node].Index, (ushort)i));
            }

            int lastComponent = -1;
            foreach (var (component, container) in componentContainers)
            {
                var sc = StrongComponents[component];
                if (component != lastComponent)
                {
                    sc.ContainerOffset = (ushort)StrongComponentContainers.Count;
                    lastComponent = component;
                }
                sc.NumContainers++;
                StrongComponents[component] = sc;
                StrongComponentContainers.Add(container);
            }
            // TODO: should sort each list by priority, so subsequent sorting can be a cheap merge

            // Now find all containers reachable from each component:
            // * If a component is a leaf, it can only reach its own.
            // * Otherwise recurse into its children, then merge the childrens' reachable lists.
            // Note this might use O(N^2) memory, so we use int instead of ushort.

            // TODO: We could split this by ResourceID?

            for (int c = 0; c < StrongComponents.Count; ++c)
            {
                if (StrongComponents[c].ReachableContainerOffset == StrongComponent.REACHABLE_CONTAINER_UNVISITED)
                {
                    ReachableContainersSearch(c);
                }
            }
        }

        void ReachableContainersSearch(int c)
        {
            var component = StrongComponents[c];

            // Process all the children. (This is a DAG so no risk of cycles)
            for (int i = 0; i < component.NumScEdges; ++i)
            {
                ReachableContainersSearch(StrongComponentEdges[component.ScEdgeOffset + i]);
            }

            int start = StrongComponentReachableContainers.Count;

            // Copy own containers
            for (int j = 0; j < component.NumContainers; ++j)
            {
                StrongComponentReachableContainers.Add(StrongComponentContainers[component.ContainerOffset + j]);
            }

            // Copy each child's reachable containers
            for (int i = 0; i < component.NumScEdges; ++i)
            {
                var child = StrongComponents[StrongComponentEdges[component.ScEdgeOffset + i]];
                for (int j = 0; j < child.NumReachableContainers; ++j)
                {
                    StrongComponentReachableContainers.Add(StrongComponentContainers[child.ContainerOffset + j]);
                }
            }

            int end = StrongComponentReachableContainers.Count;

            // Sort by decreasing priority
            StrongComponentReachableContainers.Sort(start, end - start, new PriorityComparer(Containers));

            component.ReachableContainerOffset = start;
            component.NumReachableContainers = (ushort)(end - start);
            StrongComponents[c] = component;
        }

        void BuildStrongComponents(List<Edge> rawEdges)
        {
            // Compute the SCCs
            StrongComponentAlgorithm();

            // Now that we have the SCCs, we want to generate a new set of edges between SCCs.
            // This gives a DAG of SCCs, where each SCC contains a list of parts.

            var scEdges = new SortedSet<Edge>(new EdgeComparer());
            for (int i = 0; i < rawEdges.Count; ++i)
            {
                var from = Parts[rawEdges[i].From];
                var to = Parts[rawEdges[i].To];
                if (from.Index != to.Index)
                {
                    scEdges.Add(new Edge { From = from.Index, To = to.Index });
                }
            }

            // Set up the StrongComponent pointers into the StrongComponentEdge list
            int lastFrom = -1;
            foreach (var edge in scEdges)
            {
                var sc = StrongComponents[edge.From];
                if (edge.From != lastFrom)
                {
                    sc.ScEdgeOffset = (ushort)StrongComponentEdges.Count;
                    lastFrom = edge.From;
                }
                sc.NumScEdges++;
                StrongComponents[edge.From] = sc;
                StrongComponentEdges.Add(edge.To);
            }
            // (TODO: Implement this more elegantly)
        }


        // Strongly connected components, based on Gabow (Path-based depth-first search for strong
        // and biconnected components).
        //
        // (This seems a little nicer than the Sedgewick implementation on
        // https://en.wikipedia.org/wiki/Path-based_strong_component_algorithm
        // because Sedgewick stores both preorder and component number for each vertex, while Gabow
        // stores a single index that represents both.)
        //
        // We use Part.Index as temporary storage for I[v] during the algorithm, then adjust it at
        // the end to give the 0-based component index.

        class SccState
        {
            internal ushort C;
            internal Stack<ushort> S = new Stack<ushort>();
            internal Stack<ushort> B = new Stack<ushort>();
        };

        void StrongComponentAlgorithm()
        {
            var state = new SccState();

            int N = Parts.Count;
            state.C = (ushort)N;

            for (int i = 0; i < N; ++i)
            {
                if (Parts[i].Index == 0)
                {
                    StrongComponentSearch(state, (ushort)i);
                }
            }

            // Offset all the component numbers so they start from 0
            for (int i = 0; i < N; ++i)
            {
                var part = Parts[i];
                part.Index = (ushort)(part.Index - N - 1);
                Parts[i] = part;
            }
        }

        void StrongComponentSearch(SccState state, ushort v)
        {
            var S = state.S;
            var B = state.B;

            S.Push(v);
            var Iv = (ushort)S.Count;
            B.Push(Iv);

            var part = Parts[v];
            part.Index = Iv;
            Parts[v] = part;

            for (var i = 0; i < part.NumEdges; ++i)
            {
                var w = Edges[part.EdgeOffset + i];
                var wPart = Parts[w];

                if (wPart.Index == 0)
                {
                    StrongComponentSearch(state, w);
                }
                else
                {
                    while (wPart.Index < B.Peek())
                    {
                        B.Pop();
                    }
                }
            }

            if (Iv == B.Peek())
            {
                B.Pop();
                state.C++;

                int numParts = 0;
                int offset = StrongComponentParts.Count;

                var component = new List<ushort>();
                while (Iv <= S.Count)
                {
                    var u = S.Pop();

                    var uPart = Parts[u];
                    uPart.Index = state.C;
                    Parts[u] = uPart;

                    numParts += 1;
                    StrongComponentParts.Add(u);
                }

                StrongComponents.Add(new StrongComponent
                {
                    NumParts = (ushort)numParts,
                    PartOffset = (ushort)offset,
                    NumContainers = 0,
                    ContainerOffset = 0,
                    NumScEdges = 0,
                    ScEdgeOffset = 0,
                    NumReachableContainers = 0,
                    ReachableContainerOffset = StrongComponent.REACHABLE_CONTAINER_UNVISITED,
                });
            }
        }
    }
}
