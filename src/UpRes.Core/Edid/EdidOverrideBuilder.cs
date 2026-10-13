using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UpRes.Core.Edid;

public sealed class OverrideResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>EDID modificado completo (todos os blocos concatenados).</summary>
    public byte[] Edid { get; set; } = new byte[0];

    /// <summary>Um array de 128 bytes por bloco: vira os valores "0", "1"... do EDID_OVERRIDE.</summary>
    public List<byte[]> Blocks { get; } = new List<byte[]>();

    public List<string> Changes { get; } = new List<string>();
    public DetailedTiming? Timing { get; set; }

    public static OverrideResult Fail(string error) => new OverrideResult { Success = false, Error = error };
}

/// <summary>
/// Gera o EDID do override a partir do EDID original: copia fiel, mantém o timing preferido
/// e ADICIONA o novo timing como DTD extra. Ajusta o Range Limits se ele barrar o novo modo
/// e recalcula o checksum de cada bloco alterado.
/// </summary>
public static class EdidOverrideBuilder
{
    public static OverrideResult Build(byte[] original, DetailedTiming target)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (target == null) throw new ArgumentNullException(nameof(target));

        if (original.Length < EdidParser.BlockLength || original.Length % EdidParser.BlockLength != 0)
            return OverrideResult.Fail($"EDID original com tamanho inválido ({original.Length} bytes).");

        var info = EdidParser.Parse(original);
        if (!info.HeaderValid) return OverrideResult.Fail("Cabeçalho do EDID original inválido.");
        if (info.BadChecksumBlocks.Count > 0)
            return OverrideResult.Fail("EDID original com checksum inválido no(s) bloco(s) " + string.Join(", ", info.BadChecksumBlocks) + ". Leitura corrompida?");

        var timing = target.Clone();
        if (info.PreferredTiming != null && timing.ImageWidthMm == 0 && timing.ImageHeightMm == 0)
        {
            timing.ImageWidthMm = info.PreferredTiming.ImageWidthMm;
            timing.ImageHeightMm = info.PreferredTiming.ImageHeightMm;
        }
        try { timing.Validate(); }
        catch (InvalidOperationException ex) { return OverrideResult.Fail(ex.Message); }

        if (info.Modes.Any(m => m.Width == timing.HActive && m.Height == timing.DisplayHeight && Math.Abs(m.RefreshHz - timing.RefreshHz) < 0.5))
            return OverrideResult.Fail($"O EDID já anuncia {timing.HActive}x{timing.DisplayHeight} @ ~{timing.RefreshHz:0} Hz. Não é preciso override para este modo; o limite está na GPU, na porta ou no cabo.");

        var result = new OverrideResult { Timing = timing };
        var touched = new SortedSet<int>();
        var d = Place((byte[])info.Raw.Clone(), info, timing, result, touched);
        if (d == null)
            return OverrideResult.Fail("Não há espaço para mais um DTD: nenhum descritor 'dummy' livre no bloco base e nenhum slot livre na extensão CTA-861.");

        AdjustRangeLimits(d, info, timing, result, touched);

        foreach (int b in touched)
        {
            EdidParser.FixChecksum(d, b);
            result.Changes.Add($"Checksum do bloco {b} recalculado (0x{d[b * EdidParser.BlockLength + 127]:X2}).");
        }

        result.Edid = d;
        for (int b = 0; b < d.Length / EdidParser.BlockLength; b++)
        {
            var block = new byte[EdidParser.BlockLength];
            Array.Copy(d, b * EdidParser.BlockLength, block, 0, EdidParser.BlockLength);
            result.Blocks.Add(block);
        }

        // Autoverificação: o resultado precisa ser um EDID válido que contém o novo modo e mantém o preferido.
        var check = EdidParser.Parse(d);
        if (check.BadChecksumBlocks.Count > 0)
            return OverrideResult.Fail("Erro interno: checksum inválido no EDID gerado.");
        if (!check.Modes.Any(m => m.Width == timing.HActive && m.Height == timing.DisplayHeight && m.PixelClockKHz == (timing.PixelClockKHz + 5) / 10 * 10))
            return OverrideResult.Fail("Erro interno: o novo timing não aparece no EDID gerado.");
        if (info.PreferredTiming != null && check.PreferredTiming?.ToString() != info.PreferredTiming.ToString())
            return OverrideResult.Fail("Erro interno: o timing preferido foi alterado.");

        result.Success = true;
        return result;
    }

    /// <summary>Grava o DTD e devolve o EDID resultante (pode ser maior, caso 3), ou null se não houver espaço.</summary>
    private static byte[]? Place(byte[] d, EdidInfo info, DetailedTiming timing, OverrideResult r, SortedSet<int> touched)
    {
        // 1) Descritor "dummy" (tag 0x10) no bloco base. Nunca o 1º (preferido), o nome, o serial nem o range limits.
        var dummy = info.BaseDescriptors.FirstOrDefault(s => s.Index > 0 && s.Kind == DescriptorKind.Dummy);
        if (dummy != null)
        {
            timing.Encode(d, dummy.Offset);
            touched.Add(0);
            r.Changes.Add($"Novo DTD gravado no descritor #{dummy.Index + 1} do bloco base (offset {dummy.Offset}), que era um descritor vazio (0x10).");
            return d;
        }

        // 2) Slot livre após os DTDs de uma extensão CTA-861.
        foreach (var cta in info.CtaExtensions)
        {
            int b = cta.BlockIndex * EdidParser.BlockLength;
            if (cta.DtdOffset == 0)
            {
                // d=0: sem data blocks e sem DTDs. Passa a 4 (sem data blocks) e usa o início da área.
                d[b + 2] = 4;
                timing.Encode(d, b + 4);
                touched.Add(cta.BlockIndex);
                r.Changes.Add($"Extensão CTA-861 (bloco {cta.BlockIndex}) não tinha área de DTD; offset ajustado para 4 e novo DTD gravado no offset 4.");
                return d;
            }
            if (cta.FreeDtdOffset.HasValue)
            {
                timing.Encode(d, b + cta.FreeDtdOffset.Value);
                touched.Add(cta.BlockIndex);
                r.Changes.Add($"Novo DTD adicionado na extensão CTA-861 (bloco {cta.BlockIndex}, offset {cta.FreeDtdOffset.Value}), depois de {cta.Dtds.Count} DTD(s) existente(s).");
                return d;
            }
        }

        // 3) EDID só com o bloco base: cria uma extensão CTA-861 mínima contendo apenas o DTD.
        if (info.BlockCount == 1)
        {
            var bigger = new byte[EdidParser.BlockLength * 2];
            Array.Copy(d, bigger, EdidParser.BlockLength);
            int b = EdidParser.BlockLength;
            bigger[b + 0] = 0x02; // tag CTA-861
            bigger[b + 1] = 0x03; // revisão 3
            bigger[b + 2] = 0x04; // DTDs começam no byte 4 (sem data blocks)
            bigger[b + 3] = 0x00; // sem flags de áudio/YCbCr, 0 DTDs nativos
            timing.Encode(bigger, b + 4);
            bigger[126] = 1;
            r.Changes.Add("O EDID não tinha extensões: criada uma extensão CTA-861 (bloco 1) com o novo DTD; contador de extensões do bloco base = 1.");
            touched.Add(0);
            touched.Add(1);
            return bigger;
        }

        return null;
    }

    private static void AdjustRangeLimits(byte[] d, EdidInfo info, DetailedTiming timing, OverrideResult r, SortedSet<int> touched)
    {
        var rl = info.RangeLimits;
        if (rl == null) return;
        int o = rl.Offset;
        byte flags = d[o + 4];

        int needPclk = (int)Math.Ceiling(timing.PixelClockMHz / 10.0);
        if (rl.MaxPixelClockMHz < timing.PixelClockMHz)
        {
            if (needPclk > 255) needPclk = 255;
            d[o + 9] = (byte)needPclk;
            if (rl.TimingSupport == 0x04) d[o + 12] = (byte)(d[o + 12] & 0x03); // zera a precisão adicional do CVT
            r.Changes.Add(string.Format(CultureInfo.InvariantCulture,
                "Range Limits: pixel clock máximo {0:0.##} → {1} MHz.", rl.MaxPixelClockMHz, needPclk * 10));
            touched.Add(0);
        }

        int needH = (int)Math.Ceiling(timing.HFreqKHz);
        if (rl.MaxHKHz < needH && (flags & 0x08) == 0 && needH <= 255)
        {
            r.Changes.Add($"Range Limits: frequência horizontal máxima {rl.MaxHKHz} → {needH} kHz.");
            d[o + 8] = (byte)needH;
            touched.Add(0);
        }

        int needV = (int)Math.Ceiling(timing.RefreshHz);
        if (rl.MaxVHz < needV && (flags & 0x02) == 0 && needV <= 255)
        {
            r.Changes.Add($"Range Limits: frequência vertical máxima {rl.MaxVHz} → {needV} Hz.");
            d[o + 6] = (byte)needV;
            touched.Add(0);
        }
        int lowV = (int)Math.Floor(timing.RefreshHz);
        if (rl.MinVHz > lowV && (flags & 0x03) != 0x03)
        {
            r.Changes.Add($"Range Limits: frequência vertical mínima {rl.MinVHz} → {lowV} Hz.");
            d[o + 5] = (byte)lowV;
            touched.Add(0);
        }

        if (rl.TimingSupport == 0x04 && rl.CvtMaxActivePixels > 0 && rl.CvtMaxActivePixels < timing.HActive)
        {
            r.Changes.Add($"Range Limits (CVT): limite de {rl.CvtMaxActivePixels} pixels ativos por linha removido.");
            d[o + 12] = (byte)(d[o + 12] & 0xFC);
            d[o + 13] = 0;
            touched.Add(0);
        }
    }
}
