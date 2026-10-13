# NextPVR Sala

Cliente nativo em C# para [NextPVR](https://www.nextpvr.com/), com .NET 8 em Windows, Linux, Android e Tizen moderno, além de uma variante compatível com a Samsung BU8000/Tizen 6.5. O aplicativo e o player executam no dispositivo e acessam diretamente o servidor NextPVR, sem aplicação web, WebView ou serviços auxiliares.

## Recursos

- Autenticação pelo PIN de acesso remoto do NextPVR.
- Canais de TV e rádio em lista contínua, sem paginação.
- Busca por nome ou número, ignorando maiúsculas e acentos.
- Layout consistente: menu superior, busca, lista de canais e ações do canal selecionado.
- Reprodução em tela cheia, com nome do feed e controles translúcidos no overlay.
- Controles ocultados após quatro segundos de inatividade; uso do controle remoto ou mouse os revela novamente.
- Troca de feeds com CH+/CH− e botões Canal +/−, percorrendo a lista completa do servidor e retornando ao início/fim nos limites.
- Guia de programação das próximas 24 horas e agendamento de gravações.
- Listagem e reprodução de gravações, com pausa e avanço/retorno de 30 segundos.

A interface está em português e aceita controle remoto, teclado e mouse. O endereço do servidor é salvo localmente; o PIN e a sessão permanecem somente em memória.

## Plataformas e projetos

| Plataforma | Projeto | Interface e reprodução | Runtime |
|---|---|---|---|
| Windows / Linux | `src/NextPvr.Desktop` | Avalonia + libVLC no processo | .NET 8 |
| Android / Android TV | `src/NextPvr.Android` | Views nativas + libVLC incorporado | .NET 8 para Android; Android 6+ |
| Tizen 10 / API 13 | `src/NextPvr.Tizen` | NUI + VideoView nativos | .NET 8 do firmware |
| Samsung BU8000 / Tizen 6.5 | `src/NextPvr.Tizen.Legacy` | Mesmo código NUI + VideoView | TizenFX API 9 do firmware |
| Núcleo compartilhado | `src/NextPvr.Core` | API NextPVR e navegação entre feeds | .NET 8 / .NET Standard 2.1 |

O TFM `tizen90` do projeto Legacy significa **TizenFX API 9 / Tizen OS 6.5**, não Tizen OS 9. Ele usa o núcleo .NET Standard 2.1. O pacote moderno `net8.0-tizen10.0` não é compatível com a BU8000/Tizen 6.5. Confira o firmware e as [especificações Samsung](https://developer.samsung.com/smarttv/develop/specifications/general-specifications.html) antes de escolher o projeto.

## Executar

As distribuições são geradas em `artifacts/`; essa pasta não é versionada.

| Plataforma | Arquivo |
|---|---|
| Windows x64 | `artifacts/windows-x64/NextPvr.Desktop.exe` |
| Linux x64 | `artifacts/linux-x64/NextPvr.Desktop` |
| Android | `artifacts/android/com.nextpvrclient.sala-Signed.apk` |
| Tizen moderno | `artifacts/tizen/org.npvr.sala-1.0.0.tpk` |
| Tizen 6.5, certificado padrão | `artifacts/tizen65/org.npvr.sala-1.1.0.tpk` |
| Tizen 6.5, assinatura Samsung | `artifacts/tizen65/samsung/org.npvr.sala-1.1.0.tpk` |

**Windows:** mantenha a pasta inteira ao distribuir. Ela inclui o runtime .NET e o VLC.

**Linux:** a distribuição inclui .NET, mas requer libVLC, plugins de codecs e sessão gráfica X11. Em Debian/Ubuntu, por exemplo:

```bash
sudo apt install libvlc5 vlc-plugin-base vlc-plugin-video-output
chmod +x artifacts/linux-x64/NextPvr.Desktop
./artifacts/linux-x64/NextPvr.Desktop
```

**Android:** instale o APK assinado. Ele inclui .NET e VLC e permite aparelhos sem touchscreen, mouse e abertura pelo launcher de Android TV. A assinatura local é de desenvolvimento; configure uma chave de distribuição antes de publicar em loja.

**Samsung Tizen:** use o pacote adequado ao firmware e um certificado **Samsung TV** com o DUID do aparelho autorizado. O certificado padrão do SDK e o distribuidor genérico Tizen não substituem esse certificado. As variantes moderna e Legacy têm o mesmo app ID (`org.npvr.sala`).

Na primeira abertura, informe o endereço do servidor, por exemplo `http://192.168.1.10:8866`, e o PIN de acesso remoto configurado no NextPVR. O servidor deve permitir clientes remotos e estar acessível na rede. Canais e EPG devem estar configurados no servidor; não é necessário compartilhar pastas de gravações.

## Samsung BU8000: compilar, assinar e instalar

Utilize a [Tizen Extension para VS Code](https://marketplace.visualstudio.com/items?itemName=tizen.vscode-tizen-csharp) e as ferramentas do SDK instaladas por ela.

1. Abra `NextPvr.Tizen.code-workspace` em **File → Open Workspace from File**.
2. Confirme o projeto `src/NextPvr.Tizen.Legacy`, compatível com Tizen 6.5/API 9.
3. Conecte a TV em modo de desenvolvimento e configure um perfil de certificado Samsung TV autorizado para seu DUID.
4. Execute **Terminal → Run Task → NextPVR: assinar e instalar na BU8000**.
5. Informe o nome do perfil Samsung e o IP/porta da TV.

A tarefa compila em Release, reassina o pacote, instala e inicia o aplicativo. **Ctrl+Shift+B somente compila**; não instala nem substitui a assinatura Samsung.

O mesmo fluxo está disponível no PowerShell:

```powershell
./scripts/deploy-tizen65.ps1 -ProfileName 'Nome do perfil Samsung' -DeviceSerial 'IP_DA_TV:26101'
```

Para apenas assinar um pacote Legacy já compilado:

```powershell
./scripts/sign-tizen65.ps1 -ProfileName 'Nome do perfil Samsung'
```

Os scripts utilizam as credenciais gerenciadas pelo SDK e não recebem senhas de certificados em argumentos. Ajuste `-TizenCliPath` se o SDK estiver em outro local.

**Instalação validada em 12/10/2026:** a tarefa concluiu compilação sem erros/avisos, assinatura Samsung, instalação e inicialização na UN70BU8000GXZD, com Tizen 6.5 e ARM de 32 bits. O usuário confirmou a reprodução dos canais na TV. A versão mais recente, com troca de feeds pelo controle, foi instalada e iniciada; a conferência dessas teclas físicas ainda está pendente.

## Controles

| Ação | Controle remoto / teclado | Mouse |
|---|---|---|
| Navegar | Setas; Tab também no desktop | Clique / rolagem |
| Selecionar | OK / Enter | Clique |
| Assistir no desktop | Enter no canal ou Assistir | Duplo clique no canal ou Assistir |
| Consultar guia | Canal → Guia | Canal → Guia |
| Agendar gravação | Guia → programa → Gravar programa | Mesmo fluxo |
| Retornar | Back / Esc ou Voltar | Voltar |
| Reexibir overlay | Qualquer tecla do controle | Movimento, clique ou rolagem |
| Trocar feed ao vivo | CH+/CH− no Tizen/Android; Page Up/Down ou próxima/anterior faixa no desktop | Canal + / Canal − |
| Gravações | Pausar/Play, −30s e +30s | Mesmos botões |

O player abre automaticamente em tela cheia. O nome do feed aparece junto aos controles e é atualizado após cada troca, separado das mensagens de erro.

Com o overlay oculto, a primeira tecla de navegação apenas revela os controles. CH+/CH− troca o feed imediatamente e mostra o overlay; Voltar encerra o player diretamente. A troca usa a lista completa do servidor, independentemente do filtro de busca, e é oferecida somente durante TV ao vivo.

As teclas Samsung `XF86RaiseChannel` e `XF86LowerChannel` seguem a [documentação de interação da Samsung](https://developer.samsung.com/smarttv/develop/tizen-net-tv/guides/user-interaction.html).

No formulário desktop, ↑/↓ ou Enter movem entre campos e botões; ←/→ editam o texto. Android/Tizen utilizam o teclado nativo do aparelho.

## Desenvolvimento

`global.json` seleciona o SDK .NET **8.0.424**, com roll-forward para o patch mais recente dessa faixa. A solução principal contém núcleo, desktop e testes; Android e Tizen são projetos separados.

```powershell
dotnet restore NextPvrClient.sln --configfile NuGet.Config
dotnet build NextPvrClient.sln --no-restore
dotnet run --project src/NextPvr.Desktop --no-restore
```

Requisitos adicionais:

- Android: workload `android` do .NET 8, JDK 17 e SDK Android API 34/build-tools 34.
- Tizen moderno: [workload oficial Samsung](https://github.com/Samsung/Tizen.NET/wiki/Installing-Tizen-.NET-Workload) compatível com o SDK.
- Tizen Legacy: SDK NuGet `Tizen.NET.Sdk/1.1.7`, declarado no projeto, e ferramentas Samsung para assinatura/implantação.

Publicação:

```powershell
./scripts/publish.ps1 windows
./scripts/publish.ps1 linux
./scripts/publish.ps1 android -AndroidSdkDirectory 'C:\caminho\android-sdk'
./scripts/publish.ps1 tizen -TizenDotnet 'C:\caminho\dotnet.exe'
./scripts/publish.ps1 tizen65 -TizenDotnet 'C:\caminho\dotnet.exe'
```

### Configuração local do VS Code

Nesta máquina, o workspace e a tarefa de implantação utilizam um SDK preparado em `.tools/dotnet/`. Os caminhos do workspace e os padrões dos scripts são específicos deste ambiente: ajuste-os ao clonar ou mover o projeto. `.tools/` não é distribuída nem versionada.

Para abrir o workspace com o SDK local no ambiente do editor, salve os arquivos, feche todas as janelas do VS Code e execute:

```powershell
./scripts/open-tizen-vscode.ps1
```

Para avaliar o SDK sem abrir o editor:

```powershell
./scripts/open-tizen-vscode.ps1 -CheckOnly
```

Em caso de `NETSDK1139` no projeto moderno, confira se o SDK usado possui a carga Tizen. Para a BU8000, confirme também que o editor abriu a solução Legacy, em vez de uma solução automática antiga em cache. O runtime .NET 10 presente no SDK local atende as extensões C# atuais; não altera o alvo .NET 8 do aplicativo.

## Testes e validação

```powershell
dotnet run --project tests/NextPvr.ProtocolTests -c Release
dotnet run --project tests/NextPvr.DesktopTests -c Release
dotnet run --project tests/NextPvr.DesktopTests -c Release -- --ui-only
```

Depois de compilar o Legacy em Release, teste também a DLL .NET Standard usada pela TV:

```powershell
dotnet run --project tests/NextPvr.ProtocolTests -c Release -p:UseLegacyCore=true
```

- **Protocolo:** 22 verificações passaram, incluindo autenticação, sessão, canais, EPG, gravações, URLs autenticadas e troca circular de feeds. O transporte é em memória, sem servidor HTTP auxiliar.
- **Interface desktop:** navegação do formulário, lista contínua de 100 canais, busca por nome/número/acentos, nenhum resultado e acesso ao último canal.
- **Player incorporado:** carregamento do libVLC e decodificação de áudio PCM em memória.
- **Prévias:** `artifacts/desktop-preview.png` e `artifacts/channels-preview.png`.
- **Pacotes:** distribuições Windows/Linux, APK Android e TPKs das duas variantes Tizen gerados. Linux/Android não foram executados em hardware nesta máquina.

Consulte [VALIDATION.md](VALIDATION.md) para os registros e o roteiro de aceitação em dispositivos reais. Compilação e comando de inicialização não confirmam, por si só, a composição visual do overlay ou a recepção das teclas físicas.

## Protocolo e limitações

O núcleo acessa `/service` com respostas XML e utiliza `session.initiate`, `session.login`, `session.valid`, `channel.list`, `channel.listings`, `recording.list` e `recording.save`. A autenticação implementa `MD5(':' + MD5(PIN) + ':' + salt)`, exigido pelo NextPVR. Os streams usam `/live?channeloid=...` ou `/live?recording=...`, com SID. A sessão é renovada durante o uso e reautenticada quando o servidor retorna erro 8.

O PIN e as URLs de stream não são escritos nos logs da aplicação. Não versione certificados, chaves privadas ou configurações com credenciais.

O player usa o stream original do servidor. No Tizen, a reprodução depende dos codecs disponíveis no firmware; não há transcodificador local. Esta versão não oferece timeshift persistente, edição/exclusão de gravações, gravações recorrentes, DRM ou seleção de trilhas.

O protocolo foi conferido no [cliente NextPVR do projeto Kodi](https://github.com/kodi-pvr/pvr.nextpvr/tree/Piers/src). A implementação C# é própria e não inclui o código C++ desse projeto.

## Arquivos locais e versionamento

O `.gitignore` exclui saídas de compilação/publicação, SDKs locais, arquivos gerados pelo Tizen, caches de IDE, logs, pacotes instaláveis e certificados/chaves. Código, manifestos, soluções, scripts, configuração de pacotes e workspace compartilhado permanecem versionáveis.

