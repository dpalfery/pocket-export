namespace PocketExport.Api;

/// <summary>
///     Entry point for the PocketExport API host application.
/// </summary>
public static class Program
{
    /// <summary>
    ///     Configures and starts the web application host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        WebApplication app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.Run();
    }
}