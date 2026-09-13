using AnnoTape.Core.Editing;
using AnnoTape.Core.Geometry;
using AnnoTape.Core.Models;

namespace AnnoTape.Core.Tests;

[TestClass]
public sealed class GeometryAndHistoryTests
{
    [TestMethod]
    public void ContainFitAndCoordinateRoundTripRemainStable()
    {
        var frame = ImageGeometry.FitContain(4000, 3000, new PixelRect(0, 64, 400, 546));
        Assert.AreEqual(400d, frame.Width, 0.0001);
        Assert.AreEqual(300d, frame.Height, 0.0001);
        var source = new NormalizedPoint(0.23, 0.81);
        var pixels = ImageGeometry.ToPixels(source, frame);
        var roundTrip = ImageGeometry.ToNormalized(pixels.X, pixels.Y, frame);
        Assert.AreEqual(source.X, roundTrip.X, 0.000001);
        Assert.AreEqual(source.Y, roundTrip.Y, 0.000001);
    }

    [TestMethod]
    public void CommandsUndoAndRedoWithoutLosingStableIdentity()
    {
        var document = new PhotoDocument();
        var annotation = new DimensionAnnotation();
        var history = new EditorHistory();
        history.Apply(document, new AddAnnotationCommand(annotation));
        Assert.HasCount(1, document.Annotations);
        Assert.IsTrue(history.Undo(document));
        Assert.IsEmpty(document.Annotations);
        Assert.IsTrue(history.Redo(document));
        Assert.AreEqual(annotation.Id, document.Annotations.Single().Id);
    }

    [TestMethod]
    public void ImageWideEditsUndoAllAnnotationsAsOneOperation()
    {
        var first = new DimensionAnnotation { DisplayText = "1000", Unit = MeasurementUnit.Millimetres };
        var second = new DimensionAnnotation { DisplayText = "2000", Unit = MeasurementUnit.Millimetres };
        var document = new PhotoDocument { Annotations = { first, second } };
        var changed = document.Annotations.Select(item => item.Copy()).ToArray();
        changed[0].DisplayText = "1";
        changed[0].Unit = MeasurementUnit.Metres;
        changed[1].DisplayText = "2";
        changed[1].Unit = MeasurementUnit.Metres;
        var history = new EditorHistory();

        history.Apply(document, new ReplaceAnnotationsCommand(document.Annotations, changed));
        Assert.IsTrue(document.Annotations.All(item => item.Unit == MeasurementUnit.Metres));
        Assert.IsTrue(history.Undo(document));
        Assert.IsTrue(document.Annotations.All(item => item.Unit == MeasurementUnit.Millimetres));
    }

    [TestMethod]
    public void HitTestingUsesScreenAspectRatio()
    {
        var distance = ImageGeometry.DistanceToSegmentPixels(
            new NormalizedPoint(0.5, 0.52),
            new NormalizedPoint(0.1, 0.5),
            new NormalizedPoint(0.9, 0.5),
            1000,
            500);
        Assert.AreEqual(10d, distance, 0.001);
    }
}
