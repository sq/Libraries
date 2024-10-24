// derived from musl libc src/internal/intscan.c, MIT licensed

using System;
using System.Text;
using Squared.Util.Text;

namespace Squared.Util {
    internal static unsafe class IntScan {
		/* Lookup table for digit values. -1==255>=36 -> invalid */
		public static readonly sbyte[] table = { 
			-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,-1,-1,-1,-1,-1,-1,
			-1,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,
			25,26,27,28,29,30,31,32,33,34,35,-1,-1,-1,-1,-1,
			-1,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,
			25,26,27,28,29,30,31,32,33,34,35,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
			-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
		};

		public static unsafe ulong __intscan(ref AbstractString.Pointer f, uint @base, int pok, ulong lim, byte *pTable, out int neg, out bool ok)
		{
			neg=0;
			byte* val = pTable+1;
			uint c = 0;
			uint x;
			ulong y;
			ok = true;

			if (@base > 36 || @base == 1) {
				// errno = EINVAL;
				ok = false;
				return 0;
			}
			while (true) {
				f = f.Next(out c);
				if (!f || !Text.Unicode.IsWhiteSpace(c))
					break;
			}
			if (c=='+' || c=='-') {
				neg = -(c=='-' ? 1 : 0);
				f = f.Next(out c);
			}
			if ((@base == 0 || @base == 16) && c=='0') {
				f = f.Next(out c);
				if ((c|32)=='x') {
					f = f.Next(out c);
					if (val[c]>=16) {
						ok = false;
						return 0;
					}
					@base = 16;
				} else if (@base == 0) {
					@base = 8;
				}
			} else {
				if (@base == 0) @base = 10;
				if (val[c] >= @base) {
					ok = false;
					return 0;
				}
			}
			if (@base == 10) {
				for (x=0; c-'0'<10U && x<=UInt32.MaxValue/10-1; f = f.Next(out c))
					x = x*10 + (c-'0');
				for (y=x; c-'0'<10U && y<=UInt64.MaxValue/10 && 10*y<=UInt64.MaxValue-(c-'0'); f = f.Next(out c))
					y = y*10 + (c-'0');
				if (c-'0'>=10U) goto done;
			} else if ((@base & @base-1) == 0) {
				// FIXME
				ok = false;
				return 0;
				/*
				char bs = "\0\1\2\4\7\3\6\5"[(0x17*@base)>>5&7];
				for (x=0; val[c]<@base && x<=UINT_MAX/32; c=shgetc(f))
					x = x<<bs | val[c];
				for (y=x; val[c]<@base && y<=ULLONG_MAX>>bs; c=shgetc(f))
					y = y<<bs | val[c];
				*/
			} else {
				for (x=0; val[c]<@base && x<=UInt32.MaxValue/36-1; f = f.Next(out c))
					x = x*@base + val[c];
				for (y=x; val[c]<@base && y<=UInt64.MaxValue/@base && @base*y<=UInt64.MaxValue-val[c]; f = f.Next(out c))
					y = y*@base + val[c];
			}
			if (val[c]<@base) {
				ok = false;
				y = lim;
				if ((lim&1) != 0) neg = 0;
			}
		done:
			if (y>=lim) {
				if ((lim&1) == 0 && neg == 0) {
					ok = false;
					return lim-1;
				} else if (y>lim) {
					ok = false;
					return lim;
				}
			}
			if (neg != 0)
				return unchecked((ulong)(-(long)y));
			else
				return y;
		}    
	}
}
