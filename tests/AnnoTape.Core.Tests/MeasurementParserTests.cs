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
}
