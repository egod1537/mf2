using Unity.Mathematics;

namespace MF2.World.Rendering
{
    /// <summary>Independent readiness/version gates for one chunk projection.</summary>
    public struct ChunkReadiness
    {
        public int3 Coordinate { get; private set; }
        public ulong DataVersion { get; private set; }
        public ulong CollisionVersion { get; private set; }
        public ulong VisualVersion { get; private set; }

        public bool HasData => DataVersion != 0;
        public bool IsCollisionReady => HasData && CollisionVersion == DataVersion;
        public bool IsVisualReady => HasData && VisualVersion == DataVersion;
        public bool IsActive => IsCollisionReady && IsVisualReady;

        public void MarkDataReady(int3 coordinate, ulong version)
        {
            if (version == 0)
                return;
            if (HasData && (Coordinate.x != coordinate.x || Coordinate.y != coordinate.y ||
                            Coordinate.z != coordinate.z || version < DataVersion))
            {
                return;
            }

            Coordinate = coordinate;
            if (version == DataVersion)
                return;
            DataVersion = version;
            CollisionVersion = 0;
            VisualVersion = 0;
        }

        public bool TryMarkCollisionReady(ulong builtVersion)
        {
            if (!HasData || builtVersion != DataVersion)
                return false;
            CollisionVersion = builtVersion;
            return true;
        }

        public bool TryMarkVisualReady(ulong builtVersion)
        {
            if (!HasData || builtVersion != DataVersion)
                return false;
            VisualVersion = builtVersion;
            return true;
        }

        public void Invalidate()
        {
            CollisionVersion = 0;
            VisualVersion = 0;
        }
    }
}
