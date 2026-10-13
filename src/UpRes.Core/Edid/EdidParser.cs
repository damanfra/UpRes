using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UpRes.Core.Edid;

/// <summary>Parser de EDID 1.x: bloco base, extensões CTA-861 e DisplayID (timings tipo I/VII).</summary>
public static class EdidParser
{
    public const int BlockLength = 128;
    public static readonly int[] DescriptorOffsets = { 54, 72, 90, 108 };

    private static readonly byte[] Header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    public static EdidInfo Parse(byte[] raw)
    {
        if (raw == null) throw new ArgumentNullException(nameof(raw));
        if (raw.Length < BlockLength)
            throw new FormatException($"EDID com {raw.Length} bytes; o mínimo é {BlockLength}.");

        var info = new EdidInfo { Raw = (byte[])raw.Clone() };
        if (raw.Length % BlockLength != 0)
            info.Warnings.Add($"Tamanho {raw.Length} não é múltiplo de 128; bytes finais ignorados.");

        var d = info.Raw;
        info.HeaderValid = Header.SequenceEqual(d.Take(8));
        if (!info.HeaderValid) info.Warnings.Add("Cabeçalho do EDID inválido.");

        for (int b = 0; b < info.BlockCount; b++)
            if (!ChecksumOk(d, b)) info.BadChecksumBlocks.Add(b);

        ParseBase(info, d);

        for (int b = 1; b < info.BlockCount; b++)
        {
            int o = b * BlockLength;
            switch (d[o])
            {
                case 0x02: ParseCta(info, d, b); break;
                case 0x70: ParseDisplayId(info, d, b); break;
                case 0xF0: info.OtherExtensions.Add($"Bloco {b}: Block Map (0xF0)"); break;
                default: info.OtherExtensions.Add($"Bloco {b}: extensão tag 0x{d[o]:X2}"); break;
            }
        }

        if (info.DeclaredExtensionCount != info.BlockCount - 1)
            info.Warnings.Add($"O bloco base declara {info.DeclaredExtensionCount} extensão(ões), mas há {info.BlockCount - 1}.");

        return info;
    }

    public static bool ChecksumOk(byte[] d, int block)
    {
        int sum = 0;
        for (int i = 0; i < BlockLength; i++) sum += d[block * BlockLength + i];
        return (sum & 0xFF) == 0;
    }

    /// <summary>Recalcula o byte 127 do bloco para que a soma dos 128 bytes seja 0 mod 256.</summary>
    public static void FixChecksum(byte[] d, int block)
    {
        int o = block * BlockLength;
        int sum = 0;
        for (int i = 0; i < BlockLength - 1; i++) sum += d[o + i];
        d[o + BlockLength - 1] = (byte)((256 - (sum & 0xFF)) & 0xFF);
    }

    public static string DecodeManufacturer(byte hi, byte lo)
    {
        int v = (hi << 8) | lo;
        char C(int x) => x >= 1 && x <= 26 ? (char)('A' + x - 1) : '?';
        return new string(new[] { C((v >> 10) & 0x1F), C((v >> 5) & 0x1F), C(v & 0x1F) });
    }

    private static void ParseBase(EdidInfo info, byte[] d)
    {
        info.ManufacturerId = DecodeManufacturer(d[8], d[9]);
        info.ProductCode = (ushort)(d[10] | (d[11] << 8));
        info.SerialNumber = (uint)(d[12] | (d[13] << 8) | (d[14] << 16) | (d[15] << 24));
        info.ManufactureWeek = d[16];
        info.ManufactureYear = d[17] + 1990;
        info.VersionMajor = d[18];
        info.VersionMinor = d[19];

        info.IsDigital = (d[20] & 0x80) != 0;
        info.InputInterface = !info.IsDigital ? "Analógica" : (d[20] & 0x0F) switch
        {
            1 => "DVI",
            2 => "HDMI-a",
            3 => "HDMI-b",
            4 => "MDDI",
            5 => "DisplayPort",
            _ => "Digital (não especificada)",
        };
        info.ScreenWidthCm = d[21];
        info.ScreenHeightCm = d[22];
        info.DeclaredExtensionCount = d[126];

        ParseEstablished(info, d);
        ParseStandardTimings(info, d, 38, 8, "Standard Timing");

        for (int i = 0; i < DescriptorOffsets.Length; i++)
        {
            int o = DescriptorOffsets[i];
            var slot = new DescriptorSlot { Index = i, Offset = o };
            var t = DetailedTiming.Decode(d, o);
            if (t != null)
            {
                slot.Kind = DescriptorKind.DetailedTiming;
                slot.Timing = t;
                if (info.PreferredTiming == null && i == 0) info.PreferredTiming = t;
                info.Modes.Add(ModeFrom(t, $"DTD base #{i + 1}" + (i == 0 ? " (preferido)" : "")));
            }
            else
            {
                slot.Tag = d[o + 3];
                switch (slot.Tag)
                {
                    case 0xFC: slot.Kind = DescriptorKind.MonitorName; slot.Text = ReadText(d, o); info.MonitorName = slot.Text; break;
                    case 0xFF: slot.Kind = DescriptorKind.SerialText; slot.Text = ReadText(d, o); info.SerialText = slot.Text; break;
                    case 0xFE: slot.Kind = DescriptorKind.Text; slot.Text = ReadText(d, o); break;
                    case 0xFD: slot.Kind = DescriptorKind.RangeLimits; info.RangeLimits = ParseRangeLimits(d, o); break;
                    case 0xFB: slot.Kind = DescriptorKind.ColorPoint; break;
                    case 0xFA: slot.Kind = DescriptorKind.StandardTimings; ParseStandardTimings(info, d, o + 5, 6, "Standard Timing (descritor)"); break;
                    case 0xF9: slot.Kind = DescriptorKind.Dcm; break;
                    case 0xF8: slot.Kind = DescriptorKind.Cvt3Byte; break;
                    case 0xF7: slot.Kind = DescriptorKind.EstablishedIII; break;
                    case 0x10: slot.Kind = DescriptorKind.Dummy; break;
                    default: slot.Kind = DescriptorKind.Other; break;
                }
            }
            info.BaseDescriptors.Add(slot);
        }

        if (info.PreferredTiming == null)
            info.Warnings.Add("O primeiro descritor do bloco base não é um DTD (timing preferido ausente).");
    }

    private static string ReadText(byte[] d, int o)
    {
        var sb = new StringBuilder();
        for (int i = 5; i < 18; i++)
        {
            byte c = d[o + i];
            if (c == 0x0A || c == 0x00) break;
            sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '?');
        }
        return sb.ToString().Trim();
    }

    internal static RangeLimits ParseRangeLimits(byte[] d, int o)
    {
        byte flags = d[o + 4];
        var r = new RangeLimits
        {
            Offset = o,
            MinVHz = d[o + 5] + ((flags & 0x03) == 0x03 ? 255 : 0),
            MaxVHz = d[o + 6] + ((flags & 0x02) != 0 ? 255 : 0),
            MinHKHz = d[o + 7] + ((flags & 0x0C) == 0x0C ? 255 : 0),
            MaxHKHz = d[o + 8] + ((flags & 0x08) != 0 ? 255 : 0),
            MaxPixelClockMHz = d[o + 9] * 10,
            TimingSupport = d[o + 10],
        };
        if (r.TimingSupport == 0x04)
        {
            // Bloco CVT: byte 12 bits 7..2 = precisão adicional (subtrai 0,25 MHz por unidade);
            // bits 1..0 + byte 13 = máximo de pixels ativos por linha / 8 (0 = sem limite).
            byte b12 = d[o + 12];
            r.MaxPixelClockMHz -= (b12 >> 2) * 0.25;
            r.CvtMaxActivePixels = (((b12 & 0x03) << 8) | d[o + 13]) * 8;
        }
        return r;
    }

    private static void ParseEstablished(EdidInfo info, byte[] d)
    {
        var map = new (int Byte, int Bit, int W, int H, double Hz)[]
        {
            (35, 7, 720, 400, 70), (35, 6, 720, 400, 88), (35, 5, 640, 480, 60), (35, 4, 640, 480, 67),
            (35, 3, 640, 480, 72), (35, 2, 640, 480, 75), (35, 1, 800, 600, 56), (35, 0, 800, 600, 60),
            (36, 7, 800, 600, 72), (36, 6, 800, 600, 75), (36, 5, 832, 624, 75), (36, 3, 1024, 768, 60),
            (36, 2, 1024, 768, 70), (36, 1, 1024, 768, 75), (36, 0, 1280, 1024, 75), (37, 7, 1152, 870, 75),
        };
        foreach (var m in map)
            if ((d[m.Byte] & (1 << m.Bit)) != 0)
                info.Modes.Add(new AdvertisedMode(m.W, m.H, m.Hz, "Established Timing"));
        if ((d[36] & 0x10) != 0)
            info.Modes.Add(new AdvertisedMode(1024, 768, 87, "Established Timing", interlaced: true));
    }

    private static void ParseStandardTimings(EdidInfo info, byte[] d, int start, int count, string source)
    {
        bool v13 = info.VersionMajor > 1 || info.VersionMinor >= 3;
        for (int i = 0; i < count; i++)
        {
            byte b1 = d[start + i * 2], b2 = d[start + i * 2 + 1];
            if ((b1 == 0x01 && b2 == 0x01) || b1 == 0x00) continue;
            int w = (b1 + 31) * 8;
            int h = ((b2 >> 6) & 0x03) switch
            {
                0 => v13 ? w * 10 / 16 : w,
                1 => w * 3 / 4,
                2 => w * 4 / 5,
                _ => w * 9 / 16,
            };
            info.Modes.Add(new AdvertisedMode(w, h, (b2 & 0x3F) + 60, source));
        }
    }

    private static void ParseCta(EdidInfo info, byte[] d, int block)
    {
        int b = block * BlockLength;
        var cta = new CtaExtension
        {
            BlockIndex = block,
            Revision = d[b + 1],
            DtdOffset = d[b + 2],
            NativeDtdCount = d[b + 3] & 0x0F,
        };

        int dtdStart = cta.DtdOffset;
        if (dtdStart != 0 && (dtdStart < 4 || dtdStart > 127))
        {
            info.Warnings.Add($"Bloco {block}: offset de DTD inválido ({dtdStart}).");
            info.CtaExtensions.Add(cta);
            return;
        }

        // Data blocks (bytes 4 .. dtdStart-1).
        int i = 4;
        int end = dtdStart == 0 ? 4 : dtdStart;
        while (i < end)
        {
            int tag = d[b + i] >> 5;
            int len = d[b + i] & 0x1F;
            int p = b + i + 1;
            if (i + 1 + len > end) { info.Warnings.Add($"Bloco {block}: data block truncado no byte {i}."); break; }

            if (tag == 2) // Video Data Block
            {
                for (int k = 0; k < len; k++) AddSvd(info, cta, d[p + k]);
            }
            else if (tag == 3 && len >= 3) // Vendor Specific Data Block
            {
                if (d[p] == 0x03 && d[p + 1] == 0x0C && d[p + 2] == 0x00)
                {
                    cta.HasHdmiVsdb = true;
                    if (len >= 7 && d[p + 6] != 0) cta.HdmiMaxTmdsMHz = d[p + 6] * 5;
                }
                else if (d[p] == 0xD8 && d[p + 1] == 0x5D && d[p + 2] == 0xC4)
                {
                    cta.HasHfVsdb = true;
                    if (len >= 5 && d[p + 4] != 0) cta.HfMaxTmdsMHz = d[p + 4] * 5;
                }
            }
            i += 1 + len;
        }

        // DTDs.
        if (dtdStart != 0)
        {
            int o = dtdStart;
            int n = 0;
            while (o + DetailedTiming.DescriptorLength <= 127)
            {
                var t = DetailedTiming.Decode(d, b + o);
                if (t == null) break;
                cta.Dtds.Add(t);
                n++;
                info.Modes.Add(ModeFrom(t, $"CTA bloco {block} DTD #{n}"));
                o += DetailedTiming.DescriptorLength;
            }
            if (o + DetailedTiming.DescriptorLength <= 127 && IsZero(d, b + o, DetailedTiming.DescriptorLength))
                cta.FreeDtdOffset = o;
        }

        info.CtaExtensions.Add(cta);
    }

    private static void AddSvd(EdidInfo info, CtaExtension cta, byte svd)
    {
        int vic;
        bool native = false;
        if (svd >= 1 && svd <= 127) vic = svd;
        else if (svd >= 129 && svd <= 192) { vic = svd & 0x7F; native = true; }
        else if (svd >= 193 && svd <= 253) vic = svd;
        else return;

        cta.Vics.Add(vic);
        if (native) cta.NativeVics.Add(vic);

        var e = CtaVicTable.Get(vic);
        if (e != null)
        {
            info.Modes.Add(new AdvertisedMode(e.Width, e.Height, e.RefreshHz, $"CTA VIC {vic}" + (native ? " (nativo)" : ""),
                (int)Math.Round(e.PixelClockMHz * 1000), null, e.Interlaced));
        }
    }

    private static void ParseDisplayId(EdidInfo info, byte[] d, int block)
    {
        info.DisplayIdBlocks.Add(block);
        int b = block * BlockLength;
        int sectionBytes = d[b + 2];
        int i = 5;
        int end = Math.Min(5 + sectionBytes, 127);
        int n = 0;
        while (i + 3 <= end)
        {
            int tag = d[b + i];
            int len = d[b + i + 2];
            if (tag == 0 && len == 0) break;
            int p = i + 3;
            if (p + len > end) break;

            if (tag == 0x03 || tag == 0x22) // Type I (10 kHz) / Type VII (1 kHz) detailed timing
            {
                for (int k = 0; k + 20 <= len; k += 20)
                {
                    int q = b + p + k;
                    int raw = d[q] | (d[q + 1] << 8) | (d[q + 2] << 16);
                    int pclkKHz = tag == 0x03 ? (raw + 1) * 10 : raw + 1;
                    int U16(int at) => d[q + at] | (d[q + at + 1] << 8);
                    int hActive = U16(4) + 1, hBlank = U16(6) + 1;
                    int hFront = (U16(8) & 0x7FFF) + 1, hSync = U16(10) + 1;
                    int vActive = U16(12) + 1, vBlank = U16(14) + 1;
                    int vFront = (U16(16) & 0x7FFF) + 1, vSync = U16(18) + 1;
                    var t = new DetailedTiming
                    {
                        PixelClockKHz = pclkKHz,
                        HActive = hActive, HFrontPorch = hFront, HSyncWidth = hSync, HBackPorch = hBlank - hFront - hSync,
                        VActive = vActive, VFrontPorch = vFront, VSyncWidth = vSync, VBackPorch = vBlank - vFront - vSync,
                        HSyncPositive = (d[q + 9] & 0x80) != 0,
                        VSyncPositive = (d[q + 17] & 0x80) != 0,
                        Interlaced = (d[q + 3] & 0x10) != 0,
                    };
                    n++;
                    info.Modes.Add(ModeFrom(t, $"DisplayID bloco {block} timing #{n}"));
                }
            }
            i = p + len;
        }
    }

    private static AdvertisedMode ModeFrom(DetailedTiming t, string source) =>
        new AdvertisedMode(t.HActive, t.DisplayHeight, Math.Round(t.RefreshHz, 2), source, t.PixelClockKHz, t.HFreqKHz, t.Interlaced);

    private static bool IsZero(byte[] d, int o, int len)
    {
        for (int i = 0; i < len; i++) if (d[o + i] != 0) return false;
        return true;
    }

    /// <summary>Hex dump legível, 16 bytes por linha, com separação por bloco.</summary>
    public static string HexDump(byte[] d)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < d.Length; i += 16)
        {
            if (i % BlockLength == 0) sb.AppendLine($"-- bloco {i / BlockLength} --");
            sb.Append(i.ToString("X4", CultureInfo.InvariantCulture)).Append(": ");
            for (int k = 0; k < 16 && i + k < d.Length; k++)
                sb.Append(d[i + k].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
