using System.Globalization;

namespace Hospitality.Pos.Desktop.Services;

/// <summary>Indian-rupee formatting for paise-based amounts.</summary>
public static class Inr
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-IN");

    public static string Format(long paise)
    {
        var rupees = paise / 100m;
        var format = paise % 100 == 0 ? "C0" : "C2";
        return rupees.ToString(format, Culture).Replace("₹ ", "₹");
    }

    public static string Format(decimal rupees) =>
        rupees.ToString(rupees % 1 == 0 ? "C0" : "C2", Culture).Replace("₹ ", "₹");

    public static long ToPaise(decimal rupees) => (long)Math.Round(rupees * 100m, MidpointRounding.AwayFromZero);

    public static decimal ToRupees(long paise) => paise / 100m;
}
