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
    ** 2008 June 18
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
    ** $Id: test_mutex.c,v 1.15 2009/03/20 13:15:30 drh Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */

    //#include "tcl.h"
    //#include "sqlite3.h"
    //#include "sqliteInt.h"
    //#include <stdlib.h>
    //#include <assert.h>
    //#include <string.h>

    /* defined in test1.c */
    //const char *sqlite3TestErrorName(int);

    /* A countable mutex */
    public class _sqlite3_mutex
    {
      public sqlite3_mutex pReal;
      public int eType;
    };

    /* State variables */
    public class test_mutex_globals
    {
      public bool isInstalled;              /* True if installed */
      public bool disableInit;              /* True to cause sqlite3_initalize() to fail */
      public bool disableTry;               /* True to force sqlite3_mutex_try() to fail */
      public bool isInit;                   /* True if initialized */
      public sqlite3_mutex_methods m;      /* Interface to "real" mutex system */
      public int[] aCounter = new int[8];              /* Number of grabs of each type of mutex */
      public sqlite3_mutex[] aStatic = new sqlite3_mutex[6];     /* The six static mutexes */
    }
    static test_mutex_globals g = new test_mutex_globals();

    /* Return true if the countable mutex is currently held */
    static int counterMutexHeld( _sqlite3_mutex p )
    {
      return g.m.xMutexHeld( p.pReal );
    }

    /* Return true if the countable mutex is not currently held */
    static int counterMutexNotheld( _sqlite3_mutex p )
    {
      return g.m.xMutexNotheld( p.pReal );
    }

    /* Initialize the countable mutex interface
    ** Or, if g.disableInit is non-zero, then do not initialize but instead
    ** return the value of g.disableInit as the result code.  This can be used
    ** to simulate an initialization failure.
    */
    static int counterMutexInit()
    {
      int rc;
      if ( g.disableInit ) return g.disableInit ? 1 : 0;
      rc = g.m.xMutexInit();
      g.isInit = true;
      return rc;
    }

    /*
    ** Uninitialize the mutex subsystem
    */
    static int counterMutexEnd()
    {
      g.isInit = false;
      return g.m.xMutexEnd();
    }

    /*
    ** Allocate a countable mutex
    */
    //static sqlite3_mutex *counterMutexAlloc(int eType){
    //  sqlite3_mutex pReal;
    //  sqlite3_mutex pRet = 0;

    //  Debug.Assert( g.isInit );
    //  Debug.Assert(eType<8 && eType>=0);

    //  pReal = g.m.xMutexAlloc(eType);
    //  if( null==pReal ) return 0;

    //  if( eType==SQLITE_MUTEX_FAST || eType==SQLITE_MUTEX_RECURSIVE ){
    //    pRet = (sqlite3_mutex *)malloc(sizeof(sqlite3_mutex));
    //  }else{
    //    pRet = &g.aStatic[eType-2];
    //  }

    //  pRet.eType = eType;
    //  pRet.pReal = pReal;
    //  return pRet;
    //}

    /*
    ** Free a countable mutex
    */
    static void counterMutexFree( _sqlite3_mutex p )
    {
      Debug.Assert( g.isInit );
      g.m.xMutexFree( p.pReal );
      if ( p.eType == SQLITE_MUTEX_FAST || p.eType == SQLITE_MUTEX_RECURSIVE )
      {
        p = null;//free(p);
      }
    }

    /*
    ** Enter a countable mutex.  Block until entry is safe.
    */
    static void counterMutexEnter( _sqlite3_mutex p )
    {
      Debug.Assert( g.isInit );
      g.aCounter[p.eType]++;
      g.m.xMutexEnter( p.pReal );
    }

    /*
    ** Try to enter a mutex.  Return true on success.
    */
    static int counterMutexTry( _sqlite3_mutex p )
    {
      Debug.Assert( g.isInit );
      g.aCounter[p.eType]++;
      if ( g.disableTry ) return SQLITE_BUSY;
      return g.m.xMutexTry( p.pReal );
    }

    /* Leave a mutex
    */
    static void counterMutexLeave( _sqlite3_mutex p )
    {
      Debug.Assert( g.isInit );
      g.m.xMutexLeave( p.pReal );
    }

    /*
    ** sqlite3_shutdown
    */
    static int test_shutdown(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc;

      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      rc = sqlite3_shutdown();
      TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_VOLATILE );
      return TCL.TCL_OK;
    }

    /*
    ** sqlite3_initialize
    */
    static int test_initialize(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv )
    {
      int rc;

      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      rc = sqlite3_initialize();
      TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_VOLATILE );
      return TCL.TCL_OK;
    }

    /*
    ** install_mutex_counters BOOLEAN
    */
    //static int test_install_mutex_counters(
    //  void * clientData,
    //  Tcl_Interp *interp,
    //  int objc,
    //  Tcl_Obj *CONST objv[]
    //){
    //  int rc = SQLITE_OK;
    //  int isInstall;

    //  sqlite3_mutex_methods counter_methods = {
    //    counterMutexInit,
    //    counterMutexEnd,
    //    counterMutexAlloc,
    //    counterMutexFree,
    //    counterMutexEnter,
    //    counterMutexTry,
    //    counterMutexLeave,
    //    counterMutexHeld,
    //    counterMutexNotheld
    //  };

    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "BOOLEAN");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.TCL_OK!=Tcl_GetBooleanFromObj(interp, objv[1], isInstall) ){
    //    return TCL.TCL_ERROR;
    //  }

    //  Debug.Assert(isInstall==0 || isInstall==1);
    //  Debug.Assert(g.isInstalled==0 || g.isInstalled==1);
    //  if( isInstall==g.isInstalled ){
    //    TCL.Tcl_AppendResult(interp, "mutex counters are ");
    //    TCL.Tcl_AppendResult(interp, isInstall?"already installed":"not installed");
    //    return TCL.TCL_ERROR;
    //  }

    //  if( isInstall ){
    //    Debug.Assert( g.m.xMutexAlloc==0 );
    //    rc = sqlite3_config(SQLITE_CONFIG_GETMUTEX, g.m);
    //    if( rc==SQLITE_OK ){
    //      sqlite3_config(SQLITE_CONFIG_MUTEX, counter_methods);
    //    }
    //    g.disableTry = 0;
    //  }else{
    //    Debug.Assert( g.m.xMutexAlloc );
    //    rc = sqlite3_config(SQLITE_CONFIG_MUTEX, g.m);
    //    memset(&g.m, 0, sizeof(sqlite3_mutex_methods));
    //  }

    //  if( rc==SQLITE_OK ){
    //    g.isInstalled = isInstall;
    //  }

    //  TCL.Tcl_SetResult(interp, (char *)sqlite3TestErrorName(rc), TCL.Tcl_VOLATILE);
    //  return TCL.TCL_OK;
    //}

    /*
    ** read_mutex_counters
    */
    //static int test_read_mutex_counters(
    //     object clientdata,
    //      Tcl_Interp interp,
    //      int objc,
    //      Tcl_Obj[] objv
    //){
    //  Tcl_Obj pRet;
    //  int ii;
    //  char *aName[8] = {
    //    "fast",        "recursive",   "static_master", "static_mem",
    //    "static_open", "static_prng", "static_lru",    "static_lru2"
    //  };

    //  if( objc!=1 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "");
    //    return TCL.TCL_ERROR;
    //  }

    //  pRet = TCL.Tcl_NewObj();
    //  TCL.Tcl_IncrRefCount(pRet);
    //  for(ii=0; ii<8; ii++){
    //    TCL.Tcl_ListObjAppendElement(interp, pRet, TCL.Tcl_NewStringObj(aName[ii], -1));
    //    TCL.Tcl_ListObjAppendElement(interp, pRet, TCL.Tcl_NewIntObj(g.aCounter[ii]));
    //  }
    //  TCL.Tcl_SetObjResult(interp, pRet);
    //  TCL.Tcl_DecrRefCount(pRet);

    //  return TCL.TCL_OK;
    //}

    /*
    ** clear_mutex_counters
    */
    static int test_clear_mutex_counters(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int ii;

      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      for ( ii = 0 ; ii < 8 ; ii++ )
      {
        g.aCounter[ii] = 0;
      }
      return TCL.TCL_OK;
    }

    /*
    ** Create and free a mutex.  Return the mutex pointer.  The pointer
    ** will be invalid since the mutex has already been freed.  The
    ** return pointer just checks to see if the mutex really was allocated.
    */
    static int test_alloc_mutex(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv )
    {
#if SQLITE_THREADSAFE
sqlite3_mutex p = sqlite3_mutex_alloc( SQLITE_MUTEX_FAST );
string zBuf = "";//[100];
sqlite3_mutex_free( ref p );
sqlite3_snprintf( 100, ref zBuf, "%p", p );
TCL.Tcl_AppendResult( interp, zBuf );
#endif
      return TCL.TCL_OK;
    }

    /*
    ** sqlite3_config OPTION
    **
    ** OPTION can be either one of the keywords:
    **
    **            SQLITE_CONFIG_SINGLETHREAD
    **            SQLITE_CONFIG_MULTITHREAD
    **            SQLITE_CONFIG_SERIALIZED
    **
    ** Or OPTION can be an raw integer.
    */
    struct ConfigOption
    {
      public string zName;
      public int iValue;
      public ConfigOption( string zName, int iValue )
      {
        this.zName = zName;
        this.iValue = iValue;
      }
    }
    static bool Tcl_GetIndexFromObjStruct( Interp interp, TclObject to, ConfigOption[] table, int s, string msg, int flags, ref int index )
    {
      try
      {
        for ( index = 0 ; index < table.Length ; index++ )
        { if ( table[index].zName == msg ) return false; }
        return true;
      }
      catch { return true; }
    }


    static int test_config(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      ConfigOption[] aOpt = new ConfigOption[] {
new ConfigOption("singlethread", SQLITE_CONFIG_SINGLETHREAD),
new ConfigOption("multithread",  SQLITE_CONFIG_MULTITHREAD),
new ConfigOption("serialized",   SQLITE_CONFIG_SERIALIZED),
new ConfigOption(null,0)
};
      int s = aOpt.Length;//sizeof(struct ConfigOption);
      int i = 0;
      int rc;

      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }

      if ( Tcl_GetIndexFromObjStruct( interp, objv[1], aOpt, s, "flag", 0, ref i ) )
      {
        if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref i ) )
        {
          return TCL.TCL_ERROR;
        }
      }
      else
      {
        i = aOpt[i].iValue;
      }

      rc = sqlite3_config( i );
      TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_VOLATILE );
      return TCL.TCL_OK;
    }

    //static sqlite3 *getDbPointer(Tcl_Interp *pInterp, Tcl_Obj *pObj){
    //sqlite3 *db;
    //Tcl_CmdInfo info;
    //char *zCmd = TCL.Tcl_GetString(pObj);
    //if( TCL.Tcl_GetCommandInfo(pInterp, zCmd, &info) ){
    //  db = *((sqlite3 **)info.objClientData);
    //}else{
    //  db = (sqlite3*)sqlite3TestTextToPtr(zCmd);
    //}
    //assert( db );
    //return db;

    //static int test_enter_db_mutex(
    //  void * clientData,
    //  Tcl_Interp *interp,
    //  int objc,
    //  Tcl_Obj *CONST objv[]
    //){
    //  sqlite3 *db;
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "DB");
    //    return TCL.TCL_ERROR;
    //  }
    //  db = getDbPointer(interp, objv[1]);
    //  if( !db ){
    //    return TCL.TCL_ERROR;
    //  }
    //  sqlite3_mutex_enter(sqlite3_db_mutex(db));
    //  return TCL.TCL_OK;
    //}

    //static int test_leave_db_mutex(
    //  void * clientData,
    //  Tcl_Interp *interp,
    //  int objc,
    //  Tcl_Obj *CONST objv[]
    //){
    //  sqlite3 *db;
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "DB");
    //    return TCL.TCL_ERROR;
    //  }
    //  db = getDbPointer(interp, objv[1]);
    //  if( !db ){
    //    return TCL.TCL_ERROR;
    //  }
    //  sqlite3_mutex_leave(sqlite3_db_mutex(db));
    //  return TCL.TCL_OK;
    //}

    static public int Sqlitetest_mutex_Init( Tcl_Interp interp )
    {
      //static struct {
      //  char *zName;
      //  Tcl_ObjCmdProc *xProc;
      //}
      _aObjCmd[] aCmd = new _aObjCmd[]{
new _aObjCmd( "sqlite3_shutdown",         test_shutdown ),
new _aObjCmd( "sqlite3_initialize",       test_initialize ),
new _aObjCmd( "sqlite3_config",           test_config ),

//new _aCmd("enter_db_mutex",          (Tcl_ObjCmdProc*)test_enter_db_mutex },
//new _aCmd( "leave_db_mutex",          (Tcl_ObjCmdProc*)test_leave_db_mutex },

//new _aCmd( "alloc_dealloc_mutex",     (Tcl_ObjCmdProc)test_alloc_mutex ),
//new _aCmd( "install_mutex_counters",  (Tcl_ObjCmdProc)test_install_mutex_counters ),
//new _aCmd( "read_mutex_counters",     (Tcl_ObjCmdProc)test_read_mutex_counters ),
//new _aCmd( "clear_mutex_counters",    (Tcl_ObjCmdProc)test_clear_mutex_counters ),
};
      int i;
      for ( i = 0 ; i < aCmd.Length ; i++ )
      {//sizeof(aCmd)/sizeof(aCmd[0]); i++){
        TCL.Tcl_CreateObjCommand( interp, aCmd[i].zName, aCmd[i].xProc, null, null );
      }

      //Tcl_LinkVar(interp, "disable_mutex_init",
      //             g.disableInit, VarFlag.SQLITE3_LINK_INT  );
      //Tcl_LinkVar(interp, "disable_mutex_try",
      //            g.disableTry, VarFlag.SQLITE3_LINK_INT  );
      return SQLITE_OK;
    }
  }
#endif
}
