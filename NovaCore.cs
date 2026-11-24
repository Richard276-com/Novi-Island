// NOVA Island - experience state foundation.
// Compatible with C# 5 and .NET Framework 4.8.
using System;

namespace DynIsland
{
    public enum NovaState
    {
        Idle, Working, Thinking, Permission, Success, Error, Notification
    }

    public sealed class NovaStateChangedEventArgs : EventArgs
    {
        public NovaState Previous { get; private set; }
        public NovaState Current { get; private set; }
        public string Message { get; private set; }

        public NovaStateChangedEventArgs(NovaState previous, NovaState current, string message)
        {
            Previous = previous;
            Current = current;
            Message = message ?? String.Empty;
        }
    }

    // Single source of truth for NOVA's visible experience state.
    public sealed class NovaStateManager
    {
        private NovaState current = NovaState.Idle;
        private string message = String.Empty;
        private DateTime changedAt = DateTime.UtcNow;

        public event EventHandler<NovaStateChangedEventArgs> StateChanged;
        public NovaState Current { get { return current; } }
        public string Message { get { return message; } }
        public DateTime ChangedAtUtc { get { return changedAt; } }

        public void SetState(NovaState next, string statusMessage)
        {
            if (String.IsNullOrEmpty(statusMessage)) statusMessage = String.Empty;
            if (current == next && message == statusMessage) return;

            NovaState previous = current;
            current = next;
            message = statusMessage;
            changedAt = DateTime.UtcNow;

            EventHandler<NovaStateChangedEventArgs> handler = StateChanged;
            if (handler != null)
                handler(this, new NovaStateChangedEventArgs(previous, current, message));
        }

        public void Reset()
        {
            SetState(NovaState.Idle, String.Empty);
        }
    }
}
