using System;
using System.Collections.Generic;
using System.Linq;

namespace Before1800
{
    // Simulation code checks IsPaused. UI, networking and timeout clocks must not.
    public sealed class PauseService
    {
        private readonly Dictionary<string, string> tokens = new Dictionary<string, string>();
        public bool IsPaused { get { return tokens.Count != 0; } }
        public string[] Reasons { get { return tokens.Values.Distinct().OrderBy(x => x).ToArray(); } }
        public event Action<bool> Changed;
        public IDisposable Acquire(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A pause reason is required.", "reason");
            bool wasPaused = IsPaused;
            string id = Guid.NewGuid().ToString("N");
            tokens.Add(id, reason);
            Notify(wasPaused);
            return new Token(this, id);
        }
        public void Reset()
        {
            bool wasPaused = IsPaused;
            tokens.Clear();
            Notify(wasPaused);
        }
        private void Release(string id)
        {
            bool wasPaused = IsPaused;
            tokens.Remove(id); // Old-generation tokens cannot release a new pause.
            Notify(wasPaused);
        }
        private void Notify(bool previous)
        {
            if (previous != IsPaused && Changed != null) Changed(IsPaused);
        }
        private sealed class Token : IDisposable
        {
            private PauseService owner;
            private readonly string id;
            public Token(PauseService owner, string id) { this.owner = owner; this.id = id; }
            public void Dispose()
            {
                PauseService service = owner;
                owner = null;
                if (service != null) service.Release(id);
            }
        }
    }
}
