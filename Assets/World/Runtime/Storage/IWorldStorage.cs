using System.Collections.Generic;
using Unity.Mathematics;

namespace MF2.World.Storage
{
    /// <summary>
    /// Managed persistence seam. Implementations never receive WorldCore, NativeContainers,
    /// Unity object references, or mesh data.
    /// </summary>
    public interface IWorldStorage
    {
        IReadOnlyList<RecoveryArtifact> RecoveryArtifacts { get; }

        WorldStorageSnapshot LoadAll();

        IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate);

        /// <summary>
        /// Merges the supplied replacement chunks with entries already stored in this region.
        /// An empty replacement deletes that chunk entry. Chunks not supplied are preserved.
        /// </summary>
        void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks);
    }
}
