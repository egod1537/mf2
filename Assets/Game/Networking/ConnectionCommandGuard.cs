using System.Collections.Generic;

namespace MF2.Game.Networking
{
    public enum ConnectionCommandRejection : byte
    {
        None,
        InvalidEnvelope,
        DuplicateOrOutOfOrder,
        RateLimited
    }

    /// <summary>
    /// Server-side replay and rate guard for one authenticated connection. Sequence values are
    /// client-local transport metadata; they never become domain player identity.
    /// </summary>
    public sealed class ConnectionCommandGuard
    {
        public const int DefaultMaxCommandsPerTick = 8;

        private sealed class ConnectionState
        {
            public ulong LastObservedSequence;
            public long WindowTick = -1;
            public int AcceptedInWindow;
        }

        private readonly Dictionary<int, ConnectionState> states = new();
        private readonly int maxCommandsPerTick;

        public ConnectionCommandGuard(int maxCommandsPerTick = DefaultMaxCommandsPerTick)
        {
            if (maxCommandsPerTick <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxCommandsPerTick));
            this.maxCommandsPerTick = maxCommandsPerTick;
        }

        public int Count => states.Count;

        public bool TryAccept(
            int connectionId,
            ulong sequence,
            long authoritativeTick,
            out ConnectionCommandRejection rejection)
        {
            if (connectionId < 0 || sequence == 0 || authoritativeTick < 0)
            {
                rejection = ConnectionCommandRejection.InvalidEnvelope;
                return false;
            }

            if (!states.TryGetValue(connectionId, out var state))
            {
                state = new ConnectionState();
                states.Add(connectionId, state);
            }
            else if (sequence <= state.LastObservedSequence)
            {
                rejection = ConnectionCommandRejection.DuplicateOrOutOfOrder;
                return false;
            }

            // Mark every new sequence as observed, including rate-limited envelopes. A client
            // cannot replay a rejected command during a later tick window.
            state.LastObservedSequence = sequence;
            if (state.WindowTick != authoritativeTick)
            {
                state.WindowTick = authoritativeTick;
                state.AcceptedInWindow = 0;
            }

            if (state.AcceptedInWindow >= maxCommandsPerTick)
            {
                rejection = ConnectionCommandRejection.RateLimited;
                return false;
            }

            state.AcceptedInWindow++;
            rejection = ConnectionCommandRejection.None;
            return true;
        }

        public bool Release(int connectionId) => states.Remove(connectionId);
        public void Clear() => states.Clear();
    }
}
