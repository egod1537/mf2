using System;
using System.Collections.Generic;

namespace MF2.Game.Domain
{
    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public PlayerId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("A player id cannot be empty.", nameof(value));
            Value = value;
        }

        public Guid Value { get; }

        public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);
        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);
        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }

    public readonly struct SessionId : IEquatable<SessionId>
    {
        public SessionId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("A session id cannot be empty.", nameof(value));
            Value = value;
        }

        public Guid Value { get; }

        public bool Equals(SessionId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is SessionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("D");

        public static bool operator ==(SessionId left, SessionId right) => left.Equals(right);
        public static bool operator !=(SessionId left, SessionId right) => !left.Equals(right);
    }

    public sealed class PlayerRegistry : IEntityStateReader
    {
        private readonly Dictionary<PlayerId, PlayerModel> players =
            new Dictionary<PlayerId, PlayerModel>();
        private readonly Dictionary<PlayerId, PlayerStateChange> pendingChanges =
            new Dictionary<PlayerId, PlayerStateChange>();

        public event Action<PlayerStateChange> PlayerStateChanged;

        public int Count => players.Count;

        public bool TryAdd(PlayerId playerId) => TryAdd(playerId, out _);

        public bool TryAdd(PlayerId playerId, out PlayerModel model)
        {
            if (players.TryGetValue(playerId, out model))
                return false;

            model = new PlayerModel(playerId);
            players.Add(playerId, model);
            Publish(PlayerStateChange.Upserted(model));
            return true;
        }

        public PlayerModel Restore(PlayerModel snapshot)
        {
            if (snapshot.Id.Value == Guid.Empty)
                throw new ArgumentException("A restored player requires a valid id.", nameof(snapshot));

            players[snapshot.Id] = snapshot;
            Publish(PlayerStateChange.Upserted(snapshot));
            return snapshot;
        }

        internal void ValidateSnapshots(IReadOnlyList<PlayerModel> snapshots)
        {
            if (snapshots == null)
                throw new ArgumentNullException(nameof(snapshots));

            var ids = new HashSet<PlayerId>();
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot.Id.Value == Guid.Empty || !ids.Add(snapshot.Id))
                {
                    throw new ArgumentException(
                        "Restored players require unique, valid ids.",
                        nameof(snapshots));
                }
            }
        }

        internal void ReplaceWith(IReadOnlyList<PlayerModel> snapshots)
        {
            ValidateSnapshots(snapshots);
            players.Clear();
            pendingChanges.Clear();
            for (var i = 0; i < snapshots.Count; i++)
                players.Add(snapshots[i].Id, snapshots[i]);
        }

        public bool TryGet(PlayerId playerId, out PlayerModel model) =>
            players.TryGetValue(playerId, out model);

        public bool TryGetPlayer(PlayerId playerId, out PlayerModel model) =>
            TryGet(playerId, out model);

        public bool Remove(PlayerId playerId)
        {
            if (!players.Remove(playerId, out var model))
                return false;
            Publish(PlayerStateChange.Removed(
                playerId,
                checked(model.Revision + 1)));
            return true;
        }

        internal bool TrySetHealth(PlayerId playerId, int health)
        {
            if (!players.TryGetValue(playerId, out var model))
                return false;
            if (health == model.Health)
                return true;
            var updated = model.WithHealth(health);
            players[playerId] = updated;
            Publish(PlayerStateChange.Upserted(updated));
            return true;
        }

        public bool Contains(PlayerId playerId) => players.ContainsKey(playerId);

        public void Clear()
        {
            if (players.Count == 0)
                return;
            var snapshots = CaptureSnapshots();
            players.Clear();
            for (var i = 0; i < snapshots.Length; i++)
            {
                var snapshot = snapshots[i];
                Publish(PlayerStateChange.Removed(
                    snapshot.Id,
                    checked(snapshot.Revision + 1)));
            }
        }

        public PlayerId[] CapturePlayerIds()
        {
            var result = new PlayerId[players.Count];
            players.Keys.CopyTo(result, 0);
            Array.Sort(result);
            return result;
        }

        public PlayerModel[] CaptureSnapshots()
        {
            var result = new PlayerModel[players.Count];
            players.Values.CopyTo(result, 0);
            Array.Sort(result, (left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        public PlayerStateChange[] CaptureChanges()
        {
            if (pendingChanges.Count == 0)
                return Array.Empty<PlayerStateChange>();
            var ids = new PlayerId[pendingChanges.Count];
            pendingChanges.Keys.CopyTo(ids, 0);
            Array.Sort(ids);
            var result = new PlayerStateChange[ids.Length];
            for (var i = 0; i < ids.Length; i++)
                result[i] = pendingChanges[ids[i]];
            pendingChanges.Clear();
            return result;
        }

        private void Publish(in PlayerStateChange change)
        {
            pendingChanges[change.PlayerId] = change;
            PlayerStateChanged?.Invoke(change);
        }
    }
}
