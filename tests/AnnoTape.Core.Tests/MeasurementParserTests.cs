using AnnoTape.Core.Measurements;
using AnnoTape.Core.Models;

namespace AnnoTape.Core.Tests;

[TestClass]
public sealed class MeasurementParserTests
{
    [TestMethod]
    [DataRow("100", MeasurementUnit.Millimetres, "100")]
    [DataRow("2.5", MeasurementUnit.Centimetres, "25.0")]
    [DataRow("1.2", MeasurementUnit.Metres, "1200.0")]
    [DataRow("2", MeasurementUnit.Inches, "50.8")]
    [DataRow("5' 7 1/2\"", MeasurementUnit.FeetAndInches, "1714.50")]
    public void ParsesSupportedUnits(string text, MeasurementUnit unit, string expectedMillimetres)
    {
        Assert.IsTrue(MeasurementParser.TryParse(text, unit, out var result));
        Assert.AreEqual(decimal.Parse(expectedMillimetres, System.Globalization.CultureInfo.InvariantCulture), result.Millimetres);
        Assert.AreEqual(text, result.DisplayText);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-1")]
    [DataRow("five")]
    public void RejectsInvalidValues(string text) =>
        Assert.IsFalse(MeasurementParser.TryParse(text, MeasurementUnit.Millimetres, out _));

    [TestMethod]
    public void RejectsTwelveOrMoreResidualInches() =>
        Assert.IsFalse(MeasurementParser.TryParse("3' 12\"", MeasurementUnit.FeetAndInches, out _));

    [TestMethod]
    public void AnnotationColoursAreCanonicalAndRejectCssInjection()
    {
        Assert.AreEqual("#AABBCC", AnnotationColours.Normalize("#abc"));
        Assert.AreEqual("#FF453A", AnnotationColours.Normalize(null, AnnotationStyle.Red));
        Assert.AreEqual(AnnotationColours.Copper, AnnotationColours.Normalize("red;display:none"));
    }

    [TestMethod]
    [DataRow("1000", MeasurementUnit.Millimetres, "1000")]
    [DataRow("1000", MeasurementUnit.Centimetres, "100")]
    [DataRow("1000", MeasurementUnit.Metres, "1")]
    [DataRow("25.4", MeasurementUnit.Inches, "1")]
    [DataRow("1714.5", MeasurementUnit.FeetAndInches, "5' 7.5\"")]
    public void FormatsStoredMillimetresInTheGlobalUnit(string millimetres, MeasurementUnit unit, string expected) =>
        Assert.AreEqual(expected, MeasurementParser.Format(
            decimal.Parse(millimetres, System.Globalization.CultureInfo.InvariantCulture), unit));
}
