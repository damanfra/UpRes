using System.Linq;
using UpRes.Core.Analysis;
using UpRes.Core.Edid;
using UpRes.Core.Output;
using Xunit;

namespace UpRes.Core.Tests;

public class CvtTests
{
    [Fact]
    public void Cvt_rb_2560x1440_60_matches_reference_table()
    {
        var t = CvtReducedBlanking.Compute(2560, 1440, 60);
        Assert.Equal(241500, t.PixelClockKHz);
        Assert.Equal((2560, 48, 32, 80, 2720), (t.HActive, t.HFrontPorch, t.HSyncWidth, t.HBackPorch, t.HTotal));
        Assert.Equal((1440, 3, 5, 33, 1481), (t.VActive, t.VFrontPorch, t.VSyncWidth, t.VBackPorch, t.VTotal));
        Assert.True(t.HSyncPositive);
        Assert.False(t.VSyncPositive);
        Assert.Equal(88.79, t.HFreqKHz, 2);
        Assert.Equal(59.95, t.RefreshHz, 2);
    }

    [Fact]
    public void Cvt_rb_1920x1080_60_matches_vesa_dmt()
    {
        // DMT 1920x1080 RB: 138,5 MHz, 2080 x 1111.
        var t = CvtReducedBlanking.Compute(1920, 1080, 60);
        Assert.Equal(138500, t.PixelClockKHz);
        Assert.Equal(2080, t.HTotal);
        Assert.Equal(1111, t.VTotal);
    }

    [Fact]
    public void Cvt_rb_50hz_is_lighter()
    {
        var t = CvtReducedBlanking.Compute(2560, 1440, 50);
        Assert.True(t.PixelClockKHz < 241500);
        Assert.Equal(50, t.RefreshHz, 0);
    }
}

public class DtdTests
{
    [Fact]
    public void Encode_decode_roundtrip()
    {
        var t = CvtReducedBlanking.Compute(2560, 1440, 60);
        t.ImageWidthMm = 600; t.ImageHeightMm = 340;
        var bytes = t.ToBytes();
        var back = DetailedTiming.Decode(bytes, 0)!;
        Assert.Equal(t.ToString(), back.ToString());
        Assert.Equal(t.Describe(), back.Describe());
        Assert.Equal((600, 340), (back.ImageWidthMm, back.ImageHeightMm));
        Assert.Equal(0x1A, bytes[17]); // digital separado, H+, V-
    }

    [Fact]
    public void Known_bytes_for_2560x1440_cvt_rb()
    {
        var t = CvtReducedBlanking.Compute(2560, 1440, 60);
        var b = t.ToBytes();
        // 24150 = 0x5E56; H 2560=0xA00, blank 160=0x0A0; V 1440=0x5A0, blank 41=0x029
        Assert.Equal(new byte[] { 0x56, 0x5E, 0x00, 0xA0, 0xA0, 0xA0, 0x29, 0x50, 0x30, 0x20, 0x35, 0x00 }, b.Take(12).ToArray());
    }
}

public class ParserTests
{
    [Fact]
    public void Parses_gaming_monitor()
    {
        var e = EdidParser.Parse(EdidFactory.Gaming240());
        Assert.True(e.HeaderValid);
        Assert.Empty(e.BadChecksumBlocks);
        Assert.Equal("GSM", e.ManufacturerId);
        Assert.Equal("GSM5BBF", e.HardwareId);
        Assert.Equal("LG ULTRAGEAR", e.MonitorName);
        Assert.Equal("TESTSERIAL01", e.SerialText);
        Assert.Equal("1.4", e.Version);
        Assert.Equal("DisplayPort", e.InputInterface);
        Assert.Equal(2, e.BlockCount);
        Assert.Equal(1920, e.PreferredTiming!.HActive);
        Assert.Equal(240, e.PreferredTiming.RefreshHz, 0);

        Assert.NotNull(e.RangeLimits);
        Assert.Equal(270, e.RangeLimits!.MaxHKHz);
        Assert.Equal(560, e.RangeLimits.MaxPixelClockMHz);

        var cta = Assert.Single(e.CtaExtensions);
        Assert.Equal(new[] { 16, 4, 3, 63 }, cta.Vics);
        Assert.Equal(new[] { 16 }, cta.NativeVics);
        Assert.Equal(300, cta.HdmiMaxTmdsMHz);
        Assert.Single(cta.Dtds);
        Assert.Equal(35, cta.FreeDtdOffset);

        Assert.Contains(e.Modes, m => m.Width == 1920 && m.Height == 1080 && m.Source.Contains("Standard"));
        Assert.Contains(e.Modes, m => m.Width == 1920 && m.Height == 1080 && m.Source == "CTA VIC 63");
    }

    [Fact]
    public void Parses_cvt_range_limits_with_precision_and_max_active()
    {
        var e = EdidParser.Parse(EdidFactory.Office60WithDummy());
        Assert.Equal(169.5, e.RangeLimits!.MaxPixelClockMHz);
        Assert.Equal(1920, e.RangeLimits.CvtMaxActivePixels);
        Assert.Equal(DescriptorKind.Dummy, e.BaseDescriptors[3].Kind);
    }

    [Fact]
    public void Checksum_fix_makes_block_sum_zero()
    {
        var d = EdidFactory.Office60();
        d[100] ^= 0x55;
        Assert.False(EdidParser.ChecksumOk(d, 0));
        EdidParser.FixChecksum(d, 0);
        Assert.True(EdidParser.ChecksumOk(d, 0));
    }
}

public class OverrideBuilderTests
{
    private static DetailedTiming Target => CvtReducedBlanking.Compute(2560, 1440, 60);

    [Fact]
    public void Gaming_monitor_gets_dtd_in_cta_slot_and_base_untouched()
    {
        var original = EdidFactory.Gaming240();
        var r = EdidOverrideBuilder.Build(original, Target);
        Assert.True(r.Success, r.Error);
        Assert.Equal(2, r.Blocks.Count);
        Assert.Equal(original.Take(128), r.Blocks[0]); // base intacto (range limits já comportava)

        var e = EdidParser.Parse(r.Edid);
        Assert.Empty(e.BadChecksumBlocks);
        Assert.Equal(EdidParser.Parse(original).PreferredTiming!.ToString(), e.PreferredTiming!.ToString());
        Assert.Equal(2, e.CtaExtensions[0].Dtds.Count);
        var added = e.CtaExtensions[0].Dtds[1];
        Assert.Equal(2560, added.HActive);
        Assert.Equal(1440, added.VActive);
        Assert.Equal(600, added.ImageWidthMm); // copiado do nativo
        Assert.Equal(original.Skip(128).Take(35), r.Blocks[1].Take(35)); // data blocks e DTD original intactos
    }

    [Fact]
    public void Base_only_monitor_gets_new_cta_extension_and_adjusted_range_limits()
    {
        var r = EdidOverrideBuilder.Build(EdidFactory.Office60(), Target);
        Assert.True(r.Success, r.Error);
        Assert.Equal(2, r.Blocks.Count);
        Assert.Equal(1, r.Blocks[0][126]);
        var e = EdidParser.Parse(r.Edid);
        Assert.Empty(e.BadChecksumBlocks);
        Assert.Contains(e.Modes, m => m.Width == 2560 && m.Height == 1440);
        Assert.True(e.RangeLimits!.MaxPixelClockMHz >= 241.5);
        Assert.True(e.RangeLimits.MaxHKHz >= 89);
        Assert.Equal("DELL P2419H", e.MonitorName);
    }

    [Fact]
    public void Dummy_descriptor_is_used_and_cvt_limits_lifted()
    {
        var r = EdidOverrideBuilder.Build(EdidFactory.Office60WithDummy(), Target);
        Assert.True(r.Success, r.Error);
        Assert.Single(r.Blocks);
        var e = EdidParser.Parse(r.Edid);
        Assert.Equal(DescriptorKind.DetailedTiming, e.BaseDescriptors[3].Kind);
        Assert.Equal(2560, e.BaseDescriptors[3].Timing!.HActive);
        Assert.Equal(0, e.RangeLimits!.CvtMaxActivePixels);
        Assert.Equal(250, e.RangeLimits.MaxPixelClockMHz);
        Assert.Equal("DELL P2419H", e.MonitorName);
    }

    [Fact]
    public void Refuses_when_mode_already_present()
    {
        var first = EdidOverrideBuilder.Build(EdidFactory.Gaming240(), Target);
        var second = EdidOverrideBuilder.Build(first.Edid, Target);
        Assert.False(second.Success);
        Assert.Contains("já anuncia", second.Error);
    }

    [Fact]
    public void Refuses_corrupted_edid()
    {
        var d = EdidFactory.Gaming240();
        d[60] ^= 0xFF;
        Assert.False(EdidOverrideBuilder.Build(d, Target).Success);
    }
}

public class EvaluatorTests
{
    private static MonitorContext Ctx(byte[] edid, ConnectionKind conn) => new MonitorContext
    {
        FriendlyName = "Teste", HardwareId = EdidParser.Parse(edid).HardwareId, InstanceId = "5&1a2b3c&0&UID4352",
        RegistryKeyFound = true, Connection = conn, Gpu = GpuVendor.Intel, GpuName = "Intel(R) Iris(R) Xe Graphics",
        Edid = edid, EdidSource = "teste",
    };

    [Fact]
    public void Gaming_monitor_over_dp_is_likely()
    {
        var ev = Evaluator.Evaluate(Ctx(EdidFactory.Gaming240(), ConnectionKind.DisplayPort), CvtReducedBlanking.Compute(2560, 1440, 60));
        Assert.Equal(Verdict.Provavel, ev.Verdict);
        Assert.True(ev.RecommendGenerate);
        Assert.Contains(ev.Checks, c => c.Title == "Scaler" && c.Status == CheckStatus.Atencao);
    }

    [Fact]
    public void Office_monitor_is_unlikely_and_suggests_lower_refresh()
    {
        var ev = Evaluator.Evaluate(Ctx(EdidFactory.Office60(), ConnectionKind.Hdmi), CvtReducedBlanking.Compute(2560, 1440, 60));
        Assert.Equal(Verdict.Improvavel, ev.Verdict);
        Assert.NotNull(ev.SuggestedRefreshHz);
        var t = CvtReducedBlanking.Compute(2560, 1440, ev.SuggestedRefreshHz!.Value);
        Assert.True(t.PixelClockMHz <= 148.5 && t.HFreqKHz <= 67.5);
    }

    [Fact]
    public void Internal_panel_is_unlikely()
    {
        var ev = Evaluator.Evaluate(Ctx(EdidFactory.Gaming240(), ConnectionKind.Internal), CvtReducedBlanking.Compute(2560, 1440, 60));
        Assert.Equal(Verdict.Improvavel, ev.Verdict);
    }

    [Fact]
    public void Already_offered_by_windows()
    {
        var ctx = Ctx(EdidFactory.Gaming240(), ConnectionKind.DisplayPort);
        ctx.WindowsModes.Add(new WindowsMode(2560, 1440, 60));
        Assert.Equal(Verdict.JaDisponivel, Evaluator.Evaluate(ctx, CvtReducedBlanking.Compute(2560, 1440, 60)).Verdict);
    }

    [Fact]
    public void Missing_edid_is_uncertain_without_override()
    {
        var ctx = Ctx(EdidFactory.Gaming240(), ConnectionKind.DisplayPort);
        ctx.Edid = null;
        var ev = Evaluator.Evaluate(ctx, CvtReducedBlanking.Compute(2560, 1440, 60));
        Assert.Equal(Verdict.Incerto, ev.Verdict);
        Assert.False(ev.RecommendGenerate);
    }
}

public class OutputTests
{
    private static (MonitorContext, OverrideResult) Sample()
    {
        var edid = EdidFactory.Gaming240();
        var ctx = new MonitorContext { FriendlyName = "LG ULTRAGEAR", HardwareId = "GSM5BBF", InstanceId = "5&1a2b3c&0&UID4352", Edid = edid };
        return (ctx, EdidOverrideBuilder.Build(edid, CvtReducedBlanking.Compute(2560, 1440, 60)));
    }

    [Fact]
    public void Apply_reg_has_exact_path_and_roundtrips_blocks()
    {
        var (ctx, ov) = Sample();
        var reg = RegFileGenerator.Apply(ctx, ov);
        const string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\DISPLAY\GSM5BBF\5&1a2b3c&0&UID4352\Device Parameters\EDID_OVERRIDE";
        Assert.StartsWith("Windows Registry Editor Version 5.00\r\n", reg);
        Assert.Contains("[-" + key + "]\r\n", reg);
        Assert.Contains("[" + key + "]\r\n", reg);

        var values = RegFileGenerator.ParseBinaryValues(reg);
        Assert.Equal(2, values.Count);
        Assert.Equal(ov.Blocks[0], values["0"]);
        Assert.Equal(ov.Blocks[1], values["1"]);
        Assert.All(reg.Split("\r\n"), l => Assert.True(l.Length <= 80 || l.StartsWith(";") || l.StartsWith("["), l));
    }

    [Fact]
    public void Undo_reg_only_deletes_the_key()
    {
        var (ctx, _) = Sample();
        var reg = RegFileGenerator.Undo(ctx);
        var lines = reg.Split("\r\n").Where(l => l.Length > 0 && !l.StartsWith(";")).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(@"[-HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\DISPLAY\GSM5BBF\5&1a2b3c&0&UID4352\Device Parameters\EDID_OVERRIDE]", lines[1]);
    }

    [Fact]
    public void Powershell_text_contains_all_blocks()
    {
        var (ctx, ov) = Sample();
        var ps = PowerShellGenerator.Apply(ctx, ov);
        Assert.Contains(@"HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY\GSM5BBF\5&1a2b3c&0&UID4352\Device Parameters\EDID_OVERRIDE", ps);
        Assert.Contains("-Name '0'", ps);
        Assert.Contains("-Name '1'", ps);
    }

    [Fact]
    public void Report_builds()
    {
        var (ctx, _) = Sample();
        ctx.Connection = ConnectionKind.DisplayPort;
        var ev = Evaluator.Evaluate(ctx, CvtReducedBlanking.Compute(2560, 1440, 60));
        var text = ReportBuilder.Build(ctx, ev, includeHex: true);
        Assert.Contains("VEREDITO", text);
        Assert.Contains("EDID PROPOSTO (hex)", text);
        Assert.Contains("COMO APLICAR", InstructionsGenerator.Build(ctx, ev));
    }
}

public class DevicePathTests
{
    [Theory]
    [InlineData(@"\\?\DISPLAY#GSM5BBF#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "GSM5BBF", "5&1a2b3c&0&UID4352")]
    [InlineData(@"DISPLAY\GSM5BBF\5&1a2b3c&0&UID4352_0", "GSM5BBF", "5&1a2b3c&0&UID4352")]
    [InlineData(@"DISPLAY\DEL4321\4&abc&0&UID200193", "DEL4321", "4&abc&0&UID200193")]
    public void Parses(string path, string hwid, string instance)
    {
        Assert.True(DevicePath.TryParse(path, out var h, out var i));
        Assert.Equal(hwid, h);
        Assert.Equal(instance, i);
    }

    [Fact]
    public void Rejects_non_display()
    {
        Assert.False(DevicePath.TryParse(@"\\?\PCI#VEN_8086&DEV_46A8#3&11583659&0&10#{...}", out _, out _));
    }

    [Theory]
    [InlineData(@"\\?\PCI#VEN_8086&DEV_46A8&SUBSYS_0B0E1028", GpuVendor.Intel)]
    [InlineData(@"\\?\PCI#VEN_10DE&DEV_2520", GpuVendor.Nvidia)]
    [InlineData(@"\\?\PCI#VEN_1002&DEV_73DF", GpuVendor.Amd)]
    public void Gpu_vendor(string path, GpuVendor v) => Assert.Equal(v, DevicePath.VendorFromPath(path));
}
