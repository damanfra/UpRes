using System.Linq;
using System.Reflection;

namespace UpRes.Core;

/// <summary>Versão do build (preenchida pelo Directory.Build.targets): aparece no relatório, na janela e nos .reg.</summary>
public static class BuildInfo
{
    private static readonly Assembly Asm = typeof(BuildInfo).Assembly;

    /// <summary>Ex.: 0.2.0+b160276 ou 0.2.0+b160276-dirty.</summary>
    public static string Version { get; } =
        Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    public static string BuildDate { get; } =
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "?";

    public static string Display => $"UpRes {Version} (compilado em {BuildDate})";
}
