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
    // All the relevant state for reproducing a frame in the offline test app,
    // in a JSON-serialisable structure
    public class FrameState
    {
        public double UniversalTime;
        public double DeltaTime;
        public VesselGraph Vessel;
        public List<Request> Requests = new List<Request>();
        public List<Container> Containers = new List<Container>();
    }

    public struct Container
    {
        public ushort ResourceId;
        public double CapacityUnits;
        public double StoredUnits;
        public bool NonStageable;
    }

    public class Request
    {
        public uint Handle;
        public bool Active;
        public string RequestTargetGuid;
        public string UniqueIdentifier;
        public double FlowPriorityOffset;
        public List<Command> Commands;
    }

    public enum FlowDirection
    {
        FLOW_INBOUND,
        FLOW_OUTBOUND
    }

    public class ResourceRecipeIngredientDefinitionOverride
    {
        public string name;
        public double unitsPerRecipeUnit;
        public ResourceFlowMode flowMode;
    }

    public class Command
    {
        public ushort FlowResource;
        public double FlowUnits;
        public double TargetUnits;
        public FlowDirection FlowDirection;
        public ResourceFlowMode FlowModeOverride;
        public double FlowPriorityOffset;
        public ResourceRecipeIngredientDefinitionOverride[] IngredientOverrides;
    }
}