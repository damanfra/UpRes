using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UpRes.Core.Edid;

namespace UpRes.Core.Analysis;

public enum Verdict
{
    Provavel,
    Incerto,
    Improvavel,
    NaoSeAplica,
    JaDisponivel,
}

public enum CheckStatus
{
    Ok,
    Info,
    Atencao,
    Falha,
}

public sealed class Check
{
    public Check(string title, CheckStatus status, string detail) { Title = title; Status = status; Detail = detail; }
    public string Title { get; }
    public CheckStatus Status { get; }
    public string Detail { get; }

    public string StatusLabel => Status switch
    {
        CheckStatus.Ok => "[OK]",
        CheckStatus.Info => "[INFO]",
        CheckStatus.Atencao => "[ATENÇÃO]",
        _ => "[FALHA]",
    };
}

public sealed class Evaluation
{
    public Verdict Verdict { get; set; }
    public string VerdictText { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<Check> Checks { get; } = new List<Check>();
    public DetailedTiming Target { get; set; } = new DetailedTiming();
    public EdidInfo? Edid { get; set; }
    public EdidInfo? ExistingOverride { get; set; }
    public OverrideResult? Override { get; set; }

    /// <summary>Veredito permite gerar os arquivos sem confirmação extra.</summary>
    public bool RecommendGenerate => Override?.Success == true && (Verdict == Verdict.Provavel || Verdict == Verdict.Incerto);

    /// <summary>Taxa (Hz) que caberia nos limites anunciados, quando a pedida não cabe.</summary>
    public int? SuggestedRefreshHz { get; set; }

    public const string ScalerDisclaimer =
        "Nenhuma leitura garante que o scaler do monitor aceita 2560 pixels por linha. " +
        "O veredito é heurístico; só o teste prova. Tenha o desfazer pronto antes de aplicar.";
}

/// <summary>Heurísticas somente leitura para decidir se vale tentar o override.</summary>
public static class Evaluator
{
    public static Evaluation Evaluate(MonitorContext ctx, DetailedTiming target)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (target == null) throw new ArgumentNullException(nameof(target));

        var ev = new Evaluation { Target = target };
        int tw = target.HActive, th = target.DisplayHeight;
        string tLabel = $"{tw}x{th}";

        // Conexão
        bool internalPanel = ctx.Connection == ConnectionKind.Internal;
        ev.Checks.Add(ctx.Connection switch
        {
            ConnectionKind.Internal => new Check("Conexão", CheckStatus.Falha, $"Tela interna ({ctx.ConnectionRaw}). Override em painel interno de notebook está fora do escopo e costuma ser ignorado."),
            ConnectionKind.Unknown => new Check("Conexão", CheckStatus.Info, "Tipo de conexão desconhecido."),
            ConnectionKind.Vga => new Check("Conexão", CheckStatus.Atencao, "Conexão analógica (VGA). Funciona em teoria, mas a qualidade a 241 MHz costuma ser ruim."),
            _ => new Check("Conexão", CheckStatus.Ok, ctx.ConnectionRaw.Length > 0 ? ctx.ConnectionRaw : ctx.Connection.ToString()),
        });

        // GPU
        ev.Checks.Add(ctx.Gpu switch
        {
            GpuVendor.Intel => new Check("GPU", CheckStatus.Ok, $"{ctx.GpuName} (Intel). Na máquina de referência (Iris Xe) o driver respeitou o EDID override."),
            GpuVendor.Nvidia => new Check("GPU", CheckStatus.Info, $"{ctx.GpuName} (NVIDIA). O driver costuma respeitar o EDID override; não testado neste projeto."),
            GpuVendor.Amd => new Check("GPU", CheckStatus.Info, $"{ctx.GpuName} (AMD). O driver costuma respeitar o EDID override; não testado neste projeto."),
            _ => new Check("GPU", CheckStatus.Info, string.IsNullOrEmpty(ctx.GpuName) ? "GPU não identificada." : ctx.GpuName),
        });

        if (!ctx.RegistryKeyFound)
            ev.Checks.Add(new Check("Chave no registro", CheckStatus.Atencao,
                $"A chave {ctx.RegistrySubPath} não foi encontrada. O .reg gerado pode não ter efeito."));

        // Override existente
        if (ctx.ExistingOverride != null && ctx.ExistingOverride.Length >= EdidParser.BlockLength)
        {
            try
            {
                ev.ExistingOverride = EdidParser.Parse(ctx.ExistingOverride);
                bool has = ev.ExistingOverride.Modes.Any(m => m.Width == tw && m.Height == th);
                ev.Checks.Add(new Check("Override existente", CheckStatus.Info,
                    $"Já existe EDID_OVERRIDE para este monitor nesta máquina ({ev.ExistingOverride.BlockCount} bloco(s)); " +
                    (has ? $"ele contém {tLabel}." : $"ele NÃO contém {tLabel}.") +
                    " Veja a aba 'Override atual' para um backup."));
            }
            catch (FormatException ex)
            {
                ev.Checks.Add(new Check("Override existente", CheckStatus.Atencao, "Existe EDID_OVERRIDE, mas é inválido: " + ex.Message));
            }
        }

        if (ctx.Edid == null || ctx.Edid.Length < EdidParser.BlockLength)
        {
            ev.Checks.Add(new Check("EDID", CheckStatus.Falha, "Não foi possível ler o EDID deste monitor."));
            return Finish(ev, internalPanel ? Verdict.Improvavel : Verdict.Incerto, "Sem EDID não há como gerar o override.");
        }

        EdidInfo edid;
        try { edid = EdidParser.Parse(ctx.Edid); }
        catch (FormatException ex)
        {
            ev.Checks.Add(new Check("EDID", CheckStatus.Falha, ex.Message));
            return Finish(ev, Verdict.Incerto, "EDID ilegível.");
        }
        ev.Edid = edid;

        if (ev.ExistingOverride != null && ctx.ExistingOverride != null && ctx.Edid.SequenceEqual(ctx.ExistingOverride))
            ev.Checks.Add(new Check("Fonte do EDID", CheckStatus.Atencao,
                "O EDID lido é idêntico ao override existente; o original pode estar mascarado. Se possível, rode o app numa máquina sem override."));

        if (!edid.HeaderValid || edid.BadChecksumBlocks.Count > 0)
            ev.Checks.Add(new Check("Integridade do EDID", CheckStatus.Falha,
                "Cabeçalho ou checksum inválido" + (edid.BadChecksumBlocks.Count > 0 ? " (blocos " + string.Join(", ", edid.BadChecksumBlocks) + ")" : "") + "."));
        else
            ev.Checks.Add(new Check("Integridade do EDID", CheckStatus.Ok, $"EDID {edid.Version}, {edid.BlockCount} bloco(s), checksums válidos. Fonte: {ctx.EdidSource}."));

        // Identificação / modelo confirmado
        var known = KnownModels.Find(edid.HardwareId);
        ev.Checks.Add(known != null
            ? new Check("Modelo", CheckStatus.Ok, $"{known.Name} ({edid.HardwareId}) está na lista de modelos confirmados. {known.Note}")
            : new Check("Modelo", CheckStatus.Info, $"{edid.DisplayName} ({edid.HardwareId}) não está na lista de modelos confirmados."));

        // Resolução nativa
        var native = edid.PreferredTiming;
        if (native != null)
        {
            if (native.HActive >= tw && native.DisplayHeight >= th)
            {
                ev.Checks.Add(new Check("Resolução nativa", CheckStatus.Info, $"Nativa {native.HActive}x{native.DisplayHeight}: já é {tLabel} ou maior."));
                return Finish(ev, Verdict.NaoSeAplica, "O monitor já é nativamente desta resolução ou maior.");
            }
            ev.Checks.Add(new Check("Resolução nativa", CheckStatus.Info, $"Timing preferido: {native}."));
        }

        // Windows já oferece?
        var offered = ctx.WindowsModes.Where(m => m.Width == tw && m.Height == th).ToList();
        if (offered.Count > 0)
        {
            ev.Checks.Add(new Check("Windows", CheckStatus.Ok,
                $"O Windows já oferece {tLabel} ({string.Join(", ", offered.Select(m => m.RefreshHz + " Hz").Distinct())})" +
                (ctx.ExistingOverride != null ? ", provavelmente pelo override existente." : ".")));
            return Finish(ev, Verdict.JaDisponivel, $"{tLabel} já está disponível em Configurações > Tela. Nada a fazer.");
        }

        // 1440p já no EDID?
        var inEdid = edid.Modes.Where(m => m.Width == tw && m.Height == th).ToList();
        if (inEdid.Count > 0)
            ev.Checks.Add(new Check($"{tLabel} no EDID", CheckStatus.Atencao,
                $"O EDID já anuncia {tLabel} ({string.Join("; ", inEdid.Select(m => m.ToString()))}), mas o Windows não oferece. " +
                "O limite provavelmente é a GPU, a porta ou o cabo, e não o monitor."));
        else
            ev.Checks.Add(new Check($"{tLabel} no EDID", CheckStatus.Info,
                $"Não anunciado (normal: não existe VIC CTA-861 para {tLabel} e Standard Timings não chegam a 2560 de largura)."));

        // Anuncia algo maior que o nativo?
        bool aboveNative = false;
        if (native != null)
        {
            var bigger = edid.Modes.Where(m => m.Width > native.HActive || m.Height > native.DisplayHeight).ToList();
            aboveNative = bigger.Count > 0;
            if (aboveNative)
                ev.Checks.Add(new Check("Acima da nativa", CheckStatus.Ok,
                    "O monitor já anuncia resoluções maiores que a nativa (" + string.Join("; ", bigger.Select(m => $"{m.Width}x{m.Height}@{m.RefreshHz:0.##}").Distinct().Take(5)) +
                    "). O scaler aceita entrada acima do painel: forte indício positivo."));
        }

        // Banda (pixel clock)
        var withClock = edid.Modes.Where(m => m.PixelClockKHz.HasValue).ToList();
        CheckStatus pclkStatus = CheckStatus.Info;
        double maxPclkMHz = 0;
        if (withClock.Count > 0)
        {
            var top = withClock.OrderByDescending(m => m.PixelClockKHz).First();
            maxPclkMHz = top.PixelClockKHz!.Value / 1000.0;
            pclkStatus = maxPclkMHz >= target.PixelClockMHz ? CheckStatus.Ok
                : maxPclkMHz >= target.PixelClockMHz * 0.85 ? CheckStatus.Atencao : CheckStatus.Falha;
            ev.Checks.Add(new Check("Banda (pixel clock)", pclkStatus, F(
                "Maior pixel clock anunciado: {0:0.00} MHz ({1}). Necessário: {2:0.00} MHz. {3}",
                maxPclkMHz, top, target.PixelClockMHz,
                pclkStatus == CheckStatus.Ok ? "A entrada já recebe essa banda; ela não é o gargalo." :
                pclkStatus == CheckStatus.Atencao ? "Pouco abaixo do necessário: pode funcionar, mas sem folga." :
                "Bem abaixo do necessário: a entrada provavelmente não aguenta.")));
        }
        else
        {
            ev.Checks.Add(new Check("Banda (pixel clock)", CheckStatus.Info, "Nenhum modo com pixel clock conhecido."));
        }

        // Frequência horizontal
        var withH = edid.Modes.Where(m => m.HFreqKHz.HasValue).ToList();
        CheckStatus hStatus = CheckStatus.Info;
        double maxH = 0;
        if (withH.Count > 0)
        {
            var top = withH.OrderByDescending(m => m.HFreqKHz).First();
            maxH = top.HFreqKHz!.Value;
            hStatus = maxH >= target.HFreqKHz ? CheckStatus.Ok
                : maxH >= target.HFreqKHz * 0.9 ? CheckStatus.Atencao : CheckStatus.Falha;
            ev.Checks.Add(new Check("Frequência horizontal", hStatus, F(
                "Maior anunciada: {0:0.00} kHz ({1}). Necessária: {2:0.00} kHz. {3}",
                maxH, top, target.HFreqKHz,
                hStatus == CheckStatus.Ok ? "Dentro do que o scaler já recebe." :
                hStatus == CheckStatus.Atencao ? "Um pouco acima do que o monitor anuncia." :
                "Bem acima do que o monitor anuncia; costuma dar 'Out of range'.")));
        }

        // Range limits
        var rl = edid.RangeLimits;
        if (rl != null)
        {
            var issues = new List<string>();
            if (rl.MaxPixelClockMHz < target.PixelClockMHz) issues.Add(F("pixel clock máx. {0:0.##} MHz", rl.MaxPixelClockMHz));
            if (rl.MaxHKHz < target.HFreqKHz) issues.Add($"H máx. {rl.MaxHKHz} kHz");
            if (rl.MaxVHz < target.RefreshHz) issues.Add($"V máx. {rl.MaxVHz} Hz");
            if (rl.CvtMaxActivePixels > 0 && rl.CvtMaxActivePixels < tw) issues.Add($"máx. {rl.CvtMaxActivePixels} pixels/linha");
            ev.Checks.Add(issues.Count == 0
                ? new Check("Range Limits (0xFD)", CheckStatus.Ok, rl + ". Comporta o novo modo.")
                : new Check("Range Limits (0xFD)", CheckStatus.Info, rl + ". Abaixo do novo modo em: " + string.Join(", ", issues) +
                    ". O override ajusta esses campos (vários drivers descartam modos fora do Range Limits)."));
        }
        else
        {
            ev.Checks.Add(new Check("Range Limits (0xFD)", CheckStatus.Info, "Descritor ausente."));
        }

        // HDMI
        CheckStatus hdmiStatus = CheckStatus.Info;
        if (ctx.Connection == ConnectionKind.Hdmi || ctx.Connection == ConnectionKind.Unknown)
        {
            int? tmds = Max(edid.HdmiMaxTmdsMHz, edid.HfMaxTmdsMHz);
            if (tmds.HasValue)
            {
                hdmiStatus = tmds.Value >= target.PixelClockMHz ? CheckStatus.Ok : CheckStatus.Falha;
                if (ctx.Connection == ConnectionKind.Unknown && hdmiStatus == CheckStatus.Falha) hdmiStatus = CheckStatus.Atencao;
                ev.Checks.Add(new Check("HDMI (Max TMDS)", hdmiStatus, F("Max TMDS anunciado: {0} MHz; necessário {1:0.00} MHz.", tmds.Value, target.PixelClockMHz)));
            }
            else if (ctx.Connection == ConnectionKind.Hdmi && !edid.HasHdmiVsdb)
            {
                hdmiStatus = target.PixelClockMHz > 165 ? CheckStatus.Atencao : CheckStatus.Ok;
                ev.Checks.Add(new Check("HDMI (Max TMDS)", hdmiStatus,
                    "Sem HDMI VSDB: o driver pode tratar o monitor como DVI single-link (limite de 165 MHz)."));
            }
            else if (ctx.Connection == ConnectionKind.Hdmi)
            {
                ev.Checks.Add(new Check("HDMI (Max TMDS)", CheckStatus.Info, "HDMI VSDB sem Max TMDS Clock declarado."));
            }
        }
        else if (ctx.Connection == ConnectionKind.DisplayPort || ctx.Connection == ConnectionKind.UsbTunnel)
        {
            ev.Checks.Add(new Check("Link DisplayPort", CheckStatus.Info, "Em DisplayPort a banda do link raramente limita 241,5 MHz (HBR2 x4 ≈ 540 MHz a 24 bpp)."));
        }

        // Montagem do override
        ev.Override = EdidOverrideBuilder.Build(ctx.Edid, target);
        ev.Checks.Add(ev.Override.Success
            ? new Check("Override", CheckStatus.Ok, "EDID modificado gerado: " + string.Join(" ", ev.Override.Changes))
            : new Check("Override", CheckStatus.Falha, ev.Override.Error ?? "Falha desconhecida."));

        ev.Checks.Add(new Check("Scaler", CheckStatus.Atencao, Evaluation.ScalerDisclaimer));

        // Sugestão de taxa menor quando a pedida não cabe
        if (pclkStatus == CheckStatus.Falha || hStatus == CheckStatus.Falha || hdmiStatus == CheckStatus.Falha)
        {
            double limP = maxPclkMHz > 0 ? maxPclkMHz : double.MaxValue;
            double limH = maxH > 0 ? maxH : double.MaxValue;
            int? tmds = Max(edid.HdmiMaxTmdsMHz, edid.HfMaxTmdsMHz);
            if (ctx.Connection == ConnectionKind.Hdmi && tmds.HasValue) limP = Math.Min(limP, tmds.Value);
            for (int hz = (int)Math.Floor(target.RefreshHz) - 1; hz >= 24; hz--)
            {
                var t = CvtReducedBlanking.Compute(tw, th, hz);
                if (t.PixelClockMHz <= limP && t.HFreqKHz <= limH) { ev.SuggestedRefreshHz = hz; break; }
            }
        }

        // Veredito
        if (internalPanel)
            return Finish(ev, Verdict.Improvavel, "Tela interna: fora do escopo.");
        if (pclkStatus == CheckStatus.Falha || hStatus == CheckStatus.Falha || hdmiStatus == CheckStatus.Falha)
            return Finish(ev, Verdict.Improvavel, "Os limites anunciados pelo monitor ficam bem abaixo do necessário para este modo." +
                (ev.SuggestedRefreshHz.HasValue ? $" Uma taxa de {ev.SuggestedRefreshHz} Hz caberia nos limites anunciados." : ""));
        if (known != null)
            return Finish(ev, Verdict.Provavel, "Modelo confirmado: o mesmo override já funcionou neste modelo.");
        if (aboveNative)
            return Finish(ev, Verdict.Provavel, "O monitor já aceita resoluções acima da nativa.");
        if (pclkStatus == CheckStatus.Ok && hStatus == CheckStatus.Ok)
            return Finish(ev, Verdict.Provavel, "A entrada já recebe mais banda e frequência horizontal do que o novo modo exige.");
        return Finish(ev, Verdict.Incerto, "Os dados do EDID não permitem concluir.");
    }

    private static Evaluation Finish(Evaluation ev, Verdict v, string summary)
    {
        ev.Verdict = v;
        ev.Summary = summary;
        ev.VerdictText = v switch
        {
            Verdict.Provavel => "PROVÁVEL",
            Verdict.Incerto => "INCERTO",
            Verdict.Improvavel => "IMPROVÁVEL",
            Verdict.NaoSeAplica => "NÃO SE APLICA",
            _ => "JÁ DISPONÍVEL",
        };
        if (v == Verdict.Provavel && KnownModels.Find(ev.Edid?.HardwareId ?? "") != null)
            ev.VerdictText = "PROVÁVEL (modelo confirmado)";
        return ev;
    }

    private static int? Max(int? a, int? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;

    private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
