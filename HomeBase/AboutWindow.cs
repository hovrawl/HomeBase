using System;
using System.Threading;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Input;
using SkiaSharp;

namespace HomeBase;

public static class AboutWindow
{
    public static void Show()
    {
        var thread = new Thread(RunWindow);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunWindow()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(480, 160);
        options.Title = "About HomeBase";
        options.WindowBorder = WindowBorder.Fixed;
        options.WindowState = WindowState.Normal;
        options.TopMost = true;

        using IWindow window = Window.Create(options);

        GRContext? grContext = null;
        GRBackendRenderTarget? renderTarget = null;
        SKSurface? surface = null;
        
        window.Load += () =>
        {
            var input = window.CreateInput();
            foreach (var keyboard in input.Keyboards)
            {
                keyboard.KeyDown += (kb, key, code) =>
                {
                    if (key == Key.Escape) window.Close();
                };
            }

            var grGlInterface = GRGlInterface.Create(name =>
                window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : IntPtr.Zero);
            grGlInterface.Validate();
            grContext = GRContext.CreateGl(grGlInterface);
        };

        window.Render += (delta) =>
        {
            if (grContext == null) return;

            var framebufferSize = window.FramebufferSize;
            if (renderTarget == null || renderTarget.Width != framebufferSize.X || renderTarget.Height != framebufferSize.Y)
            {
                renderTarget?.Dispose();
                surface?.Dispose();

                renderTarget = new GRBackendRenderTarget(
                    framebufferSize.X,
                    framebufferSize.Y,
                    0,
                    8,
                    new GRGlFramebufferInfo(0, 0x8058)); // 0x8058 = GL_RGBA8

                surface = SKSurface.Create(
                    grContext,
                    renderTarget,
                    GRSurfaceOrigin.BottomLeft,
                    SKColorType.Rgba8888);
            }

            if (surface == null) return;

            grContext.ResetContext();
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(242, 242, 242));

            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                TextSize = 16,
                Typeface = SKTypeface.FromFamilyName("Segoe UI")
            };

            string[] lines = {
                "Home Base holds shortcuts to apps, quick notes and task lists.",
                "Control + Space to Summon.",
                "Escape to Dismiss"
            };

            float y = 50;
            foreach (var line in lines)
            {
                canvas.DrawText(line, 30, y, paint);
                y += 30;
            }

            canvas.Flush();
        };

        window.Closing += () =>
        {
            surface?.Dispose();
            renderTarget?.Dispose();
            grContext?.Dispose();
        };

        window.Run();
    }
}
