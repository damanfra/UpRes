using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using UpRes.App.Native;
using UpRes.Core.Analysis;
using static UpRes.App.Native.NativeMethods;

namespace UpRes.App;

/// <summary>
/// Descobre os monitores ATIVOS e lê o EDID de cada um. Somente leitura:
/// o registro é aberto com OpenSubKey(writable: false) e nada é executado.
/// </summary>
internal static class MonitorDiscovery
{
    private const string EnumDisplay = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    public static List<MonitorContext> GetActiveMonitors()
    {
        var list = new List<MonitorContext>();
        foreach (var path in QueryActivePaths())
        {
            var ctx = new MonitorContext();
            try { Fill(ctx, path); }
            catch (Exception ex) { ctx.Notes.Add("Erro ao ler este monitor: " + ex.Message); }
            list.Add(ctx);
        }

        // Clone/espelhamento gera mais de um caminho para o mesmo monitor.
        list = list
            .GroupBy(m => string.IsNullOrEmpty(m.MonitorDevicePath) ? Guid.NewGuid().ToString() : m.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var found = ScanAllOverrides(list);
        foreach (var ctx in list) ctx.OtherOverrides.AddRange(found);
        return list;
    }

    /// <summary>Procura EDID_OVERRIDE em todas as instâncias de Enum\DISPLAY (inclusive monitores antigos/inativos).</summary>
    private static List<FoundOverride> ScanAllOverrides(List<MonitorContext> active)
    {
        var result = new List<FoundOverride>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(EnumDisplay, writable: false);
            if (root == null) return result;
            foreach (var hwid in root.GetSubKeyNames())
            {
                using var hw = root.OpenSubKey(hwid, writable: false);
                if (hw == null) continue;
                foreach (var inst in hw.GetSubKeyNames())
                {
                    try
                    {
                        using var ov = hw.OpenSubKey(inst + @"\Device Parameters\EDID_OVERRIDE", writable: false);
                        if (ov == null) continue;
                        var data = ReadBlocks(ov);
                        bool isActive = active.Any(a => a.HardwareId.Equals(hwid, StringComparison.OrdinalIgnoreCase)
                                                     && a.InstanceId.Equals(inst, StringComparison.OrdinalIgnoreCase));
                        result.Add(new FoundOverride(hwid, inst, data ?? new byte[0], isActive));
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException) { }
        return result;
    }

    private static byte[]? ReadBlocks(RegistryKey ov)
    {
        var blocks = ov.GetValueNames()
            .Select(n => int.TryParse(n, out int i) ? (Index: i, Data: ov.GetValue(n) as byte[]) : (Index: -1, Data: null))
            .Where(x => x.Index >= 0 && x.Data != null)
            .OrderBy(x => x.Index)
            .ToList();
        return blocks.Count > 0 ? blocks.SelectMany(b => b.Data!).ToArray() : null;
    }

    private static DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm);
            if (err != ERROR_SUCCESS) throw new InvalidOperationException($"GetDisplayConfigBufferSizes falhou ({err}).");
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (err == ERROR_SUCCESS) return paths.Take((int)np).ToArray();
            if (err != ERROR_INSUFFICIENT_BUFFER) throw new InvalidOperationException($"QueryDisplayConfig falhou ({err}).");
        }
        throw new InvalidOperationException("QueryDisplayConfig: a topologia mudou repetidamente durante a leitura.");
    }

    private static void Fill(MonitorContext ctx, DISPLAYCONFIG_PATH_INFO path)
    {
        var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME;
        target.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
        target.header.adapterId = path.targetInfo.adapterId;
        target.header.id = path.targetInfo.id;
        int err = DisplayConfigGetDeviceInfo(ref target);
        if (err != ERROR_SUCCESS) throw new InvalidOperationException($"GET_TARGET_NAME falhou ({err}).");

        ctx.FriendlyName = target.monitorFriendlyDeviceName ?? "";
        ctx.MonitorDevicePath = target.monitorDevicePath ?? "";
        ctx.Connection = DevicePath.FromOutputTechnology(target.outputTechnology);
        ctx.ConnectionRaw = OutputTechName(target.outputTechnology);

        var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME;
        source.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
        source.header.adapterId = path.sourceInfo.adapterId;
        source.header.id = path.sourceInfo.id;
        if (DisplayConfigGetDeviceInfo(ref source) == ERROR_SUCCESS)
            ctx.GdiDeviceName = source.viewGdiDeviceName ?? "";

        var adapter = new DISPLAYCONFIG_ADAPTER_NAME();
        adapter.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_ADAPTER_NAME;
        adapter.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_ADAPTER_NAME));
        adapter.header.adapterId = path.targetInfo.adapterId;
        if (DisplayConfigGetDeviceInfo(ref adapter) == ERROR_SUCCESS)
            ctx.Gpu = DevicePath.VendorFromPath(adapter.adapterDevicePath);

        ctx.GpuName = AdapterName(ctx.GdiDeviceName);
        ReadModes(ctx);

        if (!DevicePath.TryParse(ctx.MonitorDevicePath, out var hwid, out var instance))
        {
            ctx.Notes.Add("Não foi possível extrair HardwareID/InstanceID de: " + ctx.MonitorDevicePath);
            return;
        }
        ctx.HardwareId = hwid;
        ctx.InstanceId = instance;
        if (string.IsNullOrEmpty(ctx.FriendlyName)) ctx.FriendlyName = hwid;

        ReadRegistry(ctx);
        ReadWmi(ctx);
        if (ctx.Edid == null && ctx.WmiEdid != null)
        {
            ctx.Edid = ctx.WmiEdid;
            ctx.EdidSource = "WMI (WmiGetMonitorRawEEdidV1Block)";
        }
    }

    private static void ReadRegistry(MonitorContext ctx)
    {
        try
        {
            using var hw = Registry.LocalMachine.OpenSubKey(EnumDisplay + "\\" + ctx.HardwareId, writable: false);
            if (hw == null) { ctx.Notes.Add($"Chave Enum\\DISPLAY\\{ctx.HardwareId} não encontrada."); return; }

            // Usa a grafia exata da subchave (o caminho do QueryDisplayConfig pode vir em minúsculas).
            var actual = hw.GetSubKeyNames().FirstOrDefault(n => n.Equals(ctx.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (actual == null)
            {
                ctx.Notes.Add($"Instância {ctx.InstanceId} não encontrada. Instâncias existentes (inclui monitores antigos): " + string.Join(", ", hw.GetSubKeyNames()));
                return;
            }
            ctx.InstanceId = actual;
            ctx.RegistryKeyFound = true;

            int stale = hw.GetSubKeyNames().Length - 1;
            if (stale > 0)
                ctx.Notes.Add($"Há {stale} outra(s) instância(s) de {ctx.HardwareId} no registro (conexões antigas/outras portas). O .reg usa só a instância ativa.");

            using var dp = hw.OpenSubKey(actual + @"\Device Parameters", writable: false);
            if (dp == null) { ctx.Notes.Add("Subchave 'Device Parameters' ausente."); return; }

            if (dp.GetValue("EDID") is byte[] edid && edid.Length >= 128)
            {
                ctx.Edid = edid;
                ctx.EdidSource = "registro (Device Parameters\\EDID)";
            }

            using var ov = dp.OpenSubKey("EDID_OVERRIDE", writable: false);
            if (ov != null)
            {
                ctx.ExistingOverride = ReadBlocks(ov);
                if (ctx.ExistingOverride == null)
                    ctx.Notes.Add("Existe a chave EDID_OVERRIDE, mas sem valores \"0\", \"1\"...");
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
        {
            ctx.Notes.Add("Sem permissão de leitura no registro: " + ex.Message);
        }
    }

    /// <summary>EDID via WmiMonitorDescriptorMethods (o que o Windows entrega; pode refletir o override, se houver).</summary>
    private static void ReadWmi(MonitorContext ctx)
    {
        try
        {
            string prefix = $@"DISPLAY\{ctx.HardwareId}\{ctx.InstanceId}";
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorDescriptorMethods");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    string name = mo["InstanceName"] as string ?? "";
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                    var all = new List<byte>();
                    for (byte block = 0; block < 8; block++)
                    {
                        var inParams = mo.GetMethodParameters("WmiGetMonitorRawEEdidV1Block");
                        inParams["BlockId"] = block;
                        ManagementBaseObject outParams;
                        try { outParams = mo.InvokeMethod("WmiGetMonitorRawEEdidV1Block", inParams, null); }
                        catch (ManagementException) { break; }
                        if (!(outParams?["BlockContent"] is byte[] content) || content.Length < 128) break;
                        all.AddRange(content.Take(128));
                        if (block == 0 && content[126] == 0) break;
                        if (block >= all[126]) break;
                    }
                    if (all.Count >= 128) ctx.WmiEdid = all.ToArray();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ctx.Notes.Add("Leitura do EDID via WMI falhou: " + ex.Message);
        }
    }

    private static void ReadModes(MonitorContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.GdiDeviceName)) return;
        var dm = NewDevMode();
        if (EnumDisplaySettings(ctx.GdiDeviceName, ENUM_CURRENT_SETTINGS, ref dm))
            ctx.CurrentMode = new WindowsMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);

        var seen = new HashSet<(int, int, int)>();
        for (int i = 0; i < 2000; i++)
        {
            dm = NewDevMode();
            if (!EnumDisplaySettings(ctx.GdiDeviceName, i, ref dm)) break;
            if (seen.Add((dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency)))
                ctx.WindowsModes.Add(new WindowsMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
        }
    }

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        return dm;
    }

    private static string AdapterName(string gdiName)
    {
        for (uint i = 0; i < 32; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if (string.Equals(dd.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase)) return dd.DeviceString;
        }
        return "";
    }

    private static string OutputTechName(int t) => t switch
    {
        -1 => "Outra",
        0 => "VGA (HD15)",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS (interna)",
        10 => "DisplayPort",
        11 => "eDP (interna)",
        13 => "UDI (interna)",
        15 => "Miracast",
        16 => "Indireta (cabo)",
        17 => "Indireta (virtual)",
        18 => "DisplayPort via USB-C/Thunderbolt",
        unchecked((int)0x80000000) => "Interna",
        _ => $"Tipo {t}",
    };
}
