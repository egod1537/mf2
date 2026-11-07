using System;
using MF2.Game.Domain;
using UnityEngine;

namespace MF2.Entities.Player
{
    /// <summary>Immutable per-spawn identity and pose; it carries no service or connection object.</summary>
    public readonly struct PlayerSpawnContext
    {
        public PlayerSpawnContext(
            SessionId sessionId,
            PlayerId playerId,
            int ownerConnectionId,
            Vector3 initialPosition,
            Quaternion initialRotation)
        {
            if (sessionId.Value == Guid.Empty)
                throw new ArgumentException("A spawn requires a valid session id.", nameof(sessionId));
            if (playerId.Value == Guid.Empty)
                throw new ArgumentException("A spawn requires a valid player id.", nameof(playerId));
            if (ownerConnectionId < 0)
                throw new ArgumentOutOfRangeException(nameof(ownerConnectionId));

            SessionId = sessionId;
            PlayerId = playerId;
            OwnerConnectionId = ownerConnectionId;
            InitialPosition = initialPosition;
            InitialRotation = initialRotation;
        }

        public SessionId SessionId { get; }
        public PlayerId PlayerId { get; }
        public int OwnerConnectionId { get; }
        public Vector3 InitialPosition { get; }
        public Quaternion InitialRotation { get; }
    }
}
