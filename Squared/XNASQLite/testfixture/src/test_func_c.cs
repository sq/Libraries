using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using Bitmask = System.UInt64;
using i64 = System.Int64;
using u8 = System.Byte;
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
  using sqlite3_stmt = csSQLite.Vdbe;

  public partial class csSQLite
  {
    /*
    ** 2008 March 19
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
    ** implements new SQL functions used by the test scripts.
    **
    ** $Id: test_func.c,v 1.16 2009/07/22 07:27:57 danielk1977 Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    //#include "sqlite3.h"
    //#include "tcl.h"
    //#include <stdlib.h>
    //#include <string.h>
    //#include <assert.h>


    /*
    ** Allocate nByte bytes of space using sqlite3Malloc(). If the
    ** allocation fails, call sqlite3_result_error_nomem() to notify
    ** the database handle that malloc() has failed.
    */
    static Object testContextMalloc( sqlite3_context context, int nByte )
    {
      Object z = new Object();// sqlite3Malloc( nByte );
      if ( z == null && nByte > 0 )
      {
        sqlite3_result_error_nomem( context );
      }
      return z;
    }

    /*
    ** This function generates a string of random characters.  Used for
    ** generating test data.
    */
    static void randStr( sqlite3_context context, int argc, sqlite3_value[] argv )
    {
      string zSrc =
      "abcdefghijklmnopqrstuvwxyz" +
      "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
      "0123456789" +
      ".-!,:*^+=_|?/<> ";
      int iMin, iMax, n, i;
      i64 r = 0;

      StringBuilder zBuf = new StringBuilder( 1000 );

      /* It used to be possible to call randstr() with any number of arguments,
      ** but now it is registered with SQLite as requiring exactly 2.
      */
      Debug.Assert( argc == 2 );

      iMin = sqlite3_value_int( argv[0] );
      if ( iMin < 0 ) iMin = 0;
      if ( iMin >= zBuf.Capacity ) iMin = zBuf.Capacity - 1;
      iMax = sqlite3_value_int( argv[1] );
      if ( iMax < iMin ) iMax = iMin;
      if ( iMax >= zBuf.Capacity ) iMax = zBuf.Capacity - 1;
      n = iMin;
      if ( iMax > iMin )
      {
        sqlite3_randomness( sizeof( i64 ), ref r );
        r &= 0x7fffffff;
        n += (int)( r % ( iMax + 1 - iMin ) );
      }
      Debug.Assert( n < zBuf.Capacity );//sizeof( zBuf ) );
      i64 zRan = 0;
      for ( i = 0 ; i < n ; i++ )
      {
        sqlite3_randomness( 1, ref zRan );
        zBuf.Append( zSrc[(int)( Math.Abs( zRan ) % ( zSrc.Length - 1 ) )] );
      }
      //zBuf[n] = 0;
      sqlite3_result_text( context, zBuf.ToString(), n, SQLITE_TRANSIENT );
    }

    /*
    ** The following two SQL functions are used to test returning a text
    ** result with a destructor. Function 'test_destructor' takes one argument
    ** and returns the same argument interpreted as TEXT. A destructor is
    ** passed with the sqlite3_result_text() call.
    **
    ** SQL function 'test_destructor_count' returns the number of outstanding
    ** allocations made by 'test_destructor';
    **
    ** WARNING: Not threadsafe.
    */
    static int test_destructor_count_var = 0;
    static void destructor( ref string p )
    {
      string zVal = p;
      Debug.Assert( zVal != null );
      //zVal--;
      //sqlite3DbFree( null, ref zVal );
      test_destructor_count_var--;
    }
    static void test_destructor(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
      String zVal;
      int len;

      test_destructor_count_var++;
      Debug.Assert( nArg == 1 );
      if ( sqlite3_value_type( argv[0] ) == SQLITE_NULL ) return;
      len = sqlite3_value_bytes( argv[0] );
      zVal = "";//testContextMalloc( pCtx, len + 3 );
      if ( null == zVal )
      {
        return;
      }
      //zVal[len+1] = 0;
      //zVal[len+2] = 0;
      //zVal++;
      zVal = sqlite3_value_text( argv[0] );//memcpy(zVal, sqlite3_value_text(argv[0]), len);

      sqlite3_result_text( pCtx, zVal, -1, destructor );
    }
#if !SQLITE_OMIT_UTF16
static void test_destructor16(
//sqlite3_context pCtx,     /* Function context */
//int nArg,                 /* Number of function arguments */
//sqlite3_value[] argv      /* Values for all function arguments */
){
char *zVal;
int len;

test_destructor_count_var++;
Debug.Assert(nArg==1 );
if( sqlite3_value_type(argv[0])==SQLITE_NULL ) return;
len = sqlite3_value_bytes16(argv[0]);
zVal = testContextMalloc(pCtx, len+3);
if( !zVal ){
return;
}
zVal[len+1] = 0;
zVal[len+2] = 0;
zVal++;
memcpy(zVal, sqlite3_value_text16(argv[0]), len);
sqlite3_result_text16(pCtx, zVal, -1, destructor);
}
#endif

    static void test_destructor_count(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
      sqlite3_result_int( pCtx, test_destructor_count_var );
    }

    /*
    ** The following aggregate function, test_agg_errmsg16(), takes zero
    ** arguments. It returns the text value returned by the sqlite3_errmsg16()
    ** API function.
    */
    //void sqlite3BeginBenignMalloc(void);
    //void sqlite3EndBenignMalloc(void);
    static void test_agg_errmsg16_step( sqlite3_context a, int b, sqlite3_value[] c )
    {
    }
    static void test_agg_errmsg16_final( sqlite3_context ctx )
    {
#if !SQLITE_OMIT_UTF16
      string z;
      sqlite3 db = sqlite3_context_db_handle( ctx );
      sqlite3_aggregate_context( ctx, 2048 );
      sqlite3BeginBenignMalloc();
      z = sqlite3_errmsg16( db );
      sqlite3EndBenignMalloc();
      sqlite3_result_text16( ctx, z, -1, SQLITE_TRANSIENT );
#endif
    }

    /*
    ** Routines for testing the sqlite3_get_auxdata() and sqlite3_set_auxdata()
    ** interface.
    **
    ** The test_auxdata() SQL function attempts to register each of its arguments
    ** as auxiliary data.  If there are no prior registrations of aux data for
    ** that argument (meaning the argument is not a constant or this is its first
    ** call) then the result for that argument is 0.  If there is a prior
    ** registration, the result for that argument is 1.  The overall result
    ** is the individual argument results separated by spaces.
    */
    static void free_test_auxdata( ref string p ) {
      p = null;
      //sqlite3DbFree( null, ref p );
    }
    static void test_auxdata(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
      int i;
      StringBuilder zRet = new StringBuilder( nArg * 2 );//testContextMalloc( pCtx, nArg * 2 );
      if ( null == zRet ) return;
      //memset(zRet, 0, nArg*2);
      for ( i = 0 ; i < nArg ; i++ )
      {
        string z = sqlite3_value_text( argv[i] );
        if ( z != null )
        {
          int n;
          string zAux = sqlite3_get_auxdata( pCtx, i );
          if ( zAux != null )
          {
            zRet.Append( '1' );//[i * 2] = '1';
            Debug.Assert( zAux == z );//strcmp( zAux, z ) == 0 );
          }
          else
          {
            zRet.Append( '0' );//[i * 2] = '0';
          }
          n = z.Length;// strlen( z ) + 1;
          zAux = "";//testContextMalloc( pCtx, n );
          if ( zAux != null )
          {
            zAux = z.Substring( 0, n );// memcpy( zAux, z, n );
            sqlite3_set_auxdata( pCtx, i, zAux, free_test_auxdata );
          }
          zRet.Append( ' ' );// zRet[i * 2 + 1] = ' ';
        }
      }
      sqlite3_result_text( pCtx, zRet.ToString(), 2 * nArg - 1, free_test_auxdata );
    }

    /*
    ** A function to test error reporting from user functions. This function
    ** returns a copy of its first argument as the error message.  If the
    ** second argument exists, it becomes the error code.
    */
    static void test_error(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
      sqlite3_result_error( pCtx, sqlite3_value_text( argv[0] ), -1 );
      if ( nArg == 2 )
      {
        sqlite3_result_error_code( pCtx, sqlite3_value_int( argv[1] ) );
      }
    }

    /*
    ** Implementation of the counter(X) function.  If X is an integer
    ** constant, then the first invocation will return X.  The second X+1.
    ** and so forth.  Can be used (for example) to provide a sequence number
    ** in a result set.
    */
    //static void counterFunc(
    //sqlite3_context pCtx,     /* Function context */
    //int nArg,                 /* Number of function arguments */
    //sqlite3_value[] argv      /* Values for all function arguments */
    //){
    //  int *pCounter = (int*)sqlite3_get_auxdata(pCtx, 0);
    //  if( pCounter==0 ){
    //    pCounter = sqlite3_malloc( sizeof(*pCounter) );
    //    if( pCounter==0 ){
    //      sqlite3_result_error_nomem(pCtx);
    //      return;
    //    }
    //    *pCounter = sqlite3_value_int(argv[0]);
    //    sqlite3_set_auxdata(pCtx, 0, pCounter, //sqlite3_free);
    //  }else{
    //    ++*pCounter;
    //  }
    //  sqlite3_result_int(pCtx, *pCounter);
    //}
    //
    //

    /*
    ** This function takes two arguments.  It performance UTF-8/16 type
    ** conversions on the first argument then returns a copy of the second
    ** argument.
    **
    ** This function is used in cases such as the following:
    **
    **      SELECT test_isolation(x,x) FROM t1;
    **
    ** We want to verify that the type conversions that occur on the
    ** first argument do not invalidate the second argument.
    */
    static void test_isolation(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
#if !SQLITE_OMIT_UTF16
sqlite3_value_text16(argv[0]);
sqlite3_value_text(argv[0]);
sqlite3_value_text16(argv[0]);
sqlite3_value_text(argv[0]);
#endif
      sqlite3_result_value( pCtx, argv[1] );
    }

    /*
    ** Invoke an SQL statement recursively.  The function result is the
    ** first column of the first row of the result set.
    */
    static void test_eval(
    sqlite3_context pCtx,     /* Function context */
    int nArg,                 /* Number of function arguments */
    sqlite3_value[] argv      /* Values for all function arguments */
    )
    {
      sqlite3_stmt pStmt = new sqlite3_stmt();
      int rc;
      sqlite3 db = sqlite3_context_db_handle( pCtx );
      string zSql;

      zSql = sqlite3_value_text( argv[0] );
      rc = sqlite3_prepare_v2( db, zSql, -1, ref  pStmt, 0 );
      if ( rc == SQLITE_OK )
      {
        rc = sqlite3_step( pStmt );
        if ( rc == SQLITE_ROW )
        {
          sqlite3_result_value( pCtx, sqlite3_column_value( pStmt, 0 ) );
        }
        rc = sqlite3_finalize( ref pStmt );
      }
      if ( rc != 0 )
      {
        string zErr;
        Debug.Assert( pStmt == null );
        zErr = sqlite3_mprintf( "sqlite3_prepare_v2() error: %s", sqlite3_errmsg( db ) );
        sqlite3_result_text(pCtx, zErr, -1, null);//sqlite3_free );
        sqlite3_result_error_code( pCtx, rc );
      }
    }

    class _aFuncs
    {
      public string zName;
      public int nArg;
      public u8 eTextRep; /* 1: UTF-16.  0: UTF-8 */
      public dxFunc xFunc;
      public _aFuncs( string zName, int nArg, u8 eTextRep, dxFunc xFunc )
      {
        this.zName = zName;
        this.nArg = nArg;
        this.eTextRep = eTextRep;
        this.xFunc = xFunc;
      }
    }

    static int registerTestFunctions( sqlite3 db, ref string dummy1, sqlite3_api_routines dummy2 )
    {
      _aFuncs[] aFuncs = new _aFuncs[]  {
new _aFuncs( "randstr",               2, SQLITE_UTF8, randStr    ),
new _aFuncs( "test_destructor",       1, SQLITE_UTF8, test_destructor),
#if !SQLITE_OMIT_UTF16
{ "test_destructor16",     1, SQLITE_UTF8, test_destructor16},
#endif
new _aFuncs(  "test_destructor_count", 0, SQLITE_UTF8, test_destructor_count),
new _aFuncs(  "test_auxdata",         -1, SQLITE_UTF8, test_auxdata),
new _aFuncs( "test_error",            1, SQLITE_UTF8, test_error),
new _aFuncs( "test_error",            2, SQLITE_UTF8, test_error),
new _aFuncs(  "test_eval",             1, SQLITE_UTF8, test_eval),
new _aFuncs(  "test_isolation",        2, SQLITE_UTF8, test_isolation),
//{ "test_counter",        2, SQLITE_UTF8, counterFunc},
};
      int i;

      for ( i = 0 ; i < aFuncs.Length ; i++ )
      {//sizeof(aFuncs)/sizeof(aFuncs[0]); i++){
        sqlite3_create_function( db, aFuncs[i].zName, aFuncs[i].nArg,
        aFuncs[i].eTextRep, 0, aFuncs[i].xFunc, null, null );
      }

      sqlite3_create_function( db, "test_agg_errmsg16", 0, SQLITE_ANY, 0, null,
      test_agg_errmsg16_step, test_agg_errmsg16_final );

      return SQLITE_OK;
    }

    /*
    ** TCLCMD:  autoinstall_test_functions
    **
    ** Invoke this TCL command to use sqlite3_auto_extension() to cause
    ** the standard set of test functions to be loaded into each new
    ** database connection.
    */
    static int autoinstall_test_funcs(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      //extern int Md5_Register(sqlite3*);
      int rc = sqlite3_auto_extension( (dxInit)registerTestFunctions );
      if ( rc == SQLITE_OK )
      {
        rc = sqlite3_auto_extension( (dxInit)Md5_Register );
      }
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return TCL.TCL_OK;
    }


    /*
    ** A bogus step function and finalizer function.
    */
    static void tStep( sqlite3_context a, int b, sqlite3_value[] c ) { }
    static void tFinal( sqlite3_context a ) { }


    /*
    ** tclcmd:  abuse_create_function
    **
    ** Make various calls to sqlite3_create_function that do not have valid
    ** parameters.  Verify that the error condition is detected and reported.
    */
    static int abuse_create_function(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      //extern int getDbPointer(Tcl_Interp*, const char*, sqlite3**);
      sqlite3 db = null;
      int rc;
      int mxArg;

      if ( getDbPointer( interp, TCL.Tcl_GetString( objv[1] ), ref db ) != 0 ) return TCL.TCL_ERROR;

      rc = sqlite3_create_function( db, "tx", 1, SQLITE_UTF8, 0, tStep, tStep, tFinal );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", 1, SQLITE_UTF8, 0, tStep, tStep, null );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", 1, SQLITE_UTF8, 0, tStep, null, tFinal );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", 1, SQLITE_UTF8, 0, null, null, tFinal );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", 1, SQLITE_UTF8, 0, null, tStep, null );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", -2, SQLITE_UTF8, 0, tStep, null, null );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "tx", 128, SQLITE_UTF8, 0, tStep, null, null );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      rc = sqlite3_create_function( db, "funcxx" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789",
      1, SQLITE_UTF8, 0, tStep, null, null );
      if ( rc != SQLITE_MISUSE ) goto abuse_err;

      /* This last function registration should actually work.  Generate
      ** a no-op function (that always returns NULL) and which has the
      ** maximum-length function name and the maximum number of parameters.
      */
      sqlite3_limit( db, SQLITE_LIMIT_FUNCTION_ARG, 10000 );
      mxArg = sqlite3_limit( db, SQLITE_LIMIT_FUNCTION_ARG, -1 );
      rc = sqlite3_create_function( db, "nullx" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789" +
      "_123456789_123456789_123456789_123456789_123456789",
      mxArg, SQLITE_UTF8, 0, tStep, null, null );
      if ( rc != SQLITE_OK ) goto abuse_err;

      return TCL.TCL_OK;

abuse_err:
      TCL.Tcl_AppendResult( interp, "sqlite3_create_function abused test failed"
      );
      return TCL.TCL_ERROR;
    }


    /*
    ** Register commands with the TCL interpreter.
    */
    public static int Sqlitetest_func_Init( Tcl_Interp interp )
    {
      //static struct {
      //   char *zName;
      //   Tcl_ObjCmdProc *xProc;
      //}
      _aObjCmd[] aObjCmd = new _aObjCmd[]  {
new _aObjCmd( "autoinstall_test_functions",    autoinstall_test_funcs ),
new _aObjCmd( "abuse_create_function",         abuse_create_function  ),
};
      int i;
      //extern int Md5_Register(sqlite3*);

      for ( i = 0 ; i < aObjCmd.Length ; i++ )
      {//sizeof(aObjCmd)/sizeof(aObjCmd[0]); i++){
        TCL.Tcl_CreateObjCommand( interp, aObjCmd[i].zName, aObjCmd[i].xProc, null, null );
      }
      sqlite3_initialize();
      sqlite3_auto_extension( (dxInit)registerTestFunctions );
      sqlite3_auto_extension( (dxInit)Md5_Register );
      return TCL.TCL_OK;
    }
  }
#endif
}

