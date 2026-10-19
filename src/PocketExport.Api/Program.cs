using PocketExport.Api.Services;
using PocketExport.Core.Coordination;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Metadata;
using PocketExport.Core.Ml;
using PocketExport.Core.Orchestration;
using PocketExport.Core.Pocket;
using PocketExport.Core.Routing;
using PocketExport.Core.Speakers;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using PocketExport.Core.Taxonomy;

namespace PocketExport.Api;

/// <summary>
/// Entry point for the PocketExport API host application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Configures and starts the web application host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        var vaultRoot = builder.Configuration["PocketExport:VaultRoot"]
            ?? Environment.GetEnvironmentVariable("POCKET_EXPORT_VAULT_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PocketVault");

        var localDbPath = builder.Configuration["PocketExport:DatabasePath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketExport", "index.db");

        var lmStudioEndpoint = builder.Configuration["PocketExport:LmStudioEndpoint"] ?? "http://localhost:1234/v1/chat/completions";
        var audioWorkerEndpoint = builder.Configuration["PocketExport:AudioWorkerEndpoint"] ?? "http://127.0.0.1:8765";
        var pocketApiToken = builder.Configuration["PocketExport:PocketApiToken"] ?? Environment.GetEnvironmentVariable("POCKET_API_TOKEN") ?? "pk_placeholder";

        builder.Services.AddControllers();
        builder.Services.AddSingleton<IVaultPathResolver>(_ => new VaultPathResolver(vaultRoot));
        builder.Services.AddSingleton<ILedgerManager, LedgerManager>();
        builder.Services.AddSingleton(_ => new SqliteIndexContext(localDbPath));
        builder.Services.AddSingleton<LedgerProjectionService>();
        builder.Services.AddSingleton<IDistributedLeaseService, DistributedLeaseService>();
        builder.Services.AddSingleton<ISpeakerVoicebankService, SpeakerVoicebankService>();
        builder.Services.AddSingleton<IAudioContainerTagger, AudioContainerTagger>();
        builder.Services.AddSingleton<IVaultFolderRouter, VaultFolderRouter>();
        builder.Services.AddScoped<IAudioSnippetStreamer, AudioSnippetStreamer>();

        builder.Services.AddHttpClient<IPocketApiClient, PocketApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://public.heypocketai.com/api/v1/");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pocketApiToken);
        });

        builder.Services.AddHttpClient<IAudioWorkerClient, AudioWorkerClient>(client =>
        {
            client.BaseAddress = new Uri(audioWorkerEndpoint);
        });

        builder.Services.AddHttpClient<ITaxonomyClassifier, TaxonomyClassifier>(client =>
        {
            client.BaseAddress = new Uri(lmStudioEndpoint);
        });

        builder.Services.AddHttpClient<ITemplateSummarizer, TemplateSummarizer>(client =>
        {
            client.BaseAddress = new Uri(lmStudioEndpoint);
        });

        builder.Services.AddScoped<PocketSyncEngine>();
        builder.Services.AddScoped<IFullPipelineOrchestrator, FullPipelineOrchestrator>();

        WebApplication app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapControllers();

        app.Run();
    }
}