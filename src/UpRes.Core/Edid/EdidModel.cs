using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UpRes.Core.Edid;

/// <summary>Um modo de vídeo anunciado pelo EDID, venha de onde vier.</summary>
public sealed class AdvertisedMode
{
    public AdvertisedMode(int width, int height, double refreshHz, string source,
        int? pixelClockKHz = null, double? hFreqKHz = null, bool interlaced = false)
    {
        Width = width;
        Height = height;
        RefreshHz = refreshHz;
        Source = source;
        PixelClockKHz = pixelClockKHz;
        HFreqKHz = hFreqKHz;
        Interlaced = interlaced;
    }

    public int Width { get; }
    public int Height { get; }
    public double RefreshHz { get; }
    public bool Interlaced { get; }

    /// <summary>Conhecido para DTDs, DisplayID e VICs da tabela; nulo para Standard/Established Timings.</summary>
    public int? PixelClockKHz { get; }
    public double? HFreqKHz { get; }

    /// <summary>De onde veio: "DTD base #1", "CTA VIC 16", "Standard Timing" etc.</summary>
    public string Source { get; }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}x{1}{2} @ {3:0.##} Hz{4}  [{5}]",
            Width, Height, Interlaced ? "i" : "", RefreshHz,
            PixelClockKHz.HasValue ? string.Format(CultureInfo.InvariantCulture, " ({0:0.00} MHz)", PixelClockKHz.Value / 1000.0) : "",
            Source);
}

public enum DescriptorKind
{
    DetailedTiming,
    MonitorName,      // 0xFC
    SerialText,       // 0xFF
    Text,             // 0xFE
    RangeLimits,      // 0xFD
    ColorPoint,       // 0xFB
    StandardTimings,  // 0xFA
    Dcm,              // 0xF9
    Cvt3Byte,         // 0xF8
    EstablishedIII,   // 0xF7
    Dummy,            // 0x10
    Other,
}

/// <summary>Um dos 4 descritores de 18 bytes do bloco base (offsets 54, 72, 90, 108).</summary>
public sealed class DescriptorSlot
{
    public int Index { get; set; }
    public int Offset { get; set; }
    public DescriptorKind Kind { get; set; }
    public byte Tag { get; set; }
    public DetailedTiming? Timing { get; set; }
    public string? Text { get; set; }

    public override string ToString() => Kind == DescriptorKind.DetailedTiming
        ? $"#{Index + 1} DTD: {Timing}"
        : $"#{Index + 1} {Kind} (tag 0x{Tag:X2}){(Text != null ? ": " + Text : "")}";
}

/// <summary>Display Range Limits Descriptor (tag 0xFD).</summary>
public sealed class RangeLimits
{
    public int Offset { get; set; }
    public int MinVHz { get; set; }
    public int MaxVHz { get; set; }
    public int MinHKHz { get; set; }
    public int MaxHKHz { get; set; }

    /// <summary>Pixel clock máximo em MHz (já descontada a precisão adicional do CVT, se houver).</summary>
    public double MaxPixelClockMHz { get; set; }

    /// <summary>0x00 GTF padrão, 0x01 só limites, 0x02 GTF secundário, 0x04 CVT.</summary>
    public byte TimingSupport { get; set; }

    /// <summary>Limite de pixels ativos por linha declarado no bloco CVT (0 = sem limite / não declarado).</summary>
    public int CvtMaxActivePixels { get; set; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "V {0}–{1} Hz, H {2}–{3} kHz, pixel clock máx. {4:0.##} MHz{5}",
        MinVHz, MaxVHz, MinHKHz, MaxHKHz, MaxPixelClockMHz,
        CvtMaxActivePixels > 0 ? $", CVT: máx. {CvtMaxActivePixels} pixels/linha" : "");
}

/// <summary>Extensão CTA-861 (tag 0x02).</summary>
public sealed class CtaExtension
{
    public int BlockIndex { get; set; }
    public int Revision { get; set; }
    public int DtdOffset { get; set; }
    public int NativeDtdCount { get; set; }
    public List<int> Vics { get; } = new List<int>();
    public List<int> NativeVics { get; } = new List<int>();
    public List<DetailedTiming> Dtds { get; } = new List<DetailedTiming>();

    /// <summary>Offset do primeiro slot de 18 bytes livre após os DTDs (ou null se não houver espaço).</summary>
    public int? FreeDtdOffset { get; set; }

    /// <summary>Max TMDS Clock do HDMI VSDB (OUI 00-0C-03), em MHz.</summary>
    public int? HdmiMaxTmdsMHz { get; set; }

    /// <summary>Max TMDS Character Rate do HDMI Forum VSDB (OUI C4-5D-D8), em MHz.</summary>
    public int? HfMaxTmdsMHz { get; set; }

    public bool HasHdmiVsdb { get; set; }
    public bool HasHfVsdb { get; set; }
}

public sealed class EdidInfo
{
    public byte[] Raw { get; set; } = new byte[0];
    public int BlockCount => Raw.Length / EdidParser.BlockLength;
    public bool HeaderValid { get; set; }
    public List<int> BadChecksumBlocks { get; } = new List<int>();
    public List<string> Warnings { get; } = new List<string>();

    public string ManufacturerId { get; set; } = "???";
    public ushort ProductCode { get; set; }
    public uint SerialNumber { get; set; }

    /// <summary>Formato usado pelo Windows em Enum\DISPLAY, por exemplo GSM5BBF.</summary>
    public string HardwareId => ManufacturerId + ProductCode.ToString("X4", CultureInfo.InvariantCulture);

    public int ManufactureWeek { get; set; }
    public int ManufactureYear { get; set; }
    public int VersionMajor { get; set; }
    public int VersionMinor { get; set; }
    public string Version => $"{VersionMajor}.{VersionMinor}";

    public bool IsDigital { get; set; }
    public string InputInterface { get; set; } = "";
    public int ScreenWidthCm { get; set; }
    public int ScreenHeightCm { get; set; }

    public string? MonitorName { get; set; }
    public string? SerialText { get; set; }

    public int DeclaredExtensionCount { get; set; }
    public List<DescriptorSlot> BaseDescriptors { get; } = new List<DescriptorSlot>();
    public DetailedTiming? PreferredTiming { get; set; }
    public RangeLimits? RangeLimits { get; set; }
    public List<CtaExtension> CtaExtensions { get; } = new List<CtaExtension>();
    public List<int> DisplayIdBlocks { get; } = new List<int>();
    public List<string> OtherExtensions { get; } = new List<string>();
    public List<AdvertisedMode> Modes { get; } = new List<AdvertisedMode>();

    public int? HdmiMaxTmdsMHz => CtaExtensions.Select(c => c.HdmiMaxTmdsMHz).FirstOrDefault(v => v.HasValue);
    public int? HfMaxTmdsMHz => CtaExtensions.Select(c => c.HfMaxTmdsMHz).FirstOrDefault(v => v.HasValue);
    public bool HasHdmiVsdb => CtaExtensions.Any(c => c.HasHdmiVsdb);

    public string DisplayName => string.IsNullOrWhiteSpace(MonitorName) ? HardwareId : MonitorName!;
}
