
using Silk.NET.Maths;

namespace HomeBase.Models;

public class InputState
{
    public Vector2D<int> MousePosition { get; set; }

    public bool LeftMouseDown { get; set; }
    public bool LeftMousePressed { get; set; }
    public bool LeftMouseReleased { get; set; }

    public bool RightMouseDown { get; set; }
    public bool RightMousePressed { get; set; }
    public bool RightMouseReleased { get; set; }

    public void UpdateMouse(Vector2D<int> mousePosition, bool leftDown, bool rightDown)
    {
        bool previousLeftMouseDown = LeftMouseDown;
        bool previousRightMouseDown = RightMouseDown;

        MousePosition = mousePosition;

        LeftMouseDown = leftDown;
        LeftMousePressed = leftDown && !previousLeftMouseDown;
        LeftMouseReleased = !leftDown && previousLeftMouseDown;

        RightMouseDown = rightDown;
        RightMousePressed = rightDown && !previousRightMouseDown;
        RightMouseReleased = !rightDown && previousRightMouseDown;
    }
}
