namespace MeridianAgent.Infrastructure.Data;

/// <summary>
/// Resolves the sample-data directory. Works when running from source (`dotnet run`, where the
/// samples are copied next to the assembly) and in Docker (where they live under the content root).
/// A directory that actually contains files wins over an empty one, so stale empty folders created
/// by earlier runs never shadow the real data.
/// </summary>
public static class DataPaths
{
    public static string Resolve(string configured)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), configured),
            Path.Combine(AppContext.BaseDirectory, configured),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories).Any())
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
