using System;
using System.Threading;

namespace RinhaBackend.Net.Helper;

public sealed class AtomicCounter
{
    private int _value;

    public int Next() => Interlocked.Increment(ref _value);

    public int Current => _value;

    public void Reset() => Interlocked.Exchange(ref _value, 0);
}