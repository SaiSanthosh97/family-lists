namespace FamilyLists.App.Services;

/// <summary>
/// A bill shared into the app from another app (WhatsApp, Gmail, Files). It waits here until a month's list is open,
/// which then offers to read it. Only the latest share is kept, in memory only.
/// </summary>
public static class Inbox
{
    private static readonly Lock Gate = new();
    private static (byte[] Content, string? Name)? _pending;

    public static event Action? Arrived;

    public static bool HasBill
    {
        get
        {
            lock (Gate)
            {
                return _pending is not null;
            }
        }
    }

    public static void Put(byte[] content, string? fileName)
    {
        lock (Gate)
        {
            _pending = (content, fileName);
        }

        Arrived?.Invoke();
    }

    public static (byte[] Content, string? Name)? Take()
    {
        lock (Gate)
        {
            var item = _pending;
            _pending = null;
            return item;
        }
    }
}
