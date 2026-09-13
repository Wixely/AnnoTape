using System.Globalization;
using System.Text.RegularExpressions;
using AnnoTape.Core.Models;

namespace AnnoTape.Core.Measurements;

public readonly record struct MeasurementValue(string DisplayText, decimal Millimetres, MeasurementUnit Unit);

public static partial class MeasurementParser
{
    private const decimal MillimetresPerInch = 25.4m;

    public static bool TryParse(string text, MeasurementUnit unit, out MeasurementValue value)
    {
        value = default;
        var display = text.Trim();
        if (display.Length == 0) return false;

        if (unit == MeasurementUnit.FeetAndInches)
        {
            if (!TryParseFeetAndInches(display, out var inches) || inches < 0) return false;
            value = new(display, inches * MillimetresPerInch, unit);
            return true;
        }

        if (!decimal.TryParse(display, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) &&
            !decimal.TryParse(display, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return false;
        if (amount < 0) return false;

        var millimetres = unit switch
        {
            MeasurementUnit.Millimetres => amount,
            MeasurementUnit.Centimetres => amount * 10m,
            MeasurementUnit.Metres => amount * 1000m,
            MeasurementUnit.Inches => amount * MillimetresPerInch,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        value = new(display, millimetres, unit);
        return true;
    }

    private static bool TryParseFeetAndInches(string text, out decimal totalInches)
    {
        totalInches = 0;
        var match = FeetInchesRegex().Match(text);
        if (!match.Success) return false;
        if (!decimal.TryParse(match.Groups["feet"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var feet))
            return false;

        decimal inches = 0;
        if (match.Groups["inches"].Success && !decimal.TryParse(
                match.Groups["inches"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out inches))
            return false;

        if (match.Groups["num"].Success)
        {
            var numerator = decimal.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
            var denominator = decimal.Parse(match.Groups["den"].Value, CultureInfo.InvariantCulture);
            if (denominator == 0) return false;
            inches += numerator / denominator;
        }

        if (inches >= 12m) return false;
        totalInches = feet * 12m + inches;
        return true;
    }

    public static string Suffix(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimetres => "mm",
        MeasurementUnit.Centimetres => "cm",
        MeasurementUnit.Metres => "m",
        MeasurementUnit.Inches => "in",
        MeasurementUnit.FeetAndInches => "ft/in",
        _ => ""
    };

    public static string Format(decimal millimetres, MeasurementUnit unit, int? precision = null)
    {
        if (unit == MeasurementUnit.FeetAndInches)
        {
            var totalInches = millimetres / MillimetresPerInch;
            var feet = decimal.ToInt32(decimal.Floor(totalInches / 12m));
            var inches = decimal.Round(totalInches - feet * 12m, precision ?? 2, MidpointRounding.AwayFromZero);
            if (inches >= 12m)
            {
                feet++;
                inches = 0;
            }
            return $"{feet}' {FormatNumber(inches, precision ?? 2)}\"";
        }

        var amount = unit switch
        {
            MeasurementUnit.Millimetres => millimetres,
            MeasurementUnit.Centimetres => millimetres / 10m,
            MeasurementUnit.Metres => millimetres / 1000m,
            MeasurementUnit.Inches => millimetres / MillimetresPerInch,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        var defaultPrecision = unit switch
        {
            MeasurementUnit.Millimetres => 2,
            MeasurementUnit.Centimetres => 2,
            MeasurementUnit.Metres => 3,
            MeasurementUnit.Inches => 2,
            _ => 2
        };
        return FormatNumber(decimal.Round(amount, precision ?? defaultPrecision, MidpointRounding.AwayFromZero), precision ?? defaultPrecision);
    }

    private static string FormatNumber(decimal value, int precision) =>
        value.ToString(precision <= 0 ? "0" : $"0.{new string('#', precision)}", CultureInfo.InvariantCulture);

    [GeneratedRegex("^\\s*(?<feet>\\d+)\\s*(?:'|ft)\\s*(?:(?<inches>\\d+(?:\\.\\d+)?)\\s*)?(?:(?<num>\\d+)\\s*/\\s*(?<den>\\d+)\\s*)?(?:\"|in)?\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeetInchesRegex();
}
