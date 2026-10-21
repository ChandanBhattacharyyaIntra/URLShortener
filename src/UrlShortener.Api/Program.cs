using System.Text.Json.Serialization;
using UrlShortener.Api.Orchestrator;
using UrlShortener.Api.Shortener;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var storePath = builder.Configuration["StorePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "store.json");

builder.Services.AddSingleton(new JsonUrlStore(storePath));
builder.Services.AddSingleton<UrlShortenerService>();
builder.Services.AddSingleton<OrchestratorEngine>();

var app = builder.Build();

var store = app.Services.GetRequiredService<JsonUrlStore>();
await store.LoadAsync();

app.UseMiddleware<RateLimitMiddleware>();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (AppException ex)
    {
        context.Response.StatusCode = ex.Status;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOrchestrator();
app.MapShortener();

app.Run();

public partial class Program;
