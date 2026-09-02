using AnnoTape.App.Editor;
using AnnoTape.App.Platform;
using AnnoTape.Core.Editing;
using AnnoTape.Core.Export;
using AnnoTape.Core.Measurements;
using AnnoTape.Core.Models;
using AnnoTape.Core.Persistence;
using CupriFace;
using CupriFace.Interaction;
using CupriFace.Resources;
using SkiaSharp;

namespace AnnoTape.App;

public sealed class AnnoTapeApp : CupriApp
{
    private readonly IPlatformCapabilities _platform;
    private readonly EditorViewModel _model = new();
    private readonly ProjectRepository _repository;
    private readonly CrashSafeStorage _storage;
    private readonly Task _initialization;
    private DebouncedAutosave? _autosave;
    private readonly EditorHistory _history = new();
    private readonly AnnotationExporter _exporter = new();
    private CupriDocument? _document;
    private AnnoProject _project = new();
    private Guid? _selectedId;
    private DimensionAnnotation? _dragBefore;
    private DimensionAnnotation? _drawing;
    private readonly Dictionary<int, (float X, float Y)> _gestureStarts = [];
    private double _gestureBaseZoom = 1;
    private double _gestureBasePanX;
    private double _gestureBasePanY;
    private double _gestureBaseDistance;
    private int _refreshRequested;
    private int _saveGeneration;

    public AnnoTapeApp(IPlatformCapabilities platform)
    {
        _platform = platform;
        _platform.ExternalPhotoCompleted += selection =>
        {
            if (selection is not null) RunDetached(async () =>
            {
                await EnsureInitializedAsync();
                await AcceptSelectionAsync(selection);
            });
        };
        _storage = new CrashSafeStorage(platform.AppDataPath);
        _storage.Initialize();
        _storage.CleanAbandonedStaging(TimeSpan.FromDays(1));
        _repository = new ProjectRepository(Path.Combine(platform.AppDataPath, "annotape.db"));
        _model.Status = "Starting storage…";
        _initialization = Task.Run(InitializeApplicationAsync);
    }

    public Task Initialization => _initialization;
    public Exception? InitializationError { get; private set; }

    protected override CupriSource MarkupSource =>
        CupriSource.Embedded<AnnoTapeApp>("AnnoTape.App.Assets.AnnoTape.html");
    protected override CupriSource StyleSource =>
        CupriSource.Embedded<AnnoTapeApp>("AnnoTape.App.Assets.AnnoTape.css");
    public override string Title => "AnnoTape";
    public override int Width => 400;
    public override int Height => 800;
    public override SKColor Background => new(0x05, 0x05, 0x06);
    public override object Model => _model;
    public override double RefreshIntervalSeconds => 0.1;

    public override PresentInfo Present(float windowWidth, float windowHeight)
    {
        _model.ViewportWidth = windowWidth;
        _model.ViewportHeight = windowHeight;
        if (Interlocked.Exchange(ref _refreshRequested, 0) == 1) _document?.Refresh();
        return new(windowWidth, windowHeight, 1);
    }

    public override void Configure(CupriDocument document)
    {
        _document = document;
        document.OnClick(".pick-photo", _ => RunDetached(() => ImportAsync(camera: false)));
        document.OnClick(".take-photo", _ => RunDetached(() => ImportAsync(camera: true)));
        document.OnClick(".back-home", _ => RunDetached(async () =>
        {
            await EnsureInitializedAsync();
            await FlushAsync();
            await LoadRecentAsync();
            _model.Page = "home";
            ScheduleRefresh();
        }));
        document.OnClick(".add-measurement", _ => { _model.AddMode = !_model.AddMode; _model.Status = _model.AddMode ? "Drag across the photo" : "Navigate mode"; });
        document.OnClick(".save-value", _ => SaveSelectedValue());
        document.OnClick(".delete-measurement", _ => DeleteSelected());
        document.OnClick(".undo", _ => { if (CurrentDocument is { } photo && _history.Undo(photo)) Changed(); });
        document.OnClick(".redo", _ => { if (CurrentDocument is { } photo && _history.Redo(photo)) Changed(); });
        document.OnClick(".open-export", _ => { _model.ExportShelfOpen = true; ScheduleRefresh(); });
        document.OnClick(".export-png", _ => ExportFromShelf(ExportFormat.Png));
        document.OnClick(".export-jpeg", _ => ExportFromShelf(ExportFormat.Jpeg));
        document.OnAction("data-select", e => { Select(Guid.Parse(e.Value)); return true; });
        document.OnAction("data-open-project", e =>
        {
            RunDetached(async () =>
            {
                await EnsureInitializedAsync();
                await OpenProjectAsync(Guid.Parse(e.Value));
            });
            return true;
        });
        document.OnPointer("data-drag", HandleAnnotationDrag);
        document.OnPointer("data-editor", HandleEditorPointer);
    }

    private PhotoDocument? CurrentDocument => _project.Documents.FirstOrDefault();

    private void NewProject()
    {
        _project = new AnnoProject();
        _model.ProjectTitle = _project.Title;
        _model.ProjectNotes = "";
        _model.ProjectLocation = "";
        _model.ImageSource = "";
        _model.AnnotationViews = [];
        _model.Page = "editor";
        _selectedId = null;
        SyncSelection();
        _autosave!.Schedule(_project);
    }

    private async Task ImportAsync(bool camera)
    {
        try
        {
            await EnsureInitializedAsync();
            if (_model.Page != "editor") NewProject();
            var pending = new PendingExternalOperation(Guid.NewGuid(), camera ? "camera" : "picker", _project.Id, null, DateTimeOffset.UtcNow);
            _storage.WritePending(pending);
            await _autosave!.FlushAsync(_project);
            var selection = camera
                ? await _platform.CapturePhotoAsync()
                : await _platform.PickPhotoAsync();
            if (selection is null)
            {
                _storage.ClearPending();
                _model.Status = "Photo selection cancelled";
                return;
            }

            await AcceptSelectionAsync(selection);
        }
        catch (Exception exception)
        {
            _storage.ClearPending();
            _model.Status = $"Import failed: {exception.Message}";
        }
        finally
        {
            ScheduleRefresh();
        }
    }

    private async Task AcceptSelectionAsync(PhotoSelection selection)
    {
        await using var input = await selection.OpenReadAsync(CancellationToken.None);
        var path = await _storage.ImportAsync(input, _project.Id, selection.Extension);
        _project.Documents.Clear();
        _project.Documents.Add(new PhotoDocument
        {
            ProjectId = _project.Id,
            SourcePath = path,
            PixelWidth = selection.PixelWidth,
            PixelHeight = selection.PixelHeight,
            RotationDegrees = selection.RotationDegrees
        });
        _model.Page = "editor";
        var previewReady = await LoadDisplayPhotoAsync(_project.Documents[0]);
        _model.Status = previewReady
            ? "Photo imported. Choose Add measurement."
            : "Photo imported, but a display preview could not be created. Choose another photo.";
        _storage.ClearPending();
        await _autosave!.FlushAsync(_project);
        RebuildAnnotations();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        SyncProjectDetails();
        await _autosave!.FlushAsync(_project, cancellationToken);
    }

    private bool HandleEditorPointer(MultiPointerEvent pointer)
    {
        if (_model.AddMode) return HandleDrawing(pointer);
        _gestureStarts[pointer.Id] = (pointer.X, pointer.Y);
        if (pointer.Phase == PointerPhase.Down)
        {
            _gestureBaseZoom = _model.Zoom;
            _gestureBasePanX = _model.PanX;
            _gestureBasePanY = _model.PanY;
            if (pointer.Pointers.Count >= 2) _gestureBaseDistance = Distance(pointer.Pointers[0], pointer.Pointers[1]);
            return true;
        }
        if (pointer.Phase == PointerPhase.Move)
        {
            if (pointer.Pointers.Count >= 2)
            {
                var distance = Distance(pointer.Pointers[0], pointer.Pointers[1]);
                if (_gestureBaseDistance > 0) _model.Zoom = Math.Clamp(_gestureBaseZoom * distance / _gestureBaseDistance, 1, 8);
            }
            else if (_gestureStarts.TryGetValue(pointer.Id, out var start))
            {
                _model.PanX = _gestureBasePanX + pointer.X - start.X;
                _model.PanY = _gestureBasePanY + pointer.Y - start.Y;
            }
            return true;
        }
        if (pointer.Phase is PointerPhase.Up or PointerPhase.Cancel) _gestureStarts.Remove(pointer.Id);
        return true;
    }

    private bool HandleDrawing(MultiPointerEvent pointer)
    {
        if (CurrentDocument is not { } photo) return false;
        var point = _model.PointerToImage(pointer.X, pointer.Y);
        if (pointer.Phase == PointerPhase.Down)
        {
            _drawing = new DimensionAnnotation { Start = point, End = point, LabelAnchor = point };
            photo.Annotations.Add(_drawing);
            _selectedId = _drawing.Id;
        }
        else if (pointer.Phase == PointerPhase.Move && _drawing is not null)
        {
            _drawing.End = point;
            _drawing.LabelAnchor = new((_drawing.Start.X + point.X) / 2d, (_drawing.Start.Y + point.Y) / 2d - 0.05);
        }
        else if (pointer.Phase == PointerPhase.Up && _drawing is not null)
        {
            var completed = _drawing.Copy();
            photo.Annotations.RemoveAll(item => item.Id == completed.Id);
            _history.Apply(photo, new AddAnnotationCommand(completed));
            _drawing = null;
            _model.AddMode = false;
            _model.Status = "Enter the real measurement below";
            SyncSelection();
            Changed();
        }
        else if (pointer.Phase == PointerPhase.Cancel && _drawing is not null)
        {
            photo.Annotations.RemoveAll(item => item.Id == _drawing.Id);
            _drawing = null;
        }
        RebuildAnnotations();
        return true;
    }

    private bool HandleAnnotationDrag(MultiPointerEvent pointer)
    {
        if (CurrentDocument is not { } photo) return false;
        var parts = pointer.Value.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var id)) return false;
        var annotation = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (annotation is null) return false;
        if (pointer.Phase == PointerPhase.Down)
        {
            _selectedId = id;
            _dragBefore = annotation.Copy();
            SyncSelection();
        }
        else if (pointer.Phase == PointerPhase.Move)
        {
            var point = _model.PointerToImage(pointer.X, pointer.Y);
            if (parts[0] == "start") annotation.Start = point;
            else if (parts[0] == "end") annotation.End = point;
            else annotation.LabelAnchor = point;
            annotation.ModifiedUtc = DateTimeOffset.UtcNow;
        }
        else if (pointer.Phase == PointerPhase.Up && _dragBefore is not null)
        {
            var after = annotation.Copy();
            var index = photo.Annotations.FindIndex(item => item.Id == id);
            photo.Annotations[index] = _dragBefore.Copy();
            _history.Apply(photo, new ReplaceAnnotationCommand(_dragBefore, after));
            _dragBefore = null;
            Changed();
        }
        RebuildAnnotations();
        return true;
    }

    private void SaveSelectedValue()
    {
        if (CurrentDocument is not { } photo || _selectedId is not { } id) return;
        var annotation = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (annotation is null) return;
        if (!MeasurementParser.TryParse(_model.MeasurementText, _model.SelectedUnit, out var measurement))
        {
            _model.Status = "Enter a valid non-negative measurement";
            return;
        }
        var before = annotation.Copy();
        var after = annotation.Copy();
        after.DisplayText = measurement.DisplayText;
        after.NormalizedMillimetres = measurement.Millimetres;
        after.Unit = measurement.Unit;
        after.Label = string.IsNullOrWhiteSpace(_model.MeasurementLabel) ? null : _model.MeasurementLabel.Trim();
        after.ModifiedUtc = DateTimeOffset.UtcNow;
        _history.Apply(photo, new ReplaceAnnotationCommand(before, after));
        _model.Status = "Measurement saved";
        Changed();
    }

    private void DeleteSelected()
    {
        if (CurrentDocument is not { } photo || _selectedId is not { } id) return;
        var index = photo.Annotations.FindIndex(item => item.Id == id);
        if (index < 0) return;
        _history.Apply(photo, new DeleteAnnotationCommand(photo.Annotations[index], index));
        _selectedId = null;
        SyncSelection();
        Changed();
    }

    private void DuplicateSelected()
    {
        if (CurrentDocument is not { } photo || _selectedId is not { } id) return;
        var source = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (source is null) return;
        var copy = new DimensionAnnotation
        {
            Start = NormalizedPoint.Clamp(source.Start.X + 0.03, source.Start.Y + 0.03),
            End = NormalizedPoint.Clamp(source.End.X + 0.03, source.End.Y + 0.03),
            LabelAnchor = NormalizedPoint.Clamp(source.LabelAnchor.X + 0.03, source.LabelAnchor.Y + 0.03),
            DisplayText = source.DisplayText,
            NormalizedMillimetres = source.NormalizedMillimetres,
            Unit = source.Unit,
            Precision = source.Precision,
            Label = source.Label,
            Style = source.Style
        };
        _history.Apply(photo, new AddAnnotationCommand(copy));
        _selectedId = copy.Id;
        Changed();
    }

    private void RecolourSelected()
    {
        if (CurrentDocument is not { } photo || _selectedId is not { } id) return;
        var source = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (source is null) return;
        var after = source.Copy();
        after.Style = (AnnotationStyle)(((int)source.Style + 1) % Enum.GetValues<AnnotationStyle>().Length);
        after.ModifiedUtc = DateTimeOffset.UtcNow;
        _history.Apply(photo, new ReplaceAnnotationCommand(source, after));
        Changed();
    }

    private void Select(Guid id)
    {
        _selectedId = id;
        SyncSelection();
        RebuildAnnotations();
    }

    private void SyncSelection()
    {
        var annotation = CurrentDocument?.Annotations.FirstOrDefault(item => item.Id == _selectedId);
        _model.HasSelection = annotation is not null;
        if (annotation is null)
        {
            _model.SelectionSummary = "No measurement selected";
            return;
        }
        _model.MeasurementText = annotation.DisplayText;
        _model.MeasurementLabel = annotation.Label ?? "";
        _model.UnitName = annotation.Unit.ToString();
        _model.SelectionSummary = $"Selected: {annotation.DisplayText} {MeasurementParser.Suffix(annotation.Unit)}";
    }

    private void Changed()
    {
        SyncProjectDetails();
        _model.SaveState = "Saving…";
        var generation = Interlocked.Increment(ref _saveGeneration);
        if (_autosave?.Schedule(_project) is { } saveTask)
        {
            _model.SaveState = "Queued";
            RunDetached(async () =>
            {
                await saveTask;
                if (generation != Volatile.Read(ref _saveGeneration)) return;
                _model.SaveState = "Saved";
                ScheduleRefresh();
            });
        }
        RebuildAnnotations();
    }

    private void RebuildAnnotations()
    {
        var frame = _model.BaseImageRect;
        _model.AnnotationViews = CurrentDocument?.Annotations
            .Select(item => AnnotationViewModel.From(item, item.Id == _selectedId, frame.Width, frame.Height))
            .ToList() ?? [];
        SyncSelection();
        ScheduleRefresh();
    }

    private async Task ExportAsync(ExportFormat format)
    {
        if (CurrentDocument is not { } photo) return;
        try
        {
            var directory = Path.Combine(_platform.AppDataPath, "exports");
            Directory.CreateDirectory(directory);
            var extension = format == ExportFormat.Png ? ".png" : ".jpg";
            var path = Path.Combine(directory, $"AnnoTape-{DateTime.UtcNow:yyyyMMdd-HHmmss}{extension}");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await _exporter.ExportAsync(photo, stream, new ExportOptions(format));
            await _platform.ShareFileAsync(path, format == ExportFormat.Png ? "image/png" : "image/jpeg");
            _model.Status = "Export ready to share";
        }
        catch (Exception exception)
        {
            _model.Status = $"Export failed: {exception.Message}";
        }
        finally { ScheduleRefresh(); }
    }

    private void ExportFromShelf(ExportFormat format)
    {
        _model.ExportShelfOpen = false;
        ScheduleRefresh();
        RunDetached(() => ExportAsync(format));
    }

    private async Task LoadRecentAsync()
    {
        var recent = await _repository.RecentAsync(5);
        _model.RecentSummary = recent.Count == 0
            ? "No saved projects yet."
            : string.Join("  ·  ", recent.Select(item => item.Title));
        _model.RecentProjects = recent.Select(item => new RecentProjectViewModel
        {
            Id = item.Id.ToString("D"),
            Title = item.Title,
            Subtitle = $"{item.ModifiedUtc.LocalDateTime:g} · {item.State}"
        }).ToList();
        if (_storage.ReadPending() is not null) _model.Status = "An interrupted photo request can be retried safely.";
    }

    private async Task RecoverPendingAsync()
    {
        var pending = _storage.ReadPending();
        if (pending is null) return;
        var recovered = await _repository.LoadAsync(pending.ProjectId);
        if (recovered is null) return;
        _project = recovered;
        _model.ProjectTitle = recovered.Title;
        _model.ProjectNotes = recovered.Notes;
        _model.ProjectLocation = recovered.Location ?? "";
        _model.Page = "editor";
        var photo = recovered.Documents.FirstOrDefault();
        if (photo is not null) await LoadDisplayPhotoAsync(photo);
        RebuildAnnotations();
    }

    private async Task OpenProjectAsync(Guid id)
    {
        var project = await _repository.LoadAsync(id);
        if (project is null) return;
        _project = project;
        _project.LastOpenedUtc = DateTimeOffset.UtcNow;
        _model.ProjectTitle = project.Title;
        _model.ProjectNotes = project.Notes;
        _model.ProjectLocation = project.Location ?? "";
        var photo = project.Documents.FirstOrDefault();
        _model.ImageSource = "";
        if (photo is not null && !await LoadDisplayPhotoAsync(photo))
            _model.Status = "The original photo is safe, but its display preview could not be created.";
        _model.Page = "editor";
        _selectedId = null;
        await _repository.SaveAsync(_project);
        RebuildAnnotations();
    }

    private async Task InitializeApplicationAsync()
    {
        try
        {
            await _repository.InitializeAsync();
            _autosave = new DebouncedAutosave(_repository, TimeSpan.FromMilliseconds(450));
            await LoadRecentAsync();
            await RecoverPendingAsync();
            if (_storage.TakeHostRestartProject() is { } restartProjectId)
                await OpenProjectAsync(restartProjectId);
            if (_model.Status == "Starting storage…") _model.Status = "Ready";
        }
        catch (Exception exception)
        {
            InitializationError = exception;
            _model.Status = $"Storage startup failed: {exception.Message}";
        }
        finally
        {
            ScheduleRefresh();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        await _initialization;
        if (_autosave is null)
            throw new InvalidOperationException("Storage is unavailable.", InitializationError);
    }

    private void SyncProjectDetails()
    {
        _project.Title = string.IsNullOrWhiteSpace(_model.ProjectTitle) ? "Untitled measurement" : _model.ProjectTitle.Trim();
        _project.Notes = _model.ProjectNotes;
        _project.Location = string.IsNullOrWhiteSpace(_model.ProjectLocation) ? null : _model.ProjectLocation.Trim();
    }

    private async Task<bool> LoadDisplayPhotoAsync(PhotoDocument photo)
    {
        _model.ImageSource = "";
        _model.SourceWidth = photo.RotationDegrees is 90 or 270 ? photo.PixelHeight : photo.PixelWidth;
        _model.SourceHeight = photo.RotationDegrees is 90 or 270 ? photo.PixelWidth : photo.PixelHeight;
        _model.Zoom = 1;
        _model.PanX = 0;
        _model.PanY = 0;
        try
        {
            _model.ImageSource = await _platform.PrepareDisplayImageAsync(photo.SourcePath, photo.RotationDegrees);
            return true;
        }
        catch (Exception exception)
        {
            _model.Status = $"Preview failed: {exception.Message}";
            return false;
        }
    }

    private void ScheduleRefresh() => Interlocked.Exchange(ref _refreshRequested, 1);

    private void RunDetached(Func<Task> operation)
    {
        _ = RunAsync(operation);
        return;
        async Task RunAsync(Func<Task> action)
        {
            try { await action(); }
            catch (Exception exception) { _model.Status = exception.Message; ScheduleRefresh(); }
        }
    }

    private static double Distance(CupriPointer first, CupriPointer second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
