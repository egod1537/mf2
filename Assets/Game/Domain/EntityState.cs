using System;

namespace MF2.Game.Domain
{
    /// <summary>
    /// Read-only snapshot source. Implementations return value copies, never mutable registry data.
    /// </summary>
    public interface IEntityStateReader
    {
        event Action<PlayerStateChange> PlayerStateChanged;

        bool TryGetPlayer(PlayerId playerId, out PlayerModel snapshot);
    }
}
