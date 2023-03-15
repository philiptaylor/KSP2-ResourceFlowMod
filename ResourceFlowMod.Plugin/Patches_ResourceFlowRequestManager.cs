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
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;

using HarmonyLib;
using KSP.Sim.ResourceSystem;

using ResourceFlowMod.Lib;

namespace ResourceFlowMod.Plugin
{
    [HarmonyPatch(typeof(ResourceFlowRequestManager))]
    class Patches_ResourceFlowRequestManager
    {
        static AccessTools.FieldRef<ResourceFlowRequestManager, DictionaryValueList<ResourceFlowRequestHandle, ResourceFlowRequestManager.ManagedRequestWrapper>> _requestWrappers =
            AccessTools.FieldRefAccess<ResourceFlowRequestManager, DictionaryValueList<ResourceFlowRequestHandle, ResourceFlowRequestManager.ManagedRequestWrapper>>("_requestWrappers");

        static AccessTools.FieldRef<ResourceFlowRequestManager, FlowGraph> _flowGraph =
            AccessTools.FieldRefAccess<ResourceFlowRequestManager, FlowGraph>("_flowGraph");

        static AccessTools.FieldRef<ResourceFlowRequestManager, DictionaryValueList<IFlowNode, ResourceFlowPriorityQuerySolver>> _solvers =
            AccessTools.FieldRefAccess<ResourceFlowRequestManager, DictionaryValueList<IFlowNode, ResourceFlowPriorityQuerySolver>>("_solvers");

        static AccessTools.FieldRef<ResourceFlowRequestManager, Action> _RequestsUpdated =
            AccessTools.FieldRefAccess<ResourceFlowRequestManager, Action>("RequestsUpdated");

        internal static ConditionalWeakTable<ResourceFlowRequestManager, RequestManager> s_requestManagers = new ConditionalWeakTable<ResourceFlowRequestManager, RequestManager>();

        // If true, we'll keep using the game's old resource system. Our new system will run in parallel
        // but won't affect the simulation behaviour (much). This is maybe useful for testing discrepancies
        // between the systems, performance testing, etc.
        // If false, most of the old resource system (especially the expensive parts) will be disabled.
        static bool s_useOldSystem = false;

        static RequestManager GetRequestManager(ResourceFlowRequestManager instance)
        {
            if (s_requestManagers.TryGetValue(instance, out RequestManager ret))
                return ret;

            Plugin.Log.LogError("Unknown ResourceFlowRequestManager");
            return null;
        }

        [HarmonyPatch("CopyTo")]
        [HarmonyPrefix]
        static bool CopyTo(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestManager destination,
            IFlowNode node)
        {
            // Despite the name, CopyTo actually moves requests
            GetRequestManager(__instance).MoveRequests(GetRequestManager(destination), node);

            // KSP2 bug: When reconstructing the old _activeRequests, it accidentally pushes all the
            // non-moved values onto hashSet2 (not hashSet3) so they end up on _inactiveRequests.
            // Don't know if that has any visible effect.

            return s_useOldSystem;
        }

        [HarmonyPatch(MethodType.Constructor)]
        [HarmonyPatch(new Type[] { typeof(ResourceDefinitionDatabase), typeof(FlowGraph) })]
        [HarmonyPostfix]
        static void Constructor(
            ResourceFlowRequestManager __instance,
            ResourceDefinitionDatabase resourceDefinitionDatabase,
            FlowGraph flowGraph)
        {
            s_requestManagers.Add(__instance, new RequestManager(__instance, resourceDefinitionDatabase));
        }

        [HarmonyPatch("HasRequest")]
        [HarmonyPrefix]
        static bool HasRequest(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            __result = GetRequestManager(__instance).HasRequest(request);
            return false;
        }

        [HarmonyPatch("TryGetRequest")]
        [HarmonyPrefix]
        static bool TryGetRequest(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ResourceFlowRequestManager.ManagedRequestWrapper wrapper,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            // This is required by:
            //  KSP.Game.StagePartDataContext.GetFuelCurAmount/GetFuelTotalAmount
            //    - calling GetResourceStoredUnits for each fuel ingredient
            //  KSP.Modules.Module_*.OnModuleOABFixedUpdate
            //    - ignores the wrapper, just checking there's a valid handle
            //  KSP.Modules.PropellantState.PropellantState
            //    - calculating fuel mixture density
            //  Broker.AllocateOrGetRequest
            //    - ignores the wrapper, just checking if optionalrequestHandle is valid
            //  KSP.UI.ResourceManagerPartEntry.AreFlowRequestsDone/StartResourceTransfer
            //
            // Some of these look into the returned instructions, which won't work with our reimplementation.
            //
            // TODO: Make this work somehow. In the meantime, keep using the old system
            return true;
        }

        [HarmonyPatch("IsRequestActive")]
        [HarmonyPrefix]
        static bool IsRequestActive(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            __result = GetRequestManager(__instance).IsRequestActive(request);
            return false;
        }

        [HarmonyPatch("IsRequestInactive")]
        [HarmonyPrefix]
        static bool IsRequestInactive(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            __result = !GetRequestManager(__instance).IsRequestActive(request);
            return false;
        }

        [HarmonyPatch("SetRequestActive")]
        [HarmonyPrefix]
        static bool SetRequestActive(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            __result = GetRequestManager(__instance).SetRequestActive(request, true);
            return s_useOldSystem;
        }

        [HarmonyPatch("SetRequestInactive")]
        [HarmonyPrefix]
        static bool SetRequestInactive(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            __result = GetRequestManager(__instance).SetRequestActive(request, false);
            return s_useOldSystem;
        }

        [HarmonyPatch("ForceRemoveRequest")]
        [HarmonyPrefix]
        static bool ForceRemoveRequest(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request)
        {
            GetRequestManager(__instance).OnRemoveRequest(request);

            // TODO: Once TryGetRequest is fixed, return s_useOldSystem
            return true;
        }

        [HarmonyPatch("RequestHasCommands")]
        [HarmonyPrefix]
        static bool RequestHasCommands(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            // We don't save the list of commands, but any command will generate >=1 instructions
            // (unless it's something weird and buggy like a recipe with 0 ingredients), so just
            // count instructions. (Also the game doesn't use this function anyway)
            __result = GetRequestManager(__instance).RequestHasInstructions(request);
            return false;
        }

        [HarmonyPatch("SetCommands")]
        [HarmonyPatch(new Type[] { typeof(ResourceFlowRequestHandle), typeof(IEnumerable<ResourceFlowRequestCommandConfig>), typeof(double), typeof(double) })]
        [HarmonyPrefix]
        static bool SetCommands(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            IEnumerable<ResourceFlowRequestCommandConfig> commands,
            double normalizedFlowMinimum,
            double flowPriorityOffset,
            ref bool __result)
        {
            Plugin.SetCommandsNewTimer.Start();
            GetRequestManager(__instance).SetCommands(request, commands, normalizedFlowMinimum, flowPriorityOffset);
            Plugin.SetCommandsNewTimer.Stop();

            Plugin.SetCommandsOldTimer.Start();

            __result = true;
            return s_useOldSystem;
        }

        [HarmonyPatch("SetCommands")]
        [HarmonyPatch(new Type[] { typeof(ResourceFlowRequestHandle), typeof(IEnumerable<ResourceFlowRequestCommandConfig>), typeof(double), typeof(double) })]
        [HarmonyPostfix]
        static void SetCommands_post(
            ResourceFlowRequestManager __instance)
        {
            Plugin.SetCommandsOldTimer.Stop();
        }

        [HarmonyPatch("UpdateCommands")]
        [HarmonyPrefix]
        static void UpdateCommands(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            double universalTime,
            double deltaTime)
        {
            // Not implemented nor used by KSP2
            throw new NotImplementedException();
        }

        [HarmonyPatch("RequestHasInstructions")]
        [HarmonyPrefix]
        static bool RequestHasInstructions(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle request,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            __result = GetRequestManager(__instance).RequestHasInstructions(request);
            return false;
        }

        [HarmonyPatch("MarkAllResourceSolversDirty")]
        [HarmonyPatch(new Type[] { typeof(KSP.Sim.impl.PartOwnerComponent) })]
        [HarmonyPrefix]
        static bool MarkAllResourceSolversDirty(
            ResourceFlowRequestManager __instance,
            KSP.Sim.impl.PartOwnerComponent partOwner)
        {
            // The game calls RebuildFlowGraphIfDirty(force=true) here, and this is called every time a part
            // is constructed while loading the vessel, giving an O(n^2) cost.
            // Also this happens before the stages are configured, making it useless for us.
            // So just mark it as dirty and let it be rebuilt later.
            // (TODO: Does this break anything?)

            _solvers(__instance).Clear();
            _flowGraph(__instance).SetGraphDirty();
            return false;
        }

        [HarmonyPatch("MarkAllResourceSolversDirty")]
        [HarmonyPatch(new Type[] { typeof(KSP.OAB.ObjectAssembly) })]
        [HarmonyPrefix]
        static bool MarkAllResourceSolversDirty(
            ResourceFlowRequestManager __instance,
            KSP.OAB.ObjectAssembly assembly)
        {
            // TODO: Implement OAB
            return s_useOldSystem;
        }

        [HarmonyPatch("UpdateFlowRequests")]
        [HarmonyPrefix]
        static bool UpdateFlowRequests(
            ResourceFlowRequestManager __instance,
            double tickUniversalTime,
            double tickDeltaTime)
        {
            var man = GetRequestManager(__instance);

            Plugin.CopyContainersTimer.Start();
            man.ReadContainers();
            Plugin.CopyContainersTimer.Stop();

            Plugin.UpdateFlowRequestsNewTimer.Start();
            man.UpdateFlowRequests(tickDeltaTime);

            if (s_useOldSystem)
            {
                Plugin.UpdateFlowRequestsNewTimer.Stop();

                Plugin.UpdateFlowRequestsOldTimer.Start();
                return true;
            }
            else
            {
                Plugin.UpdateFlowRequestsNewTimer.Stop();

                Plugin.CopyContainersTimer.Start();
                man.WriteContainers(out int numChanged);
                Plugin.CopyContainersTimer.Stop();

                Plugin.RequestsUpdatedTimer.Start();
                if (numChanged > 0)
                {
                    _RequestsUpdated(__instance).Invoke();
                }
                Plugin.RequestsUpdatedTimer.Stop();

                Plugin.UpdateFlowRequestsOldTimer.Start();
                return false;
            }
        }

        [HarmonyPatch("UpdateFlowRequests")]
        [HarmonyPostfix]
        static void UpdateFlowRequests_post(
            ResourceFlowRequestManager __instance)
        {
            Plugin.UpdateFlowRequestsOldTimer.Stop();
        }

        [HarmonyPatch("ProcessActiveRequests")]
        [HarmonyPrefix]
        static void ProcessActiveRequests(
            ResourceFlowRequestManager __instance,
            List<ResourceFlowRequestManager.ManagedRequestWrapper> orderedRequests,
            double tickUniversalTime,
            double tickDeltaTime)
        {
            Plugin.UpdateFlowRequestsOldTimer.Stop();
            Plugin.ProcessActiveRequestsTimer.Start();
        }

        [HarmonyPatch("ProcessActiveRequests")]
        [HarmonyPostfix]
        static void ProcessActiveRequests_post(
            ResourceFlowRequestManager __instance)
        {
            Plugin.ProcessActiveRequestsTimer.Stop();
            Plugin.UpdateFlowRequestsOldTimer.Start();
        }

        [HarmonyPatch("AllocateOrGetRequestWrapper")]
        [HarmonyPrefix]
        static bool AllocateOrGetRequestWrapper(
            ResourceFlowRequestManager __instance,
            IFlowNode node,
            string uniqueIdentifier,
            ref ResourceFlowRequestManager.ManagedRequestWrapper __result)
        {
            // TODO: Once TryGetRequest is fixed, reimplement this for !s_useOldSystem
            return true;
        }

        [HarmonyPatch("AllocateRequest")]
        [HarmonyPrefix]
        static void AllocateRequest(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle handle,
            IFlowNode node,
            string uniqueIdentifier)
        {
            // (Only called by AllocateOrGetRequestWrapper)

            GetRequestManager(__instance).OnAllocateRequest(handle, node);
        }

        [HarmonyPatch("GetRequestState")]
        [HarmonyPrefix]
        static bool GetRequestState(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle handle,
            ref FlowRequestResolutionState __result)
        {
            if (s_useOldSystem)
                return true;

            var request = GetRequestManager(__instance).GetRequest(handle);
            __result = default(FlowRequestResolutionState);
            if (request != null)
            {
                __result.RequestHandle = handle; // unused by the game
                __result.LastTickUniversalTime = 0; // unused by the game

                // Used by PartComponentModule_Engine.CheckDeprived, ResourceManagerPartEntry.AreFlowRequestsDone,
                // but they only care if it's >0, and (I think?) we never get called with deltaTime==0, so just fake it
                __result.LastTickDeltaTime = 0.02;

                __result.WasLastTickDeliveryAccepted = request.WasLastTickDeliveryAccepted;
                __result.LastTickDeliveryNormalized = request.LastTickDeliveryNormalized;
                __result.LastAcceptedDeliveryUniversalTime = 0; // unused by the game

                // Used by JetpackResourceManager.CalcuateThrust, PartComponentModule_Engine.CheckDeprived,
                // but only when WasLastTickDeliveryAccepted==true, so it's redundant with LastTickDeliveryNormalized
                __result.LastAcceptedDeliveryNormalized = request.LastTickDeliveryNormalized;

                if (request.ResourceNotProcessed != 0)
                {
                    __result.ResourcesNotProcessed = new List<ResourceDefinitionID> { new ResourceDefinitionID(request.ResourceNotProcessed) };
                }
            }
            return false;
        }

        [HarmonyPatch("GetRequestRequiredResourcesAvailable")]
        [HarmonyPrefix]
        static bool GetRequestRequiredResourcesAvailable(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle handle,
            ref List<ContainedResourceData> resourceList,
            KSP.Sim.ResourceSystem.FlowDirection flowDirection)
        {
            if (s_useOldSystem)
                return true;

            var resources = new Dictionary<ushort, RequestProcessor.RequestResources>();
            GetRequestManager(__instance).GetRequestResources(handle, resources, flowDirection == KSP.Sim.ResourceSystem.FlowDirection.FLOW_INBOUND);

            foreach (var item in resources)
            {
                resourceList.Add(new ContainedResourceData
                {
                    ResourceID = new ResourceDefinitionID(item.Key),
                    CapacityUnits = item.Value.CapacityUnits,
                    StoredUnits = item.Value.StoredUnits,
                    NonStageable = false,
                    IsPartOfRecipe = false,
                    RecipeID = ResourceDefinitionID.InvalidID,
                });
            }
            return false;
        }

        [HarmonyPatch("GetRequestResourceContainerParts")]
        [HarmonyPrefix]
        static bool GetRequestResourceContainerParts(
            ResourceFlowRequestManager __instance,
            ResourceFlowRequestHandle handle,
            ref DictionaryValueList<ResourceFlowRequestManager.RequestPriorityContainerGroupKey, ResourceContainerGroup> __result)
        {
            if (s_useOldSystem)
                return true;

            // Used by DeltaVPropellantInfo.UpdatePropellantInfo, but that only uses the keys, not values.
            // And the key is a single node, so I don't understand what the values are meant to be.
            // So just set all the values to null.

            __result = new DictionaryValueList<ResourceFlowRequestManager.RequestPriorityContainerGroupKey, ResourceContainerGroup>();
            foreach (var (node, priority) in GetRequestManager(__instance).GetRequestContainers(handle))
            {
                __result.Add(new ResourceFlowRequestManager.RequestPriorityContainerGroupKey(node, (double)priority), null);
            }
            return false;
        }

        [HarmonyPatch("TryGetRequestsByResource")]
        [HarmonyPrefix]
        static bool TryGetRequestsByResource(
            ResourceFlowRequestManager __instance,
            string resourceName,
            KSP.Sim.ResourceSystem.FlowDirection direction,
            ref List<ResourceFlowRequestManager.ManagedRequestWrapper> requests,
            ref bool __result)
        {
            if (s_useOldSystem)
                return true;

            // TODO: Implement this properly. (Needed by AssemblyCheckMissingResource)
            requests = null;
            __result = false;
            return false;
        }


        private static FrameState DumpFrameState(
            ResourceFlowRequestManager manager,
            List<ResourceFlowRequestManager.ManagedRequestWrapper> orderedRequests,
            double tickUniversalTime,
            double tickDeltaTime)
        {
            var extra = Plugin.GetFlowGraphExtra(_flowGraph(manager));

            var frame = new FrameState();
            frame.UniversalTime = tickUniversalTime;
            frame.DeltaTime = tickDeltaTime;
            frame.Vessel = extra.VesselState.Graph;

            var reqs = _requestWrappers(manager).GetListEnumerator();
            while (reqs.MoveNext())
            {
                var req = reqs.Current;

                var commands = req.commands.Select(cmd => new Command
                {
                    FlowResource = cmd.FlowResource.Value,
                    FlowUnits = cmd.FlowUnits,
                    TargetUnits = cmd.TargetUnits,
                    FlowDirection = Plugin.ConvertFlowDirection(cmd.FlowDirection),
                    FlowModeOverride = Plugin.ConvertFlowMode(cmd.FlowModeOverride),
                    FlowPriorityOffset = cmd.FlowPriorityOffset,
                    IngredientOverrides = cmd.IngredientOverrides?.Select(x =>
                        new ResourceFlowMod.Lib.ResourceRecipeIngredientDefinitionOverride
                        {
                            name = x.name,
                            unitsPerRecipeUnit = x.unitsPerRecipeUnit,
                            flowMode = Plugin.ConvertFlowMode(x.flowMode),
                        }
                    ).ToArray(),
                }).ToList();

                frame.Requests.Add(new Request
                {
                    Handle = req.requestHandle.ID,
                    Active = manager.IsRequestActive(req.requestHandle),
                    RequestTargetGuid = req.requestTarget.GlobalId.ToString(),
                    UniqueIdentifier = req.uniqueIdentifier,
                    FlowPriorityOffset = req.flowPriorityOffset,
                    Commands = commands,
                });
            }

            foreach (var container in _flowGraph(manager).ContainerGroup.Containers)
            {
                if (container is ResourceContainer resourceContainer)
                {
                    foreach (var resource in resourceContainer.GetAllResourcesContainedData())
                    {
                        frame.Containers.Add(new Container
                        {
                            ResourceId = resource.ResourceID.Value,
                            StoredUnits = resource.StoredUnits,
                            CapacityUnits = resource.CapacityUnits,
                            NonStageable = resource.NonStageable,
                        });
                    }
                }
            }

            return frame;
        }
    }
}