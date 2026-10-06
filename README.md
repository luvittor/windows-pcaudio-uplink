# windows-pcaudio-uplink

Cliente Windows em C#/.NET para capturar o audio do PC via WASAPI loopback e enviar para via TCP/FFmpeg para um servidor.

## Uso padrao

```powershell
dotnet run
```

Esse modo roda em foreground quando nao existe um servidor ativo. Para iniciar em background e liberar o terminal, use `start`.

## Background

Iniciar com os defaults:

```powershell
dotnet run -- start
```

Iniciar com outra captura:

```powershell
dotnet run -- start --capture spotify
dotnet run -- start --capture chrome
```

Trocar a captura do servidor ativo sem reiniciar o FFmpeg nem a conexao TCP:

```powershell
dotnet run -- switch --capture spotify
dotnet run -- switch --capture media-player
dotnet run -- switch --capture device
```

Com o servidor ativo, a forma curta tambem faz hot swap e retorna ao terminal:

```powershell
dotnet run -- --capture chrome
```

Ela nunca abre um segundo transmissor. `dotnet run` sem parametros, quando o servidor ja esta ativo, apenas mostra o status. Para trocar o perfil de uplink, pare e inicie novamente; somente a captura e trocada a quente.

Consultar:

```powershell
dotnet run -- status
```

Ver log:

```powershell
dotnet run -- log
```

Parar:

```powershell
dotnet run -- stop
```

O log e o estado local ficam em `runtime/`, que e ignorado pelo git. O log guarda no maximo as ultimas 1000 linhas.

O status mostra os PIDs do servidor e do FFmpeg, a quantidade de trocas e os bytes descartados por backpressure. O PID do FFmpeg deve permanecer igual durante um hot swap.

## Configuracao

O app le as configuracoes nesta ordem, com prioridade crescente:

1. `appsettings.json` ou `--config`
2. perfis separados `--uplink` e `--capture`
3. variaveis de ambiente `PCAUDIO_UPLINK_*`
4. parametros de linha de comando

Os arquivos antigos `appsettings*.json` continuam funcionando. Para evoluir para UI/controle local, os perfis novos ficam separados:

- `configs/defaults.json`: aponta quais perfis iniciar quando nenhum `--uplink`/`--capture` for informado
- `configs/uplink/*.json`: destino e formato da transmissao, abre a conexao FFmpeg/TCP
- `configs/capture/*.json`: fonte de captura, ganho e silencio, pode ser trocado com o servidor ligado

No fluxo por perfis, qualquer inconsistencia impede a inicializacao: `configs/defaults.json` ausente, campos `uplink`/`capture` ausentes, perfil apontado inexistente ou JSON invalido.

Defaults principais:

- host: `192.168.15.14`
- porta: `18080`
- codec de saida: MP3 via FFmpeg (`libmp3lame`)
- bitrate: `128k`
- ganho: `+6 dB`
- captura padrao: dispositivo de reproducao padrao do Windows via WASAPI loopback (`captureMode: "device"`)
- captura opcional: audio de uma arvore de processo/app (`captureMode: "process"`)
- conexao: injeta silencio PCM quando o WASAPI nao entrega frames, para manter o MP3/TCP aberto

## Volume

Ajuste pelo arquivo `appsettings.json`:

```json
{
  "gainDb": 6.0
}
```

Ou por linha de comando:

```powershell
dotnet run -- --gain-db 9
```

Ou por variavel de ambiente:

```powershell
$env:PCAUDIO_UPLINK_GAIN_DB = "9"
dotnet run
```

## Listar dispositivos

```powershell
dotnet run -- --list-devices
```

## Selecionar dispositivo

```powershell
dotnet run -- --device-index 0
```

## Capturar audio de um app/processo

Liste processos candidatos:

```powershell
dotnet run -- --list-processes
```

Capture pelo nome do processo:

```powershell
dotnet run -- --capture-mode process --process-name Microsoft.Media.Player
```

Ou pelo PID:

```powershell
dotnet run -- --capture-mode process --process-id 34060
```

O modo por processo usa a API de application/process loopback do Windows e requer Windows 10 2004 build 19041 ou superior. Ele captura o processo alvo e sua arvore de filhos.

## Usar outro arquivo de configuracao

```powershell
dotnet run -- --config appsettings-flac-48000-stereo16-media-player.json
```

## Usar perfis separados

Pelo caminho completo:

```powershell
dotnet run -- --uplink configs/uplink/flac-48000-stereo16.json --capture configs/capture/device.json
```

Ou pelo nome do perfil:

```powershell
dotnet run -- --uplink flac-48000-stereo16 --capture media-player
```

Nesse formato, `--uplink` define para onde e como transmitir; `--capture` define de onde capturar.

O motor converte todas as fontes para um transporte PCM interno estavel, com a taxa e os canais do uplink. Por isso uma troca entre uma fonte 44,1 kHz e outra 48 kHz nao reinicia o encoder.

Para conferir a configuracao resolvida sem iniciar audio:

```powershell
dotnet run -- --capture spotify --print-config
```

Perfis de captura incluidos:

- `device`
- `media-player`
- `spotify`
- `chrome`

## Teste com duracao

```powershell
dotnet run -- --duration 10
```

## Parametros uteis

- `--host 192.168.15.14`
- `--port 18080`
- `--bitrate 128k`
- `--gain-db 6`
- `--capture-mode device`
- `--device-index 0`
- `--process-name Microsoft.Media.Player`
- `--process-id 34060`
- `--capture-buffer-ms 100`
- `--config appsettings-flac-48000-stereo16-media-player.json`
- `--uplink flac-48000-stereo16`
- `--capture media-player`
- `--duration 10`
- `--ffmpeg-path C:\caminho\ffmpeg.exe`
- `--audio-codec flac`
- `--output-format flac`
- `--output-sample-rate 44100`
- `--output-channels 2`
- `--output-sample-format s16`
- `--silence-after-ms 150`
- `--silence-chunk-ms 100`
- `--status-interval-seconds 1`

## Enviar FLAC 44.1 kHz stereo 16-bit

Use os parametros abaixo para gerar FLAC em 44100 Hz, 2 canais e 16-bit:

```powershell
dotnet run -- --audio-codec flac --output-format flac --output-sample-rate 44100 --output-channels 2 --output-sample-format s16
```

Ou copie os valores de `appsettings-flac-44100-stereo16.json` para `appsettings.json`.

## Enviar FLAC 48 kHz stereo 16-bit do Reprodutor de Midia

Com o Reprodutor de Midia do Windows aberto e tocando audio:

```powershell
dotnet run -- --config appsettings-flac-48000-stereo16-media-player.json
```

Ou usando perfis separados:

```powershell
dotnet run -- --uplink flac-48000-stereo16 --capture media-player
```

Se o app aparecer com outro nome, rode `dotnet run -- --list-processes` e ajuste `processName` ou use `--process-id`.

## Enviar FLAC 48 kHz stereo 16-bit do Spotify

Com o Spotify aberto e tocando audio:

```powershell
dotnet run -- --uplink flac-48000-stereo16 --capture spotify
```

## Enviar FLAC 48 kHz stereo 16-bit do Chrome

Com o Chrome aberto e tocando audio:

```powershell
dotnet run -- --uplink flac-48000-stereo16 --capture chrome
```

Esse perfil captura o processo do Chrome. A API de process loopback do Windows nao separa uma aba especifica do navegador; se outra aba do Chrome tocar audio, ela pode entrar junto.

## Testes automatizados

```powershell
dotnet run -- --run-tests
```

Os testes cobrem configuracoes antigas e novas, parser de comandos, protocolo de controle por pipe, normalizacao de taxa/canais, hot swap, falha de troca sem perder a fonte atual, argumentos do FFmpeg, log rotativo e utilitarios de audio.

## Receptor mock local

Para testar sem o servidor remoto, abra um terminal e execute:

```powershell
dotnet run -- mock-server --duration 60
```

Em outro terminal:

```powershell
dotnet run -- start --uplink mock-flac-48000-stereo16 --capture device
dotnet run -- switch --capture media-player
dotnet run -- switch --capture spotify
dotnet run -- status
dotnet run -- stop
```

O mock aceita uma unica conexao TCP, grava `runtime/mock-received.flac`, mostra os bytes recebidos e analisa o volume ao final. Se o uplink reconectar durante uma troca, o teste falha em vez de esconder a reconexao.

Para validar o isolamento entre apps, deixe apenas um deles tocando e compare `dotnet run -- status` antes e depois do hot swap. A captura do app pausado deve ficar proxima de `nivel: 0%`; a do app tocando deve mostrar nivel acima de zero, mantendo os mesmos PIDs de servidor e FFmpeg.

## Variaveis de ambiente

- `PCAUDIO_UPLINK_HOST`
- `PCAUDIO_UPLINK_PORT`
- `PCAUDIO_UPLINK_BITRATE`
- `PCAUDIO_UPLINK_GAIN_DB`
- `PCAUDIO_UPLINK_CAPTURE_MODE`
- `PCAUDIO_UPLINK_DEVICE_INDEX`
- `PCAUDIO_UPLINK_PROCESS_ID`
- `PCAUDIO_UPLINK_PROCESS_NAME`
- `PCAUDIO_UPLINK_CAPTURE_BUFFER_MS`
- `PCAUDIO_UPLINK_DURATION_SECONDS`
- `PCAUDIO_UPLINK_CONFIG`
- `PCAUDIO_UPLINK_UPLINK_CONFIG`
- `PCAUDIO_UPLINK_CAPTURE_CONFIG`
- `PCAUDIO_UPLINK_LIST_PROCESSES`
- `PCAUDIO_UPLINK_FFMPEG_PATH`
- `PCAUDIO_UPLINK_FFMPEG_LOG_LEVEL`
- `PCAUDIO_UPLINK_AUDIO_CODEC`
- `PCAUDIO_UPLINK_OUTPUT_FORMAT`
- `PCAUDIO_UPLINK_OUTPUT_SAMPLE_RATE`
- `PCAUDIO_UPLINK_OUTPUT_CHANNELS`
- `PCAUDIO_UPLINK_OUTPUT_SAMPLE_FORMAT`
- `PCAUDIO_UPLINK_SILENCE_AFTER_MS`
- `PCAUDIO_UPLINK_SILENCE_CHUNK_MS`
- `PCAUDIO_UPLINK_STATUS_INTERVAL_SECONDS`
- `PCAUDIO_UPLINK_FFMPEG_EXIT_TIMEOUT_MS`

## Teste importante

Este prototipo existe para comparar com o `python-pcaudio-uplink` e verificar se o caminho NAudio/WASAPI se comporta como o Stream What You Hear: transmitir para o Survivor mesmo quando o audio local estiver mutado.

## Parar

Use `Ctrl+C`.

## Dependencias instaladas

- .NET SDK 9 via Winget: `Microsoft.DotNet.SDK.9`
- Pacotes NuGet: `NAudio`, `NAudio.Wasapi`
- FFmpeg: usa `ffmpeg` no PATH ou procura a instalacao Winget `Gyan.FFmpeg`

## Remocao

```powershell
winget uninstall --id Microsoft.DotNet.SDK.9
```

Se quiser remover o FFmpeg instalado antes:

```powershell
winget uninstall --id Gyan.FFmpeg
```
