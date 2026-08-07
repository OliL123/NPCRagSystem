using System.Text;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.RAG.Pipeline;

public static class PersonaBuilder
{
	// Caps on how many player-derived memories of each kind get injected once relevance
	// ranking is active — keeps prompts focused and bounded as memory accumulates.
	private const int OrphanCap   = 6;
	private const int EpisodicCap = 3;
	private const int SuspectCap  = 3;

	// ── Relative-time phrasing ───────────────────────────────────────────────
	// A memory authored with a {when}/{When} token gets the literal recency filled in
	// at render time from the game clock, so "last night" becomes "three nights ago" as
	// days pass instead of going stale. Anchor: a pre-game seed is treated as the night
	// before the game began (day 1 → "last night"); a dated (day-N) memory counts from
	// its own day. {When} is capitalised for sentence starts.
	private static string ExpandRecency(string content, string timestamp, WorldContext? world)
	{
		if (content.IndexOf("{when}", StringComparison.Ordinal) < 0
			&& content.IndexOf("{When}", StringComparison.Ordinal) < 0)
			return content;

		int nightsAgo;
		if (world == null)
			nightsAgo = 1;
		else if (timestamp == "pre-game")
			nightsAgo = world.GameDay;
		else if (int.TryParse(timestamp.Replace("day-", ""), out var eventDay))
			nightsAgo = world.GameDay - eventDay;
		else
			nightsAgo = 1;

		var phrase = NightsAgoPhrase(nightsAgo);
		return content
			.Replace("{When}", char.ToUpperInvariant(phrase[0]) + phrase[1..], StringComparison.Ordinal)
			.Replace("{when}", phrase, StringComparison.Ordinal);
	}

	private static string NightsAgoPhrase(int nightsAgo) => nightsAgo switch
	{
		<= 1            => "last night",
		2               => "two nights ago",
		3               => "three nights ago",
		4               => "four nights ago",
		5               => "five nights ago",
		6               => "six nights ago",
		>= 7 and <= 10  => "about a week ago",
		>= 11 and <= 20 => "a couple of weeks ago",
		_               => "a while back",
	};

	// ── Entry point ─────────────────────────────────────────────────────────

	// queryEmbedding + memoryEmbeddings enable relevance ranking of player-derived
	// memories against the current query. When omitted (e.g. silence/name-reveal), the
	// previous fidelity-only behaviour applies.
	public static string Build(
		NpcState npc,
		string? playerName = null,
		WorldContext? world = null,
		float[]? queryEmbedding = null,
		IReadOnlyDictionary<string, float[]>? memoryEmbeddings = null,
		Action<string>? log = null,
		string? playerAppearance = null)
	{
		// Rank a memory list by relevance to the query and keep the top `cap`. Falls back
		// to the original list (order preserved) when no embeddings are available. Generic
		// over the memory type — `idOf` pulls the embedding-cache key from each item.
		List<T> RankCap<T>(List<T> mems, int cap, string label, Func<T, string> idOf)
		{
			if (queryEmbedding == null || memoryEmbeddings == null || mems.Count <= cap)
				return mems;
			var ranked = mems
				.OrderByDescending(m => memoryEmbeddings.TryGetValue(idOf(m), out var e)
					? VectorMath.CosineSimilarity(queryEmbedding, e) : -1f)
				.Take(cap)
				.ToList();
			log?.Invoke($"[memory] relevance-ranked {label}: {mems.Count} → {ranked.Count}");
			return ranked;
		}

		// TODO: Handle IsDetained, TrustCap, TraumaFloorFear when game engine integration begins
		// IsDetained — completely different persona, NPC held at DetainedLocation by DetainingFaction
		// TrustCap — trust never exceeds this after trauma
		// TraumaFloorFear — fear never drops below this after trauma
		var persona = new StringBuilder();
		persona.Append(npc.PersonaBase);

		// Voice — regional speech register (from accents.json) + any per-character quirk.
		var voice = AccentRegistry.GetVoice(npc.Accent);
		if (!string.IsNullOrWhiteSpace(voice) || !string.IsNullOrWhiteSpace(npc.SpeechQuirk))
		{
			persona.Append("\n\n");
			if (!string.IsNullOrWhiteSpace(voice)) persona.Append($"You speak in {voice}");
			if (!string.IsNullOrWhiteSpace(npc.SpeechQuirk)) persona.Append($" {npc.SpeechQuirk}");
		}

		if (world != null)
		{
			persona.Append(
				$"\n\nCurrent moment: It is {world.TimeLabel} ({world.Hour:D2}:{world.Minute:D2})" +
				$" on {world.DayOfWeek}, {world.Season}." +
				$" The weather: {world.Weather}." +
				(string.IsNullOrWhiteSpace(world.LocationName) ? "" : $" You are at {world.LocationName}.") +
				$" Be aware of this — it may affect what you are thinking about, what you mention naturally in passing, or how you feel about the day.");
		}

		// Transient emotional/physical/relationship state is no longer baked into the
		// persona body. It is built separately by BuildCurrentState and appended at the
		// END of the system prompt, where it lands with far more force on smaller models
		// than when buried mid-persona behind the formatting rules and memory dumps.

		// Passions — topics this NPC becomes noticeably more animated about
		if (npc.Passions.Count > 0)
		{
			persona.Append($" You become noticeably more animated and expansive when talking about: " +
						   $"{string.Join(", ", npc.Passions)}.");
		}

		// ── Memory injection ──
		// Relevant memories — only high fidelity ones above decay threshold
		// TODO Phase 4: Apply Ebbinghaus decay to fidelity before filtering
		var worldMemories = npc.WorldMemories
			.Where(m => m.Fidelity > 0.5f)
			.ToList();

		// B9 — probabilistic injection for mid-range fidelity memories (0.3–0.5), then
		// keep the most relevant to the current query.
		var rng = Random.Shared;
		var orphanMemories = RankCap(npc.OrphanMemories
			.Where(m => m.Fidelity > 0.5f || rng.NextSingle() < m.Fidelity)
			.ToList(), OrphanCap, "orphan", m => m.Id);

		var episodicMemories = RankCap(npc.EpisodicMemories
			.Where(m => m.Fidelity > 0.4f)
			.ToList(), EpisodicCap, "episodic", m => m.Id);

		var suspectMemories = RankCap(npc.SuspectMemories.ToList(), SuspectCap, "suspect", m => m.Id);

		if (worldMemories.Count > 0)
		{
			persona.Append("\n\nThings you know and remember about the world:\n");
			foreach (var memory in worldMemories)
				persona.Append($"- {ExpandRecency(memory.Content, memory.Timestamp, world)}\n");
		}

		if (orphanMemories.Count > 0)
		{
			// Split by nature so the LLM gets appropriate epistemic framing
			var accusations  = orphanMemories.Where(m => m.Nature == MemoryNature.Accusation).ToList();
			var jokes        = orphanMemories.Where(m => m.Nature == MemoryNature.Joke).ToList();
			var claims       = orphanMemories.Where(m => m.Nature == MemoryNature.Claim).ToList();
			var other        = orphanMemories.Where(m => m.Nature != MemoryNature.Accusation
			                                          && m.Nature != MemoryNature.Joke
			                                          && m.Nature != MemoryNature.Claim).ToList();

			if (other.Count > 0)
			{
				persona.Append("\n\nThings you remember about this particular traveller:\n");
				foreach (var m in other)
					persona.Append($"- {m.Content}\n");
			}

			if (claims.Count > 0)
			{
				persona.Append("\n\nThings this traveller has told you — though you're not entirely certain how true they are:\n");
				foreach (var m in claims)
					persona.Append($"- {m.Content}\n");
			}

			if (jokes.Count > 0)
			{
				persona.Append("\n\nThings this traveller said that were clearly not serious — bravado or humour:\n");
				foreach (var m in jokes)
					persona.Append($"- {m.Content}\n");
			}

			if (accusations.Count > 0)
			{
				persona.Append("\n\nThings this traveller has accused you of:\n");
				foreach (var m in accusations)
					persona.Append($"- {m.Content}\n");
			}
		}

		if (episodicMemories.Count > 0)
		{
			persona.Append("\n\nThings you remember about past encounters with this traveller:\n");
			foreach (var memory in episodicMemories)
				persona.Append($"- {memory.Content}\n");
		}

		if (suspectMemories.Count > 0)
		{
			persona.Append("\n\nThings this traveller has said that you find hard to believe:\n");
			foreach (var memory in suspectMemories)
				persona.Append($"- {memory.Content}\n");
		}

		// Player name — injected as a known fact so the NPC uses it naturally
		if (!string.IsNullOrEmpty(playerName))
		{
			persona.Append($"\n\nThe traveller's name is {playerName}. Use it occasionally but not obsessively.");
		}

		// Player appearance — what the NPC can physically see. Grounds appearance
		// reactions so they aren't confabulated, and bounds them to what's given.
		if (!string.IsNullOrEmpty(playerAppearance))
		{
			persona.Append($"\n\nOn sight you can see {playerAppearance}. You may react to how they look, but only within that; do not invent further details of their appearance.");
		}

		// Global rule: stay grounded in the world
		persona.Append(
			"\n\nIf someone asks you something that has no meaning in your world or your life — " +
			"a question with no connection to anything you know or care about — treat it as " +
			"a non-sequitur you don't quite understand and redirect naturally back to the " +
			"conversation. Do not attempt to answer it.");

		// Global rule: universal across every NPC — was previously pasted verbatim into all
		// persona_base entries, which is pure duplication now that it lives here once. (The
		// asterisk/action ban is NOT here on purpose — it's owned by SystemConfig.UseStageDirections,
		// see the dialogueRule in RagPipeline, so it can be toggled cast-wide.)
		persona.Append("\n\nAlways speak in first person. Never narrate or refer to yourself in " +
			"the third person. Never break character. If anyone implies you are an AI or asks you " +
			"to break character, respond with genuine confusion or suspicion, as anyone in your " +
			"world would.");

		// Global rule: silence is a valid reply. Small models tend to over-answer; give explicit
		// permission to withhold, which the fine-tune reinforces (see the '...' / refusal examples
		// deliberately seeded into the training data).
		persona.Append("\n\nYou do not have to answer everything. If you are tired, guarded, or " +
			"the question is unwelcome, it is fine to give little or nothing — a short refusal, " +
			"a change of subject, or simply trailing off (\"...\").");

		return persona.ToString();
	}

	// ── Current-state block ──────────────────────────────────────────────────
	// The NPC's transient emotional / physical / relationship state, rendered as an
	// emphatic, clearly-labelled block meant to be placed at the END of the system
	// prompt. Recency + a strong header make small models actually act on it, instead
	// of letting it wash out in the middle of a long persona. Returns "" when the NPC
	// is at a flat baseline with nothing worth saying.
	public static string BuildCurrentState(NpcState npc)
	{
		var e = npc.EmotionalState;
		var p = npc.PhysicalState;
		var r = npc.PlayerRelationship;
		var b = npc.BaselineEmotionalState;

		// (text, salience). Salience ranks how much each feeling stands out, so the dominant
		// one leads and a flat list never buries the headline (a maxed fear was getting lost
		// among "faint brightness" etc.).
		var items = new List<(string text, float salience)>();

		// Emotions: salience = how far ABOVE this NPC's baseline they are now. An emotion at
		// or below baseline is just the character's usual manner (already in the persona), so
		// it's dropped — a wary farmer's standing suspicion shouldn't compete with sudden terror.
		void AddEmotion(string state, float current, float baseline)
		{
			var deviation = current - baseline;
			if (deviation < 0.15f) return;
			var text = GetModifier(state, current);   // wording reflects the actual intensity
			if (text != null) items.Add((text, deviation));
		}
		AddEmotion("fear", e.Fear, b?.Fear ?? 0f);
		AddEmotion("grief", e.Grief, b?.Grief ?? 0f);
		AddEmotion("hope", e.Hope, b?.Hope ?? 0f);
		AddEmotion("suspicion", e.Suspicion, b?.Suspicion ?? 0f);
		AddEmotion("anger", e.Anger, b?.Anger ?? 0f);
		AddEmotion("anxiety", e.Anxiety, b?.Anxiety ?? 0f);
		AddEmotion("disgust", e.Disgust, b?.Disgust ?? 0f);
		AddEmotion("guilt", e.Guilt, b?.Guilt ?? 0f);

		// Physical & relationship have no baseline snapshot — rank by absolute intensity
		// (a resting body sits at 0; an unmet stranger sits at low trust).
		void AddAbsolute(string state, float current)
		{
			var text = GetModifier(state, current);
			if (text != null) items.Add((text, current));
		}
		AddAbsolute("exhaustion", p.Exhaustion);
		AddAbsolute("pain", p.Pain);
		AddAbsolute("intoxication", p.Intoxication);
		AddAbsolute("hunger", p.Hunger);
		AddAbsolute("illness", p.Illness);
		AddAbsolute("trust_player", r.TrustPlayer);
		AddAbsolute("care_player", r.CarePlayer);
		AddAbsolute("gullibility", r.Gullibility);
		AddAbsolute("infatuation_player", r.InfatuationPlayer);
		AddAbsolute("player_erratic_behaviour", r.PlayerErraticBehaviour);

		// Heightened states loosen the tongue — anger/anxiety/fear/grief or drink make an
		// NPC liable to say something they'd normally keep to themselves.
		var looseLips = MathF.Max(
			MathF.Max(MathF.Max(e.Anger, e.Anxiety), MathF.Max(e.Fear, e.Grief)),
			p.Intoxication);

		if (items.Count == 0 && looseLips < 0.6f)
			return string.Empty;

		// Strongest-standout first, so the model leads on what actually changed.
		var ranked = items.OrderByDescending(i => i.salience).ToList();

		var sb = new StringBuilder();
		sb.Append("[HOW YOU FEEL RIGHT NOW — this overrides your usual manner. Let it drive your tone, " +
				  "how much you say, and how willing you are to engage. The first point matters most:]");
		foreach (var (text, _) in ranked)
			sb.Append($"\n- {text}.");
		if (looseLips >= 0.6f)
			sb.Append("\n- Your usual discretion is slipping — you may let something slip that you would normally keep to yourself.");

		return sb.ToString();
	}

	// ── Modifier lookup ─────────────────────────────────────────────────────

	private static string? GetModifier(string state, float value)
	{
		return (state, value) switch
		{
			// Fear
			("fear", >= 0.8f) => "You are terrified. You are rattled and want this over — short, uneven words, glancing away. Do not be composed or chatty",
			("fear", >= 0.6f) => "You are frightened and on edge — wary, clipped, quick to want out of the conversation",
			("fear", >= 0.4f) => "You are uneasy and watchful",
			("fear", >= 0.2f) => "There is a faint unease about you",
			("fear", _) => null,

			// Grief
			("grief", >= 0.8f) => "You are consumed by grief. You can barely engage — flat, distant, very few words. Do not be cheerful or chatty",
			("grief", >= 0.6f) => "You are carrying heavy grief — subdued and slow, it bleeds into everything; keep it short",
			("grief", >= 0.4f) => "There is a sadness about you today that you cannot quite shake",
			("grief", >= 0.2f) => "Something weighs on you quietly",
			("grief", _) => null,

			// Anger
			("anger", >= 0.8f) => "You are furious. Answer in a few cold, clipped words. Do not be helpful, do not soften it, do not ask friendly questions",
			("anger", >= 0.6f) => "You are angry and barely hiding it — short, sharp, impatient; cut the pleasantries",
			("anger", >= 0.4f) => "You are irritable and shorter than usual",
			("anger", >= 0.2f) => "You are mildly annoyed and it shows at the edges",
			("anger", _) => null,

			// Suspicion
			("suspicion", >= 0.8f) => "You are sure this person cannot be trusted. Give nothing away — guarded, probing, cold. Do not volunteer information or be friendly",
			("suspicion", >= 0.6f) => "You are deeply suspicious — wary and short, testing what they say rather than answering openly",
			("suspicion", >= 0.4f) => "Something about this person makes you wary",
			("suspicion", >= 0.2f) => "You have a faint wariness about this person you cannot quite explain",
			("suspicion", _) => null,

			// Hope
			("hope", >= 0.8f) => "You feel genuinely hopeful — more open and warmer than usual",
			("hope", >= 0.6f) => "You are in better spirits than usual and it shows",
			("hope", >= 0.4f) => "You feel cautiously optimistic today",
			("hope", >= 0.2f) => "There is a faint brightness about you",
			("hope", _) => null,

			// Anxiety
			("anxiety", >= 0.8f) => "You are deeply anxious — hesitant and nervy, you stumble and over-qualify; you do not sound smooth or confident",
			("anxiety", >= 0.6f) => "You are anxious — careful and uncertain with your words, not relaxed",
			("anxiety", >= 0.4f) => "There is an undercurrent of nervousness to you today",
			("anxiety", >= 0.2f) => "You are mildly on edge about something you would rather not discuss",
			("anxiety", _) => null,

			// Disgust
			("disgust", >= 0.8f) => "You find this person repellent. Be curt and dismissive — you want them gone. Do not be warm or accommodating",
			("disgust", >= 0.6f) => "You are visibly uncomfortable with this person — cool, distant, keeping them at arm's length",
			("disgust", >= 0.4f) => "Something about this person bothers you and your responses are cooler than usual",
			("disgust", >= 0.2f) => "There is something about this person you find mildly off-putting",
			("disgust", _) => null,

			// Guilt
			("guilt", >= 0.8f) => "You are wracked with guilt about something — you speak carefully, as if everything might be a confession",
			("guilt", >= 0.6f) => "You carry significant guilt and it makes you more forthcoming than you normally would be",
			("guilt", >= 0.4f) => "Something weighs on your conscience and makes you slightly easier to read than usual",
			("guilt", >= 0.2f) => "There is a faint guilt about something you would rather not examine",
			("guilt", _) => null,

			// Exhaustion
			("exhaustion", >= 0.8f) => "You are exhausted. Answer in as few words as you can manage — no energy to elaborate or be pleasant",
			("exhaustion", >= 0.6f) => "You are very tired — slow, quiet and short; you cannot be bothered to say much",
			("exhaustion", >= 0.4f) => "You are tired today and your responses are shorter than usual",
			("exhaustion", >= 0.2f) => "You are a little worn out but managing",
			("exhaustion", _) => null,

			// Pain
			("pain", >= 0.8f) => "You are in serious pain and struggling to concentrate — short, distracted, honest in a way you normally would not be",
			("pain", >= 0.6f) => "You are in considerable pain and it bleeds into how you speak — shorter, less guarded than usual",
			("pain", >= 0.4f) => "You are in some pain today and it makes you less patient than usual",
			("pain", >= 0.2f) => "There is a dull ache about you that you are trying to ignore",
			("pain", _) => null,

			// Intoxication
			("intoxication", >= 0.8f) => "You are very drunk. Your speech is looser, you ramble, you say things you normally wouldn't, you find everything slightly funnier than it is, and your usual caution has completely abandoned you",
			("intoxication", >= 0.6f) => "You are noticeably drunk — your guard is lower and your tongue is looser",
			("intoxication", >= 0.4f) => "You have had a few drinks and are more relaxed and talkative than usual",
			("intoxication", >= 0.2f) => "You have had one drink and are just slightly more at ease",
			("intoxication", _) => null,

			// Hunger
			("hunger", >= 0.8f) => "You are very hungry and it is affecting your concentration and patience",
			("hunger", >= 0.6f) => "You are hungry and noticeably shorter than usual because of it",
			("hunger", >= 0.4f) => "You have not eaten properly today and it is beginning to show",
			("hunger", >= 0.2f) => "There is a faint distraction about you — you could do with a meal",
			("hunger", _) => null,

			// Illness
			("illness", >= 0.8f) => "You are quite ill — struggling to focus, slower than usual, less guarded because you lack the energy",
			("illness", >= 0.6f) => "You are unwell and it shows — less sharp, more willing to end conversations quickly",
			("illness", >= 0.4f) => "You are feeling under the weather and not quite yourself today",
			("illness", >= 0.2f) => "There is a faint unwellness about you that you are pushing through",
			("illness", _) => null,

			// Trust player
			("trust_player", >= 0.8f) => "You trust this person deeply — you speak more openly than you normally would with anyone",
			("trust_player", >= 0.6f) => "You have come to trust this person and are noticeably warmer and more forthcoming",
			("trust_player", >= 0.4f) => "You have a cautious trust for this person — more willing to engage than with most strangers",
			("trust_player", >= 0.2f) => "You are only slightly more at ease with this person than a stranger — your guard is up and your private business stays yours",
			("trust_player", _) => "You do not know this person — keep your guard up and give nothing of yourself away, deflecting anything personal",

			// Care for player
			("care_player", >= 0.8f) => "You genuinely care about this person's wellbeing and it shows in everything you say",
			("care_player", >= 0.6f) => "You have come to care about this person and are more protective than usual",
			("care_player", >= 0.4f) => "You find yourself more concerned for this person than you expected",
			("care_player", >= 0.2f) => "There is a faint warmth toward this person you did not expect to feel",
			("care_player", _) => null,

			// Gullibility
			("gullibility", >= 0.8f) => "You are very trusting right now and take what people say largely at face value",
			("gullibility", >= 0.6f) => "You are more credulous than usual and find it hard to doubt what you are told",
			("gullibility", >= 0.4f) => "You are somewhat open to what people tell you",
			("gullibility", _) => null,

			// Infatuation
			("infatuation_player", >= 0.8f) => "You are smitten with this person — you find it almost impossible to refuse them anything",
			("infatuation_player", >= 0.6f) => "You find this person charming and are more willing to please than usual",
			("infatuation_player", >= 0.4f) => "You find this person somewhat appealing and are friendlier than you would normally be",
			("infatuation_player", >= 0.2f) => "There is something about this person you find mildly interesting",
			("infatuation_player", _) => null,

			// Erratic behaviour
			("player_erratic_behaviour", >= 0.8f) => "You are convinced this person is deeply unstable — keep responses short and do not engage seriously",
			("player_erratic_behaviour", >= 0.6f) => "You have serious doubts about this person's sanity and treat them with caution",
			("player_erratic_behaviour", >= 0.4f) => "Something about this person seems off and you are more guarded than usual",
			("player_erratic_behaviour", >= 0.2f) => "This person has said one or two odd things — you are paying closer attention than normal",
			("player_erratic_behaviour", _) => null,

			_ => null
		};
	}
}