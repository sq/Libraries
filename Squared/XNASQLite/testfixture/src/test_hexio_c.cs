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
    ** 2007 April 6
    **
    ** The author disclaims copyright to this source code.  In place of
    ** a legal notice, here is a blessing:
    **
    **    May you do good and not evil.
    **    May you find forgiveness for yourself and forgive others.
    **    May you share freely, never taking more than you give.
    **
    *************************************************************************
    ** Code for testing all sorts of SQLite interfaces.  This code
    ** implements TCL commands for reading and writing the binary
    ** database files and displaying the content of those files as
    ** hexadecimal.  We could, _in theory, use the built-_in "binary"
    ** command of TCL to do a lot of this, but there are some issues
    ** with historical versions of the "binary" command.  So it seems
    ** easier and safer to build our own mechanism.
    **
    ** $Id: test_hexio.c,v 1.7 2008/05/12 16:17:42 drh Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    //#include "sqliteInt.h"
    //#include "tcl.h"
    //#include <stdlib.h>
    //#include <string.h>
    //#include <assert.h>


    /*
    ** Convert binary to hex.  The input zBuf[] contains N bytes of
    ** binary data.  zBuf[] is 2*n+1 bytes long.  Overwrite zBuf[]
    ** with a hexadecimal representation of its original binary input.
    */
    static void sqlite3TestBinToHex( byte[] zBuf, int N )
    {
      StringBuilder zHex = new StringBuilder( "0123456789ABCDEF" );
      int i, j;
      byte c;
      i = N * 2;
      zBuf[i--] = 0;
      for ( j = N - 1 ; j >= 0 ; j-- )
      {
        c = zBuf[j];
        zBuf[i--] = (byte)zHex[c & 0xf];
        zBuf[i--] = (byte)zHex[c >> 4];
      }
      Debug.Assert( i == -1 );
    }

    /*
    ** Convert hex to binary.  The input zIn[] contains N bytes of
    ** hexadecimal.  Convert this into binary and write aOut[] with
    ** the binary data.  Spaces _in the original input are ignored.
    ** Return the number of bytes of binary rendered.
    */
    static int sqlite3TestHexToBin( string zIn, int N, byte[] aOut )
    {
      int[] aMap = new int[]  {
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
1, 2, 3, 4, 5, 6, 7, 8,  9,10, 0, 0, 0, 0, 0, 0,
0,11,12,13,14,15,16, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0,11,12,13,14,15,16, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0,
};
      int i, j;
      int hi = 1;
      int c;

      for ( i = j = 0 ; i < N ; i++ )
      {
        c = aMap[zIn[i]];
        if ( c == 0 ) continue;
        if ( hi != 0 )
        {
          aOut[j] = (byte)( ( c - 1 ) << 4 );
          hi = 0;
        }
        else
        {
          aOut[j++] |= (byte)( c - 1 );
          hi = 1;
        }
      }
      return j;
    }


    /*
    ** Usage:   hexio_read  FILENAME  OFFSET  AMT
    **
    ** Read AMT bytes from file FILENAME beginning at OFFSET from the
    ** beginning of the file.  Convert that information to hexadecimal
    ** and return the resulting HEX string.
    */
    static int hexio_read(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int offset = 0;
      int amt = 0, got;
      string zFile;
      byte[] zBuf;
      FileStream _in;

      if ( objc != 4 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "FILENAME OFFSET AMT" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref offset ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[3], ref amt ) ) return TCL.TCL_ERROR;
      zFile = TCL.Tcl_GetString( objv[1] );
      zBuf = new byte[amt * 2 + 1];// sqlite3Malloc( amt * 2 + 1 );
      if ( zBuf == null )
      {
        return TCL.TCL_ERROR;
      }
      _in = new FileStream( zFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
      //if( _in==null){
      //  _in = fopen(zFile, "r");
      //}
      if ( _in == null )
      {
        TCL.Tcl_AppendResult( interp, "cannot open input file ", zFile );
        return TCL.TCL_ERROR;
      }
      _in.Seek( offset, SeekOrigin.Begin ); //fseek(_in, offset, SEEK_SET);
      got = _in.Read( zBuf, 0, amt ); // got = fread( zBuf, 1, amt, _in );
      _in.Flush();
      _in.Close();// fclose( _in );
      if ( got < 0 )
      {
        got = 0;
      }
      sqlite3TestBinToHex( zBuf, got );
      TCL.Tcl_AppendResult( interp, System.Text.Encoding.UTF8.GetString( zBuf ).Substring( 0, got * 2 ) );
      zBuf = null;// //sqlite3DbFree( db, ref zBuf );
      return TCL.TCL_OK;
    }


    /*
    ** Usage:   hexio_write  FILENAME  OFFSET  DATA
    **
    ** Write DATA into file FILENAME beginning at OFFSET from the
    ** beginning of the file.  DATA is expressed _in hexadecimal.
    */
    static int hexio_write(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int offset = 0;
      int nIn = 0, nOut, written;
      string zFile;
      string zIn;
      byte[] aOut;
      FileStream _out;

      if ( objc != 4 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "FILENAME OFFSET HEXDATA" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref offset ) ) return TCL.TCL_ERROR;
      zFile = TCL.Tcl_GetString( objv[1] );
      zIn = TCL.Tcl_GetStringFromObj( objv[3], ref nIn );
      aOut = new byte[nIn / 2 + 1];//sqlite3Malloc( nIn/2 );
      if ( aOut == null )
      {
        return TCL.TCL_ERROR;
      }
      nOut = sqlite3TestHexToBin( zIn, nIn, aOut );
      _out = new FileStream( zFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite );// fopen( zFile, "r+b" );
      //if( _out==0 ){
      //  _out = fopen(zFile, "r+");
      //}
      if ( _out == null )
      {
        TCL.Tcl_AppendResult( interp, "cannot open output file ", zFile );
        return TCL.TCL_ERROR;
      }
      _out.Seek( offset, SeekOrigin.Begin );// fseek( _out, offset, SEEK_SET );
      written = (int)_out.Position;
      _out.Write( aOut, 0, nOut );// written = fwrite( aOut, 1, nOut, _out );
      written = (int)_out.Position - written;
      aOut = null;// //sqlite3DbFree( db, ref aOut );
      _out.Flush();
      _out.Close();// fclose( _out );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( written ) );
      return TCL.TCL_OK;
    }

    /*
    ** USAGE:   hexio_get_int   HEXDATA
    **
    ** Interpret the HEXDATA argument as a big-endian integer.  Return
    ** the value of that integer.  HEXDATA can contain between 2 and 8
    ** hexadecimal digits.
    */
    static int hexio_get_int(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int val;
      int nIn = 0, nOut;
      string zIn;
      byte[] aOut;
      byte[] aNum = new byte[4];

      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "HEXDATA" );
        return TCL.TCL_ERROR;
      }
      zIn = TCL.Tcl_GetStringFromObj( objv[1], ref nIn );
      aOut = new byte[nIn / 2];// sqlite3Malloc( nIn / 2 );
      if ( aOut == null )
      {
        return TCL.TCL_ERROR;
      }
      nOut = sqlite3TestHexToBin( zIn, nIn, aOut );
      if ( nOut >= 4 )
      {
        aNum[0] = aOut[0]; // memcpy( aNum, aOut, 4 );
        aNum[1] = aOut[1];
        aNum[2] = aOut[2];
        aNum[3] = aOut[3];
      }
      else
      {
        //memset(aNum, 0, sizeof(aNum));
        //memcpy(&aNum[4-nOut], aOut, nOut);
        aNum[4 - nOut] = aOut[0];
        if ( nOut > 1 ) aNum[4 - nOut + 1] = aOut[1];
        if ( nOut > 2 ) aNum[4 - nOut + 2] = aOut[2];
        if ( nOut > 3 ) aNum[4 - nOut + 3] = aOut[3];
      }
      aOut = null;// //sqlite3DbFree( db, ref aOut );
      val = ( aNum[0] << 24 ) | ( aNum[1] << 16 ) | ( aNum[2] << 8 ) | aNum[3];
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( val ) );
      return TCL.TCL_OK;
    }


    /*
    ** USAGE:   hexio_render_int16   INTEGER
    **
    ** Render INTEGER has a 16-bit big-endian integer _in hexadecimal.
    */
    static int hexio_render_int16(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int val = 0;
      byte[] aNum = new byte[10];

      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "INTEGER" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref val ) ) return TCL.TCL_ERROR;
      aNum[0] = (byte)( val >> 8 );
      aNum[1] = (byte)val;
      sqlite3TestBinToHex( aNum, 2 );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewStringObj( aNum, 4 ) );
      return TCL.TCL_OK;
    }


    /*
    ** USAGE:   hexio_render_int32   INTEGER
    **
    ** Render INTEGER has a 32-bit big-endian integer _in hexadecimal.
    */
    static int hexio_render_int32(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int val = 0;
      byte[] aNum = new byte[10];

      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "INTEGER" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref val ) ) return TCL.TCL_ERROR;
      aNum[0] = (byte)( val >> 24 );
      aNum[1] = (byte)( val >> 16 );
      aNum[2] = (byte)( val >> 8 );
      aNum[3] = (byte)val;
      sqlite3TestBinToHex( aNum, 4 );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewStringObj( aNum, 8 ) );
      return TCL.TCL_OK;
    }

    /*
    ** USAGE:  utf8_to_utf8  HEX
    **
    ** The argument is a UTF8 string represented _in hexadecimal.
    ** The UTF8 might not be well-formed.  Run this string through
    ** sqlite3Utf8to8() convert it back to hex and return the result.
    */
    //static int utf8_to_utf8(
    //  void * clientData,
    //  Tcl_Interp *interp,
    //  int objc,
    //  Tcl_Obj *CONST objv[]
    //){
    //#if SQLITE_DEBUG
    //  int n;
    //  int nOut;
    //  const unsigned char *zOrig;
    //  unsigned char *z;
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "HEX");
    //    return TCL.TCL_ERROR;
    //  }
    //  zOrig = (unsigned char *)Tcl_GetStringFromObj(objv[1], n);
    //  z = sqlite3Malloc( n+3 );
    //  n = sqlite3TestHexToBin(zOrig, n, z);
    //  z[n] = 0;
    //  nOut = sqlite3Utf8To8(z);
    //  sqlite3TestBinToHex(z,nOut);
    //  TCL.Tcl_AppendResult(interp, (char*)z, 0);
    //  //sqlite3DbFree(db,z);
    //#endif
    //  return TCL.TCL_OK;
    //}


    /*
    ** Register commands with the TCL interpreter.
    */
    static public int Sqlitetest_hexio_Init( Tcl_Interp interp )
    {
      //static struct {
      //   string zName;
      //   Tcl_ObjCmdProc *xProc;
      //}
      _aObjCmd[] aObjCmd = new _aObjCmd[] {
new _aObjCmd(  "hexio_read",                   hexio_read            ),
new _aObjCmd(  "hexio_write",                  hexio_write           ),
new _aObjCmd(  "hexio_get_int",                hexio_get_int         ),
new _aObjCmd(  "hexio_render_int16",           hexio_render_int16    ),
new _aObjCmd(  "hexio_render_int32",           hexio_render_int32    ),
//new _aObjCmd(  "utf8_to_utf8",                 utf8_to_utf8          },
};
      int i;
      for ( i = 0 ; i < aObjCmd.Length ; i++ )
      {
        TCL.Tcl_CreateObjCommand( interp, aObjCmd[i].zName, aObjCmd[i].xProc, null, null );
      }
      return TCL.TCL_OK;
    }
  }
#endif
}

