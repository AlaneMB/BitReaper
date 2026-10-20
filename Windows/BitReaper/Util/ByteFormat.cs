using System.Globalization;

namespace BitReaper.Util;

public static class ByteFormat
{
    /// <summary>Set from the preferences; 1 KB = 1000 bytes when true, 1 KiB = 1024 bytes otherwise.</summary>
    public static bool DecimalUnits { get; set; } = true;

    private static readonly string[] Decimal = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB" };
    private static readonly string[] Binary = { "bytes", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };

    /// <summary>Splits a size into a number and a unit, e.g. ("261.5", "GB").</summary>
    public static (string Number, string Unit) Parts(long bytes, bool? decimalUnits = null)
    {
        bool useDecimal = decimalUnits ?? DecimalUnits;
        double unitBase = useDecimal ? 1000 : 1024;
        var units = useDecimal ? Decimal : Binary;
        double value = Math.Max(bytes, 0);
        int index = 0;
        while (value >= unitBase && index < units.Length - 1)
        {
            value /= unitBase;
            index++;
        }
        if (index == 0) return (Count(bytes), bytes == 1 ? "byte" : "bytes");
        return (value.ToString("#,##0.#", CultureInfo.CurrentCulture), units[index]);
    }

    public static string Size(long bytes, bool? decimalUnits = null)
    {
        var (number, unit) = Parts(bytes, decimalUnits);
        return $"{number} {unit}";
    }

    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Percent(long part, long whole)
    {
        if (whole <= 0) return "—";
        double fraction = (double)part / whole * 100;
        if (part > 0 && fraction < 0.1) return "<0.1 %";
        return fraction.ToString(fraction >= 10 ? "0" : "0.0", CultureInfo.CurrentCulture) + " %";
    }

    public static string Date(DateTime local)
    {
        var today = DateTime.Today;
        if (local.Date == today) return "Today";
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        return local.ToString("d", CultureInfo.CurrentCulture);
    }

    public static string DateAndTime(DateTime local) => local.ToString("g", CultureInfo.CurrentCulture);
}
