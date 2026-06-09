using System.Numerics;
using Silk.NET.Maths;

namespace HomeBase.Models;

public sealed class UIAnimation
{
    public UIAnimationState State { get; private set; } = UIAnimationState.Hidden;

    public Vector2D<int> ShownPosition { get; set; }
    public Vector2D<int> HiddenPosition { get; set; }

    private Vector2 _startPosition;
    private Vector2 _targetPosition;
    private float _time;

    public float Duration { get; set; } = 0.22f;

    public void Show(Vector2D<int> currentPosition)
    {
        _startPosition = new Vector2(currentPosition.X, currentPosition.Y);
        _targetPosition = new Vector2(ShownPosition.X, ShownPosition.Y);
        _time = 0f;
        State = UIAnimationState.Showing;
    }

    public void Hide(Vector2D<int> currentPosition)
    {
        _startPosition = new Vector2(currentPosition.X, currentPosition.Y);
        _targetPosition = new Vector2(HiddenPosition.X, HiddenPosition.Y);
        _time = 0f;
        State = UIAnimationState.Hiding;
    }

    public Vector2D<int>? Update(double deltaTime)
    {
        if (State is not UIAnimationState.Showing and not UIAnimationState.Hiding)
        {
            return null;
        }

        _time += (float)deltaTime;

        float t = Math.Clamp(_time / Duration, 0f, 1f);
        float easedT = State == UIAnimationState.Showing
            ? EaseOutCubic(t)
            : EaseInCubic(t);

        Vector2 position = Lerp(_startPosition, _targetPosition, easedT);

        if (t >= 1f)
        {
            State = State == UIAnimationState.Showing
                ? UIAnimationState.Shown
                : UIAnimationState.Hidden;
        }

        return new Vector2D<int>(
            (int)MathF.Round(position.X),
            (int)MathF.Round(position.Y));
    }

    private static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - MathF.Pow(1f - t, 3f);
    }

    private static float EaseInCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * t;
    }

    private static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return a + (b - a) * t;
    }
}