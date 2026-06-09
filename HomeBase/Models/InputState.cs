using System.Numerics;

namespace HomeBase.Models;

public sealed class InputState
{
    public Vector2 MousePosition { get; set; }

    public bool LeftMouseDown { get; set; }
    public bool LeftMousePressed { get; set; }
    public bool LeftMouseReleased { get; set; }

    public bool RightMouseDown { get; set; }
    public bool RightMousePressed { get; set; }
    public bool RightMouseReleased { get; set; }

    public string? ActiveElementId { get; set; }
}