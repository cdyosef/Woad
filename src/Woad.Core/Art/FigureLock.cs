using System.Security.Cryptography;
using System.Text;

namespace Woad.Core.Art;

/// <summary>
/// A painted figure that ships locked: it can't be picked until its code is entered. The code itself is never kept,
/// only a hash of it (<see cref="Hash"/>), so it can't be read out of the game's files. Figures sharing a code unlock
/// together (a group). A <see cref="Secret"/> one is hidden altogether until then; otherwise it's teased: shown as a
/// dark shape, "???", to find the code for.
/// </summary>
public sealed record FigureLock(string Hash, bool Secret = false)
{
    /// <summary>A code as it's compared: lower case, letters and digits only ("Stone Gate!" is "stonegate").</summary>
    public static string Normalize(string code) =>
        new([.. code.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit)]);

    /// <summary>The hash a code is kept as.</summary>
    public static string HashOf(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("woad-figure-code:" + Normalize(code))));

    /// <summary>A lock for a code (null for a blank one).</summary>
    public static FigureLock? For(string code, bool secret) =>
        Normalize(code).Length == 0 ? null : new FigureLock(HashOf(code), secret);
}
