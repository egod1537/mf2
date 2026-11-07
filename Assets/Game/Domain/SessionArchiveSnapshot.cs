using System;
using System.Collections.Generic;
using MF2.World.Storage;

namespace MF2.Game.Domain
{
    /// <summary>
    /// Managed persistence boundary for a server session. It contains no Unity object,
    /// FishNet connection, or native container reference.
    /// </summary>
    public sealed class SessionArchiveSnapshot
    {
        private readonly IReadOnlyList<PlayerModel> players;

        public SessionArchiveSnapshot(
            SessionId id,
            long generation,
            SessionPhase phase,
            long tickIndex,
            double elapsedSeconds,
            int seed,
            IReadOnlyList<PlayerModel> sourcePlayers,
            WorldStorageSnapshot world)
        {
            if (id.Value == Guid.Empty)
                throw new ArgumentException("An archive requires a valid session id.", nameof(id));
            if (generation <= 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (phase != SessionPhase.Ready &&
                phase != SessionPhase.Running &&
                phase != SessionPhase.Paused)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phase),
                    "Only ready, running, or paused sessions can be archived.");
            }
            if (tickIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(tickIndex));
            if (elapsedSeconds < 0d || double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }
            if (sourcePlayers == null)
                throw new ArgumentNullException(nameof(sourcePlayers));

            var copy = new PlayerModel[sourcePlayers.Count];
            var ids = new HashSet<PlayerId>();
            for (var i = 0; i < sourcePlayers.Count; i++)
            {
                var player = sourcePlayers[i];
                if (player.Id.Value == Guid.Empty || !ids.Add(player.Id))
                {
                    throw new ArgumentException(
                        "Archive players require unique, valid ids.",
                        nameof(sourcePlayers));
                }
                copy[i] = player;
            }
            Array.Sort(copy, (left, right) => left.Id.CompareTo(right.Id));

            Id = id;
            Generation = generation;
            Phase = phase;
            TickIndex = tickIndex;
            ElapsedSeconds = elapsedSeconds;
            Seed = seed;
            players = Array.AsReadOnly(copy);
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public SessionId Id { get; }
        public long Generation { get; }
        public SessionPhase Phase { get; }
        public long TickIndex { get; }
        public double ElapsedSeconds { get; }
        public int Seed { get; }
        public IReadOnlyList<PlayerModel> Players => players;
        public WorldStorageSnapshot World { get; }
    }
}
