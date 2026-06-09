using Silk.NET.Input.Glfw;
using Silk.NET.Windowing.Glfw;
using HomeBase;


// Platform
GlfwWindowing.Use();
GlfwInput.RegisterPlatform();

// App
using var app = new HomeBaseApp();

app.Run();


public enum ViewMode { All, Grouped }

public enum UIAnimationState { Hidden, Showing, Shown, Hiding }