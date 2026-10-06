using System.Text;

namespace WindowsPcAudioUplink;

public sealed class RollingFileTextWriter(string path, int maxLines) : TextWriter
{
    readonly object gate = new();
    readonly StringBuilder partialLine = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (gate)
        {
            if (value == '\r')
            {
                return;
            }

            if (value == '\n')
            {
                AppendLine(partialLine.ToString());
                partialLine.Clear();
                return;
            }

            partialLine.Append(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (gate)
        {
            if (partialLine.Length > 0)
            {
                partialLine.Append(value);
                AppendLine(partialLine.ToString());
                partialLine.Clear();
                return;
            }

            AppendLine(value ?? "");
        }
    }

    public override void Flush()
    {
        lock (gate)
        {
            if (partialLine.Length > 0)
            {
                AppendLine(partialLine.ToString());
                partialLine.Clear();
            }
        }
    }

    void AppendLine(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {line}{Environment.NewLine}", Encoding.UTF8);
        TrimIfNeeded();
    }

    void TrimIfNeeded()
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length <= maxLines)
        {
            return;
        }

        File.WriteAllLines(path, lines.Skip(lines.Length - maxLines), Encoding.UTF8);
    }
}
