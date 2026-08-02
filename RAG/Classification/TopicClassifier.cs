using NPCRAGSystem.Utils;
﻿using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Classification;
using NPCRAGSystem.Interfaces.Core;

namespace NPCRAGSystem.RAG.Classification;

public class TopicClassifier : ITopicClassifier
{
	private readonly IEntityRegistry _entityRegistry;

	// Keyword overlap between topics is intentional — queries touching multiple
	// domains score higher across both topics, improving retrieval breadth.
	// Known overlaps:
	// "building", "district", "gate"  — Location + Architecture
	// "power", "authority", "enforce" — Faction + History + Governance
	// "noble", "house", "ruling"      — Faction + History
	// "divine", "belief", "phantasis" — Mythology + Religion
	// "legend"                        — Lore + Mythology
	private static readonly Dictionary<Topic, string[]> TopicKeywords = new()
	{
		[Topic.Architecture] = new[]
		{
			"building", "structure", "architecture", "construct", "stone",
			"arch", "vault", "dome", "tower", "spire", "cathedral",
			"campanile", "rotunda", "nave", "facade", "district",
			"civic grammar", "gospel grammar", "bridge", "gate", "wall"
		},
		[Topic.Character] = new[]
		{
			"who is", "who was", "who are", "tell me about him", "tell me about her",
			"what did he", "what did she", "what does he", "what does she",
			"his name", "her name", "person", "people", "individual"
		},
		[Topic.Culture] = new[]
		{
			"food", "eat", "drink", "ale", "custom", "tradition",
			"festival", "clothing", "wear", "art", "music",
			"daily life", "people live", "culture"
		},
		[Topic.Event] = new[]
		{
			"what happened", "when did", "incident", "attack", "burning",
			"fire", "conflict", "battle", "war", "siege", "disaster",
			"accident", "event", "occurred", "took place", "destroyed",
			"abolished", "disbanded", "formed", "founded", "fell",
			"rose", "collapsed", "overthrew"
		},
		[Topic.Faction] = new[]
		{
			"company", "guild", "empire", "faction", "organisation", "group",
			"army", "soldiers", "merchants", "traders", "viriman",
			"march", "garrison", "council", "noble", "house", "ruling",
			"enforce", "power", "authority", "politics", "political",
			"govern", "control", "influence", "dominate"
		},
		[Topic.Governance] = new[]
		{
			"law", "legal", "illegal", "crime", "criminal", "punish",
			"sentence", "trial", "court", "constable", "constabulary",
			"enforce", "jurisdiction", "edict", "decree", "administration",
			"census", "tax", "registration", "civic", "authority"
		},
		[Topic.History] = new[]
		{
			"history", "historical", "power", "empire", "rise", "fall",
			"establish", "found", "origin", "past", "ancient", "old",
			"noble", "house", "ruling", "enforce", "authority"
		},
		[Topic.Location] = new[]
		{
			"where is", "where are", "where was", "how far", "how do i get",
			"which direction", "location", "place", "district", "gate",
			"road", "route", "town", "city", "village", "outskirts"
		},
		[Topic.Lore] = new[]
		{
			"world", "land", "continent", "known", "legend", "tale",
			"story", "record", "book", "scroll", "written", "knowledge",
			"learn", "understand", "explain", "tell me about"
		},
		[Topic.Mythology] = new[]
		{
			"god", "gods", "divine", "deity", "religion", "religious", "prophecy",
			"myth", "legend", "creation", "dream", "phantasis", "reverie", "belief"
		},
		[Topic.Nature] = new[]
		{
			"animal", "creature", "plant", "forest", "river", "sea",
			"ocean", "climate", "weather", "ecology", "fish", "coral",
			"reef", "volcano", "plateau", "biome", "environment", "nature",
			"gulf", "mountain", "steppe", "tundra", "desert", "savanna"
		},
		[Topic.Relationship] = new[]
		{
			"relationship", "connection", "between", "linked", "tied",
			"alliance", "enemy", "rival", "friend", "associate",
			"together", "involved", "related", "affect", "influence"
		},
		[Topic.Religion] = new[]
		{
			"gospel", "primarch", "sermon", "worship", "faith", "belief",
			"shrine", "folk practice", "ritual", "prayer", "temple",
			"excision", "dreamer", "phantasis", "divine", "sacred",
			"holy", "reverie", "sect", "doctrine", "heresy"
		}
	};

	// ── Construction ────────────────────────────────────────────────────────

	public TopicClassifier(IEntityRegistry entityRegistry)
	{
		_entityRegistry = entityRegistry;
	}

	// ── Classification ──────────────────────────────────────────────────────

	public List<Topic> Classify(string query)
	{
		var lower = query.ToLowerInvariant();
		var scores = new Dictionary<Topic, int>();

		// Entity registry hits score higher than keyword matches
		var entityTopics = _entityRegistry.GetTopicsForText(lower);
		foreach (var topic in entityTopics)
			scores[topic] = scores.GetValueOrDefault(topic) + 3;

		// Keyword scoring — whole-word matching prevents substring false positives
		// ("art" matching "particular", "war" matching "toward", etc.)
		foreach (var (topic, keywords) in TopicKeywords)
		{
			var score = keywords.Count(k => StringUtils.IsWholeWordMatch(lower, k));
			if (score > 0)
				scores[topic] = scores.GetValueOrDefault(topic) + score;
		}

		if (scores.Count == 0)
			return new List<Topic> { Topic.Lore };

		return scores
			.OrderByDescending(x => x.Value)
			.Select(x => x.Key)
			.ToList();
	}
}