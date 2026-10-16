using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Blazor treats stderr as fatal; handled engine diagnostics stay visible without a false crash banner.
Console.SetError(Console.Out);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<DoomSharp.Browser.App>("#app");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
await builder.Build().RunAsync();
