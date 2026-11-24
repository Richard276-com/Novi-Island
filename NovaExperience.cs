// NOVA Island - experience coordinator.
using System;

namespace DynIsland
{
    public sealed class NovaExperienceEngine : IDisposable
    {
        private bool disposed;
        public NovaStateManager States { get; private set; }
        public event EventHandler<NovaStateChangedEventArgs> StateChanged;

        public NovaExperienceEngine()
        {
            States = new NovaStateManager();
            States.StateChanged += OnStateChanged;
        }

        public void SetIdle() { EnsureNotDisposed(); States.Reset(); }
        public void SetWorking(string task) { Set(NovaState.Working, task); }
        public void SetThinking(string prompt) { Set(NovaState.Thinking, prompt); }
        public void RequestPermission(string permission) { Set(NovaState.Permission, permission); }
        public void SetSuccess(string message) { Set(NovaState.Success, message); }
        public void SetError(string message) { Set(NovaState.Error, message); }
        public void Notify(string message) { Set(NovaState.Notification, message); }

        private void Set(NovaState state, string message)
        {
            EnsureNotDisposed();
            States.SetState(state, message);
        }

        private void OnStateChanged(object sender, NovaStateChangedEventArgs e)
        {
            EventHandler<NovaStateChangedEventArgs> handler = StateChanged;
            if (handler != null) handler(this, e);
        }

        private void EnsureNotDisposed()
        {
            if (disposed) throw new ObjectDisposedException("NovaExperienceEngine");
        }

        public void Dispose()
        {
            if (disposed) return;
            States.StateChanged -= OnStateChanged;
            disposed = true;
        }
    }
}
