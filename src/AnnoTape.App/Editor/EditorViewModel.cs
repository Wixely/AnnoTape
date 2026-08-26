using System.Globalization;
using AnnoTape.Core.Geometry;
using AnnoTape.Core.Measurements;
using AnnoTape.Core.Models;
using CupriFace.Binding;

namespace AnnoTape.App.Editor;

[CupriBindable]
public sealed partial class EditorViewModel
{
    private const double HeaderHeight = 64;
    private const double InspectorHeight = 238;

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
    public string AddButtonClass => AddMode ? "tool active" : "tool";
    public string MeasurementText { get; set; } = "1000";
    public string MeasurementLabel { get; set; } = "";
    public string UnitName { get; set; } = nameof(MeasurementUnit.Millimetres);
    public string RecentSummary { get; set; } = "No saved projects yet.";
    public List<RecentProjectViewModel> RecentProjects { get; set; } = [];
    public string SelectionSummary { get; set; } = "No measurement selected";
    public string SaveState { get; set; } = "Saved";
    public bool HasSelection { get; set; }
    public string InspectorDisplay => HasSelection ? "flex" : "none";
    public double ViewportWidth { get; set; } = 400;
    public double ViewportHeight { get; set; } = 800;
    public double SourceWidth { get; set; } = 1;
    public double SourceHeight { get; set; } = 1;
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public List<AnnotationViewModel> AnnotationViews { get; set; } = [];

    public PixelRect StageRect => new(0, HeaderHeight, ViewportWidth, Math.Max(80, ViewportHeight - HeaderHeight - InspectorHeight));
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
    public required string ClassName { get; init; }

    public static AnnotationViewModel From(DimensionAnnotation annotation, bool selected, double imageWidth, double imageHeight)
    {
        var c = CultureInfo.InvariantCulture;
        var dx = (annotation.End.X - annotation.Start.X) * imageWidth;
        var dy = (annotation.End.Y - annotation.Start.Y) * imageHeight;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var widthPercent = imageWidth <= 0 ? 0 : length / imageWidth * 100d;
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        var midpointX = (annotation.Start.X + annotation.End.X) / 2d;
        var midpointY = (annotation.Start.Y + annotation.End.Y) / 2d;
        var lineLeft = midpointX - (imageWidth <= 0 ? 0 : length / imageWidth / 2d);
        string Percent(double value) => (value * 100d).ToString("0.###", c) + "%";
        var suffix = MeasurementParser.Suffix(annotation.Unit);
        var caption = string.IsNullOrWhiteSpace(annotation.Label)
            ? $"{annotation.DisplayText} {suffix}"
            : $"{annotation.Label}: {annotation.DisplayText} {suffix}";
        return new()
        {
            Id = annotation.Id.ToString("D"),
            ClassName = selected ? $"annotation selected style-{annotation.Style.ToString().ToLowerInvariant()}" : $"annotation style-{annotation.Style.ToString().ToLowerInvariant()}",
            LineStyle = $"left:{Percent(lineLeft)};top:{Percent(midpointY)};width:{widthPercent.ToString("0.###", c)}%;transform:rotate({angle.ToString("0.###", c)}deg)",
            StartStyle = $"left:{Percent(annotation.Start.X)};top:{Percent(annotation.Start.Y)}",
            EndStyle = $"left:{Percent(annotation.End.X)};top:{Percent(annotation.End.Y)}",
            LabelStyle = $"left:{Percent(annotation.LabelAnchor.X)};top:{Percent(annotation.LabelAnchor.Y)}",
            Caption = caption
        };
    }
}
