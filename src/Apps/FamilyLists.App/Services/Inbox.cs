namespace FamilyLists.App.Services;

/// <summary>
/// Things shared into the app from other apps, waiting to be used. Only the latest of each kind is kept, in memory only.
/// <list type="bullet">
/// <item>A bill (photo or PDF from WhatsApp, Gmail, Files): waits until a month's list is open, which offers to read it.</item>
/// <item>A note (text from Google Keep, Samsung Notes…): opens the Import screen.</item>
/// </list>
/// </summary>
public static class Inbox
{
    private static readonly Lock Gate = new();
    private static (byte[] Content, string? Name)? _pending;
    private static string? _note;

    public static event Action? Arrived;

    public static event Action? NoteArrived;

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

    public static bool HasNote
    {
        get
        {
            lock (Gate)
            {
                return _note is not null;
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

    public static void PutNote(string text)
    {
        lock (Gate)
        {
            _note = text;
        }

        NoteArrived?.Invoke();
    }

    public static string? TakeNote()
    {
        lock (Gate)
        {
            var note = _note;
            _note = null;
            return note;
        }
    }
}
