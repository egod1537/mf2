using System;
using System.Collections.Generic;
using MF2.World.Storage;

namespace MF2.Game.Domain
{
    public sealed class SessionSnapshot
    {
        private readonly IReadOnlyList<PlayerId> playerIds;
        private readonly IReadOnlyList<PlayerModel> players;

        internal SessionSnapshot(
            SessionId id,
            long generation,
            SessionPhase phase,
            long tickIndex,
            int seed,
            PlayerModel[] playerSnapshots,
            int loadedChunkCount,
            int dirtyStorageChunkCount,
            WorldStorageSnapshot world)
        {
            Id = id;
            if (generation <= 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            Generation = generation;
            Phase = phase;
            TickIndex = tickIndex;
            Seed = seed;
            if (playerSnapshots == null)
                throw new ArgumentNullException(nameof(playerSnapshots));

            var snapshotCopy = (PlayerModel[])playerSnapshots.Clone();
            var idCopy = new PlayerId[snapshotCopy.Length];
            for (var i = 0; i < snapshotCopy.Length; i++)
                idCopy[i] = snapshotCopy[i].Id;
            players = Array.AsReadOnly(snapshotCopy);
            playerIds = Array.AsReadOnly(idCopy);
            LoadedChunkCount = loadedChunkCount;
            DirtyStorageChunkCount = dirtyStorageChunkCount;
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public SessionId Id { get; }
        public long Generation { get; }
        public SessionPhase Phase { get; }
        public long TickIndex { get; }
        public int Seed { get; }
        public bool IsRunning => Phase == SessionPhase.Running;
        public IReadOnlyList<PlayerId> PlayerIds => playerIds;
        public IReadOnlyList<PlayerModel> Players => players;
        public int LoadedChunkCount { get; }
        public int DirtyStorageChunkCount { get; }
        public WorldStorageSnapshot World { get; }
    }
}
