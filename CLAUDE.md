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
**Versão:** `Directory.Build.targets` grava `<Version>.<BuildNumber>+<git describe --dirty>` (ex.: `0.3.0.0+b160276` em build local, `0.3.0.12+b160276` no Release nº 12 da Action) na InformationalVersion. Ela aparece no cabeçalho do relatório ("Versão: …"), no título da janela e nos comentários dos `.reg`/PowerShell. **Sempre confira a versão de um relatório recebido antes de interpretá-lo.** `-dirty` = compilado com alterações locais; `sem-git` = compilado fora de um clone git.

**Releases:** `.github/workflows/release.yml`. Cada push na `main` roda os testes, compila em `windows-latest` com `-p:BuildNumber=<run_number>` e publica o Release `v<Version>.<run_number>` com o zip (`UpRes.exe`, `.config`, `UpRes.Core.dll`, README). Em PRs, só testa e compila. Ao mudar funcionalidade, suba o `<Version>` em `Directory.Build.props`.
**Checar atualizações:** botão no app (`UpdateCheck` no Core, `UpdateClient` no App). Só roda quando o usuário clica e consulta `api.github.com/repos/damanfra/UpRes/releases/latest`, sem autenticação (o repo é público). Compara commit e versão. **Nunca baixa nem instala**; no máximo abre a página do Release no navegador, se o usuário escolher.
**Autor dos commits:** Daniel Manfrini <daniel.manfrini@gmail.com> (configurado no repo local).

Build: `dotnet build UpRes.sln` · testes: `dotnet test` · publicar: `dotnet build src/UpRes.App -c Release` (saída em `src/UpRes.App/bin/Release/net48/`).

## Fundamentos técnicos
- O painel continua 1920×1080. Quem aceita (ou não) o 1440p é o **scaler**, que reduz a imagem. O ganho é área de trabalho, sem nitidez real.
- **Timing padrão: CVT-RB 2560×1440 @ 60 Hz**: 241,50 MHz; H 2560/48/32/80 (total 2720); V 1440/3/5/33 (total 1481); 88,79 kHz; 59,95 Hz; H+ V−. Vem junto um **plano B de 50 Hz**, com os mesmos totais e 201,42 MHz (`EdidOverrideBuilder.AtRefresh`). Taxa escolhível de 24 a 120 Hz; acima de 76 Hz o diagnóstico mostra "Taxa alta". Plano B (`EdidOverrideBuilder.PlanB`): alvo > 60 Hz → também grava o CVT-RB 60 Hz; alvo de 60 Hz → o mesmo timing a 50 Hz. **Confirmado:** são exatamente os DTDs que estão no EDID que funciona no MN801. O gerador reproduz esse EDID byte a byte (teste `Generator_reproduces_the_working_edid_byte_for_byte`).
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
- Na instância ativa (`4&12ecdce7&0&UID4145`) **não existe `EDID_OVERRIDE`** (nem em nenhuma outra instância), mas o valor `Device Parameters\EDID` (igual ao WMI) **contém** os dois DTDs de 1440p.
- **Arquivos recuperados** (`Aplicar-1440p.ps1/.reg`, `Desfazer-*`, `LEIA-ME.txt`): são o **pacote preparado para o colega**, derivado da máquina do Daniel que já funcionava. Não são o que aplicou o override no Daniel (esse foi o `apply-edid.ps1`, perdido). O `.reg` está em `Fixtures/MUL2380_Aplicar-1440p_original.reg`. Conclusões:
  - O `.ps1` do pacote só grava os DTDs 60 Hz e 50 Hz nos offsets `0xC3`/`0xD5` do bloco CTA e recalculou o checksum do bloco 1. **O bloco base, incluindo o Range Limits de fábrica (30–90 kHz / 300 MHz), não foi alterado.** Ele só aceita EDIDs com essa área zerada. Logo o EDID de fábrica é conhecido, por evidência indireta mas consistente: é o atual sem esses dois DTDs (`RealEdidTests.Factory()`). Ele **não** traz 1440p.
  - O `.reg` original tem exatamente os blocos que o UpRes gera a partir do EDID de fábrica (teste `Generated_reg_has_the_same_blocks_as_the_original_working_reg`).
  - Depois de gravar, o script do pacote desativava e reativava o adaptador de vídeo, e foi isso que fez o Windows reler o EDID. A chave sumiu depois (provável remediação do SentinelOne); o EDID modificado segue em cache até a próxima redetecção.
  - **Colega:** o `.ps1` percorria **todas** as instâncias de `MUL2380`, então o InstanceID não explica a falha **se** ele rodou o `.ps1`. Já o `.reg` antigo tem o InstanceID do Daniel fixo, e esse sim falharia. Outras causas possíveis no `.ps1`:
    - offsets fixos (EDID com revisão diferente → "Ignorado…");
    - falta de elevação ou ExecutionPolicy;
    - bloqueio silencioso do SentinelOne.

    Precisa do relatório do app na máquina dele.
- **Correção do Daniel:** o MN801 só passou a rodar 1440p **hoje**, depois do script da sessão anterior. Confirmado pelos arquivos originais: o EDID de fábrica não traz 1440p. Hipótese mais forte para o sumiço da chave: a remediação do SentinelOne desfez as alterações de registro do processo bloqueado e apagou o `EDID_OVERRIDE`. O valor `EDID` ainda guarda o conteúdo modificado (cache da última detecção do monitor), então o modo **pode sumir no próximo reinício ou reconexão**. Por isso, quando não há override, o app oferece gravar o EDID atual como `EDID_OVERRIDE` (`Evaluation.PersistCurrent`). Para modelos com `FactoryEdidHas1440p == false` (o MN801), o app afirma que o modo vai sumir.
- Calibração: o maior modo anunciado (185,59 MHz / 84,36 kHz) fica abaixo do necessário, mas o 1440p funciona. Por isso o `Evaluator` passou a considerar o **Range Limits declarado** como capacidade da entrada.
- O EDID real está em `tests/UpRes.Core.Tests/Fixtures/MUL2380_MN801_registro.hex`.

## Regras de trabalho para o Claude
- Nunca execute nada que grave em `HKLM` nem que chame `cmd`/`powershell` para aplicar mudanças, nem para testar.
- Nunca tente contornar, desativar ou enganar o SentinelOne.
- Só adicione um modelo em `KnownModels.Confirmed` depois de um teste real.

## Estado atual
- [x] Override 1440p funcionando na máquina do Daniel (aplicado manualmente antes do bloqueio).
- [x] Solução criada: Core + App WinForms net48 + testes (53 passando, incluindo o EDID real).
- [x] Daniel compilou e rodou o app na máquina dele. O SentinelOne não bloqueou. O relatório foi recebido.
- [x] Monitor identificado: Multilaser MN801 (`MUL2380`), adicionado em `KnownModels`. O timing foi confirmado e o gerador reproduz o EDID que funciona.
- [x] Override com 60 Hz + plano B de 50 Hz. Varredura de overrides em todas as instâncias. Comparação registro × WMI.
- [x] Opção de tornar permanente o EDID atual quando falta o override (aba "Aplicar").
- [x] Daniel importou o "Aplicar" (build `863058b`, 15:26): `EDID_OVERRIDE` presente na instância ativa, idêntico ao EDID que funciona.
- [x] Diagnóstico pós-aplicação limpo: sem FALHA de override, sem evidência circular em "Acima da nativa", "Fonte do EDID" como INFO.
- [x] **Reinício confirmado** (build `b7cc87f`, 15:33): 2560×1440 continua em uso, com o `EDID_OVERRIDE` na instância ativa. A máquina do Daniel está resolvida. O nome GDI mudou de `DISPLAY7` para `DISPLAY2` (normal); o InstanceID não mudou.
- [x] Versão nova rodada no Daniel: nenhum `EDID_OVERRIDE` em outra instância; EDID do WMI igual ao do registro.
- [x] **Colega também resolvido** (informado pelo Daniel): o 1440p foi aplicado no monitor dele. Objetivo do projeto atingido.
- [x] Botão "Checar atualizações" + Action de build/release na `main`; merge para a `main`.
- [x] Branch padrão do repositório = `main`. Primeiro Release publicado pela Action: `v0.3.0.1`.
- [ ] Opcional: obter o relatório da máquina do colega para guardar o EDID de fábrica real do MN801 como fixture (hoje ele é inferido) e registrar o InstanceID e a GPU dele em `KnownModels`.
