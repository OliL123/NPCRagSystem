using NPCRAGSystem.Configuration;
using NPCRAGSystem.State.Managers;
using NPCRAGSystem.State.Repositories;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.RAG.Pipeline;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.Game;

public class GameLoop
{
    private readonly RagPipeline _pipeline;
    private readonly NpcRegistry _npcRegistry;
    private readonly NpcMemoryManager _npcMemoryManager;
    private readonly ConversationTracker _conversationTracker;
    private readonly WorkingMemoryManager _workingMemoryManager;
    private readonly GameStateManager _gameStateManager;
    private readonly LocationRegistry _locationRegistry;
    private readonly EpisodicMemoryCreator _episodicCreator;
    private readonly MemoryConsolidator _memoryConsolidator;
    private readonly PlayerStateManager _playerStateManager;
    private readonly SystemConfig _systemConfig;
    private readonly GossipService _gossipService;
    private readonly IReadOnlyList<(string name, ILlmService llm)> _compareLlms;
    private readonly string _primaryModelName;

    private string? _activeNpcId;
    private int _mottePetCount;

    public GameLoop(
        RagPipeline pipeline,
        NpcRegistry npcRegistry,
        NpcMemoryManager npcMemoryManager,
        ConversationTracker conversationTracker,
        WorkingMemoryManager workingMemoryManager,
        GameStateManager gameStateManager,
        LocationRegistry locationRegistry,
        EpisodicMemoryCreator episodicCreator,
        MemoryConsolidator memoryConsolidator,
        PlayerStateManager playerStateManager,
        SystemConfig systemConfig,
        GossipService gossipService,
        string primaryModelName = "default",
        IReadOnlyList<(string name, ILlmService llm)>? compareLlms = null)
    {
        _pipeline = pipeline;
        _npcRegistry = npcRegistry;
        _npcMemoryManager = npcMemoryManager;
        _conversationTracker = conversationTracker;
        _workingMemoryManager = workingMemoryManager;
        _gameStateManager = gameStateManager;
        _locationRegistry = locationRegistry;
        _episodicCreator = episodicCreator;
        _memoryConsolidator = memoryConsolidator;
        _playerStateManager = playerStateManager;
        _systemConfig = systemConfig;
        _gossipService = gossipService;
        _primaryModelName = primaryModelName;
        _compareLlms = compareLlms ?? new List<(string, ILlmService)>();
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    public async Task RunAsync()
    {
        if (_systemConfig.DevMode)
        {
            var compareHint = _compareLlms.Count > 0 ? " | 'compare <msg>'" : "";
            Console.WriteLine("Commands: 'move' | 'leave' | 'stats' | 'time' | 'talk <npc>' | " +
                              "'wm <note> [| <flavour>] [| !]' | 'debug <npc> <attr> <val>' | " +
                              "'tag <good|edit|discard> [texture] [| note]' | 'forget' | 'reset <npc>' | " +
                              $"'collect <on|off>' | 'advance <Nh | Nd>' | 'setloc <loc>'{compareHint} | 'quit'  [dev]");
        }
        else
        {
            Console.WriteLine("Commands: 'move' | 'leave' | 'time' | 'quit'");
        }
        Console.WriteLine();

        if (!_systemConfig.SkipIntro && !_playerStateManager.HasCompletedIntro)
            await IntroSequenceAsync();
        else
            await LocationLoopAsync();
    }

    // ── Intro sequence ────────────────────────────────────────────────────────

    private async Task IntroSequenceAsync()
    {
        _gameStateManager.CurrentLocation = "sleeping_hound_bar";

        ConsoleEx.Dim(
            "\nSomething tugs at the top of your head. Or at least you think that is the top of your head. " +
            "A deep throbbing ache pounds at your skull, making it a bit hard to tell anything from everything.\n\n" +
            "Turns out, it was a horse eating your hair. After further investigation, you find out you're in its " +
            "water trough. That explains why everything is wet. And the horse.\n\n" +
            "You don't know why you're in there, how it got to this, or even your name. " +
            "Maybe it's best to ask someone... There seems to be an inn behind you.\n");

        Console.WriteLine("Actions:");
        Console.WriteLine("  1. The Sleeping Hound\n");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("> ");
            Console.ResetColor();

            var choice = Console.ReadLine()?.Trim() ?? "";

            if (choice.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                await SaveAndQuitAsync(null);
                return;
            }

            if (choice == "1"
                || choice.Contains("sleeping hound", StringComparison.OrdinalIgnoreCase)
                || choice.Contains("inn", StringComparison.OrdinalIgnoreCase)
                || choice.Length == 0)
                break;

            ConsoleEx.Dim("  (type 1 or the name)");
        }

        ConsoleEx.Dim("\nThe warmth hits you first — woodsmoke, old ale, and something frying somewhere in the back. " +
                          "The common room is mostly empty this early.\n");

        var corin = _npcRegistry.GetAllNpcs()
            .FirstOrDefault(n => n.Name.Equals("Corin Maret", StringComparison.OrdinalIgnoreCase));

        if (corin == null)
        {
            await _playerStateManager.CompleteIntroAsync();
            await LocationLoopAsync();
            return;
        }

        _workingMemoryManager.AddAuthoredWorkingMemory(
            corin.Id,
            "This traveller just stumbled in — they were face-down in the water trough outside all morning. You've already said your opening line. Now listen and respond naturally.",
            isSignificant: false);

        _workingMemoryManager.AddAuthoredWorkingMemory(
            corin.Id,
            "You're curious whether they remember anything about last night. Be warm but don't push too hard.",
            isSignificant: false);

        // The player doesn't know Corin yet — show him as "???" like any unmet NPC
        var corinLabel = GetDisplayName(corin);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("--- Speaking with someone ---");
        Console.ResetColor();

        if (!string.IsNullOrEmpty(corin.PhysicalDescription))
        {
            ConsoleEx.Dim($"\n{corin.PhysicalDescription}\n");
        }

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"\n{corinLabel}: ");
        Console.ResetColor();
        Console.WriteLine("Oi lad, you're the one in the trough right?");

        ConsoleEx.Dim("\n  *You hear a woman laughing in the back.*\n");

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"{corinLabel}: ");
        Console.ResetColor();
        Console.WriteLine("Say son, do you remember anything about last night?\n");

        _conversationTracker.AddConversationTurn(
            corin.Id,
            "[You enter the inn]",
            "Oi lad, you're the one in the trough right? Say, do you remember anything about last night?");

        // ── Player's one response to "do you remember anything?" ──────────────
        string firstResponse;
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("You: ");
            Console.ResetColor();

            firstResponse = Console.ReadLine()?.Trim() ?? "";

            if (firstResponse.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                await SaveAndQuitAsync(null);
                return;
            }
            if (!string.IsNullOrEmpty(firstResponse)) break;
        }

        // ── Authored bridge: reacts to their answer and asks for name ─────────
        var low = firstResponse.ToLowerInvariant();
        string corinBridge;

        if (low.Contains("king") || low.Contains("emperor") || low.Contains("crown") ||
            low.Contains("queen") || low.Contains("lord") || low.Contains("ruler") ||
            low.Contains("majest") || low.Contains("royal"))
        {
            corinBridge = "Well, Emperor you may be — but I never caught your Majesty's name. What is it?";
        }
        else if (firstResponse.Length < 35 && (low.StartsWith("no") || low.Contains("nothing") ||
            low.Contains("don't remember") || low.Contains("not much") || low.Contains("nope") ||
            low.Contains("cant") || low.Contains("can't") || low == "no." || low == "nope."))
        {
            corinBridge = "No? Well, how about you tell me your name first — and I'll tell you what I heard last night.";
        }
        else
        {
            corinBridge = "Heh. Well, no matter — I never did catch your name through all that. What do they call you?";
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"\n{corinLabel}: ");
        Console.ResetColor();
        Console.WriteLine(corinBridge);
        Console.WriteLine();

        _conversationTracker.AddConversationTurn(corin.Id, firstResponse, corinBridge);

        // ── Name capture ──────────────────────────────────────────────────────
        while (true)
        {
            ConsoleEx.Dim("(What is your name? Type it and press Enter.)");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Your name: ");
            Console.ResetColor();

            var nameInput = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(nameInput)) continue;
            if (nameInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                await SaveAndQuitAsync(null);
                return;
            }

            // The NPC's spoken reaction is cosmetic and may fail if Ollama hiccups —
            // but the captured name is not. Persist name + intro completion regardless,
            // so a transient error never forces the player back through the intro.
            try
            {
                await _pipeline.HandleNameRevealAsync(corin.Id, nameInput, GetDisplayName(corin));
            }
            catch (HttpRequestException ex) { PrintError(ex.Message); }

            await _playerStateManager.SetNameAsync(nameInput);
            await _playerStateManager.CompleteIntroAsync();
            await _playerStateManager.RevealNpcAsync(corin.Id); // he introduced himself during the intro
            await PersistAsync();

            await EndConversationAsync(corin);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[You've met {corin.Name}!]");
            Console.ResetColor();

            ConsoleEx.Dim("\nYour memory is still a fog. The people here might know something about who you are — it could be worth asking around.\n");

            await LocationLoopAsync();
            return;
        }
    }

    // ── Location loop ─────────────────────────────────────────────────────────

    private async Task LocationLoopAsync()
    {
        string? lastRenderedLocId = null;

        while (true)
        {
            var locId = _gameStateManager.CurrentLocation;
            var location = _locationRegistry.GetLocation(locId);

            if (location == null)
            {
                Console.WriteLine($"[error] Unknown location '{locId}'. Resetting to sleeping_hound_bar.");
                _gameStateManager.CurrentLocation = "sleeping_hound_bar";
                lastRenderedLocId = null;
                continue;
            }

            if (locId != lastRenderedLocId)
            {
                lastRenderedLocId = locId;
                var npcsHere = _locationRegistry.GetNpcsAt(locId, _gameStateManager.CurrentHour, _gameStateManager.CurrentDay);
                RenderLocation(location, locId, npcsHere);
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("> ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            var cmd = await TryHandleCommandAsync(input, null);
            if (cmd.Outcome == CommandOutcome.Quit) { await SaveAndQuitAsync(_activeNpcId); return; }
            if (cmd.Outcome == CommandOutcome.SwitchNpc)
            {
                _activeNpcId = cmd.Target!.Id;
                await ConversationLoopAsync(cmd.Target);
                _activeNpcId = null;
                lastRenderedLocId = null;
                continue;
            }
            if (cmd.Outcome == CommandOutcome.Handled) { lastRenderedLocId = null; continue; }

            if (input.Equals("look", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("location", StringComparison.OrdinalIgnoreCase))
            {
                lastRenderedLocId = null;
                continue;
            }

            // Resolve current people / happenings / exits for input matching. NPCs who are
            // part of an active happening are shown under it, not loose in the room.
            var present = (IReadOnlyList<NpcState>)_locationRegistry.GetNpcsAt(locId, _gameStateManager.CurrentHour, _gameStateManager.CurrentDay);
            var activeHappenings = GetActiveHappenings(location);
            var (loosePeople, gatheredByHappening) = PartitionByHappenings(present, activeHappenings);
            bool hasMotte = locId == "sleeping_hound_bar";
            int npcOffset = hasMotte ? 1 : 0;
            int totalPeople = loosePeople.Count + npcOffset;

            // Numeric input
            if (int.TryParse(input, out var num))
            {
                // 1 = Motte (if in bar)
                if (hasMotte && num == 1)
                {
                    PetMotte();
                    continue;
                }

                // Loose people (not currently part of a happening)
                var npcIdx = num - 1 - npcOffset;
                if (npcIdx >= 0 && npcIdx < loosePeople.Count)
                {
                    _activeNpcId = loosePeople[npcIdx].Id;
                    await ConversationLoopAsync(loosePeople[npcIdx]);
                    _activeNpcId = null;
                    lastRenderedLocId = null;
                    continue;
                }

                // Exits (numbered after people)
                var exitIdx = num - 1 - totalPeople;
                if (exitIdx >= 0 && exitIdx < location.ConnectedLocations.Count)
                {
                    await MoveToLocation(location.ConnectedLocations[exitIdx]);
                    lastRenderedLocId = null;
                    continue;
                }

                // Knockable households (numbered after exits) — one answerer per door
                var knockDoors = GetKnockableDoors(locId);
                var knockIdx = num - 1 - totalPeople - location.ConnectedLocations.Count;
                if (knockIdx >= 0 && knockIdx < knockDoors.Count)
                {
                    var answerer = ChooseAnswerer(knockDoors[knockIdx].residents);
                    _activeNpcId = answerer.Id;
                    await KnockAsync(answerer);
                    _activeNpcId = null;
                    lastRenderedLocId = null;
                    continue;
                }

                // Happenings (numbered after knock) — check out the goings-on here
                var happeningIdx = num - 1 - totalPeople - location.ConnectedLocations.Count - knockDoors.Count;
                if (happeningIdx >= 0 && happeningIdx < activeHappenings.Count)
                {
                    var h = activeHappenings[happeningIdx];
                    gatheredByHappening.TryGetValue(h.Id, out var folk);
                    await CheckOutHappeningAsync(h, folk);
                    lastRenderedLocId = null;
                    continue;
                }
            }

            // Motte by name
            if (hasMotte && (input.Contains("motte", StringComparison.OrdinalIgnoreCase) ||
                input.Contains("pet", StringComparison.OrdinalIgnoreCase) ||
                input.Contains("dog", StringComparison.OrdinalIgnoreCase)))
            {
                PetMotte();
                continue;
            }

            // NPC by name — only works once the player knows who they are (incl. those in a happening)
            // Whole-word on the display name so a short entry doesn't match inside a longer
            // name (typing "Cor" shouldn't select "Cordia"). Id stays a substring match — ids
            // carry underscores ("corin_vale") that a word-boundary check would trip over.
            var selectedNpc = present.FirstOrDefault(n =>
                IsNpcKnown(n) && (
                    StringUtils.IsWholeWordMatch(n.Name, input) ||
                    n.Id.Contains(input, StringComparison.OrdinalIgnoreCase)));

            if (selectedNpc != null)
            {
                _activeNpcId = selectedNpc.Id;
                await ConversationLoopAsync(selectedNpc);
                _activeNpcId = null;
                lastRenderedLocId = null;
                continue;
            }

            // Exit by name
            var matchedExit = location.ConnectedLocations.FirstOrDefault(c =>
            {
                var cl = _locationRegistry.GetLocation(c);
                return cl?.Name.Contains(input, StringComparison.OrdinalIgnoreCase) == true ||
                       c.Contains(input, StringComparison.OrdinalIgnoreCase);
            });

            if (matchedExit != null)
            {
                await MoveToLocation(matchedExit);
                lastRenderedLocId = null;
                continue;
            }

            ConsoleEx.Dim("  (type a number, a name, or 'look' / 'time')");
        }
    }

    private void RenderLocation(LocationState location, string locId, IReadOnlyList<NpcState> npcsHere)
    {
        bool hasMotte = locId == "sleeping_hound_bar";
        int npcOffset = hasMotte ? 1 : 0;

        var flavour = _locationRegistry.GetFlavourText(locId, _gameStateManager.CurrentHour, npcsHere.Count > 0);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n── {location.Name} ── Day {_gameStateManager.CurrentDay}, {_gameStateManager.CurrentHour:D2}:{_gameStateManager.CurrentMinute:D2} — {WorldContext.WeatherFromDay(_gameStateManager.CurrentDay)}");
        Console.ResetColor();

        if (!string.IsNullOrEmpty(flavour))
        {
            ConsoleEx.Dim(flavour);
        }

        Console.WriteLine();

        if (hasMotte)
        {
            var motteLabel = _mottePetCount >= 4 ? "Motte (ignoring you)" : "Pet Motte";
            Console.WriteLine($"  1. {motteLabel}");
        }

        // NPCs in an active happening are shown under it, not loose in the room.
        var activeHappenings = GetActiveHappenings(location);
        var (loosePeople, gatheredByHappening) = PartitionByHappenings(npcsHere, activeHappenings);

        if (loosePeople.Count > 0)
        {
            for (int i = 0; i < loosePeople.Count; i++)
                Console.WriteLine($"  {i + 1 + npcOffset}. {GetNpcLabel(loosePeople[i])}");
        }
        else if (!hasMotte && activeHappenings.Count == 0)
        {
            ConsoleEx.Dim("  (no one here)");
        }

        int totalPeople = loosePeople.Count + npcOffset;

        if (location.ConnectedLocations.Count > 0)
        {
            Console.WriteLine();
            for (int i = 0; i < location.ConnectedLocations.Count; i++)
            {
                var connLoc = _locationRegistry.GetLocation(location.ConnectedLocations[i]);
                var currentLoc = _locationRegistry.GetLocation(_gameStateManager.CurrentLocation);
                var legAdvancesDay = connLoc?.TravelAdvancesDay == true || currentLoc?.TravelAdvancesDay == true;
                var travelTime = legAdvancesDay ? "~1 day"
                    : connLoc?.TravelTimeMinutes == 0 ? "nearby"
                    : $"~{connLoc?.TravelTimeMinutes ?? 15} min";

                // The south gate into the city is locked until you've spoken to the sergeant;
                // once cleared, the newly-open way in is highlighted.
                bool isGate = locId == "antitheis_north_gate" && location.ConnectedLocations[i] == "antitheis_outer_ring";
                if (isGate && !_playerStateManager.HasFlag("gate_north_cleared"))
                {
                    ConsoleEx.Dim($"  {i + 1 + totalPeople}. → {connLoc?.Name}  (speak to the sergeant first)");
                }
                else if (isGate)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  {i + 1 + totalPeople}. → {connLoc?.Name}  ({travelTime})");
                    Console.ResetColor();
                }
                // Closed locations are shown faded and can't be entered until they open.
                else if (connLoc != null && !connLoc.IsOpenAt(_gameStateManager.CurrentHour))
                {
                    ConsoleEx.Dim($"  {i + 1 + totalPeople}. → {connLoc.Name}  (closed)");
                }
                else
                {
                    Console.WriteLine($"  {i + 1 + totalPeople}. → {connLoc?.Name ?? location.ConnectedLocations[i]}  ({travelTime})");
                }
            }
        }

        // Knockable households — one option per door (off-schedule residents who are home).
        var knockDoors = GetKnockableDoors(locId);
        if (knockDoors.Count > 0)
        {
            int afterExits = totalPeople + location.ConnectedLocations.Count;
            Console.WriteLine();
            for (int i = 0; i < knockDoors.Count; i++)
            {
                ConsoleEx.Dim($"  {afterExits + i + 1}. Knock on {knockDoors[i].label}");
            }
        }

        // Happenings — time-gated goings-on you can check out (numbered after knock).
        if (activeHappenings.Count > 0)
        {
            int afterKnock = totalPeople + location.ConnectedLocations.Count + knockDoors.Count;
            Console.WriteLine();
            for (int i = 0; i < activeHappenings.Count; i++)
            {
                var h = activeHappenings[i];
                var hint = gatheredByHappening.TryGetValue(h.Id, out var folk) && folk.Count > 0
                    ? $"  ({folk.Count} {(folk.Count == 1 ? "person" : "people")} here)"
                    : "";
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"  {afterKnock + i + 1}. → check out {h.Label}{hint}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
    }

    // NPCs who can be roused at this location right now: their home_door is here and they
    // are currently home (their scheduled spot resolves to an off-map private room).
    private List<NpcState> GetKnockableNpcs(string locId)
    {
        if (string.IsNullOrEmpty(locId)) return new List<NpcState>();
        var hour = _gameStateManager.CurrentHour;
        var day = _gameStateManager.CurrentDay;
        return _npcRegistry.GetAllNpcs()
            .Where(n => !string.IsNullOrEmpty(n.HomeDoor)
                     && string.Equals(n.HomeDoor, locId, StringComparison.OrdinalIgnoreCase)
                     && _locationRegistry.IsNpcOffSchedule(n, hour, day))
            .ToList();
    }

    // Group knockable residents by household door (their label) so a shared home shows ONE
    // knock option instead of one per person.
    private List<(string label, List<NpcState> residents)> GetKnockableDoors(string locId)
    {
        return GetKnockableNpcs(locId)
            .GroupBy(n => string.IsNullOrEmpty(n.HomeDoorLabel) ? n.Id : n.HomeDoorLabel,
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                label: !string.IsNullOrEmpty(g.First().HomeDoorLabel) ? g.First().HomeDoorLabel : "a closed door",
                residents: g.ToList()))
            .ToList();
    }

    // Who opens the door: someone awake (the one who goes to bed latest) if anyone is; else the
    // household head, falling back to the latest-to-bed — usually the senior adult.
    private NpcState ChooseAnswerer(List<NpcState> residents)
    {
        var hour = _gameStateManager.CurrentHour;
        var awake = residents.Where(n => !n.IsAsleepAt(hour)).ToList();
        var pool = awake.Count > 0 ? awake : residents;
        return pool.FirstOrDefault(n => n.HouseholdHead)
            ?? pool.OrderByDescending(n => n.SleepStartHour).ThenBy(n => n.Id).First();
    }

    // Should this NPC speak first? Not always — service roles usually do, but guarded, wary,
    // hostile or grieving characters tend to wait for the player to approach (more mysterious),
    // and warmth toward the player nudges it the other way. Randomised so it varies.
    private bool ShouldOpen(NpcState npc)
    {
        var e = npc.EmotionalState;
        var r = npc.PlayerRelationship;
        float reluctance = Math.Max(Math.Max(e.Suspicion, e.Fear), Math.Max(e.Anger, Math.Max(e.Anxiety * 0.6f, e.Grief * 0.6f)));
        float baseWillingness = npc.Tier == 2 ? 0.85f : 0.45f;  // Service NPCs initiate by role
        float willingness = Math.Clamp(
            baseWillingness + r.CarePlayer * 0.2f + r.TrustPlayer * 0.15f + e.Hope * 0.1f - reluctance * 0.7f,
            0.05f, 0.95f);
        return Random.Shared.NextSingle() < willingness;
    }

    // Happenings active at the player's location right now (fixtures + the day's random ones).
    private List<Happening> GetActiveHappenings(LocationState location)
    {
        if (location.Happenings.Count == 0) return new List<Happening>();
        int hour = _gameStateManager.CurrentHour;
        int day = _gameStateManager.CurrentDay;
        int dow = WorldContext.DayOfWeekIndex(day);
        return location.Happenings.Where(h => h.IsActive(hour, dow, day)).ToList();
    }

    // Pull NPCs who belong to an active happening out of the loose room list and under it.
    private (List<NpcState> loose, Dictionary<string, List<NpcState>> gathered) PartitionByHappenings(
        IReadOnlyList<NpcState> present, List<Happening> active)
    {
        var gathered = new Dictionary<string, List<NpcState>>();
        var claimed = new HashSet<string>();
        foreach (var h in active)
        {
            var here = present.Where(n => h.NpcIds.Contains(n.Id) && !claimed.Contains(n.Id)).ToList();
            if (here.Count > 0)
            {
                gathered[h.Id] = here;
                foreach (var n in here) claimed.Add(n.Id);
            }
        }
        var loose = present.Where(n => !claimed.Contains(n.Id)).ToList();
        return (loose, gathered);
    }

    // Check out a happening: print its scene, then let the player talk to whoever is gathered.
    private async Task CheckOutHappeningAsync(Happening happening, List<NpcState>? folk)
    {
        if (!string.IsNullOrEmpty(happening.Description))
        {
            ConsoleEx.Dim($"\n{happening.Description}\n");
        }

        // Flavour-only happening — a beat, then back to the room.
        if (folk == null || folk.Count == 0) return;

        while (true)
        {
            Console.WriteLine($"At {happening.Label}:");
            for (int i = 0; i < folk.Count; i++)
                Console.WriteLine($"  {i + 1}. {GetNpcLabel(folk[i])}");
            ConsoleEx.Dim($"  {folk.Count + 1}. (step away)");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("> ");
            Console.ResetColor();
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            if (int.TryParse(input, out var num))
            {
                if (num >= 1 && num <= folk.Count)
                {
                    _activeNpcId = folk[num - 1].Id;
                    await ConversationLoopAsync(folk[num - 1]);
                    _activeNpcId = null;
                    continue;
                }
                if (num == folk.Count + 1) return;
            }

            var cmd = await TryHandleCommandAsync(input, null);
            if (cmd.Outcome == CommandOutcome.Quit) { await SaveAndQuitAsync(_activeNpcId); Environment.Exit(0); return; }
            if (cmd.Outcome == CommandOutcome.SwitchNpc)
            {
                _activeNpcId = cmd.Target!.Id;
                await ConversationLoopAsync(cmd.Target);
                _activeNpcId = null;
                continue;
            }
            if (cmd.Outcome == CommandOutcome.Handled) continue;

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("leave", StringComparison.OrdinalIgnoreCase))
                return;

            var byName = folk.FirstOrDefault(n => IsNpcKnown(n) &&
                (n.Name.Contains(input, StringComparison.OrdinalIgnoreCase) ||
                 n.Id.Contains(input, StringComparison.OrdinalIgnoreCase)));
            if (byName != null)
            {
                _activeNpcId = byName.Id;
                await ConversationLoopAsync(byName);
                _activeNpcId = null;
                continue;
            }

            ConsoleEx.Dim("  (a number, a name, or 'back')");
        }
    }

    // Knock to rouse a resident. They answer at the door (no entering); a working-memory
    // note tells them they were sought out at this hour so they react in character.
    private async Task KnockAsync(NpcState npc)
    {
        var hour = _gameStateManager.CurrentHour;
        var asleep = npc.IsAsleepAt(hour);

        ConsoleEx.Dim(asleep
            ? "\nYou knock. After a long pause and the sound of someone stirring within, the door opens."
            : "\nYou knock. After a moment, the door opens.");

        // The knock framing must match the actual hour — only late-night knocks are "late" and
        // unguarded; a daytime caller is just an ordinary interruption at home.
        bool isLate = hour >= 21 || hour < 6;
        var activity = !string.IsNullOrEmpty(npc.HomeLife) ? npc.HomeLife : "your own business";

        string note;
        if (asleep)
        {
            note = $"The traveller knocked and woke you at {hour:D2}:00 — you were asleep, and answered the door anyway. "
                 + "Roused from sleep and not expecting anyone, whatever you usually hold behind your manner sits closer to the surface than it would by day. Let that show in how you respond, in whichever direction is true to you.";
        }
        else if (isLate)
        {
            note = $"The traveller knocked at your door at {hour:D2}:00, late, after you had gone in for the night — you were {activity}. "
                 + "It is late and you did not expect a caller; you have not put on the face you keep for the daylight, and whatever you usually hold behind your manner sits closer to the surface than it would by day. Let that show in how you respond, in whichever direction is true to you.";
        }
        else
        {
            note = $"The traveller knocked at your door at {hour:D2}:00 — you were at home, {activity}, and did not expect a caller. "
                 + "Answer as you would when interrupted at home during the day.";
        }
        _workingMemoryManager.AddAuthoredWorkingMemory(npc.Id, note, isSignificant: false);

        if (asleep)
            _npcRegistry.UpdateState(npc.Id, "exhaustion", Math.Min(1f, npc.PhysicalState.Exhaustion + 0.2f));

        await ConversationLoopAsync(npc, rousedAtDoor: true);
    }

    private bool IsNpcKnown(NpcState npc)
        => npc.KnownAtStart || _playerStateManager.IsNpcKnown(npc.Id);

    private string GetNpcLabel(NpcState npc)
    {
        if (IsNpcKnown(npc)) return npc.Name;
        if (!string.IsNullOrWhiteSpace(npc.AnonIntro)) return npc.AnonIntro;
        var desc = !string.IsNullOrEmpty(npc.PhysicalDescription) ? npc.PhysicalDescription : "Someone";
        // Cut just after the first "person noun" so the label is a clean short descriptor
        // ("A stout, middle-aged man") instead of a comma-chopped fragment ("A stout") or the
        // whole over-long sentence.
        var m = System.Text.RegularExpressions.Regex.Match(desc,
            @"\b(woman|man|girl|boy|lad|lass|fellow|figure|person|child|priest|sergeant|soldier|guard|gentleman|lady|youth|crone|matron)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return desc[..(m.Index + m.Length)].Trim();
        // Fallback: first clause, or first sentence if there's no comma.
        var commaIdx = desc.IndexOf(',');
        return (commaIdx > 0 ? desc[..commaIdx] : desc.Split('.')[0]).Trim();
    }

    private string GetDisplayName(NpcState npc)
    {
        var name = IsNpcKnown(npc) ? npc.Name : "???";
        return _compareLlms.Count > 0 ? $"[{_primaryModelName}] {name}" : name;
    }

    private string GetNpcIntroText(NpcState npc)
    {
        var variants = npc.LocationalIntros;
        if (variants.Count > 0)
        {
            // The player is co-located with the NPC they're talking to.
            var loc = _gameStateManager.CurrentLocation;
            var hour = _gameStateManager.CurrentHour;

            // Pass 1: a variant tied to the current location (honouring hours if set) —
            // so an NPC is described doing what fits where they actually are.
            foreach (var v in variants)
            {
                if (string.IsNullOrEmpty(v.Location)) continue;
                if (!string.Equals(v.Location, loc, StringComparison.OrdinalIgnoreCase)) continue;
                if (v.Hours != null && !v.Hours.Contains(hour)) continue;
                return v.Text;
            }

            // Pass 2: location-agnostic variants, by hour (original behaviour)
            foreach (var v in variants)
            {
                if (v.Condition == "default") continue;
                if (!string.IsNullOrEmpty(v.Location)) continue;
                if (v.Hours != null && !v.Hours.Contains(hour)) continue;
                return v.Text;
            }

            var def = variants.FirstOrDefault(v => v.Condition == "default");
            if (def != null) return def.Text;
        }
        return npc.PhysicalDescription;
    }

    private void PetMotte()
    {
        _mottePetCount++;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        string motteText = _mottePetCount switch
        {
            1 => "\nMotte opens one eye when you crouch beside her, decides you're acceptable, " +
                 "and leans her broad head into your hand. She smells of old woodsmoke and dog. " +
                 "After a moment she closes her eye again.\n",
            2 => "\nMotte tolerates a second round. Her tail thumps once against the floor, then stops.\n",
            3 => "\nMotte opens one eye, looks at your hand, and very deliberately turns her head the other way.\n",
            _  => "\nMotte gets up, walks to the other side of the fireplace, and lies down with her back to you.\n"
        };
        Console.WriteLine(motteText);
        Console.ResetColor();
    }

    // ── Conversation loop ─────────────────────────────────────────────────────

    private enum CommandOutcome { NotACommand, Handled, SwitchNpc, Quit }

    private readonly record struct CommandResult(CommandOutcome Outcome, NpcState? Target = null)
    {
        public static readonly CommandResult No = new(CommandOutcome.NotACommand);
        public static readonly CommandResult Ok = new(CommandOutcome.Handled);
        public static readonly CommandResult Exit = new(CommandOutcome.Quit);
        public static CommandResult Switch(NpcState n) => new(CommandOutcome.SwitchNpc, n);
    }

    // Single source of truth for typed commands, shared by the location menu, the conversation
    // loop, and the happening loop. Anything that isn't a command returns NotACommand, and the
    // caller handles it in context (menu selection / dialogue / happening pick). This exists
    // because those three loops each used to hand-roll their own command list, so a command added
    // to one (e.g. 'talk') silently fell through as dialogue in the others. 'activeNpc' is whoever
    // is being spoken to, or null at the location/happening menu (commands that need an NPC say so).
    private async Task<CommandResult> TryHandleCommandAsync(string input, NpcState? activeNpc)
    {
        if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            return CommandResult.Exit;

        if (input.Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleEx.Dim($"[time] Day {_gameStateManager.CurrentDay}, {_gameStateManager.CurrentHour:D2}:{_gameStateManager.CurrentMinute:D2} — {WorldContext.WeatherFromDay(_gameStateManager.CurrentDay)}");
            return CommandResult.Ok;
        }

        // Player-facing time skip: "wait 30m", "wait 1h", "wait 1d". Only intercept when the
        // argument is a real duration, so dialogue like "wait, what?" still reaches the NPC.
        if (input.StartsWith("wait ", StringComparison.OrdinalIgnoreCase))
        {
            var waitArg = input[5..].Trim();
            if (IsDurationArg(waitArg))
            {
                await TryAdvanceByArgAsync(waitArg);
                ConsoleEx.Dim($"[time] Day {_gameStateManager.CurrentDay}, {_gameStateManager.CurrentHour:D2}:{_gameStateManager.CurrentMinute:D2} — {WorldContext.WeatherFromDay(_gameStateManager.CurrentDay)}");
                return CommandResult.Ok;
            }
        }

        // Everything below is developer-only. With dev mode off these words fall through as
        // NotACommand, so they reach the NPC as ordinary dialogue.
        if (!_systemConfig.DevMode)
            return CommandResult.No;

        // Commands that act on the active NPC need one; at a menu there isn't one.
        CommandResult NeedsNpc(string name)
        {
            ConsoleEx.Dim($"[{name}] enter a conversation first — no active NPC.");
            return CommandResult.Ok;
        }

        if (input.StartsWith("advance", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAdvanceCommand(input);
            return CommandResult.Ok;
        }

        if (input.StartsWith("debug ", StringComparison.OrdinalIgnoreCase))
        {
            HandleDebugCommand(input);
            return CommandResult.Ok;
        }

        if (input.StartsWith("reset ", StringComparison.OrdinalIgnoreCase))
        {
            HandleResetCommand(input);
            return CommandResult.Ok;
        }

        // Force the current location (dev/testing). Overrides the auto-move that 'talk' applies, so
        // you can drop an NPC somewhere their persona would never name and check the location-aware
        // prompt actually grounds them there. Persists until you move/talk again.
        if (input.Equals("setloc", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("setloc ", StringComparison.OrdinalIgnoreCase))
        {
            var locId = input.Length > 6 ? input[7..].Trim() : "";
            if (string.IsNullOrEmpty(locId))
            {
                ConsoleEx.Dim("[setloc] usage: setloc <location id>. Locations:");
                foreach (var l in _locationRegistry.GetAllLocations().OrderBy(l => l.Id))
                    ConsoleEx.Dim($"  {l.Id}  ({l.Name})");
                return CommandResult.Ok;
            }
            var loc = _locationRegistry.GetLocation(locId);
            if (loc == null) { ConsoleEx.Dim($"[setloc] no location '{locId}'. Type 'setloc' to list ids."); return CommandResult.Ok; }
            _gameStateManager.CurrentLocation = locId;
            ConsoleEx.Dim($"[setloc] location → {loc.Name} ({locId}). NPC prompts will now say you're here.");
            return CommandResult.Ok;
        }

        if (input.Equals("collect", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("collect ", StringComparison.OrdinalIgnoreCase))
        {
            var arg = input.Length > 7 ? input[8..].Trim().ToLowerInvariant() : "";
            _systemConfig.CollectionMode = arg switch
            {
                "on" => true,
                "off" => false,
                _ => !_systemConfig.CollectionMode,
            };
            ConsoleEx.Dim(_systemConfig.CollectionMode
                ? "[collect] ON — memory/state side-effects off; turns are isolated for clean training capture."
                : "[collect] OFF — normal play (memory + state evolution active).");
            return CommandResult.Ok;
        }

        if (input.StartsWith("wm ", StringComparison.OrdinalIgnoreCase))
        {
            if (activeNpc == null) return NeedsNpc("wm");
            HandleWmCommand(input, activeNpc.Id);
            return CommandResult.Ok;
        }

        if (input.StartsWith("compare ", StringComparison.OrdinalIgnoreCase))
        {
            if (activeNpc == null) return NeedsNpc("compare");
            await HandleCompareCommand(input, activeNpc.Id);
            return CommandResult.Ok;
        }

        if (input.StartsWith("tag ", StringComparison.OrdinalIgnoreCase))
        {
            HandleTagCommand(input);
            return CommandResult.Ok;
        }

        if (input.Equals("stats", StringComparison.OrdinalIgnoreCase))
        {
            if (activeNpc == null) return NeedsNpc("stats");
            PrintStats(activeNpc);
            return CommandResult.Ok;
        }

        if (input.Equals("forget", StringComparison.OrdinalIgnoreCase))
        {
            if (activeNpc == null) return NeedsNpc("forget");
            _conversationTracker.ClearConversationHistory(activeNpc.Id);
            _workingMemoryManager.ClearWorkingMemory(activeNpc.Id);
            ConsoleEx.Dim("[forget] conversation thread + working memory cleared — next reply starts fresh.");
            return CommandResult.Ok;
        }

        // Jump into / switch to any NPC by id or name, ignoring location and schedule.
        if (input.Equals("talk", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("talk ", StringComparison.OrdinalIgnoreCase))
        {
            var query = input.Length > 4 ? input[5..].Trim() : "";
            if (string.IsNullOrEmpty(query))
            {
                ConsoleEx.Dim("[talk] usage: talk <npc id or name>. Known NPCs:");
                foreach (var n in _npcRegistry.GetAllNpcs().OrderBy(n => n.Id))
                    ConsoleEx.Dim($"  {n.Id}  ({n.Name})");
                return CommandResult.Ok;
            }
            var target = _npcRegistry.Resolve(query);
            if (target == null) { ConsoleEx.Dim($"[talk] No NPC matching '{query}'."); return CommandResult.Ok; }
            if (activeNpc != null && target.Id == activeNpc.Id)
            {
                ConsoleEx.Dim($"[talk] Already speaking with {GetDisplayName(activeNpc)}.");
                return CommandResult.Ok;
            }
            // Jump the player to wherever the NPC actually is (per their schedule), so the
            // conversation — and the NPC's location-aware prompt — is grounded at their real spot
            // instead of naming wherever you happened to be standing. Without this, talking to a
            // Mid City clerk from a Carvallen bar injects "you are at the Sleeping Hound", which
            // fights their persona and makes them waffle about where they are.
            var npcLoc = _locationRegistry.GetCurrentLocationId(target, _gameStateManager.CurrentHour, _gameStateManager.CurrentDay);
            if (!string.IsNullOrEmpty(npcLoc))
                _gameStateManager.CurrentLocation = npcLoc;
            return CommandResult.Switch(target);
        }

        return CommandResult.No;
    }

    private async Task ConversationLoopAsync(NpcState npc, bool rousedAtDoor = false)
    {
        // Intro sequence — printed on entry, and again whenever a dev 'talk' switches NPC
        // mid-session (see the loop below). Kept as a local so both paths stay identical.
        async Task IntroduceAsync(NpcState who, bool roused)
        {
            var known = IsNpcKnown(who);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(known ? $"\n--- Speaking with {who.Name} ---" : "\n--- Speaking with someone ---");
            Console.ResetColor();

            // When roused at the door, the knock already set the scene, and the NPC is home and
            // off-schedule — describing them at their daytime work would be wrong, so skip the
            // location intro and let only their mood colour the moment.
            string? introText = roused ? null : GetNpcIntroText(who);
            var emotionNote = GetDominantEmotionIntro(who);
            if (!string.IsNullOrEmpty(introText))
            {
                ConsoleEx.Dim(emotionNote != null ? $"\n{introText} {emotionNote}\n" : $"\n{introText}\n");
            }
            else if (emotionNote != null)
            {
                ConsoleEx.Dim($"\n{emotionNote}\n");
            }

            // The NPC may speak first — but not always. Guarded, wary, hostile or grieving characters
            // (and the more mysterious ones) wait for you to approach; it's mood- and role-dependent.
            // When they do open, the line is generated fresh from their seed + mood + time/weather.
            if (!roused && ShouldOpen(who))
            {
                // The pipeline prints the opener itself (??? prefix + thinking dots + streamed line).
                try { await _pipeline.GenerateOpenerAsync(who.Id, GetDisplayName(who)); Console.WriteLine(); }
                catch (HttpRequestException) { /* no opener */ }
            }

            // Speaking with the gate sergeant clears you to pass into the city.
            if (who.Id == "caradek")
                await _playerStateManager.SetFlagAsync("gate_north_cleared");
        }

        await IntroduceAsync(npc, rousedAtDoor);

        while (true)
        {
            // Finish the previous turn's background memory work BEFORE drawing the prompt. It writes
            // its own console output, so flushing after ReadLine let it land in the middle of what
            // the player was typing. Draining it first puts those lines above the prompt, and still
            // guarantees the work is done before we read or mutate state (stats, debug, next query,
            // leaving) further down.
            await _pipeline.FlushPendingMemoryWorkAsync(npc.Id);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("You: ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                try
                {
                    await _pipeline.HandleSilenceAsync(npc.Id, GetDisplayName(npc));
                    await PersistAsync();
                }
                catch (HttpRequestException ex) { PrintError(ex.Message); }
                continue;
            }

            if (input.Equals("leave", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("move", StringComparison.OrdinalIgnoreCase))
            {
                await EndConversationAsync(npc, "The traveller broke off and walked away mid-conversation, leaving you where you stood.");
                if (input.Equals("move", StringComparison.OrdinalIgnoreCase))
                {
                    var loc = _locationRegistry.GetLocation(_gameStateManager.CurrentLocation);
                    if (loc != null) await HandleMoveCommand("move", loc);
                }
                return;
            }

            var cmd = await TryHandleCommandAsync(input, npc);
            if (cmd.Outcome == CommandOutcome.Quit)
            {
                await EndConversationAsync(npc, "The traveller broke off and walked away mid-conversation, leaving you where you stood.");
                await SaveAndQuitAsync(null);
                Environment.Exit(0);
                return;
            }
            if (cmd.Outcome == CommandOutcome.SwitchNpc)
            {
                // 'talk <other>' mid-conversation. In normal play we close the current NPC's
                // session first; in collection mode we just switch so turns stay isolated. Pending
                // memory work was already flushed at the top of the loop.
                if (!_systemConfig.CollectionMode) await EndConversationAsync(npc);
                npc = cmd.Target!;
                _activeNpcId = npc.Id;
                await IntroduceAsync(npc, false);
                continue;
            }
            if (cmd.Outcome == CommandOutcome.Handled) continue;

            try
            {
                var response = await _pipeline.QueryAsync(npc.Id, input, GetDisplayName(npc));
                Console.WriteLine();

                // Reveal the NPC's name once it's in play — either the NPC said their own
                // first name, or the player addressed them by it (e.g. "You Sael?").
                if (!IsNpcKnown(npc))
                {
                    var firstName = npc.Name.Split(' ')[0];
                    // Whole-word, not substring: a short name ("Cor", "Sael") must not count as
                    // "in play" because it happens to sit inside an unrelated word ("cordial").
                    var nameInPlay =
                        (!string.IsNullOrEmpty(response) && StringUtils.IsWholeWordMatch(response, firstName))
                        || StringUtils.IsWholeWordMatch(input, firstName);

                    if (nameInPlay)
                    {
                        await _playerStateManager.RevealNpcAsync(npc.Id);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n[You've met {npc.Name}!]");
                        Console.ResetColor();
                    }
                }

                // NPC state is saved by the turn's background task; game state was saved by
                // the time-advance inside QueryAsync. No per-turn PersistAsync here, so the
                // prompt returns without waiting on (or racing) the background save.

                // The NPC can end the conversation on their own — a farewell/dismissal (the
                // <END> control token) or being too angry/disgusted to continue. In collection
                // mode we suppress this entirely: a single spicy reply (suspicion, anger, a
                // dismissal) must not kick us back to the world menu mid-battery. The collector
                // drives the whole session and ends it manually with 'leave'/'talk'.
                if (!_systemConfig.CollectionMode)
                {
                    if (_pipeline.LastReplyEndedConversation)
                    {
                        var e = npc.EmotionalState;
                        var endNote = (e.Anger >= 0.6f || e.Disgust >= 0.6f)
                            ? "You ended this conversation yourself, your patience with the traveller spent. You did not part well."
                            : "You drew this conversation to a close yourself, in your own time.";
                        await EndConversationAsync(npc, endNote);
                        return;
                    }

                    var farewell = _locationRegistry.GetActiveFarewell(
                        npc, _gameStateManager.CurrentLocation, _gameStateManager.CurrentHour, _gameStateManager.CurrentDay);

                    if (farewell != null)
                    {
                        var displayName = IsNpcKnown(npc) ? npc.Name : "They";
                        ConsoleEx.Dim($"\n{displayName}: {farewell}");
                        await EndConversationAsync(npc, "You had to break off and go; your time was needed elsewhere.");
                        return;
                    }
                }
            }
            catch (HttpRequestException ex) { PrintError(ex.Message); }

            Console.WriteLine();
        }
    }

    // ── Session management ────────────────────────────────────────────────────

    private async Task EndConversationAsync(NpcState npc, string? mannerNote = null)
    {
        // Make sure the last turn's background memory work has landed before we
        // consolidate the session and propagate gossip.
        await _pipeline.FlushPendingMemoryWorkAsync(npc.Id);

        // Write any untagged dialogue turn to the training log before the session closes.
        _pipeline.FlushTrainingLog();

        // Record HOW the conversation ended (the traveller walked off, you dismissed them,
        // you parted badly, you were called away) so it folds into this session's episodic
        // memory and leaves an impression. Only Tier 1 persists the session.
        if (npc.Tier == 1 && !_systemConfig.CollectionMode && !string.IsNullOrEmpty(mannerNote))
            _workingMemoryManager.AddAuthoredWorkingMemory(npc.Id, mannerNote, isSignificant: false);

        // Only Principal (Tier 1) NPCs consolidate a session into long-term episodic memory and
        // spread gossip. Service/Ambient NPCs are functional and don't persist the encounter.
        // Collection mode skips consolidation entirely so battery turns don't contaminate each other.
        if (npc.Tier == 1 && !_systemConfig.CollectionMode)
        {
            var sessionMemories = await _npcMemoryManager.EndSessionAsync(
                npc.Id,
                _episodicCreator,
                _workingMemoryManager,
                _memoryConsolidator,
                _gameStateManager.CurrentDay,
                _systemConfig.LogMemory);

            if (sessionMemories.Count > 0)
                await _gossipService.PropagateAsync(
                    npc, sessionMemories,
                    _gameStateManager.CurrentLocation,
                    _gameStateManager.CurrentHour,
                    _gameStateManager.CurrentDay,
                    _systemConfig.LogGossip);
        }

        _workingMemoryManager.ClearWorkingMemory(npc.Id);
        _conversationTracker.ClearConversationHistory(npc.Id);
        await PersistAsync();
    }

    private async Task SaveAndQuitAsync(string? npcId)
    {
        Console.WriteLine();
        if (npcId != null)
        {
            var npc = _npcRegistry.GetNpc(npcId);
            if (npc != null) await EndConversationAsync(npc);
        }
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        if (_systemConfig.PersistsNpcState)
            await _npcRegistry.SaveAsync();
        if (_systemConfig.PersistsGameState)
            await _gameStateManager.SaveAsync();
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private async Task MoveToLocation(string locationId)
    {
        var target = _locationRegistry.GetLocation(locationId);
        if (target == null) { Console.WriteLine($"[error] Location '{locationId}' not found."); return; }

        // The south gate stays shut to the city until the watch has waved you through.
        if (_gameStateManager.CurrentLocation == "antitheis_north_gate"
            && locationId == "antitheis_outer_ring"
            && !_playerStateManager.HasFlag("gate_north_cleared"))
        {
            ConsoleEx.Dim("  The watch hasn't waved you through yet. Speak to the sergeant at the gate first.");
            return;
        }

        if (!target.IsOpenAt(_gameStateManager.CurrentHour))
        {
            ConsoleEx.Dim($"  {target.Name} is locked at this hour.");
            return;
        }

        var origin = _locationRegistry.GetLocation(_gameStateManager.CurrentLocation);
        var advancesDay = target.TravelAdvancesDay || (origin?.TravelAdvancesDay ?? false);

        if (advancesDay)
        {
            ConsoleEx.Dim($"\n[travel] Travelling to {target.Name} advances the day.");
            await _gameStateManager.AdvanceDayAsync(1,
                _systemConfig.PersistsGameState);
        }
        else if (target.TravelTimeMinutes > 0)
        {
            await _gameStateManager.AdvanceTimeAsync(target.TravelTimeMinutes,
                _systemConfig.PersistsGameState);
        }

        _gameStateManager.CurrentLocation = locationId;
        if (_systemConfig.PersistsGameState)
            await _gameStateManager.SaveAsync();
    }

    private async Task HandleMoveCommand(string input, LocationState location)
    {
        if (location.ConnectedLocations.Count == 0) { Console.WriteLine("  (nowhere to go from here)"); return; }

        Console.WriteLine("\nWhere?");
        for (int i = 0; i < location.ConnectedLocations.Count; i++)
        {
            var cl = _locationRegistry.GetLocation(location.ConnectedLocations[i]);
            Console.WriteLine($"  {i + 1}. {cl?.Name ?? location.ConnectedLocations[i]}");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("> ");
        Console.ResetColor();

        var choice = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(choice)) return;

        string? targetId = null;
        if (int.TryParse(choice, out var idx) && idx >= 1 && idx <= location.ConnectedLocations.Count)
            targetId = location.ConnectedLocations[idx - 1];
        else
            targetId = location.ConnectedLocations.FirstOrDefault(c =>
            {
                var cl = _locationRegistry.GetLocation(c);
                return cl?.Name.Contains(choice, StringComparison.OrdinalIgnoreCase) == true ||
                       c.Contains(choice, StringComparison.OrdinalIgnoreCase);
            });

        if (targetId == null) { Console.WriteLine("  (no matching exit)"); return; }
        await MoveToLocation(targetId);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private async Task HandleCompareCommand(string input, string npcId)
    {
        if (_compareLlms.Count == 0)
        {
            ConsoleEx.Dim("[compare] No comparison models selected at startup.");
            return;
        }

        var message = input["compare ".Length..].Trim();
        if (string.IsNullOrEmpty(message))
        {
            ConsoleEx.Dim("[compare] Usage: compare <message>");
            return;
        }

        ConsoleEx.Dim($"[compare] {_primaryModelName} vs {string.Join(", ", _compareLlms.Select(c => c.name))}");

        await _pipeline.CompareAsync(npcId, message, _compareLlms, _primaryModelName);
    }

    // Tag the most recent dialogue turn for the training set:
    //   tag good | tag good surprise | tag edit | tag discard | tag good hmph | <reason>
    // Optional second word is a free "texture" label (surprise, hesitation, dismissal…);
    // anything after a '|' is a note.
    private void HandleTagCommand(string input)
    {
        var rest = input["tag ".Length..].Trim();
        var noteSplit = rest.Split('|', 2);
        var left = noteSplit[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var note = noteSplit.Length > 1 ? noteSplit[1].Trim() : null;
        var tag = left.Length > 0 ? left[0].ToLowerInvariant() : "";
        var texture = left.Length > 1 ? left[1] : null;

        if (tag is not ("good" or "edit" or "discard"))
        {
            ConsoleEx.Dim("[tag] usage: tag <good|edit|discard> [texture] [| note]");
            return;
        }

        var ok = _pipeline.TagLastTurn(tag, texture, note);
        ConsoleEx.Dim(ok
            ? $"[tag] {tag}{(texture != null ? $" ({texture})" : "")} recorded."
            : "[tag] nothing to tag yet — say something to the NPC first.");
    }

    private async Task HandleAdvanceCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _gameStateManager.AdvanceDayAsync(1, _systemConfig.PersistsGameState);
            ConsoleEx.Dim($"[time] Day {_gameStateManager.CurrentDay}, {_gameStateManager.CurrentHour:D2}:{_gameStateManager.CurrentMinute:D2}");
            return;
        }

        if (await TryAdvanceByArgAsync(parts[1]))
            ConsoleEx.Dim($"[time] Day {_gameStateManager.CurrentDay}, {_gameStateManager.CurrentHour:D2}:{_gameStateManager.CurrentMinute:D2}");
        else
            Console.WriteLine("[advance] usage: advance <Nm | Nh | Nd>  e.g.  advance 30m  advance 2h  advance 1d");
    }

    // True when arg is a bare duration: Nm (minutes), Nh (hours), Nd (days), or N (days).
    private static bool IsDurationArg(string arg)
    {
        arg = arg.Trim().ToLowerInvariant();
        if (arg.Length == 0) return false;
        var body = (arg.EndsWith('m') || arg.EndsWith('h') || arg.EndsWith('d')) ? arg[..^1] : arg;
        return int.TryParse(body, out var n) && n > 0;
    }

    // Shared duration parser used by both `advance` (dev) and `wait` (player).
    private async Task<bool> TryAdvanceByArgAsync(string arg)
    {
        arg = arg.Trim().ToLowerInvariant();
        var persist = _systemConfig.PersistsGameState;

        if (arg.EndsWith('m') && int.TryParse(arg[..^1], out var mins) && mins > 0)
        {
            await _gameStateManager.AdvanceTimeAsync(mins, persist);
            return true;
        }
        if (arg.EndsWith('h') && int.TryParse(arg[..^1], out var hours) && hours > 0)
        {
            await _gameStateManager.AdvanceTimeAsync(hours * 60, persist);
            return true;
        }
        if (arg.EndsWith('d') && int.TryParse(arg[..^1], out var days) && days > 0)
        {
            await _gameStateManager.AdvanceDayAsync(days, persist);
            return true;
        }
        if (int.TryParse(arg, out var rawDays) && rawDays > 0)
        {
            await _gameStateManager.AdvanceDayAsync(rawDays, persist);
            return true;
        }
        return false;
    }

    private void HandleWmCommand(string input, string npcId)
    {
        var raw = input["wm ".Length..].Trim();
        var parts = raw.Split('|', 3);
        var content = parts[0].Trim();
        var flavourText = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var isSignificant = parts.Length > 2 && parts[2].Trim() == "!";

        if (string.IsNullOrEmpty(content)) return;

        _workingMemoryManager.AddAuthoredWorkingMemory(npcId, content, flavourText, isSignificant);
        ConsoleEx.Dim($"[wm] added: \"{content}\"" +
            (!string.IsNullOrEmpty(flavourText) ? $" | flavour: \"{flavourText}\"" : "") +
            (isSignificant ? " | [significant]" : ""));
    }

    private void HandleResetCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) { Console.WriteLine("[reset] usage: reset <npc_id or name>"); return; }

        var target = _npcRegistry.Resolve(parts[1]);
        if (target == null) { Console.WriteLine($"[reset] NPC '{parts[1]}' not found."); return; }

        _npcRegistry.ResetToBaseline(target.Id);
        _workingMemoryManager.ClearWorkingMemory(target.Id);
        _conversationTracker.ClearConversationHistory(target.Id);

        ConsoleEx.Dim($"[reset] {target.Name} restored to authored baseline — player-derived memories, mood, and thread cleared.");
    }

    private void HandleDebugCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) { Console.WriteLine("[debug] usage: debug <npc_id> <attribute> <value>"); return; }

        var attribute = parts[2].ToLowerInvariant();

        if (!float.TryParse(parts[3], out var val)) { Console.WriteLine("[debug] usage: debug <npc_id> <attribute> <value>"); return; }

        var target = _npcRegistry.Resolve(parts[1]);

        if (target != null)
        {
            _npcRegistry.UpdateState(target.Id, attribute, val);
            ConsoleEx.Dim($"[debug] {target.Name} {attribute} → {val:F2}");
        }
        else
        {
            Console.WriteLine($"[debug] NPC '{parts[1]}' not found.");
        }
    }

    // ── Display helpers ───────────────────────────────────────────────────────

    private static string? GetDominantEmotionIntro(NpcState npc)
    {
        const float deltaThreshold = 0.2f;
        var e = npc.EmotionalState;
        var b = npc.BaselineEmotionalState;
        if (b == null) return null;

        var dominant = new[]
        {
            ("fear",      e.Fear      - b.Fear),
            ("anger",     e.Anger     - b.Anger),
            ("grief",     e.Grief     - b.Grief),
            ("anxiety",   e.Anxiety   - b.Anxiety),
            ("suspicion", e.Suspicion - b.Suspicion),
            ("disgust",   e.Disgust   - b.Disgust),
            ("guilt",     e.Guilt     - b.Guilt),
        }
        .Where(x => x.Item2 >= deltaThreshold)
        .OrderByDescending(x => x.Item2)
        .FirstOrDefault();

        if (dominant == default) return null;
        npc.EmotionalIntros.TryGetValue(dominant.Item1, out var intro);
        return intro;
    }

    private void PrintStats(NpcState npc)
    {
        var e = npc.EmotionalState;
        var p = npc.PhysicalState;
        var r = npc.PlayerRelationship;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n[stats] {npc.Name}");
        Console.WriteLine($"  Emotional — fear:{e.Fear:F2} grief:{e.Grief:F2} hope:{e.Hope:F2} " +
                           $"suspicion:{e.Suspicion:F2} anger:{e.Anger:F2} " +
                           $"anxiety:{e.Anxiety:F2} disgust:{e.Disgust:F2} guilt:{e.Guilt:F2}");
        Console.WriteLine($"  Physical  — exhaustion:{p.Exhaustion:F2} pain:{p.Pain:F2} " +
                           $"intoxication:{p.Intoxication:F2} hunger:{p.Hunger:F2} illness:{p.Illness:F2}");
        Console.WriteLine($"  Player    — trust:{r.TrustPlayer:F2} care:{r.CarePlayer:F2} " +
                           $"gullibility:{r.Gullibility:F2} infatuation:{r.InfatuationPlayer:F2} " +
                           $"erratic:{r.PlayerErraticBehaviour:F2}");
        Console.WriteLine($"  Memories  — world:{npc.WorldMemories.Count} orphan:{npc.OrphanMemories.Count} " +
                           $"suspect:{npc.SuspectMemories.Count} episodic:{npc.EpisodicMemories.Count}");

        var workingMems = _workingMemoryManager.GetWorkingMemory(npc.Id);
        if (workingMems.Count > 0)
        {
            Console.WriteLine("  Working memory:");
            foreach (var m in workingMems)
                Console.WriteLine($"    [{(m.IsAuthored ? "authored" : "dynamic")}] {m.Content}" +
                    (!string.IsNullOrEmpty(m.FlavourText) ? $" | flavour: \"{m.FlavourText}\"" : ""));
        }

        Console.ResetColor();
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error: {message}]");
        Console.ResetColor();
    }
}
