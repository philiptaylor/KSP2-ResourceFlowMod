# KSP2 Resource Flow Mod

This mod reimplements KSP2's resource flow system, in an attempt to make it more efficient than the game's original
implementation (especially for very large rockets) and hopefully fix some bugs and add some features.

It is currently very incomplete and probably introduces lots of new bugs and breaks several features,
so it's not actually useful.

## Installation

Requires BepInEx 5.x (or SpaceWarp 0.4.0 which includes BepInEx).

There are currently no prebuilt binaries, so see the section below.

## Building

Copy `KSP2_x64_Data\Managed\*.dll` into `lib\`

Run `dotnet build`

To avoid having to copy files, set up a soft link from KSP2 to the build output (in an Administrator command prompt):

```
cd "\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program 2\BepInEx\plugins\ResourceFlowMod"
mklink ResourceFlowMod.Lib.dll c:\wherever_you_put_it\ResourceFlowMod.Plugin\bin\Debug\netstandard2.0\ResourceFlowMod.Lib.dll
mklink ResourceFlowMod.Lib.pdb c:\wherever_you_put_it\ResourceFlowMod.Plugin\bin\Debug\netstandard2.0\ResourceFlowMod.Lib.pdb
mklink ResourceFlowMod.Plugin.dll c:\wherever_you_put_it\ResourceFlowMod.Plugin\bin\Debug\netstandard2.0\ResourceFlowMod.Plugin.dll
mklink ResourceFlowMod.Plugin.pdb c:\wherever_you_put_it\ResourceFlowMod.Plugin\bin\Debug\netstandard2.0\ResourceFlowMod.Plugin.pdb
```

or copy the files manually if you prefer.

## Code layout

`ResourceFlowMod.Plugin` is the BepInEx plugin. This uses Harmony to patch various KSP2 functions and inject our new code.

The plugin also includes some GUI and debug visualisation code, and can export the relevant game state as JSON.

`ResourceFlowMod.Lib` is a standalone library (no dependency on KSP2 or Unity) which contains most of the resource flow logic.
To avoid that dependency, it also replicates some of the structures and enums used by KSP2's resource API (where necessary
for compatibility).

`ResourceFlowMod.TestApp` is a console application that uses `ResourceFlowMod.Lib` and reads the JSON exported by the plugin,
to allow testing and debugging outside of the game.

## KSP2 resource flow API

This section is a brief introduction to the API which the mod is reimplementing.

Every vessel in the game contains an `ObjectAssembly` (in the OAB) or a `PartOwnerComponent` (in flight).
For simplicity we will only talk about the `PartOwnerComponent`, but both are similar.

Every `PartOwnerComponent` owns a `ResourceFlowRequestManager`.

`PartOwnerComponent` has a list of all the `PartComponent`s in the vessel.
Every `PartComponent` owns a `ResourceFlowRequestBroker`, which is a thin wrapper around the `ResourceFlowRequestManager`
(but with the ability to switch to a different manager, e.g. when a part is moved between vessels).

Every `PartComponent` owns an array of one `ResourceContainer`. This contains an array of resource type, capacity and stored units.

Other components can register a resource flow request with `ResourceFlowRequestBroker.AllocateOrGetRequest()`,
and then assign a set of flow commands with `ResourceFlowRequestBroker.SetCommands()`. (The commands will persist
until the next `SetCommands()`, so they can be set up once and then left, but some components call it every `FixedUpdate`
(i.e. every 20 msecs).)

`SetCommands()` is passed an array of `ResourceFlowRequestCommandConfig`. These specify the resource type,
desired flow rate (units per second), direction (inbound or outbound from the vessel), etc.

Some resources are defined as _recipes_ of two or more other resources (which must be non-recipes).
For example Methalox is defined as 0.2 units of Methane and 0.8 units of Oxidizer for every unit of Methalox.
Resource converters (e.g. fuel cells) will have both inbound and outbound commands in the same request.

When a request contains multiple commands (which may themselves contain multiple ingredients),
the ratio between resources must be preserved. E.g. if a methalox-to-monoprop converter only has enough oxidizer
available for 50% of the requested flow, then it will consume 50% of the methane and produce 50% of the monoprop.

`SetCommands()` also takes a `normalizedFlowMinimum` parameter. If the fraction of the flow that can be satisfied
is below this value, then the request will fail and no resources will be moved.
(This has some unfortunate consequences for the implementation; it would be really nice if it could be removed
from the API.)

Once per `FixedUpdate`, `ResourceFlowRequestManager.UpdateFlowRequests()` will process all the requests sequentially
and update the `ResourceContainer` state.

## Current implementation

The current implementation has a number of issues which this mod is trying to address:

It seems to have roughly O(n^3) worst-case performance in the number of parts; with a few dozen parts it can be doing
a megabyte of memory allocations per frame. That seems to be mainly because `ResourceFlowRequestManager.UpdateFlowRequests`
calls `CreateRequestContainerGroup` for every request, which does a deep copy of every resource container in the group,
and then every new `ResourceContainer` broadcasts a `ResourceContainedChangedMessage` to every other part.

Even without the message broadcast, it still seems relatively expensive because of all the copying.

The cost is particularly important because resource flow needs to be calculated for every vessel in the game,
as they may be e.g. in the middle of an interstellar burn and actively using resources while the player is not looking
at them. The calculations are done on every `FixedUpdate`, which is normally every 20 msecs, but at 4x physics time-warp
it will be every 5 msecs.
And at very high time-warp factors it may become necessary to iterate the flow calculations multiple times per `FixedUpdate`,
e.g. when a vessel is both producing and consuming a resource and the quantity per update exceeds the capacity
of its resource containers.

That was the original motivation for experimenting with a more efficient implementation.

## New implementation

`VesselGraph` represents the basic structure of a vessel (parts and attachments and containers and fuel lines etc).

`VesselConverter` converts the game's representation (starting from `PartOwnerComponent` or
the OAB equivalent) into a `VesselGraph`, plus some extra data structures to map between that and the
game objects.

`BasicFlowGraph` represents the resource flow within a vessel. It is constructed from a `VesselGraph`.
First it flattens out all the game-specific properties (fuel lines, crossfeed, etc) into a simple graph of
nodes and directional edges.
Then it computes the strongly connected components of that graph (representing sets of parts that can all
reach the same set of containers, to avoid redundant computation later), and a DAG of those strong components
(representing any acyclic connections over fuel lines).
Then for every strong component, it stores a list of all containers reachable over the DAG.

It also computes container priorities by searching outwards from from the root node, using the `ActivationStage`
property of decoupler nodes to determine the order in which containers will be decoupled.

`BasicFlowGraph` is mostly implemented with lists of structs, and references are `ushort` indices into lists.
This is to minimise the number of heap allocations (reducing GC cost), hopefully improve cache efficiency etc,
and hypothetically to make it easier to port to Burst compilation in the future (if that ever seems worthwhile).

`RequestManager` stores the list of requests. `SetCommands` converts a list of commands (which may use recipes)
into a list of instructions (of non-recipe resources).

`RequestProcessor` implements the resource flow. For each instruction in an active request, it gets the list
of reachable containers (from the `BasicFlowGraph`), and determines how much of the request can be satifised
by the current container state. Then it applies the satisfiable fraction of the request: for each instruction
it finds the highest-priority group of reachable containers, splits the resource between each container (in
proportion to their stored or available amount, to keep them balanced), and moves on to the next lower
priority until complete.