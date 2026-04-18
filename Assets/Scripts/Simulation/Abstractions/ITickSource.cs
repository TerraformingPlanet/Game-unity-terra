using System;

public interface ITickSource
{
    event Action<int> OnTick;

    int TickCount { get; }
    bool IsRunning { get; }
}