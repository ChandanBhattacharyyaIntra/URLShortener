using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), "urlshortener-tests", Guid.NewGuid().ToString("n"), "store.json");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        builder.UseSetting("StorePath", _storePath);
        builder.UseEnvironment("Testing");
    }
}
