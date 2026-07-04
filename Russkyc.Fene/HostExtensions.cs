using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Russkyc.Fene;

public static class HostExtensions
{
    public static void MapFeneFontsApi(this IEndpointRouteBuilder app)
    {
        // GET /fonts/list - Returns JSON list of available fonts
        app.MapGet("/fonts/list", async (context) =>
        {
            context.Response.ContentType = "application/json";
            try
            {
                var fonts = Platform.GetInstalledFonts();
                var fontList = fonts.Keys.Select(name => new
                {
                    name,
                    url = $"/fonts/download/{Uri.EscapeDataString(name)}"
                }).ToList();

                var json = JsonSerializer.Serialize(fontList);
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(json);
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Failed to retrieve fonts" }));
            }
        }).WithName("GetAvailableFonts");

        // GET /fonts/download/{fontName} - Downloads system font file
        app.MapGet("/fonts/download/{fontName}", async (string fontName, HttpContext context) =>
        {
            try
            {
                var fonts = Platform.GetInstalledFonts();
                
                if (!fonts.TryGetValue(fontName, out var filePath) || !File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = $"Font '{fontName}' not found" }));
                    return;
                }

                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var contentType = extension switch
                {
                    ".ttf" => "font/ttf",
                    ".otf" => "font/otf",
                    ".woff" => "font/woff",
                    ".woff2" => "font/woff2",
                    ".ttc" => "font/ttf",
                    ".otc" => "font/otf",
                    _ => "application/octet-stream"
                };

                context.Response.ContentType = contentType;
                context.Response.Headers["Cache-Control"] = "public, max-age=31536000"; // 1 year
                context.Response.Headers["Accept-Ranges"] = "bytes";

                var fileInfo = new FileInfo(filePath);
                context.Response.ContentLength = fileInfo.Length;
                context.Response.StatusCode = 200;
                
                await using var fileStream = File.OpenRead(filePath);
                await fileStream.CopyToAsync(context.Response.Body);
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Failed to download font" }));
            }
        }).WithName("DownloadFont");

        // GET /fonts/css - Returns CSS @font-face declarations for all fonts
        app.MapGet("/fonts/css", async (context) =>
        {
            context.Response.ContentType = "text/css; charset=utf-8";
            try
            {
                var fonts = Platform.GetInstalledFonts();
                var css = new StringBuilder();
                css.AppendLine("/* Auto-generated system fonts CSS - served dynamically */");
                
                foreach (var (fontName, filePath) in fonts)
                {
                    var escapedName = fontName.Replace("\"", "\\\"");
                    var urlEncodedName = Uri.EscapeDataString(fontName);
                    
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();
                    var cssFormat = extension switch
                    {
                        ".woff2" => "woff2",
                        ".woff" => "woff",
                        ".otf" => "opentype",
                        _ => "truetype"
                    };

                    css.Append($$"""
                    @font-face {
                        font-family: "{{escapedName}}";
                        src: url('/fonts/download/{{urlEncodedName}}') format('{{cssFormat}}');
                        font-display: block;
                    }
                    """).AppendLine();
                }

                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(css.ToString());
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Failed to generate font CSS" }));
            }
        }).WithName("GetFontsCss");
    }
}