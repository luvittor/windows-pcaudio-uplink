using System.Text.Json;

namespace WindowsPcAudioUplink;

public sealed class DefaultsSettings
{
    public string? Uplink { get; set; }
    public string? Capture { get; set; }

    public static DefaultsSettings LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<DefaultsSettings>(json, AppSettings.JsonOptions) ?? new DefaultsSettings();
    }

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Uplink) || string.IsNullOrWhiteSpace(Capture))
        {
            throw new InvalidOperationException($"Configuracao invalida: {path} precisa definir 'uplink' e 'capture'.");
        }
    }
}
