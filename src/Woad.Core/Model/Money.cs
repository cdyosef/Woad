namespace Woad.Core.Model;

/// <summary>
/// Coins: gold, silver and copper. A purse is kept as a count of copper (10 copper a silver, 10
/// silver a gold); prices and treasure values are mostly set in whole gold and converted.
/// </summary>
public static class Money
{
    public const int CopperPerSilver = 10;

    public const int CopperPerGold = 100;

    /// <summary>Whole gold, in copper.</summary>
    public static int Gold(int gold) => gold * CopperPerGold;

    /// <summary>Silver, in copper.</summary>
    public static int Silver(int silver) => silver * CopperPerSilver;

    /// <summary>A sum written short, the coins it has: "2g 3s 5c", "4s", "0g".</summary>
    public static string Short(int copper)
    {
        if (copper == 0)
        {
            return "0g";
        }

        string sign = copper < 0 ? "-" : "";
        copper = Math.Abs(copper);
        var parts = new List<string>();
        if (copper / CopperPerGold > 0)
        {
            parts.Add($"{copper / CopperPerGold}g");
        }

        if (copper / CopperPerSilver % 10 > 0)
        {
            parts.Add($"{copper / CopperPerSilver % 10}s");
        }

        if (copper % CopperPerSilver > 0)
        {
            parts.Add($"{copper % CopperPerSilver}c");
        }

        return sign + string.Join(" ", parts);
    }

    /// <summary>A sum written out: "2 gold, 3 silver and 5 copper", "4 silver", "no coin".</summary>
    public static string Long(int copper)
    {
        if (copper == 0)
        {
            return "no coin";
        }

        copper = Math.Abs(copper);
        var parts = new List<string>();
        if (copper / CopperPerGold > 0)
        {
            parts.Add($"{copper / CopperPerGold} gold");
        }

        if (copper / CopperPerSilver % 10 > 0)
        {
            parts.Add($"{copper / CopperPerSilver % 10} silver");
        }

        if (copper % CopperPerSilver > 0)
        {
            parts.Add($"{copper % CopperPerSilver} copper");
        }

        return parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }
}
