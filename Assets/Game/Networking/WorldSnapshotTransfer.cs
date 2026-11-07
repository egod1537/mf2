using System;
using FishNet.Broadcast;

namespace MF2.Game.Networking
{
    public struct WorldSnapshotBeginBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public int TransferId;
        public int TotalBlocks;
        public int PartCount;
    }

    public struct WorldSnapshotPartBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public int TransferId;
        public int PartIndex;
        public ReplicatedBlock[] Blocks;
    }

    public struct WorldSnapshotEndBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public int TransferId;
    }

    /// <summary>
    /// Snapshot parts stay below Tugboat's 1282-byte MTU. ReplicatedBlock is 20 bytes and the
    /// fixed FishNet/broadcast envelope remains below the reserved 256-byte margin.
    /// </summary>
    public static class WorldSnapshotTransferProtocol
    {
        public const int MaximumBlocksPerPart = 48;
        // A delta uses the same ReplicatedBlock payload and FishNet broadcast envelope as one
        // snapshot part. Larger authoritative ticks fall back to a bounded full snapshot so a
        // single broadcast never relies on transport fragmentation or partial same-tick apply.
        public const int MaximumDeltaBlocksPerMessage = MaximumBlocksPerPart;
        public const int MaximumSnapshotBlocks = 1_000_000;
        public const int EstimatedFixedEnvelopeBytes = 256;
        public const int ReplicatedBlockBytes = 20;
        public const int MaximumEstimatedPartBytes =
            EstimatedFixedEnvelopeBytes + MaximumBlocksPerPart * ReplicatedBlockBytes;

        public static int GetPartCount(int totalBlocks)
        {
            if (totalBlocks < 0 || totalBlocks > MaximumSnapshotBlocks)
                throw new ArgumentOutOfRangeException(nameof(totalBlocks));
            return totalBlocks == 0
                ? 0
                : checked((totalBlocks + MaximumBlocksPerPart - 1) / MaximumBlocksPerPart);
        }

        public static ReplicatedBlock[] CopyPart(ReplicatedBlock[] source, int partIndex)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var partCount = GetPartCount(source.Length);
            if (partIndex < 0 || partIndex >= partCount)
                throw new ArgumentOutOfRangeException(nameof(partIndex));

            var offset = checked(partIndex * MaximumBlocksPerPart);
            var count = Math.Min(MaximumBlocksPerPart, source.Length - offset);
            var result = new ReplicatedBlock[count];
            Array.Copy(source, offset, result, 0, count);
            return result;
        }

        public static bool RequiresSnapshotForDelta(int blockCount)
        {
            if (blockCount < 0 || blockCount > MaximumSnapshotBlocks)
                throw new ArgumentOutOfRangeException(nameof(blockCount));
            return blockCount > MaximumDeltaBlocksPerMessage;
        }
    }

    /// <summary>Stages one bounded transfer and publishes blocks only after a complete End.</summary>
    public sealed class WorldSnapshotTransferAssembler
    {
        private WorldSnapshotBeginBroadcast begin;
        private ReplicatedBlock[][] parts;
        private bool[] received;
        private int receivedParts;

        public bool IsActive => parts != null;
        public Guid SessionId => begin.SessionId;
        public long Generation => begin.Generation;
        public long TickIndex => begin.TickIndex;

        public bool Begin(WorldSnapshotBeginBroadcast message)
        {
            Reset();
            if (message.SessionId == Guid.Empty || message.Generation <= 0 ||
                message.TickIndex < 0 || message.TransferId <= 0 ||
                message.TotalBlocks < 0 ||
                message.TotalBlocks > WorldSnapshotTransferProtocol.MaximumSnapshotBlocks)
            {
                return false;
            }

            int expectedParts;
            try
            {
                expectedParts = WorldSnapshotTransferProtocol.GetPartCount(message.TotalBlocks);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            if (message.PartCount != expectedParts)
                return false;

            begin = message;
            parts = new ReplicatedBlock[message.PartCount][];
            received = new bool[message.PartCount];
            return true;
        }

        public bool AddPart(WorldSnapshotPartBroadcast message)
        {
            if (!Matches(message.SessionId, message.Generation, message.TickIndex, message.TransferId) ||
                message.PartIndex < 0 || message.PartIndex >= parts.Length ||
                received[message.PartIndex] || message.Blocks == null)
            {
                return false;
            }

            var expectedLength = message.PartIndex == parts.Length - 1
                ? begin.TotalBlocks - message.PartIndex * WorldSnapshotTransferProtocol.MaximumBlocksPerPart
                : WorldSnapshotTransferProtocol.MaximumBlocksPerPart;
            if (message.Blocks.Length != expectedLength)
            {
                Reset();
                return false;
            }

            var copy = new ReplicatedBlock[message.Blocks.Length];
            Array.Copy(message.Blocks, copy, copy.Length);
            parts[message.PartIndex] = copy;
            received[message.PartIndex] = true;
            receivedParts++;
            return true;
        }

        public bool TryComplete(
            WorldSnapshotEndBroadcast message,
            out WorldSnapshotBeginBroadcast metadata,
            out ReplicatedBlock[] blocks)
        {
            metadata = default;
            blocks = null;
            if (!Matches(message.SessionId, message.Generation, message.TickIndex, message.TransferId) ||
                receivedParts != parts.Length)
            {
                return false;
            }

            var result = new ReplicatedBlock[begin.TotalBlocks];
            var offset = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null)
                    return false;
                Array.Copy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            metadata = begin;
            blocks = result;
            Reset();
            return true;
        }

        public void Reset()
        {
            begin = default;
            parts = null;
            received = null;
            receivedParts = 0;
        }

        private bool Matches(Guid sessionId, long generation, long tickIndex, int transferId) =>
            parts != null && sessionId == begin.SessionId && generation == begin.Generation &&
            tickIndex == begin.TickIndex && transferId == begin.TransferId;
    }
}
