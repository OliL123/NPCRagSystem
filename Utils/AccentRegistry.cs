using System.Text.Json;

namespace NPCRAGSystem.Utils;

// Loads the regional speech registers from accents.json once at startup. NPCs reference a
// register by key (NpcState.Accent); PersonaBuilder expands it into the [VOICE] block. Define
// each accent in one place → every NPC of that region stays consistent, and refining a register
// updates the whole cast with no persona edits. (A negative [NOT YOUR VOICE] contrast block was
// tried and removed 2026-07-02 — negation is unreliable on small models and primes the very
// markers it names; positive voice text + the fine-tune do the register work instead.)
public static class AccentRegistry
{
	private static Dictionary<string, string> _accents = new(StringComparer.OrdinalIgnoreCase);

	public static void Load(string path)
	{
		if (!File.Exists(path)) return;
		var wrapper = JsonSerializer.Deserialize<Wrapper>(
			File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (wrapper?.Accents != null)
			_accents = new Dictionary<string, string>(wrapper.Accents, StringComparer.OrdinalIgnoreCase);
	}

	// The register's voice text for a key, or "" if unknown/empty (NPC gets no [VOICE] line).
	public static string GetVoice(string? key)
		=> !string.IsNullOrWhiteSpace(key) && _accents.TryGetValue(key, out var v) ? v : "";

	private class Wrapper
	{
		public Dictionary<string, string>? Accents { get; set; }
	}
}
