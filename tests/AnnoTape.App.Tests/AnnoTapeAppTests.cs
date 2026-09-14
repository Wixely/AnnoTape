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
    public async Task GeneratedBrandGraphicsRenderInHomeAndEmptyEditor()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        Assert.IsGreaterThan(100_000, app.Icon?.Length ?? 0);

        using (var home = app.CreateDocument())
        {
            Assert.IsTrue(home.Settle(400, 800));
            using var homeImage = home.RenderToImage(400, 800, app.Background);
            var mark = Find(home.Root, node => node.Element?.ClassList.Contains("mark") == true);
            Assert.IsNotNull(mark);
            var markBox = HitTesting.ScreenBox(mark);
            Assert.AreEqual(48, markBox.W, 0.1);
            SaveSnapshotIfRequested(homeImage, "brand-home.png");
        }

        ((EditorViewModel)app.Model).Page = "editor";
        using var editor = app.CreateDocument();
        Assert.IsTrue(editor.Settle(400, 800));
        using var editorImage = editor.RenderToImage(400, 800, app.Background);
        var illustration = Find(editor.Root, node => node.Element?.ClassList.Contains("empty-illustration") == true);
        Assert.IsNotNull(illustration);
        var illustrationBox = HitTesting.ScreenBox(illustration);
        Assert.AreEqual(190, illustrationBox.W, 0.1);
        Assert.AreEqual(112, illustrationBox.H, 0.1);
        SaveSnapshotIfRequested(editorImage, "brand-empty-editor.png");
    }

    [TestMethod]
    public async Task ReadmeDemoRendersARealAlcoveMeasurement()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "artwork", "demo", "room-alcove.png"));
        Assert.IsTrue(File.Exists(sourcePath), sourcePath);

        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ProjectTitle = "Living room alcove";
        model.ProjectLocation = "Reception room";
        model.ProjectNotes = "Joinery opening";
        model.Status = "Measurement saved";
        model.ImageSource = sourcePath;
        model.SourceWidth = 1536;
        model.SourceHeight = 1024;
        model.ViewportWidth = 400;
        model.ViewportHeight = 800;
        model.HasSelection = true;
        model.MeasurementText = "1840";
        model.SelectionSummary = "Selected: 1840 mm";

        var annotation = new DimensionAnnotation
        {
            Start = new(0.25, 0.61),
            End = new(0.75, 0.61),
            LabelAnchor = new(0.5, 0.61),
            DisplayText = "1840",
            NormalizedMillimetres = 1840,
            Unit = MeasurementUnit.Millimetres,
            ColourHex = AnnotationColours.Copper
        };
        var frame = model.BaseImageRect;
        model.AnnotationViews =
        [
            AnnotationViewModel.From(annotation, selected: true, frame.Width, frame.Height)
        ];

        using var document = app.CreateDocument();
        Assert.IsTrue(document.Settle(400, 800));
        using var image = document.RenderToImage(400, 800, app.Background);
        SaveSnapshotIfRequested(image, "annotape-alcove-demo.png");

        Assert.AreEqual(400, image.Width);
        Assert.AreEqual(800, image.Height);
        Assert.IsNotNull(Find(document.Root, node => node.Element?.ClassList.Contains("dimension-line") == true));
        Assert.IsNotNull(Find(document.Root, node => node.Element?.ClassList.Contains("measure-label") == true));
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
        SaveSnapshotIfRequested(image, "export-sheet.png");

        Assert.IsNull(app.InitializationError);
        Assert.AreEqual(400, image.Width);
        Assert.AreEqual(800, image.Height);
        Assert.IsTrue(model.ExportShelfOpen);
        Assert.IsNotNull(Find(document.Root, node => node.Element?.ClassList.Contains("cupri-shelf-panel") == true));

        var fullSizeSwitch = Find(document.Root, node =>
            node.Element?.ClassList.Contains("cupri-switch") == true &&
            AttributeUp(node, "aria-label") == "Export at original image resolution");
        Assert.IsNotNull(fullSizeSwitch);
        var switchBox = HitTesting.ScreenBox(fullSizeSwitch);
        document.DispatchClick(switchBox.X + switchBox.W / 2, switchBox.Y + switchBox.H / 2);
        Assert.IsFalse(model.ExportFullSize);
        Assert.Contains("2048", model.ExportSizeDescription);
        document.Refresh();
        using (document.RenderToImage(400, 800, app.Background)) { }

        var destinationSwitch = Find(document.Root, node =>
            node.Element?.ClassList.Contains("cupri-switch") == true &&
            AttributeUp(node, "aria-label") == "Share export instead of saving to device");
        Assert.IsNotNull(destinationSwitch);
        var destinationBox = HitTesting.ScreenBox(destinationSwitch);
        document.DispatchClick(destinationBox.X + destinationBox.W / 2, destinationBox.Y + destinationBox.H / 2);
        Assert.IsFalse(model.ExportShare);
        Assert.Contains("file location", model.ExportDestinationDescription);
    }

    [TestMethod]
    public async Task InvalidPhotoSelectionBecomesAStatusMessage()
    {
        var platform = new FakePlatform(_directory)
        {
            PickFailure = new InvalidDataException("Choose a supported image file; videos cannot be measured.")
        };
        var app = new AnnoTapeApp(platform);
        await app.Initialization;
        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }
        var picker = Find(document.Root, node => node.Element?.ClassList.Contains("pick-photo") == true);
        Assert.IsNotNull(picker);

        var box = HitTesting.ScreenBox(picker);
        document.DispatchClick(box.X + box.W / 2, box.Y + box.H / 2);
        var model = (EditorViewModel)app.Model;
        await WaitForAsync(() => model.Status.StartsWith("Import failed:", StringComparison.Ordinal));

        Assert.Contains("videos cannot be measured", model.Status);
    }

    [TestMethod]
    public async Task WheelOverPhotoZoomsAtPointerInsteadOfScrollingEditor()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ImageSource = "missing-test-photo.png";
        model.SourceWidth = 400;
        model.SourceHeight = 300;
        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }

        Assert.IsTrue(document.DispatchWheel(100, 200, -50));

        Assert.IsGreaterThan(1, model.Zoom);
        Assert.AreNotEqual(0, model.PanX);
    }

    [TestMethod]
    public async Task TwoPointersPinchPhotoWithoutCreatingMeasurement()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ImageSource = "missing-test-photo.png";
        model.SourceWidth = 400;
        model.SourceHeight = 300;
        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }

        Assert.IsTrue(document.DispatchPointer(10, PointerPhase.Down, 120, 220));
        Assert.IsTrue(document.DispatchPointer(11, PointerPhase.Down, 220, 220));
        Assert.IsTrue(document.DispatchPointer(11, PointerPhase.Move, 270, 220));

        Assert.AreEqual(1.5, model.Zoom, 0.02);
    }

    [TestMethod]
    public async Task MiddleButtonPansEvenWhileAddModeIsArmed()
    {
        var app = new AnnoTapeApp(new FakePlatform(_directory));
        await app.Initialization;
        var model = (EditorViewModel)app.Model;
        model.Page = "editor";
        model.ImageSource = "missing-test-photo.png";
        model.SourceWidth = 400;
        model.SourceHeight = 300;
        model.Zoom = 2;
        model.AddMode = true;
        using var document = app.CreateDocument();
        using (document.RenderToImage(400, 800, app.Background)) { }

        Assert.IsTrue(document.DispatchMiddlePointer(-1, PointerPhase.Down, 200, 220));
        Assert.IsTrue(document.DispatchMiddlePointer(-1, PointerPhase.Move, 240, 250));

        Assert.AreEqual(40, model.PanX, 0.1);
        Assert.AreEqual(30, model.PanY, 0.1);
        Assert.IsTrue(model.AddMode);
    }

    [TestMethod]
    public void ViewportZoomKeepsAnchorFixedAndClampsAtActualSize()
    {
        var model = new EditorViewModel
        {
            ViewportWidth = 400,
            ViewportHeight = 800,
            SourceWidth = 400,
            SourceHeight = 300
        };

        Assert.IsTrue(model.SetViewport(1, 0, 0, 100, 200, 100, 200, 2));
        var anchored = model.PointerToImage(100, 200);
        Assert.AreEqual(0.25, anchored.X, 0.001);

        Assert.IsTrue(model.SetViewport(model.Zoom, model.PanX, model.PanY, 100, 200, 100, 200, 0.2));
        Assert.AreEqual(1, model.Zoom, 0.001);
        Assert.AreEqual(0, model.PanX, 0.001);
        Assert.AreEqual(0, model.PanY, 0.001);
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

    private static void SaveSnapshotIfRequested(SKImage image, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("ANNOTAPE_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(directory, fileName));
        encoded.SaveTo(output);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200 && !predicate(); attempt++)
            await Task.Delay(10);
        Assert.IsTrue(predicate(), "The asynchronous operation did not complete in time.");
    }

    private sealed class FakePlatform(string appDataPath) : IPlatformCapabilities
    {
#pragma warning disable CS0067
        public event Action<PhotoSelection?>? ExternalPhotoCompleted;
        public event Action<string>? ExternalPhotoFailed;
#pragma warning restore CS0067
        public Exception? PickFailure { get; init; }
        public string AppDataPath { get; } = appDataPath;
        public Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default) =>
            PickFailure is null ? Task.FromResult<PhotoSelection?>(null) : Task.FromException<PhotoSelection?>(PickFailure);
        public Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default) => Task.FromResult<PhotoSelection?>(null);
        public Task<string> PrepareDisplayImageAsync(string sourcePath, int rotationDegrees, CancellationToken cancellationToken = default) => Task.FromResult(sourcePath);
        public Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> SaveFileAsync(string path, string suggestedName, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
