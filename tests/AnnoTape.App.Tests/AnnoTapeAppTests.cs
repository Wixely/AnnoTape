using AnnoTape.App;
using AnnoTape.App.Platform;
using AnnoTape.App.Editor;
using AnnoTape.Core.Models;

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
