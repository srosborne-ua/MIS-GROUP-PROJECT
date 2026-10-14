namespace API.Services;

// Loads KEY=VALUE lines from the project's .env file into the process environment
// so the Anthropic SDK can read ANTHROPIC_API_KEY. Variables already set in the
// environment win. Never log the values.
public static class DotEnv
{
    public static void Load()
    {
        var path = Find(Directory.GetCurrentDirectory()) ?? Find(AppContext.BaseDirectory);
        if (path is null) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith("export ")) trimmed = trimmed[7..].TrimStart();

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];

            if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        }
    }

    // .env lives in the repository root, one level above API/. Walk up a few levels
    // so it is found whether the API runs from the repo root, API/, or bin/.
    private static string? Find(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
