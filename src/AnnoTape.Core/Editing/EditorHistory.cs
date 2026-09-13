using AnnoTape.Core.Models;

namespace AnnoTape.Core.Editing;

public interface IEditorCommand
{
    string Description { get; }
    void Execute(PhotoDocument document);
    void Undo(PhotoDocument document);
}

public sealed class EditorHistory
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(PhotoDocument document, IEditorCommand command)
    {
        command.Execute(document);
        document.Revision++;
        _undo.Push(command);
        _redo.Clear();
    }

    public bool Undo(PhotoDocument document)
    {
        if (!_undo.TryPop(out var command)) return false;
        command.Undo(document);
        document.Revision++;
        _redo.Push(command);
        return true;
    }

    public bool Redo(PhotoDocument document)
    {
        if (!_redo.TryPop(out var command)) return false;
        command.Execute(document);
        document.Revision++;
        _undo.Push(command);
        return true;
    }
}

public sealed class AddAnnotationCommand(DimensionAnnotation annotation) : IEditorCommand
{
    private readonly DimensionAnnotation _annotation = annotation.Copy();
    public string Description => "Add measurement";
    public void Execute(PhotoDocument document) => document.Annotations.Add(_annotation.Copy());
    public void Undo(PhotoDocument document) => document.Annotations.RemoveAll(item => item.Id == _annotation.Id);
}

public sealed class DeleteAnnotationCommand(DimensionAnnotation annotation, int index) : IEditorCommand
{
    private readonly DimensionAnnotation _annotation = annotation.Copy();
    public string Description => "Delete measurement";
    public void Execute(PhotoDocument document) => document.Annotations.RemoveAll(item => item.Id == _annotation.Id);
    public void Undo(PhotoDocument document) => document.Annotations.Insert(Math.Clamp(index, 0, document.Annotations.Count), _annotation.Copy());
}

public sealed class ReplaceAnnotationCommand(DimensionAnnotation before, DimensionAnnotation after) : IEditorCommand
{
    private readonly DimensionAnnotation _before = before.Copy();
    private readonly DimensionAnnotation _after = after.Copy();
    public string Description => "Edit measurement";
    public void Execute(PhotoDocument document) => Replace(document, _after);
    public void Undo(PhotoDocument document) => Replace(document, _before);

    private static void Replace(PhotoDocument document, DimensionAnnotation replacement)
    {
        var index = document.Annotations.FindIndex(item => item.Id == replacement.Id);
        if (index < 0) throw new InvalidOperationException("The annotation no longer exists.");
        document.Annotations[index] = replacement.Copy();
    }
}

public sealed class ReplaceAnnotationsCommand(
    IReadOnlyList<DimensionAnnotation> before,
    IReadOnlyList<DimensionAnnotation> after) : IEditorCommand
{
    private readonly DimensionAnnotation[] _before = before.Select(item => item.Copy()).ToArray();
    private readonly DimensionAnnotation[] _after = after.Select(item => item.Copy()).ToArray();
    public string Description => "Edit all measurements";
    public void Execute(PhotoDocument document) => Replace(document, _after);
    public void Undo(PhotoDocument document) => Replace(document, _before);

    private static void Replace(PhotoDocument document, IReadOnlyList<DimensionAnnotation> replacements)
    {
        foreach (var replacement in replacements)
        {
            var index = document.Annotations.FindIndex(item => item.Id == replacement.Id);
            if (index < 0) throw new InvalidOperationException("An annotation no longer exists.");
            document.Annotations[index] = replacement.Copy();
        }
    }
}
