using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace UpRes.Core;

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    LocalIsNewer,
    NoReleases,
    Unknown,
}

/// <summary>Campos usados do JSON de GET /repos/{owner}/{repo}/releases/latest.</summary>
[DataContract]
public sealed class ReleaseInfo
{
    [DataMember(Name = "tag_name")] public string Tag { get; set; } = "";
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "html_url")] public string HtmlUrl { get; set; } = "";
    [DataMember(Name = "target_commitish")] public string? TargetCommitish { get; set; }
    [DataMember(Name = "published_at")] public string? PublishedAt { get; set; }
    [DataMember(Name = "body")] public string? Body { get; set; }
}

public sealed class UpdateResult
{
    public UpdateStatus Status { get; set; }
    public string Message { get; set; } = "";
    public ReleaseInfo? Release { get; set; }
}

/// <summary>
/// Compara a versão deste build com o último Release publicado no GitHub.
/// Só lê: o app nunca baixa nem instala nada; no máximo abre a página do Release se o usuário pedir.
/// </summary>
public static class UpdateCheck
{
    public const string Owner = "damanfra";
    public const string Repo = "UpRes";
    public static string LatestReleaseApi => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases";

    private static readonly Regex LocalRx = new Regex(@"^(?<v>\d+(\.\d+){1,3})\+(?<id>[^\s]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex TagRx = new Regex(@"^v?(?<v>\d+(\.\d+){1,3})$", RegexOptions.CultureInvariant);
    private static readonly Regex ShaRx = new Regex(@"^[0-9a-f]{7,40}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static ReleaseInfo ParseRelease(string json)
    {
        var ser = new DataContractJsonSerializer(typeof(ReleaseInfo));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (ReleaseInfo)ser.ReadObject(ms);
    }

    /// <summary>"0.3.0.12+b160276-dirty" → (0.3.0.12, "b160276", dirty).</summary>
    public static bool TryParseLocal(string informational, out Version version, out string? hash, out bool dirty)
    {
        version = new Version(0, 0);
        hash = null;
        dirty = false;
        var m = LocalRx.Match(informational ?? "");
        if (!m.Success) return false;
        version = Normalize(Version.Parse(m.Groups["v"].Value));
        string id = m.Groups["id"].Value;
        dirty = id.EndsWith("-dirty", StringComparison.Ordinal);
        if (dirty) id = id.Substring(0, id.Length - "-dirty".Length);
        if (ShaRx.IsMatch(id)) hash = id.ToLowerInvariant();
        return true;
    }

    public static Version? ParseTag(string tag)
    {
        var m = TagRx.Match(tag ?? "");
        return m.Success ? Normalize(Version.Parse(m.Groups["v"].Value)) : null;
    }

    public static UpdateResult Compare(string localInformational, ReleaseInfo release)
    {
        var r = new UpdateResult { Release = release };
        var remote = ParseTag(release.Tag);
        if (!TryParseLocal(localInformational, out var local, out var hash, out bool dirty) || remote == null)
        {
            r.Status = UpdateStatus.Unknown;
            r.Message = $"Não foi possível comparar a versão local ({localInformational}) com a publicada ({release.Tag}).";
            return r;
        }

        string remoteSha = (release.TargetCommitish ?? "").ToLowerInvariant();
        bool sameCommit = hash != null && ShaRx.IsMatch(remoteSha) && remoteSha.StartsWith(hash, StringComparison.Ordinal);

        if (sameCommit)
        {
            r.Status = UpdateStatus.UpToDate;
            r.Message = $"Você está no mesmo código da versão publicada {release.Tag}" +
                        (dirty ? ", mas com alterações locais não commitadas." : ".");
        }
        else if (remote > local)
        {
            r.Status = UpdateStatus.UpdateAvailable;
            r.Message = $"Há uma versão mais nova: {release.Tag} (publicada em {FormatDate(release.PublishedAt)}). Esta é {localInformational}." +
                        (local.Revision == 0 ? " Esta cópia foi compilada localmente; se você compilou do código mais recente, pode ignorar." : "");
        }
        else if (remote < local)
        {
            r.Status = UpdateStatus.LocalIsNewer;
            r.Message = $"Esta versão ({localInformational}) é mais nova que a última publicada ({release.Tag}).";
        }
        else
        {
            r.Status = UpdateStatus.UpToDate;
            r.Message = $"Você está na versão publicada mais recente ({release.Tag}).";
        }
        return r;
    }

    private static Version Normalize(Version v) =>
        new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static string FormatDate(string? iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : "data desconhecida";
}
