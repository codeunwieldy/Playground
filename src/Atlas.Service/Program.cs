using Atlas.AI;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Service.Services;
using Atlas.Storage;
using Atlas.Storage.Repositories;

var builder = Host.CreateApplicationBuilder(args);

// Enable Windows Service hosting
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AtlasFileIntelligence";
});

builder.Services.Configure<AtlasServiceOptions>(builder.Configuration.GetSection(AtlasServiceOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.SectionName));

builder.Services.AddSingleton(static _ => PolicyProfileFactory.CreateDefault());
builder.Services.AddSingleton<PathSafetyClassifier>();
builder.Services.AddSingleton<AtlasPolicyEngine>();
builder.Services.AddSingleton<RollbackPlanner>();

builder.Services.AddSingleton(static serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
    return new AtlasDatabaseBootstrapper(options);
});

builder.Services.AddSingleton<IAtlasPlanningClient>(serviceProvider =>
{
    var optionsAccessor = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>();
    var client = new HttpClient
    {
        BaseAddress = new Uri(optionsAccessor.Value.BaseUrl.TrimEnd('/'))
    };
    var conversationRepo = serviceProvider.GetService<IConversationRepository>();
    return new OpenAIResponsesPlanningClient(client, optionsAccessor, conversationRepo);
});

builder.Services.AddSingleton<FileScanner>();
builder.Services.AddSingleton<PlanExecutionService>();
builder.Services.AddSingleton<OptimizationScanner>();

builder.Services.AddSingleton<Atlas.Storage.Repositories.SqliteConnectionFactory>();
builder.Services.AddSingleton<IPlanRepository, PlanRepository>();
builder.Services.AddSingleton<IRecoveryRepository, RecoveryRepository>();
builder.Services.AddSingleton<IOptimizationRepository, OptimizationRepository>();
builder.Services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();

builder.Services.AddHostedService<AtlasStartupWorker>();
builder.Services.AddHostedService<AtlasPipeServerWorker>();

var host = builder.Build();
await host.RunAsync();
