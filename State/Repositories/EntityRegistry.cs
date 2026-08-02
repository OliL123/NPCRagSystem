using NPCRAGSystem.Utils;
﻿using System.Text.Json;
using System.Text.Json.Serialization;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.State.Repositories;

public class EntityRegistry : IEntityRegistry
{
	private readonly Dictionary<string, Topic> _entities;

	private EntityRegistry(Dictionary<string, Topic> entities)
	{
		_entities = entities;
	}

	// ── Loading ───────────────────────────────────────────────────────────────

	public static async Task<EntityRegistry> LoadAsync(string path)
	{
		var json = await File.ReadAllTextAsync(path);
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			Converters = { new JsonStringEnumConverter() }
		};

		var entries = JsonSerializer.Deserialize<List<EntityEntry>>(json, options)
			?? throw new InvalidOperationException("Failed to deserialise entities.json");

		var entities = entries.ToDictionary(
			e => e.Name.ToLowerInvariant(),
			e => e.Topic,
			StringComparer.OrdinalIgnoreCase
		);

		Console.WriteLine($"  Entity registry loaded. {entities.Count} known entities.");
		return new EntityRegistry(entities);
	}

	// ── Queries ───────────────────────────────────────────────────────────────

	public List<Topic> GetTopicsForText(string text)
	{
		return MatchEntities(text)
			.Select(m => m.Topic)
			.Distinct()
			.ToList();
	}

	public List<string> GetEntitiesForText(string text)
	{
		return MatchEntities(text)
			.Select(m => m.Name)
			.ToList();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private IEnumerable<(string Name, Topic Topic)> MatchEntities(string text)
	{
		var lower = text.ToLowerInvariant();
		return _entities
			.Where(kvp => StringUtils.IsWholeWordMatch(lower, kvp.Key))
			.Select(kvp => (kvp.Key, kvp.Value));
	}

	private class EntityEntry
	{
		[JsonPropertyName("name")]
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("topic")]
		public Topic Topic { get; set; }
	}
}