using System.Text.Json;

namespace Atlas.Stubs.Scenarios;

/// <summary>
/// Holds the scenarios file and reloads it when it changes, so that a reviewer can change a
/// behaviour without restarting the stand-in.
/// </summary>
public sealed class ScenarioStore
{
    private static readonly JsonSerializerOptions _Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _Path;
    private readonly ILogger<ScenarioStore> _Logger;
    private readonly object _Lock = new();
    private ScenarioFile _Current;
    private DateTime _LoadedFrom;

    /// <summary>Loads the scenarios file.</summary>
    /// <param name="path">The scenarios file. Must exist and be valid.</param>
    /// <param name="logger">Where reload problems are reported.</param>
    /// <exception cref="InvalidOperationException">The file is missing or invalid.</exception>
    public ScenarioStore(string path, ILogger<ScenarioStore> logger)
    {
        _Path = path;
        _Logger = logger;
        (_Current, _LoadedFrom) = Load();
    }

    /// <summary>The current scenarios, reloaded first if the file has changed.</summary>
    public ScenarioFile Current
    {
        get
        {
            lock (_Lock)
            {
                if (File.GetLastWriteTimeUtc(_Path) != _LoadedFrom)
                {
                    Reload();
                }

                return _Current;
            }
        }
    }

    private void Reload()
    {
        try
        {
            (_Current, _LoadedFrom) = Load();
            _Logger.LogInformation("Reloaded scenarios from {Path}", _Path);
        }
        catch (InvalidOperationException ex)
        {
            // Keep the last good scenarios; an editor may have saved a half-written file.
            _LoadedFrom = File.GetLastWriteTimeUtc(_Path);
            _Logger.LogWarning(ex, "Scenarios file is invalid; keeping the previous version");
        }
    }

    private (ScenarioFile File, DateTime WriteTime) Load()
    {
        try
        {
            var writeTime = File.GetLastWriteTimeUtc(_Path);
            using var stream = File.OpenRead(_Path);
            var file = JsonSerializer.Deserialize<ScenarioFile>(stream, _Json)
                       ?? throw new InvalidOperationException("The scenarios file is empty.");
            return (file, writeTime);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidOperationException($"Cannot read scenarios from '{_Path}'.", ex);
        }
    }
}
