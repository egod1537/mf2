using System;

namespace MF2.World.Registry
{
    /// <summary>
    /// Stable block ID scheme v1.
    ///
    /// Input is strict lowercase ASCII namespace:path text, so normalization is identity.
    /// Bytes are the exact UTF-8 bytes of that canonical text. Hash is xxHash32 seed zero.
    /// </summary>
    public static class StableBlockId
    {
        public const int SchemeVersion = 1;
        public const uint Seed = 0;
        public const string PersistenceScheme = "xxhash32-utf8-seed0-v1";
        public const string ReservedAirName = "minecraft:air";
        public const int MaxCanonicalNameBytes = 125;

        private const uint Prime1 = 0x9E3779B1U;
        private const uint Prime2 = 0x85EBCA77U;
        private const uint Prime3 = 0xC2B2AE3DU;
        private const uint Prime4 = 0x27D4EB2FU;
        private const uint Prime5 = 0x165667B1U;

        public static uint Compute(string canonicalName)
        {
            ValidateCanonicalName(canonicalName);
            if (string.Equals(canonicalName, ReservedAirName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{ReservedAirName}' is represented only by reserved ID zero.",
                    nameof(canonicalName));
            }

            return ComputeAsciiBytesUnchecked(canonicalName);
        }

        public static void ValidateCanonicalName(string canonicalName)
        {
            if (string.IsNullOrEmpty(canonicalName))
                throw new ArgumentException("A canonical block name is required.", nameof(canonicalName));
            if (canonicalName.Length > MaxCanonicalNameBytes)
            {
                throw new ArgumentException(
                    $"Canonical names are limited to {MaxCanonicalNameBytes} UTF-8 bytes.",
                    nameof(canonicalName));
            }

            var colon = canonicalName.IndexOf(':');
            if (colon <= 0 || colon == canonicalName.Length - 1 ||
                canonicalName.IndexOf(':', colon + 1) >= 0)
            {
                throw new ArgumentException(
                    "Canonical block names must contain exactly one non-edge ':' separator.",
                    nameof(canonicalName));
            }

            for (var i = 0; i < canonicalName.Length; i++)
            {
                var character = canonicalName[i];
                if (i == colon)
                    continue;

                var valid = character >= 'a' && character <= 'z' ||
                            character >= '0' && character <= '9' ||
                            character == '_' || character == '-' || character == '.' ||
                            i > colon && character == '/';
                if (!valid)
                {
                    throw new ArgumentException(
                        $"Canonical block name contains invalid character '{character}' at {i}. " +
                        "Only lowercase ASCII resource-location characters are accepted.",
                        nameof(canonicalName));
                }
            }
        }

        internal static uint ComputeAsciiBytesUnchecked(string value)
        {
            unchecked
            {
                var length = value.Length;
                var index = 0;
                uint hash;

                if (length >= 16)
                {
                    var v1 = Seed + Prime1 + Prime2;
                    var v2 = Seed + Prime2;
                    var v3 = Seed;
                    var v4 = Seed - Prime1;
                    var limit = length - 16;

                    do
                    {
                        v1 = Round(v1, ReadUInt32(value, index));
                        index += 4;
                        v2 = Round(v2, ReadUInt32(value, index));
                        index += 4;
                        v3 = Round(v3, ReadUInt32(value, index));
                        index += 4;
                        v4 = Round(v4, ReadUInt32(value, index));
                        index += 4;
                    } while (index <= limit);

                    hash = RotateLeft(v1, 1) +
                           RotateLeft(v2, 7) +
                           RotateLeft(v3, 12) +
                           RotateLeft(v4, 18);
                }
                else
                {
                    hash = Seed + Prime5;
                }

                hash += (uint)length;
                while (index <= length - 4)
                {
                    hash += ReadUInt32(value, index) * Prime3;
                    hash = RotateLeft(hash, 17) * Prime4;
                    index += 4;
                }

                while (index < length)
                {
                    hash += (byte)value[index] * Prime5;
                    hash = RotateLeft(hash, 11) * Prime1;
                    index++;
                }

                hash ^= hash >> 15;
                hash *= Prime2;
                hash ^= hash >> 13;
                hash *= Prime3;
                hash ^= hash >> 16;
                return hash;
            }
        }

        private static uint Round(uint accumulator, uint input)
        {
            unchecked
            {
                accumulator += input * Prime2;
                accumulator = RotateLeft(accumulator, 13);
                accumulator *= Prime1;
                return accumulator;
            }
        }

        private static uint ReadUInt32(string value, int index)
        {
            return (byte)value[index] |
                   (uint)(byte)value[index + 1] << 8 |
                   (uint)(byte)value[index + 2] << 16 |
                   (uint)(byte)value[index + 3] << 24;
        }

        private static uint RotateLeft(uint value, int count)
        {
            return value << count | value >> (32 - count);
        }
    }
}
