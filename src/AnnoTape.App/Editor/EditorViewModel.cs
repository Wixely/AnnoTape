using System.Globalization;
using AnnoTape.Core.Geometry;
using AnnoTape.Core.Measurements;
using AnnoTape.Core.Models;
using CupriFace.Binding;

namespace AnnoTape.App.Editor;

[CupriBindable]
public sealed partial class EditorViewModel
{
    private const double HeaderHeight = 60;
    private const double EmptyInspectorHeight = 96;
    private const double CompactInspectorHeight = 198;
    private const double SelectionInspectorHeight = 346;

    public string Page { get; set; } = "home";
    public string HomeDisplay => Page == "home" ? "flex" : "none";
    public string EditorDisplay => Page == "editor" ? "flex" : "none";
    public string Status { get; set; } = "Ready";
    public string ProjectTitle { get; set; } = "Untitled measurement";
    public string ProjectNotes { get; set; } = "";
    public string ProjectLocation { get; set; } = "";
    public string ImageSource { get; set; } = "";
    public string EmptyDisplay => ImageSource.Length == 0 ? "flex" : "none";
    public string ImageDisplay => ImageSource.Length == 0 ? "none" : "block";
    public bool AddMode { get; set; }
    public string AddButtonClass => AddMode ? "add-measurement active" : "add-measurement";
    public string AddButtonLabel => AddMode ? "Drawing…" : "Add";
    public bool ExportShelfOpen { get; set; }
    public bool ExportFullSize { get; set; } = true;
    public string ExportSizeDescription => ExportFullSize ? "Original pixel dimensions" : "Share size · max 2048 px";
    public string MeasurementText { get; set; } = "1000";
    public string MeasurementLabel { get; set; } = "";
    public string UnitName { get; set; } = nameof(MeasurementUnit.Millimetres);
    public bool UnitOpen { get; set; }
    public bool SnapEnabled { get; set; }
    public string LineColour { get; set; } = AnnotationColours.Copper;
    public bool ColourOpen { get; set; }
    public string RecentSummary { get; set; } = "No saved projects yet.";
    public List<RecentProjectViewModel> RecentProjects { get; set; } = [];
    public string SelectionSummary { get; set; } = "No measurement selected";
    public string SaveState { get; set; } = "Saved";
    public bool HasSelection { get; set; }
    public string InspectorDisplay => HasSelection ? "flex" : "none";
    public string InspectorClass => HasSelection
        ? "inspector has-selection"
        : ImageSource.Length == 0 ? "inspector empty-project" : "inspector";
    public string ToolsDisplay => ImageSource.Length == 0 ? "none" : "flex";
    public string SelectionSummaryDisplay => ImageSource.Length == 0 ? "none" : "block";
    public double ViewportWidth { get; set; } = 400;
    public double ViewportHeight { get; set; } = 800;
    public double SourceWidth { get; set; } = 1;
    public double SourceHeight { get; set; } = 1;
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public List<AnnotationViewModel> AnnotationViews { get; set; } = [];

    public PixelRect StageRect => new(0, HeaderHeight, ViewportWidth,
        Math.Max(80, ViewportHeight - HeaderHeight - InspectorHeight));
    private double InspectorHeight => HasSelection
        ? SelectionInspectorHeight
        : ImageSource.Length == 0 ? EmptyInspectorHeight : CompactInspectorHeight;
    public PixelRect BaseImageRect => ImageGeometry.FitContain(SourceWidth, SourceHeight, StageRect);
    public string ImageFrameStyle
    {
        get
        {
            var rect = BaseImageRect;
            var c = CultureInfo.InvariantCulture;
            return $"display:{ImageDisplay};left:{rect.X.ToString("0.###", c)}px;top:{(rect.Y - HeaderHeight).ToString("0.###", c)}px;" +
                   $"width:{rect.Width.ToString("0.###", c)}px;height:{rect.Height.ToString("0.###", c)}px;" +
                   $"transform:translate({PanX.ToString("0.###", c)}px,{PanY.ToString("0.###", c)}px) scale({Zoom.ToString("0.###", c)})";
        }
    }

    public NormalizedPoint PointerToImage(double x, double y)
    {
        var frame = BaseImageRect;
        var centreX = frame.X + frame.Width / 2d;
        var centreY = frame.Y + frame.Height / 2d;
        var unscaledX = (x - centreX - PanX) / Zoom + centreX;
        var unscaledY = (y - centreY - PanY) / Zoom + centreY;
        return ImageGeometry.ToNormalized(unscaledX, unscaledY, frame);
    }

    public bool SetViewport(double baseZoom, double basePanX, double basePanY,
        double anchorX, double anchorY, double targetX, double targetY, double requestedZoom)
    {
        var zoom = Math.Clamp(requestedZoom, 1, 8);
        var frame = BaseImageRect;
        var centreX = frame.X + frame.Width / 2d;
        var centreY = frame.Y + frame.Height / 2d;
        var ratio = zoom / Math.Max(0.001, baseZoom);
        var panX = targetX - centreX - ratio * (anchorX - centreX - basePanX);
        var panY = targetY - centreY - ratio * (anchorY - centreY - basePanY);
        var maxPanX = frame.Width * (zoom - 1) / 2d;
        var maxPanY = frame.Height * (zoom - 1) / 2d;
        panX = Math.Clamp(panX, -maxPanX, maxPanX);
        panY = Math.Clamp(panY, -maxPanY, maxPanY);
        var changed = Math.Abs(Zoom - zoom) > 0.0001 || Math.Abs(PanX - panX) > 0.01 || Math.Abs(PanY - panY) > 0.01;
        Zoom = zoom;
        PanX = panX;
        PanY = panY;
        return changed;
    }

    public NormalizedPoint LabelAnchorFor(NormalizedPoint start, NormalizedPoint end, double labelWidth = 88)
    {
        var frame = BaseImageRect;
        var dx = (end.X - start.X) * frame.Width;
        var dy = (end.Y - start.Y) * frame.Height;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var midpointX = (start.X + end.X) / 2d;
        var midpointY = (start.Y + end.Y) / 2d;
        if (length < 0.001 || frame.Width <= 0 || frame.Height <= 0)
            return NormalizedPoint.Clamp(midpointX, midpointY);

        // A normal-length dimension carries its label at the exact midpoint.
        // Short dimensions offset it so the label does not cover both handles.
        if (length * Zoom >= labelWidth + 32)
            return NormalizedPoint.Clamp(midpointX, midpointY);

        var normalX = dy / length;
        var normalY = -dx / length;
        if (normalY > 0 || (Math.Abs(normalY) < 0.25 && normalX < 0))
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        const double labelClearance = 32;
        return NormalizedPoint.Clamp(
            midpointX + normalX * labelClearance / (frame.Width * Zoom),
            midpointY + normalY * labelClearance / (frame.Height * Zoom));
    }

    public bool IsNearAutomaticLabelAnchor(NormalizedPoint point, NormalizedPoint start, NormalizedPoint end, double labelWidth = 88)
    {
        var target = LabelAnchorFor(start, end, labelWidth);
        var frame = BaseImageRect;
        var dx = (point.X - target.X) * frame.Width * Zoom;
        var dy = (point.Y - target.Y) * frame.Height * Zoom;
        return Math.Sqrt(dx * dx + dy * dy) <= 28;
    }

    public bool IsMeasurementDrag(NormalizedPoint start, NormalizedPoint end)
    {
        var frame = BaseImageRect;
        var dx = (end.X - start.X) * frame.Width * Zoom;
        var dy = (end.Y - start.Y) * frame.Height * Zoom;
        return Math.Sqrt(dx * dx + dy * dy) >= 24;
    }

    public NormalizedPoint ApplyAngleSnap(NormalizedPoint origin, NormalizedPoint candidate)
    {
        if (!SnapEnabled) return candidate;
        var frame = BaseImageRect;
        if (frame.Width <= 0 || frame.Height <= 0) return candidate;
        var dx = (candidate.X - origin.X) * frame.Width;
        var dy = (candidate.Y - origin.Y) * frame.Height;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001) return candidate;

        const double angleStep = Math.PI * 2d / 16d;
        var angle = Math.Round(Math.Atan2(dy, dx) / angleStep) * angleStep;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var boundedLength = length;
        if (cosine > 0) boundedLength = Math.Min(boundedLength, (1 - origin.X) * frame.Width / cosine);
        else if (cosine < 0) boundedLength = Math.Min(boundedLength, -origin.X * frame.Width / cosine);
        if (sine > 0) boundedLength = Math.Min(boundedLength, (1 - origin.Y) * frame.Height / sine);
        else if (sine < 0) boundedLength = Math.Min(boundedLength, -origin.Y * frame.Height / sine);

        return NormalizedPoint.Clamp(
            origin.X + cosine * boundedLength / frame.Width,
            origin.Y + sine * boundedLength / frame.Height);
    }

    public MeasurementUnit SelectedUnit => Enum.TryParse<MeasurementUnit>(UnitName, out var unit) ? unit : MeasurementUnit.Millimetres;
}

public sealed class RecentProjectViewModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
}

public sealed class AnnotationViewModel
{
    public required string Id { get; init; }
    public required string LineStyle { get; init; }
    public required string StartStyle { get; init; }
    public required string EndStyle { get; init; }
    public required string LabelStyle { get; init; }
    public required string Caption { get; init; }
    public required string LineClass { get; init; }
    public required string LabelClass { get; init; }
    public required string ColourStyle { get; init; }
    public required string MarkerStyle { get; init; }

    public static AnnotationViewModel From(DimensionAnnotation annotation, bool selected, double imageWidth, double imageHeight)
    {
        var c = CultureInfo.InvariantCulture;
        var dx = (annotation.End.X - annotation.Start.X) * imageWidth;
        var dy = (annotation.End.Y - annotation.Start.Y) * imageHeight;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var widthPercent = imageWidth <= 0 ? 0 : length / imageWidth * 100d;
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        string Percent(double value) => (value * 100d).ToString("0.###", c) + "%";
        var caption = CaptionFor(annotation);
        var labelWidth = LabelWidthFor(annotation);
        var colour = AnnotationColours.Normalize(annotation.ColourHex, annotation.Style);
        return new()
        {
            Id = annotation.Id.ToString("D"),
            LineClass = selected ? "dimension-line selected" : "dimension-line",
            LabelClass = selected ? "measure-label selected" : "measure-label",
            ColourStyle = $"background:{colour}",
            MarkerStyle = selected ? $"background:{colour}" : "background:transparent;border-color:transparent;box-shadow:none",
            LineStyle = $"left:{Percent(annotation.Start.X)};top:{Percent(annotation.Start.Y)};width:{widthPercent.ToString("0.###", c)}%;transform:rotate({angle.ToString("0.###", c)}deg);background:{colour}",
            StartStyle = $"left:{Percent(annotation.Start.X)};top:{Percent(annotation.Start.Y)}",
            EndStyle = $"left:{Percent(annotation.End.X)};top:{Percent(annotation.End.Y)}",
            LabelStyle = $"left:{Percent(annotation.LabelAnchor.X)};top:{Percent(annotation.LabelAnchor.Y)};width:{labelWidth}px;transform:translate({(-labelWidth / 2d).ToString("0.###", c)}px,-22px);color:{colour};border-color:{colour}",
            Caption = caption
        };
    }

    public static int LabelWidthFor(DimensionAnnotation annotation) =>
        Math.Clamp(32 + CaptionFor(annotation).Length * 8, 88, 280);

    private static string CaptionFor(DimensionAnnotation annotation)
    {
        var suffix = MeasurementParser.Suffix(annotation.Unit);
        return string.IsNullOrWhiteSpace(annotation.Label)
            ? $"{annotation.DisplayText} {suffix}"
            : $"{annotation.Label}: {annotation.DisplayText} {suffix}";
    }
}
