# UpRes

Verificador **somente leitura** que avalia se um monitor 1080p aceita sinal **2560×1440**. Quando é viável, ele gera o `.reg` de **EDID override** para *você* aplicar manualmente, já com o `.reg` de desfazer.

O app **não** grava no registro, **não** executa comandos e **não** pede elevação. Ele lê, calcula e mostra texto. Você pode copiar o texto ou salvar como `.reg`.

## Requisitos
- Windows 10/11 com .NET Framework 4.8, que já vem instalado.
- Para compilar: Visual Studio 2022/2026 ou o .NET SDK 8+.

## Compilar
```
dotnet build UpRes.sln -c Release
```
Executável: `src/UpRes.App/bin/Release/net48/UpRes.exe`. Copie junto o `UpRes.Core.dll` da mesma pasta.

Testes: `dotnet test`

## Usar
1. Abra o `UpRes.exe` sem administrador e escolha o monitor externo.
2. Leia o veredito e a aba **Diagnóstico**.
3. Salve o **Desfazer (.reg)**. Se a aba **Override atual** tiver conteúdo, salve também esse backup.
4. Salve o **Aplicar (.reg)** e siga a aba **Instruções**: importar como admin, reiniciar e escolher 2560×1440 em Configurações > Tela.

O `.reg` vale **só para aquele monitor, naquela máquina, naquela porta**, porque o InstanceID muda entre máquinas. Para outra máquina, rode o app nela.

> Nenhuma leitura garante que o scaler do monitor aceita 2560 pixels por linha. O veredito é heurístico; só o teste prova.
> Em máquina corporativa, combine com o TI antes: alterar o registro, ou até rodar um `.exe` novo sem assinatura, pode gerar alerta no antivírus.

## Estrutura
| Projeto | Conteúdo |
|---|---|
| `src/UpRes.Core` | Parser de EDID (base, CTA-861, DisplayID), CVT-RB, montagem do override, avaliador, geradores de `.reg`/PowerShell/instruções. `netstandard2.0`, sem dependência do Windows. |
| `src/UpRes.App` | WinForms (.NET Framework 4.8): `QueryDisplayConfig`, `EnumDisplaySettings`, leitura do registro e do WMI. |
| `tests/UpRes.Core.Tests` | xUnit. |

Veja o `CLAUDE.md` para o contexto técnico completo e o estado atual.
