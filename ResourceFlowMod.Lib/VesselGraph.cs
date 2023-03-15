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
    public class VesselGraph
    {
        public struct Node // TODO: rename to Part
        {
            public string Guid; // TODO: better mapping between Node and IFlowNode
            public string Name; // just used for debugging
            public bool IsDecoupler;
            public int ActivationStage;
            public bool FuelCrossfeed;
            public bool IsFuelLine;
            public bool IsFirstAnchor;
            public int OtherEndAnchor; // Parts index, or -1 if none
        }

        public struct Edge
        {
            public int From;
            public int To;
            public bool AllowCrossfeed;
        }

        public struct Resource // XXX: rename to Container
        {
            public int Node;
            public ushort ResourceID;
            public double CapacityUnits;
            public double StoredUnits;
            public bool NonStageable; // TODO: what does this mean? (true for EVAPropellant, ElectricCharge, MonoPropellant)
        }

        public List<Node> Parts = new List<Node>();
        public int RootPart;
        public List<Edge> Attachments = new List<Edge>();
        public List<Resource> Resources = new List<Resource>();
    }
}
