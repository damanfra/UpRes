using System;
using System.Text;
using UpRes.Core.Edid;

namespace UpRes.Core.Tests;

/// <summary>
/// EDIDs sintéticos para os testes. Substituir/complementar por EDIDs reais em Fixtures/ quando
/// tivermos o dump do monitor do Daniel.
/// </summary>
internal static class EdidFactory
{
    private static readonly byte[] Header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    public static DetailedTiming T1080p240 => new DetailedTiming
    {
        PixelClockKHz = 554600, HActive = 1920, HFrontPorch = 48, HSyncWidth = 32, HBackPorch = 80,
        VActive = 1080, VFrontPorch = 3, VSyncWidth = 5, VBackPorch = 23, HSyncPositive = true, VSyncPositive = false,
        ImageWidthMm = 600, ImageHeightMm = 340,
    };

    public static DetailedTiming T1080p144 => new DetailedTiming
    {
        PixelClockKHz = 325080, HActive = 1920, HFrontPorch = 48, HSyncWidth = 32, HBackPorch = 80,
        VActive = 1080, VFrontPorch = 3, VSyncWidth = 5, VBackPorch = 23, HSyncPositive = true,
        ImageWidthMm = 600, ImageHeightMm = 340,
    };

    public static DetailedTiming T1080p60 => new DetailedTiming
    {
        PixelClockKHz = 148500, HActive = 1920, HFrontPorch = 88, HSyncWidth = 44, HBackPorch = 148,
        VActive = 1080, VFrontPorch = 4, VSyncWidth = 5, VBackPorch = 36, HSyncPositive = true, VSyncPositive = true,
        ImageWidthMm = 527, ImageHeightMm = 296,
    };

    /// <summary>Monitor gamer 1080p 240 Hz (LG-like): base + CTA-861 com HDMI VSDB e 1 DTD; slot livre no CTA.</summary>
    public static byte[] Gaming240()
    {
        var d = new byte[256];
        Base(d, "GSM", 0x5BBF, 0xA5, 60, 34);
        T1080p240.Encode(d, 54);
        RangeLimits(d, 72, flags: 0x08, minV: 48, maxV: 240, minH: 30, maxH: 15 /* +255 = 270 */, maxPclk10: 56, support: 0x01);
        Text(d, 90, 0xFC, "LG ULTRAGEAR");
        Text(d, 108, 0xFF, "TESTSERIAL01");
        d[126] = 1;

        int b = 128;
        d[b + 0] = 0x02;
        d[b + 1] = 0x03;
        d[b + 3] = 0x70 | 0x01;
        int i = 4;
        d[b + i++] = (2 << 5) | 4; d[b + i++] = 0x90; d[b + i++] = 4; d[b + i++] = 3; d[b + i++] = 63; // VDB: VIC 16 nativo, 4, 3, 63
        d[b + i++] = (3 << 5) | 7; d[b + i++] = 0x03; d[b + i++] = 0x0C; d[b + i++] = 0x00; d[b + i++] = 0x10; d[b + i++] = 0x00; d[b + i++] = 0x00; d[b + i++] = 60; // HDMI VSDB, 300 MHz
        d[b + 2] = (byte)i; // 17
        T1080p144.Encode(d, b + i);

        EdidParser.FixChecksum(d, 0);
        EdidParser.FixChecksum(d, 1);
        return d;
    }

    /// <summary>Monitor de escritório 1080p 60 Hz, só bloco base, sem descritor dummy.</summary>
    public static byte[] Office60()
    {
        var d = new byte[128];
        Base(d, "DEL", 0xA0F1, 0x80, 53, 30);
        T1080p60.Encode(d, 54);
        RangeLimits(d, 72, flags: 0, minV: 56, maxV: 76, minH: 30, maxH: 83, maxPclk10: 17, support: 0x01);
        Text(d, 90, 0xFC, "DELL P2419H");
        Text(d, 108, 0xFF, "ABC123");
        EdidParser.FixChecksum(d, 0);
        return d;
    }

    /// <summary>Igual ao Office60, mas com o descritor 4 vazio (dummy 0x10) e range limits CVT limitando 1920 px/linha.</summary>
    public static byte[] Office60WithDummy()
    {
        var d = Office60();
        Array.Clear(d, 108, 18);
        d[108 + 3] = 0x10;
        RangeLimits(d, 72, flags: 0, minV: 56, maxV: 76, minH: 30, maxH: 83, maxPclk10: 17, support: 0x04);
        d[72 + 11] = 0x11;              // CVT 1.1
        d[72 + 12] = (2 << 2) | 0x00;   // precisão adicional: 2 x 0,25 MHz
        d[72 + 13] = 1920 / 8;          // máximo 1920 pixels/linha
        EdidParser.FixChecksum(d, 0);
        return d;
    }

    private static void Base(byte[] d, string mfg, ushort product, byte input, byte wCm, byte hCm)
    {
        Array.Copy(Header, d, 8);
        int v = ((mfg[0] - 'A' + 1) << 10) | ((mfg[1] - 'A' + 1) << 5) | (mfg[2] - 'A' + 1);
        d[8] = (byte)(v >> 8); d[9] = (byte)v;
        d[10] = (byte)product; d[11] = (byte)(product >> 8);
        d[12] = 0x01; d[16] = 12; d[17] = 30; // semana 12 de 2020
        d[18] = 1; d[19] = 4;
        d[20] = input; d[21] = wCm; d[22] = hCm; d[23] = 120; d[24] = 0x3A;
        d[35] = 0x21; d[36] = 0x08; d[37] = 0x00; // 640x480@60, 800x600@60, 1024x768@60
        for (int i = 38; i < 54; i++) d[i] = 0x01;
        d[38] = 0xD1; d[39] = 0xC0; // 1920x1080@60
    }

    private static void RangeLimits(byte[] d, int o, byte flags, byte minV, byte maxV, byte minH, byte maxH, byte maxPclk10, byte support)
    {
        Array.Clear(d, o, 18);
        d[o + 3] = 0xFD; d[o + 4] = flags;
        d[o + 5] = minV; d[o + 6] = maxV; d[o + 7] = minH; d[o + 8] = maxH; d[o + 9] = maxPclk10; d[o + 10] = support;
        if (support == 0x01) { d[o + 11] = 0x0A; for (int i = 12; i < 18; i++) d[o + i] = 0x20; }
    }

    private static void Text(byte[] d, int o, byte tag, string text)
    {
        Array.Clear(d, o, 18);
        d[o + 3] = tag;
        var bytes = Encoding.ASCII.GetBytes(text);
        int i = 0;
        for (; i < Math.Min(13, bytes.Length); i++) d[o + 5 + i] = bytes[i];
        if (i < 13) d[o + 5 + i++] = 0x0A;
        for (; i < 13; i++) d[o + 5 + i] = 0x20;
    }
}
