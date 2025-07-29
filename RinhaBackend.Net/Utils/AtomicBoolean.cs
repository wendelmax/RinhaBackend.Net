namespace RinhaBackend.Net.Helper;

public sealed class AtomicBoolean
{
    private int _flag;

    public AtomicBoolean(bool initialValue = false)
    {
        _flag = initialValue ? 1 : 0;
    }

    public bool Value
    {
        get => Interlocked.CompareExchange(ref _flag, 1, 1) == 1;
        set => Interlocked.Exchange(ref _flag, value ? 1 : 0);
    }

    public bool CompareAndSet(bool expected, bool newValue)
    {
        int expectedInt = expected ? 1 : 0;
        int newInt = newValue ? 1 : 0;
        return Interlocked.CompareExchange(ref _flag, newInt, expectedInt) == expectedInt;
    }
}