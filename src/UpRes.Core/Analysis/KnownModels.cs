using System;
using System.Collections.Generic;
using System.Linq;

namespace UpRes.Core.Analysis;

/// <summary>
/// Modelos em que o override de 1440p foi testado e confirmado.
/// Só adicione um modelo depois de um teste real (aplicar, usar 2560x1440, desfazer).
/// </summary>
public static class KnownModels
{
    public sealed class Model
    {
        public Model(string hardwareId, string name, string note)
        { HardwareId = hardwareId; Name = name; Note = note; }

        /// <summary>Fabricante + código do produto em hex, como em Enum\DISPLAY (ex.: GSM5BBF).</summary>
        public string HardwareId { get; }
        public string Name { get; }
        public string Note { get; }
    }

    public static readonly IReadOnlyList<Model> Confirmed = new List<Model>
    {
        // TODO: preencher com o HardwareID real do monitor do Daniel quando confirmado.
        // Exemplo (NÃO confirmado): new Model("GSM5BBF", "LG UltraGear 27GN750", "CVT-RB 2560x1440@60, Intel Iris Xe, ..."),
    };

    public static Model? Find(string hardwareId) =>
        Confirmed.FirstOrDefault(m => m.HardwareId.Equals(hardwareId ?? "", StringComparison.OrdinalIgnoreCase));
}
