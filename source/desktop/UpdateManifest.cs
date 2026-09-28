using System;
using System.Text.Json;
namespace ZXGT7Guard;
internal sealed record UpdateManifest(Version Version, Uri Download)
{
    public static Uri ParseHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new FormatException("Adresse HTTPS invalide.");
        return uri;
    }
    public static UpdateManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!Version.TryParse(root.GetProperty("version").GetString(), out var version) || version.Build < 0)
            throw new FormatException("Version attendue : majeur.mineur.correctif.");
        version = new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        return new UpdateManifest(version, ParseHttps(root.GetProperty("downloadUrl").GetString() ?? ""));
    }
}
