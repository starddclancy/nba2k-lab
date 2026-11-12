using System.Globalization;
using System.Text.RegularExpressions;

namespace Nba2k27Editor.Core.Model;

/// <summary>Rating, bitfield and unit conversions lifted from the Python editor.</summary>
public static class Conversions
{
    public const int RatingMin = 25;
    public const int RatingMaxDisplay = 99;
    public const int RatingMaxTrue = 110;
    public const int YearBase = 1900;
    public const int HeightUnitScale = 254;
    public const int HeightMinInches = 48;
    public const int HeightMaxInches = 120;
    public const double WeightMinPounds = 100.0;
    public const double WeightMaxPounds = 350.0;
    public const double PoundsToKilograms = 0.45359237;
    public const int BoxPlusMinusBias = 0x8000;
    public const int BoxPlusMinusNegativeThreshold = 0x4000;

    private static readonly HashSet<string> YearFieldAllowlist = new(StringComparer.Ordinal)
    {
        "DRAFTEDYEAR", "HISTORICYEAR",
    };

    private static readonly Regex NonNumeric = new("[^0-9.-]", RegexOptions.Compiled);
    private static readonly Regex IdPrefixed = new(@"^\s*\[(\d+)\]", RegexOptions.Compiled);
    private static readonly Regex YearKeyCleaner = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    public static int ToInt(object? value) => unchecked((int)ToLong(value));

    /// <summary>64-bit variant; process addresses must never be narrowed to int.</summary>
    public static long ToLong(object? value)
    {
        switch (value)
        {
            case null:
                return 0;
            case string text:
                text = text.Trim();
                if (text.Length == 0)
                {
                    return 0;
                }
                bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                return long.TryParse(
                    hex ? text[2..] : text,
                    hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed) ? parsed : 0;
            case double number:
                return (long)number;
            case float single:
                return (long)single;
            case decimal money:
                return (long)money;
            case bool flag:
                return flag ? 1 : 0;
            default:
                if (value is IConvertible convertible)
                {
                    try
                    {
                        return convertible.ToInt64(CultureInfo.InvariantCulture);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
                return 0;
        }
    }

    public static double ToDouble(object? value, double fallback = 0.0)
    {
        switch (value)
        {
            case null:
                return fallback;
            case string text:
                return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : fallback;
            default:
                try
                {
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return fallback;
                }
        }
    }

    public static int? ParseIdPrefixedOption(object? value)
    {
        var match = IdPrefixed.Match(value?.ToString() ?? string.Empty);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    public static bool IsYearOffsetField(string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return false;
        }
        var key = YearKeyCleaner.Replace(fieldName, string.Empty).ToUpperInvariant();
        return YearFieldAllowlist.Contains(key);
    }

    public static int ConvertRawToYear(int raw, int baseYear = YearBase)
    {
        if (raw >= baseYear)
        {
            return raw;
        }
        if (raw < 0)
        {
            raw = 0;
        }
        return baseYear + raw;
    }

    public static int ConvertYearToRaw(int year, int baseYear = YearBase)
    {
        if (year >= 0 && year < baseYear)
        {
            return year;
        }
        int raw = year - baseYear;
        return raw < 0 ? 0 : raw;
    }

    public static int ConvertRawToRating(int raw, int length)
    {
        int maxRaw = (1 << length) - 1;
        if (maxRaw <= 0)
        {
            return RatingMin;
        }
        double ratingTrue = RatingMin + (raw / (double)maxRaw) * (RatingMaxTrue - RatingMin);
        if (ratingTrue < RatingMin)
        {
            ratingTrue = RatingMin;
        }
        else if (ratingTrue > RatingMaxDisplay)
        {
            ratingTrue = RatingMaxDisplay;
        }
        return (int)Math.Round(ratingTrue);
    }

    public static int ConvertRatingToRaw(double rating, int length)
    {
        int maxRaw = (1 << length) - 1;
        if (maxRaw <= 0)
        {
            return 0;
        }
        double value = rating;
        if (value < RatingMin)
        {
            value = RatingMin;
        }
        else if (value > RatingMaxDisplay)
        {
            value = RatingMaxDisplay;
        }
        double fraction = (value - RatingMin) / (RatingMaxTrue - RatingMin);
        if (fraction < 0.0)
        {
            fraction = 0.0;
        }
        else if (fraction > 1.0)
        {
            fraction = 1.0;
        }
        long rawValue = (long)Math.Round(fraction * maxRaw);
        return (int)Math.Clamp(rawValue, 0, maxRaw);
    }

    public static int ConvertPotentialToRaw(double rating, int length, double minimum = 40.0, double maximum = 99.0)
    {
        double clamped = Math.Clamp(rating, minimum, maximum);
        return length > 0 ? ConvertRatingToRaw(clamped, length) : (int)Math.Round(clamped);
    }

    public static int ConvertRawToPotential(int raw, int length, double minimum = 40.0, double maximum = 99.0)
    {
        int rating = length > 0 ? ConvertRawToRating(raw, length) : raw;
        return Math.Clamp(rating, (int)minimum, (int)maximum);
    }

    public static int ConvertRawToBodyScaleDisplay(object? raw, int length = 0)
    {
        double value = ToDouble(raw);
        return ConvertTendencyRawToRating((int)Math.Round(value * 50.0), length);
    }

    public static double ConvertBodyScaleDisplayToRaw(object? displayValue, int length = 0)
    {
        return ConvertRatingToTendencyRaw(ToDouble(displayValue), length) / 50.0;
    }

    public static int ConvertRawToInjuryDurationDays(int raw, int maximumDays = 450)
    {
        int ticks = raw & 0xFFFFF;
        return Math.Clamp(ticks / 1440, 0, maximumDays);
    }

    public static int ConvertInjuryDurationDaysToRaw(double days, int maximumDays = 450)
    {
        int value = (int)Math.Round(days);
        value = Math.Clamp(value, 0, maximumDays);
        return value * 1440;
    }

    public static double? NormalizeWeightValue(object? value)
    {
        double parsed;
        switch (value)
        {
            case null:
                return null;
            case double number:
                parsed = number;
                break;
            case float single:
                parsed = single;
                break;
            case int integer:
                parsed = integer;
                break;
            case long wide:
                parsed = wide;
                break;
            default:
                if (!double.TryParse((value.ToString() ?? string.Empty).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    return null;
                }
                break;
        }
        if (parsed < WeightMinPounds)
        {
            return WeightMinPounds;
        }
        if (parsed > WeightMaxPounds)
        {
            return WeightMaxPounds;
        }
        return parsed;
    }

    public static double ConvertPoundsToKilograms(object? pounds) => ToDouble(pounds) * PoundsToKilograms;

    public static double ConvertKilogramsToPounds(object? kilograms) => ToDouble(kilograms) / PoundsToKilograms;

    public static int RawHeightToInches(int rawValue)
    {
        int inches = (int)Math.Round(rawValue / (double)HeightUnitScale);
        return Math.Max(0, inches);
    }

    public static int ClampHeightInches(int inches)
    {
        if (inches < HeightMinInches)
        {
            return HeightMinInches;
        }
        if (inches > HeightMaxInches)
        {
            return HeightMaxInches;
        }
        return inches;
    }

    public static int HeightInchesToRaw(int inches)
    {
        long rawValue = (long)Math.Round(inches * (double)HeightUnitScale);
        return (int)Math.Max(0, rawValue);
    }

    public static string FormatHeightInches(int inches)
    {
        int feet = inches / 12;
        int remainder = inches % 12;
        return $"{feet}'{remainder}\"";
    }

    public static int ConvertTendencyRawToRating(int raw, int length)
    {
        return Math.Clamp(raw, 0, 100);
    }

    public static int ConvertRatingToTendencyRaw(double rating, int length)
    {
        double value = Math.Clamp(rating, 0.0, 100.0);
        return (int)Math.Round(value);
    }

    public static int ConvertRawToBoxPlusMinus(object? raw)
    {
        int value = ToInt(raw);
        return value >= BoxPlusMinusNegativeThreshold ? value - BoxPlusMinusBias : value;
    }

    public static int ConvertBoxPlusMinusToRaw(object? displayValue)
    {
        int value = ToInt(displayValue);
        return value < 0 ? value + BoxPlusMinusBias : value;
    }

    public static (int Low, int High) PlayerNumericBounds(string? categoryName, string? fieldName, int lengthBits)
    {
        var category = (categoryName ?? string.Empty).Trim();
        if (category is "Attributes" or "Durability")
        {
            return (25, 99);
        }
        if (category == "Tendencies")
        {
            return (0, 100);
        }
        int bits = lengthBits > 0 ? lengthBits : 0;
        var field = (fieldName ?? string.Empty).Trim().ToLowerInvariant();
        if (field == "height")
        {
            return (HeightMinInches, HeightMaxInches);
        }
        return (0, bits > 0 ? (1 << bits) - 1 : 0);
    }
}
