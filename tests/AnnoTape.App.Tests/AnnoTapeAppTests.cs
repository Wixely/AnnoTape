using AnnoTape.App;
using AnnoTape.App.Platform;
using AnnoTape.App.Editor;
using AnnoTape.Core.Models;
using CupriFace;
using CupriFace.Dom;
using CupriFace.Diagnostics;
using CupriFace.Interaction;
using SkiaSharp;

namespace AnnoTape.App.Tests;

[TestClass]
public sealed class AnnoTapeAppTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AnnoTape.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task PortableAppCreatesAndRendersAResponsiveDocument()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        using var document = app.CreateDocument();
        using var image = document.RenderToImage(400, 800, app.Background);
        await app.Initialization;
        Assert.IsNull(app.InitializationError);
        Assert.AreEqual(400, image.Width);
        Assert.AreEqual(800, image.Height);
        Assert.Contains("AnnoTape", app.Html);
        Assert.Contains("no inferred measurements", app.Html);
        Assert.Contains("<cupri-toolbar", app.Html);
        Assert.Contains("<cupri-shelf", app.Html);
        Assert.Contains("<cupri-switch", app.Html);
        Assert.Contains("<cupri-color", app.Html);
    }

    [TestMethod]
    [DataRow(320, 720)]
    [DataRow(360, 800)]
    [DataRow(400, 800)]
    [DataRow(800, 400)]
    public async Task CupriDoctorFindsNoMarkupStyleOrLayoutProblems(int width, int height)
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;

        var homeReport = CupriDoctor.Check(app.Html, app.Css, width: width, height: height, model: app.Model);
        Assert.IsTrue(homeReport.IsClean, homeReport.ToString());

        var model = (EditorViewModel)app.Model;
        model.ViewportWidth = width;
        model.ViewportHeight = height;
        model.Page = "editor";
        var editorReport = CupriDoctor.Check(app.Html, app.Css, width: width, height: height, model: app.Model);
        Assert.IsTrue(editorReport.IsClean, editorReport.ToString());

        model.ImageSource = "photo.jpg";
        model.HasSelection = true;
        var selectionReport = CupriDoctor.Check(app.Html, app.Css, width: width, height: height, model: app.Model);
        Assert.IsTrue(selectionReport.IsClean, selectionReport.ToString());

        model.ColourOpen = true;
        var colourPickerReport = CupriDoctor.Check(app.Html, app.Css, width: width, height: height, model: app.Model);
        Assert.IsTrue(colourPickerReport.IsClean, colourPickerReport.ToString());
    }

    [TestMethod]
    public async Task HomeContentIsCentredAndWidthLimitedOnWideScreens()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        using var document = app.CreateDocument();
        using var image = document.RenderToImage(800, 600, app.Background);
        var hero = Find(document.Root, node => node.Element?.ClassList.Contains("hero") == true);
        Assert.IsNotNull(hero);

        var box = HitTesting.ScreenBox(hero);
        Assert.AreEqual(520f, box.W, 0.1f, document.DumpTree());
        Assert.AreEqual(400f, box.X + box.W / 2, 0.1f, document.DumpTree());
    }

    [TestMethod]
    [DataRow(320, 720)]
    [DataRow(400, 800)]
    [DataRow(800, 400)]
    public async Task CupriDebugBoxesShowActionButtonContentsAreCentred(int width, int height)
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        using (var home = app.CreateDocument())
        using (home.RenderToImage(width, height, app.Background))
            AssertActionButtonsCentred(home);

        var model = (EditorViewModel)app.Model;
        model.ViewportWidth = width;
        model.ViewportHeight = height;
        model.Page = "editor";
        model.ImageSource = "missing-test-photo.png";
        model.HasSelection = true;
        model.ExportShelfOpen = true;
        using var editor = app.CreateDocument();
        using (editor.RenderToImage(width, height, app.Background))
            AssertActionButtonsCentred(editor);
    }

    [TestMethod]
    public async Task ExportButtonOpensTheCupriFaceShelf()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ImageSource = "test-photo.png";

        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }
        var exportButton = Find(document.Root, node => node.Element?.ClassList.Contains("open-export") == true);
        Assert.IsNotNull(exportButton);

        var box = HitTesting.ScreenBox(exportButton);
        document.DispatchClick(box.X + box.W / 2, box.Y + box.H / 2);
        using var image = document.RenderToImage(400, 800, app.Background);

        Assert.IsNull(app.InitializationError);
        Assert.AreEqual(400, image.Width);
        Assert.AreEqual(800, image.Height);
        Assert.IsTrue(model.ExportShelfOpen);
        Assert.IsNotNull(Find(document.Root, node => node.Element?.ClassList.Contains("cupri-shelf-panel") == true));
    }

    [TestMethod]
    public void AnnotationLineStartsAtTheFirstEndpointAndAccountsForImageAspectRatio()
    {
        var annotation = new DimensionAnnotation { Start = new(0.2, 0.2), End = new(0.2, 0.8) };
        var view = AnnotationViewModel.From(annotation, selected: true, imageWidth: 400, imageHeight: 300);
        Assert.Contains("width:45%", view.LineStyle);
        Assert.Contains("left:20%", view.LineStyle);
        Assert.Contains("top:20%", view.LineStyle);
        Assert.Contains("rotate(90deg)", view.LineStyle);
        Assert.Contains("width:88px", view.LabelStyle);
        Assert.Contains("translate(-44px,-22px)", view.LabelStyle);
    }

    [TestMethod]
    public void AutomaticLabelIsCentredExceptWhenTheLineIsTooShort()
    {
        var model = new EditorViewModel
        {
            ViewportWidth = 400,
            ViewportHeight = 800,
            SourceWidth = 400,
            SourceHeight = 300,
            ImageSource = "photo.jpg"
        };

        var leftToRight = model.LabelAnchorFor(new(0.2, 0.5), new(0.8, 0.5));
        var rightToLeft = model.LabelAnchorFor(new(0.8, 0.5), new(0.2, 0.5));
        var shortLeftToRight = model.LabelAnchorFor(new(0.45, 0.5), new(0.55, 0.5));
        var shortRightToLeft = model.LabelAnchorFor(new(0.55, 0.5), new(0.45, 0.5));

        Assert.AreEqual(leftToRight.X, rightToLeft.X, 0.001);
        Assert.AreEqual(leftToRight.Y, rightToLeft.Y, 0.001);
        Assert.AreEqual(0.5, leftToRight.X, 0.001);
        Assert.AreEqual(0.5, leftToRight.Y, 0.001);
        Assert.AreEqual(shortLeftToRight.X, shortRightToLeft.X, 0.001);
        Assert.AreEqual(shortLeftToRight.Y, shortRightToLeft.Y, 0.001);
        Assert.IsLessThan(0.5, shortLeftToRight.Y);
        Assert.IsTrue(model.IsNearAutomaticLabelAnchor(shortLeftToRight, new(0.45, 0.5), new(0.55, 0.5)));
        Assert.IsFalse(model.IsNearAutomaticLabelAnchor(new(0.5, 0.8), new(0.45, 0.5), new(0.55, 0.5)));
    }

    [TestMethod]
    public void MeasurementRequiresARealScreenDistanceDrag()
    {
        var model = new EditorViewModel
        {
            ViewportWidth = 400,
            ViewportHeight = 800,
            SourceWidth = 400,
            SourceHeight = 300,
            ImageSource = "photo.jpg"
        };

        Assert.IsFalse(model.IsMeasurementDrag(new(0.5, 0.5), new(0.5, 0.5)));
        Assert.IsFalse(model.IsMeasurementDrag(new(0.5, 0.5), new(0.54, 0.5)));
        Assert.IsTrue(model.IsMeasurementDrag(new(0.5, 0.5), new(0.6, 0.5)));
    }

    [TestMethod]
    public void EnabledAngleSnapUsesOneOfSixteenScreenSpaceDirections()
    {
        var model = new EditorViewModel
        {
            ViewportWidth = 400,
            ViewportHeight = 800,
            SourceWidth = 400,
            SourceHeight = 300,
            ImageSource = "photo.jpg",
            SnapEnabled = true
        };

        var origin = new NormalizedPoint(0.5, 0.5);
        var snapped = model.ApplyAngleSnap(origin, new(0.7, 0.56));
        var frame = model.BaseImageRect;
        var angle = Math.Atan2((snapped.Y - origin.Y) * frame.Height, (snapped.X - origin.X) * frame.Width) * 180 / Math.PI;

        Assert.AreEqual(22.5, angle, 0.001);
    }

    [TestMethod]
    public void ArbitraryColourIsAppliedToLineMarkersAndLabel()
    {
        var annotation = new DimensionAnnotation { ColourHex = "#12abef" };
        var view = AnnotationViewModel.From(annotation, selected: true, imageWidth: 400, imageHeight: 300);

        Assert.Contains("background:#12ABEF", view.LineStyle);
        Assert.AreEqual("background:#12ABEF", view.ColourStyle);
        Assert.AreEqual("background:#12ABEF", view.MarkerStyle);
        Assert.Contains("color:#12ABEF", view.LabelStyle);
        Assert.Contains("border-color:#12ABEF", view.LabelStyle);
    }

    [TestMethod]
    public async Task RenderedAnnotationLineConnectsItsEndpoints()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ViewportWidth = 400;
        model.ViewportHeight = 800;
        model.SourceWidth = 400;
        model.SourceHeight = 300;
        model.ImageSource = "missing-test-photo.png";
        model.HasSelection = true;
        var annotation = new DimensionAnnotation
        {
            Start = new(0.2, 0.2),
            End = new(0.2, 0.8),
            LabelAnchor = new(0.7, 0.5),
            Style = AnnotationStyle.Copper
        };
        model.AnnotationViews = [AnnotationViewModel.From(annotation, selected: true, 400, 300)];

        using var document = app.CreateDocument();
        using var image = document.RenderToImage(400, 800, app.Background);
        using var bitmap = SKBitmap.FromImage(image);
        var frame = model.BaseImageRect;
        var lineX = (int)Math.Round(frame.X + annotation.Start.X * frame.Width);
        var lineY = (int)Math.Round(frame.Y + 0.5 * frame.Height);
        var pixel = bitmap.GetPixel(lineX, lineY);
        var handlePixel = bitmap.GetPixel(
            (int)Math.Round(frame.X + annotation.Start.X * frame.Width + 3),
            (int)Math.Round(frame.Y + annotation.Start.Y * frame.Height));

        Assert.AreEqual((byte)184, pixel.Red, 8, document.DumpTree());
        Assert.AreEqual((byte)115, pixel.Green, 8, document.DumpTree());
        Assert.AreEqual((byte)51, pixel.Blue, 8, document.DumpTree());
        Assert.AreEqual((byte)184, handlePixel.Red, 8, document.DumpTree());
        Assert.AreEqual((byte)115, handlePixel.Green, 8, document.DumpTree());
        Assert.AreEqual((byte)51, handlePixel.Blue, 8, document.DumpTree());
    }

    [TestMethod]
    public async Task EachRenderedLabelRemainsIndependentlyHittable()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ViewportWidth = 400;
        model.ViewportHeight = 800;
        model.SourceWidth = 400;
        model.SourceHeight = 300;
        model.ImageSource = "missing-test-photo.png";
        model.HasSelection = true;
        var first = new DimensionAnnotation { LabelAnchor = new(0.25, 0.25) };
        var second = new DimensionAnnotation { LabelAnchor = new(0.75, 0.75) };
        model.AnnotationViews =
        [
            AnnotationViewModel.From(first, selected: false, 400, 300),
            AnnotationViewModel.From(second, selected: true, 400, 300)
        ];

        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }
        var frame = model.BaseImageRect;
        var firstHit = document.HitTest(
            (float)(frame.X + first.LabelAnchor.X * frame.Width),
            (float)(frame.Y + first.LabelAnchor.Y * frame.Height));
        var secondHit = document.HitTest(
            (float)(frame.X + second.LabelAnchor.X * frame.Width),
            (float)(frame.Y + second.LabelAnchor.Y * frame.Height));

        Assert.AreEqual($"label:{first.Id:D}", AttributeUp(firstHit, "data-drag"), document.DumpTree());
        Assert.AreEqual($"label:{second.Id:D}", AttributeUp(secondHit, "data-drag"), document.DumpTree());
    }

    [TestMethod]
    public void EmptyEditorHidesUnavailableToolsAndUsesTheCompactDock()
    {
        var model = new EditorViewModel();

        Assert.AreEqual("none", model.ToolsDisplay);
        Assert.AreEqual("none", model.SelectionSummaryDisplay);
        Assert.AreEqual("inspector empty-project", model.InspectorClass);

        model.ImageSource = "photo.jpg";

        Assert.AreEqual("flex", model.ToolsDisplay);
        Assert.AreEqual("block", model.SelectionSummaryDisplay);
        Assert.AreEqual("inspector", model.InspectorClass);
    }

    private static RenderNode? Find(RenderNode node, Func<RenderNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
        {
            if (Find(child, match) is { } found) return found;
        }

        return null;
    }

    private static string? AttributeUp(RenderNode? node, string name)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current.Element?.GetAttribute(name) is { } value) return value;
        return null;
    }

    private static void AssertActionButtonsCentred(CupriDocument document)
    {
        var buttons = FindAll(document.Root, node =>
            node.Element?.ClassList.Contains("cupri-button") == true &&
            node.Element.ClassList.Contains("export-option") == false);
        Assert.IsNotEmpty(buttons, document.DumpTree());
        foreach (var button in buttons)
        {
            var leaves = FindAll(button, node => node != button && node.Children.Count == 0)
                .Select(HitTesting.ScreenBox)
                .Where(box => box.W > 0 && box.H > 0)
                .ToArray();
            if (leaves.Length == 0) continue;
            var contentLeft = leaves.Min(box => box.X);
            var contentRight = leaves.Max(box => box.X + box.W);
            var contentTop = leaves.Min(box => box.Y);
            var contentBottom = leaves.Max(box => box.Y + box.H);
            var buttonBox = HitTesting.ScreenBox(button);
            var className = button.Element?.ClassName ?? "button";
            Assert.AreEqual(buttonBox.X + buttonBox.W / 2, (contentLeft + contentRight) / 2, 1f,
                $"Horizontal centring failed for {className}.\n{document.DumpTree()}");
            Assert.AreEqual(buttonBox.Y + buttonBox.H / 2, (contentTop + contentBottom) / 2, 1f,
                $"Vertical centring failed for {className}.\n{document.DumpTree()}");
        }
    }

    private static List<RenderNode> FindAll(RenderNode node, Func<RenderNode, bool> match)
    {
        List<RenderNode> found = [];
        void Walk(RenderNode current)
        {
            if (match(current)) found.Add(current);
            foreach (var child in current.Children) Walk(child);
        }
        Walk(node);
        return found;
    }

    private sealed class FakePlatform(string appDataPath) : IPlatformCapabilities
    {
#pragma warning disable CS0067
        public event Action<PhotoSelection?>? ExternalPhotoCompleted;
#pragma warning restore CS0067
        public string AppDataPath { get; } = appDataPath;
        public Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default) => Task.FromResult<PhotoSelection?>(null);
        public Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default) => Task.FromResult<PhotoSelection?>(null);
        public Task<string> PrepareDisplayImageAsync(string sourcePath, int rotationDegrees, CancellationToken cancellationToken = default) => Task.FromResult(sourcePath);
        public Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
