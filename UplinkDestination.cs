namespace WindowsPcAudioUplink;

public sealed record UplinkDestination(string Host, int Port = 18080)
{
    public override string ToString() => $"{Host}:{Port}";
}

public static class UplinkDestinationSelector
{
    public static IReadOnlyList<UplinkDestination> Resolve(
        string primaryHost,
        int primaryPort,
        IEnumerable<UplinkDestination>? configured = null)
    {
        var destinations = new List<UplinkDestination>();
        AddIfValid(destinations, new UplinkDestination(primaryHost, primaryPort));

        if (configured is not null)
        {
            foreach (var destination in configured)
            {
                AddIfValid(destinations, destination);
            }
        }

        return destinations;
    }

    static void AddIfValid(List<UplinkDestination> destinations, UplinkDestination destination)
    {
        if (string.IsNullOrWhiteSpace(destination.Host) || destination.Port is < 1 or > 65535)
        {
            return;
        }

        if (!destinations.Any(existing =>
                string.Equals(existing.Host, destination.Host, StringComparison.OrdinalIgnoreCase) &&
                existing.Port == destination.Port))
        {
            destinations.Add(destination);
        }
    }
}
