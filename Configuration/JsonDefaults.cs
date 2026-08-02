using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPCRAGSystem.Configuration;

// Shared JSON options for all save/template files. The key setting is the relaxed encoder:
// the default System.Text.Json encoder escapes apostrophes as ' and em-dashes as —
// (hundreds per NPC file), which makes the authored templates painful to read and edit.
// UnsafeRelaxedJsonEscaping keeps those characters literal. "Unsafe" only means it does not
// additionally escape HTML-sensitive characters — irrelevant for local game JSON.
public static class JsonDefaults
{
    // Indented, readable, with the relaxed encoder. Used for game-state, player-state and
    // pending-gossip files (no enums, default-cased keys via [JsonPropertyName]).
    public static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // As Readable, plus case-insensitive property matching and string-enum (de)serialisation.
    // Used for the NPC and location files.
    public static readonly JsonSerializerOptions Config = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
