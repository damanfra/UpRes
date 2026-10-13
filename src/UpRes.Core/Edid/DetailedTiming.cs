using System;
using System.Globalization;

namespace UpRes.Core.Edid;

/// <summary>
/// Um timing completo (equivalente a um Detailed Timing Descriptor de 18 bytes do EDID).
/// Valores verticais de modos entrelaçados são por campo, como no próprio DTD.
/// </summary>
public sealed class DetailedTiming
{
    public const int DescriptorLength = 18;

    public int PixelClockKHz { get; set; }

    public int HActive { get; set; }
    public int HFrontPorch { get; set; }
    public int HSyncWidth { get; set; }
    public int HBackPorch { get; set; }

    public int VActive { get; set; }
    public int VFrontPorch { get; set; }
    public int VSyncWidth { get; set; }
    public int VBackPorch { get; set; }

    public bool HSyncPositive { get; set; }
    public bool VSyncPositive { get; set; }
    public bool Interlaced { get; set; }

    /// <summary>Tamanho da imagem em mm (bytes 12–14 do DTD). Copiado do timing nativo ao gerar o override.</summary>
    public int ImageWidthMm { get; set; }
    public int ImageHeightMm { get; set; }

    /// <summary>Byte 17 bruto do DTD (flags), preservado na decodificação.</summary>
    public byte RawFlags { get; set; }

    public int HBlank => HFrontPorch + HSyncWidth + HBackPorch;
    public int HTotal => HActive + HBlank;
    public int VBlank => VFrontPorch + VSyncWidth + VBackPorch;
    public int VTotal => VActive + VBlank;

    /// <summary>Altura exibida (para entrelaçado o DTD guarda a altura de um campo).</summary>
    public int DisplayHeight => Interlaced ? VActive * 2 : VActive;

    public double PixelClockMHz => PixelClockKHz / 1000.0;
    public double HFreqKHz => HTotal == 0 ? 0 : (double)PixelClockKHz / HTotal;
    public double RefreshHz => HTotal == 0 || VTotal == 0 ? 0 : PixelClockKHz * 1000.0 / ((double)HTotal * VTotal);

    public bool Is(int width, int height) => HActive == width && DisplayHeight == height;

    public DetailedTiming Clone() => (DetailedTiming)MemberwiseClone();

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}x{1}{2} @ {3:0.00} Hz ({4:0.00} MHz, {5:0.00} kHz)",
            HActive, DisplayHeight, Interlaced ? "i" : "", RefreshHz, PixelClockMHz, HFreqKHz);

    public string Describe() =>
        string.Format(CultureInfo.InvariantCulture,
            "{0}\n  H: ativo {1} / front {2} / sync {3} / back {4} / total {5}  ({6})\n  V: ativo {7} / front {8} / sync {9} / back {10} / total {11}  ({12})",
            this,
            HActive, HFrontPorch, HSyncWidth, HBackPorch, HTotal, HSyncPositive ? "H+" : "H-",
            VActive, VFrontPorch, VSyncWidth, VBackPorch, VTotal, VSyncPositive ? "V+" : "V-");

    /// <summary>Decodifica um DTD. Retorna null se os 18 bytes forem um descritor de monitor (pixel clock 0).</summary>
    public static DetailedTiming? Decode(byte[] d, int o)
    {
        if (d == null) throw new ArgumentNullException(nameof(d));
        if (o < 0 || o + DescriptorLength > d.Length) throw new ArgumentOutOfRangeException(nameof(o));

        int pc = d[o] | (d[o + 1] << 8);
        if (pc == 0) return null;

        int hActive = d[o + 2] | ((d[o + 4] & 0xF0) << 4);
        int hBlank = d[o + 3] | ((d[o + 4] & 0x0F) << 8);
        int vActive = d[o + 5] | ((d[o + 7] & 0xF0) << 4);
        int vBlank = d[o + 6] | ((d[o + 7] & 0x0F) << 8);
        int hFront = d[o + 8] | ((d[o + 11] & 0xC0) << 2);
        int hSync = d[o + 9] | ((d[o + 11] & 0x30) << 4);
        int vFront = (d[o + 10] >> 4) | ((d[o + 11] & 0x0C) << 2);
        int vSync = (d[o + 10] & 0x0F) | ((d[o + 11] & 0x03) << 4);
        int wMm = d[o + 12] | ((d[o + 14] & 0xF0) << 4);
        int hMm = d[o + 13] | ((d[o + 14] & 0x0F) << 8);
        byte flags = d[o + 17];
        bool digitalSeparate = (flags & 0x18) == 0x18;

        return new DetailedTiming
        {
            PixelClockKHz = pc * 10,
            HActive = hActive,
            HFrontPorch = hFront,
            HSyncWidth = hSync,
            HBackPorch = hBlank - hFront - hSync,
            VActive = vActive,
            VFrontPorch = vFront,
            VSyncWidth = vSync,
            VBackPorch = vBlank - vFront - vSync,
            Interlaced = (flags & 0x80) != 0,
            HSyncPositive = digitalSeparate && (flags & 0x02) != 0,
            VSyncPositive = digitalSeparate && (flags & 0x04) != 0,
            ImageWidthMm = wMm,
            ImageHeightMm = hMm,
            RawFlags = flags,
        };
    }

    /// <summary>Codifica como DTD (sincronismo digital separado) nos 18 bytes a partir de <paramref name="o"/>.</summary>
    public void Encode(byte[] d, int o)
    {
        if (d == null) throw new ArgumentNullException(nameof(d));
        if (o < 0 || o + DescriptorLength > d.Length) throw new ArgumentOutOfRangeException(nameof(o));
        Validate();

        int pc = (PixelClockKHz + 5) / 10;
        int hBlank = HBlank, vBlank = VBlank;

        d[o + 0] = (byte)(pc & 0xFF);
        d[o + 1] = (byte)(pc >> 8);
        d[o + 2] = (byte)(HActive & 0xFF);
        d[o + 3] = (byte)(hBlank & 0xFF);
        d[o + 4] = (byte)((((HActive >> 8) & 0x0F) << 4) | ((hBlank >> 8) & 0x0F));
        d[o + 5] = (byte)(VActive & 0xFF);
        d[o + 6] = (byte)(vBlank & 0xFF);
        d[o + 7] = (byte)((((VActive >> 8) & 0x0F) << 4) | ((vBlank >> 8) & 0x0F));
        d[o + 8] = (byte)(HFrontPorch & 0xFF);
        d[o + 9] = (byte)(HSyncWidth & 0xFF);
        d[o + 10] = (byte)(((VFrontPorch & 0x0F) << 4) | (VSyncWidth & 0x0F));
        d[o + 11] = (byte)((((HFrontPorch >> 8) & 0x03) << 6) | (((HSyncWidth >> 8) & 0x03) << 4)
                           | (((VFrontPorch >> 4) & 0x03) << 2) | ((VSyncWidth >> 4) & 0x03));
        d[o + 12] = (byte)(ImageWidthMm & 0xFF);
        d[o + 13] = (byte)(ImageHeightMm & 0xFF);
        d[o + 14] = (byte)((((ImageWidthMm >> 8) & 0x0F) << 4) | ((ImageHeightMm >> 8) & 0x0F));
        d[o + 15] = 0; // borda H
        d[o + 16] = 0; // borda V
        d[o + 17] = (byte)((Interlaced ? 0x80 : 0) | 0x18 | (VSyncPositive ? 0x04 : 0) | (HSyncPositive ? 0x02 : 0));
    }

    public byte[] ToBytes()
    {
        var b = new byte[DescriptorLength];
        Encode(b, 0);
        return b;
    }

    /// <summary>Verifica se os valores cabem nos campos de um DTD.</summary>
    public void Validate()
    {
        void Check(bool ok, string msg) { if (!ok) throw new InvalidOperationException("Timing não cabe em um DTD: " + msg); }
        Check(PixelClockKHz > 0 && PixelClockKHz <= 655350, "pixel clock fora de 0,01–655,35 MHz");
        Check(HActive > 0 && HActive <= 4095, "H ativo > 4095");
        Check(HBlank > 0 && HBlank <= 4095, "H blank > 4095");
        Check(VActive > 0 && VActive <= 4095, "V ativo > 4095");
        Check(VBlank > 0 && VBlank <= 4095, "V blank > 4095");
        Check(HFrontPorch >= 0 && HFrontPorch <= 1023, "H front porch > 1023");
        Check(HSyncWidth > 0 && HSyncWidth <= 1023, "H sync > 1023");
        Check(VFrontPorch >= 0 && VFrontPorch <= 63, "V front porch > 63");
        Check(VSyncWidth > 0 && VSyncWidth <= 63, "V sync > 63");
        Check(HBackPorch >= 0 && VBackPorch >= 0, "back porch negativo");
        Check(ImageWidthMm >= 0 && ImageWidthMm <= 4095 && ImageHeightMm >= 0 && ImageHeightMm <= 4095, "tamanho da imagem");
    }
}
