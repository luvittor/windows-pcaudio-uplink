# windows-pcaudio-uplink

Cliente Windows em C#/.NET para capturar o audio do PC via WASAPI loopback e enviar MP3 para o Survivor (`harmonyfm-mimic`).

## Uso padrao

```powershell
dotnet run
```

## Configuracao

O app le as configuracoes nesta ordem, com prioridade crescente:

1. `appsettings.json`
2. variaveis de ambiente `PCAUDIO_UPLINK_*`
3. parametros de linha de comando

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

Se o app aparecer com outro nome, rode `dotnet run -- --list-processes` e ajuste `processName` ou use `--process-id`.

## Testes automatizados

```powershell
dotnet run -- --run-tests
```

Os testes cobrem regressao de configuracoes antigas em modo `device`, perfil FLAC 48 kHz atual, perfil novo do Reprodutor de Midia, argumentos do FFmpeg, mapeamento de formato PCM e utilitarios de audio.

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
