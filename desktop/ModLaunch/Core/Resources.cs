using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>Встроенные в программу файлы (Assets/*.config.json).</summary>
public static class Resources
{
    public static JsonNode? Json(string name)
    {
        try
        {
            using var stream = typeof(Resources).Assembly.GetManifestResourceStream(name);
            return stream is null ? null : JsonNode.Parse(stream);
        }
        catch { return null; }
    }
}
