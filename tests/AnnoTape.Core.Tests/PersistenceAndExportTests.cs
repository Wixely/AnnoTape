using AnnoTape.Core.Export;
using AnnoTape.Core.Models;
using AnnoTape.Core.Persistence;
using DnaX.Data.Migrations.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace AnnoTape.Core.Tests;

[TestClass]
public sealed class PersistenceAndExportTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AnnoTape.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task RepositoryRoundTripsProjectAndAnnotations()
    {
        var repository = new ProjectRepository(Path.Combine(_directory, "data.db"));
        await repository.InitializeAsync();
        var project = new AnnoProject { Title = "Kitchen", Notes = "North wall" };
        var document = new PhotoDocument { ProjectId = project.Id, SourcePath = "source.jpg", PixelWidth = 4000, PixelHeight = 3000 };
        document.Annotations.Add(new DimensionAnnotation { DisplayText = "2.4", NormalizedMillimetres = 2400, Unit = MeasurementUnit.Metres, Label = "opening" });
        project.Documents.Add(document);

        await repository.SaveAsync(project);
        var loaded = await repository.LoadAsync(project.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Kitchen", loaded.Title);
        Assert.HasCount(1, loaded.Documents);
        Assert.AreEqual("opening", loaded.Documents[0].Annotations.Single().Label);
        Assert.AreEqual(2400m, loaded.Documents[0].Annotations.Single().NormalizedMillimetres);

        await using var connection = new SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var ledger = connection.CreateCommand();
        ledger.CommandText = "SELECT group_concat(\"Id\", ',') FROM (SELECT \"Id\" FROM \"__DnaXMigrations\" ORDER BY \"Version\");";
        Assert.AreEqual("initial-schema,remove-legacy-ledger", await ledger.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task EveryHistoricalMigrationChainMatchesTheCanonicalSchema()
    {
        var verification = await DnaXSqliteMigrationVerifier.VerifyAllHistoricalVersionsAsync(ApplicationSchema.Manifest);

        Assert.HasCount(2, verification.HistoricalVersions);
        Assert.IsFalse(string.IsNullOrWhiteSpace(verification.CanonicalSchemaSnapshot));
        Assert.IsTrue(verification.HistoricalVersions.All(version =>
            version.SchemaSnapshot == verification.CanonicalSchemaSnapshot));
    }

    [TestMethod]
    public async Task ExistingPreDnaXDatabaseIsBaselinedAndUpgraded()
    {
        var databasePath = Path.Combine(_directory, "legacy.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = ReadEmbeddedSql("AnnoTape.Core.Persistence.Migrations.001_initial.sql") +
                "CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);" +
                "INSERT INTO schema_migrations(version, applied_utc) VALUES(1, '2026-01-01T00:00:00.0000000+00:00');" +
                "INSERT INTO projects(id,title,notes,created_utc,modified_utc,last_opened_utc,state,schema_version) " +
                "VALUES('00000000-0000-0000-0000-000000000001','Existing','kept','2026-01-01','2026-01-01','2026-01-01',0,1);";
            await command.ExecuteNonQueryAsync();
        }

        var repository = new ProjectRepository(databasePath);
        await repository.InitializeAsync();

        var project = await repository.LoadAsync(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.AreEqual("Existing", project?.Title);
        await using var migrated = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await migrated.OpenAsync();
        await using var commandAfter = migrated.CreateCommand();
        commandAfter.CommandText =
            "SELECT (SELECT count(*) FROM \"__DnaXMigrations\") || ':' || " +
            "(SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='schema_migrations');";
        Assert.AreEqual("2:0", await commandAfter.ExecuteScalarAsync());
    }

    [TestMethod]
    public void PendingOperationIsDurableAndClearable()
    {
        var storage = new CrashSafeStorage(_directory);
        var operation = new PendingExternalOperation(Guid.NewGuid(), "picker", Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        storage.WritePending(operation);
        Assert.AreEqual(operation.Id, storage.ReadPending()?.Id);
        storage.ClearPending();
        Assert.IsNull(storage.ReadPending());
    }

    [TestMethod]
    public void HostRestartProjectMarkerIsConsumedOnce()
    {
        var storage = new CrashSafeStorage(_directory);
        var projectId = Guid.NewGuid();

        storage.WriteHostRestartProject(projectId);

        Assert.AreEqual(projectId, storage.TakeHostRestartProject());
        Assert.IsNull(storage.TakeHostRestartProject());
    }

    [TestMethod]
    public async Task ExportRendersAtOrientedSourceResolution()
    {
        var sourcePath = Path.Combine(_directory, "source.png");
        using (var bitmap = new SKBitmap(80, 40))
        {
            bitmap.Erase(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(sourcePath);
            encoded.SaveTo(output);
        }
        var document = new PhotoDocument { SourcePath = sourcePath, PixelWidth = 80, PixelHeight = 40, RotationDegrees = 90 };
        document.Annotations.Add(new DimensionAnnotation { Start = new(0.1, 0.5), End = new(0.9, 0.5), LabelAnchor = new(0.5, 0.3) });
        await using var destination = new MemoryStream();
        await new AnnotationExporter().ExportAsync(document, destination, new ExportOptions(ExportFormat.Png));
        destination.Position = 0;
        using var result = SKBitmap.Decode(destination);
        Assert.AreEqual(40, result.Width);
        Assert.AreEqual(80, result.Height);
    }

    private static string ReadEmbeddedSql(string resourceName)
    {
        using var stream = typeof(ApplicationSchema).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
