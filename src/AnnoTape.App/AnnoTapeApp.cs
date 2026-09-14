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
    private Guid? _selectionBeforeDrawing;
    private double _gestureBaseZoom = 1;
    private double _gestureBasePanX;
    private double _gestureBasePanY;
    private double _gestureBaseDistance;
    private double _gestureBaseFocusX;
    private double _gestureBaseFocusY;
    private int _gesturePointerCount;
    private int _refreshRequested;
    private int _saveGeneration;

    public AnnoTapeApp(IPlatformCapabilities platform)
    {
        _platform = platform;
        _storage = new CrashSafeStorage(platform.AppDataPath);
        _storage.Initialize();
        _storage.CleanAbandonedStaging(TimeSpan.FromDays(1));
        _platform.ExternalPhotoCompleted += selection =>
        {
            if (selection is not null) RunDetached(async () =>
            {
                await EnsureInitializedAsync();
                await AcceptSelectionAsync(selection);
            });
        };
        _platform.ExternalPhotoFailed += message =>
        {
            _storage.ClearPending();
            _model.Status = $"Import failed: {message}";
            ScheduleRefresh();
        };
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
    public override byte[]? Icon =>
        CupriSource.Embedded<AnnoTapeApp>("Assets/annotape-icon.png").ReadBytes();
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
        document.OnClick(".center-label", _ => CenterSelectedLabel());
        document.OnClick(".delete-measurement", _ => DeleteSelected());
        document.OnClick(".undo", _ => { if (CurrentDocument is { } photo && _history.Undo(photo)) Changed(); });
        document.OnClick(".redo", _ => { if (CurrentDocument is { } photo && _history.Redo(photo)) Changed(); });
        document.OnClick(".open-export", _ => { _model.ExportShelfOpen = true; ScheduleRefresh(); });
        document.OnAction("data-set-path", e =>
        {
            if (e.Value == nameof(EditorViewModel.UnitName))
            {
                ApplyGlobalUnit(e.Element.GetAttribute("data-set-value"));
                return true;
            }
            if (!e.Element.ClassList.Contains("cupri-color-sw")) return false;
            ApplyColour(e.Element.GetAttribute("data-set-value"));
            return true;
        });
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
        document.OnWheel("data-editor", HandleEditorWheel);
    }

    private PhotoDocument? CurrentDocument => _project.Documents.FirstOrDefault();

    private void NewProject()
    {
        _project = new AnnoProject();
        _model.ProjectTitle = _project.Title;
        _model.ProjectNotes = "";
        _model.ProjectLocation = "";
        _model.ImageSource = "";
        _model.UnitName = nameof(MeasurementUnit.Millimetres);
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
        if (_model.AddMode && !pointer.IsMiddleButton && pointer.Pointers.Count == 1) return HandleDrawing(pointer);
        if (_model.AddMode && (pointer.IsMiddleButton || pointer.Pointers.Count >= 2)) CancelDrawingForNavigation();

        var (focusX, focusY) = PointerCentre(pointer.Pointers);
        var pointerCount = pointer.Pointers.Count;
        if (pointer.Phase == PointerPhase.Down || pointerCount != _gesturePointerCount)
        {
            _gestureBaseZoom = _model.Zoom;
            _gestureBasePanX = _model.PanX;
            _gestureBasePanY = _model.PanY;
            _gestureBaseFocusX = focusX;
            _gestureBaseFocusY = focusY;
            _gestureBaseDistance = pointerCount >= 2 ? PointerSpread(pointer.Pointers) : 0;
            _gesturePointerCount = pointerCount;
            return true;
        }
        if (pointer.Phase == PointerPhase.Move)
        {
            var zoom = _gestureBaseZoom;
            if (pointerCount >= 2 && _gestureBaseDistance > 0.01)
                zoom *= PointerSpread(pointer.Pointers) / _gestureBaseDistance;
            return _model.SetViewport(
                _gestureBaseZoom, _gestureBasePanX, _gestureBasePanY,
                _gestureBaseFocusX, _gestureBaseFocusY, focusX, focusY, zoom);
        }
        if (pointer.Phase is PointerPhase.Up or PointerPhase.Cancel && pointerCount <= 1) _gesturePointerCount = 0;
        return true;
    }

    private bool HandleEditorWheel(CupriWheelEvent wheel)
    {
        if (_model.ImageSource.Length == 0) return false;
        var steps = -wheel.DeltaY / 50d;
        var targetZoom = _model.Zoom * Math.Pow(1.18, steps);
        _model.SetViewport(
            _model.Zoom, _model.PanX, _model.PanY,
            wheel.X, wheel.Y, wheel.X, wheel.Y, targetZoom);
        return true;
    }

    private void CancelDrawingForNavigation()
    {
        if (_drawing is null || CurrentDocument is not { } photo) return;
        photo.Annotations.RemoveAll(item => item.Id == _drawing.Id);
        _drawing = null;
        _selectedId = _selectionBeforeDrawing;
        _selectionBeforeDrawing = null;
        SyncSelection();
        RebuildAnnotations();
    }

    private bool HandleDrawing(MultiPointerEvent pointer)
    {
        if (CurrentDocument is not { } photo) return false;
        var point = _model.PointerToImage(pointer.X, pointer.Y);
        if (pointer.Phase == PointerPhase.Down)
        {
            _selectionBeforeDrawing = _selectedId;
            _drawing = new DimensionAnnotation
            {
                Start = point,
                End = point,
                LabelAnchor = point,
                LabelCentered = true,
                DisplayText = MeasurementParser.Format(1000m, _model.SelectedUnit),
                Unit = _model.SelectedUnit,
                ColourHex = AnnotationColours.Normalize(_model.LineColour)
            };
            photo.Annotations.Add(_drawing);
            _selectedId = _drawing.Id;
        }
        else if (pointer.Phase == PointerPhase.Move && _drawing is not null)
        {
            _drawing.End = _model.ApplyAngleSnap(_drawing.Start, point);
            _drawing.LabelAnchor = AutomaticLabelAnchor(_drawing);
        }
        else if (pointer.Phase == PointerPhase.Up && _drawing is not null)
        {
            _drawing.End = _model.ApplyAngleSnap(_drawing.Start, point);
            _drawing.LabelAnchor = AutomaticLabelAnchor(_drawing);
            if (!_model.IsMeasurementDrag(_drawing.Start, _drawing.End))
            {
                photo.Annotations.RemoveAll(item => item.Id == _drawing.Id);
                _drawing = null;
                _selectedId = _selectionBeforeDrawing;
                _selectionBeforeDrawing = null;
                _model.Status = "Drag between two points to add a measurement";
                SyncSelection();
                RebuildAnnotations();
                return true;
            }

            var completed = _drawing.Copy();
            photo.Annotations.RemoveAll(item => item.Id == completed.Id);
            _history.Apply(photo, new AddAnnotationCommand(completed));
            _drawing = null;
            _selectionBeforeDrawing = null;
            _model.AddMode = false;
            _model.Status = "Enter the real measurement below";
            SyncSelection();
            Changed();
        }
        else if (pointer.Phase == PointerPhase.Cancel && _drawing is not null)
        {
            photo.Annotations.RemoveAll(item => item.Id == _drawing.Id);
            _drawing = null;
            _selectedId = _selectionBeforeDrawing;
            _selectionBeforeDrawing = null;
            SyncSelection();
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
            if (parts[0] == "start")
            {
                annotation.Start = _model.ApplyAngleSnap(annotation.End, point);
                if (annotation.LabelCentered) annotation.LabelAnchor = AutomaticLabelAnchor(annotation);
            }
            else if (parts[0] == "end")
            {
                annotation.End = _model.ApplyAngleSnap(annotation.Start, point);
                if (annotation.LabelCentered) annotation.LabelAnchor = AutomaticLabelAnchor(annotation);
            }
            else
            {
                annotation.LabelCentered = _model.IsNearAutomaticLabelAnchor(
                    point, annotation.Start, annotation.End, AnnotationViewModel.LabelWidthFor(annotation));
                annotation.LabelAnchor = annotation.LabelCentered ? AutomaticLabelAnchor(annotation) : point;
            }
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
        if (after.LabelCentered) after.LabelAnchor = AutomaticLabelAnchor(after);
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
            LabelCentered = source.LabelCentered,
            DisplayText = source.DisplayText,
            NormalizedMillimetres = source.NormalizedMillimetres,
            Unit = source.Unit,
            Precision = source.Precision,
            Label = source.Label,
            Style = source.Style,
            ColourHex = source.ColourHex
        };
        if (copy.LabelCentered) copy.LabelAnchor = AutomaticLabelAnchor(copy);
        _history.Apply(photo, new AddAnnotationCommand(copy));
        _selectedId = copy.Id;
        Changed();
    }

    private void ApplyColour(string? value)
    {
        var colour = AnnotationColours.Normalize(value);
        _model.LineColour = colour;
        _model.ColourOpen = false;
        if (CurrentDocument is not { } photo || _selectedId is not { } id)
        {
            _model.Status = $"New measurements will use {colour}";
            ScheduleRefresh();
            return;
        }
        var source = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (source is null) return;
        if (string.Equals(AnnotationColours.Normalize(source.ColourHex, source.Style), colour, StringComparison.OrdinalIgnoreCase)) return;
        var after = source.Copy();
        after.ColourHex = colour;
        after.ModifiedUtc = DateTimeOffset.UtcNow;
        _history.Apply(photo, new ReplaceAnnotationCommand(source, after));
        _model.Status = "Measurement colour updated";
        Changed();
    }

    private void CenterSelectedLabel()
    {
        if (CurrentDocument is not { } photo || _selectedId is not { } id) return;
        var source = photo.Annotations.FirstOrDefault(item => item.Id == id);
        if (source is null) return;
        var after = source.Copy();
        after.LabelCentered = true;
        after.LabelAnchor = AutomaticLabelAnchor(after);
        after.ModifiedUtc = DateTimeOffset.UtcNow;
        _history.Apply(photo, new ReplaceAnnotationCommand(source, after));
        _model.Status = "Label returned to the line centre";
        Changed();
    }

    private void ApplyGlobalUnit(string? value)
    {
        _model.UnitOpen = false;
        if (!Enum.TryParse<MeasurementUnit>(value, out var unit)) return;
        _model.UnitName = unit.ToString();
        if (CurrentDocument is not { } photo || photo.Annotations.Count == 0)
        {
            _model.Status = $"New measurements will use {MeasurementParser.Suffix(unit)}";
            ScheduleRefresh();
            return;
        }

        var before = photo.Annotations.Select(item => item.Copy()).ToArray();
        var after = before.Select(item =>
        {
            var converted = item.Copy();
            converted.Unit = unit;
            converted.DisplayText = MeasurementParser.Format(converted.NormalizedMillimetres, unit, converted.Precision);
            if (converted.LabelCentered) converted.LabelAnchor = AutomaticLabelAnchor(converted);
            converted.ModifiedUtc = DateTimeOffset.UtcNow;
            return converted;
        }).ToArray();
        _history.Apply(photo, new ReplaceAnnotationsCommand(before, after));
        _model.Status = $"All measurements converted to {MeasurementParser.Suffix(unit)}";
        Changed();
    }

    private NormalizedPoint AutomaticLabelAnchor(DimensionAnnotation annotation) =>
        _model.LabelAnchorFor(annotation.Start, annotation.End, AnnotationViewModel.LabelWidthFor(annotation));

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
        _model.LineColour = AnnotationColours.Normalize(annotation.ColourHex, annotation.Style);
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
                await _exporter.ExportAsync(photo, stream,
                    new ExportOptions(format, MaxDimension: _model.ExportFullSize ? null : 2048));
            var contentType = format == ExportFormat.Png ? "image/png" : "image/jpeg";
            if (_model.ExportShare)
            {
                await _platform.ShareFileAsync(path, contentType);
                _model.Status = _model.ExportFullSize ? "Full-resolution export ready to share" : "Share-size export ready";
            }
            else
            {
                var saved = await _platform.SaveFileAsync(path, Path.GetFileName(path), contentType);
                _model.Status = saved
                    ? _model.ExportFullSize ? "Full-resolution export saved" : "Share-size export saved"
                    : "Save cancelled";
            }
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
        if (photo is not null)
        {
            ApplyDocumentDefaults(photo);
            await LoadDisplayPhotoAsync(photo);
        }
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
        if (photo is not null) ApplyDocumentDefaults(photo);
        _model.ImageSource = "";
        if (photo is not null && !await LoadDisplayPhotoAsync(photo))
            _model.Status = "The original photo is safe, but its display preview could not be created.";
        _model.Page = "editor";
        _selectedId = null;
        await _repository.SaveAsync(_project);
        RebuildAnnotations();
    }

    private void ApplyDocumentDefaults(PhotoDocument photo)
    {
        if (photo.Annotations.FirstOrDefault() is not { } annotation) return;
        _model.UnitName = annotation.Unit.ToString();
        _model.LineColour = AnnotationColours.Normalize(annotation.ColourHex, annotation.Style);
    }

    private async Task InitializeApplicationAsync()
    {
        try
        {
            await _repository.InitializeAsync();
            _autosave = new DebouncedAutosave(_repository, TimeSpan.FromMilliseconds(450));
            await LoadRecentAsync();
            await RecoverPendingAsync();
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

    private static (double X, double Y) PointerCentre(IReadOnlyList<CupriPointer> pointers)
    {
        if (pointers.Count == 0) return (0, 0);
        return (pointers.Average(item => item.X), pointers.Average(item => item.Y));
    }

    private static double PointerSpread(IReadOnlyList<CupriPointer> pointers)
    {
        if (pointers.Count < 2) return 0;
        var centre = PointerCentre(pointers);
        return pointers.Average(item => Math.Sqrt(
            Math.Pow(item.X - centre.X, 2) + Math.Pow(item.Y - centre.Y, 2)));
    }
}
