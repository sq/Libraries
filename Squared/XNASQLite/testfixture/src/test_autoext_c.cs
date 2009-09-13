using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
    ** 2006 August 23
    **
    ** The author disclaims copyright to this source code.  In place of
    ** a legal notice, here is a blessing:
    **
    **    May you do good and not evil.
    **    May you find forgiveness for yourself and forgive others.
    **    May you share freely, never taking more than you give.
    **
    *************************************************************************
    ** Test extension for testing the sqlite3_auto_extension() function.
    **
    ** $Id: test_autoext.c,v 1.5 2008/07/08 02:12:37 drh Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    //#include "tcl.h"
    //#include "sqlite3ext.h"

#if !SQLITE_OMIT_LOAD_EXTENSION
    //static int SQLITE_EXTENSION_INIT1 = null;
    static sqlite3_api_routines sqlite3_api = null;

    /*
    ** The sqr() SQL function returns the square of its input value.
    */
    static void sqrFunc(
    sqlite3_context context,
    int argc,
    sqlite3_value[] argv
    )
    {
      double r = sqlite3_value_double( argv[0] );
      sqlite3_result_double( context, r * r );
    }

    /*
    ** This is the entry point to register the extension for the sqr() function.
    */
    static int sqr_init(
    sqlite3 db,
    ref string pzErrMsg,
    sqlite3_api_routines pApi
    )
    {
      sqlite3_api = pApi; // SQLITE_EXTENSION_INIT2( pApi );
      sqlite3_create_function( db, "sqr", 1, SQLITE_ANY, 0, sqrFunc, null, null );
      return 0;
    }

    /*
    ** The cube() SQL function returns the cube of its input value.
    */
    static void cubeFunc(
    sqlite3_context context,
    int argc,
    sqlite3_value[] argv
    )
    {
      double r = sqlite3_value_double( argv[0] );
      sqlite3_result_double( context, r * r * r );
    }

    /*
    ** This is the entry point to register the extension for the cube() function.
    */
    static int cube_init(
    sqlite3 db,
    ref string pzErrMsg,
    sqlite3_api_routines pApi
    )
    {
      sqlite3_api = pApi; //SQLITE_EXTENSION_INIT2( pApi );
      sqlite3_create_function( db, "cube", 1, SQLITE_ANY, 0, cubeFunc, null, null );
      return 0;
    }

    /*
    ** This is a broken extension entry point
    */
    static int broken_init(
    sqlite3 db,
    ref string pzErrMsg,
    sqlite3_api_routines pApi
    )
    {
      string zErr;
      sqlite3_api = pApi; //SQLITE_EXTENSION_INIT2( pApi );
      zErr = sqlite3_mprintf( "broken autoext!" );
      pzErrMsg = zErr;
      return 1;
    }

    /*
    ** tclcmd:   sqlite3_auto_extension_sqr
    **
    ** Register the "sqr" extension to be loaded automatically.
    */
    static int autoExtSqrObjCmd(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc = sqlite3_auto_extension( sqr_init );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return SQLITE_OK;
    }

    /*
    ** tclcmd:   sqlite3_auto_extension_cube
    **
    ** Register the "cube" extension to be loaded automatically.
    */
    static int autoExtCubeObjCmd(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc = sqlite3_auto_extension( cube_init );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return SQLITE_OK;
    }

    /*
    ** tclcmd:   sqlite3_auto_extension_broken
    **
    ** Register the broken extension to be loaded automatically.
    */
    static int autoExtBrokenObjCmd(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc = sqlite3_auto_extension( (dxInit)broken_init );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return SQLITE_OK;
    }

#else
// static void sqlite3_reset_auto_extension() { }
#endif //* SQLITE_OMIT_LOAD_EXTENSION */


    /*
** tclcmd:   sqlite3_reset_auto_extension
**
** Reset all auto-extensions
*/
    static int resetAutoExtObjCmd(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      sqlite3_reset_auto_extension();
      return SQLITE_OK;
    }


    /*
    ** This procedure registers the TCL procs defined in this file.
    */
    public static int Sqlitetest_autoext_Init( Tcl_Interp interp )
    {
#if !SQLITE_OMIT_LOAD_EXTENSION
      TCL.Tcl_CreateObjCommand( interp, "sqlite3_auto_extension_sqr",
      autoExtSqrObjCmd, null, null );
      TCL.Tcl_CreateObjCommand( interp, "sqlite3_auto_extension_cube",
      autoExtCubeObjCmd, null, null );
      TCL.Tcl_CreateObjCommand( interp, "sqlite3_auto_extension_broken",
      autoExtBrokenObjCmd, null, null );
#endif
      TCL.Tcl_CreateObjCommand( interp, "sqlite3_reset_auto_extension",
      resetAutoExtObjCmd, null, null );
      return TCL.TCL_OK;
    }
  }
#endif
}
