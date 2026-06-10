using Silk.NET.Windowing;
using SkiaSharp;

namespace HomeBase.Render;

public static class Helpers
{
    public static float LogicalToFramebufferPixels(float framebufferScale, float logicalPixels)
    {
        return logicalPixels * framebufferScale;
    }
    
    internal static SKImage? GetDockItemIcon(string path)
    {
        if (_iconCache.TryGetValue(path, out SKImage? image))
        {
            return image;
        }

        image = Windows.Windows.LoadIconImageForFile(path);


        if (image is not null)
        {
            _iconCache[path] = image;
        }

        return image;
    }
}