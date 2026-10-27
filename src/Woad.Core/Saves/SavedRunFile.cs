namespace Woad.Core.Saves;

/// <summary>The one saved Dungeon Run, kept in a file: rewritten after every move, removed when the run ends.</summary>
public sealed class SavedRunFile(string path)
{
    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    /// <summary>The saved run, or null if there's none.</summary>
    /// <exception cref="InvalidDataException">The file isn't a save this version can read.</exception>
    public SavedRun? Load() => Exists ? SavedRun.FromJson(File.ReadAllText(Path)) : null;

    /// <summary>Writes the run beside the old save, then swaps it in, so a crash mid-write never loses both.</summary>
    public void Save(SavedRun run)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, run.ToJson());
        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>
    /// Moves a run that can't be played any more out of the way, beside it as ".old" (".old2" and on
    /// if that's taken), so nothing is lost.
    /// </summary>
    public void SetAside()
    {
        if (Exists)
        {
            string aside = Path + ".old";
            for (int n = 2; File.Exists(aside); n++)
            {
                aside = $"{Path}.old{n}";
            }

            File.Move(Path, aside);
        }
    }

    public void Delete()
    {
        if (Exists)
        {
            File.Delete(Path);
        }
    }
}
