using AnnoTape.App;
using AnnoTape.App.Platform;
using AnnoTape.App.Editor;
using AnnoTape.Core.Models;
using CupriFace.Dom;
using CupriFace.Interaction;

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
    public void AnnotationLineAccountsForImageAspectRatioAndCentredTransforms()
    {
        var annotation = new DimensionAnnotation { Start = new(0.2, 0.2), End = new(0.2, 0.8) };
        var view = AnnotationViewModel.From(annotation, selected: true, imageWidth: 400, imageHeight: 300);
        Assert.Contains("width:45%", view.LineStyle);
        Assert.Contains("left:-2.5%", view.LineStyle);
        Assert.Contains("top:50%", view.LineStyle);
        Assert.Contains("rotate(90deg)", view.LineStyle);
        Assert.Contains("width:80px", view.LabelStyle);
        Assert.Contains("margin-left:-40px", view.LabelStyle);
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
