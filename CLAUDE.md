# UpRes: contexto para o Claude

> Fazer 2560×1440 aparecer no Windows para um monitor 1080p, via **EDID override** gerado por um app **somente leitura**.
> Comunicação com o Daniel: português (pt-BR). Ele é desenvolvedor .NET/C# experiente e compila o `.exe` por conta própria.
> Atualize a seção "Estado atual" ao fim de cada etapa.

## Objetivo

Um `.exe` Windows que:
1. Lista os monitores **ativos** e lê o EDID de cada um.
2. Avalia de forma **heurística** se o monitor 1080p aceita sinal 2560×1440. O veredito é *Provável*, *Incerto* ou *Improvável*, nunca "garantido".
3. Mostra o texto do `aplicar-1440p.reg` e do `desfazer-1440p.reg` com o caminho **desta máquina**, para o usuário aplicar **manualmente**. Também mostra uma versão em PowerShell como texto e as instruções. O usuário pode copiar ou usar "Salvar como…".

### Restrição principal: o app nunca aplica nada
Ele não grava no registro, não chama `cmd`/`powershell`, não pede elevação (`asInvoker`) e não instala nada.
Motivo: o **SentinelOne** da máquina corporativa colocou em quarentena e apagou um `.exe` e scripts sem assinatura que, iniciados via `cmd`, gravavam em `HKLM\...\Enum\DISPLAY`. **Mesmo um `.exe` somente leitura e sem assinatura pode ser marcado.** Se isso acontecer, peça liberação ao TI. Nunca tente contornar o antivírus.

## Decisões tomadas (13/10/2026)
- **Runtime: .NET Framework 4.8**, que já vem no Windows 10/11. O .NET 5 foi descartado porque está sem suporte e exigiria instalar runtime na máquina do colega.
- **UI: WinForms**, montada em código (sem designer).
- **Salvar `.reg`:** permitido via "Salvar como…", em UTF-16 LE com BOM, como o regedit exporta.

## Estrutura
```
src/UpRes.Core     netstandard2.0, sem Windows: parser EDID, CVT-RB, override, avaliador, geradores de texto
src/UpRes.App      net48 WinForms: QueryDisplayConfig/EnumDisplaySettings (P/Invoke), registro/WMI só leitura, UI
tests/UpRes.Core.Tests  xUnit (net10.0): fixtures sintéticas em EdidFactory; EDIDs reais irão para Fixtures/
```
Build: `dotnet build UpRes.sln` · testes: `dotnet test` · publicar: `dotnet build src/UpRes.App -c Release` (saída em `src/UpRes.App/bin/Release/net48/`).

## Fundamentos técnicos
- O painel continua 1920×1080. Quem aceita (ou não) o 1440p é o **scaler**, que reduz a imagem. O ganho é área de trabalho, sem nitidez real.
- **Timing padrão: CVT-RB 2560×1440 @ 60 Hz**: 241,50 MHz; H 2560/48/32/80 (total 2720); V 1440/3/5/33 (total 1481); 88,79 kHz; 59,95 Hz; H+ V−. O usuário pode escolher outra taxa (24–75 Hz) no app. **[confirmar]** comparando com o `.reg` que funciona hoje no Daniel.
- **Override:** fica em `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\<HWID>\<InstanceID>\Device Parameters\EDID_OVERRIDE`, com um REG_BINARY de 128 bytes por bloco, nomeados `"0"`, `"1"`… **[confirmar]** com o `.reg` exportado.
- O **InstanceID muda por máquina, GPU e porta**. Essa é a causa mais provável de o script não ter funcionado no colega. O app obtém o InstanceID do monitor **ativo** via `QueryDisplayConfig` → `DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath` e usa a grafia exata da subchave do registro.
- **Como o EDID é montado** (`EdidOverrideBuilder`): parte de uma cópia fiel do original. O timing preferido (DTD #1) **não** é alterado. O novo DTD entra num destes lugares, nesta ordem:
  1. descritor dummy (0x10) do bloco base;
  2. slot livre de DTD na extensão CTA-861;
  3. uma extensão CTA-861 nova, se o EDID só tiver o bloco base.

  O Range Limits (0xFD) é ajustado quando barra o modo: pixel clock máximo, H/V máximos e o limite de pixels por linha do CVT. Por fim, o checksum de cada bloco alterado é recalculado.
- **Heurísticas** (`Evaluator`):
  - conexão (painel interno = improvável);
  - GPU;
  - modelo confirmado (`KnownModels`);
  - se o Windows já oferece 1440p;
  - se o EDID já anuncia 1440p, ou alguma resolução acima da nativa;
  - maior pixel clock e maior frequência H anunciados, comparados com o necessário;
  - Range Limits;
  - HDMI Max TMDS (VSDB/HF-VSDB), ou o limite de 165 MHz quando o monitor está em HDMI sem VSDB;
  - se dá para montar o override.

  Quando a taxa pedida não cabe, o app sugere uma taxa menor.
- **Fonte do EDID:** o valor `EDID` em `Device Parameters`. Se ele faltar, o app usa o WMI `WmiGetMonitorRawEEdidV1Block`. Se já existir um `EDID_OVERRIDE`, o app mostra um **backup em `.reg`** dele (aba "Override atual").
- Referência conceitual: o CRU (ToastyX). **Não** sugira o CRU nesta máquina, porque é um `.exe` de terceiros e provavelmente também seria bloqueado.

## Regras de trabalho para o Claude
- Nunca execute nada que grave em `HKLM` nem que chame `cmd`/`powershell` para aplicar mudanças, nem para testar.
- Nunca tente contornar, desativar ou enganar o SentinelOne.
- Só adicione um modelo em `KnownModels.Confirmed` depois de um teste real.

## Estado atual
- [x] Override 1440p funcionando na máquina do Daniel (aplicado manualmente antes do bloqueio).
- [x] Solução criada: Core + App WinForms net48 + testes (29 passando, com EDIDs sintéticos).
- [ ] Obter do Daniel o **relatório do app** (botão "Copiar relatório", que inclui os EDIDs em hex) ou o `.reg` exportado do override atual. O app mostra esse backup na aba "Override atual".
- [ ] Confirmar o modelo do monitor (suspeita: LG UltraGear 27GN750) e o HardwareID real; adicionar em `KnownModels`.
- [ ] Comparar o timing e a estrutura do override que funciona com o que o app gera, e ajustar se divergirem. Criar um teste com esse EDID real como fixture (o `.reg` gerado deve reproduzir o que funcionou).
- [ ] Daniel compila e roda na máquina dele (verificar se o SentinelOne tolera o `.exe`).
- [ ] Validar na máquina do colega: gerar lá e aplicar manualmente o `.reg` com o InstanceID dele.
