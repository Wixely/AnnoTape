namespace AnnoTape.Core.Models;

public enum ProjectState
{
    Draft,
    Completed
}

public enum MeasurementUnit
{
    Millimetres,
    Centimetres,
    Metres,
    Inches,
    FeetAndInches
}

public enum AnnotationStyle
{
    Copper,
    White,
    Black,
    Yellow,
    Red
}

public readonly record struct NormalizedPoint(double X, double Y)
{
    public static NormalizedPoint Clamp(double x, double y) =>
        new(Math.Clamp(x, 0d, 1d), Math.Clamp(y, 0d, 1d));

    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && X is >= 0 and <= 1 && Y is >= 0 and <= 1;
}

public sealed record DimensionAnnotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public NormalizedPoint Start { get; set; } = new(0.2, 0.5);
    public NormalizedPoint End { get; set; } = new(0.8, 0.5);
    public NormalizedPoint LabelAnchor { get; set; } = new(0.5, 0.45);
    public string DisplayText { get; set; } = "1000";
    public decimal NormalizedMillimetres { get; set; } = 1000m;
    public MeasurementUnit Unit { get; set; } = MeasurementUnit.Millimetres;
    public int? Precision { get; set; }
    public string? Label { get; set; }
    public AnnotationStyle Style { get; set; } = AnnotationStyle.Copper;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DimensionAnnotation Copy() => this with { };
}

public sealed record PhotoDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string SourcePath { get; set; } = "";
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public int RotationDegrees { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset? AutosavedUtc { get; set; }
    public List<DimensionAnnotation> Annotations { get; init; } = [];
}

public sealed record AnnoProject
{
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled measurement";
    public string Notes { get; set; } = "";
    public string? Location { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastOpenedUtc { get; set; } = DateTimeOffset.UtcNow;
    public ProjectState State { get; set; } = ProjectState.Draft;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<PhotoDocument> Documents { get; init; } = [];
}

public sealed record PendingExternalOperation(
    Guid Id,
    string Kind,
    Guid ProjectId,
    string? StagingPath,
    DateTimeOffset StartedUtc);

