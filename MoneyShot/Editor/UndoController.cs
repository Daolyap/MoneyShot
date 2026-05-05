using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MoneyShot.Views;

namespace MoneyShot.Editor;

/// <summary>
/// Owns the editor undo stack and the action records. EditorWindow pushes onto this from the
/// places where mutations happen, and calls <see cref="Undo"/> when the user triggers undo.
/// </summary>
internal sealed class UndoController
{
    private readonly Stack<IUndoAction> _stack = new();

    public int Count => _stack.Count;

    public void Push(IUndoAction action) => _stack.Push(action);

    public void Clear() => _stack.Clear();

    public void Undo(EditorWindow window)
    {
        if (_stack.Count == 0) return;
        _stack.Pop().Undo(window);
    }

    public interface IUndoAction
    {
        void Undo(EditorWindow window);
    }

    public sealed class AddElementUndoAction(UIElement element) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoAddElement(element);
    }

    public sealed class RemoveElementUndoAction(UIElement element, int index) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoRemoveElement(element, index);
    }

    public sealed class CropUndoAction(BitmapSource previousImage, IReadOnlyList<UIElement> previousElements, int previousNumberCounter) : IUndoAction
    {
        public void Undo(EditorWindow window) =>
            window.UndoCrop(previousImage, previousElements, previousNumberCounter);
    }

    public sealed class ResizeUndoAction(UIElement element, ElementState previousState) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoResize(element, previousState);
    }
}
