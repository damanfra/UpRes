using System;

namespace UpRes.Core.Edid;

/// <summary>
/// Gerador de timings VESA CVT 1.2 com Reduced Blanking (v1), modo progressivo e sem margens.
/// Para 2560x1440 @ 60 Hz produz 241,50 MHz, H total 2720, V total 1481 (H+ / V-).
/// </summary>
public static class CvtReducedBlanking
{
    private const double MinVBlankMicroseconds = 460.0;
    private const int VFrontPorch = 3;
    private const int MinVBackPorch = 6;
    private const int HBlankPixels = 160;
    private const int HSyncPixels = 32;
    private const int HFrontPorchPixels = 48;
    private const double ClockStepMHz = 0.25;

    public static DetailedTiming Compute(int width, int height, double refreshHz)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (refreshHz <= 0) throw new ArgumentOutOfRangeException(nameof(refreshHz));

        // CVT exige largura múltipla de 8 (granularidade de célula).
        int hActive = width / 8 * 8;
        int vSync = VSyncForAspect(hActive, height);

        double hPeriodEstimate = (1000000.0 / refreshHz - MinVBlankMicroseconds) / height;
        int vbiLines = (int)Math.Floor(MinVBlankMicroseconds / hPeriodEstimate) + 1;
        int minVbi = VFrontPorch + vSync + MinVBackPorch;
        int actualVbi = Math.Max(vbiLines, minVbi);
        int totalV = height + actualVbi;
        int totalH = hActive + HBlankPixels;

        double pixelClockMHz = ClockStepMHz * Math.Floor(refreshHz * totalV * totalH / 1000000.0 / ClockStepMHz);

        return new DetailedTiming
        {
            PixelClockKHz = (int)Math.Round(pixelClockMHz * 1000),
            HActive = hActive,
            HFrontPorch = HFrontPorchPixels,
            HSyncWidth = HSyncPixels,
            HBackPorch = HBlankPixels - HFrontPorchPixels - HSyncPixels,
            VActive = height,
            VFrontPorch = VFrontPorch,
            VSyncWidth = vSync,
            VBackPorch = actualVbi - VFrontPorch - vSync,
            HSyncPositive = true,
            VSyncPositive = false,
        };
    }

    /// <summary>Largura do sync vertical que o CVT usa para identificar a proporção.</summary>
    private static int VSyncForAspect(int w, int h)
    {
        bool Is(int a, int b) => Math.Abs((double)w / h - (double)a / b) < 0.01;
        if (Is(4, 3)) return 4;
        if (Is(16, 9)) return 5;
        if (Is(16, 10)) return 6;
        if (Is(5, 4) || Is(15, 9)) return 7;
        return 10;
    }
}
