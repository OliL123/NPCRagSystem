using System.Text.Json;
using NPCRAGSystem.Configuration;
using Spectre.Console;

namespace NPCRAGSystem.Game;

public static class ModelPicker
{
    private record OllamaModel(string Name, long Size);

    public static async Task<(string Primary, string? Critique, List<string> Comparisons)> PickAsync(
        HttpClient http,
        string ollamaUrl,
        string fallback,
        SystemConfig config)
    {
        if (!config.EnableModelPicker) return (fallback, null, new List<string>());

        List<OllamaModel> models;
        try
        {
            var json = await http.GetStringAsync($"{ollamaUrl}/api/tags");
            using var doc = JsonDocument.Parse(json);
            models = doc.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(m => new OllamaModel(
                    m.GetProperty("name").GetString() ?? "",
                    m.TryGetProperty("size", out var s) ? s.GetInt64() : 0))
                .Where(m => !string.IsNullOrEmpty(m.Name))
                .OrderBy(m => m.Name)
                .ToList();
        }
        catch
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Could not reach Ollama at {Markup.Escape(ollamaUrl)} — is it running? " +
                $"Falling back to '{Markup.Escape(fallback)}'.[/]");
            return (fallback, null, new List<string>());
        }

        if (models.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Ollama is running but has no models installed. Falling back to '{Markup.Escape(fallback)}'.[/]");
            return (fallback, null, new List<string>());
        }
        if (models.Count == 1)
        {
            AnsiConsole.MarkupLine($"[dim]Only one model installed ({Markup.Escape(models[0].Name)}) — using it.[/]");
            return (models[0].Name, null, new List<string>());
        }

        AnsiConsole.WriteLine();

        var primary = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Select primary model:[/]")
                .AddChoices(models.Select(FormatModelChoice)));

        primary = StripSize(primary);

        // Self-critique is off by default — it runs a second full generation per reply,
        // which is slow on a large primary model. Let the player opt in.
        config.UseSelfCritique = AnsiConsole.Confirm(
            "[dim]Enable self-critique? (catches hallucinations, but adds a second pass per reply — slower)[/]",
            defaultValue: false);

        string? critique = null;
        if (config.UseSelfCritique)
        {
            var others = models.Where(m => m.Name != primary).ToList();
            if (others.Count > 0)
            {
                var enableCritique = AnsiConsole.Confirm(
                    "[dim]Use a separate model for critique? (No keeps it on the primary — avoids model-swap reloads)[/]", defaultValue: false);

                if (enableCritique)
                {
                    var pick = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold]Select critique model:[/]")
                            .AddChoices(others.Select(FormatModelChoice)));
                    critique = StripSize(pick);
                }
            }
        }

        var comparisons = new List<string>();
        if (config.DevMode && config.EnableModelComparison)
        {
            var enableCompare = AnsiConsole.Confirm(
                "[dim]Enable comparison mode?[/]", defaultValue: false);

            if (enableCompare)
            {
                var others = models.Where(m => m.Name != primary && m.Name != critique).ToList();
                if (others.Count > 0)
                {
                    var picks = AnsiConsole.Prompt(
                        new MultiSelectionPrompt<string>()
                            .Title("[bold]Select comparison model(s):[/]")
                            .NotRequired()
                            .InstructionsText("[grey](space to toggle, enter to confirm — pick as many as you like)[/]")
                            .AddChoices(others.Select(FormatModelChoice)));
                    comparisons = picks.Select(StripSize).ToList();
                }
            }
        }

        AnsiConsole.WriteLine();
        return (primary, critique, comparisons);
    }

    private static string FormatModelChoice(OllamaModel m)
    {
        var gb = m.Size / 1_073_741_824.0;
        var sizeLabel = gb >= 0.1 ? $"  ({gb:F1} GB)" : "";
        return $"{m.Name}{sizeLabel}";
    }

    // Strip the "  (X.X GB)" suffix added for display
    private static string StripSize(string choice)
        => choice.Contains("  (") ? choice[..choice.IndexOf("  (")] : choice;
}
