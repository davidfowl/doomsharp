using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: DoomSharp.Serve <published-wwwroot> [--urls http://127.0.0.1:5187]");
    return 2;
}
string root = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(root, "index.html")))
{
    Console.Error.WriteLine("Published wwwroot/index.html does not exist: " + root);
    return 2;
}
var builder = WebApplication.CreateBuilder(args.Skip(1).ToArray());
if (!args.Contains("--urls")) builder.WebHost.UseUrls("http://127.0.0.1:5187");
var app = builder.Build();
FileExtensionContentTypeProvider types = new();
types.Mappings[".wasm"] = "application/wasm";
types.Mappings[".dat"] = "application/octet-stream";
types.Mappings[".wad"] = "application/octet-stream";
using PhysicalFileProvider files = new(root);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
await app.RunAsync();
return 0;
