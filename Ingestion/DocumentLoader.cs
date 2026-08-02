namespace NPCRAGSystem.Ingestion;

public class DocumentLoader
{
	public Dictionary<string, string> LoadFromDirectory(string directoryPath, bool stripMarkdown = true)
	{
		if (!Directory.Exists(directoryPath))
			throw new DirectoryNotFoundException($"Data directory not found: { directoryPath }");

		var documents = new Dictionary<string, string>();
		var files = Directory.GetFiles(directoryPath, "*.txt", SearchOption.AllDirectories);

		if (files.Length == 0) 
			Console.WriteLine($"Warning: no .txt files found in {directoryPath}");

		foreach ( var file in files)
		{
			var content = File.ReadAllText(file).Trim();

			if (stripMarkdown)
				content = MarkdownStripper.Strip(content);

			if (!string.IsNullOrWhiteSpace(content))
			{
				documents[Path.GetFileName(file)] = content;
				Console.WriteLine($" Loaded: {Path.GetFileName(file)} ({content.Length:N0} chars)");
			}
		}

		return documents;


	}

}