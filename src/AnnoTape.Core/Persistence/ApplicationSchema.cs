using DnaX.Data.Migrations;

namespace AnnoTape.Core.Persistence;

public static class ApplicationSchema
{
    public static DnaXMigrationManifest Manifest { get; } = new(
        currentVersion: 2,
        migrations:
        [
            DnaXMigration.EmbeddedSql(
                1,
                "initial-schema",
                "Create the AnnoTape project, document, and annotation schema",
                typeof(ApplicationSchema).Assembly,
                "AnnoTape.Core.Persistence.Migrations.001_initial.sql"),
            DnaXMigration.EmbeddedSql(
                2,
                "remove-legacy-ledger",
                "Remove the pre-DnaX migration ledger",
                typeof(ApplicationSchema).Assembly,
                "AnnoTape.Core.Persistence.Migrations.002_remove_legacy_ledger.sql")
        ]);
}

