using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Creator;

/// <summary>Установка мода из ModLaunch Hub в игру.</summary>
public static class HubInstaller
{
    public static async Task<JsonObject> Install(ModRegistry registry, string id, IProgress<double>? progress, CancellationToken ct, HubVersion? version = null)
    {
        var mod = await Hub.Get(id, ct) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
        var meta = new JsonObject { ["url"] = null, ["icon"] = mod.Images.FirstOrDefault() };
        if (mod.IsPackage)
        {
            var zip = await Hub.Download(mod, version, progress, ct);
            try
            {
                if (registry.Has(mod.Id)) registry.Remove(mod.Id);
                return await Installer.InstallAny(registry, zip, new JsonObject
                {
                    ["id"] = mod.Id, ["name"] = mod.Name, ["version"] = version?.Version ?? mod.Version, ["author"] = mod.Author,
                    ["source"] = "hub", ["icon"] = mod.Images.FirstOrDefault(),
                }, new Progress<InstallStep>(_ => { }), ct);
            }
            finally { try { File.Delete(zip); } catch { } }
        }
        var build = ModScript.Compile(mod.Code);
        if (!build.Ok) throw new InvalidOperationException(I18n.T("cr.build.hasErrors"));
        var packed = await Projects.Pack(build, null, ct);
        _ = Hub.CountDownload(mod.Id);
        progress?.Report(1);
        return Projects.Install(registry, build, packed, mod.Id, "hub", meta);
    }
}
