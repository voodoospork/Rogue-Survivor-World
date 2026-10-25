Rogue Survivor World
====================

Current World version: 0.3.1.
Recent improvements include active survivors, robbery and community tribute,
attacking stair blockers, and shouts carrying up from the bottom stair.
Existing saves remain supported.

A continuation of Rogue Survivor about independent communities enduring the
collapse. You are one survivor among groups with homes, needs, relationships
and uncertain futures. Cooperation, scarcity, zombies and violence have real
consequences.

World supports larger district populations through area-based population
scaling and performance improvements. The default civilian/street-police target
is 25 on 50x50 maps, 49 on 70x70, and 100 on 100x100. Shared decision/visibility
work reduces repeated calculations; optional Responsive NPCs and Adaptive World
modes provide more simulation capacity for busy worlds. See the population and
performance section below for measured gains and their limits.

Maintained by voodoospork, starting from
Qew7's Rogue Survivor Expanded (https://github.com/Qew7/Rogue-Survivor-Expanded),
which continues Tranquill6's Expanded project (https://github.com/Tranquill6/Rogue-Survivor-Expanded)
and roguedjack's original Rogue Survivor. See CREDITS.txt for the
source lineage, bundled artwork and original licensing references.

World uses its own release numbering. The title screen, window title and Windows
file details identify World; saves, mod requirements and existing user folders
retain compatibility version 0.4.1. See versioning (docs/world-versioning.txt).
World 0.3.0 adds robbery and recurring community tribute. Further feature
releases increase the middle number; fixes increase the final number.

World features
--------------

- A World preset with organically founded civilian tribes and named local police units.
- Community introductions, real needs, aid, NPC-led membership and shared homes.
- Home routines, eligible watch duty and witnessed defense.
- Ordinary gathering and trading during acquaintance, plus bounded nearby outings
  for residents who are off watch, with a return home when time or distance runs out.
- Independent chiefs and residents, household admissions, real food and space concerns,
  communicated relocation, scarcity eviction, fragmentation and last-member collapse.
- Persistent relationships, blood feuds and negotiated peace carried through encounters.
- Hostile communities assembling willing residents for raids on reported rival shelters,
  with real travel, normal combat, home reserves and bounded withdrawal.
- Robbery demands for real food, medicine or weapons, with desperation and visible gear decisions.
- Recurring community tribute, physical collection, refusal and locally declared consequences.
- Lasting community cultures: open visitors, private homes, forceful defense and grudges.
- Distinct guarded communities, groups that attack outsiders on sight, and supply
  raiders that choose vulnerable targets, take real loot and withdraw.
- Predatory groups conceal their intentions behind ordinary introductions and
  stated rules. Unarmed community members avoid attacking armed survivors.
- Heard territorial warnings, temporary household permission, and item-specific restitution.
- Property disputes that can escalate to feuds; amends settle losses and peace ends declarations.
- Dated supply leads and food/medicine expeditions using existing resources.
- Personal barter and entrusted food/medicine deliveries on the same trade circuit,
  with real cargo, carried receipts, promised payment and accountable failed journeys.
- Personal followers that travel together, remember contact and exits, and can be recalled.
- Adjacent trap disarming without stepping onto the trap.
- Explicit attacks against stair blockers, including neutral survivors.
- One-floor upward shout propagation when standing on the bottom stair.
- District populations that scale with map area, supported by shared calculation
  optimizations and optional Responsive NPCs / Adaptive World simulation.

Player knowledge comes from observations and reports and remains visibly dated.
Supplies are finite; groups and followers have no guaranteed safety or survival.
Personal leaders command travelling followers; chiefs coordinate independent residents.
Fresh World games seed no civilian settlements or Leadership bonuses. Civilians must
actually agree to share and legally claim a home. Existing settlements upgrade during
ordinary play, preserving identities, homes and personal follower orders.
Player founding, wider crisis balancing and distant strategic simulation remain future work.

Recent improvements / World 0.3.1
---------------------------------

- Startup activity: survivors keep gathering and trading while getting acquainted.
  Failed shelter opportunities no longer trap them in a social wait. Community
  founding still needs familiarity, a viable home and independent consent.
- Resident activity: off-duty residents can explore and gather near home for up
  to two game hours / twelve tiles, then return. Watch, orders and urgent needs
  retain priority. See docs/survivor-activity.txt.
- Robbery: V / 7 demands real food, medicine or a weapon. NPC choices account for
  desperation, visible equipment and nearby support; V answers an active demand.
- Community tribute: chiefs can demand recurring food payments from weaker
  communities at contact. Collection transfers actual carried food, and refusal
  can have local consequences. V / 4 / 5 reports dated accounts. See
  docs/robbery-and-tribute.txt.
- Stair blockers: stand on the exit, press K, then NumPad5 or click yourself to
  attack the actor at the other landing. Neutral targets can defend themselves.
- Stair shouts: S carries to the upper landing only from the exact bottom stair.
  Normal hearing and waking rules apply; shouts do not relay through more floors.

Current Windows development
---------------------------

Build RogueSurvivor.sln in Visual Studio with modern MSBuild/Roslyn, Microsoft's
.NET desktop development workload and the .NET Framework 4.8 targeting pack.
Select Release / Any CPU. Run WRogue/bin/Release/RogueSurvivor.exe with its
generated Resources, mods and runtime configuration beside it. Choose World
when starting a new game. Existing format-5 saves gain knowledge through play;
loading does not generate groups or resources.

The October 24 World 0.3.1 Release builds with zero warnings/errors and passes
native units. All seven new activity cases pass in Classic, Responsive NPCs and
Adaptive World. The installed build passes 14 focused activity, home, consent,
watch and genuine older-save checks. Robbery/tribute and stair cases pass in the
latest full regression, which records 622/624 successful scenarios.

That full run retains a generated diplomacy player death and a follower reunion
driver failure. The corrected follower driver passes its separate complete
journey; the full run remains qualified. Unchanged 10-second/50-MB save/load
limits pass. A repeated generated-map initial-decision sample improves from
22/67 waits to 0/67; this does not measure long-term encounter frequency.
Human playtesting and Linux/Docker verification remain open. See
docs/survivor-activity.txt and docs/robbery-and-tribute.txt for details;
docs/community-aggression.txt retains earlier aggression evidence.

| Default control | Action |
|---|---|
| V | Talk; 4/5: Tribute accounts, 6: Trade / deliveries, 7: Robbery; answer an active demand. |
| G | Hover an inventory item, then choose an adjacent direction to give it. |
| J / Tab | Heard journal, Communities, Scavenging and Trade circuits. |
| O | Personal follower orders and dated travel/contact status. |
| S | Sound-off and follower replies; carries one floor up from the bottom stair. |
| Ctrl+R | Recall travelling followers within actual hearing range. |
| D | Disarm an adjacent armed trap; choose direction or click nearby. |
| K / NumPad5 | On an exit, explicitly attack the actor blocking the other landing. |
| Ctrl+B | Personal base claims and storage management. |
| Shift+S | Save. |
| Shift+L | Load. |
| Shift+K | Rebind controls. |

Disarming costs one turn and leaves the trap on the ground; Esc cancels free.
Sprung traps retain existing escape rules. Witnessed interference with claimed
supplies or defenses can provoke their owners. Assigned follower work stays
intact when recalling; surviving followers left across a district remain in the party.

Listen to a community's stated rules or ask with V. Permission to visit lasts
180 local turns and gives no right to supplies. A heard territorial warning allows
30 observed awake turns to leave; blocked legal exits pause that grace period.
Leaving ends local boundary pursuit. Peaceful first thefts receive a restitution
demand; forceful communities may defend their stock immediately. G returns
matching usable items, including partial repayment. Spoiled food does not count.
An activated defense must actually be restored at its original position. Returning
what was taken gives no aid reward and does not end an already declared feud.
Personal enemies still prevent giving; negotiated peace grants no immunity.

Trade circuits use one player-carried shipment alongside personal barter. V / 6
hears offers, negotiates, asks for commissions, shares remembered needs and reports
outcomes. G transfers cargo to the named recipient or returns it to its sender.
J, then Tab three times, shows dated trade records; C in this view can
suspend a journey while retaining the unresolved obligation. Senders use their
own spare supplies, retaining personal food and medicine reserves. Deadlines use
two world days; payment is promised from existing goods and may become unavailable.
No new stock or distant simulation is introduced. NPC caravans remain later work.

The October 15 trade-circuit build passes native units and 485/485 scenarios,
including all 20 trade cases and the genuine older format-5 fixture. A controlled
two-district journey exercises personal barter, mid-trip reload, actual delivery
and payment. Large save/load budgets pass. Natural commission frequency and
human menu review remain open; generated supplier counts alone do not establish
completed natural trade journeys.

The earlier October 8 performance regression passed native units and 458/461 scenarios,
including all 20 community-character cases, five performance-cache cases,
and retained adaptation, follower, disarming and genuine older format-5 checks.
Three generated trials were unsuccessful in that earlier run: the diplomacy chief dies, the
overnight trial loses membership before its final save assertion, and the
scavenging expedition misses its unchanged deadline. Fresh character trials
produce territorial warnings, a feud and community losses; one completes aid,
departure, return and dated consequence reports. These players eventually die.
Growth, repair and survival remain contingent on actual conditions.
Automated journeys are separate from human visual review and unscripted play.
The first performance pass improves typical headless changing-world turn time
by 10–15% across controlled 40/104/200-actor workloads, with matching recorded
turn outcomes and unchanged save/load limits. It reuses unchanged sight and
departure geometry while keeping perception and simulation rules. Native
drawing and human input latency still require live measurement; profiling is
disabled by default.
The subsequent controls fix passes native units and 53 focused checks on
the installed build. All 58 original default bindings are preserved; Recall
uses the previously free Ctrl+R and Disarm uses the previously free D. The old
World Save/Recall default pair migrates on loading key settings, while other
custom bindings are retained. Rebinding an occupied key requires confirmation.
The inherited Docker/Linux workflows have not been reproduced for this fork
in the Windows development session.

Starting source: Expanded v0.4.1, commit
6da3d7b7dd21a8741826021ab86d6db14c3fe56c (https://github.com/Qew7/Rogue-Survivor-Expanded/tree/6da3d7b7dd21a8741826021ab86d6db14c3fe56c),
based on Rogue Survivor Alpha 10.1. Original notices, resources, manual and mod
attribution remain in place. The starting Expanded README is archived at
docs/upstream-expanded-readme.txt.

Inherited Expanded reference
----------------------------

The following reference describes the starting Expanded project. World's
features and controls above apply to this fork; engine/mod compatibility remains 0.4.1.

Rogue Survivor Expanded
-----------------------

An expanded version of roguedjack's Rogue Survivor, based on the source from
Tranquill6 (https://github.com/Tranquill6).
Current Expanded version: 0.4.1 (based on Rogue Survivor Alpha 10.1).

Play on macOS or Linux
----------------------

Install Docker with Compose, then run:


docker compose up --build -d


Open http://localhost:6080 (http://localhost:6080), click the game window to
focus it, and press Enter if the game asks to create its data folders. Save with
Shift+S. Saves live in the game-data Docker volume and survive
docker compose down; docker compose down -v deletes them. To use another web
port, start with ROGUE_PORT=6081 docker compose up --build -d. Sound is
unavailable in the browser session.

On Windows, download the RogueSurvivorExpanded-vX.Y.Z-windows.zip asset from
the GitHub release, extract it, and run RogueSurvivor.exe from the extracted
directory. The game requires .NET Framework 4.8. To build from source, build
RogueSurvivor.sln in Release|Any CPU.

New features
------------

Mouse movement
--------------

Press M to toggle mouse movement. Hover over a destination to see the route and
step count, then left-click to walk. Clicking an adjacent enemy or interactive
object performs the usual bump action. Taking damage interrupts travel.

Right-click for a context menu of available actions. At a district edge,
right-click the exit marker (or your character while standing next to the edge)
and choose Leave district to travel to the neighboring district without a
numpad. Shift+M shows the mouse control help in the game log.

When an action asks for a direction (push, pull, barricade, build, give, trade,
and similar actions), click an adjacent tile. Click your character for actions
that can target yourself, or right-click to cancel. Direction keys and Escape
still work. While aiming a grenade, hover over a tile to preview the target and
left-click to throw; you can still aim with direction keys and throw with F.

Mods
----

Open Mods from the main menu to enable multiple mods and set their priority.
Use Space to toggle a mod, Left/Right to change its priority, Enter to apply,
and Esc to cancel. Higher-priority mods override files from lower-priority mods.

Put each mod in its own folder under WRogue/mods. A mod can replace files in
Data/ and Images/; an optional authors.json can provide a description and
one or more authors and websites, plus version and game_version.
The game_version value must name this version (0.4.1) or a compatible
earlier version to enable the mod. Versions 0.1.0 and 0.1.1 are supported.
Deonapocalypse is included as an example. The selected set is remembered for
new games. Loading a save automatically uses its recorded mods and priority;
returning to the menu restores the previous selection. Missing mods fall back
to original files when the save can still be read; otherwise the game reports
the required mod and version. New saves also record the Expanded version and
reject incompatible versions with a message showing both version numbers.

Configurable game presets
-------------------------

New games open the configuration screen with Standard settings. Adjust zombie types, infection,
corpses, evolution, bases, survival thresholds, and decay. Other gameplay
options opens the existing settings for population, spawn rates, events, and
other rules. Load preset offers Standard, Corpses & Infection,
Vintage, Expanded, and your saved configurations. The options screen
shows the loaded values. In the preset list, move with Up/Down and page through
the option preview with Left/Right. Save as preset stores a named configuration for
future games. On the configuration screen, hover over a setting or select it with
Up/Down to read its description. Page Up/Down move between pages of settings;
Left/Right change the selected value.
The chosen rules and settings are stored with the game save. Loading restores
its stored mode rules. World saves require format 5; earlier formats are unsupported.

NPC traits and memories
-----------------------

The new-game configuration has an NPC traits and memories switch. When on,
intelligent living NPCs begin with three traits and one or two unresolved
memories. Traits influence what they collect, how they fight, their willingness
to join and trust a group, trade, exploration, and their response to crime.
Item likes and dislikes can target any item model. Significant experiences,
including a leader's death, violence, a base theft, and a raid, create new
memories for NPCs who experience or witness them. After several game days,
memories resolve according to the NPC's traits and later experiences, granting
a skill level or an advanced trait. Memories tied to people remain in the NPC's
private relationship history after resolution. The NPC reacts differently to
specific people, their leader's group, and their faction when trading or
considering a leader. Inspecting an NPC shows traits, but not private memories.
The catalog and extension points are described in
docs/npc-personality.txt.
Developer contracts and a complete single-file extension are in
docs/npc-content-modules.txt.
Press Shift+I in game to see your relationships with people, groups, and factions.
Press V to talk to a nearby person. With one possible listener the exchange starts
immediately; with several, choose a direction. They may tell you a report they know.
If they asked you or you overheard them ask someone else for food, medicine or
replacement supplies, V lets you answer Y or N (Esc postpones).
Y promises delivery within 180 turns; give the item to
fulfil the promise. N is an actual refusal that can change their next action and
opinion. You also hear nearby NPC conversations through walls within hearing range;
unseen speakers stay anonymous. Their private memories remain hidden.
Press J to review rumors, requests and replies you actually heard. Conversations
and completed trades add contacts to Shift+I; disputes and attacks can change them.
Use Read Records below Load Game to browse format-5 NPC histories together or individually.
Search names, filter life metrics, sort residents, or press I to read the most interesting NPC;
S/F also search and filter events inside a timeline.
Civilian, gang, soldier and CHAR guard NPCs can pursue trait-driven intentions:
thank and repay a helper, ask for or share food, decline a request, and leave an
unsafe leader. These use real AI actions and persist through saves. Internal
intentions remain private during play; Read Records includes their outcomes and
linked stories. NPCs can also pass spoken reports, search for missing companions
through known exits, avoid or warn reported aggressors, and propose group supply
or shelter plans. Participants pursue independent goals; pickups, gifts,
arrivals and return reports determine the episode's outcome. Groups retain their
identity through succession, factions influence plan preferences, and a saved
director limits concurrent stories and reserves resources. A bounded planner
composes actions to reach desired results: an NPC can obtain food by collecting
known supplies, asking another person or accepting a trade offer. Traits and
relationships affect method costs; refusals and changed resources cause
replanning. Action sequences and their consequences are generated during play.
Elementary goals and actions remain authored mechanics. Plans and causal links
survive saving and appear retrospectively in Read Records. See
docs/npc-intentions.txt for behavior and limits.

An integrated state evaluator now generates goals from unmet needs and personal
values: food, health, care, debt, safety, justice, contact and group autonomy.
Traits, relationships and evidence confidence determine their importance.
Existing injury or debt can motivate action without a new scripted incident;
new urgent needs can replace weaker generated goals. Recovery uses remembered
medicine and actual treatment. Read Records retains each goal's state and utility
explanation, and saves preserve its subject and cooldown.

Interactions include scarce-resource disputes, actual concessions or refusals,
promises with deadlines, direct treatment or medicine gifts, food-loss compensation
and attachments to people, specific possessions and home. Replies and outcomes
alter later goals; reputation includes personal, group and faction history.
Completed episodes can cause connected later episodes. See
docs/npc-social-stories.txt for mechanics and scenarios.
Gameplay keeps memories private; Read Records retains retrospective causal history.

The Expanded preset enables claimable bases. Stand inside an enclosed
building and press Ctrl+B to preview its boundaries; press Y to claim
it. Hostile actors and undead block their rooms. Fortified passages can connect
clear buildings into one base. Living NPC leaders with followers can claim bases;
only the leader and their current followers share ownership. The player can
claim a base alone. Your base boundary is highlighted in
green on the minimap, and its district coordinate appears beside your name and
faction. While standing inside another group's base, your status shows
FOREIGN BASE; foreign bases are not marked on the minimap. A basement or
another level can join the same base when its claimable
area has a two-way stair connection to an already claimed area. Claim each
level separately with Ctrl+B and Y. Open subway tracks are not claimed
with a station room. Claiming an unconnected area releases every level of the
previous base; you can own one base at a time. Successful community succession
preserves its linked home sections; ordinary claim loss still applies when the
community has no committed survivor.

In your base, stand in a room and press Ctrl+B, then F to assign food
storage or W to assign weapon storage. In the follower order menu, press
E to send a follower for supplies. They take needed provisions from storage,
search their district and neighboring districts through map exits, and return
found food and weapons to the assigned rooms. Enable Claimable bases in
any preset to use these mechanics. If you move your base during a scavenging
trip, the follower brings collected supplies to the new base.
NPC leaders check each member's current hunger and the food on their base and
in the group's inventories. When those reserves cannot cover current hunger
and the next two days' consumption for everyone, a leader sends an available
follower from the base for supplies. If none is available, the leader may go instead.
They leave stored food and weapons in place until they need them. Taking
supplies from another group's base is possible. Awake witnesses respond according
to their community's learned rules: restitution first or immediate defense.
Private player claims retain their existing ownership and theft rules.

Simulation modes
----------------

New games and older saves default to Classic. The saved Simulation mode
choice is available in preset creation and the current game's options.
Responsive NPCs lets safe NPCs continue validated guarding, resting and
movement between decisions, while checking immediate danger and urgent needs.
Adaptive World also accounts for elapsed distant time, using cheaper quiet
activity and detailed processing for danger and unsupported mechanics. The
player's district and personal followers remain detailed.

Adaptive World has saved Catch up while waiting and Catch-up effort
controls. Entering a district completes required catch-up; cancelling entry
retains unfinished work. Existing saves keep format-5 compatibility. These
modes do not increase population defaults or limits.

Larger populations and performance
----------------------------------

Civs + Police Density sets the ordinary surface population per 50x50
district. The default remains 25. Population scales with map area, rounded to
the nearest resident: 25 at 50x50, 49 at 70x70, and 100 at 100x100. The
options show the reference density and the target for the selected map size.
The adjustable density remains 10–75 per standard district; at 100x100 this
corresponds to 40–300 residents.

Street police retain their 15% chance within that shared budget. Fixed-size
police interiors and other special populations keep their existing staffing.
Refugee waves use the same scaled surface target and the existing distribution
between surface, sewers and subway. Existing maps use their actual dimensions;
loading a save does not add residents, and reducing density does not remove them.
Zombie counts retain their separate setting. These rules apply in every
simulation mode, so larger populations can change both survival and performance.

The performance work provides headroom for these larger populations. Shared
visibility/decision calculations help every mode. Responsive NPCs retains safe
routine actions between full decisions; Adaptive World also processes eligible
quiet distant activity more cheaply. Immediate danger, combat, raids and awake
predatory communities keep detailed processing. Classic remains the default;
choose the other modes through the saved Simulation mode option.

Prior controlled Windows measurements reached 3.20x baseline throughput in a
crowded 200-actor fixture with Responsive NPCs, with a 46.9% shorter median native
command cycle. Gains depend on the workload: generated 100x100 maps with the
scaled population measured about 5% faster in Responsive than optimized Classic,
and simulating more people still costs more time. These are measured capacity
improvements, not a fixed turn-time guarantee. Details, population targets and
benchmark limits are in docs/population-and-performance.txt.

Develop and test
----------------

To publish a release, open Actions → Release → Run workflow on the default
branch and enter a new World X.Y.Z version. The workflow updates VERSION,
World's version constant, Windows application version and README; builds the
Windows game; commits the version; creates world-vX.Y.Z;
and publishes a GitHub Release with a ready-to-run Windows ZIP. The ZIP includes
the executable, resources, and bundled mods. Do not create the tag or GitHub
Release separately. The workflow needs permission to push to the default branch.
Save/mod compatibility and serialized assembly identity stay fixed during World
release bumps. For local version changes, run ruby tools/release_version.rb X.Y.Z.
World's tag prefix keeps its versions distinct from inherited Expanded tags.


docker build --target test .
bash tests/e2e.sh
sh tests/scenario.sh --list
sh tests/scenario.sh --bench-ai
sh tests/scenario.sh storage/save-budget


Gameplay scenarios and how to run one are documented in
docs/gameplay-scenarios.txt. See
optimizations.txt for a summary of completed changes and
docs/performance.txt for their measurements and
docs/save-format.txt for save compatibility. GitHub
Actions runs the automated checks.

The regular test target includes a large save/load performance gate: each
operation must finish within 10 seconds and each save must fit in 50 MB.
It also reports sampled RAM peaks; see the workload and copied-save command in
docs/performance.txt (docs/performance.txt#automated-save-and-load-budget).
