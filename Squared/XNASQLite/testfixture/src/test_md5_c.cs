using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using Bitmask = System.UInt64;
using u32 = System.UInt32;
using u64 = System.UInt64;

namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_Obj = tcl.lang.TclObject;
  using Tcl_CmdInfo = tcl.lang.Command;

  using sqlite3_value = csSQLite.MemRef;

  public partial class csSQLite
  {
    /*
    ** SQLite uses this code for testing only.  It is not a part of
    ** the SQLite library.  This file implements two new TCL commands
    ** "md5" and "md5file" that compute md5 checksums on arbitrary text
    ** and on complete files.  These commands are used by the "testfixture"
    ** program to help verify the correct operation of the SQLite library.
    **
    ** The original use of these TCL commands was to test the ROLLBACK
    ** feature of SQLite.  First compute the MD5-checksum of the database.
    ** Then make some changes but rollback the changes rather than commit
    ** them.  Compute a second MD5-checksum of the file and verify that the
    ** two checksums are the same.  Such is the original use of this code.
    ** New uses may have been added since this comment was written.
    **
    ** $Id: test_md5.c,v 1.10 2009/02/03 19:52:59 shane Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    /*
    * This code implements the MD5 message-digest algorithm.
    * The algorithm is due to Ron Rivest.  This code was
    * written by Colin Plumb in 1993, no copyright is claimed.
    * This code is in the public domain; do with it what you wish.
    *
    * Equivalent code is available from RSA Data Security, Inc.
    * This code has been tested against that, and is equivalent,
    * except that you don't need to include two pages of legalese
    * with every copy.
    *
    * To compute the message digest of a chunk of bytes, declare an
    * MD5Context structure, pass it to MD5Init, call MD5Update as
    * needed on buffers full of bytes, and then call MD5Final, which
    * will fill a supplied 16-byte array with the digest.
    */
    //#include <tcl.h>
    //#include <string.h>
    //#include "sqlite3.h"

    /*
    * If compiled on a machine that doesn't have a 32-bit integer,
    * you just set "u32" to the appropriate datatype for an
    * unsigned 32-bit integer.  For example:
    *
    *       cc -Du32='unsigned long' md5.c
    *
    */
    //#if !u32
    //#  define u32 unsigned int
    //#endif

    class MD5Context
    {
      public bool isInit;
      public u32[] buf = new u32[4];
      public u32[] bits = new u32[2];
      public u32[] _in = new u32[64];
      public MemRef _Mem;
    };
    //typedef struct Context MD5Context;

    /*
    * Note: this code is harmless on little-endian machines.
    */
    //static void byteReverse (byte[] buf, unsigned longs){
    //
    // NOOP on Windows
    //
    //u32 t;
    //      do {
    //              t = (u32)((unsigned)buf[3]<<8 | buf[2]) << 16 |
    //                          ((unsigned)buf[1]<<8 | buf[0]);
    //              (u32 )buf = t;
    //              buf += 4;
    //      } while (--longs);
    //}

    ///* The four core functions - F1 is optimized somewhat */

    delegate u32 dxF1234( u32 x, u32 y, u32 z );

    //* #define F1(x, y, z) (x & y | ~x & z) */
    //#define F1(x, y, z) (z ^ (x & (y ^ z)))
    static u32 F1( u32 x, u32 y, u32 z ) { return ( z ^ ( x & ( y ^ z ) ) ); }

    //#define F2(x, y, z) F1(z, x, y)
    static u32 F2( u32 x, u32 y, u32 z ) { return F1( z, x, y ); }

    //#define F3(x, y, z) (x ^ y ^ z)
    static u32 F3( u32 x, u32 y, u32 z ) { return ( x ^ y ^ z ); }

    //#define F4(x, y, z) (y ^ (x | ~z))
    static u32 F4( u32 x, u32 y, u32 z ) { return ( y ^ ( x | ~z ) ); }

    ///* This is the central step in the MD5 algorithm. */
    //#define MD5STEP(f, w, x, y, z, data, s) \
    //        ( w += f(x, y, z) + data,  w = w<<s | w>>(32-s),  w += x )
    static void MD5STEP( dxF1234 f, ref u32 w, u32 x, u32 y, u32 z, u32 data, byte s )
    {
      w += f( x, y, z ) + data;
      w = w << s | w >> ( 32 - s );
      w += x;
    }

    /*
    * The core of the MD5 algorithm, this alters an existing MD5 hash to
    * reflect the addition of 16 longwords of new data.  MD5Update blocks
    * the data and converts bytes into longwords for this routine.
    */
    static void MD5Transform( u32[] buf, u32[] _in )
    {
      u32 a, b, c, d;

      a = buf[0];
      b = buf[1];
      c = buf[2];
      d = buf[3];

      MD5STEP( F1, ref a, b, c, d, _in[0] + 0xd76aa478, 7 );
      MD5STEP( F1, ref d, a, b, c, _in[1] + 0xe8c7b756, 12 );
      MD5STEP( F1, ref c, d, a, b, _in[2] + 0x242070db, 17 );
      MD5STEP( F1, ref b, c, d, a, _in[3] + 0xc1bdceee, 22 );
      MD5STEP( F1, ref a, b, c, d, _in[4] + 0xf57c0faf, 7 );
      MD5STEP( F1, ref d, a, b, c, _in[5] + 0x4787c62a, 12 );
      MD5STEP( F1, ref c, d, a, b, _in[6] + 0xa8304613, 17 );
      MD5STEP( F1, ref b, c, d, a, _in[7] + 0xfd469501, 22 );
      MD5STEP( F1, ref a, b, c, d, _in[8] + 0x698098d8, 7 );
      MD5STEP( F1, ref d, a, b, c, _in[9] + 0x8b44f7af, 12 );
      MD5STEP( F1, ref c, d, a, b, _in[10] + 0xffff5bb1, 17 );
      MD5STEP( F1, ref b, c, d, a, _in[11] + 0x895cd7be, 22 );
      MD5STEP( F1, ref a, b, c, d, _in[12] + 0x6b901122, 7 );
      MD5STEP( F1, ref d, a, b, c, _in[13] + 0xfd987193, 12 );
      MD5STEP( F1, ref c, d, a, b, _in[14] + 0xa679438e, 17 );
      MD5STEP( F1, ref b, c, d, a, _in[15] + 0x49b40821, 22 );

      MD5STEP( F2, ref a, b, c, d, _in[1] + 0xf61e2562, 5 );
      MD5STEP( F2, ref d, a, b, c, _in[6] + 0xc040b340, 9 );
      MD5STEP( F2, ref c, d, a, b, _in[11] + 0x265e5a51, 14 );
      MD5STEP( F2, ref b, c, d, a, _in[0] + 0xe9b6c7aa, 20 );
      MD5STEP( F2, ref a, b, c, d, _in[5] + 0xd62f105d, 5 );
      MD5STEP( F2, ref d, a, b, c, _in[10] + 0x02441453, 9 );
      MD5STEP( F2, ref c, d, a, b, _in[15] + 0xd8a1e681, 14 );
      MD5STEP( F2, ref b, c, d, a, _in[4] + 0xe7d3fbc8, 20 );
      MD5STEP( F2, ref a, b, c, d, _in[9] + 0x21e1cde6, 5 );
      MD5STEP( F2, ref d, a, b, c, _in[14] + 0xc33707d6, 9 );
      MD5STEP( F2, ref c, d, a, b, _in[3] + 0xf4d50d87, 14 );
      MD5STEP( F2, ref b, c, d, a, _in[8] + 0x455a14ed, 20 );
      MD5STEP( F2, ref a, b, c, d, _in[13] + 0xa9e3e905, 5 );
      MD5STEP( F2, ref d, a, b, c, _in[2] + 0xfcefa3f8, 9 );
      MD5STEP( F2, ref c, d, a, b, _in[7] + 0x676f02d9, 14 );
      MD5STEP( F2, ref b, c, d, a, _in[12] + 0x8d2a4c8a, 20 );

      MD5STEP( F3, ref a, b, c, d, _in[5] + 0xfffa3942, 4 );
      MD5STEP( F3, ref d, a, b, c, _in[8] + 0x8771f681, 11 );
      MD5STEP( F3, ref c, d, a, b, _in[11] + 0x6d9d6122, 16 );
      MD5STEP( F3, ref b, c, d, a, _in[14] + 0xfde5380c, 23 );
      MD5STEP( F3, ref a, b, c, d, _in[1] + 0xa4beea44, 4 );
      MD5STEP( F3, ref d, a, b, c, _in[4] + 0x4bdecfa9, 11 );
      MD5STEP( F3, ref c, d, a, b, _in[7] + 0xf6bb4b60, 16 );
      MD5STEP( F3, ref b, c, d, a, _in[10] + 0xbebfbc70, 23 );
      MD5STEP( F3, ref a, b, c, d, _in[13] + 0x289b7ec6, 4 );
      MD5STEP( F3, ref d, a, b, c, _in[0] + 0xeaa127fa, 11 );
      MD5STEP( F3, ref c, d, a, b, _in[3] + 0xd4ef3085, 16 );
      MD5STEP( F3, ref b, c, d, a, _in[6] + 0x04881d05, 23 );
      MD5STEP( F3, ref a, b, c, d, _in[9] + 0xd9d4d039, 4 );
      MD5STEP( F3, ref d, a, b, c, _in[12] + 0xe6db99e5, 11 );
      MD5STEP( F3, ref c, d, a, b, _in[15] + 0x1fa27cf8, 16 );
      MD5STEP( F3, ref b, c, d, a, _in[2] + 0xc4ac5665, 23 );

      MD5STEP( F4, ref a, b, c, d, _in[0] + 0xf4292244, 6 );
      MD5STEP( F4, ref d, a, b, c, _in[7] + 0x432aff97, 10 );
      MD5STEP( F4, ref c, d, a, b, _in[14] + 0xab9423a7, 15 );
      MD5STEP( F4, ref b, c, d, a, _in[5] + 0xfc93a039, 21 );
      MD5STEP( F4, ref a, b, c, d, _in[12] + 0x655b59c3, 6 );
      MD5STEP( F4, ref d, a, b, c, _in[3] + 0x8f0ccc92, 10 );
      MD5STEP( F4, ref c, d, a, b, _in[10] + 0xffeff47d, 15 );
      MD5STEP( F4, ref b, c, d, a, _in[1] + 0x85845dd1, 21 );
      MD5STEP( F4, ref a, b, c, d, _in[8] + 0x6fa87e4f, 6 );
      MD5STEP( F4, ref d, a, b, c, _in[15] + 0xfe2ce6e0, 10 );
      MD5STEP( F4, ref c, d, a, b, _in[6] + 0xa3014314, 15 );
      MD5STEP( F4, ref b, c, d, a, _in[13] + 0x4e0811a1, 21 );
      MD5STEP( F4, ref a, b, c, d, _in[4] + 0xf7537e82, 6 );
      MD5STEP( F4, ref d, a, b, c, _in[11] + 0xbd3af235, 10 );
      MD5STEP( F4, ref c, d, a, b, _in[2] + 0x2ad7d2bb, 15 );
      MD5STEP( F4, ref b, c, d, a, _in[9] + 0xeb86d391, 21 );

      buf[0] += a;
      buf[1] += b;
      buf[2] += c;
      buf[3] += d;
    }

    /*
    * Start MD5 accumulation.  Set bit count to 0 and buffer to mysterious
    * initialization constants.
    */
    static void MD5Init( MD5Context ctx )
    {
      ctx.isInit = true;
      ctx.buf[0] = 0x67452301;
      ctx.buf[1] = 0xefcdab89;
      ctx.buf[2] = 0x98badcfe;
      ctx.buf[3] = 0x10325476;
      ctx.bits[0] = 0;
      ctx.bits[1] = 0;
    }
    /*
    * Update context to reflect the concatenation of another buffer full
    * of bytes.
    */
    static void MD5Update( MD5Context pCtx, byte[] buf, int len )
    {

      MD5Context ctx = (MD5Context)pCtx;
      int t;

      /* Update bitcount */

      t = (int)ctx.bits[0];
      if ( ( ctx.bits[0] = (u32)( t + ( (u32)len << 3 ) ) ) < t )
        ctx.bits[1]++; /* Carry from low to high */
      ctx.bits[1] += (u32)( len >> 29 );

      t = ( t >> 3 ) & 0x3f;    /* Bytes already in shsInfo.data */

      /* Handle any leading odd-sized chunks */

      int _buf = 0; // Offset into buffer
      int p = t; //Offset into ctx._in
      if ( t != 0 )
      {
        //byte p = (byte)ctx._in + t;
        t = 64 - t;
        if ( len < t )
        {
          Buffer.BlockCopy( buf, _buf, ctx._in, p, len );// memcpy( p, buf, len );
          return;
        }
        Buffer.BlockCopy( buf, _buf, ctx._in, p, t ); //memcpy( p, buf, t );
        //byteReverse(ctx._in, 16);
        MD5Transform( ctx.buf, ctx._in );
        _buf += t;// buf += t;
        len -= t;
      }

      /* Process data in 64-byte chunks */

      while ( len >= 64 )
      {
        Buffer.BlockCopy( buf, _buf, ctx._in, 0, 64 );//memcpy( ctx._in, buf, 64 );
        //byteReverse(ctx._in, 16);
        MD5Transform( ctx.buf, ctx._in );
        _buf += 64;// buf += 64;
        len -= 64;
      }

      /* Handle any remaining bytes of data. */

      Buffer.BlockCopy( buf, _buf, ctx._in, 0, len ); //memcpy( ctx._in, buf, len );
    }

    /*
    * Final wrapup - pad to 64-byte boundary with the bit pattern
    * 1 0* (64-bit count of bits processed, MSB-first)
    */

    static void MD5Final( byte[] digest, MD5Context pCtx )
    {
      MD5Context ctx = pCtx;
      int count;
      int p;

      /* Compute number of bytes mod 64 */
      count = (int)( ctx.bits[0] >> 3 ) & 0x3F;

      /* Set the first char of padding to 0x80.  This is safe since there is
      always at least one byte free */
      p = count;
      ctx._in[p++] = 0x80;

      /* Bytes of padding needed to make 64 bytes */
      count = 64 - 1 - count;

      /* Pad out to 56 mod 64 */
      if ( count < 8 )
      {
        /* Two lots of padding:  Pad the first block to 64 bytes */
        Array.Clear( ctx._in, p, count );//memset(p, 0, count);
        //byteReverse( ctx._in, 16 );
        MD5Transform( ctx.buf, ctx._in );

        /* Now fill the next block with 56 bytes */
        Array.Clear( ctx._in, 0, 56 );//memset(ctx._in, 0, 56);
      }
      else
      {
        /* Pad block to 56 bytes */
        Array.Clear( ctx._in, p, count - 8 );//memset(p, 0, count-8);
      }
      //byteReverse( ctx._in, 14 );

      /* Append length in bits and transform */
      ctx._in[14] = (byte)ctx.bits[0];
      ctx._in[15] = (byte)ctx.bits[1];

      MD5Transform( ctx.buf, ctx._in );
      //byteReverse( ctx.buf, 4 );
      Buffer.BlockCopy( ctx.buf, 0, digest, 0, 16 );//memcpy(digest, ctx.buf, 16);
      //memset(ctx, 0, sizeof(ctx));    /* In case it is sensitive */
      Array.Clear( ctx._in, 0, ctx._in.Length );
      Array.Clear( ctx.bits, 0, ctx.bits.Length );
      Array.Clear( ctx.buf, 0, ctx.buf.Length );
      ctx._Mem = null;
    }

    /*
    ** Convert a digest into base-16.  digest should be declared as
    ** "unsigned char digest[16]" in the calling function.  The MD5
    ** digest is stored in the first 16 bytes.  zBuf should
    ** be "char zBuf[33]".
    */
    static void DigestToBase16( byte[] digest, byte[] zBuf )
    {
      string zEncode = "0123456789abcdef";
      int i, j;

      for ( j = i = 0 ; i < 16 ; i++ )
      {
        int a = digest[i];
        zBuf[j++] = (byte)zEncode[( a >> 4 ) & 0xf];
        zBuf[j++] = (byte)zEncode[a & 0xf];
      }
      if ( j < zBuf.Length ) zBuf[j] = 0;
    }

    /*
    ** A TCL command for md5.  The argument is the text to be hashed.  The
    ** Result is the hash in base64.
    */
    static int md5_cmd( object cd, Tcl_Interp interp, int argc, Tcl_Obj[] argv )
    {
      MD5Context ctx = new MD5Context();
      byte[] digest = new byte[16];
      byte[] zBuf = new byte[32];


      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0],
        " TEXT\"" );
        return TCL.TCL_ERROR;
      }
      MD5Init( ctx );
      MD5Update( ctx, Encoding.UTF8.GetBytes( argv[1].ToString() ), Encoding.UTF8.GetByteCount( argv[1].ToString() ) );
      MD5Final( digest, ctx );
      DigestToBase16( digest, zBuf );
      TCL.Tcl_AppendResult( interp, Encoding.UTF8.GetString( zBuf ) );
      return TCL.TCL_OK;
    }

    /*
    ** A TCL command to take the md5 hash of a file.  The argument is the
    ** name of the file.
    */
    static int md5file_cmd( object cd, Tcl_Interp interp, int argc, Tcl_Obj[] argv )
    {
      StreamReader _in = null;
      byte[] digest = new byte[16];
      StringBuilder zBuf = new StringBuilder( 10240 );

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0],
        " FILENAME\"", 0 );
        return TCL.TCL_ERROR;
      }
      Debugger.Break(); // TODO --   _in = fopen( argv[1], "rb" );
      if ( _in == null )
      {
        TCL.Tcl_AppendResult( interp, "unable to open file \"", argv[1],
        "\" for reading", 0 );
        return TCL.TCL_ERROR;
      }
      Debugger.Break(); // TODO
      //MD5Init( ctx );
      //for(;;){
      //  int n;
      //  n = fread(zBuf, 1, zBuf.Capacity, _in);
      //  if( n<=0 ) break;
      //  MD5Update(ctx, zBuf.ToString(), (unsigned)n);
      //}
      //fclose(_in);
      //MD5Final(digest, ctx);
      //  DigestToBase16(digest, zBuf);
      //Tcl_AppendResult( interp, zBuf );
      return TCL.TCL_OK;
    }

    /*
    ** Register the two TCL commands above with the TCL interpreter.
    */
    static public int Md5_Init( Tcl_Interp interp )
    {
      TCL.Tcl_CreateCommand( interp, "md5", md5_cmd, null, null );
      TCL.Tcl_CreateCommand( interp, "md5file", md5file_cmd, null, null );
      return TCL.TCL_OK;
    }

    /*
    ** During testing, the special md5sum() aggregate function is available.
    ** inside SQLite.  The following routines implement that function.
    */
    static void md5step( sqlite3_context context, int argc, sqlite3_value[] argv )
    {
      MD5Context p = null;
      int i;
      if ( argc < 1 ) return;
      MemRef pMem = sqlite3_aggregate_context( context, -1 );//sizeof(*p));
      if ( pMem._MD5Context == null )
      {
        pMem._MD5Context = new MD5Context();
        ( (MD5Context)pMem._MD5Context )._Mem = pMem;
      }
      p = (MD5Context)pMem._MD5Context;
      if ( p == null ) return;
      if ( !p.isInit )
      {
        MD5Init( p );
      }
      for ( i = 0 ; i < argc ; i++ )
      {
        byte[] zData = sqlite3_value_text( argv[i] ) == null ? null : Encoding.UTF8.GetBytes( sqlite3_value_text( argv[i] ) );
        if ( zData != null )
        {
          MD5Update( p, zData, zData.Length );
        }
      }
    }

    static void md5finalize( sqlite3_context context )
    {
      MD5Context p;
      byte[] digest = new byte[16];
      byte[] zBuf = new byte[33];
      MemRef pMem = sqlite3_aggregate_context( context, 0 );
      if ( pMem != null )
      {
        p = (MD5Context)pMem._MD5Context;
        MD5Final( digest, p );
      }
      DigestToBase16( digest, zBuf );
      sqlite3_result_text( context, Encoding.UTF8.GetString( zBuf ), -1, SQLITE_TRANSIENT );
    }

    static int Md5_Register( sqlite3 db, ref string dummy1, sqlite3_api_routines dummy2 )
    {
      int rc = sqlite3_create_function( db, "md5sum", -1, SQLITE_UTF8, 0, null,
      md5step, md5finalize );
      sqlite3_overload_function( db, "md5sum", -1 ); /* To exercise this API */
      return rc;
    }

  }
#endif
}
