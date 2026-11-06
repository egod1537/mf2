using System;
using System.Collections.Generic;
using MF2.World.Registry;

namespace MF2.World.Storage
{
    /// <summary>
    /// Extension seam for ID and packed-state schema validation during managed storage I/O.
    /// Phase 4 validates known stable IDs; later state schemas can supply stricter validators.
    /// </summary>
    public interface IStoredBlockValidator
    {
        void Validate(uint id, uint state, string context);
    }

    public sealed class StoredBlockValidator : IStoredBlockValidator
    {
        private readonly HashSet<uint> knownIds;

        private StoredBlockValidator(HashSet<uint> knownIds)
        {
            this.knownIds = knownIds;
        }

        public static StoredBlockValidator AllowAnyNonAirId { get; } =
            new StoredBlockValidator(null);

        public static StoredBlockValidator FromManifest(BlockRegistryManifest manifest)
        {
            if (manifest == null)
                return AllowAnyNonAirId;
            if (manifest.IdSchemeVersion != StableBlockId.SchemeVersion)
            {
                throw new InvalidOperationException(
                    $"Registry scheme {manifest.IdSchemeVersion} does not match runtime scheme " +
                    $"{StableBlockId.SchemeVersion}.");
            }

            var ids = new HashSet<uint>();
            for (var i = 0; i < manifest.Entries.Count; i++)
            {
                var id = StableBlockId.Compute(manifest.Entries[i].CanonicalName);
                if (!ids.Add(id))
                    throw new InvalidOperationException("Registry contains duplicate stable IDs.");
            }

            return new StoredBlockValidator(ids);
        }

        public void Validate(uint id, uint state, string context)
        {
            if (id == 0)
                throw new WorldStorageException($"{context}: sparse block entry uses reserved Air ID zero.");
            if (knownIds != null && !knownIds.Contains(id))
            {
                throw new WorldStorageException(
                    $"{context}: stable block ID 0x{id:X8} is not present in the active registry.");
            }

            // No packed-state schema exists in Phase 4. The uint payload remains authoritative.
            _ = state;
        }
    }
}
