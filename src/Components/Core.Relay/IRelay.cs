namespace FamilyLists.Core.Relay;

/// <summary>
/// A store-and-forward mailbox between phones. The relay only ever sees encrypted text and files, the topic name and
/// the caller's IP address. It keeps messages for a limited time (ntfy.sh: messages ~12 h, files ~3 h).
/// </summary>
public interface IRelay
{
    /// <returns>The relay's id for the new message.</returns>
    Task<string> PublishAsync(string topic, string text, CancellationToken cancellationToken = default);

    Task<string> PublishFileAsync(string topic, byte[] file, string text, CancellationToken cancellationToken = default);

    /// <param name="since">The last message id already seen, or null for everything still kept.</param>
    Task<IReadOnlyList<RelayMessage>> FetchAsync(string topic, string? since, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAsync(Uri attachmentUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps a connection open and calls <paramref name="onMessage"/> whenever a new message arrives on the topic, so
    /// an open app can sync within a second or two instead of polling. Returns when cancelled or the connection drops
    /// (the caller reconnects). The message itself is then read by a normal fetch, with all the usual checks.
    /// </summary>
    Task WatchAsync(string topic, Func<Task> onMessage, CancellationToken cancellationToken = default) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}

/// <summary>Creates a relay client for a server URL (each family can use a different server).</summary>
public interface IRelayFactory
{
    IRelay For(string serverUrl);
}

/// <summary>Real ntfy clients over one shared HttpClient.</summary>
public sealed class NtfyRelayFactory(HttpClient http, string userAgent) : IRelayFactory
{
    public IRelay For(string serverUrl) => new NtfyRelay(http, new Uri(serverUrl), userAgent);
}
