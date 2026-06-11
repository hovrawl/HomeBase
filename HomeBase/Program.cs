using Silk.NET.Input.Glfw;
using Silk.NET.Windowing.Glfw;
using HomeBase;


// Platform
GlfwWindowing.Use();
GlfwInput.RegisterPlatform();

// App
using var app = new HomeBaseApp();

app.Run();