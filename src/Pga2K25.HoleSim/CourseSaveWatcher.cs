using System.Security.Cryptography;

namespace Pga2K25.HoleSim;

/// <summary>Which save of a course file a result came from.</summary>
public sealed record FileStamp(string Path, DateTime ModifiedUtc, long Length, string Sha256)
{
    public static FileStamp Read(string path)
    {
        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new FileStamp(path, info.LastWriteTimeUtc, info.Length, hash);
    }

    public string ShortHash => Sha256.Length >= 10 ? Sha256[..10] : Sha256;
}

public enum SaveCheckState { Unchanged, Settling, NewSave, Missing }

public sealed record SaveCheck(SaveCheckState State, FileStamp? Stamp = null);

/// <summary>
/// Notices when the designer saves a course again, so the simulator can re-read it in place instead of the user
/// backing out of the section. It only reports: it never reloads or runs anything by itself. A new save is reported
/// once its size and time have held still across two checks (the game writes the file in more than one step), and
/// only when its contents differ from what is loaded. <see cref="Check"/> only stats the file until a change settles,
/// so polling it every couple of seconds is cheap.
/// </summary>
public sealed class CourseSaveWatcher
{
    (DateTime Time, long Length)? _seen;
    FileStamp? _pending, _dismissed;

    public CourseSaveWatcher(FileStamp loaded) { Loaded = loaded; }

    public FileStamp Loaded { get; private set; }

    public SaveCheck Check()
    {
        var info = new FileInfo(Loaded.Path);
        if (!info.Exists) return new SaveCheck(SaveCheckState.Missing);
        var now = (info.LastWriteTimeUtc, info.Length);
        if (now == (Loaded.ModifiedUtc, Loaded.Length)) { _seen = null; return new SaveCheck(SaveCheckState.Unchanged); }
        if (_pending is { } p && now == (p.ModifiedUtc, p.Length)) return new SaveCheck(SaveCheckState.NewSave, p);
        if (_dismissed is { } d && now == (d.ModifiedUtc, d.Length)) return new SaveCheck(SaveCheckState.Unchanged);
        if (_seen != now) { _seen = now; return new SaveCheck(SaveCheckState.Settling); }

        FileStamp stamp;
        try { stamp = FileStamp.Read(Loaded.Path); }
        catch (IOException) { return new SaveCheck(SaveCheckState.Settling); }
        catch (UnauthorizedAccessException) { return new SaveCheck(SaveCheckState.Settling); }
        if (stamp.ModifiedUtc != now.LastWriteTimeUtc || stamp.Length != now.Length) { _seen = null; return new SaveCheck(SaveCheckState.Settling); }
        if (stamp.Sha256 == Loaded.Sha256)
        {
            Loaded = stamp;   // touched but identical: nothing to report
            return new SaveCheck(SaveCheckState.Unchanged);
        }
        _pending = stamp;
        return new SaveCheck(SaveCheckState.NewSave, stamp);
    }

    /// <summary>The new save was loaded.</summary>
    public void Accept(FileStamp stamp) { Loaded = stamp; _pending = null; _dismissed = null; _seen = null; }

    /// <summary>"Not now": stop reporting this save until the file changes again.</summary>
    public void Dismiss() { _dismissed = _pending; _pending = null; }
}
