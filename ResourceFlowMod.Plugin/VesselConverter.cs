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

using KSP.OAB;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;

using ResourceFlowMod.Lib;

namespace ResourceFlowMod.Plugin
{
    class VesselState
    {
        internal VesselGraph Graph = new VesselGraph();
        internal List<IFlowNode> PartMapping = new List<IFlowNode>();
        internal List<IResourceContainer> ContainerMapping = new List<IResourceContainer>();
        internal Dictionary<IFlowNode, ushort> PartMappingRev = new Dictionary<IFlowNode, ushort>();
    }

    class VesselConverter
    {
        public static VesselState BuildVesselGraph(PartOwnerComponent partOwner)
        {
            var state = new VesselState();

            // First pass: Assign numeric IDs to each part
            var nodeIds = new Dictionary<IFlowNode, int>();
            int nodeId = 0;
            foreach (PartComponent partComponent in partOwner.Parts)
            {
                // TODO: what does this mean?
                if (partComponent.isDecimated)
                {
                    Plugin.Log.LogInfo($"Skipping decimated part ({partComponent.Name})");
                    continue;
                }

                nodeIds[partComponent] = nodeId;
                state.PartMapping.Add(partComponent);
                state.PartMappingRev[partComponent] = (ushort)nodeId;

                nodeId++;
            }

            state.Graph.RootPart = nodeIds[partOwner.RootPart];

            // Second pass: Copy part and attachment data
            nodeId = 0;
            foreach (PartComponent partComponent in partOwner.Parts)
            {
                if (partComponent.isDecimated)
                {
                    continue;
                }

                KSP.Modules.Data_CompoundPart compoundPart = null;
                if (partComponent.PartData.isCompound)
                {
                    partComponent.TryGetModuleData<PartComponentModule_FuelLine, KSP.Modules.Data_CompoundPart>(out compoundPart);
                }

                var node = new VesselGraph.Node
                {
                    Guid = partComponent.GlobalId.ToString(),
                    Name = partComponent.Name,
                    IsDecoupler = (partComponent.PartData.stageType == AssemblyPartStageType.DecouplerHorizontal || partComponent.PartData.stageType == AssemblyPartStageType.DecouplerVertical),
                    ActivationStage = partComponent.ActivationStage,
                    FuelCrossfeed = partComponent.FuelCrossfeed,
                    OtherEndAnchor = -1,
                };
                if (compoundPart != null)
                {
                    node.IsFuelLine = true;
                    node.IsFirstAnchor = compoundPart.IsFirstAnchor;
                    if (compoundPart.OtherEndAnchor != IGGuid.Empty)
                    {
                        PartComponent otherEndAnchor;
                        int otherEndAnchorId;
                        if (!partOwner.TryGetPart(compoundPart.OtherEndAnchor, out otherEndAnchor))
                        {
                            Plugin.Log.LogWarning("Cannot get OtherEndAnchor");
                        }
                        else if (!nodeIds.TryGetValue(otherEndAnchor, out otherEndAnchorId))
                        {
                            Plugin.Log.LogWarning("OtherEndAnchor is not in parts list");
                        }
                        else
                        {
                            node.OtherEndAnchor = otherEndAnchorId;
                        }
                    }
                }
                state.Graph.Parts.Add(node);

                foreach (var attachNodeData in partComponent.GetAttachments())
                {
                    if (attachNodeData.AttachedPart == null)
                    {
                        continue;
                    }

                    int attachedNodeId;
                    if (!nodeIds.TryGetValue(attachNodeData.AttachedPart, out attachedNodeId))
                    {
                        Plugin.Log.LogWarning("AttachedPart is not in parts list");
                        continue;
                    }

                    state.Graph.Attachments.Add(new VesselGraph.Edge
                    {
                        From = nodeId,
                        To = attachedNodeId,
                        AllowCrossfeed = attachNodeData.AllowCrossfeed,
                    });
                }

                if (partComponent.Containers != null)
                {
                    foreach (IResourceContainer container in partComponent.Containers)
                    {
                        if (container is ResourceContainer resourceContainer)
                        {
                            foreach (var resource in resourceContainer.GetAllResourcesContainedData())
                            {
                                state.Graph.Resources.Add(new VesselGraph.Resource
                                {
                                    Node = nodeId,
                                    ResourceID = resource.ResourceID.Value,
                                    StoredUnits = resource.StoredUnits,
                                    CapacityUnits = resource.CapacityUnits,
                                    NonStageable = resource.NonStageable,
                                });
                                state.ContainerMapping.Add(container);
                            }
                        }
                    }
                }

                nodeId++;
            }

            return state;
        }
    }
}