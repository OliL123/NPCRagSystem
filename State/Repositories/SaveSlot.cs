using NPCRAGSystem.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NPCRAGSystem.State.Repositories;

// Manages the live save slot. The pristine authored starting state lives in the source tree
// (Data/World for the npcs/ files, Data/SaveTemplate for the single-file seeds); the live game
// lives in a separate save directory seeded from them. This keeps runtime state (evolving NPC
// memories, the clock, player progress) out of the source tree, so rebuilds and content edits
// can never clobber a game in progress.
public static class SaveSlot
{
    // Single-file state copied from the template into a fresh slot. NPC regional files
    // are copied separately (they live in an npcs/ subdirectory).
    private static readonly string[] SeedFiles =
        { "game_state.json", "player_state.json", "debug_npc.json" };

    private static readonly JsonSerializerOptions Indented = JsonDefaults.Readable;

    // Player-derived memory layers wiped on a new game. Authored world_memories,
    // relationships, schedules and the emotional baseline are preserved.
    private static readonly string[] PlayerDerivedMemoryFields =
        { "orphan_memories", "suspect_memories", "episodic_memories" };

    // A slot is considered to exist once it has a game_state to continue from.
    public static bool Exists(string saveDir)
        => Directory.Exists(saveDir) && File.Exists(Path.Combine(saveDir, "game_state.json"));

    // Copy the authored starting state into the save slot (overwrites slot files). The npcs/
    // regional files come from the world dir (Data/World); the single-file seeds from the
    // template dir (Data/SaveTemplate).
    public static void Seed(string worldDir, string templateDir, string saveDir)
    {
        Directory.CreateDirectory(saveDir);

        var npcSrc = Path.Combine(worldDir, "npcs");
        var npcDst = Path.Combine(saveDir, "npcs");
        Directory.CreateDirectory(npcDst);
        if (Directory.Exists(npcSrc))
            foreach (var f in Directory.GetFiles(npcSrc, "*.json"))
                File.Copy(f, Path.Combine(npcDst, Path.GetFileName(f)), overwrite: true);

        foreach (var name in SeedFiles)
        {
            var src = Path.Combine(templateDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(saveDir, name), overwrite: true);
        }
    }

    // Wipe the slot and reseed from the authored starting state, then purge — starts a new game.
    public static void Reset(string worldDir, string templateDir, string saveDir)
    {
        if (Directory.Exists(saveDir))
            Directory.Delete(saveDir, recursive: true);
        Seed(worldDir, templateDir, saveDir);
        Purge(saveDir);
    }

    // Guarantees a true blank slate after seeding, independent of how clean the template
    // happens to be: the player is unknown again, no NPCs are discovered, the clock is
    // back to the start, queued gossip is dropped, and every NPC's player-derived memories
    // (orphan/suspect/episodic) are wiped. Authored content — persona, world_memories,
    // relationships, schedules, emotional baseline — is left untouched.
    private static void Purge(string saveDir)
    {
        // Player identity & discoveries — name unknown, intro not done, nobody known
        var blankPlayer = new JsonObject
        {
            ["name"] = "",
            ["has_completed_intro"] = false,
            ["known_npcs"] = new JsonArray()
        };
        File.WriteAllText(Path.Combine(saveDir, "player_state.json"),
            blankPlayer.ToJsonString(Indented));

        // Clock back to the beginning
        var freshGame = new JsonObject
        {
            ["current_day"] = 1,
            ["current_hour"] = 8,
            ["current_minute"] = 0,
            ["current_location"] = "sleeping_hound_bar"
        };
        File.WriteAllText(Path.Combine(saveDir, "game_state.json"),
            freshGame.ToJsonString(Indented));

        // Strip player-derived memory layers from every NPC, preserving all other fields
        var npcDir = Path.Combine(saveDir, "npcs");
        if (Directory.Exists(npcDir))
        {
            foreach (var file in Directory.GetFiles(npcDir, "*.json"))
            {
                JsonNode? root;
                try { root = JsonNode.Parse(File.ReadAllText(file)); }
                catch { continue; }

                var npcs = root?["npcs"]?.AsArray();
                if (npcs == null) continue;

                foreach (var npc in npcs)
                {
                    if (npc == null) continue;
                    foreach (var field in PlayerDerivedMemoryFields)
                        npc[field] = new JsonArray();
                }

                File.WriteAllText(file, root!.ToJsonString(Indented));
            }
        }

        // No carried-over queued gossip
        var pending = Path.Combine(saveDir, "pending_gossip.json");
        if (File.Exists(pending)) File.Delete(pending);
    }
}
