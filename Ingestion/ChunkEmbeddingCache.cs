using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Ingestion;

public class ChunkEmbeddingCache
{
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = false  // Compact — no need for human readability in cache
	};

	// ── Public API ────────────────────────────────────────────────────────────

	public static async Task<List<DocumentChunk>?> TryLoadAsync(
		string cachePath,
		string lorePath,
		string modelName)
	{
		if (!File.Exists(cachePath)) return null;

		try
		{
			var json = await File.ReadAllTextAsync(cachePath);
			var cache = JsonSerializer.Deserialize<CacheFile>(json, Options);

			if (cache == null) return null;

			// Invalidate if model changed
			if (cache.ModelName != modelName)
			{
				Console.WriteLine("  Embedding cache invalid — model changed. Rebuilding...");
				return null;
			}

			// Invalidate if any lore file has changed
			var currentHashes = ComputeFileHashes(lorePath);
			if (!HashesMatch(cache.FileHashes, currentHashes))
			{
				Console.WriteLine("  Embedding cache invalid — lore files changed. Rebuilding...");
				return null;
			}

			Console.WriteLine($"  Embedding cache hit. Loading {cache.Chunks.Count} cached chunks...");
			return cache.Chunks;
		}
		catch
		{
			// Corrupt cache — rebuild
			Console.WriteLine("  Embedding cache corrupt. Rebuilding...");
			return null;
		}
	}

	public static async Task SaveAsync(
		List<DocumentChunk> chunks,
		string cachePath,
		string lorePath,
		string modelName)
	{
		var cache = new CacheFile
		{
			ModelName = modelName,
			FileHashes = ComputeFileHashes(lorePath),
			Chunks = chunks
		};

		var json = JsonSerializer.Serialize(cache, Options);
		await File.WriteAllTextAsync(cachePath, json);

		Console.WriteLine($"  Embedding cache saved. {chunks.Count} chunks.");
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static Dictionary<string, string> ComputeFileHashes(string lorePath)
	{
		var hashes = new Dictionary<string, string>();

		foreach (var file in Directory.GetFiles(lorePath, "*.txt", SearchOption.AllDirectories))
		{
			using var stream = File.OpenRead(file);
			var hash = MD5.HashData(stream);
			hashes[Path.GetFileName(file)] = Convert.ToHexString(hash);
		}

		return hashes;
	}

	private static bool HashesMatch(
		Dictionary<string, string> stored,
		Dictionary<string, string> current)
	{
		if (stored.Count != current.Count) return false;

		foreach (var (file, hash) in current)
		{
			if (!stored.TryGetValue(file, out var storedHash)) return false;
			if (storedHash != hash) return false;
		}

		return true;
	}

	// ── Cache File Model ──────────────────────────────────────────────────────

	private class CacheFile
	{
		[JsonPropertyName("model_name")]
		public string ModelName { get; set; } = string.Empty;

		[JsonPropertyName("file_hashes")]
		public Dictionary<string, string> FileHashes { get; set; } = new();

		[JsonPropertyName("chunks")]
		public List<DocumentChunk> Chunks { get; set; } = new();
	}
}