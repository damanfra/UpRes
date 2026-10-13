using System;
using System.Collections.Generic;
using UpRes.Core.Edid;

namespace UpRes.Core.Analysis;

public enum ConnectionKind
{
    Unknown,
    Hdmi,
    DisplayPort,
    Dvi,
    Vga,
    Internal,
    UsbTunnel,
    Other,
}

public enum GpuVendor
{
    Unknown,
    Intel,
    Nvidia,
    Amd,
    Other,
}

/// <summary>Um modo que o Windows oferece hoje para o monitor (EnumDisplaySettings).</summary>
public sealed class WindowsMode
{
    public WindowsMode(int width, int height, int refreshHz) { Width = width; Height = height; RefreshHz = refreshHz; }
    public int Width { get; }
    public int Height { get; }
    public int RefreshHz { get; }
    public override string ToString() => $"{Width}x{Height} @ {RefreshHz} Hz";
}

/// <summary>
/// Tudo o que se sabe de um monitor ativo. Preenchido pelo app (Windows) e consumido pelo
/// avaliador e pelos geradores, que não dependem do Windows.
/// </summary>
public sealed class MonitorContext
{
    public string FriendlyName { get; set; } = "";
    public string GdiDeviceName { get; set; } = "";

    /// <summary>Ex.: \\?\DISPLAY#GSM5BBF#5&amp;1a2b3c&amp;0&amp;UID4352#{e6f07b5f-...}</summary>
    public string MonitorDevicePath { get; set; } = "";

    /// <summary>Ex.: GSM5BBF</summary>
    public string HardwareId { get; set; } = "";

    /// <summary>Ex.: 5&amp;1a2b3c&amp;0&amp;UID4352 — muda por máquina, GPU e porta.</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>True quando a chave Enum\DISPLAY\HWID\Instance foi encontrada no registro.</summary>
    public bool RegistryKeyFound { get; set; }

    public ConnectionKind Connection { get; set; }
    public string ConnectionRaw { get; set; } = "";
    public GpuVendor Gpu { get; set; }
    public string GpuName { get; set; } = "";

    public WindowsMode? CurrentMode { get; set; }
    public List<WindowsMode> WindowsModes { get; } = new List<WindowsMode>();

    /// <summary>EDID original (valor "EDID" do registro ou WMI).</summary>
    public byte[]? Edid { get; set; }
    public string EdidSource { get; set; } = "";

    /// <summary>Override já existente nesta máquina (valores "0", "1"... concatenados), se houver.</summary>
    public byte[]? ExistingOverride { get; set; }

    public List<string> Notes { get; } = new List<string>();

    /// <summary>Caminho da chave no registro, sem o prefixo da hive.</summary>
    public string RegistrySubPath => $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{HardwareId}\{InstanceId}";

    public string OverrideKeyPath => $@"HKEY_LOCAL_MACHINE\{RegistrySubPath}\Device Parameters\EDID_OVERRIDE";
}

public static class DevicePath
{
    /// <summary>
    /// Extrai HardwareID e InstanceID de um monitorDevicePath (QueryDisplayConfig) ou de um
    /// InstanceName do WMI (DISPLAY\GSM5BBF\5&amp;1a2b3c&amp;0&amp;UID4352_0).
    /// </summary>
    public static bool TryParse(string path, out string hardwareId, out string instanceId)
    {
        hardwareId = instanceId = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        string p = path.Trim();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);

        string[] parts = p.IndexOf('#') >= 0 ? p.Split('#') : p.Split('\\');
        if (parts.Length < 3 || !parts[0].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)) return false;

        hardwareId = parts[1];
        instanceId = parts[2];

        // WMI acrescenta "_0" ao final da instância.
        if (p.IndexOf('#') < 0)
        {
            int us = instanceId.LastIndexOf('_');
            if (us > 0 && int.TryParse(instanceId.Substring(us + 1), out _) && instanceId.IndexOf("UID", StringComparison.OrdinalIgnoreCase) < us)
                instanceId = instanceId.Substring(0, us);
        }
        return hardwareId.Length > 0 && instanceId.Length > 0;
    }

    public static ConnectionKind FromOutputTechnology(int tech) => tech switch
    {
        0 => ConnectionKind.Vga,          // HD15
        4 => ConnectionKind.Dvi,
        5 => ConnectionKind.Hdmi,
        6 => ConnectionKind.Internal,     // LVDS
        10 => ConnectionKind.DisplayPort, // DP externo
        11 => ConnectionKind.Internal,    // eDP
        13 => ConnectionKind.Internal,    // UDI embutido
        18 => ConnectionKind.UsbTunnel,   // DP via USB4/Thunderbolt
        unchecked((int)0x80000000) => ConnectionKind.Internal,
        -1 => ConnectionKind.Other,
        _ => ConnectionKind.Other,
    };

    public static GpuVendor VendorFromPath(string adapterPath)
    {
        string s = (adapterPath ?? "").ToUpperInvariant();
        if (s.Contains("VEN_8086")) return GpuVendor.Intel;
        if (s.Contains("VEN_10DE")) return GpuVendor.Nvidia;
        if (s.Contains("VEN_1002")) return GpuVendor.Amd;
        return s.Length == 0 ? GpuVendor.Unknown : GpuVendor.Other;
    }
}
