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

using HarmonyLib;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using KSP.OAB;

namespace ResourceFlowMod.Plugin
{
    [HarmonyPatch(typeof(FlowGraph))]
    class Patches_FlowGraph
    {
        static AccessTools.FieldRef<FlowGraph, bool> _flowGraphIsDirty =
            AccessTools.FieldRefAccess<FlowGraph, bool>("_flowGraphIsDirty");

        [HarmonyPatch("RebuildFlowGraphIfDirty")]
        [HarmonyPrefix]
        static void RebuildFlowGraphIfDirty(
            FlowGraph __instance,
            PartOwnerComponent partOwner,
            bool force,
            ref System.Diagnostics.Stopwatch __state)
        {
            var extra = Plugin.GetFlowGraphExtra(__instance);

            if (_flowGraphIsDirty(__instance) || force)
            {
                // TODO: BuildVesselGraph depends on PartComponent.ActivationStage, which isn't necessarily
                // set by the time this is called. Maybe we need some way to force staging to be evaluated?

                __state = System.Diagnostics.Stopwatch.StartNew();

                extra.VesselState = VesselConverter.BuildVesselGraph(partOwner);

                __state.Stop();
                Plugin.Log.LogInfo($"BuildVesselGraph took {__state.Elapsed.TotalMilliseconds:F3} msecs");

                __state.Restart();
            }
            else
            {
                __state = null;
            }
        }

        [HarmonyPatch("RebuildFlowGraphIfDirty")]
        [HarmonyPostfix]
        static void RebuildFlowGraphIfDirty_post(
            FlowGraph __instance,
            ref System.Diagnostics.Stopwatch __state)
        {
            if (__state != null)
            {
                __state.Stop();
                Plugin.Log.LogInfo($"RebuildFlowGraphIfDirty took {__state.Elapsed.TotalMilliseconds:F3} msecs");
            }
        }

        [HarmonyPatch("RebuildOABFlowGraphIfDirty")]
        [HarmonyPrefix]
        static void RebuildOABFlowGraphIfDirty(
            FlowGraph __instance,
            ObjectAssembly objectAssembly,
            bool force)
        {
            if (_flowGraphIsDirty(__instance) || force)
            {
                // TODO
            }
        }
    }
}