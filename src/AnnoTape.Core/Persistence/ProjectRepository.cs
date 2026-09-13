using System.Data;
using System.Globalization;
using Dapper;
using DnaX.Data.Migrations;
using DnaX.Data.Migrations.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using AnnoTape.Core.Models;

namespace AnnoTape.Core.Persistence;

public sealed class ProjectRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = false,
        ForeignKeys = true
    }.ToString();

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var legacySchema = await HasLegacySchemaAsync(cancellationToken);
        ServiceCollection services = new();
        services.AddDnaXDataMigrations("AnnoTape", options =>
        {
            options.ConnectionFactory = _ => new SqliteConnection(_connectionString);
            options.Manifest = ApplicationSchema.Manifest;
            options.ApplicationVersion = "0.1.0";
            options.UseSqlite(sqlite =>
            {
                sqlite.EnableWriteAheadLogging = true;
                sqlite.EnforceForeignKeys = true;
                sqlite.DeferForeignKeysDuringMigration = true;
                sqlite.LockTimeout = TimeSpan.FromSeconds(30);
            });
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        if (legacySchema)
        {
            await provider.BaselineDnaXDatabaseAsync(
                "AnnoTape",
                throughVersion: 1,
                VerifyLegacySchemaAsync,
                cancellationToken);
        }
        await provider.MigrateDnaXDatabaseAsync("AnnoTape", cancellationToken);
    }

    public async Task SaveAsync(AnnoProject project, CancellationToken cancellationToken = default)
    {
        project.ModifiedUtc = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO projects(id,title,notes,location,created_utc,modified_utc,last_opened_utc,state,schema_version)
            VALUES(@Id,@Title,@Notes,@Location,@CreatedUtc,@ModifiedUtc,@LastOpenedUtc,@State,@SchemaVersion)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,notes=excluded.notes,location=excluded.location,
              modified_utc=excluded.modified_utc,last_opened_utc=excluded.last_opened_utc,state=excluded.state,
              schema_version=excluded.schema_version;
            """,
            new
            {
                Id = project.Id.ToString("D"), project.Title, project.Notes, project.Location,
                CreatedUtc = Format(project.CreatedUtc), ModifiedUtc = Format(project.ModifiedUtc),
                LastOpenedUtc = Format(project.LastOpenedUtc), State = (int)project.State, project.SchemaVersion
            }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM photo_documents WHERE project_id=@ProjectId;",
            new { ProjectId = project.Id.ToString("D") }, transaction, cancellationToken: cancellationToken));

        for (var documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            document.AutosavedUtc = DateTimeOffset.UtcNow;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO photo_documents(id,project_id,ordinal,source_path,pixel_width,pixel_height,rotation_degrees,revision,autosaved_utc)
                VALUES(@Id,@ProjectId,@Ordinal,@SourcePath,@PixelWidth,@PixelHeight,@RotationDegrees,@Revision,@AutosavedUtc);
                """,
                new
                {
                    Id = document.Id.ToString("D"), ProjectId = project.Id.ToString("D"), Ordinal = documentIndex,
                    document.SourcePath, document.PixelWidth, document.PixelHeight, document.RotationDegrees,
                    document.Revision, AutosavedUtc = Format(document.AutosavedUtc.Value)
                }, transaction, cancellationToken: cancellationToken));

            for (var annotationIndex = 0; annotationIndex < document.Annotations.Count; annotationIndex++)
            {
                var annotation = document.Annotations[annotationIndex];
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dimension_annotations(
                      id,document_id,ordinal,start_x,start_y,end_x,end_y,label_x,label_y,label_centered,display_text,
                      normalized_millimetres,unit,precision,label,style,colour_hex,created_utc,modified_utc)
                    VALUES(@Id,@DocumentId,@Ordinal,@StartX,@StartY,@EndX,@EndY,@LabelX,@LabelY,@LabelCentered,@DisplayText,
                      @NormalizedMillimetres,@Unit,@Precision,@Label,@Style,@ColourHex,@CreatedUtc,@ModifiedUtc);
                    """,
                    new
                    {
                        Id = annotation.Id.ToString("D"), DocumentId = document.Id.ToString("D"), Ordinal = annotationIndex,
                        StartX = annotation.Start.X, StartY = annotation.Start.Y, EndX = annotation.End.X, EndY = annotation.End.Y,
                        LabelX = annotation.LabelAnchor.X, LabelY = annotation.LabelAnchor.Y, annotation.LabelCentered, annotation.DisplayText,
                        NormalizedMillimetres = annotation.NormalizedMillimetres.ToString(CultureInfo.InvariantCulture),
                        Unit = (int)annotation.Unit, annotation.Precision, annotation.Label, Style = (int)annotation.Style,
                        ColourHex = AnnotationColours.Normalize(annotation.ColourHex, annotation.Style),
                        CreatedUtc = Format(annotation.CreatedUtc), ModifiedUtc = Format(annotation.ModifiedUtc)
                    }, transaction, cancellationToken: cancellationToken));
            }
        }
        transaction.Commit();
    }

    public async Task<AnnoProject?> LoadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var projectRow = await connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            "SELECT * FROM projects WHERE id=@Id;", new { Id = id.ToString("D") }, cancellationToken: cancellationToken));
        if (projectRow is null) return null;

        var documentRows = (await connection.QueryAsync<DocumentRow>(new CommandDefinition(
            "SELECT * FROM photo_documents WHERE project_id=@Id ORDER BY ordinal;",
            new { Id = id.ToString("D") }, cancellationToken: cancellationToken))).ToList();
        var project = Map(projectRow);
        foreach (var row in documentRows)
        {
            var document = Map(row);
            var annotations = await connection.QueryAsync<AnnotationRow>(new CommandDefinition(
                "SELECT * FROM dimension_annotations WHERE document_id=@Id ORDER BY ordinal;",
                new { row.Id }, cancellationToken: cancellationToken));
            document.Annotations.AddRange(annotations.Select(Map));
            project.Documents.Add(document);
        }
        return project;
    }

    public async Task<IReadOnlyList<AnnoProject>> RecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProjectRow>(new CommandDefinition(
            "SELECT * FROM projects ORDER BY last_opened_utc DESC LIMIT @Limit;",
            new { Limit = Math.Clamp(limit, 1, 100) }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM projects WHERE id=@Id;", new { Id = id.ToString("D") }, cancellationToken: cancellationToken));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys=ON;", cancellationToken: cancellationToken));
        return connection;
    }

    private async Task<bool> HasLegacySchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var tables = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM sqlite_schema WHERE type='table' AND name IN ('schema_migrations','projects');",
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);
        return tables.SetEquals(["schema_migrations", "projects"]);
    }

    private static async ValueTask VerifyLegacySchemaAsync(
        DnaXBaselineVerificationContext context,
        CancellationToken cancellationToken)
    {
        var objects = (await context.Connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT name FROM sqlite_schema
            WHERE (type='table' AND name IN ('schema_migrations','projects','photo_documents','dimension_annotations'))
               OR (type='index' AND name IN ('ix_photo_documents_project','ix_annotations_document'));
            """,
            transaction: context.Transaction,
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);
        string[] expectedObjects =
        [
            "schema_migrations", "projects", "photo_documents", "dimension_annotations",
            "ix_photo_documents_project", "ix_annotations_document"
        ];
        var versions = (await context.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT version FROM schema_migrations ORDER BY version;",
            transaction: context.Transaction,
            cancellationToken: cancellationToken))).ToArray();
        if (!objects.SetEquals(expectedObjects) || !versions.SequenceEqual([1L]))
            throw new InvalidOperationException("The pre-DnaX AnnoTape schema could not be verified for baseline adoption.");
    }

    private static AnnoProject Map(ProjectRow row) => new()
    {
        Id = Guid.Parse(row.Id), Title = row.Title, Notes = row.Notes, Location = row.Location,
        CreatedUtc = Parse(row.Created_Utc), ModifiedUtc = Parse(row.Modified_Utc), LastOpenedUtc = Parse(row.Last_Opened_Utc),
        State = (ProjectState)row.State, SchemaVersion = checked((int)row.Schema_Version)
    };

    private static PhotoDocument Map(DocumentRow row) => new()
    {
        Id = Guid.Parse(row.Id), ProjectId = Guid.Parse(row.Project_Id), SourcePath = row.Source_Path,
        PixelWidth = checked((int)row.Pixel_Width), PixelHeight = checked((int)row.Pixel_Height), RotationDegrees = checked((int)row.Rotation_Degrees),
        Revision = checked((int)row.Revision), AutosavedUtc = row.Autosaved_Utc is null ? null : Parse(row.Autosaved_Utc)
    };

    private static DimensionAnnotation Map(AnnotationRow row) => new()
    {
        Id = Guid.Parse(row.Id), Start = new(row.Start_X, row.Start_Y), End = new(row.End_X, row.End_Y),
        LabelAnchor = new(row.Label_X, row.Label_Y), LabelCentered = row.Label_Centered, DisplayText = row.Display_Text,
        NormalizedMillimetres = decimal.Parse(row.Normalized_Millimetres, CultureInfo.InvariantCulture),
        Unit = (MeasurementUnit)row.Unit, Precision = row.Precision is null ? null : checked((int)row.Precision.Value), Label = row.Label,
        Style = (AnnotationStyle)row.Style, ColourHex = AnnotationColours.Normalize(row.Colour_Hex, (AnnotationStyle)row.Style),
        CreatedUtc = Parse(row.Created_Utc), ModifiedUtc = Parse(row.Modified_Utc)
    };

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class ProjectRow
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Notes { get; set; } = "";
        public string? Location { get; set; }
        public string Created_Utc { get; set; } = "";
        public string Modified_Utc { get; set; } = "";
        public string Last_Opened_Utc { get; set; } = "";
        public long State { get; set; }
        public long Schema_Version { get; set; }
    }

    private sealed class DocumentRow
    {
        public string Id { get; set; } = "";
        public string Project_Id { get; set; } = "";
        public long Ordinal { get; set; }
        public string Source_Path { get; set; } = "";
        public long Pixel_Width { get; set; }
        public long Pixel_Height { get; set; }
        public long Rotation_Degrees { get; set; }
        public long Revision { get; set; }
        public string? Autosaved_Utc { get; set; }
    }

    private sealed class AnnotationRow
    {
        public string Id { get; set; } = "";
        public string Document_Id { get; set; } = "";
        public long Ordinal { get; set; }
        public double Start_X { get; set; }
        public double Start_Y { get; set; }
        public double End_X { get; set; }
        public double End_Y { get; set; }
        public double Label_X { get; set; }
        public double Label_Y { get; set; }
        public bool Label_Centered { get; set; }
        public string Display_Text { get; set; } = "";
        public string Normalized_Millimetres { get; set; } = "";
        public long Unit { get; set; }
        public long? Precision { get; set; }
        public string? Label { get; set; }
        public long Style { get; set; }
        public string Colour_Hex { get; set; } = AnnotationColours.Copper;
        public string Created_Utc { get; set; } = "";
        public string Modified_Utc { get; set; } = "";
    }
}
