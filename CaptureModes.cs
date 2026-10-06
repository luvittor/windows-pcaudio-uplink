namespace WindowsPcAudioUplink;

public static class CaptureModes
{
    public const string Device = "device";
    public const string Process = "process";

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            null or "" => Device,
            "driver" => Device,
            "wasapi" => Device,
            "device" => Device,
            "app" => Process,
            "application" => Process,
            "process" => Process,
            _ => throw new ArgumentException($"Modo de captura invalido: {value}. Use 'device' ou 'process'.")
        };
    }
}
