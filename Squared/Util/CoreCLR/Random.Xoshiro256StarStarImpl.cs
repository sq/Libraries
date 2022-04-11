// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Squared.CoreCLR
{
    /// <summary>
    /// Provides an implementation of the xoshiro256** algorithm. This implementation is used
    /// on 64-bit when no seed is specified and an instance of the base Random class is constructed.
    /// As such, we are free to implement however we see fit, without back compat concerns around
    /// the sequence of numbers generated or what methods call what other methods.
    /// </summary>
    public struct Xoshiro {
        // NextUInt64 is based on the algorithm from http://prng.di.unimi.it/xoshiro256starstar.c:
        //
        //     Written in 2018 by David Blackman and Sebastiano Vigna (vigna@acm.org)
        //
        //     To the extent possible under law, the author has dedicated all copyright
        //     and related and neighboring rights to this software to the public domain
        //     worldwide. This software is distributed without any warranty.
        //
        //     See <http://creativecommons.org/publicdomain/zero/1.0/>.

        private bool _isInitialized;
        private ulong _s0, _s1, _s2, _s3;
        private static RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public Xoshiro (in Xoshiro copyFrom) {
            _isInitialized = copyFrom._isInitialized;
            _s0 = copyFrom._s0;
            _s1 = copyFrom._s1;
            _s2 = copyFrom._s2;
            _s3 = copyFrom._s3;
        }

        public unsafe Xoshiro (ulong[] state) {
            _isInitialized = true;
            if (state == null) {
                byte[] buf = new byte[sizeof(ulong) * 4];
                lock (_rng) {
                    fixed (byte * pBufB = buf) {
                        var ptr = (ulong*)pBufB;
                        do
                        {
                            // FIXME: Use GetNonZeroBytes and remove the loop?
                            _rng.GetBytes(buf);
                            _s0 = ptr[0];
                            _s1 = ptr[1];
                            _s2 = ptr[2];
                            _s3 = ptr[3];
                        }
                        while ((_s0 | _s1 | _s2 | _s3) == 0); // at least one value must be non-zero
                    }
                }
                return;
            }
            else if (state.Length != 4)
                throw new ArgumentOutOfRangeException(nameof(state));
            _s0 = state[0];
            _s1 = state[1];
            _s2 = state[2];
            _s3 = state[3];
        }

        public void Save (ulong[] result) {
            if (!_isInitialized)
                throw new InvalidOperationException("Not initialized");
            else if (result == null)
                throw new ArgumentNullException(nameof(result));
            else if (result.Length != 4)
                throw new ArgumentOutOfRangeException(nameof(result));
            result[0] = _s0;
            result[1] = _s1;
            result[2] = _s2;
            result[3] = _s3;
        }

        /// <summary>Produces a value in the range [0, uint.MaxValue].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // small-ish hot path used by very few call sites
        public uint NextUInt32() => (uint)(NextUInt64() >> 32);

        /// <summary>Produces a value in the range [0, ulong.MaxValue].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // small-ish hot path used by a handful of "next" methods
        public ulong NextUInt64()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Not initialized");

            ulong s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;

            ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
            ulong t = s1 << 17;

            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;

            s2 ^= t;
            s3 = BitOperations.RotateLeft(s3, 45);

            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;

            return result;
        }

        public int Next()
        {
            while (true)
            {
                // Get top 31 bits to get a value in the range [0, int.MaxValue], but try again
                // if the value is actually int.MaxValue, as the method is defined to return a value
                // in the range [0, int.MaxValue).
                ulong result = NextUInt64() >> 33;
                if (result != int.MaxValue)
                {
                    return (int)result;
                }
            }
        }

        public int Next(int maxValue)
        {
            if (maxValue > 1)
            {
                // Narrow down to the smallest range [0, 2^bits] that contains maxValue.
                // Then repeatedly generate a value in that outer range until we get one within the inner range.
                int bits = BitOperations.Log2Ceiling((uint)maxValue);
                while (true)
                {
                    ulong result = NextUInt64() >> (sizeof(ulong) * 8 - bits);
                    if (result < (uint)maxValue)
                    {
                        return (int)result;
                    }
                }
            }

            Debug.Assert(maxValue == 0 || maxValue == 1);
            return 0;
        }

        public int Next(int minValue, int maxValue)
        {
            // HACK: By default this will just return a huge garbage number. Good job, BCL
            if (maxValue <= minValue)
                return minValue;

            ulong exclusiveRange = (ulong)(maxValue - minValue);

            if (exclusiveRange > 1)
            {
                // Narrow down to the smallest range [0, 2^bits] that contains maxValue.
                // Then repeatedly generate a value in that outer range until we get one within the inner range.
                int bits = BitOperations.Log2Ceiling(exclusiveRange);
                while (true)
                {
                    ulong result = NextUInt64() >> (sizeof(ulong) * 8 - bits);
                    if (result < exclusiveRange)
                    {
                        return (int)result + minValue;
                    }
                }
            }

            Debug.Assert(minValue == maxValue || minValue + 1 == maxValue);
            return minValue;
        }

        public long NextInt64()
        {
            while (true)
            {
                // Get top 63 bits to get a value in the range [0, long.MaxValue], but try again
                // if the value is actually long.MaxValue, as the method is defined to return a value
                // in the range [0, long.MaxValue).
                ulong result = NextUInt64() >> 1;
                if (result != long.MaxValue)
                {
                    return (long)result;
                }
            }
        }

        public long NextInt64(long maxValue)
        {
            if (maxValue > 1)
            {
                // Narrow down to the smallest range [0, 2^bits] that contains maxValue.
                // Then repeatedly generate a value in that outer range until we get one within the inner range.
                int bits = BitOperations.Log2Ceiling((ulong)maxValue);
                while (true)
                {
                    ulong result = NextUInt64() >> (sizeof(ulong) * 8 - bits);
                    if (result < (ulong)maxValue)
                    {
                        return (long)result;
                    }
                }
            }

            Debug.Assert(maxValue == 0 || maxValue == 1);
            return 0;
        }

        public long NextInt64(long minValue, long maxValue)
        {
            ulong exclusiveRange = (ulong)(maxValue - minValue);

            if (exclusiveRange > 1)
            {
                // Narrow down to the smallest range [0, 2^bits] that contains maxValue.
                // Then repeatedly generate a value in that outer range until we get one within the inner range.
                int bits = BitOperations.Log2Ceiling(exclusiveRange);
                while (true)
                {
                    ulong result = NextUInt64() >> (sizeof(ulong) * 8 - bits);
                    if (result < exclusiveRange)
                    {
                        return (long)result + minValue;
                    }
                }
            }

            Debug.Assert(minValue == maxValue || minValue + 1 == maxValue);
            return minValue;
        }

        public void NextBytes(byte[] buffer) => NextBytes(new ArraySegment<byte>(buffer));

        // FIXME
        public unsafe void NextBytes(ArraySegment<byte> buffer)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Not initialized");

            ulong s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;

            fixed (byte * _pBuffer = buffer.Array) {
                var pBuffer = _pBuffer + buffer.Offset;

                while (buffer.Count >= sizeof(ulong))
                {
                    *((ulong*)pBuffer) = BitOperations.RotateLeft(s1 * 5, 7) * 9;

                    // Update PRNG state.
                    ulong t = s1 << 17;
                    s2 ^= s0;
                    s3 ^= s1;
                    s1 ^= s2;
                    s0 ^= s3;
                    s2 ^= t;
                    s3 = BitOperations.RotateLeft(s3, 45);

                    pBuffer += sizeof(ulong);
                    buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + sizeof(ulong), buffer.Count - sizeof(ulong));
                }
            }

            if (buffer.Count > 0)
            {
                ulong next = BitOperations.RotateLeft(s1 * 5, 7) * 9;
                byte* remainingBytes = (byte*)&next;
                Debug.Assert(buffer.Count < sizeof(ulong));
                for (int i = 0; i < buffer.Count; i++)
                {
                    buffer.Array[buffer.Offset + i] = remainingBytes[i];
                }

                // Update PRNG state.
                ulong t = s1 << 17;
                s2 ^= s0;
                s3 ^= s1;
                s1 ^= s2;
                s0 ^= s3;
                s2 ^= t;
                s3 = BitOperations.RotateLeft(s3, 45);
            }

            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
        }

        public double NextDouble() =>
            // As described in http://prng.di.unimi.it/:
            // "A standard double (64-bit) floating-point number in IEEE floating point format has 52 bits of significand,
            //  plus an implicit bit at the left of the significand. Thus, the representation can actually store numbers with
            //  53 significant binary digits. Because of this fact, in C99 a 64-bit unsigned integer x should be converted to
            //  a 64-bit double using the expression
            //  (x >> 11) * 0x1.0p-53"
            (NextUInt64() >> 11) * (1.0 / (1ul << 53));

        public float NextSingle() =>
            // Same as above, but with 24 bits instead of 53.
            (NextUInt64() >> 40) * (1.0f / (1u << 24));
    }
}
