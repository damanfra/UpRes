using System;
using System.IO;
using System.Linq;
using UpRes.Core.Analysis;
using UpRes.Core.Edid;
using UpRes.Core.Output;
using Xunit;

namespace UpRes.Core.Tests;

/// <summary>Testes com o EDID real do Multilaser MN801 (MUL2380), em que 2560x1440 funciona.</summary>
public class RealEdidTests
{
    internal static byte[] LoadHex(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var hex = File.ReadAllLines(path)
            .Where(l => !l.TrimStart().StartsWith("#"))
            .SelectMany(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return hex.Select(h => Convert.ToByte(h, 16)).ToArray();
    }

    private static byte[] Working => LoadHex("MUL2380_MN801_registro.hex");

    /// <summary>
    /// EDID de fábrica: o script original (Aplicar-1440p.ps1) só gravou os dois DTDs de 2560x1440 nos
    /// offsets 0xC3/0xD5 (67–102 do bloco CTA) e recalculou o checksum do bloco 1. Removê-los devolve o original.
    /// </summary>
    internal static byte[] Factory()
    {
        var d = Working;
        Array.Clear(d, 128 + 67, 36);
        EdidParser.FixChecksum(d, 1);
        return d;
    }

    private static DetailedTiming T60 => CvtReducedBlanking.Compute(2560, 1440, 60);

    [Fact]
    public void Parses_mn801()
    {
        var e = EdidParser.Parse(Working);
        Assert.Empty(e.BadChecksumBlocks);
        Assert.Equal("MUL2380", e.HardwareId);
        Assert.Equal("MN801", e.MonitorName);
        Assert.Equal(1920, e.PreferredTiming!.HActive);
        Assert.Equal(300, e.RangeLimits!.MaxPixelClockMHz);
        Assert.Equal(90, e.RangeLimits.MaxHKHz);
        Assert.Equal(300, e.HdmiMaxTmdsMHz);
        Assert.Contains(e.Modes, m => m.Width == 2560 && m.Height == 1440 && m.PixelClockKHz == 241500);
        Assert.Contains(e.Modes, m => m.Width == 2560 && m.Height == 1440 && m.PixelClockKHz == 201420);
    }

    [Fact]
    public void Generated_reg_has_the_same_blocks_as_the_original_working_reg()
    {
        var original = RegFileGenerator.ParseBinaryValues(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MUL2380_Aplicar-1440p_original.reg")));
        var ctx = new MonitorContext { FriendlyName = "MN801", HardwareId = "MUL2380", InstanceId = "4&12ecdce7&0&UID4145", Edid = Factory() };
        var r = EdidOverrideBuilder.Build(Factory(), new[] { T60, EdidOverrideBuilder.AtRefresh(T60, 50) });
        var generated = RegFileGenerator.ParseBinaryValues(RegFileGenerator.Apply(ctx, r));
        Assert.Equal(new[] { "0", "1" }, original.Keys.OrderBy(k => k));
        Assert.Equal(original["0"], generated["0"]);
        Assert.Equal(original["1"], generated["1"]);
    }

    [Fact]
    public void Plan_b_50hz_matches_the_working_dtd()
    {
        Assert.Equal(201420, EdidOverrideBuilder.AtRefresh(T60, 50).PixelClockKHz);
    }

    [Fact]
    public void Generator_reproduces_the_working_edid_byte_for_byte()
    {
        var r = EdidOverrideBuilder.Build(Factory(), new[] { T60, EdidOverrideBuilder.AtRefresh(T60, 50) });
        Assert.True(r.Success, r.Error);
        Assert.Equal(2, r.Timings.Count);
        Assert.Equal(Working, r.Edid);
    }

    [Fact]
    public void Stripped_mn801_is_likely_thanks_to_declared_range_limits()
    {
        var ctx = new MonitorContext
        {
            FriendlyName = "MN801", HardwareId = "MUL2380", InstanceId = "4&12ecdce7&0&UID4145", RegistryKeyFound = true,
            Connection = ConnectionKind.Hdmi, Gpu = GpuVendor.Intel, Edid = Factory(), EdidSource = "teste",
        };
        var ev = Evaluator.Evaluate(ctx, T60);
        Assert.Equal(Verdict.Provavel, ev.Verdict);
        Assert.Contains("modelo confirmado", ev.VerdictText);
        Assert.Contains(ev.Checks, c => c.Title == "Limites declarados");
    }

    [Fact]
    public void Working_machine_reports_already_available_and_explains_source()
    {
        var ctx = new MonitorContext
        {
            FriendlyName = "MN801", HardwareId = "MUL2380", InstanceId = "4&12ecdce7&0&UID4145", RegistryKeyFound = true,
            Connection = ConnectionKind.Hdmi, Gpu = GpuVendor.Intel, Edid = Working, EdidSource = "registro (Device Parameters\\EDID)",
        };
        ctx.WindowsModes.Add(new WindowsMode(2560, 1440, 60));
        ctx.OtherOverrides.Add(new FoundOverride("MUL2380", "4&12ecdce7&0&UID4144", Working, isActiveInstance: false));
        var ev = Evaluator.Evaluate(ctx, T60);
        Assert.Equal(Verdict.JaDisponivel, ev.Verdict);
        Assert.Contains("NÃO traz", ev.Summary);
        Assert.Contains(ev.Checks, c => c.Title == "Overrides em outras instâncias" && c.Status == CheckStatus.Atencao);
        Assert.Contains("UID4144", RegFileGenerator.Backup(ctx.OtherOverrides[0]));
    }
}

public class PersistTests
{
    [Fact]
    public void Working_edid_without_override_is_offered_as_permanent_override()
    {
        var working = RealEdidTests.LoadHex("MUL2380_MN801_registro.hex");
        var ctx = new MonitorContext
        {
            FriendlyName = "MN801", HardwareId = "MUL2380", InstanceId = "4&12ecdce7&0&UID4145", RegistryKeyFound = true,
            Connection = ConnectionKind.Hdmi, Gpu = GpuVendor.Intel, Edid = working, EdidSource = "registro (Device Parameters\\EDID)",
        };
        ctx.WindowsModes.Add(new WindowsMode(2560, 1440, 60));
        var ev = Evaluator.Evaluate(ctx, CvtReducedBlanking.Compute(2560, 1440, 60));
        Assert.Equal(Verdict.JaDisponivel, ev.Verdict);
        Assert.True(ev.PersistCurrent);
        Assert.Contains("NÃO traz", ev.Summary);
        Assert.True(ev.RecommendGenerate);
        Assert.Equal(2, ev.Override!.Timings.Count);
        Assert.DoesNotContain(ev.Checks, c => c.Title == "Override" && c.Status == CheckStatus.Falha);

        var reg = RegFileGenerator.Apply(ctx, ev.Override);
        var values = RegFileGenerator.ParseBinaryValues(reg);
        Assert.Equal(working, values["0"].Concat(values["1"]).ToArray());
        Assert.Contains(@"DISPLAY\MUL2380\4&12ecdce7&0&UID4145\Device Parameters\EDID_OVERRIDE]", reg);
    }
}
