using System;
using System.Diagnostics;
using System.Text;

namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
  using Tcl_CmdProc = tcl.lang.Interp.dxObjCmdProc;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_Obj = tcl.lang.TclObject;

  using sqlite3_stmt = CS_SQLite3.csSQLite.Vdbe;

  public partial class csSQLite
  {
    /*
    ** 2007 March 29
    **
    ** The author disclaims copyright to this source code.  In place of
    ** a legal notice, here is a blessing:
    **
    **    May you do good and not evil.
    **    May you find forgiveness for yourself and forgive others.
    **    May you share freely, never taking more than you give.
    **
    *************************************************************************
    **
    ** This file contains obscure tests of the C-interface required
    ** for completeness. Test code is written in C for these cases
    ** as there is not much point in binding to Tcl.
    **
    ** $Id: test9.c,v 1.7 2009/04/02 18:32:27 drh Exp $
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

    /*
    ** c_collation_test
    */
    static int c_collation_test(
    object clientdata, /* Pointer to sqlite3_enable_XXX function */
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int objc,              /* Number of arguments */
    Tcl_Obj[] objv  /* Command arguments */
    )
    {
      string zErrFunction = "N/A";
      sqlite3 db = null;

      int rc;
      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      /* Open a database. */
      rc = sqlite3_open( ":memory:", ref db );
      if ( rc != SQLITE_OK )
      {
        zErrFunction = "sqlite3_open";
        goto error_out;
      }

      rc = sqlite3_create_collation( db, "collate", 456, null, null );
      if ( rc != SQLITE_MISUSE )
      {
        sqlite3_close( db );
        zErrFunction = "sqlite3_create_collation";
        goto error_out;
      }

      sqlite3_close( db );
      return TCL.TCL_OK;

error_out:
      TCL.Tcl_ResetResult( interp );
      TCL.Tcl_AppendResult( interp, "Error testing function: ", zErrFunction, null );
      return TCL.TCL_ERROR;
    }

//    /*
//    ** c_realloc_test
//    */
//    static int c_realloc_test(
//    object clientdata, /* Pointer to sqlite3_enable_XXX function */
//    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
//    int objc,              /* Number of arguments */
//    Tcl_Obj[] objv /* Command arguments */
//    )
//    {
//      object p;
//      string zErrFunction = "N/A";

//      if ( objc != 1 )
//      {
//        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
//        return TCL.TCL_ERROR;
//      }

//      p = sqlite3Malloc( 5 );
//      if ( p == null )
//      {
//        zErrFunction = "sqlite3Malloc";
//        goto error_out;
//      }

//      /* Test that realloc()ing a block of memory to a negative size is
//      ** the same as free()ing that memory.
//      */
//      //TODO -- ignore realloc
//      //p = sqlite3_realloc(p, -1);
//      //if( p!=null ){
//      //  zErrFunction = "sqlite3_realloc";
//      //  goto error_out;
//      //}

//      return TCL.TCL_OK;

//error_out:
//      TCL.Tcl_ResetResult( interp );
//      TCL.Tcl_AppendResult( interp, "Error testing function: ", zErrFunction );
//      return TCL.TCL_ERROR;
//    }


    /*
    ** c_misuse_test
    */
    static int c_misuse_test(
    object clientdata, /* Pointer to sqlite3_enable_XXX function */
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int objc,              /* Number of arguments */
    Tcl_Obj[] objv /* Command arguments */
    )
    {
      string zErrFunction = "N/A";
      sqlite3 db = null;
      sqlite3_stmt pStmt;
      string dummyS = "";
      int rc;

      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      /* Open a database. Then close it again. We need to do this so that
      ** we have a "closed database handle" to pass to various API functions.
      */
      rc = sqlite3_open( ":memory:", ref db );
      if ( rc != SQLITE_OK )
      {
        zErrFunction = "sqlite3_open";
        goto error_out;
      }
      sqlite3_close( db );


      rc = sqlite3_errcode( db );
      if ( rc != SQLITE_MISUSE )
      {
        zErrFunction = "sqlite3_errcode";
        goto error_out;
      }

      pStmt = new sqlite3_stmt(); pStmt.pc = 1234;
      rc = sqlite3_prepare( db, null, 0, ref pStmt, ref dummyS );
      if ( rc != SQLITE_MISUSE )
      {
        zErrFunction = "sqlite3_prepare";
        goto error_out;
      }
      Debug.Assert( pStmt == null ); /* Verify that pStmt is zeroed even on a MISUSE error */


      pStmt = new sqlite3_stmt(); pStmt.pc = 1234;
      rc = sqlite3_prepare_v2( db, null, 0, ref pStmt, ref dummyS );
      if ( rc != SQLITE_MISUSE )
      {
        zErrFunction = "sqlite3_prepare_v2";
        goto error_out;
      }
      Debug.Assert( pStmt == null );

#if !SQLITE_OMIT_UTF16
pStmt = (sqlite3_stmt)1234;
rc = sqlite3_prepare16( db, null, 0, ref pStmt, ref dummyS );
if( rc!=SQLITE_MISUSE ){
zErrFunction = "sqlite3_prepare16";
goto error_out;
}
assert( pStmt==0 );
pStmt = (sqlite3_stmt)1234;
rc = sqlite3_prepare16_v2( db, null, 0, ref pStmt, ref dummyS );
if( rc!=SQLITE_MISUSE ){
zErrFunction = "sqlite3_prepare16_v2";
goto error_out;
}
assert( pStmt==0 );
#endif

      return TCL.TCL_OK;

error_out:
      TCL.Tcl_ResetResult( interp );
      TCL.Tcl_AppendResult( interp, "Error testing function: ", zErrFunction );
      return TCL.TCL_ERROR;
    }

    /*
    ** Register commands with the TCL interpreter.
    */
    static public int Sqlitetest9_Init( Tcl_Interp interp )
    {
      //static struct {
      //   char *zName;
      //   Tcl_ObjCmdProc *xProc;
      //   void *object;
      //}
      _aObjCmd[] aObjCmd = new _aObjCmd[]  {
new _aObjCmd( "c_misuse_test",    c_misuse_test, 0 ),
//new _aObjCmd( "c_realloc_test",   c_realloc_test, 0 ),
new _aObjCmd( "c_collation_test", c_collation_test, 0 ),
};
      int i;
      for ( i = 0 ; i < aObjCmd.Length ; i++ )
      {//sizeof(aObjCmd)/sizeof(aObjCmd[0]); i++){
        TCL.Tcl_CreateObjCommand( interp, aObjCmd[i].zName,
        aObjCmd[i].xProc, aObjCmd[i].clientData, null );
      }
      return TCL.TCL_OK;
    }
  }
#endif
}
