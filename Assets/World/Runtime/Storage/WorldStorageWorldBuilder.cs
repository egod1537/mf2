using System;
using MF2.World.Core;

namespace MF2.World.Storage
{
    /// <summary>
    /// Builds a replacement world from a fully validated managed snapshot. The caller can keep
    /// its current live world until this method succeeds, avoiding partial mutation on bad input.
    /// </summary>
    public static class WorldStorageWorldBuilder
    {
        public static WorldCore CreateWorld(WorldStorageSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            long totalBlocks = 0;
            for (var i = 0; i < snapshot.Chunks.Count; i++)
            {
                totalBlocks += snapshot.Chunks[i].Blocks.Count;
                if (totalBlocks > int.MaxValue)
                    throw new WorldStorageException("Stored world exceeds Int32 command capacity.");
            }

            var world = new WorldCore(
                initialChunkCapacity: Math.Max(4, snapshot.ChunkCount),
                initialCommandCapacity: Math.Max(64, (int)totalBlocks));
            try
            {
                for (var chunkIndex = 0; chunkIndex < snapshot.Chunks.Count; chunkIndex++)
                {
                    var chunk = snapshot.Chunks[chunkIndex];
                    for (var blockIndex = 0; blockIndex < chunk.Blocks.Count; blockIndex++)
                    {
                        var storedBlock = chunk.Blocks[blockIndex];
                        var position = RegionCoordinates.ComposeWorldPosition(
                            chunk.ChunkCoordinate,
                            storedBlock.Index);
                        world.Enqueue(WorldCommand.SetBlock(position, storedBlock.ToBlock()));
                    }
                }

                if (totalBlocks > 0)
                    world.Flush();
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }
    }
}
