using DnaX.Data.Migrations;

namespace AnnoTape.Core.Persistence;

public static class ApplicationSchema
{
    public static DnaXMigrationManifest Manifest { get; } = new(
        currentVersion: 4,
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
                "AnnoTape.Core.Persistence.Migrations.002_remove_legacy_ledger.sql"),
            DnaXMigration.EmbeddedSql(
                3,
                "add-annotation-colour",
                "Persist arbitrary annotation colours while preserving legacy styles",
                typeof(ApplicationSchema).Assembly,
                "AnnoTape.Core.Persistence.Migrations.003_add_annotation_colour.sql"),
            DnaXMigration.EmbeddedSql(
                4,
                "add-centered-label-state",
                "Track automatic versus manually positioned measurement labels",
                typeof(ApplicationSchema).Assembly,
                "AnnoTape.Core.Persistence.Migrations.004_add_centered_label_state.sql")
        ]);
}
