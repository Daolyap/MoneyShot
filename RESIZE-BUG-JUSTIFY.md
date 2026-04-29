# Resize/Drag Snap Bug – Root Cause & Fix

## Symptoms

When resizing or moving annotation elements, shapes snap/jump to an unexpected
position on screen. Moving an element outside the canvas border could
permanently change where it ends up. The bug was present in both active PR
branches but absent from `main`.

## Root Cause

Two changes introduced together in `copilot/address-bugs-and-enhancements`
interact to produce the snap:

### 1. `DrawingCanvas.CaptureMouse()` added to cursor-mode operations

Mouse capture was added to both drag (`_isDragging`) and resize (`_isResizing`)
operations. With mouse capture active, `MouseMove` events continue to fire even
when the cursor leaves the canvas—so the handler receives positions whose
coordinates are outside `[0, DrawingCanvas.Width] × [0, DrawingCanvas.Height]`.

### 2. `ClampToCanvasBounds` applied to all `MouseMove` and `MouseDown` points

A clamping helper was added and applied unconditionally to every mouse event:

```csharp
// Canvas_MouseMove — before the fix
var currentPoint = ClampToCanvasBounds(e.GetPosition(DrawingCanvas));
```

This clamps out-of-canvas positions to the canvas edge, which is correct for
*drawing* tools (preventing new elements from being placed outside the image)
but is wrong for *cursor-mode* operations.

### How the snap occurs

#### Drag snap (the dominant failure mode)

`_dragStartPoint` is updated to `currentPoint` on every `MouseMove` frame:

```csharp
MoveElement(_selectedElement, deltaX, deltaY);
_dragStartPoint = currentPoint;   // ← clamped value
```

Scenario:
1. User drags element toward the right edge. Cursor exits canvas (x > canvas.Width).
2. `currentPoint.X` is clamped to `canvas.Width`. Element stops moving.
3. `_dragStartPoint.X` is now frozen at `canvas.Width`.
4. User moves cursor to the left side of the canvas (x ≈ 100).
5. `deltaX = 100 − canvas.Width = −(canvas.Width − 100)` — a large negative number.
6. Element snaps far to the left in one frame. **Permanent position change.**

The "invisible anchor" the user observed is the canvas edge coordinate stored in
`_dragStartPoint` while the cursor was outside.

#### Resize snap (subtler variant)

For resize operations the start point `_resizeStartPoint` is *not* updated each
frame, so the snap is less severe. But:

* If the handle click position is near the canvas edge it could be clamped,
  shifting `_resizeStartPoint` by 2–6 px and causing an apparent jump at the
  very start of the resize.
* When the cursor exits the canvas on the growing side (e.g., dragging the right
  handle past the right edge), `currentPoint` freezes at the canvas edge. When
  the cursor re-enters from the same direction the delta is continuous, but if
  the user swings the cursor across to the opposite side the element abruptly
  snaps to a size proportional to the re-entry position rather than the intended
  final size.

#### Why `main` was unaffected

`main` has no `DrawingCanvas.CaptureMouse()` for cursor operations and no
`ClampToCanvasBounds`. Without mouse capture, `MouseMove` events simply stop
arriving when the cursor leaves the canvas, so `_dragStartPoint` is never
updated to a clamped edge value. The occasional small jump on cursor re-entry
that exists in `main` is barely noticeable because the cursor typically
re-enters close to the exit point.

## Fix (commit on `copilot/fix-resize-snapping-issue`)

Use the **raw (unclamped)** mouse position for all cursor-mode operations; keep
clamping only for drawing/crop tools where confining the stroke to the image
bounds is desirable.

### `Canvas_MouseDown`

```csharp
var rawPoint   = e.GetPosition(DrawingCanvas);
var clickPoint = _currentTool == AnnotationTool.Cursor
    ? rawPoint
    : ClampToCanvasBounds(rawPoint);
```

`_resizeStartPoint` and `_dragStartPoint` are set to `rawPoint`, eliminating
the initial 2–6 px offset that arose when a handle sat just outside the canvas
boundary.

### `Canvas_MouseMove`

```csharp
var rawPoint     = e.GetPosition(DrawingCanvas);
var currentPoint = ClampToCanvasBounds(rawPoint); // used only for drawing/crop

// Resize – raw position
if (_currentTool == AnnotationTool.Cursor && _isResizing ...)
{
    ResizeElement(_selectedElement, rawPoint);
    return;
}

// Drag – raw position; _dragStartPoint tracks the true cursor, never the edge
if (_currentTool == AnnotationTool.Cursor && _isDragging ...)
{
    var deltaX = rawPoint.X - _dragStartPoint.X;
    var deltaY = rawPoint.Y - _dragStartPoint.Y;
    MoveElement(_selectedElement, deltaX, deltaY);
    _dragStartPoint = rawPoint;
    return;
}
// Drawing/crop tools continue to use clamped `currentPoint`
```

With this change `_dragStartPoint` always holds the true cursor coordinate.
When the cursor exits the canvas and re-enters, the delta is computed from the
real re-entry position, not from a frozen canvas-edge coordinate, and no snap
occurs.

## Additional fixes in this PR branch

* **Delta-based resize** (earlier commit `f6ee31b`): reverted `ResizeElement`
  from the absolute cursor-as-edge formula back to the delta approach so that
  `deltaX = 0` at drag start → no jump on first `MouseMove`.

* **Path (arrow) excluded from resize handles** (commit `5dd83d2`): arrow
  `Path` elements have `Width = NaN` / `Height = NaN` because their visual size
  comes from geometry, not WPF layout properties. The NaN values propagate
  through the resize math (`newLeft = originalLeft + (NaN − NaN) = NaN`),
  permanently corrupting `Canvas.Left` via `Canvas.SetLeft(path, NaN)` and
  snapping the arrow to an unexpected on-screen position. Resize handles are no
  longer created for `Path` elements; they remain fully selectable and movable.
