using System.Collections.Generic;

namespace UpRes.Core.Edid;

/// <summary>
/// Subconjunto da tabela de VICs (Video Identification Codes) do CTA-861.
/// Não existe VIC para 2560x1440; a tabela serve para listar modos e detectar
/// monitores que anunciam resoluções maiores que a nativa (ex.: 4K em monitor 1080p).
/// </summary>
public static class CtaVicTable
{
    public sealed class Entry
    {
        public Entry(int w, int h, double hz, double mhz, bool interlaced = false)
        { Width = w; Height = h; RefreshHz = hz; PixelClockMHz = mhz; Interlaced = interlaced; }
        public int Width { get; }
        public int Height { get; }
        public double RefreshHz { get; }
        public double PixelClockMHz { get; }
        public bool Interlaced { get; }
    }

    private static readonly Dictionary<int, Entry> Table = new Dictionary<int, Entry>
    {
        [1] = new Entry(640, 480, 59.94, 25.175),
        [2] = new Entry(720, 480, 59.94, 27.0),
        [3] = new Entry(720, 480, 59.94, 27.0),
        [4] = new Entry(1280, 720, 60, 74.25),
        [5] = new Entry(1920, 1080, 60, 74.25, true),
        [6] = new Entry(1440, 480, 59.94, 27.0, true),
        [7] = new Entry(1440, 480, 59.94, 27.0, true),
        [16] = new Entry(1920, 1080, 60, 148.5),
        [17] = new Entry(720, 576, 50, 27.0),
        [18] = new Entry(720, 576, 50, 27.0),
        [19] = new Entry(1280, 720, 50, 74.25),
        [20] = new Entry(1920, 1080, 50, 74.25, true),
        [21] = new Entry(1440, 576, 50, 27.0, true),
        [22] = new Entry(1440, 576, 50, 27.0, true),
        [31] = new Entry(1920, 1080, 50, 148.5),
        [32] = new Entry(1920, 1080, 24, 74.25),
        [33] = new Entry(1920, 1080, 25, 74.25),
        [34] = new Entry(1920, 1080, 30, 74.25),
        [60] = new Entry(1280, 720, 24, 59.4),
        [61] = new Entry(1280, 720, 25, 74.25),
        [62] = new Entry(1280, 720, 30, 74.25),
        [63] = new Entry(1920, 1080, 120, 297.0),
        [64] = new Entry(1920, 1080, 100, 297.0),
        [93] = new Entry(3840, 2160, 24, 297.0),
        [94] = new Entry(3840, 2160, 25, 297.0),
        [95] = new Entry(3840, 2160, 30, 297.0),
        [96] = new Entry(3840, 2160, 50, 594.0),
        [97] = new Entry(3840, 2160, 60, 594.0),
        [98] = new Entry(4096, 2160, 24, 297.0),
        [99] = new Entry(4096, 2160, 25, 297.0),
        [100] = new Entry(4096, 2160, 30, 297.0),
        [101] = new Entry(4096, 2160, 50, 594.0),
        [102] = new Entry(4096, 2160, 60, 594.0),
        [117] = new Entry(3840, 2160, 100, 1188.0),
        [118] = new Entry(3840, 2160, 120, 1188.0),
    };

    public static Entry? Get(int vic) => Table.TryGetValue(vic, out var e) ? e : null;
}
