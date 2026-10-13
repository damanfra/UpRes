using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UpRes.Core.Analysis;
using UpRes.Core.Edid;

namespace UpRes.Core.Output;

/// <summary>
/// Gera texto de arquivos .reg (formato do regedit, "Windows Registry Editor Version 5.00").
/// Este código só produz texto; nada aqui grava no registro.
/// </summary>
public static class RegFileGenerator
{
    public const string Header = "Windows Registry Editor Version 5.00";
    private const string NL = "\r\n";
    private const int MaxLineLength = 80;

    public static string Apply(MonitorContext ctx, OverrideResult ov, string? comment = null)
    {
        if (ov == null || !ov.Success) throw new InvalidOperationException("Override inválido.");
        var sb = new StringBuilder();
        sb.Append(Header).Append(NL).Append(NL);
        AppendComments(sb, comment ?? DefaultApplyComment(ctx, ov));
        // Remove qualquer override anterior (blocos antigos "2", "3"... não podem sobrar) e grava o novo.
        sb.Append('[').Append('-').Append(ctx.OverrideKeyPath).Append(']').Append(NL).Append(NL);
        AppendKeyWithBlocks(sb, ctx.OverrideKeyPath, ov.Blocks);
        return sb.ToString();
    }

    public static string Undo(MonitorContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append(NL).Append(NL);
        AppendComments(sb,
            $"Desfaz o EDID override de {Name(ctx)}.\n" +
            $"Máquina: {Environment.MachineName}  |  HardwareID: {ctx.HardwareId}  |  InstanceID: {ctx.InstanceId}\n" +
            "Depois de importar: reinicie, ou desative e reative o monitor no Gerenciador de Dispositivos.");
        sb.Append("[-").Append(ctx.OverrideKeyPath).Append(']').Append(NL);
        return sb.ToString();
    }

    /// <summary>Backup do override que já existe na máquina (para restaurar depois, se preciso).</summary>
    public static string Backup(MonitorContext ctx, byte[] existingOverride) =>
        Backup(ctx.OverrideKeyPath, existingOverride, Name(ctx));

    public static string Backup(FoundOverride found) =>
        Backup(found.KeyPath, found.Data, $@"{found.HardwareId}\{found.InstanceId}" + (found.IsActiveInstance ? "" : " (instância INATIVA: este override não afeta nenhum monitor conectado agora)"));

    private static string Backup(string keyPath, byte[] data, string label)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append(NL).Append(NL);
        AppendComments(sb,
            $"Backup do EDID_OVERRIDE existente de {label}, lido em {DateTime.Now:yyyy-MM-dd HH:mm}.\n" +
            $"Máquina: {Environment.MachineName}. Importar este arquivo restaura exatamente este override.");
        sb.Append("[-").Append(keyPath).Append(']').Append(NL).Append(NL);
        AppendKeyWithBlocks(sb, keyPath, Split(data));
        return sb.ToString();
    }

    /// <summary>Uma linha "nome"=hex:..., quebrada como o regedit faz (vírgula + "\" e continuação com 2 espaços).</summary>
    public static string FormatBinaryValue(string name, byte[] data)
    {
        var sb = new StringBuilder();
        string prefix = "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"=hex:";
        sb.Append(prefix);
        int lineLen = prefix.Length;
        for (int i = 0; i < data.Length; i++)
        {
            string hex = data[i].ToString("x2", CultureInfo.InvariantCulture);
            bool last = i == data.Length - 1;
            sb.Append(hex);
            lineLen += 2;
            if (!last)
            {
                sb.Append(',');
                lineLen++;
                if (lineLen + 3 > MaxLineLength - 2)
                {
                    sb.Append('\\').Append(NL).Append("  ");
                    lineLen = 2;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Lê de volta os valores binários de um .reg (usado nos testes e para comparar com o .reg de referência).</summary>
    public static Dictionary<string, byte[]> ParseBinaryValues(string regText)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var joined = regText.Replace("\\\r\n", "").Replace("\\\n", "");
        foreach (var raw in joined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = raw.Trim();
            int eq = line.IndexOf("\"=hex:", StringComparison.OrdinalIgnoreCase);
            if (!line.StartsWith("\"", StringComparison.Ordinal) || eq < 0) continue;
            string name = line.Substring(1, eq - 1);
            string hex = line.Substring(eq + 6).Replace(" ", "");
            var bytes = hex.Length == 0 ? new byte[0]
                : hex.Split(',').Where(h => h.Length > 0).Select(h => byte.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            result[name] = bytes;
        }
        return result;
    }

    private static void AppendKeyWithBlocks(StringBuilder sb, string keyPath, IList<byte[]> blocks)
    {
        sb.Append('[').Append(keyPath).Append(']').Append(NL);
        for (int i = 0; i < blocks.Count; i++)
            sb.Append(FormatBinaryValue(i.ToString(CultureInfo.InvariantCulture), blocks[i])).Append(NL);
    }

    private static void AppendComments(StringBuilder sb, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            sb.Append("; ").Append(line).Append(NL);
        sb.Append(NL);
    }

    private static string DefaultApplyComment(MonitorContext ctx, OverrideResult ov) =>
        $"Adiciona {string.Join(" e ", ov.Timings)} ao EDID de {Name(ctx)} via EDID override.\n" +
        $"Máquina: {Environment.MachineName}  |  HardwareID: {ctx.HardwareId}  |  InstanceID: {ctx.InstanceId}\n" +
        $"Gerado por {BuildInfo.Display}, somente leitura.\n" +
        "Válido APENAS para este monitor, nesta máquina, nesta porta.\n" +
        "O timing nativo continua como preferido; escolha a nova resolução em Configurações > Tela.\n" +
        "Alterações:\n" + string.Join("\n", ov.Changes.Select(c => "  - " + c));

    private static string Name(MonitorContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.FriendlyName) ? ctx.HardwareId : $"{ctx.FriendlyName} ({ctx.HardwareId})";

    public static List<byte[]> Split(byte[] data)
    {
        var blocks = new List<byte[]>();
        for (int o = 0; o < data.Length; o += EdidParser.BlockLength)
        {
            var b = new byte[Math.Min(EdidParser.BlockLength, data.Length - o)];
            Array.Copy(data, o, b, 0, b.Length);
            blocks.Add(b);
        }
        return blocks;
    }
}
