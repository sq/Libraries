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

  public partial class csSQLite
  {

    /*
    ** 2007 August 15
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
    ** This file contains code used to implement test interfaces to the
    ** memory allocation subsystem.
    **
    ** $Id: test_malloc.c,v 1.54 2009/04/07 11:21:29 danielk1977 Exp $
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
    ** This structure is used to encapsulate the global state variables used
    ** by malloc() fault simulation.
    */
    struct MemFault
    {
      public int iCountdown;         /* Number of pending successes before a failure */
      public int nRepeat;            /* Number of times to repeat the failure */
      public int nBenign;            /* Number of benign failures seen since last config */
      public int nFail;              /* Number of failures seen since last config */
      public int enable;              /* True if enabled */
      public int isInstalled;        /* True if the fault simulation layer is installed */
      public int isBenignMode;       /* True if malloc failures are considered benign */
      public sqlite3_mem_methods m;  /* 'Real' malloc implementation */
    }
    static MemFault memfault;

    /*
    ** This routine exists as a place to set a breakpoint that will
    ** fire on any simulated malloc() failure.
    */
    static int cnt = 0;
    static void sqlite3Fault()
    {
      cnt++;
    }

    /*
    ** Check to see if a fault should be simulated.  Return true to simulate
    ** the fault.  Return false if the fault should not be simulated.
    */
    static int faultsimStep()
    {
      if ( likely( memfault.enable != 0 ) )
      {
        return 0;
      }
      if ( memfault.iCountdown > 0 )
      {
        memfault.iCountdown--;
        return 0;
      }
      sqlite3Fault();
      memfault.nFail++;
      if ( memfault.isBenignMode > 0 )
      {
        memfault.nBenign++;
      }
      memfault.nRepeat--;
      if ( memfault.nRepeat <= 0 )
      {
        memfault.enable = 0;
      }
      return 1;
    }

    /*
    ** A version of sqlite3_mem_methods.xMalloc() that includes fault simulation
    ** logic.
    */
    static byte[] faultsimMalloc( int n )
    {
      byte[] p = null;
      if ( faultsimStep() != 0 )
      {
        p = memfault.m.xMalloc( n );
      }
      return p;
    }


    /*
    ** A version of sqlite3_mem_methods.xRealloc() that includes fault simulation
    ** logic.
    */
    static byte[] faultsimRealloc( ref byte[] pOld, int n )
    {
      byte[] p = null;
      if ( faultsimStep() != 0 )
      {
        p = memfault.m.xRealloc( ref pOld, n );
      }
      return p;
    }

    /*
    ** The following method calls are passed directly through to the underlying
    ** malloc system:
    **
    **     xFree
    **     xSize
    **     xRoundup
    **     xInit
    **     xShutdown
    */
    static void faultsimFree( ref byte[] p )
    {
      memfault.m.xFree( ref p );
    }
    static int faultsimSize( byte[] p )
    {
      return memfault.m.xSize( p );
    }
    static int faultsimRoundup( int n )
    {
      return memfault.m.xRoundup( n );
    }
    static int faultsimInit( object p )
    {
      return memfault.m.xInit( memfault.m.pAppData );
    }
    static void faultsimShutdown( object p )
    {
      memfault.m.xShutdown( memfault.m.pAppData );
    }

    /*
    ** This routine configures the malloc failure simulation.  After
    ** calling this routine, the next nDelay mallocs will succeed, followed
    ** by a block of nRepeat failures, after which malloc() calls will begin
    ** to succeed again.
    */
    static void faultsimConfig( int nDelay, int nRepeat )
    {
      memfault.iCountdown = nDelay;
      memfault.nRepeat = nRepeat;
      memfault.nBenign = 0;
      memfault.nFail = 0;
      memfault.enable = ( nDelay >= 0 ) ? 1 : 0;

      /* Sometimes, when running multi-threaded tests, the isBenignMode
      ** variable is not properly incremented/decremented so that it is
      ** 0 when not inside a benign malloc block. This doesn't affect
      ** the multi-threaded tests, as they do not use this system. But
      ** it does affect OOM tests run later in the same process. So
      ** zero the variable here, just to be sure.
      */
      memfault.isBenignMode = 0;
    }

    /*
    ** Return the number of faults (both hard and benign faults) that have
    ** occurred since the injector was last configured.
    */
    static int faultsimFailures()
    {
      return memfault.nFail;
    }

    /*
    ** Return the number of benign faults that have occurred since the
    ** injector was last configured.
    */
    static int faultsimBenignFailures()
    {
      return memfault.nBenign;
    }

    /*
    ** Return the number of successes that will occur before the next failure.
    ** If no failures are scheduled, return -1.
    */
    static int faultsimPending()
    {
      if ( memfault.enable != 0 )
      {
        return memfault.iCountdown;
      }
      else
      {
        return -1;
      }
    }


    static void faultsimBeginBenign()
    {
      memfault.isBenignMode++;
    }
    static void faultsimEndBenign()
    {
      memfault.isBenignMode--;
    }

    /*
    ** Add or remove the fault-simulation layer using sqlite3_config(). If
    ** the argument is non-zero, the
    */
    static int faultsimInstall( int install )
    {
      sqlite3_mem_methods m = new sqlite3_mem_methods(
      (dxMalloc)faultsimMalloc,                 /* xMalloc */
      (dxFree)faultsimFree,                     /* xFree */
      (dxRealloc)faultsimRealloc,               /* xRealloc */
      (dxSize)faultsimSize,                     /* xSize */
      (dxRoundup)faultsimRoundup,               /* xRoundup */
      (dxMemInit)faultsimInit,                  /* xInit */
      (dxMemShutdown)faultsimShutdown,          /* xShutdown */
      null                                      /* pAppData */
      );
      int rc;

      //install = ( install != 0 ? 1 : 0 );
      Debug.Assert( memfault.isInstalled == 1 || memfault.isInstalled == 0 );

      if ( install == memfault.isInstalled )
      {
        return SQLITE_ERROR;
      }

      if ( install != 0 )
      {
        rc = sqlite3_config( SQLITE_CONFIG_GETMALLOC, ref  memfault.m );
        Debug.Assert( memfault.m.xMalloc != null );
        if ( rc == SQLITE_OK )
        {
          rc = sqlite3_config( SQLITE_CONFIG_MALLOC, m );
        }
        sqlite3_test_control( SQLITE_TESTCTRL_BENIGN_MALLOC_HOOKS,
        (void_function)faultsimBeginBenign, (void_function)faultsimEndBenign
        );
      }
      else
      {
        //sqlite3_mem_methods m;
        Debug.Assert( memfault.m.xMalloc != null );

        /* One should be able to reset the default memory allocator by storing
        ** a zeroed allocator then calling GETMALLOC. */
        m = new sqlite3_mem_methods();// memset( &m, 0, sizeof( m ) );
        sqlite3_config( SQLITE_CONFIG_MALLOC, m );
        sqlite3_config( SQLITE_CONFIG_GETMALLOC, ref m );
        Debug.Assert( m.GetHashCode() == memfault.m.GetHashCode() );//memcmp(&m, &memfault.m, sizeof(m))==0 );

        rc = sqlite3_config( SQLITE_CONFIG_MALLOC, ref memfault.m );
        sqlite3_test_control( SQLITE_TESTCTRL_BENIGN_MALLOC_HOOKS, 0, 0 );
      }

      if ( rc == SQLITE_OK )
      {
        memfault.isInstalled = 1;
      }
      return rc;
    }

#if SQLITE_TEST

    /*
** This function is implemented in test1.c. Returns a pointer to a static
** buffer containing the symbolic SQLite error code that corresponds to
** the least-significant 8-bits of the integer passed as an argument.
** For example:
**
**   sqlite3TestErrorName(1) -> "SQLITE_ERROR"
*/
    //const char *sqlite3TestErrorName(int);


    /*
    ** Transform pointers to text and back again
    */
    //static void pointerToText(void p, char *z){
    //  static const char zHex[] = "0123456789abcdef";
    //  int i, k;
    //  unsigned int u;
    //  sqlite3_u3264 n;
    //    if( p==0 ){
    //  strcpy(z, "0");
    //  return;
    //}
    //  if( sizeof(n)==sizeof(p) ){
    //    memcpy(&n, ref p, sizeof(p));
    //  }else if( sizeof(u)==sizeof(p) ){
    //    memcpy(&u, ref p, sizeof(u));
    //    n = u;
    //  }else{
    //    Debug.Assert( 0 );
    //  }
    //  for(i=0, k=sizeof(p)*2-1; i<sizeof(p)*2; i++, k--){
    //    z[k] = zHex[n&0xf];
    //    n >>= 4;
    //  }
    //  z[sizeof(p)*2] = 0;
    //}
    //static int hexToInt(int h){
    //  if( h>='0' && h<='9' ){
    //    return h - '0';
    //  }else if( h>='a' && h<='f' ){
    //    return h - 'a' + 10;
    //  }else{
    //    return -1;
    //  }
    //}
    //static int textToPointer(string z, void **pp){
    //  sqlite3_u3264 n = 0;
    //  int i;
    //  unsigned int u;
    //  for(i=0; i<sizeof(void*)*2 && z[0]; i++){
    //    int v;
    //    v = hexToInt(*z++);
    //    if( v<0 ) return TCL.TCL_ERROR;
    //    n = n*16 + v;
    //  }
    //  if( *z!=0 ) return TCL.TCL_ERROR;
    //  if( sizeof(n)==sizeof(*pp) ){
    //    memcpy(pp, ref n, sizeof(n));
    //  }else if( sizeof(u)==sizeof(*pp) ){
    //    u = (unsigned int)n;
    //    memcpy(pp, ref u, sizeof(u));
    //  }else{
    //    Debug.Assert( 0 );
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3Malloc  NBYTES
    **
    ** Raw test interface for sqlite3Malloc().
    */
    //static int test_malloc(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int nByte;
    //  void p;
    //  char zOut[100];
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "NBYTES");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[1], ref nByte) ) return TCL.TCL_ERROR;
    //  p = sqlite3Malloc((unsigned)nByte);
    //  pointerToText(p, zOut);
    //  TCL.TCL_AppendResult(interp, zOut, NULL);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_realloc  PRIOR  NBYTES
    **
    ** Raw test interface for sqlite3_realloc().
    */
    //static int test_realloc(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int nByte;
    //  void pPrior, p;
    //  char zOut[100];
    //  if( objc!=3 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "PRIOR NBYTES");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[2], ref nByte) ) return TCL.TCL_ERROR;
    //  if( textToPointer(Tcl_GetString(objv[1]), ref pPrior) ){
    //    TCL.TCL_AppendResult(interp, "bad pointer: ", TCL.Tcl_GetString(objv[1]));
    //    return TCL.TCL_ERROR;
    //  }
    //  p = sqlite3_realloc(pPrior, (unsigned)nByte);
    //  pointerToText(p, zOut);
    //  TCL.TCL_AppendResult(interp, zOut, NULL);
    //  return TCL.TCL_OK;
    //}


    /*
    ** Usage:    sqlite3_free  PRIOR
    **
    ** Raw test interface for sqlite3DbFree(db,).
    */
    //static int test_free(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  void pPrior;
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "PRIOR");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( textToPointer(Tcl_GetString(objv[1]), ref pPrior) ){
    //    TCL.TCL_AppendResult(interp, "bad pointer: ", TCL.Tcl_GetString(objv[1]));
    //    return TCL.TCL_ERROR;
    //  }
    //  sqlite3DbFree(db,pPrior);
    //  return TCL.TCL_OK;
    //}

    /*
    ** These routines are in test_hexio.c
    */
    //int sqlite3TestHexToBin(const char *, int, char *);
    //int sqlite3TestBinToHex(char*,int);

    /*
    ** Usage:    memset  ADDRESS  SIZE  HEX
    **
    ** Set a chunk of memory (obtained from malloc, probably) to a
    ** specified hex pattern.
    */
    //static int test_memset(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  void p;
    //  int size, n, i;
    //  char *zHex;
    //  char *zOut;
    //  char zBin[100];

    //  if( objc!=4 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "ADDRESS SIZE HEX");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( textToPointer(Tcl_GetString(objv[1]), ref p) ){
    //    TCL.TCL_AppendResult(interp, "bad pointer: ", TCL.Tcl_GetString(objv[1]));
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[2], ref size) ){
    //    return TCL.TCL_ERROR;
    //  }
    //  if( size<=0 ){
    //    TCL.TCL_AppendResult(interp, "size must be positive");
    //    return TCL.TCL_ERROR;
    //  }
    //  zHex = TCL.Tcl_GetStringFromObj(objv[3], ref n);
    //  if( n>sizeof(zBin)*2 ) n = sizeof(zBin)*2;
    //  n = sqlite3TestHexToBin(zHex, n, zBin);
    //  if( n==0 ){
    //    TCL.TCL_AppendResult(interp, "no data");
    //    return TCL.TCL_ERROR;
    //  }
    //  zOut = p;
    //  for(i=0; i<size; i++){
    //    zOut[i] = zBin[i%n];
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    memget  ADDRESS  SIZE
    **
    ** Return memory as hexadecimal text.
    */
    //static int test_memget(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  void p;
    //  int size, n;
    //  char *zBin;
    //  char zHex[100];

    //  if( objc!=3 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "ADDRESS SIZE");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( textToPointer(Tcl_GetString(objv[1]), ref p) ){
    //    TCL.TCL_AppendResult(interp, "bad pointer: ", TCL.Tcl_GetString(objv[1]));
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[2], ref size) ){
    //    return TCL.TCL_ERROR;
    //  }
    //  if( size<=0 ){
    //    TCL.TCL_AppendResult(interp, "size must be positive");
    //    return TCL.TCL_ERROR;
    //  }
    //  zBin = p;
    //  while( size>0 ){
    //    if( size>(sizeof(zHex)-1)/2 ){
    //      n = (sizeof(zHex)-1)/2;
    //    }else{
    //      n = size;
    //    }
    //    memcpy(zHex, zBin, n);
    //    zBin += n;
    //    size -= n;
    //    sqlite3TestBinToHex(zHex, n);
    //    TCL.TCL_AppendResult(interp, zHex);
    //  }
    //  return TCL.TCL_OK;
    //}
#if FALSE
    /*
    ** Usage:    sqlite3_memory_used
    **
    ** Raw test interface for sqlite3_memory_used().
    */
    static int test_memory_used(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewWideIntObj( sqlite3_memory_used() ) );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:    sqlite3_memory_highwater ?RESETFLAG?
    **
    ** Raw test interface for sqlite3_memory_highwater().
    */
    static int test_memory_highwater(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      bool resetFlag = false;
      if ( objc != 1 && objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "?RESET?" );
        return TCL.TCL_ERROR;
      }
      if ( objc == 2 )
      {
        if ( TCL.Tcl_GetBooleanFromObj( interp, objv[1], ref  resetFlag ) ) return TCL.TCL_ERROR;
      }
      TCL.Tcl_SetObjResult( interp,
      TCL.Tcl_NewWideIntObj( sqlite3_memory_highwater( resetFlag ? 1 : 0 ) ) );
      return TCL.TCL_OK;
    }
#endif

    /*
    ** Usage:    sqlite3_memdebug_backtrace DEPTH
    **
    ** Set the depth of backtracing.  If SQLITE_MEMDEBUG is not defined
    ** then this routine is a no-op.
    */
    //static int test_memdebug_backtrace(
    //  object clientdata,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int depth;
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "DEPT");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[1], ref depth) ) return TCL.TCL_ERROR;
    //#if SQLITE_MEMDEBUG
    //  {
    //    extern void sqlite3MemdebugBacktrace(int);
    //    sqlite3MemdebugBacktrace(depth);
    //  }
    //#endif
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_memdebug_dump  FILENAME
    **
    ** Write a summary of unfreed memory to FILENAME.
    */
    static int test_memdebug_dump(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "FILENAME" );
        return TCL.TCL_ERROR;
      }
#if (SQLITE_MEMDEBUG) || (SQLITE_MEMORY_SIZE) || (SQLITE_POW2_MEMORY_SIZE)
{
extern void sqlite3MemdebugDump(const char*);
sqlite3MemdebugDump(Tcl_GetString(objv[1]));
}
#endif
      return TCL.TCL_OK;
    }

    /*
    ** Usage:    sqlite3_memdebug_malloc_count
    **
    ** Return the total number of times malloc() has been called.
    */
    static int test_memdebug_malloc_count(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int nMalloc = -1;
      if ( objc != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "" );
        return TCL.TCL_ERROR;
      }
#if SQLITE_MEMDEBUG
{
extern int sqlite3MemdebugMallocCount();
nMalloc = sqlite3MemdebugMallocCount();
}
#endif
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( nMalloc ) );
      return TCL.TCL_OK;
    }


    /*
    ** Usage:    sqlite3_memdebug_fail  COUNTER  ?OPTIONS?
    **
    ** where options are:
    **
    **     -repeat    <count>
    **     -benigncnt <varname>
    **
    ** Arrange for a simulated malloc() failure after COUNTER successes.
    ** If a repeat count is specified, the fault is repeated that many
    ** times.
    **
    ** Each call to this routine overrides the prior counter value.
    ** This routine returns the number of simulated failures that have
    ** happened since the previous call to this routine.
    **
    ** To disable simulated failures, use a COUNTER of -1.
    */
    static int test_memdebug_fail(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int ii;
      int iFail = 0;
      int nRepeat = 1;
      Tcl_Obj pBenignCnt = null;
      int nBenign;
      int nFail = 0;

      if ( objc < 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "COUNTER ?OPTIONS?" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref  iFail ) ) return TCL.TCL_ERROR;

      for ( ii = 2 ; ii < objc ; ii += 2 )
      {
        int nOption = 0;
        string zOption = TCL.Tcl_GetStringFromObj( objv[ii], ref  nOption );
        string zErr = "";

        if ( nOption > 1 && zOption == "-repeat" )
        {
          if ( ii == ( objc - 1 ) )
          {
            zErr = "option requires an argument: ";
          }
          else
          {
            if ( TCL.Tcl_GetIntFromObj( interp, objv[ii + 1], ref  nRepeat ) )
            {
              return TCL.TCL_ERROR;
            }
          }
        }
        else if ( nOption > 1 && zOption == "-benigncnt" )
        {
          if ( ii == ( objc - 1 ) )
          {
            zErr = "option requires an argument: ";
          }
          else
          {
            pBenignCnt = objv[ii + 1];
          }
        }
        else
        {
          zErr = "unknown option: ";
        }

        if ( zErr != "" )
        {
          TCL.Tcl_AppendResult( interp, zErr, zOption );
          return TCL.TCL_ERROR;
        }
      }

      nBenign = faultsimBenignFailures();
      nFail = faultsimFailures();
      faultsimConfig( iFail, nRepeat );
      if ( pBenignCnt != null )
      {
        TCL.Tcl_ObjSetVar2( interp, pBenignCnt, null, TCL.Tcl_NewIntObj( nBenign ), 0 );
      }
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( nFail ) );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:    sqlite3_memdebug_pending
    **
    ** Return the number of malloc() calls that will succeed before a
    ** simulated failure occurs. A negative return value indicates that
    ** no malloc() failure is scheduled.
    */
    //   static int test_memdebug_pending(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int nPending;
    //  if( objc!=1 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "");
    //    return TCL.TCL_ERROR;
    //  }
    //  nPending = faultsimPending();
    //  TCL.Tcl_SetObjResult(interp, TCL.Tcl_NewIntObj(nPending));
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_memdebug_settitle TITLE
    **
    ** Set a title string stored with each allocation.  The TITLE is
    ** typically the name of the test that was running when the
    ** allocation occurred.  The TITLE is stored with the allocation
    ** and can be used to figure out which tests are leaking memory.
    **
    ** Each title overwrite the previous.
    */
    static int test_memdebug_settitle(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      string zTitle;
      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "TITLE" );
        return TCL.TCL_ERROR;
      }
      zTitle = TCL.Tcl_GetString( objv[1] );
#if SQLITE_MEMDEBUG
{
extern int sqlite3MemdebugSettitle(const char*);
sqlite3MemdebugSettitle(zTitle);
}
#endif
      return TCL.TCL_OK;
    }

    //#define MALLOC_LOG_FRAMES 10
    //static TCL.Tcl_HashTable aMallocLog;
    //static int mallocLogEnabled = 0;

    //typedef struct MallocLog MallocLog;
    //struct MallocLog {
    //  int nCall;
    //  int nByte;
    //};

    //#if SQLITE_MEMDEBUG
    //static void test_memdebug_callback(int nByte, int nFrame, void **aFrame){
    //  if( mallocLogEnabled ){
    //    MallocLog pLog;
    //    TCL.Tcl_HashEntry pEntry;
    //    int isNew;

    //    int aKey[MALLOC_LOG_FRAMES];
    //    int nKey = sizeof(int)*MALLOC_LOG_FRAMES;

    //    memset(aKey, 0, nKey);
    //    if( (sizeof(void*)*nFrame)<nKey ){
    //      nKey = nFrame*sizeof(void*);
    //    }
    //    memcpy(aKey, aFrame, nKey);

    //    pEntry = TCL.Tcl_CreateHashEntry(&aMallocLog, (const char *)aKey, ref isNew);
    //    if( isNew ){
    //      pLog = (MallocLog *)Tcl_Alloc(sizeof(MallocLog));
    //      memset(pLog, 0, sizeof(MallocLog));
    //      TCL.Tcl_SetHashValue(pEntry, (ClientData)pLog);
    //    }else{
    //      pLog = (MallocLog *)Tcl_GetHashValue(pEntry);
    //    }

    //    pLog->nCall++;
    //    pLog->nByte += nByte;
    //  }
    //}
    //#endif /* SQLITE_MEMDEBUG */

    //static void test_memdebug_log_clear(){
    //  TCL.Tcl_HashSearch search;
    //  TCL.Tcl_HashEntry pEntry;
    //  for(
    //    pEntry=Tcl_FirstHashEntry(&aMallocLog, ref search);
    //    pEntry;
    //    pEntry=Tcl_NextHashEntry(&search)
    //  ){
    //    MallocLog pLog = (MallocLog *)Tcl_GetHashValue(pEntry);
    //    TCL.Tcl_Free((char *)pLog);
    //  }
    //  TCL.Tcl_DeleteHashTable(&aMallocLog);
    //  TCL.Tcl_InitHashTable(&aMallocLog, MALLOC_LOG_FRAMES);
    //}

    //static int test_memdebug_log(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  static int isInit = 0;
    //  int iSub;

    //  static const char *MB_strs[] = { "start", "stop", "dump", "clear", "sync" };
    //  enum MB_enum {
    //      MB_LOG_START, MB_LOG_STOP, MB_LOG_DUMP, MB_LOG_CLEAR, MB_LOG_SYNC
    //  };

    //  if( !isInit ){
    //#if SQLITE_MEMDEBUG
    //    extern void sqlite3MemdebugBacktraceCallback(
    //        void (*xBacktrace)(int, int, void **));
    //    sqlite3MemdebugBacktraceCallback(test_memdebug_callback);
    //#endif
    //    TCL.Tcl_InitHashTable(&aMallocLog, MALLOC_LOG_FRAMES);
    //    isInit = 1;
    //  }

    //  if( objc<2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "SUB-COMMAND ...");
    //  }
    //  if( TCL.Tcl_GetIndexFromObj(interp, objv[1], MB_strs, "sub-command", 0, ref iSub) ){
    //    return TCL.TCL_ERROR;
    //  }

    //  switch( (enum MB_enum)iSub ){
    //    case MB_LOG_START:
    //      mallocLogEnabled = 1;
    //      break;
    //    case MB_LOG_STOP:
    //      mallocLogEnabled = 0;
    //      break;
    //    case MB_LOG_DUMP: {
    //      TCL.Tcl_HashSearch search;
    //      TCL.Tcl_HashEntry pEntry;
    //      Tcl_Obj pRet = TCL.Tcl_NewObj();

    //      Debug.Assert(sizeof(int)==sizeof(void*));

    //      for(
    //        pEntry=Tcl_FirstHashEntry(&aMallocLog, ref search);
    //        pEntry;
    //        pEntry=Tcl_NextHashEntry(&search)
    //      ){
    //        Tcl_Obj *apElem[MALLOC_LOG_FRAMES+2];
    //        MallocLog pLog = (MallocLog *)Tcl_GetHashValue(pEntry);
    //        int *aKey = (int *)Tcl_GetHashKey(&aMallocLog, pEntry);
    //        int ii;

    //        apElem[0] = TCL.Tcl_NewIntObj(pLog->nCall);
    //        apElem[1] = TCL.Tcl_NewIntObj(pLog->nByte);
    //        for(ii=0; ii<MALLOC_LOG_FRAMES; ii++){
    //          apElem[ii+2] = TCL.Tcl_NewIntObj(aKey[ii]);
    //        }

    //        TCL.Tcl_ListObjAppendElement(interp, pRet,
    //            TCL.Tcl_NewListObj(MALLOC_LOG_FRAMES+2, apElem)
    //        );
    //      }

    //      TCL.Tcl_SetObjResult(interp, pRet);
    //      break;
    //    }
    //    case MB_LOG_CLEAR: {
    //      test_memdebug_log_clear();
    //      break;
    //    }

    //    case MB_LOG_SYNC: {
    //#if SQLITE_MEMDEBUG
    //      extern void sqlite3MemdebugSync();
    //      test_memdebug_log_clear();
    //      mallocLogEnabled = 1;
    //      sqlite3MemdebugSync();
    //#endif
    //      break;
    //    }
    //  }

    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_config_scratch SIZE N
    **
    ** Set the scratch memory buffer using SQLITE_CONFIG_SCRATCH.
    ** The buffer is static and is of limited size.  N might be
    ** adjusted downward as needed to accomodate the requested size.
    ** The revised value of N is returned.
    **
    ** A negative SIZE causes the buffer pointer to be NULL.
    */
    static int test_config_scratch(
    object clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int sz = 0, N = 0, rc;
      Tcl_Obj pResult;
      byte[] buf = null;
      if ( objc != 3 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "SIZE N" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref sz ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref N ) ) return TCL.TCL_ERROR;
      //free(buf);
      if ( sz < 0 )
      {
        buf = null;
        rc = sqlite3_config( SQLITE_CONFIG_SCRATCH, 0, 0, 0 );
      }
      else
      {
        buf = new byte[sz * N + 1];// malloc( sz * N + 1 );
        rc = sqlite3_config( SQLITE_CONFIG_SCRATCH, buf, sz, N );
      }
      pResult = TCL.Tcl_NewObj();
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( rc ) );
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( N ) );
      TCL.Tcl_SetObjResult( interp, pResult );
      return TCL.TCL_OK;
    }


    /*
    ** Usage:    sqlite3_config_pagecache SIZE N
    **
    ** Set the page-cache memory buffer using SQLITE_CONFIG_PAGECACHE.
    ** The buffer is static and is of limited size.  N might be
    ** adjusted downward as needed to accomodate the requested size.
    ** The revised value of N is returned.
    **
    ** A negative SIZE causes the buffer pointer to be NULL.
    */
    static int test_config_pagecache(
    object clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int sz = 0, N = 0, rc;
      Tcl_Obj pResult;
      MemPage buf; // byte[] buf = null;
      if ( objc != 3 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "SIZE N" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref sz ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref N ) ) return TCL.TCL_ERROR;
      //free(buf);
      if ( sz < 0 )
      {
        buf = null;
        rc = sqlite3_config( SQLITE_CONFIG_PAGECACHE, 0, 0, 0 );
      }
      else
      {
        buf = new MemPage();// new byte[sz * N]; //malloc( sz * N );
        rc = sqlite3_config( SQLITE_CONFIG_PAGECACHE, buf, sz, N );
      }
      pResult = TCL.Tcl_NewObj();
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( rc ) );
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( N ) );
      TCL.Tcl_SetObjResult( interp, pResult );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:    sqlite3_config_alt_pcache INSTALL_FLAG DISCARD_CHANCE PRNG_SEED
    **
    ** Set up the alternative test page cache.  Install if INSTALL_FLAG is
    ** true and uninstall (reverting to the default page cache) if INSTALL_FLAG
    ** is false.  DISCARD_CHANGE is an integer between 0 and 100 inclusive
    ** which determines the chance of discarding a page when unpinned.  100
    ** is certainty.  0 is never.  PRNG_SEED is the pseudo-random number generator
    ** seed.
    */
    //static int test_alt_pcache(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int installFlag;
    //  int discardChance = 0;
    //  int prngSeed = 0;
    //  int highStress = 0;
    //  extern void installTestPCache(int,unsigned,unsigned,unsigned);
    //  if( objc<2 || objc>5 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv,
    //        "INSTALLFLAG DISCARDCHANCE PRNGSEEED HIGHSTRESS");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, objv[1], &installFlag) ) return TCL.TCL_ERROR;
    //  if( objc>=3 && TCL.Tcl_GetIntFromObj(interp, objv[2], &discardChance) ){
    //     return TCL.TCL_ERROR;
    //  }
    //  if( objc>=4 && TCL.Tcl_GetIntFromObj(interp, objv[3], &prngSeed) ){
    //     return TCL.TCL_ERROR;
    //  }
    //  if( objc>=5 && TCL.Tcl_GetIntFromObj(interp, objv[4], &highStress) ){
    //    return TCL.TCL_ERROR;
    //  }
    //  if( discardChance<0 || discardChance>100 ){
    //    TCL.Tcl_AppendResult(interp, "discard-chance should be between 0 and 100",
    //                     (char*)0);
    //    return TCL.TCL_ERROR;
    //  }
    //  installTestPCache(installFlag, (unsigned)discardChance, (unsigned)prngSeed,
    //                    (unsigned)highStress);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_config_memstatus BOOLEAN
    **
    ** Enable or disable memory status reporting using SQLITE_CONFIG_MEMSTATUS.
    */
    //static int test_config_memstatus(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int enable, rc;
    //   if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "BOOLEAN");
    //     return TCL.TCL_ERROR;
    //}
    //  if( TCL.Tcl_GetBooleanFromObj(interp, objv[1], &enable) ) return TCL.TCL_ERROR;
    //  rc = sqlite3_config(SQLITE_CONFIG_MEMSTATUS, enable);
    //  TCL.Tcl_SetObjResult(interp, TCL.Tcl_NewIntObj(rc));
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_config_lookaside  SIZE  COUNT
    **
    */
    static int test_config_lookaside(
    object clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc;
      int sz = 0, cnt = 0;
      Tcl_Obj pRet;
      if ( objc != 3 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "SIZE COUNT" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref sz ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref cnt ) ) return TCL.TCL_ERROR;
      pRet = TCL.Tcl_NewObj();
      TCL.Tcl_ListObjAppendElement(
      interp, pRet, TCL.Tcl_NewIntObj( sqlite3GlobalConfig.szLookaside )
      );
      TCL.Tcl_ListObjAppendElement(
      interp, pRet, TCL.Tcl_NewIntObj( sqlite3GlobalConfig.nLookaside )
      );
      rc = sqlite3_config( SQLITE_CONFIG_LOOKASIDE, sz, cnt );
      TCL.Tcl_SetObjResult( interp, pRet );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:    sqlite3_db_config_lookaside  CONNECTION  BUFID  SIZE  COUNT
    **
    ** There are two static buffers with BUFID 1 and 2.   Each static buffer
    ** is 10KB in size.  A BUFID of 0 indicates that the buffer should be NULL
    ** which will cause sqlite3_db_config() to allocate space on its own.
    */
    static int test_db_config_lookaside(
    object clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc;
      int sz = 0, cnt = 0;
      sqlite3 db = new sqlite3();
      int bufid = 0;
      byte[][] azBuf = new byte[2][];
      //int getDbPointer(Tcl_Interp*, const char*, sqlite3**);
      if ( objc != 5 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "BUFID SIZE COUNT" );
        return TCL.TCL_ERROR;
      }
      if ( getDbPointer( interp, TCL.Tcl_GetString( objv[1] ), ref db ) != 0 ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref bufid ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[3], ref sz ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetIntFromObj( interp, objv[4], ref cnt ) ) return TCL.TCL_ERROR;
      if ( bufid == 0 )
      {
        rc = sqlite3_db_config( db, SQLITE_DBCONFIG_LOOKASIDE, null, sz, cnt );
      }
      else if ( bufid >= 1 && bufid <= 2 && sz * cnt <= azBuf[0].Length )
      {
        rc = sqlite3_db_config( db, SQLITE_DBCONFIG_LOOKASIDE, azBuf[bufid], sz, cnt );
      }
      else
      {
        TCL.Tcl_AppendResult( interp, "illegal arguments - see documentation" );
        return TCL.TCL_ERROR;
      }
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:
    **
    **   sqlite3_config_heap NBYTE NMINALLOC
    */
    //static int test_config_heap(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  static char *zBuf; /* Use this memory */
    //  static int szBuf;  /* Bytes allocated for zBuf */
    //  int nByte;         /* Size of buffer to pass to sqlite3_config() */
    //  int nMinAlloc;     /* Size of minimum allocation */
    //  int rc;            /* Return code of sqlite3_config() */

    //  Tcl_Obj * CONST *aArg = &objv[1];
    //  int nArg = objc-1;

    //  if( nArg!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "NBYTE NMINALLOC");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetIntFromObj(interp, aArg[0], ref nByte) ) return TCL.TCL_ERROR;
    //  if( TCL.Tcl_GetIntFromObj(interp, aArg[1], ref nMinAlloc) ) return TCL.TCL_ERROR;

    //  if( nByte==0 ){
    //    free( zBuf );
    //    zBuf = 0;
    //    szBuf = 0;
    //    rc = sqlite3_config(SQLITE_CONFIG_HEAP, (void*)0, 0, 0);
    //  }else{
    //    zBuf = realloc(zBuf, nByte);
    //    szBuf = nByte;
    //    rc = sqlite3_config(SQLITE_CONFIG_HEAP, zBuf, nByte, nMinAlloc);
    //  }

    //  TCL.Tcl_SetResult(interp, (char *)sqlite3TestErrorName(rc), TCL.Tcl_VOLATILE);
    //  return TCL.TCL_OK;
    //}

    /*
    ** tclcmd:     sqlite3_config_error  [DB]
    **
    ** Invoke sqlite3_config() or sqlite3_db_config() with invalid
    ** opcodes and verify that they return errors.
    */
    //static int test_config_error(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  sqlite3 db;
    //  int getDbPointer(Tcl_Interp*, const char*, sqlite3**);

    //  if( objc!=2 && objc!=1 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "[DB]");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( objc==2 ){
    //    if( getDbPointer(interp, TCL.Tcl_GetString(objv[1]), ref db) ) return TCL.TCL_ERROR;
    //    if( sqlite3_db_config(db, 99999)!=SQLITE_ERROR ){
    //      TCL.Tcl_AppendResult(interp,
    //            "sqlite3_db_config(db, 99999) does not return SQLITE_ERROR",
    //            (char*)0);
    //      return TCL.TCL_ERROR;
    //    }
    //  }else{
    //    if( sqlite3_config(99999)!=SQLITE_ERROR ){
    //      TCL.Tcl_AppendResult(interp,
    //          "sqlite3_config(99999) does not return SQLITE_ERROR",
    //          (char*)0);
    //      return TCL.TCL_ERROR;
    //    }
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:
    **
    **   sqlite3_dump_memsys3  FILENAME
    **   sqlite3_dump_memsys5  FILENAME
    **
    ** Write a summary of unfreed memsys3 allocations to FILENAME.
    */
    //static int test_dump_memsys3(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  if( objc!=2 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "FILENAME");
    //    return TCL.TCL_ERROR;
    //  }

    //  switch( (int)clientData ){
    //    case 3: {
    //#if SQLITE_ENABLE_MEMSYS3
    //      extern void sqlite3Memsys3Dump(const char*);
    //      sqlite3Memsys3Dump(Tcl_GetString(objv[1]));
    //      break;
    //#endif
    //    }
    //    case 5: {
    //#if SQLITE_ENABLE_MEMSYS5
    //      extern void sqlite3Memsys5Dump(const char*);
    //      sqlite3Memsys5Dump(Tcl_GetString(objv[1]));
    //      break;
    //#endif
    //    }
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_status  OPCODE  RESETFLAG
    **
    ** Return a list of three elements which are the sqlite3_status() return
    ** code, the current value, and the high-water mark value.
    */
    class _aOp
    {
      public string zName;
      public int op;
      public _aOp( string zName, int op ) { this.zName = zName; this.op = op; }
    }

    static int test_status(
    object clientdata,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc, iValue, mxValue;
      int i, op = 0;
      bool resetFlag = false;
      string zOpName;

      _aOp[] aOp = new _aOp[] {new _aOp( "SQLITE_STATUS_MEMORY_USED",         SQLITE_STATUS_MEMORY_USED         ),
new _aOp(  "SQLITE_STATUS_MALLOC_SIZE",         SQLITE_STATUS_MALLOC_SIZE         ),
new _aOp( "SQLITE_STATUS_PAGECACHE_USED",      SQLITE_STATUS_PAGECACHE_USED      ),
new _aOp( "SQLITE_STATUS_PAGECACHE_OVERFLOW",  SQLITE_STATUS_PAGECACHE_OVERFLOW  ),
new _aOp(  "SQLITE_STATUS_PAGECACHE_SIZE",      SQLITE_STATUS_PAGECACHE_SIZE      ),
new _aOp( "SQLITE_STATUS_SCRATCH_USED",        SQLITE_STATUS_SCRATCH_USED        ),
new _aOp( "SQLITE_STATUS_SCRATCH_OVERFLOW",    SQLITE_STATUS_SCRATCH_OVERFLOW    ),
new _aOp( "SQLITE_STATUS_SCRATCH_SIZE",        SQLITE_STATUS_SCRATCH_SIZE        ),
new _aOp( "SQLITE_STATUS_PARSER_STACK",        SQLITE_STATUS_PARSER_STACK        ),
};
      Tcl_Obj pResult;
      if ( objc != 3 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "PARAMETER RESETFLAG" );
        return TCL.TCL_ERROR;
      }
      zOpName = TCL.Tcl_GetString( objv[1] );
      for ( i = 0 ; i < ArraySize( aOp ) ; i++ )
      {
        if ( aOp[i].zName == zOpName )
        {//strcmp(aOp[i].zName, zOpName)==0 ){
          op = aOp[i].op;
          break;
        }
      }
      if ( i >= ArraySize( aOp ) )
      {
        if ( TCL.Tcl_GetIntFromObj( interp, objv[1], ref  op ) ) return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetBooleanFromObj( interp, objv[2], ref  resetFlag ) ) return TCL.TCL_ERROR;
      iValue = 0;
      mxValue = 0;
      rc = sqlite3_status( op, ref  iValue, ref  mxValue, resetFlag ? 1 : 0 );
      pResult = TCL.Tcl_NewObj();
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( rc ) );
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( iValue ) );
      TCL.Tcl_ListObjAppendElement( null, pResult, TCL.Tcl_NewIntObj( mxValue ) );
      TCL.Tcl_SetObjResult( interp, pResult );
      return TCL.TCL_OK;
    }
    /*
    ** Usage:    sqlite3_db_status  DATABASE  OPCODE  RESETFLAG
    **
    ** Return a list of three elements which are the sqlite3_db_status() return
    ** code, the current value, and the high-water mark value.
    */
    //static int test_db_status(
    //object  clientData,
    //Tcl_Interp interp,
    //int objc,
    //Tcl_Obj[] objv
    //){
    //int rc, iValue, mxValue;
    //int i, op, resetFlag;
    //const char *zOpName;
    //sqlite3 db;
    //int getDbPointer(Tcl_Interp*, const char*, sqlite3**);
    //static const struct {
    //const char *zName;
    //int op;
    //} aOp[] = {
    //{ "SQLITE_DBSTATUS_LOOKASIDE_USED",    SQLITE_DBSTATUS_LOOKASIDE_USED   },
    //};
    //Tcl_Obj pResult;
    //if( objc!=4 ){
    //Tcl_WrongNumArgs(interp, 1, objv, "PARAMETER RESETFLAG");
    //return TCL.TCL_ERROR;
    //}
    //if( getDbPointer(interp, TCL.Tcl_GetString(objv[1]), ref db) ) return TCL.TCL_ERROR;
    //zOpName = TCL.Tcl_GetString(objv[2]);
    //for(i=0; i<ArraySize(aOp); i++){
    //if( strcmp(aOp[i].zName, zOpName)==0 ){
    //op = aOp[i].op;
    //break;
    //}
    //}
    //if( i>=ArraySize(aOp) ){
    //if( TCL.Tcl_GetIntFromObj(interp, objv[2], ref op) ) return TCL.TCL_ERROR;
    //}
    //if( TCL.Tcl_GetBooleanFromObj(interp, objv[3], ref resetFlag) ) return TCL.TCL_ERROR;
    //iValue = 0;
    //mxValue = 0;
    //rc = sqlite3_db_status(db, op, ref iValue, ref mxValue, resetFlag);
    //pResult = TCL.Tcl_NewObj();
    //Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(rc));
    //Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(iValue));
    //Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(mxValue));
    //Tcl_SetObjResult(interp, pResult);
    //return TCL.TCL_OK;
    //}

    /*
    ** Usage:    sqlite3_db_status  DATABASE  OPCODE  RESETFLAG
    **
    ** Return a list of three elements which are the sqlite3_db_status() return
    ** code, the current value, and the high-water mark value.
    */
    //static int test_db_status(
    //  object  clientData,
    //  Tcl_Interp interp,
    //  int objc,
    //  Tcl_Obj[] objv
    //){
    //  int rc, iValue, mxValue;
    //  int i, op, resetFlag;
    //  const char *zOpName;
    //  sqlite3 db;
    //  int getDbPointer(Tcl_Interp*, const char*, sqlite3**);
    //  static const struct {
    //    const char *zName;
    //    int op;
    //  } aOp[] = {
    //    { "SQLITE_DBSTATUS_LOOKASIDE_USED",    SQLITE_DBSTATUS_LOOKASIDE_USED   },
    //  };
    //  Tcl_Obj pResult;
    //  if( objc!=4 ){
    //    TCL.Tcl_WrongNumArgs(interp, 1, objv, "PARAMETER RESETFLAG");
    //    return TCL.TCL_ERROR;
    //  }
    //  if( getDbPointer(interp, TCL.Tcl_GetString(objv[1]), ref db) ) return TCL.TCL_ERROR;
    //  zOpName = TCL.Tcl_GetString(objv[2]);
    //  for(i=0; i<ArraySize(aOp); i++){
    //    if( strcmp(aOp[i].zName, zOpName)==0 ){
    //      op = aOp[i].op;
    //      break;
    //    }
    //  }
    //  if( i>=ArraySize(aOp) ){
    //    if( TCL.Tcl_GetIntFromObj(interp, objv[2], ref op) ) return TCL.TCL_ERROR;
    //  }
    //  if( TCL.Tcl_GetBooleanFromObj(interp, objv[3], ref resetFlag) ) return TCL.TCL_ERROR;
    //  iValue = 0;
    //  mxValue = 0;
    //  rc = sqlite3_db_status(db, op, ref iValue, ref mxValue, resetFlag);
    //  pResult = TCL.Tcl_NewObj();
    //  TCL.Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(rc));
    //  TCL.Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(iValue));
    //  TCL.Tcl_ListObjAppendElement(0, pResult, TCL.Tcl_NewIntObj(mxValue));
    //  TCL.Tcl_SetObjResult(interp, pResult);
    //  return TCL.TCL_OK;
    //}

    /*
    ** install_malloc_faultsim BOOLEAN
    */
    static int test_install_malloc_faultsim(
    object clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      int rc;
      int isInstall;
      bool bisInstall = false;

      if ( objc != 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "BOOLEAN" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetBooleanFromObj( interp, objv[1], ref  bisInstall ) )
      {
        return TCL.TCL_ERROR;
      }
      isInstall = bisInstall ? 1 : 0;
      rc = faultsimInstall( isInstall );
      TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_VOLATILE );
      return TCL.TCL_OK;
    }

    /*
    ** Register commands with the TCL interpreter.
    */
    static public int Sqlitetest_malloc_Init( Tcl_Interp interp )
    {
      //static struct {
      //   char *zName;
      //   Tcl_ObjCmdProc *xProc;
      //   int clientData;
      //} aObjCmd[] = {
      _aObjCmd[] aObjCmd = new _aObjCmd[] {
//{ "sqlite3_malloc",             test_malloc                   ,0 },
//{ "sqlite3_realloc",            test_realloc                  ,0 },
//{ "sqlite3_free",               test_free                     ,0 },
//{ "memset",                     test_memset                   ,0 },
//{ "memget",                     test_memget                   ,0 },
//{ "sqlite3_memory_used",        test_memory_used              ,0 },
#if FALSE
new _aObjCmd( "sqlite3_memory_used",        test_memory_used      ,0        ),
new _aObjCmd( "sqlite3_memory_highwater",   test_memory_highwater         ,0),
#endif
//{ "sqlite3_memory_highwater",   test_memory_highwater         ,0 },
//{ "sqlite3_memdebug_backtrace", test_memdebug_backtrace       ,0 },
new _aObjCmd(  "sqlite3_memdebug_dump",      test_memdebug_dump            ,0 ),
new _aObjCmd(  "sqlite3_memdebug_fail",      test_memdebug_fail            ,0 ),
//{ "sqlite3_memdebug_pending",   test_memdebug_pending         ,0 },
new _aObjCmd( "sqlite3_memdebug_settitle",  test_memdebug_settitle  ,0      ),
new _aObjCmd( "sqlite3_memdebug_malloc_count", test_memdebug_malloc_count ,0),
//{ "sqlite3_memdebug_log",       test_memdebug_log             ,0 },
new _aObjCmd( "sqlite3_config_scratch",     test_config_scratch           ,0 ),
new _aObjCmd("sqlite3_config_pagecache",   test_config_pagecache         ,0 ),
//{ "sqlite3_config_alt_pcache",  test_alt_pcache               ,0 },
new _aObjCmd( "sqlite3_status", test_status,0 ),
//{ "sqlite3_db_status",          test_db_status                ,0 },
new _aObjCmd( "install_malloc_faultsim",    test_install_malloc_faultsim  ,0),
//{ "sqlite3_config_heap",        test_config_heap              ,0 },
//{ "sqlite3_config_memstatus",   test_config_memstatus         ,0 },
new _aObjCmd(  "sqlite3_config_lookaside",   test_config_lookaside         ,0 ),
//{ "sqlite3_config_error",       test_config_error             ,0 },
new _aObjCmd(  "sqlite3_db_config_lookaside",test_db_config_lookaside      ,0 ),
//{ "sqlite3_dump_memsys3",       test_dump_memsys3             ,3 },
//{ "sqlite3_dump_memsys5",       test_dump_memsys3             ,5 },
};
      int i;
      for ( i = 0 ; i < aObjCmd.Length ; i++ )
      {//<sizeof(aObjCmd)/sizeof(aObjCmd[0]); i++){
        object c = (object)aObjCmd[i].clientData;
        TCL.Tcl_CreateObjCommand( interp, aObjCmd[i].zName, aObjCmd[i].xProc, c, null );
      }
      return TCL.TCL_OK;
    }
#endif
  }
#endif
}
