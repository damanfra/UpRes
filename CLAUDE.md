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
- **Timing padrão: CVT-RB 2560×1440 @ 60 Hz**: 241,50 MHz; H 2560/48/32/80 (total 2720); V 1440/3/5/33 (total 1481); 88,79 kHz; 59,95 Hz; H+ V−. Vem junto um **plano B de 50 Hz**, com os mesmos totais e 201,42 MHz (`EdidOverrideBuilder.AtRefresh`). **Confirmado:** são exatamente os DTDs que estão no EDID que funciona no MN801. O gerador reproduz esse EDID byte a byte (teste `Generator_reproduces_the_working_edid_byte_for_byte`).
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

## Descobertas (relatório real, 13/10/2026)
- O monitor do Daniel é um **Multilaser MN801** (`MUL2380`): 24", 1080p, até 75 Hz, ligado por **HDMI** numa Intel Iris Xe. Não é o LG 27GN750. Ele **roda hoje em 2560×1440 @ 59 Hz**.
- Na instância ativa (`4&12ecdce7&0&UID4145`) **não existe `EDID_OVERRIDE`**. Mesmo assim, o valor `Device Parameters\EDID` **já contém** os dois DTDs de 1440p, e o Range Limits declara H 30–90 kHz e 300 MHz. Há duas hipóteses:
  - (a) o EDID de fábrica já traz 1440p;
  - (b) o script antigo gravou direto no valor `EDID`, ou o override está noutro lugar.

  Para separar as duas: o app agora varre **todos** os `EDID_OVERRIDE` de `Enum\DISPLAY`, compara o EDID do registro com o do WMI, e falta o relatório de uma máquina onde nada foi aplicado (a do colega).
- **Correção do Daniel:** o MN801 só passou a rodar 1440p **hoje**, depois do script da sessão anterior. Logo o EDID de fábrica provavelmente não traz 1440p. Hipótese mais forte: a remediação do SentinelOne desfez as alterações de registro do processo bloqueado e apagou o `EDID_OVERRIDE`. O valor `EDID` ainda guarda o conteúdo modificado (cache da última detecção do monitor), então o modo **pode sumir no próximo reinício ou reconexão**. Por isso, quando não há override, o app oferece gravar o EDID atual como `EDID_OVERRIDE` (`Evaluation.PersistCurrent`).
- Calibração: o maior modo anunciado (185,59 MHz / 84,36 kHz) fica abaixo do necessário, mas o 1440p funciona. Por isso o `Evaluator` passou a considerar o **Range Limits declarado** como capacidade da entrada.
- O EDID real está em `tests/UpRes.Core.Tests/Fixtures/MUL2380_MN801_registro.hex`.

## Regras de trabalho para o Claude
- Nunca execute nada que grave em `HKLM` nem que chame `cmd`/`powershell` para aplicar mudanças, nem para testar.
- Nunca tente contornar, desativar ou enganar o SentinelOne.
- Só adicione um modelo em `KnownModels.Confirmed` depois de um teste real.

## Estado atual
- [x] Override 1440p funcionando na máquina do Daniel (aplicado manualmente antes do bloqueio).
- [x] Solução criada: Core + App WinForms net48 + testes (34 passando, incluindo o EDID real).
- [x] Daniel compilou e rodou o app na máquina dele. O SentinelOne não bloqueou. O relatório foi recebido.
- [x] Monitor identificado: Multilaser MN801 (`MUL2380`), adicionado em `KnownModels`. O timing foi confirmado e o gerador reproduz o EDID que funciona.
- [x] Override com 60 Hz + plano B de 50 Hz. Varredura de overrides em todas as instâncias. Comparação registro × WMI.
- [x] Opção de tornar permanente o EDID atual quando falta o override (aba "Aplicar").
- [ ] Daniel salva o "Aplicar" e o "Desfazer" **antes de reiniciar** e importa manualmente, se o TI concordar.
- [ ] Daniel roda a versão nova: ver se aparece algum `EDID_OVERRIDE` em outra instância e se o EDID do WMI difere do registro.
- [ ] Validar na máquina do colega: gerar lá e aplicar manualmente o `.reg` com o InstanceID dele.
