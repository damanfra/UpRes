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
        public Model(string hardwareId, string name, string note, bool? factoryEdidHas1440p = null)
        { HardwareId = hardwareId; Name = name; Note = note; FactoryEdidHas1440p = factoryEdidHas1440p; }

        /// <summary>Se o EDID DE FÁBRICA já traz 2560x1440 (null = desconhecido).</summary>
        public bool? FactoryEdidHas1440p { get; }

        /// <summary>Fabricante + código do produto em hex, como em Enum\DISPLAY (ex.: GSM5BBF).</summary>
        public string HardwareId { get; }
        public string Name { get; }
        public string Note { get; }
    }

    public static readonly IReadOnlyList<Model> Confirmed = new List<Model>
    {
        new Model("MUL2380", "Multilaser MN801",
            "Confirmado em 13/10/2026 na máquina do Daniel: 2560x1440 @ 59,95 Hz em uso via HDMI, Intel Iris Xe. " +
            "Timing CVT-RB 241,50 MHz (2720x1481) + plano B 50 Hz (201,42 MHz, mesmos totais). " +
            "EDID de fábrica: sem 1440p; Range Limits de fábrica 30–90 kHz / 300 MHz já comportam o modo.",
            factoryEdidHas1440p: false),
    };

    public static Model? Find(string hardwareId) =>
        Confirmed.FirstOrDefault(m => m.HardwareId.Equals(hardwareId ?? "", StringComparison.OrdinalIgnoreCase));
}
