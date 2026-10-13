using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using UpRes.Core.Analysis;
using UpRes.Core.Edid;
using UpRes.Core.Output;

namespace UpRes.App;

/// <summary>
/// Janela principal. Só lê, calcula e exibe texto. Ações possíveis do usuário: copiar para a
/// área de transferência e "Salvar como…" (arquivo de texto onde ele escolher). Nunca aplica nada.
/// </summary>
internal sealed class MainForm : Form
{
    private const int TargetWidth = 2560;
    private const int TargetHeight = 1440;

    private readonly ComboBox _monitors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    private readonly NumericUpDown _refresh = new NumericUpDown { Minimum = 24, Maximum = 120, Value = 60, Width = 60 };
    private readonly CheckBox _planB = new CheckBox { Text = "Incluir plano B (60 Hz, ou 50 Hz se a taxa for 60)", AutoSize = true, Checked = true };
    private readonly CheckBox _force = new CheckBox { Text = "Gerar mesmo se improvável", AutoSize = true };
    private readonly Label _timing = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Label _verdict = new Label { AutoSize = true, Font = new Font("Segoe UI", 16F, FontStyle.Bold) };
    private readonly Label _summary = new Label { AutoSize = true, MaximumSize = new Size(1100, 0) };
    private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };

    private readonly TextBox _diag = NewTextBox();
    private readonly TextBox _apply = NewTextBox();
    private readonly TextBox _undo = NewTextBox();
    private readonly TextBox _ps = NewTextBox();
    private readonly TextBox _steps = NewTextBox();
    private readonly TextBox _backup = NewTextBox();
    private readonly TextBox _hex = NewTextBox();

    private List<MonitorContext> _contexts = new List<MonitorContext>();
    private MonitorContext? _ctx;
    private Evaluation? _eval;

    public MainForm()
    {
        Text = $"UpRes — verificador de 2560x1440 (somente leitura) — {UpRes.Core.BuildInfo.Version}";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1200, 820);
        MinimumSize = new Size(900, 600);

        var refreshButton = new Button { Text = "Atualizar", AutoSize = true };
        refreshButton.Click += (s, e) => LoadMonitors();
        var reportButton = new Button { Text = "Copiar relatório", AutoSize = true };
        reportButton.Click += (s, e) => CopyReport();

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(8, 8, 8, 0) };
        top.Controls.Add(new Label { Text = "Monitor ativo:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_monitors);
        top.Controls.Add(refreshButton);
        top.Controls.Add(new Label { Text = "   Taxa desejada (Hz):", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_refresh);
        top.Controls.Add(_timing);
        top.Controls.Add(_planB);
        top.Controls.Add(_force);
        top.Controls.Add(reportButton);

        var verdictPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 4, 8, 4) };
        verdictPanel.Controls.Add(_verdict);
        verdictPanel.Controls.Add(_summary);

        _tabs.TabPages.Add(TextTab("Diagnóstico", _diag, null));
        _tabs.TabPages.Add(TextTab("Aplicar (.reg)", _apply, () => $"aplicar-1440p-{_ctx?.HardwareId}.reg"));
        _tabs.TabPages.Add(TextTab("Desfazer (.reg)", _undo, () => $"desfazer-1440p-{_ctx?.HardwareId}.reg"));
        _tabs.TabPages.Add(TextTab("PowerShell (texto)", _ps, null));
        _tabs.TabPages.Add(TextTab("Instruções", _steps, null));
        _tabs.TabPages.Add(TextTab("Override atual", _backup, () => $"backup-override-{_ctx?.HardwareId}.reg"));
        _tabs.TabPages.Add(TextTab("EDID (hex)", _hex, null));

        var footer = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 8),
            ForeColor = Color.DimGray,
            Text = "Este programa só LÊ: não grava no registro, não executa comandos e não pede elevação. " +
                   Evaluation.ScalerDisclaimer + " Em máquina corporativa, combine com o TI antes de aplicar.",
            MaximumSize = new Size(1150, 0),
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(verdictPanel, 0, 1);
        root.Controls.Add(_tabs, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        _monitors.SelectedIndexChanged += (s, e) => Evaluate();
        _refresh.ValueChanged += (s, e) => Evaluate();
        _force.CheckedChanged += (s, e) => Evaluate();
        _planB.CheckedChanged += (s, e) => Evaluate();
        Shown += (s, e) => LoadMonitors();
    }

    private void LoadMonitors()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _contexts = MonitorDiscovery.GetActiveMonitors();
        }
        catch (Exception ex)
        {
            _contexts = new List<MonitorContext>();
            MessageBox.Show(this, "Falha ao enumerar monitores:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _monitors.Items.Clear();
        foreach (var c in _contexts)
            _monitors.Items.Add($"{c.FriendlyName}  —  {c.HardwareId}  —  {c.ConnectionRaw}  —  {c.GdiDeviceName}  {c.CurrentMode}");

        if (_contexts.Count == 0)
        {
            _ctx = null;
            ShowNothing("Nenhum monitor ativo encontrado.");
            return;
        }

        // Prefere um monitor externo.
        int idx = _contexts.FindIndex(c => c.Connection != ConnectionKind.Internal);
        _monitors.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void Evaluate()
    {
        if (_monitors.SelectedIndex < 0 || _monitors.SelectedIndex >= _contexts.Count) return;
        _ctx = _contexts[_monitors.SelectedIndex];

        var target = CvtReducedBlanking.Compute(TargetWidth, TargetHeight, (double)_refresh.Value);
        _timing.Text = $"CVT-RB: {target}";

        try
        {
            var planB = _planB.Checked ? EdidOverrideBuilder.PlanB(target) : null;
            var extras = planB != null ? new[] { planB } : null;
            _eval = Evaluator.Evaluate(_ctx, target, extras);
        }
        catch (Exception ex)
        {
            ShowNothing("Erro na avaliação: " + ex.Message);
            return;
        }

        _verdict.Text = "Veredito: " + _eval.VerdictText;
        _verdict.ForeColor = _eval.Verdict switch
        {
            Verdict.Provavel => Color.ForestGreen,
            Verdict.Incerto => Color.DarkGoldenrod,
            Verdict.Improvavel => Color.Firebrick,
            _ => Color.SteelBlue,
        };
        _summary.Text = _eval.Summary + "  " + Evaluation.ScalerDisclaimer;

        _diag.Text = Crlf(ReportBuilder.Build(_ctx, _eval, includeHex: false));
        _hex.Text = Crlf(HexText());

        bool canGenerate = _eval.Override?.Success == true && (_eval.RecommendGenerate || _force.Checked);
        if (canGenerate)
        {
            _apply.Text = RegFileGenerator.Apply(_ctx, _eval.Override!);
            _ps.Text = Crlf(PowerShellGenerator.Apply(_ctx, _eval.Override!) + "\n\n# ---- Desfazer ----\n" + PowerShellGenerator.Undo(_ctx));
            _steps.Text = Crlf(InstructionsGenerator.Build(_ctx, _eval));
        }
        else
        {
            string why = _eval.Override == null ? "Sem override para este caso: " + _eval.Summary
                : !_eval.Override.Success ? "Não foi possível montar o override: " + _eval.Override.Error
                : $"Veredito {_eval.VerdictText}. Marque 'Gerar mesmo se improvável' para ver os arquivos." +
                  (_eval.SuggestedRefreshHz.HasValue ? $" Ou tente {_eval.SuggestedRefreshHz} Hz no campo 'Taxa desejada'." : "");
            _apply.Text = "; " + why;
            _ps.Text = "# " + why;
            _steps.Text = why;
        }

        // O desfazer vale sempre que houver override (novo ou já existente).
        _undo.Text = _ctx.HardwareId.Length > 0 ? RegFileGenerator.Undo(_ctx) : "; Monitor sem HardwareID/InstanceID.";

        // Um único .reg: cabeçalho uma vez, depois cada override encontrado.
        var backups = new List<string>();
        if (_ctx.ExistingOverride != null) backups.Add(RegFileGenerator.Backup(_ctx, _ctx.ExistingOverride));
        backups.AddRange(_ctx.OtherOverrides.Where(o => !o.IsActiveInstance).Select(RegFileGenerator.Backup));
        _backup.Text = backups.Count == 0
            ? "; Não existe EDID_OVERRIDE nesta máquina (nem para este monitor, nem em outras instâncias)."
            : Crlf(RegFileGenerator.Header + "\n\n" + string.Join("\n", backups.Select(b => b.Substring(RegFileGenerator.Header.Length).TrimStart('\r', '\n'))));
    }

    private string HexText()
    {
        var sb = new StringBuilder();
        if (_ctx?.Edid != null) { sb.AppendLine($"EDID ORIGINAL ({_ctx.EdidSource})"); sb.AppendLine(EdidParser.HexDump(_ctx.Edid)); }
        if (_ctx?.ExistingOverride != null) { sb.AppendLine("EDID_OVERRIDE EXISTENTE"); sb.AppendLine(EdidParser.HexDump(_ctx.ExistingOverride)); }
        if (_eval?.Override?.Success == true) { sb.AppendLine("EDID PROPOSTO"); sb.AppendLine(EdidParser.HexDump(_eval.Override.Edid)); }
        return sb.Length == 0 ? "Sem EDID." : sb.ToString();
    }

    private void ShowNothing(string message)
    {
        _verdict.Text = "—";
        _verdict.ForeColor = SystemColors.ControlText;
        _summary.Text = message;
        foreach (var tb in new[] { _diag, _apply, _undo, _ps, _steps, _backup, _hex }) tb.Text = message;
    }

    private void CopyReport()
    {
        if (_ctx == null || _eval == null) return;
        CopyText(Crlf(ReportBuilder.Build(_ctx, _eval, includeHex: true)));
    }

    private TabPage TextTab(string title, TextBox box, Func<string>? saveName)
    {
        var page = new TabPage(title);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };

        var copy = new Button { Text = "Copiar", AutoSize = true };
        copy.Click += (s, e) => CopyText(box.Text);
        buttons.Controls.Add(copy);

        if (saveName != null)
        {
            var save = new Button { Text = "Salvar como…", AutoSize = true };
            save.Click += (s, e) => SaveText(box.Text, saveName());
            buttons.Controls.Add(save);
        }

        page.Controls.Add(box);
        page.Controls.Add(buttons);
        return page;
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Grava apenas o arquivo de texto escolhido pelo usuário (UTF-16 LE com BOM, como o regedit exporta).</summary>
    private void SaveText(string text, string defaultName)
    {
        if (string.IsNullOrEmpty(text) || text.StartsWith("; Não", StringComparison.Ordinal) || !text.StartsWith(RegFileGenerator.Header, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Não há arquivo .reg para salvar nesta aba.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Filter = "Arquivo de registro (*.reg)|*.reg",
            FileName = defaultName,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, text, Encoding.Unicode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao salvar: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static TextBox NewTextBox() => new TextBox
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.75F),
        BackColor = SystemColors.Window,
    };

    private static string Crlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
}
