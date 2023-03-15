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
    public enum ResourceFlowMode : byte
    {
        NULL, // invalid
        NO_FLOW, // cannot flow between parts
        ALL_VESSEL, // all parts; priority order

        // TODO: What do these all mean?
        STAGE_PRIORITY_FLOW,
        STACK_PRIORITY_SEARCH,
        STAGE_STACK_FLOW_BALANCE,
    }

    public struct ResourceUnitsPair
    {
        public ushort ResourceId;
        public double Units;
    }

    public class ResourceProperties
    {
        public ResourceFlowMode FlowMode;
        public bool NonStageable;
    }

    public class RecipeProperties
    {
        public List<ResourceUnitsPair> Ingredients;
    }

    public class ResourceDef
    {
        public ushort ResourceId;
        public string Name;
        public string Abbreviation;
        public bool IsRecipeInDatabase;
        public bool IsRecipe;
        public ResourceProperties ResourceProperties;
        public RecipeProperties RecipeProperties;
    }

    public class ResourceDefDatabase
    {
        public List<ResourceDef> Resources = new List<ResourceDef>();
    }
}