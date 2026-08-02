using System.Text.Json.Serialization;
using NPCRAGSystem.Domain;

namespace NPCRAGSystem.Domain.Npc;

public class NpcState
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("persona_base")]
	public string PersonaBase { get; set; } = string.Empty;

	// Regional speech register — a key into accents.json (e.g. "northern", "city_working").
	// Expanded into the [VOICE] block of the system prompt by PersonaBuilder, so the register
	// is defined once and shared by every NPC of that region (no per-persona drift).
	[JsonPropertyName("accent")]
	public string Accent { get; set; } = string.Empty;

	// Per-character speech flourish layered on top of the regional accent — a verbal tic,
	// catchphrase, or a PERMANENT trait like a stutter or lisp. (Transient effects like a
	// fear-stutter are state-driven and handled by the model, not here.)
	[JsonPropertyName("speech_quirk")]
	public string SpeechQuirk { get; set; } = string.Empty;

	[JsonPropertyName("default_location")]
	public string DefaultLocation { get; set; } = string.Empty;

	[JsonPropertyName("default_farewell")]
	public string DefaultFarewell { get; set; } = "I need to get on. We'll talk again.";

	[JsonPropertyName("schedule")]
	public List<NpcScheduleEntry> Schedule { get; set; } = new();

	[JsonPropertyName("passions")]
	public List<string> Passions { get; set; } = new();

	[JsonPropertyName("emotional_state")]
	public NpcEmotionalState EmotionalState { get; set; } = new();

	[JsonPropertyName("physical_state")]
	public NpcPhysicalState PhysicalState { get; set; } = new();

	[JsonPropertyName("player_relationship")]
	public NpcPlayerRelationship PlayerRelationship { get; set; } = new();

	// Authored knowledge — slow decay, authored in npcs.json
	[JsonPropertyName("world_memories")]
	public List<NpcMemory> WorldMemories { get; set; } = new();

	// Believed facts not yet claimed by an episode — normal decay
	[JsonPropertyName("orphan_memories")]
	public List<NpcMemory> OrphanMemories { get; set; } = new();

	// Low-credibility claims — injected with sceptical framing
	[JsonPropertyName("suspect_memories")]
	public List<NpcMemory> SuspectMemories { get; set; } = new();

	// Significant encounter memories — slowest decay, anchor network
	[JsonPropertyName("episodic_memories")]
	public List<EpisodicMemory> EpisodicMemories { get; set; } = new();

	[JsonPropertyName("relationships")]
	public List<NpcRelationship> Relationships { get; set; } = new();

	// The NPC's authored "normal" emotional state — the anchor that deviation ranking and
	// relative-intro detection measure against. Snapshotted from the authored values the first
	// time an NPC is loaded (when absent), then PERSISTED. Because debug/runtime changes mutate
	// EmotionalState (not this), the baseline can never be polluted by leftover debug values on
	// a later reload.
	[JsonPropertyName("baseline_emotional_state")]
	public NpcEmotionalState? BaselineEmotionalState { get; set; }

	// The authored "normal" physical state (exhaustion/pain/etc.), same snapshot-once-then-persist
	// rule as the emotional baseline. Lets 'reset' restore physical state, not just mood.
	[JsonPropertyName("baseline_physical_state")]
	public NpcPhysicalState? BaselinePhysicalState { get; set; }

	// Timeless physical description — what the player sees. Source of the anonymous menu label
	// AND the default conversation-opening line when no intro_flavour variant matches.
	[JsonPropertyName("physical_description")]
	public string PhysicalDescription { get; set; } = string.Empty;

	// OPTIONAL location/time-conditioned intro variants shown at the top of a conversation.
	// When none match, the conversation opens on physical_description. Leave empty for simple NPCs.
	[JsonPropertyName("locational_intros")]
	public List<FlavourTextVariant> LocationalIntros { get; set; } = new();

	// If true, player knows this NPC's name from the start without being introduced
	[JsonPropertyName("known_at_start")]
	public bool KnownAtStart { get; set; } = false;

	// Emotion-specific modifiers appended to intro text when that state is dominant (>0.35)
	[JsonPropertyName("emotional_intros")]
	public Dictionary<string, string> EmotionalIntros { get; set; } = new();

	// If true, this NPC has no mouth and cannot speak — they communicate only through
	// eye movements / gaze. Flips the dialogue prompt's "dialogue only" rule. (Agonferre.)
	[JsonPropertyName("nonverbal")]
	public bool Nonverbal { get; set; } = false;

	// ── Knock / sleep ──────────────────────────────────────────────────────────
	// The public location from which the player can knock to rouse this NPC when they're
	// home (i.e. off-schedule, in their unreachable private room). Empty = not knockable.
	[JsonPropertyName("home_door")]
	public string HomeDoor { get; set; } = string.Empty;

	// What the player sees on the knock option, e.g. "the room behind the bar".
	[JsonPropertyName("home_door_label")]
	public string HomeDoorLabel { get; set; } = string.Empty;

	// Personal sleep window. Knocking inside it wakes them; outside it (but still home)
	// they're up. Wraps past midnight when start > end. Default 22:00–06:00.
	[JsonPropertyName("sleep_start_hour")]
	public int SleepStartHour { get; set; } = 22;

	[JsonPropertyName("sleep_end_hour")]
	public int SleepEndHour { get; set; } = 6;

	// What this NPC does in their off-hours at home (cooking, reading, mending, a hobby).
	// Surfaced when the player knocks and finds them awake, so they answer mid-activity.
	// Phrased to follow "you were ...". Empty = a generic "your own evening".
	[JsonPropertyName("home_life")]
	public string HomeLife { get; set; } = string.Empty;

	// NPC depth tier. 1 = Principal (full pipeline — deep memory, gossip, lore RAG; meant to be
	// dived into). 2 = Service (functional/directional, lighter — no long-term memory or gossip).
	// 3 = Ambient (generic padding; forgetful chat on the cheapest model). Default 1.
	[JsonPropertyName("tier")]
	public int Tier { get; set; } = 1;

	// Optional short anonymous label ("a stout, middle-aged man"). If empty, derived from
	// PhysicalDescription. Lets you override the auto-derived label per NPC when it matters.
	[JsonPropertyName("anon_intro")]
	public string AnonIntro { get; set; } = string.Empty;

	// Marks the senior member of a shared household — answers the door when everyone's asleep.
	[JsonPropertyName("household_head")]
	public bool HouseholdHead { get; set; } = false;

	public bool IsAsleepAt(int hour)
	{
		if (SleepStartHour == SleepEndHour) return false;
		return SleepStartHour < SleepEndHour
			? hour >= SleepStartHour && hour < SleepEndHour
			: hour >= SleepStartHour || hour < SleepEndHour;
	}

	// TODO: Add IsDetained, TrustCap, TraumaFloorFear when game engine integration begins
}