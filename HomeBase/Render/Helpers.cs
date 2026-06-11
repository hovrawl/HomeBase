using Silk.NET.Windowing;
using SkiaSharp;

namespace HomeBase.Render;

public static class Helpers
{
    public static float LogicalToFramebufferPixels(float framebufferScale, float logicalPixels)
    {
        return logicalPixels * framebufferScale;
    }
}