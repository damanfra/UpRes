using System;
using System.Globalization;
using System.Linq;
using System.Text;
using UpRes.Core.Analysis;
using UpRes.Core.Edid;

namespace UpRes.Core.Output;

/// <summary>Versão PowerShell do aplicar/desfazer, apenas como TEXTO para o usuário rodar manualmente.</summary>
public static class PowerShellGenerator
{
    public static string Apply(MonitorContext ctx, OverrideResult ov)
    {
        if (ov == null || !ov.Success) throw new InvalidOperationException("Override inválido.");
        var sb = new StringBuilder();
        sb.AppendLine("# Executar MANUALMENTE em um PowerShell como administrador. O UpRes nunca executa este texto.");
        sb.AppendLine($"# Adiciona {ov.Timing} ao EDID de {ctx.FriendlyName} ({ctx.HardwareId}) nesta máquina ({Environment.MachineName}).");
        sb.AppendLine($"$key = '{PsPath(ctx)}'");
        sb.AppendLine("if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force }");
        sb.AppendLine("New-Item -Path $key -Force | Out-Null");
        for (int i = 0; i < ov.Blocks.Count; i++)
        {
            sb.Append($"New-ItemProperty -Path $key -Name '{i}' -PropertyType Binary -Force -Value ([byte[]](");
            sb.Append(string.Join(",", ov.Blocks[i].Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture))));
            sb.AppendLine(")) | Out-Null");
        }
        sb.AppendLine("Write-Host 'EDID_OVERRIDE gravado. Reinicie ou desative/reative o monitor no Gerenciador de Dispositivos.'");
        return sb.ToString();
    }

    public static string Undo(MonitorContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Executar MANUALMENTE em um PowerShell como administrador. O UpRes nunca executa este texto.");
        sb.AppendLine($"$key = '{PsPath(ctx)}'");
        sb.AppendLine("if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force; Write-Host 'EDID_OVERRIDE removido.' } else { Write-Host 'Nenhum EDID_OVERRIDE encontrado.' }");
        return sb.ToString();
    }

    private static string PsPath(MonitorContext ctx) =>
        (@"HKLM:\" + ctx.RegistrySubPath + @"\Device Parameters\EDID_OVERRIDE").Replace("'", "''");
}

public static class InstructionsGenerator
{
    public static string Build(MonitorContext ctx, Evaluation ev)
    {
        var t = ev.Override?.Timing ?? ev.Target;
        string res = $"{t.HActive}x{t.DisplayHeight}";
        var sb = new StringBuilder();
        sb.AppendLine($"COMO APLICAR {res} EM {ctx.FriendlyName} ({ctx.HardwareId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        sb.AppendLine("Antes de começar");
        sb.AppendLine("  - Ambiente corporativo: alterar o registro pode gerar alerta no antivírus (SentinelOne).");
        sb.AppendLine("    Combine com o TI antes. Não tente contornar o antivírus.");
        sb.AppendLine("  - Os arquivos gerados valem SÓ para este monitor, nesta máquina, nesta porta/GPU");
        sb.AppendLine($"    (InstanceID {ctx.InstanceId}). Em outra máquina, rode o UpRes lá.");
        sb.AppendLine("  - Salve o 'desfazer-1440p.reg' ANTES de aplicar.");
        if (ctx.ExistingOverride != null)
            sb.AppendLine("  - Já existe um override neste monitor: salve também o backup da aba 'Override atual'.");
        sb.AppendLine();
        sb.AppendLine("Passo a passo");
        sb.AppendLine("  1. Salve o texto da aba 'Aplicar' como aplicar-1440p.reg e o da aba 'Desfazer' como");
        sb.AppendLine("     desfazer-1440p.reg (botões 'Salvar como…', ou copie e cole no Bloco de Notas).");
        sb.AppendLine("  2. Importe o aplicar-1440p.reg como administrador: duplo clique e confirme o UAC,");
        sb.AppendLine("     ou num prompt elevado: reg import \"<pasta>\\aplicar-1440p.reg\"");
        sb.AppendLine("  3. Reinicie o computador. Alternativa: Gerenciador de Dispositivos > Monitores >");
        sb.AppendLine($"     {ctx.FriendlyName} > Desativar dispositivo > Ativar dispositivo.");
        sb.AppendLine($"  4. Configurações > Sistema > Tela > selecione o monitor > Resolução da tela > {res}.");
        sb.AppendLine("     Se aparecer imagem, clique em 'Manter alterações'. Ajuste a escala (ex.: 125%) a gosto.");
        sb.AppendLine("  5. Confira a taxa em Configurações > Tela > Tela avançada.");
        sb.AppendLine();
        sb.AppendLine("Se a tela apagar ou mostrar 'Out of range' / 'Fora de faixa'");
        sb.AppendLine("  - NÃO clique em nada: em 15 segundos o Windows volta sozinho para a resolução anterior.");
        sb.AppendLine("  - Se não voltar: Win+Ctrl+Shift+B reinicia o driver de vídeo; ou use outro monitor,");
        sb.AppendLine("    a tela do notebook (Win+P) ou acesso remoto para importar o desfazer-1440p.reg.");
        sb.AppendLine("  - Último recurso: Modo de Segurança (resolução básica), importar o desfazer e reiniciar.");
        sb.AppendLine();
        sb.AppendLine("Para desfazer");
        sb.AppendLine("  Importe o desfazer-1440p.reg como administrador e reinicie (ou desative/reative o monitor).");
        sb.AppendLine();
        sb.AppendLine("O que esperar");
        sb.AppendLine("  O painel continua 1920x1080 físico. O monitor recebe 2560x1440 e reduz: mais área de");
        sb.AppendLine("  trabalho e texto menor, com leve efeito de supersampling. Não há ganho real de nitidez.");
        sb.AppendLine("  " + Evaluation.ScalerDisclaimer);
        return sb.ToString();
    }
}

/// <summary>Relatório completo em texto (aba Diagnóstico e botão "Copiar relatório").</summary>
public static class ReportBuilder
{
    public static string Build(MonitorContext ctx, Evaluation ev, bool includeHex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"UpRes — diagnóstico gerado em {DateTime.Now:yyyy-MM-dd HH:mm} na máquina {Environment.MachineName}");
        sb.AppendLine();
        sb.AppendLine($"VEREDITO para {ev.Target.HActive}x{ev.Target.DisplayHeight} @ {ev.Target.RefreshHz:0.00} Hz: {ev.VerdictText}");
        sb.AppendLine("  " + ev.Summary);
        sb.AppendLine("  " + Evaluation.ScalerDisclaimer);
        sb.AppendLine();

        sb.AppendLine("IDENTIFICAÇÃO");
        sb.AppendLine($"  Nome (Windows):   {ctx.FriendlyName}");
        sb.AppendLine($"  Dispositivo GDI:  {ctx.GdiDeviceName}");
        sb.AppendLine($"  HardwareID:       {ctx.HardwareId}");
        sb.AppendLine($"  InstanceID:       {ctx.InstanceId}");
        sb.AppendLine($"  Caminho:          {ctx.MonitorDevicePath}");
        sb.AppendLine($"  Chave registro:   HKLM\\{ctx.RegistrySubPath} {(ctx.RegistryKeyFound ? "(encontrada)" : "(NÃO encontrada)")}");
        sb.AppendLine($"  Conexão:          {ctx.Connection} {ctx.ConnectionRaw}");
        sb.AppendLine($"  GPU:              {ctx.GpuName} ({ctx.Gpu})");
        sb.AppendLine($"  Modo atual:       {ctx.CurrentMode?.ToString() ?? "?"}");
        var e = ev.Edid;
        if (e != null)
        {
            sb.AppendLine($"  EDID:             {e.DisplayName}, fabricante {e.ManufacturerId}, produto 0x{e.ProductCode:X4}, serial {e.SerialText ?? e.SerialNumber.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"                    versão {e.Version}, fabricado semana {e.ManufactureWeek}/{e.ManufactureYear}, {e.ScreenWidthCm}x{e.ScreenHeightCm} cm, entrada {e.InputInterface}");
            sb.AppendLine($"                    {e.BlockCount} bloco(s), fonte: {ctx.EdidSource}");
        }
        foreach (var n in ctx.Notes) sb.AppendLine("  Nota: " + n);
        sb.AppendLine();

        sb.AppendLine("DIAGNÓSTICO");
        foreach (var c in ev.Checks)
            sb.AppendLine($"  {c.StatusLabel,-10} {c.Title}: {c.Detail}");
        sb.AppendLine();

        sb.AppendLine("TIMING PROPOSTO");
        sb.AppendLine("  " + (ev.Override?.Timing ?? ev.Target).Describe().Replace("\n", "\n  "));
        sb.AppendLine();

        if (e != null)
        {
            sb.AppendLine("DESCRITORES DO BLOCO BASE");
            foreach (var s in e.BaseDescriptors) sb.AppendLine("  " + s);
            if (e.RangeLimits != null) sb.AppendLine("  Range Limits: " + e.RangeLimits);
            foreach (var c in e.CtaExtensions)
            {
                sb.AppendLine($"  CTA-861 rev {c.Revision} (bloco {c.BlockIndex}): VICs [{string.Join(", ", c.Vics)}], {c.Dtds.Count} DTD(s), " +
                              $"slot livre: {(c.FreeDtdOffset.HasValue ? "offset " + c.FreeDtdOffset : "não")}" +
                              (c.HasHdmiVsdb ? $", HDMI VSDB (Max TMDS {c.HdmiMaxTmdsMHz?.ToString(CultureInfo.InvariantCulture) ?? "n/d"} MHz)" : "") +
                              (c.HasHfVsdb ? $", HF-VSDB (Max TMDS {c.HfMaxTmdsMHz?.ToString(CultureInfo.InvariantCulture) ?? "n/d"} MHz)" : ""));
            }
            foreach (var b in e.DisplayIdBlocks) sb.AppendLine($"  DisplayID no bloco {b}");
            foreach (var o in e.OtherExtensions) sb.AppendLine("  " + o);
            foreach (var w in e.Warnings) sb.AppendLine("  Aviso: " + w);
            sb.AppendLine();

            sb.AppendLine("MODOS ANUNCIADOS PELO EDID");
            foreach (var m in e.Modes.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshHz))
                sb.AppendLine("  " + m);
            sb.AppendLine();
        }

        sb.AppendLine("MODOS OFERECIDOS PELO WINDOWS");
        foreach (var m in ctx.WindowsModes.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshHz))
            sb.AppendLine("  " + m);
        sb.AppendLine();

        if (includeHex)
        {
            if (ctx.Edid != null) { sb.AppendLine("EDID ORIGINAL (hex)"); sb.Append(EdidParser.HexDump(ctx.Edid)); sb.AppendLine(); }
            if (ctx.ExistingOverride != null) { sb.AppendLine("EDID_OVERRIDE EXISTENTE (hex)"); sb.Append(EdidParser.HexDump(ctx.ExistingOverride)); sb.AppendLine(); }
            if (ev.Override?.Success == true) { sb.AppendLine("EDID PROPOSTO (hex)"); sb.Append(EdidParser.HexDump(ev.Override.Edid)); sb.AppendLine(); }
        }
        return sb.ToString();
    }
}
