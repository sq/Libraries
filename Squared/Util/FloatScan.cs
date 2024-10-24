// derived from musl libc src/internal/floatscan.c, MIT licensed

using System;
using System.Text;
using Squared.Util.Text;

namespace Squared.Util {
    internal static unsafe class FloatScan {
		public const int LDBL_MANT_DIG = 53,
			LDBL_MAX_EXP = 1024,
			LD_B1B_DIG = 2,
			KMAX = 128,
			MASK = (KMAX - 1),
			FLT_MIN_EXP = -125,
			DBL_MIN_EXP = -1021,
			FLT_MANT_DIG = 24,
			DBL_MANT_DIG = 53;
		public static readonly int[] LD_B1B_MAX = new[] { 9007199, 254740991 };
		public static readonly int[] p10s = {
			10,
			100,
			1000,
			10000,
			100000,
			1000000,
			10000000,
			100000000
		};

		public static double scalbn (double arg, int exp) =>
			arg * Math.Pow(2, exp);

		public static double fmodl (double x, double y) =>
			x % y;

		public static double copysignl (double x, double y) {
			int sx = Math.Sign(x), sy = Math.Sign(y);
			if (sx != sy)
				return sx * -1;
			else
				return sx;
		}			

		public static long scanexp(ref AbstractString.Pointer f, bool pok)
		{
			uint c;
			long x;
			long y;
			bool neg = false;

			f = f.Next(out c);
			if (c=='+' || c=='-') {
				neg = (c=='-');
				f = f.Next(out c);
				if (c-'0'>=10U && pok) 
					f -= 1;
			}
			if (c-'0'>=10U) {
				f -= 1;
				return long.MinValue;
			}
			for (x=0; c-'0'<10U && x<int.MaxValue/10; f = f.Next(out c))
				x = 10*x + c-'0';
			for (y=x; c-'0'<10U && y<long.MaxValue/100; f = f.Next(out c))
				y = 10*y + c-'0';
			for (; c-'0'<10U; f = f.Next(out c))
				;
			f -= 1;
			return neg ? -y : y;
		}

		public static double decfloat(ref AbstractString.Pointer f, uint c, int bits, int emin, int sign, bool pok, out bool ok)
		{
			uint *x = stackalloc uint[KMAX];
			var th = LD_B1B_MAX;
			int i, j, k, a, z;
			long lrp=0, dc=0;
			long e10=0;
			int lnz = 0;
			bool gotdig = false, gotrad = false;
			int rp;
			int e2;
			int emax = -emin-bits+3;
			bool denormal = false;
			double y;
			double frac=0;
			double bias=0;

			j=0;
			k=0;

			/* Don't let leading zeros consume buffer space */
			for (; c=='0'; f = f.Next(out c)) gotdig=true;
			if (c=='.') {
				gotrad = true;
				for (f = f.Next(out c); c=='0'; f = f.Next(out c)) {
					gotdig = true;
					lrp--;
				}
			}

			x[0] = 0;
			for (; c-'0'<10U || c=='.'; f = f.Next(out c)) {
				if (c == '.') {
					if (gotrad) break;
					gotrad = true;
					lrp = dc;
				} else if (k < KMAX-3) {
					dc++;
					if (c!='0') lnz = (int)dc;
					if (j != 0) x[k] = x[k]*10 + c-'0';
					else x[k] = c-'0';
					if (++j==9) {
						k++;
						j=0;
					}
					gotdig=true;
				} else {
					dc++;
					if (c!='0') {
						lnz = (KMAX-4)*9;
						x[KMAX-4] |= 1;
					}
				}
			}
			if (!gotrad) lrp=dc;

			if (gotdig && (c|32)=='e') {
				e10 = scanexp(ref f, pok);
				if (e10 == long.MinValue) {
					if (pok) {
						f -= 1;
					} else {
						// shlim(f, 0);
						ok = true;
						return 0;
					}
					e10 = 0;
				}
				lrp += e10;
			} else if (c>=0) {
				f -= 1;
			}
			if (!gotdig) {
				// errno = EINVAL;
				// shlim(f, 0);
				ok = false;
				return 0;
			}

			/* Handle zero specially to avoid nasty special cases later */
			if (x[0] == 0) {
				ok = true;
				return sign * 0.0;
			}

			/* Optimize small integers (w/no exponent) and over/under-flow */
			if (lrp==dc && dc<10 && (bits>30 || x[0]>>bits==0)) {
				ok = true;
				return sign * (double)x[0];
			}

			// HACK: corlib expects this scenario to work if it's all leading zeroes.
			/*
			if (lrp > -emin/2) {
				ok = false;
				return sign * double.MaxValue * double.MaxValue;
			}
			*/

			// Corlib also expects this to work, since it could be leading zeroes.
			/*
			if (lrp < emin-2*LDBL_MANT_DIG) {
				ok = false;
				return sign * double.MinValue * double.MinValue;
			}
			*/

			/* Align incomplete final B1B digit */
			if (j != 0) {
				for (; j<9; j++) x[k]*=10;
				k++;
				j=0;
			}

			a = 0;
			z = k;
			e2 = 0;
			rp = (int)lrp;

			/* Optimize small to mid-size integers (even in exp. notation) */
			if (lnz<9 && lnz<=rp && rp < 18) {
				ok = true;
				if (rp == 9) 
					return sign * (double)x[0];
				if (rp < 9) 
					return sign * (double)x[0] / p10s[8-rp];
				int bitlim = bits-3*(int)(rp-9);
				if (bitlim>30 || x[0]>>bitlim==0)
					return sign * (double)x[0] * p10s[rp-10];
			}

			/* Drop trailing zeros */
			for (; x[z-1] == 0; z--);

			/* Align radix point to B1B digit boundary */
			if ((rp % 9) != 0) {
				int rpm9 = rp>=0 ? rp%9 : rp%9+9;
				int p10 = p10s[8-rpm9];
				uint carry = 0;
				for (k=a; k!=z; k++) {
					uint tmp = (uint)(x[k] % p10);
					x[k] = (uint)(x[k]/p10 + carry);
					carry = (uint)(1000000000/p10 * tmp);
					if (k==a && x[k] == 0) {
						a = (a+1 & MASK);
						rp -= 9;
					}
				}
				if (carry != 0) x[z++] = carry;
				rp += 9-rpm9;
			}

			/* Upscale until desired number of bits are left of radix point */
			while (rp < 9*LD_B1B_DIG || (rp == 9*LD_B1B_DIG && x[a]<th[0])) {
				uint carry = 0;
				e2 -= 29;
				for (k=(z-1 & MASK); ; k=(k-1 & MASK)) {
					ulong tmp = ((ulong)x[k] << 29) + carry;
					if (tmp > 1000000000) {
						carry = (uint)(tmp / 1000000000);
						x[k] = (uint)(tmp % 1000000000);
					} else {
						carry = 0;
						x[k] = (uint)tmp;
					}
					if (k==(z-1 & MASK) && k!=a && (x[k] == 0)) z = k;
					if (k==a) break;
				}
				if (carry != 0) {
					rp += 9;
					a = (a-1 & MASK);
					if (a == z) {
						z = (z-1 & MASK);
						x[z-1 & MASK] |= x[z];
					}
					x[a] = carry;
				}
			}

			/* Downscale until exactly number of bits are left of radix point */
			for (;;) {
				uint carry = 0;
				int sh = 1;
				for (i=0; i<LD_B1B_DIG; i++) {
					k = (a+i & MASK);
					if (k == z || x[k] < th[i]) {
						i=LD_B1B_DIG;
						break;
					}
					if (x[a+i & MASK] > th[i]) break;
				}
				if (i==LD_B1B_DIG && rp==9*LD_B1B_DIG) break;
				/* FIXME: find a way to compute optimal sh */
				if (rp > 9+9*LD_B1B_DIG) sh = 9;
				e2 += sh;
				for (k=a; k!=z; k=(k+1 & MASK)) {
					uint tmp = (uint)(x[k] & (1<<sh)-1);
					x[k] = (x[k]>>sh) + carry;
					carry = (uint)((1000000000>>sh) * tmp);
					if (k==a && (x[k] == 0)) {
						a = (a+1 & MASK);
						i--;
						rp -= 9;
					}
				}
				if (carry != 0) {
					if ((z+1 & MASK) != a) {
						x[z] = carry;
						z = (z+1 & MASK);
					} else x[z-1 & MASK] |= 1;
				}
			}

			/* Assemble desired bits into floating point variable */
			for (y=i=0; i<LD_B1B_DIG; i++) {
				if ((a+i & MASK)==z) x[(z=(z+1 & MASK))-1] = 0;
				y = 1000000000.0 * y + x[a+i & MASK];
			}

			y *= sign;

			/* Limit precision for denormal results */
			if (bits > LDBL_MANT_DIG+e2-emin) {
				bits = LDBL_MANT_DIG+e2-emin;
				if (bits<0) bits=0;
				denormal = true;
			}

			/* Calculate bias term to force rounding, move out lower bits */
			if (bits < LDBL_MANT_DIG) {
				bias = copysignl(scalbn(1, 2*LDBL_MANT_DIG-bits-1), y);
				frac = fmodl(y, scalbn(1, LDBL_MANT_DIG-bits));
				y -= frac;
				y += bias;
			}

			/* Process tail of decimal input so it can affect rounding */
			if ((a+i & MASK) != z) {
				uint t = x[a+i & MASK];
				if (t < 500000000 && ((t != 0) || (a+i+1 & MASK) != z))
					frac += 0.25*sign;
				else if (t > 500000000)
					frac += 0.75*sign;
				else if (t == 500000000) {
					if ((a+i+1 & MASK) == z)
						frac += 0.5*sign;
					else
						frac += 0.75*sign;
				}
				if (LDBL_MANT_DIG-bits >= 2 && (fmodl(frac, 1) == 0))
					frac++;
			}

			y += frac;
			y -= bias;

			ok = true;
			if ((e2+LDBL_MANT_DIG & int.MaxValue) > emax-5) {
				if (Math.Abs(y) >= 2/ double.Epsilon) {
					if (denormal && bits==LDBL_MANT_DIG+e2-emin)
						denormal = false;
					y *= 0.5;
					e2++;
				}

				// FIXME: the BCL test suite expects this to work, so we make it work
				if (e2 + LDBL_MANT_DIG > emax || (denormal && (frac != 0)))
					;
					// ok = false;
					// errno = ERANGE;
			}

			return scalbn(y, e2);
		}

#if FALSE
		static long double hexfloat(FILE *f, int bits, int emin, int sign, int pok)
		{
			uint32_t x = 0;
			long double y = 0;
			long double scale = 1;
			long double bias = 0;
			int gottail = 0, gotrad = 0, gotdig = 0;
			long long rp = 0;
			long long dc = 0;
			long long e2 = 0;
			int d;
			int c;

			c = shgetc(f);

			/* Skip leading zeros */
			for (; c=='0'; c = shgetc(f)) gotdig = 1;

			if (c=='.') {
				gotrad = 1;
				c = shgetc(f);
				/* Count zeros after the radix point before significand */
				for (rp=0; c=='0'; c = shgetc(f), rp--) gotdig = 1;
			}

			for (; c-'0'<10U || (c|32)-'a'<6U || c=='.'; c = shgetc(f)) {
				if (c=='.') {
					if (gotrad) break;
					rp = dc;
					gotrad = 1;
				} else {
					gotdig = 1;
					if (c > '9') d = (c|32)+10-'a';
					else d = c-'0';
					if (dc<8) {
						x = x*16 + d;
					} else if (dc < LDBL_MANT_DIG/4+1) {
						y += d*(scale/=16);
					} else if (d && !gottail) {
						y += 0.5*scale;
						gottail = 1;
					}
					dc++;
				}
			}
			if (!gotdig) {
				f -= 1;
				if (pok) {
					f -= 1;
					if (gotrad) 
						f -= 1;
				} else {
					// shlim(f, 0);
				}
				return sign * 0.0;
			}
			if (!gotrad) rp = dc;
			while (dc<8) {
				x *= 16;
				dc++;
			}
			if ((c|32)=='p') {
				e2 = scanexp(f, pok);
				if (e2 == LLONG_MIN) {
					if (pok) {
						shunget(f);
					} else {
						shlim(f, 0);
						return 0;
					}
					e2 = 0;
				}
			} else {
				shunget(f);
			}
			e2 += 4*rp - 32;

			if (!x) return sign * 0.0;
			if (e2 > -emin) {
				errno = ERANGE;
				return sign * LDBL_MAX * LDBL_MAX;
			}
			if (e2 < emin-2*LDBL_MANT_DIG) {
				errno = ERANGE;
				return sign * LDBL_MIN * LDBL_MIN;
			}

			while (x < 0x80000000) {
				if (y>=0.5) {
					x += x + 1;
					y += y - 1;
				} else {
					x += x;
					y += y;
				}
				e2--;
			}

			if (bits > 32+e2-emin) {
				bits = 32+e2-emin;
				if (bits<0) bits=0;
			}

			if (bits < LDBL_MANT_DIG)
				bias = copysignl(scalbn(1, 32+LDBL_MANT_DIG-bits-1), sign);

			if (bits<32 && y && !(x&1)) x++, y=0;

			y = bias + sign*(long double)x + sign*y;
			y -= bias;

			if (!y) errno = ERANGE;

			return scalbnl(y, e2);
		}
#endif

		public static double __floatscan(ref AbstractString.Pointer f, int prec, bool pok, out bool ok)
		{
			int sign = 1;
			long i;
			int bits;
			int emin;
			uint c;

			switch (prec) {
			case 0:
				bits = FLT_MANT_DIG;
				emin = FLT_MIN_EXP-bits;
				break;
			case 1:
				bits = DBL_MANT_DIG;
				emin = DBL_MIN_EXP-bits;
				break;
			default:
				ok = false;
				return double.NaN;
			}

			while ((f = f.Next(out c)) && char.IsWhiteSpace((char)c));

			if (c=='+' || c=='-') {
				sign -= 2*((c=='-') ? 1 : 0);
				f = f.Next(out c);
			}

			for (i=0; i<8 && (c|32)=="infinity"[(int)i]; i++) {
				if (i<7) f = f.Next(out c);
			}
			if (i==3 || i==8 || (i>3 && pok)) {
				if (i!=8) {
					f -= 1;
					if (pok) for (; i>3; i--) f -= 1;
				}
				ok = true;
				return sign >= 0 
					? double.PositiveInfinity
					: double.NegativeInfinity;
			}
			if (i == 0) for (i=0; i<3 && (c|32)=="nan"[(int)i]; i++)
				if (i<2) f = f.Next(out c);
			if (i==3) {
				if ((f = f.Next(out c)) && (c != '(')) {
					f -= 1;
					ok = true;
					return double.NaN;
				}
				for (i=1; ; i++) {
					f = f.Next(out c);
					if (c-'0'<10U || c-'A'<26U || c-'a'<26U || c=='_')
						continue;
					if (c==')') {
						ok = true;
						return double.NaN;
					}
					f -= 1;
					if (!pok) {
						// errno = EINVAL;
						// shlim(f, 0);
						ok = false;
						return double.NaN;
					}
					while (i-- != 0) f -= 1;
					ok = true;
					return double.NaN;
				}

				ok = true;
				return double.NaN;
			}

			if (i != 0) {
				f -= 1;
				// errno = EINVAL;
				// shlim(f, 0);
				ok = false;
				return 0;
			}

			if (c=='0') {
				f = f.Next(out c);
				if ((c | 32) == 'x') {
					// FIXME
					ok = false;
					return 0;
					// return hexfloat(f, bits, emin, sign, pok);
				}
				f -= 1;
				c = '0';
			}

			return decfloat(ref f, c, bits, emin, sign, pok, out ok);
		}
	}
}