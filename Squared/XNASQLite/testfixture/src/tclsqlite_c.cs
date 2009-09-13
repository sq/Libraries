using System;
using System.Diagnostics;

using u8 = System.Byte;
using sqlite_u3264 = System.UInt64;
using sqlite_int64 = System.Int64;

namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
#if !SQLITE_OMIT_INCRBLOB
using sqlite3_blob = sqlite.Incrblob;
#endif
  using sqlite3_stmt = csSQLite.Vdbe;
  using Tcl_Channel = tcl.lang.Channel;
  using Tcl_DString = tcl.lang.TclString;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_Obj = tcl.lang.TclObject;
  using Tcl_WideInt = System.Int64;

  using sqlite3_value = csSQLite.MemRef;
  using System.Text;
  using System.IO;

  public partial class csSQLite
  {
    /*
    ** 2001 September 15
    **
    ** The author disclaims copyright to this source code.  In place of
    ** a legal notice, here is a blessing:
    **
    **    May you do good and not evil.
    **    May you find forgiveness for yourself and forgive others.
    **    May you share freely, never taking more than you give.
    **
    *************************************************************************
    ** A TCL Interface to SQLite.  Append this file to sqlite3.c and
    ** compile the whole thing to build a TCL-enabled version of SQLite.
    **
    ** $Id: tclsqlite.c,v 1.242 2009/07/03 22:54:37 drh Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    //#include "tcl.h"
    //#include <errno.h>

    /*
    ** Some additional include files are needed if this file is not
    ** appended to the amalgamation.
    */
#if !SQLITE_AMALGAMATION
    //# include "sqliteInt.h"
    //# include <stdlib.h>
    //# include <string.h>
    //# include <assert.h>
    //# include <ctype.h>
#endif

    /*
* Windows needs to know which symbols to export.  Unix does not.
* BUILD_sqlite should be undefined for Unix.
*/
#if BUILD_sqlite
//#undef TCL.Tcl_STORAGE_CLASS
//#define TCL.Tcl_STORAGE_CLASS DLLEXPORT
#endif // * BUILD_sqlite */

    const int NUM_PREPARED_STMTS = 10;//#define NUM_PREPARED_STMTS 10
    const int MAX_PREPARED_STMTS = 100;//#define MAX_PREPARED_STMTS 100

    /*
    ** If TCL uses UTF-8 and SQLite is configured to use iso8859, then we
    ** have to do a translation when going between the two.  Set the
    ** UTF_TRANSLATION_NEEDED macro to indicate that we need to do
    ** this translation.
    */
#if Tcl_UTF_MAX && !SQLITE_UTF8
//# define UTF_TRANSLATION_NEEDED 1
#endif

    /*
** New SQL functions can be created as TCL scripts.  Each such function
** is described by an instance of the following structure.
*/
    //typedef struct SqlFunc SqlFunc;
    class SqlFunc
    {
      public Tcl_Interp interp;   /* The TCL interpret to execute the function */
      public Tcl_Obj pScript;     /* The Tcl_Obj representation of the script */
      public int useEvalObjv;     /* True if it is safe to use TCL.Tcl_EvalObjv */
      public string zName;        /* Name of this function */
      public SqlFunc pNext;       /* Next function on the list of them all */
    }

    /*
    ** New collation sequences function can be created as TCL scripts.  Each such
    ** function is described by an instance of the following structure.
    */
    //typedef struct SqlCollate SqlCollate;
    class SqlCollate
    {
      public Tcl_Interp interp;   /* The TCL interpret to execute the function */
      public string zScript;      /* The script to be run */
      public SqlCollate pNext;    /* Next function on the list of them all */
    }

    /*
    ** Prepared statements are cached for faster execution.  Each prepared
    ** statement is described by an instance of the following structure.
    */
    //typedef struct SqlPreparedStmt SqlPreparedStmt;
    class SqlPreparedStmt
    {
      public SqlPreparedStmt pNext;  /* Next in linked list */
      public SqlPreparedStmt pPrev;  /* Previous on the list */
      public sqlite3_stmt pStmt;     /* The prepared statement */
      public int nSql;               /* chars in zSql[] */
      public string zSql;            /* Text of the SQL statement */
    }

    //typedef struct IncrblobChannel IncrblobChannel;

    /*
    ** There is one instance of this structure for each SQLite database
    ** that has been opened by the SQLite TCL interface.
    */
    //typedef struct SqliteDb SqliteDb;
    class SqliteDb : object
    {
      public sqlite3 db;                /* The "real" database structure. MUST BE FIRST */
      public Tcl_Interp interp;         /* The interpreter used for this database */
      public string zBusy;              /* The busy callback routine */
      public string zCommit;            /* The commit hook callback routine */
      public string zTrace;             /* The trace callback routine */
      public string zProfile;           /* The profile callback routine */
      public string zProgress;          /* The progress callback routine */
      public string zAuth;              /* The authorization callback routine */
      public int disableAuth;           /* Disable the authorizer if it exists */
      public string zNull = "";         /* Text to substitute for an SQL NULL value */
      public SqlFunc pFunc;             /* List of SQL functions */
      public Tcl_Obj pUpdateHook;       /* Update hook script (if any) */
      public Tcl_Obj pRollbackHook;     /* Rollback hook script (if any) */
      public Tcl_Obj pUnlockNotify;     /* Unlock notify script (if any) */
      public SqlCollate pCollate;       /* List of SQL collation functions */
      public int rc;                    /* Return code of most recent sqlite3_exec() */
      public Tcl_Obj pCollateNeeded;    /* Collation needed script */
      public SqlPreparedStmt stmtList;  /* List of prepared statements*/
      public SqlPreparedStmt stmtLast;  /* Last statement in the list */
      public int maxStmt;               /* The next maximum number of stmtList */
      public int nStmt;                 /* Number of statements in stmtList */
#if !SQLITE_OMIT_INCRBLOB
public IncrblobChannel pIncrblob; /* Linked list of open incrblob channels */
#endif
      public int nStep, nSort;          /* Statistics for most recent operation */
      public int nTransaction;          /* Number of nested [transaction] methods */
    }

#if !SQLITE_OMIT_INCRBLOB
class IncrblobChannel
{
public sqlite3_blob pBlob;      /* sqlite3 blob handle */
public SqliteDb pDb;            /* Associated database connection */
public int iSeek;               /* Current seek offset */
public Tcl_Channel channel;     /* Channel identifier */
public IncrblobChannel pNext;   /* Linked list of all open incrblob channels */
public IncrblobChannel pPrev;   /* Linked list of all open incrblob channels */
}
#endif


    /*
** Compute a string length that is limited to what can be stored in
** lower 30 bits of a 32-bit signed integer.
*/
    static int strlen30( string z )
    {
      //const char *z2 = z;
      //while( *z2 ){ z2++; }
      return 0x3fffffff & z.Length;
    }


#if !SQLITE_OMIT_INCRBLOB
/*
** Close all incrblob channels opened using database connection pDb.
** This is called when shutting down the database connection.
*/
static void closeIncrblobChannels( SqliteDb pDb )
{
IncrblobChannel p;
IncrblobChannel pNext;

for ( p = pDb.pIncrblob ; p != null ; p = pNext )
{
pNext = p.pNext;

/* Note: Calling unregister here call TCL.Tcl_Close on the incrblob channel,
** which deletes the IncrblobChannel structure at p. So do not
** call TCL.Tcl_Free() here.
*/
TCL.Tcl_UnregisterChannel( pDb.interp, p.channel );
}
}

/*
** Close an incremental blob channel.
*/
//static int incrblobClose(object instanceData, Tcl_Interp interp){
//  IncrblobChannel p = (IncrblobChannel *)instanceData;
//  int rc = sqlite3_blob_close(p.pBlob);
//  sqlite3 db = p.pDb.db;

//  /* Remove the channel from the SqliteDb.pIncrblob list. */
//  if( p.pNext ){
//    p.pNext.pPrev = p.pPrev;
//  }
//  if( p.pPrev ){
//    p.pPrev.pNext = p.pNext;
//  }
//  if( p.pDb.pIncrblob==p ){
//    p.pDb.pIncrblob = p.pNext;
//  }

//  /* Free the IncrblobChannel structure */
//  TCL.Tcl_Free((char *)p);

//  if( rc!=SQLITE_OK ){
//    TCL.Tcl_SetResult(interp, (char *)sqlite3_errmsg(db), TCL.Tcl_VOLATILE);
//    return TCL.TCL_ERROR;
//  }
//  return TCL.TCL_OK;
//}

/*
** Read data from an incremental blob channel.
*/
//static int incrblobInput(
//  object instanceData,
//  char *buf,
//  int bufSize,
//  int *errorCodePtr
//){
//  IncrblobChannel p = (IncrblobChannel *)instanceData;
//  int nRead = bufSize;         /* Number of bytes to read */
//  int nBlob;                   /* Total size of the blob */
//  int rc;                      /* sqlite error code */

//  nBlob = sqlite3_blob_bytes(p.pBlob);
//  if( (p.iSeek+nRead)>nBlob ){
//    nRead = nBlob-p.iSeek;
//  }
//  if( nRead<=0 ){
//    return 0;
//  }

//  rc = sqlite3_blob_read(p.pBlob, (void *)buf, nRead, p.iSeek);
//  if( rc!=SQLITE_OK ){
//    *errorCodePtr = rc;
//    return -1;
//  }

//  p.iSeek += nRead;
//  return nRead;
//}

/*
** Write data to an incremental blob channel.
*/
//static int incrblobOutput(
//  object instanceData,
//  CONST char *buf,
//  int toWrite,
//  int *errorCodePtr
//){
//  IncrblobChannel p = (IncrblobChannel *)instanceData;
//  int nWrite = toWrite;        /* Number of bytes to write */
//  int nBlob;                   /* Total size of the blob */
//  int rc;                      /* sqlite error code */

//  nBlob = sqlite3_blob_bytes(p.pBlob);
//  if( (p.iSeek+nWrite)>nBlob ){
//    *errorCodePtr = EINVAL;
//    return -1;
//  }
//  if( nWrite<=0 ){
//    return 0;
//  }

//  rc = sqlite3_blob_write(p.pBlob, (void *)buf, nWrite, p.iSeek);
//  if( rc!=SQLITE_OK ){
//    *errorCodePtr = EIO;
//    return -1;
//  }

//  p.iSeek += nWrite;
//  return nWrite;
//}

/*
** Seek an incremental blob channel.
*/
//static int incrblobSeek(
//  object instanceData,
//  long offset,
//  int seekMode,
//  int *errorCodePtr
//){
//  IncrblobChannel p = (IncrblobChannel *)instanceData;

//  switch( seekMode ){
//    case SEEK_SET:
//      p.iSeek = offset;
//      break;
//    case SEEK_CUR:
//      p.iSeek += offset;
//      break;
//    case SEEK_END:
//      p.iSeek = sqlite3_blob_bytes(p.pBlob) + offset;
//      break;

//    default: Debug.Assert(!"Bad seekMode");
//  }

//  return p.iSeek;
//}


//static void incrblobWatch(object instanceData, int mode){
//  /* NO-OP */
//}
//static int incrblobHandle(object instanceData, int dir, object *hPtr){
//  return TCL.TCL_ERROR;
//}

static TCL.Tcl_ChannelType IncrblobChannelType = {
"incrblob",                        /* typeName                             */
TCL.Tcl_CHANNEL_VERSION_2,             /* version                              */
incrblobClose,                     /* closeProc                            */
incrblobInput,                     /* inputProc                            */
incrblobOutput,                    /* outputProc                           */
incrblobSeek,                      /* seekProc                             */
0,                                 /* setOptionProc                        */
0,                                 /* getOptionProc                        */
incrblobWatch,                     /* watchProc (this is a no-op)          */
incrblobHandle,                    /* getHandleProc (always returns error) */
0,                                 /* close2Proc                           */
0,                                 /* blockModeProc                        */
0,                                 /* flushProc                            */
0,                                 /* handlerProc                          */
0,                                 /* wideSeekProc                         */
};

/*
** Create a new incrblob channel.
*/
static int count = 0;
static int createIncrblobChannel(
Tcl_Interp interp,
SqliteDb pDb,
string zDb,
string zTable,
string zColumn,
sqlite_int64 iRow,
int isReadonly
){
IncrblobChannel p;
sqlite3 db = pDb.db;
sqlite3_blob pBlob;
int rc;
int flags = TCL.Tcl_READABLE|(isReadonly ? 0 : TCL.Tcl_WRITABLE);

/* This variable is used to name the channels: "incrblob_[incr count]" */
//static int count = 0;
string zChannel = "";//string[64];

rc = sqlite3_blob_open(db, zDb, zTable, zColumn, iRow, !isReadonly, pBlob);
if( rc!=SQLITE_OK ){
TCL.Tcl_SetResult(interp, sqlite3_errmsg(pDb.db), TCL.Tcl_VOLATILE);
return TCL.TCL_ERROR;
}

p = new IncrblobChannel();//(IncrblobChannel *)Tcl_Alloc(sizeof(IncrblobChannel));
p.iSeek = 0;
p.pBlob = pBlob;

sqlite3_snprintf(64, zChannel, "incrblob_%d", ++count);
p.channel = TCL.Tcl_CreateChannel(IncrblobChannelType, zChannel, p, flags);
TCL.Tcl_RegisterChannel(interp, p.channel);

/* Link the new channel into the SqliteDb.pIncrblob list. */
p.pNext = pDb.pIncrblob;
p.pPrev = null;
if( p.pNext!=null ){
p.pNext.pPrev = p;
}
pDb.pIncrblob = p;
p.pDb = pDb;

TCL.Tcl_SetResult(interp, Tcl_GetChannelName(p.channel), TCL.Tcl_VOLATILE);
return TCL.TCL_OK;
}
#else  // * else clause for "#if !SQLITE_OMIT_INCRBLOB" */
    //#define closeIncrblobChannels(pDb)
    static void closeIncrblobChannels( SqliteDb pDb ) { }
#endif

    /*
** Look at the script prefix in pCmd.  We will be executing this script
** after first appending one or more arguments.  This routine analyzes
** the script to see if it is safe to use TCL.Tcl_EvalObjv() on the script
** rather than the more general TCL.Tcl_EvalEx().  TCL.Tcl_EvalObjv() is much
** faster.
**
** Scripts that are safe to use with TCL.Tcl_EvalObjv() consists of a
** command name followed by zero or more arguments with no [...] or $
** or {...} or ; to be seen anywhere.  Most callback scripts consist
** of just a single procedure name and they meet this requirement.
*/
    static int safeToUseEvalObjv( Tcl_Interp interp, Tcl_Obj pCmd )
    {
      /* We could try to do something with TCL.Tcl_Parse().  But we will instead
      ** just do a search for forbidden characters.  If any of the forbidden
      ** characters appear in pCmd, we will report the string as unsafe.
      */
      string z;
      int n = 0;
      z = TCL.Tcl_GetStringFromObj( pCmd, ref n );
      while ( n-- > 0 )
      {
        int c = z[n];// *( z++ );
        if ( c == '$' || c == '[' || c == ';' ) return 0;
      }
      return 1;
    }

    /*
    ** Find an SqlFunc structure with the given name.  Or create a new
    ** one if an existing one cannot be found.  Return a pointer to the
    ** structure.
    */
    static SqlFunc findSqlFunc( SqliteDb pDb, string zName )
    {
      SqlFunc p, pNew;
      int i;
      pNew = new SqlFunc();//(SqlFunc)Tcl_Alloc( sizeof(*pNew) + strlen30(zName) + 1 );
      //pNew.zName = (char*)&pNew[1];
      //for(i=0; zName[i]; i++){ pNew.zName[i] = tolower(zName[i]); }
      //pNew.zName[i] = 0;
      pNew.zName = zName.ToLower();
      for ( p = pDb.pFunc ; p != null ; p = p.pNext )
      {
        if ( p.zName == pNew.zName )
        {
          //Tcl_Free((char*)pNew);
          return p;
        }
      }
      pNew.interp = pDb.interp;
      pNew.pScript = null;
      pNew.pNext = pDb.pFunc;
      pDb.pFunc = pNew;
      return pNew;
    }

    /*
    ** Finalize and free a list of prepared statements
    */
    static void flushStmtCache( SqliteDb pDb )
    {
      SqlPreparedStmt pPreStmt;

      while ( pDb.stmtList != null )
      {
        sqlite3_finalize( ref pDb.stmtList.pStmt );
        pPreStmt = pDb.stmtList;
        pDb.stmtList = pDb.stmtList.pNext;
        TCL.Tcl_Free( ref pPreStmt );
      }
      pDb.nStmt = 0;
      pDb.stmtLast = null;
    }

    /*
    ** TCL calls this procedure when an sqlite3 database command is
    ** deleted.
    */
    static void DbDeleteCmd( ref object db )
    {
      SqliteDb pDb = (SqliteDb)db;
      flushStmtCache( pDb );
      closeIncrblobChannels( pDb );
      sqlite3_close( pDb.db );
      while ( pDb.pFunc != null )
      {
        SqlFunc pFunc = pDb.pFunc;
        pDb.pFunc = pFunc.pNext;
        TCL.Tcl_DecrRefCount( ref pFunc.pScript );
        TCL.Tcl_Free( ref pFunc );
      }
      while ( pDb.pCollate != null )
      {
        SqlCollate pCollate = pDb.pCollate;
        pDb.pCollate = pCollate.pNext;
        TCL.Tcl_Free( ref pCollate );
      }
      if ( pDb.zBusy != null )
      {
        TCL.Tcl_Free( ref pDb.zBusy );
      }
      if ( pDb.zTrace != null )
      {
        TCL.Tcl_Free( ref pDb.zTrace );
      }
      if ( pDb.zProfile != null )
      {
        TCL.Tcl_Free( ref pDb.zProfile );
      }
      if ( pDb.zAuth != null )
      {
        TCL.Tcl_Free( ref pDb.zAuth );
      }
      if ( pDb.zNull != null )
      {
        TCL.Tcl_Free( ref pDb.zNull );
      }
      if ( pDb.pUpdateHook != null )
      {
        TCL.Tcl_DecrRefCount( ref pDb.pUpdateHook );
      }
      if ( pDb.pRollbackHook != null )
      {
        TCL.Tcl_DecrRefCount( ref pDb.pRollbackHook );
      }
      if ( pDb.pCollateNeeded != null )
      {
        TCL.Tcl_DecrRefCount( ref pDb.pCollateNeeded );
      }
      TCL.Tcl_Free( ref pDb );
    }

    /*
    ** This routine is called when a database file is locked while trying
    ** to execute SQL.
    */
    static int DbBusyHandler( object cd, int nTries )
    {
      SqliteDb pDb = (SqliteDb)cd;
      int rc;
      string zVal = "";//char zVal[30];

      sqlite3_snprintf( 30, ref zVal, "%d", nTries );
      rc = TCL.Tcl_VarEval( pDb.interp, pDb.zBusy, " ", zVal, null );
      if ( rc != TCL.TCL_OK || atoi( TCL.Tcl_GetStringResult( pDb.interp ) ) != 0 )
      {
        return 0;
      }
      return 1;
    }

#if !SQLITE_OMIT_PROGRESS_CALLBACK
    /*
** This routine is invoked as the 'progress callback' for the database.
*/
    static int DbProgressHandler( object cd )
    {
      SqliteDb pDb = (SqliteDb)cd;
      int rc;

      Debug.Assert( pDb.zProgress != null );
      rc = TCL.Tcl_Eval( pDb.interp, pDb.zProgress );
      if ( rc != TCL.TCL_OK || atoi( TCL.Tcl_GetStringResult( pDb.interp ) ) != 0 )
      {
        return 1;
      }
      return 0;
    }
#endif

#if !SQLITE_OMIT_TRACE
    /*
** This routine is called by the SQLite trace handler whenever a new
** block of SQL is executed.  The TCL script in pDb.zTrace is executed.
*/
    static void DbTraceHandler( object cd, string zSql )
    {
      SqliteDb pDb = (SqliteDb)cd;
      TclObject str = null;

      TCL.Tcl_DStringInit( ref str );
      TCL.Tcl_DStringAppendElement( str, pDb.zTrace );
      TCL.Tcl_DStringAppendElement( str, " {" + zSql + "}" );
      TCL.Tcl_EvalObjEx( pDb.interp, str, 0 );// TCL.Tcl_Eval( pDb.interp, TCL.Tcl_DStringValue( ref str ) );
      TCL.Tcl_DStringFree( ref  str );
      TCL.Tcl_ResetResult( pDb.interp );
    }
#endif

#if !SQLITE_OMIT_TRACE
    /*
** This routine is called by the SQLite profile handler after a statement
** SQL has executed.  The TCL script in pDb.zProfile is evaluated.
*/
    static void DbProfileHandler( object cd, string zSql, sqlite_u3264 tm )
    {
      SqliteDb pDb = (SqliteDb)cd;
      TclObject str = null;
      string zTm = "";//char zTm[100];

      sqlite3_snprintf( 100, ref zTm, "%lld", tm );
      TCL.Tcl_DStringInit( ref str );
      TCL.Tcl_DStringAppendElement( str, pDb.zProfile );
      TCL.Tcl_DStringAppendElement( str, " {" + zSql + "}" );
      TCL.Tcl_DStringAppendElement( str, " {" + zTm + "}" );
      TCL.Tcl_Eval( pDb.interp, str.ToString() );
      TCL.Tcl_DStringFree( ref str );
      TCL.Tcl_ResetResult( pDb.interp );
    }
#endif

    /*
** This routine is called when a transaction is committed.  The
** TCL script in pDb.zCommit is executed.  If it returns non-zero or
** if it throws an exception, the transaction is rolled back instead
** of being committed.
*/
    static int DbCommitHandler( object cd )
    {
      SqliteDb pDb = (SqliteDb)cd;
      int rc;

      rc = TCL.Tcl_Eval( pDb.interp, pDb.zCommit );
      if ( rc != TCL.TCL_OK || atoi( TCL.Tcl_GetStringResult( pDb.interp ) ) != 0 )
      {
        return 1;
      }
      return 0;
    }

    static void DbRollbackHandler( object _object )
    {
      SqliteDb pDb = (SqliteDb)_object;
      Debug.Assert( pDb.pRollbackHook != null );
      if ( TCL.TCL_OK != TCL.Tcl_EvalObjEx( pDb.interp, pDb.pRollbackHook, 0 ) )
      {
        TCL.Tcl_BackgroundError( pDb.interp );
      }
    }

#if (SQLITE_TEST) && (SQLITE_ENABLE_UNLOCK_NOTIFY)
static void setTestUnlockNotifyVars(Tcl_Interp *interp, int iArg, int nArg){
char zBuf[64];
sprintf(zBuf, "%d", iArg);
Tcl_SetVar(interp, "sqlite_unlock_notify_arg", zBuf, TCL_GLOBAL_ONLY);
sprintf(zBuf, "%d", nArg);
Tcl_SetVar(interp, "sqlite_unlock_notify_argcount", zBuf, TCL_GLOBAL_ONLY);
}
#else
    //# define setTestUnlockNotifyVars(x,y,z)
#endif

#if SQLITE_ENABLE_UNLOCK_NOTIFY
static void DbUnlockNotify(void **apArg, int nArg){
int i;
for(i=0; i<nArg; i++){
const int flags = (TCL_EVAL_GLOBAL|TCL_EVAL_DIRECT);
SqliteDb *pDb = (SqliteDb *)apArg[i];
setTestUnlockNotifyVars(pDb->interp, i, nArg);
assert( pDb->pUnlockNotify);
Tcl_EvalObjEx(pDb->interp, pDb->pUnlockNotify, flags);
Tcl_DecrRefCount(pDb->pUnlockNotify);
pDb->pUnlockNotify = 0;
}
}
#endif

    static void DbUpdateHandler(
    object p,
    int op,
    string zDb,
    string zTbl,
    sqlite_int64 rowid
    )
    {
      SqliteDb pDb = (SqliteDb)p;
      Tcl_Obj pCmd;

      Debug.Assert( pDb.pUpdateHook != null );
      Debug.Assert( op == SQLITE_INSERT || op == SQLITE_UPDATE || op == SQLITE_DELETE );

      pCmd = TCL.Tcl_DuplicateObj( pDb.pUpdateHook );
      TCL.Tcl_IncrRefCount( pCmd );
      TCL.Tcl_ListObjAppendElement( null, pCmd, TCL.Tcl_NewStringObj(
        ( ( op == SQLITE_INSERT ) ? "INSERT" : ( op == SQLITE_UPDATE ) ? "UPDATE" : "DELETE" ), -1 ) );
      TCL.Tcl_ListObjAppendElement( null, pCmd, TCL.Tcl_NewStringObj( zDb, -1 ) );
      TCL.Tcl_ListObjAppendElement( null, pCmd, TCL.Tcl_NewStringObj( zTbl, -1 ) );
      TCL.Tcl_ListObjAppendElement( null, pCmd, TCL.Tcl_NewWideIntObj( rowid ) );
      TCL.Tcl_EvalObjEx( pDb.interp, pCmd, TCL.TCL_EVAL_DIRECT );
    }

    static void tclCollateNeeded(
    object pCtx,
    sqlite3 db,
    int enc,
    string zName
    )
    {
      SqliteDb pDb = (SqliteDb)pCtx;
      Tcl_Obj pScript = TCL.Tcl_DuplicateObj( pDb.pCollateNeeded );
      TCL.Tcl_IncrRefCount( pScript );
      TCL.Tcl_ListObjAppendElement( null, pScript, TCL.Tcl_NewStringObj( zName, -1 ) );
      TCL.Tcl_EvalObjEx( pDb.interp, pScript, 0 );
      TCL.Tcl_DecrRefCount( ref pScript );
    }

    /*
    ** This routine is called to evaluate an SQL collation function implemented
    ** using TCL script.
    */
    static int tclSqlCollate(
    object pCtx,
    int nA,
    string zA,
    int nB,
    string zB
    )
    {
      SqlCollate p = (SqlCollate)pCtx;
      Tcl_Obj pCmd;

      pCmd = TCL.Tcl_NewStringObj( p.zScript, -1 );
      TCL.Tcl_IncrRefCount( pCmd );
      TCL.Tcl_ListObjAppendElement( p.interp, pCmd, TCL.Tcl_NewStringObj( zA, nA ) );
      TCL.Tcl_ListObjAppendElement( p.interp, pCmd, TCL.Tcl_NewStringObj( zB, nB ) );
      TCL.Tcl_EvalObjEx( p.interp, pCmd, TCL.TCL_EVAL_DIRECT );
      TCL.Tcl_DecrRefCount( ref pCmd );
      return ( atoi( TCL.Tcl_GetStringResult( p.interp ) ) );
    }

    /*
    ** This routine is called to evaluate an SQL function implemented
    ** using TCL script.
    */
    static void tclSqlFunc( sqlite3_context context, int argc, sqlite3_value[] argv )
    {
      SqlFunc p = (SqlFunc)sqlite3_user_data( context );
      Tcl_Obj pCmd = null;
      int i;
      int rc;

      if ( argc == 0 )
      {
        /* If there are no arguments to the function, call TCL.Tcl_EvalObjEx on the
        ** script object directly.  This allows the TCL compiler to generate
        ** bytecode for the command on the first invocation and thus make
        ** subsequent invocations much faster. */
        pCmd = p.pScript;
        TCL.Tcl_IncrRefCount( pCmd );
        rc = TCL.Tcl_EvalObjEx( p.interp, pCmd, 0 );
        TCL.Tcl_DecrRefCount( ref pCmd );
      }
      else
      {
        /* If there are arguments to the function, make a shallow copy of the
        ** script object, lappend the arguments, then evaluate the copy.
        **
        ** By "shallow" copy, we mean a only the outer list Tcl_Obj is duplicated.
        ** The new Tcl_Obj contains pointers to the original list elements.
        ** That way, when TCL.Tcl_EvalObjv() is run and shimmers the first element
        ** of the list to tclCmdNameType, that alternate representation will
        ** be preserved and reused on the next invocation.
        */
        Tcl_Obj[] aArg = null;
        int nArg = 0;
        if ( TCL.Tcl_ListObjGetElements( p.interp, p.pScript, ref nArg, ref aArg ) )
        {
          sqlite3_result_error( context, TCL.Tcl_GetStringResult( p.interp ), -1 );
          return;
        }
        pCmd = TCL.Tcl_NewListObj( nArg, aArg );
        TCL.Tcl_IncrRefCount( pCmd );
        for ( i = 0 ; i < argc ; i++ )
        {
          sqlite3_value pIn = argv[i];
          Tcl_Obj pVal;

          /* Set pVal to contain the i'th column of this row. */
          switch ( sqlite3_value_type( pIn ) )
          {
            case SQLITE_BLOB:
              {
                int bytes = sqlite3_value_bytes( pIn );
                pVal = TCL.Tcl_NewByteArrayObj( sqlite3_value_blob( pIn ), bytes );
                break;
              }
            case SQLITE_INTEGER:
              {
                sqlite_int64 v = sqlite3_value_int64( pIn );
                if ( v >= -2147483647 && v <= 2147483647 )
                {
                  pVal = TCL.Tcl_NewIntObj( (int)v );
                }
                else
                {
                  pVal = TCL.Tcl_NewWideIntObj( v );
                }
                break;
              }
            case SQLITE_FLOAT:
              {
                double r = sqlite3_value_double( pIn );
                pVal = TCL.Tcl_NewDoubleObj( r );
                break;
              }
            case SQLITE_NULL:
              {
                pVal = TCL.Tcl_NewStringObj( "", 0 );
                break;
              }
            default:
              {
                int bytes = sqlite3_value_bytes( pIn );
                pVal = TCL.Tcl_NewStringObj( sqlite3_value_text( pIn ), bytes );
                break;
              }
          }
          rc = TCL.Tcl_ListObjAppendElement( p.interp, pCmd, pVal ) ? 1 : 0;
          if ( rc != 0 )
          {
            TCL.Tcl_DecrRefCount( ref pCmd );
            sqlite3_result_error( context, TCL.Tcl_GetStringResult( p.interp ), -1 );
            return;
          }
        }
        if ( p.useEvalObjv == 0 )
        {
          /* TCL.Tcl_EvalObjEx() will automatically call TCL.Tcl_EvalObjv() if pCmd
          ** is a list without a string representation.  To prevent this from
          ** happening, make sure pCmd has a valid string representation */
          TCL.Tcl_GetString( pCmd );
        }
        rc = TCL.Tcl_EvalObjEx( p.interp, pCmd, TCL.TCL_EVAL_DIRECT );
        TCL.Tcl_DecrRefCount( ref pCmd );
      }

      if ( rc != 0 && rc != TCL.TCL_RETURN )
      {
        sqlite3_result_error( context, TCL.Tcl_GetStringResult( p.interp ), -1 );
      }
      else
      {
        Tcl_Obj pVar = TCL.Tcl_GetObjResult( p.interp );
        int n = 0;
        string data = "";
        Tcl_WideInt v = 0;
        double r = 0;
        string zType = pVar.GetType().Name;//.typePtr ? pVar.typePtr.name : "";
        char c = zType[0];
        if ( c == 'b' && zType == "bytearray" )
        { //Debugger.Break (); // TODO -- && pVar.bytes==0 ){
          /* Only return a BLOB type if the Tcl variable is a bytearray and
          ** has no string representation. */
          Debugger.Break(); // TODO --data = TCL.Tcl_GetByteArrayFromObj(pVar, ref n);
          sqlite3_result_blob( context, data, n, SQLITE_TRANSIENT );
        }
        else if ( c == 'b' && zType == "boolean" )
        {
          Debugger.Break(); // TODO --          TCL.Tcl_GetIntFromObj(0, pVar, ref n);
          sqlite3_result_int( context, n );
        }
        else if ( ( c == 'w' && zType == "wideInt" ) ||
        ( c == 'i' && zType == "int" ) || Int64.TryParse( pVar.ToString(), out v ) )
        {
          TCL.Tcl_GetWideIntFromObj( null, pVar, ref v );
          sqlite3_result_int64( context, v );
        }
        else if ( ( c == 'd' && zType == "double" ) || Double.TryParse( pVar.ToString(), out r ) )
        {
          TCL.Tcl_GetDoubleFromObj( null, pVar, ref r );
          sqlite3_result_double( context, r );
        }
        else
        {
          data = TCL.Tcl_GetStringFromObj( pVar, n );
          n = data.Length;
          sqlite3_result_text( context, data, n, SQLITE_TRANSIENT );
        }
      }
    }

#if !SQLITE_OMIT_AUTHORIZATION
/*
** This is the authentication function.  It appends the authentication
** type code and the two arguments to zCmd[] then invokes the result
** on the interpreter.  The reply is examined to determine if the
** authentication fails or succeeds.
*/
static int auth_callback(
void pArg,
int code,
const string zArg1,
const string zArg2,
const string zArg3,
const string zArg4
){
string zCode;
TCL.Tcl_DString str;
int rc;
const string zReply;
SqliteDb pDb = (SqliteDb*)pArg;
if( pdb.disableAuth ) return SQLITE_OK;

switch( code ){
case SQLITE_COPY              : zCode="SQLITE_COPY"; break;
case SQLITE_CREATE_INDEX      : zCode="SQLITE_CREATE_INDEX"; break;
case SQLITE_CREATE_TABLE      : zCode="SQLITE_CREATE_TABLE"; break;
case SQLITE_CREATE_TEMP_INDEX : zCode="SQLITE_CREATE_TEMP_INDEX"; break;
case SQLITE_CREATE_TEMP_TABLE : zCode="SQLITE_CREATE_TEMP_TABLE"; break;
case SQLITE_CREATE_TEMP_TRIGGER: zCode="SQLITE_CREATE_TEMP_TRIGGER"; break;
case SQLITE_CREATE_TEMP_VIEW  : zCode="SQLITE_CREATE_TEMP_VIEW"; break;
case SQLITE_CREATE_TRIGGER    : zCode="SQLITE_CREATE_TRIGGER"; break;
case SQLITE_CREATE_VIEW       : zCode="SQLITE_CREATE_VIEW"; break;
case SQLITE_DELETE            : zCode="SQLITE_DELETE"; break;
case SQLITE_DROP_INDEX        : zCode="SQLITE_DROP_INDEX"; break;
case SQLITE_DROP_TABLE        : zCode="SQLITE_DROP_TABLE"; break;
case SQLITE_DROP_TEMP_INDEX   : zCode="SQLITE_DROP_TEMP_INDEX"; break;
case SQLITE_DROP_TEMP_TABLE   : zCode="SQLITE_DROP_TEMP_TABLE"; break;
case SQLITE_DROP_TEMP_TRIGGER : zCode="SQLITE_DROP_TEMP_TRIGGER"; break;
case SQLITE_DROP_TEMP_VIEW    : zCode="SQLITE_DROP_TEMP_VIEW"; break;
case SQLITE_DROP_TRIGGER      : zCode="SQLITE_DROP_TRIGGER"; break;
case SQLITE_DROP_VIEW         : zCode="SQLITE_DROP_VIEW"; break;
case SQLITE_INSERT            : zCode="SQLITE_INSERT"; break;
case SQLITE_PRAGMA            : zCode="SQLITE_PRAGMA"; break;
case SQLITE_READ              : zCode="SQLITE_READ"; break;
case SQLITE_SELECT            : zCode="SQLITE_SELECT"; break;
case SQLITE_TRANSACTION       : zCode="SQLITE_TRANSACTION"; break;
case SQLITE_UPDATE            : zCode="SQLITE_UPDATE"; break;
case SQLITE_ATTACH            : zCode="SQLITE_ATTACH"; break;
case SQLITE_DETACH            : zCode="SQLITE_DETACH"; break;
case SQLITE_ALTER_TABLE       : zCode="SQLITE_ALTER_TABLE"; break;
case SQLITE_REINDEX           : zCode="SQLITE_REINDEX"; break;
case SQLITE_ANALYZE           : zCode="SQLITE_ANALYZE"; break;
case SQLITE_CREATE_VTABLE     : zCode="SQLITE_CREATE_VTABLE"; break;
case SQLITE_DROP_VTABLE       : zCode="SQLITE_DROP_VTABLE"; break;
case SQLITE_FUNCTION          : zCode="SQLITE_FUNCTION"; break;
case SQLITE_SAVEPOINT         : zCode="SQLITE_SAVEPOINT"; break;
default                       : zCode="????"; break;
}
TCL.Tcl_DStringInit(&str);
TCL.Tcl_DStringAppend(&str, pDb.zAuth, -1);
TCL.Tcl_DStringAppendElement(&str, zCode);
TCL.Tcl_DStringAppendElement(&str, zArg1 ? zArg1 : "");
TCL.Tcl_DStringAppendElement(&str, zArg2 ? zArg2 : "");
TCL.Tcl_DStringAppendElement(&str, zArg3 ? zArg3 : "");
TCL.Tcl_DStringAppendElement(&str, zArg4 ? zArg4 : "");
rc = TCL.Tcl_GlobalEval(pDb.interp, TCL.Tcl_DStringValue(&str));
TCL.Tcl_DStringFree(&str);
zReply = TCL.Tcl_GetStringResult(pDb.interp);
if( strcmp(zReply,"SQLITE_OK")==0 ){
rc = SQLITE_OK;
}else if( strcmp(zReply,"SQLITE_DENY")==0 ){
rc = SQLITE_DENY;
}else if( strcmp(zReply,"SQLITE_IGNORE")==0 ){
rc = SQLITE_IGNORE;
}else{
rc = 999;
}
return rc;
}
#endif // * SQLITE_OMIT_AUTHORIZATION */

    /*
** zText is a pointer to text obtained via an sqlite3_result_text()
** or similar interface. This routine returns a Tcl string object,
** reference count set to 0, containing the text. If a translation
** between iso8859 and UTF-8 is required, it is preformed.
*/
    static Tcl_Obj dbTextToObj( string zText )
    {
      Tcl_Obj pVal;
#if UTF_TRANSLATION_NEEDED
//TCL.Tcl_DString dCol;
//TCL.Tcl_DStringInit(&dCol);
//TCL.Tcl_ExternalToUtfDString(NULL, zText, -1, dCol);
//pVal = TCL.Tcl_NewStringObj(Tcl_DStringValue(&dCol), -1);
//TCL.Tcl_DStringFree(ref dCol);
if (zText.Length == Encoding.UTF8.GetByteCount(zText)) pVal = TCL.Tcl_NewStringObj( zText, -1 );
else pVal = TCL.Tcl_NewStringObj( zText, -1 );
#else
      pVal = TCL.Tcl_NewStringObj( zText, -1 );
#endif
      return pVal;
    }

    /*
    ** This routine reads a line of text from FILE in, stores
    ** the text in memory obtained from malloc() and returns a pointer
    ** to the text.  NULL is returned at end of file, or if malloc()
    ** fails.
    **
    ** The interface is like "readline" but no command-line editing
    ** is done.
    **
    ** copied from shell.c from '.import' command
    */
    //static char *local_getline(string zPrompt, FILE *in){
    //  string zLine;
    //  int nLine;
    //  int n;
    //  int eol;

    //  nLine = 100;
    //  zLine = malloc( nLine );
    //  if( zLine==0 ) return 0;
    //  n = 0;
    //  eol = 0;
    //  while( !eol ){
    //    if( n+100>nLine ){
    //      nLine = nLine*2 + 100;
    //      zLine = realloc(zLine, nLine);
    //      if( zLine==0 ) return 0;
    //    }
    //    if( fgets(&zLine[n], nLine - n, in)==0 ){
    //      if( n==0 ){
    //        free(zLine);
    //        return 0;
    //      }
    //      zLine[n] = 0;
    //      eol = 1;
    //      break;
    //    }
    //    while( zLine[n] ){ n++; }
    //    if( n>0 && zLine[n-1]=='\n' ){
    //      n--;
    //      zLine[n] = 0;
    //      eol = 1;
    //    }
    //  }
    //  zLine = realloc( zLine, n+1 );
    //  return zLine;
    //}


    /*
    ** Figure out the column names for the data returned by the statement
    ** passed as the second argument.
    **
    ** If parameter papColName is not NULL, then papColName is set to point
    ** at an array allocated using Tcl_Alloc(). It is the callers responsibility
    ** to free this array using Tcl_Free(), and to decrement the reference
    ** count of each Tcl_Obj* member of the array.
    **
    ** The return value of this function is the number of columns of data
    ** returned by pStmt (and hence the size of the papColName array).
    **
    ** If pArray is not NULL, then it contains the name of a Tcl array
    ** variable. The "*" member of this array is set to a list containing
    ** the names of the columns returned by the statement, in order from
    ** left to right. e.g. if the names of the returned columns are a, b and
    ** c, it does the equivalent of the tcl command:
    **
    **     set ${pArray}(*) {a b c}
    */
    static int
    computeColumnNames(
    Tcl_Interp interp,
    sqlite3_stmt pStmt,              /* SQL statement */
    ref Tcl_Obj[] papColName,            /* OUT: Array of column names */
    Tcl_Obj pArray                   /* Name of array variable (may be null) */
    )
    {
      int nCol;

      /* Compute column names */
      nCol = sqlite3_column_count( pStmt );
      if ( papColName != null )
      {
        int i;
        Tcl_Obj[] apColName = new Tcl_Obj[nCol];// (Tcl_Obj**)Tcl_Alloc( sizeof( Tcl_Obj* ) * nCol );
        for ( i = 0 ; i < nCol ; i++ )
        {
          apColName[i] = dbTextToObj( sqlite3_column_name( pStmt, i ) );
          TCL.Tcl_IncrRefCount( apColName[i] );
        }

        /* If results are being stored in an array variable, then create
        ** the array(*) entry for that array
        */
        if ( pArray != null )
        {
          Tcl_Obj pColList = TCL.Tcl_NewObj();
          Tcl_Obj pStar = TCL.Tcl_NewStringObj( "*", -1 );
          TCL.Tcl_IncrRefCount( pColList );
          for ( i = 0 ; i < nCol ; i++ )
          {
            TCL.Tcl_ListObjAppendElement( interp, pColList, apColName[i] );
          }
          TCL.Tcl_IncrRefCount( pStar );
          TCL.Tcl_ObjSetVar2( interp, pArray, pStar, pColList, 0 );
          TCL.Tcl_DecrRefCount( ref pColList );
          TCL.Tcl_DecrRefCount( ref pStar );
        }
        papColName = apColName;
      }

      return nCol;
    }

    /*
    ** The "sqlite" command below creates a new Tcl command for each
    ** connection it opens to an SQLite database.  This routine is invoked
    ** whenever one of those connection-specific commands is executed
    ** in Tcl.  For example, if you run Tcl code like this:
    **
    **       sqlite3 db1  "my_database"
    **       db1 close
    **
    ** The first command opens a connection to the "my_database" database
    ** and calls that connection "db1".  The second command causes this
    ** subroutine to be invoked.
    */
    enum DB_enum
    {
      DB_AUTHORIZER, DB_BACKUP, DB_BUSY,
      DB_CACHE, DB_CHANGES, DB_CLOSE,
      DB_COLLATE, DB_COLLATION_NEEDED, DB_COMMIT_HOOK,
      DB_COMPLETE, DB_COPY, DB_ENABLE_LOAD_EXTENSION,
      DB_ERRORCODE, DB_EVAL, DB_EXISTS,
      DB_FUNCTION, DB_INCRBLOB, DB_INTERRUPT,
      DB_LAST_INSERT_ROWID, DB_NULLVALUE, DB_ONECOLUMN,
      DB_PROFILE, DB_PROGRESS, DB_REKEY,
      DB_RESTORE, DB_ROLLBACK_HOOK, DB_STATUS,
      DB_TIMEOUT, DB_TOTAL_CHANGES, DB_TRACE,
      DB_TRANSACTION, DB_UNLOCK_NOTIFY, DB_UPDATE_HOOK,
      DB_VERSION,
    };

    enum TTYPE_enum
    {
      TTYPE_DEFERRED, TTYPE_EXCLUSIVE, TTYPE_IMMEDIATE
    };

    static int DbObjCmd( object cd, Tcl_Interp interp, int objc, Tcl_Obj[] objv )
    {
      SqliteDb pDb = (SqliteDb)cd;
      int choice = 0;
      int rc = TCL.TCL_OK;
      string[] DB_strs = {
"authorizer",         "backup",            "busy",
"cache",              "changes",           "close",
"collate",            "collation_needed",  "commit_hook",
"complete",           "copy",              "enable_load_extension",
"errorcode",          "eval",              "exists",
"function",           "incrblob",          "interrupt",
"last_insert_rowid",  "nullvalue",         "onecolumn",
"profile",            "progress",          "rekey",
"restore",            "rollback_hook",     "status",
"timeout",            "total_changes",     "trace",
"transaction",        "unlock_notify",     "update_hook",
"version"
};

      /* don't leave trailing commas on DB_enum, it confuses the AIX xlc compiler */
      if ( objc < 2 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv, "SUBCOMMAND ..." );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetIndexFromObj( interp, objv[1], DB_strs, "option", 0, ref choice ) )
      {
        return TCL.TCL_ERROR;
      }

      switch ( choice )
      {

        /*    $db authorizer ?CALLBACK?
        **
        ** Invoke the given callback to authorize each SQL operation as it is
        ** compiled.  5 arguments are appended to the callback before it is
        ** invoked:
        **
        **   (1) The authorization type (ex: SQLITE_CREATE_TABLE, SQLITE_INSERT, ...)
        **   (2) First descriptive name (depends on authorization type)
        **   (3) Second descriptive name
        **   (4) Name of the database (ex: "main", "temp")
        **   (5) Name of trigger that is doing the access
        **
        ** The callback should return on of the following strings: SQLITE_OK,
        ** SQLITE_IGNORE, or SQLITE_DENY.  Any other return value is an error.
        **
        ** If this method is invoked with no arguments, the current authorization
        ** callback string is returned.
        */
        case (int)DB_enum.DB_AUTHORIZER:
          {
#if SQLITE_OMIT_AUTHORIZATION
            TCL.Tcl_AppendResult( interp, "authorization not available in this build" );
            return TCL.TCL_ERROR;
#else
if( objc>3 ){
TCL.Tcl_WrongNumArgs(interp, 2, objv, "?CALLBACK?");
return TCL.TCL_ERROR;
}else if( objc==2 ){
if( pDb.zAuth ){
TCL.Tcl_AppendResult(interp, pDb.zAuth);
}
}else{
string zAuth;
int len;
if( pDb.zAuth ){
TCL.Tcl_Free(pDb.zAuth);
}
zAuth = TCL.Tcl_GetStringFromObj(objv[2], len);
if( zAuth && len>0 ){
pDb.zAuth = TCL.Tcl_Alloc( len + 1 );
memcpy(pDb.zAuth, zAuth, len+1);
}else{
pDb.zAuth = 0;
}
if( pDb.zAuth ){
pDb.interp = interp;
sqlite3_set_authorizer(pDb.db, auth_callback, pDb);
}else{
sqlite3_set_authorizer(pDb.db, 0, 0);
}
}
#endif
            break;
          }

        /*    $db backup ?DATABASE? FILENAME
        **
        ** Open or create a database file named FILENAME.  Transfer the
        ** content of local database DATABASE (default: "main") into the
        ** FILENAME database.
        */
        case (int)DB_enum.DB_BACKUP:
          {
            string zDestFile;
            string zSrcDb;
            sqlite3 pDest = null;
            sqlite3_backup pBackup;

            if ( objc == 3 )
            {
              zSrcDb = "main";
              zDestFile = TCL.Tcl_GetString( objv[2] );
            }
            else if ( objc == 4 )
            {
              zSrcDb = TCL.Tcl_GetString( objv[2] );
              zDestFile = TCL.Tcl_GetString( objv[3] );
            }
            else
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?DATABASE? FILENAME" );
              return TCL.TCL_ERROR;
            }
            rc = sqlite3_open( zDestFile, ref pDest );
            if ( rc != SQLITE_OK )
            {
              TCL.Tcl_AppendResult( interp, "cannot open target database: ",
              sqlite3_errmsg( pDest ) );
              sqlite3_close( pDest );
              return TCL.TCL_ERROR;
            }
            pBackup = sqlite3_backup_init( pDest, "main", pDb.db, zSrcDb );
            if ( pBackup == null )
            {
              TCL.Tcl_AppendResult( interp, "backup failed: ",
              sqlite3_errmsg( pDest ) );
              sqlite3_close( pDest );
              return TCL.TCL_ERROR;
            }
            while ( ( rc = sqlite3_backup_step( pBackup, 100 ) ) == SQLITE_OK ) { }
            sqlite3_backup_finish( pBackup );
            if ( rc == SQLITE_DONE )
            {
              rc = TCL.TCL_OK;
            }
            else
            {
              TCL.Tcl_AppendResult( interp, "backup failed: ",
              sqlite3_errmsg( pDest ) );
              rc = TCL.TCL_ERROR;
            }
            sqlite3_close( pDest );
            break;
          }

        //  /*    $db busy ?CALLBACK?
        //  **
        //  ** Invoke the given callback if an SQL statement attempts to open
        //  ** a locked database file.
        //  */
        case (int)DB_enum.DB_BUSY:
          {
            if ( objc > 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "CALLBACK" );
              return TCL.TCL_ERROR;
            }
            else if ( objc == 2 )
            {
              if ( pDb.zBusy != null )
              {
                TCL.Tcl_AppendResult( interp, pDb.zBusy );
              }
            }
            else
            {
              string zBusy;
              int len = 0;
              if ( pDb.zBusy != null )
              {
                TCL.Tcl_Free( ref pDb.zBusy );
              }
              zBusy = TCL.Tcl_GetStringFromObj( objv[2], ref len );
              if ( zBusy != null && len > 0 )
              {
                //pDb.zBusy = TCL.Tcl_Alloc( len + 1 );
                pDb.zBusy = zBusy;// memcpy( pDb.zBusy, zBusy, len + 1 );
              }
              else
              {
                pDb.zBusy = null;
              }
              if ( pDb.zBusy != null )
              {
                pDb.interp = interp;
                sqlite3_busy_handler( pDb.db, (dxBusy)DbBusyHandler, pDb );
              }
              else
              {
                sqlite3_busy_handler( pDb.db, null, null );
              }
            }
            break;
          }

        //  /*     $db cache flush
        //  **     $db cache size n
        //  **
        //  ** Flush the prepared statement cache, or set the maximum number of
        //  ** cached statements.
        //  */
        case (int)DB_enum.DB_CACHE:
          {
            string subCmd;
            int n = 0;

            if ( objc <= 2 )
            {
              TCL.Tcl_WrongNumArgs( interp, 1, objv, "cache option ?arg?" );
              return TCL.TCL_ERROR;
            }
            subCmd = TCL.Tcl_GetStringFromObj( objv[2], 0 );
            if ( subCmd == "flush" )
            {
              if ( objc != 3 )
              {
                TCL.Tcl_WrongNumArgs( interp, 2, objv, "flush" );
                return TCL.TCL_ERROR;
              }
              else
              {
                flushStmtCache( pDb );
              }
            }
            else if ( subCmd == "size" )
            {
              if ( objc != 4 )
              {
                TCL.Tcl_WrongNumArgs( interp, 2, objv, "size n" );
                return TCL.TCL_ERROR;
              }
              else
              {
                if ( TCL.TCL_ERROR == ( TCL.Tcl_GetIntFromObj( interp, objv[3], ref n ) ? TCL.TCL_ERROR : TCL.TCL_OK ) )
                {
                  TCL.Tcl_AppendResult( interp, "cannot convert \"",
                     TCL.Tcl_GetStringFromObj( objv[3], 0 ), "\" to integer", 0 );
                  return TCL.TCL_ERROR;
                }
                else
                {
                  if ( n < 0 )
                  {
                    flushStmtCache( pDb );
                    n = 0;
                  }
                  else if ( n > MAX_PREPARED_STMTS )
                  {
                    n = MAX_PREPARED_STMTS;
                  }
                  pDb.maxStmt = n;
                }
              }
            }
            else
            {
              TCL.Tcl_AppendResult( interp, "bad option \"",
              TCL.Tcl_GetStringFromObj( objv[2], 0 ), "\": must be flush or size", null );
              return TCL.TCL_ERROR;
            }
            break;
          }

        /*     $db changes
        **
        ** Return the number of rows that were modified, inserted, or deleted by
        ** the most recent INSERT, UPDATE or DELETE statement, not including
        ** any changes made by trigger programs.
        */
        case (int)DB_enum.DB_CHANGES:
          {
            Tcl_Obj pResult;
            if ( objc != 2 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "" );
              return TCL.TCL_ERROR;
            }
            pResult = TCL.Tcl_GetObjResult( interp );
            TCL.Tcl_SetResult( interp, sqlite3_changes( pDb.db ).ToString(), 0 );
            break;
          }

        /*    $db close
        **
        ** Shutdown the database
        */
        case (int)DB_enum.DB_CLOSE:
          {
            TCL.Tcl_DeleteCommand( interp, TCL.Tcl_GetStringFromObj( objv[0], 0 ) );
            break;
          }

        /*
        **     $db collate NAME SCRIPT
        **
        ** Create a new SQL collation function called NAME.  Whenever
        ** that function is called, invoke SCRIPT to evaluate the function.
        */
        case (int)DB_enum.DB_COLLATE:
          {
            SqlCollate pCollate;
            string zName;
            string zScript;
            int nScript = 0;
            if ( objc != 4 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "NAME SCRIPT" );
              return TCL.TCL_ERROR;
            }
            zName = TCL.Tcl_GetStringFromObj( objv[2], 0 );
            zScript = TCL.Tcl_GetStringFromObj( objv[3], nScript );
            pCollate = new SqlCollate();//(SqlCollate*)Tcl_Alloc( sizeof(*pCollate) + nScript + 1 );
            //if ( pCollate == null ) return TCL.TCL_ERROR;
            pCollate.interp = interp;
            pCollate.pNext = pDb.pCollate;
            pCollate.zScript = zScript; // pCollate[1];
            pDb.pCollate = pCollate;
            //memcpy( pCollate.zScript, zScript, nScript + 1 );
            if ( sqlite3_create_collation( pDb.db, zName, SQLITE_UTF8,
            pCollate, (dxCompare)tclSqlCollate ) != 0 )
            {
              TCL.Tcl_SetResult( interp, sqlite3_errmsg( pDb.db ), TCL.TCL_VOLATILE );
              return TCL.TCL_ERROR;
            }
            break;
          }

        /*
        **     $db collation_needed SCRIPT
        **
        ** Create a new SQL collation function called NAME.  Whenever
        ** that function is called, invoke SCRIPT to evaluate the function.
        */
        case (int)DB_enum.DB_COLLATION_NEEDED:
          {
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "SCRIPT" );
              return TCL.TCL_ERROR;
            }
            if ( pDb.pCollateNeeded != null )
            {
              TCL.Tcl_DecrRefCount( ref pDb.pCollateNeeded );
            }
            pDb.pCollateNeeded = TCL.Tcl_DuplicateObj( objv[2] );
            TCL.Tcl_IncrRefCount( pDb.pCollateNeeded );
            sqlite3_collation_needed( pDb.db, (object)pDb, (dxCollNeeded)tclCollateNeeded );
            break;
          }

        /*
        **    $db unlock_notify ?script?
        */
        case (int)DB_enum.DB_UNLOCK_NOTIFY:
          {
#if SQLITE_ENABLE_UNLOCK_NOTIFY
Tcl_AppendResult(interp, "unlock_notify not available in this build");
rc = TCL_ERROR;
#else
            //if( objc!=2 && objc!=3 ){
            //  Tcl_WrongNumArgs(interp, 2, objv, "?SCRIPT?");
            //  rc = TCL_ERROR;
            //}else{
            //  void (*xNotify)(void **, int) = 0;
            //  void *pNotifyArg = 0;

            //  if( pDb->pUnlockNotify ){
            //    Tcl_DecrRefCount(pDb->pUnlockNotify);
            //    pDb->pUnlockNotify = 0;
            //  }

            //  if( objc==3 ){
            //    xNotify = DbUnlockNotify;
            //    pNotifyArg = (void *)pDb;
            //    pDb->pUnlockNotify = objv[2];
            //    Tcl_IncrRefCount(pDb->pUnlockNotify);
            //  }

            //  if( sqlite3_unlock_notify(pDb->db, xNotify, pNotifyArg) ){
            //    Tcl_AppendResult(interp, sqlite3_errmsg(pDb->db));
            //    rc = TCL_ERROR;
            //  }
            //}
#endif
            break;
          }
        /*    $db commit_hook ?CALLBACK?
        **
        ** Invoke the given callback just before committing every SQL transaction.
        ** If the callback throws an exception or returns non-zero, then the
        ** transaction is aborted.  If CALLBACK is an empty string, the callback
        ** is disabled.
        */
        case (int)DB_enum.DB_COMMIT_HOOK:
          {
            if ( objc > 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?CALLBACK?" );
              return TCL.TCL_ERROR;
            }
            else if ( objc == 2 )
            {
              if ( pDb.zCommit != null )
              {
                TCL.Tcl_AppendResult( interp, pDb.zCommit );
              }
            }
            else
            {
              string zCommit;
              int len = 0;
              if ( pDb.zCommit != null )
              {
                TCL.Tcl_Free( ref pDb.zCommit );
              }
              zCommit = TCL.Tcl_GetStringFromObj( objv[2], ref  len );
              if ( zCommit != null && len > 0 )
              {
                pDb.zCommit = zCommit;// TCL.Tcl_Alloc( len + 1 );
                //memcpy( pDb.zCommit, zCommit, len + 1 );
              }
              else
              {
                pDb.zCommit = null;
              }
              if ( pDb.zCommit != null )
              {
                pDb.interp = interp;
                sqlite3_commit_hook( pDb.db, DbCommitHandler, pDb );
              }
              else
              {
                sqlite3_commit_hook( pDb.db, null, null );
              }
            }
            break;
          }

        /*    $db complete SQL
        **
        ** Return TRUE if SQL is a complete SQL statement.  Return FALSE if
        ** additional lines of input are needed.  This is similar to the
        ** built-in "info complete" command of Tcl.
        */
        case (int)DB_enum.DB_COMPLETE:
          {
#if !SQLITE_OMIT_COMPLETE
            Tcl_Obj pResult;
            int isComplete;
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "SQL" );
              return TCL.TCL_ERROR;
            }
            isComplete = sqlite3_complete( TCL.Tcl_GetStringFromObj( objv[2], 0 ) );
            pResult = TCL.Tcl_GetObjResult( interp );
            TCL.Tcl_SetBooleanObj( pResult, isComplete );
#endif
            break;
          }

        /*    $db copy conflict-algorithm table filename ?SEPARATOR? ?NULLINDICATOR?
        **
        ** Copy data into table from filename, optionally using SEPARATOR
        ** as column separators.  If a column contains a null string, or the
        ** value of NULLINDICATOR, a NULL is inserted for the column.
        ** conflict-algorithm is one of the sqlite conflict algorithms:
        **    rollback, abort, fail, ignore, replace
        ** On success, return the number of lines processed, not necessarily same
        ** as 'db changes' due to conflict-algorithm selected.
        **
        ** This code is basically an implementation/enhancement of
        ** the sqlite3 shell.c ".import" command.
        **
        ** This command usage is equivalent to the sqlite2.x COPY statement,
        ** which imports file data into a table using the PostgreSQL COPY file format:
        **   $db copy $conflit_algo $table_name $filename \t \\N
        */
        case (int)DB_enum.DB_COPY:
          {
            string zTable;              /* Insert data into this table */
            string zFile;               /* The file from which to extract data */
            string zConflict;           /* The conflict algorithm to use */
            sqlite3_stmt pStmt = null;  /* A statement */
            int nCol;                   /* Number of columns in the table */
            int nByte;                  /* Number of bytes in an SQL string */
            int i, j;                   /* Loop counters */
            int nSep;                   /* Number of bytes in zSep[] */
            int nNull;                  /* Number of bytes in zNull[] */
            string zSql;                /* An SQL statement */
            string zLine;               /* A single line of input from the file */
            string[] azCol;             /* zLine[] broken up into columns */
            string zCommit;             /* How to commit changes */
            TextReader _in;             /* The input file */
            int lineno = 0;             /* Line number of input file */
            string zLineNum = "";//[80] /* Line number print buffer */
            Tcl_Obj pResult;            /* interp result */

            string zSep;
            string zNull;
            if ( objc < 5 || objc > 7 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv,
                 "CONFLICT-ALGORITHM TABLE FILENAME ?SEPARATOR? ?NULLINDICATOR?" );
              return TCL.TCL_ERROR;
            }
            if ( objc >= 6 )
            {
              zSep = TCL.Tcl_GetStringFromObj( objv[5], 0 );
            }
            else
            {
              zSep = "\t";
            }
            if ( objc >= 7 )
            {
              zNull = TCL.Tcl_GetStringFromObj( objv[6], 0 );
            }
            else
            {
              zNull = "";
            }
            zConflict = TCL.Tcl_GetStringFromObj( objv[2], 0 );
            zTable = TCL.Tcl_GetStringFromObj( objv[3], 0 );
            zFile = TCL.Tcl_GetStringFromObj( objv[4], 0 );
            nSep = strlen30( zSep );
            nNull = strlen30( zNull );
            if ( nSep == 0 )
            {
              TCL.Tcl_AppendResult( interp, "Error: non-null separator required for copy" );
              return TCL.TCL_ERROR;
            }
            if ( zConflict != "rollback" &&
               zConflict != "abort" &&
               zConflict != "fail" &&
               zConflict != "ignore" &&
               zConflict != "replace" )
            {
              TCL.Tcl_AppendResult( interp, "Error: \"", zConflict,
                    "\", conflict-algorithm must be one of: rollback, " +
                    "abort, fail, ignore, or replace", 0 );
              return TCL.TCL_ERROR;
            }
            zSql = sqlite3_mprintf( "SELECT * FROM '%q'", zTable );
            if ( zSql == null )
            {
              TCL.Tcl_AppendResult( interp, "Error: no such table: ", zTable );
              return TCL.TCL_ERROR;
            }
            nByte = strlen30( zSql );
            string Dummy = null; rc = sqlite3_prepare( pDb.db, zSql, -1, ref pStmt, ref Dummy );
            //sqlite3DbFree( null, ref zSql );
            if ( rc != 0 )
            {
              TCL.Tcl_AppendResult( interp, "Error: ", sqlite3_errmsg( pDb.db ) );
              nCol = 0;
            }
            else
            {
              nCol = sqlite3_column_count( pStmt );
            }
            sqlite3_finalize( ref pStmt );
            if ( nCol == 0 )
            {
              return TCL.TCL_ERROR;
            }
            //zSql = malloc( nByte + 50 + nCol*2 );
            //if( zSql==0 ) {
            //  TCL.Tcl_AppendResult(interp, "Error: can't malloc()");
            //  return TCL.TCL_ERROR;
            //}
            sqlite3_snprintf( nByte + 50, ref zSql, "INSERT OR %q INTO '%q' VALUES(?",
                 zConflict, zTable );
            j = strlen30( zSql );
            for ( i = 1 ; i < nCol ; i++ )
            {
              //zSql+=[j++] = ',';
              //zSql[j++] = '?';
              zSql += ",?";
            }
            //zSql[j++] = ')';
            //zSql[j] = "";
            zSql += ")";
            rc = sqlite3_prepare( pDb.db, zSql, -1, ref pStmt, ref Dummy );
            //free(zSql);
            if ( rc != 0 )
            {
              TCL.Tcl_AppendResult( interp, "Error: ", sqlite3_errmsg( pDb.db ) );
              sqlite3_finalize( ref pStmt );
              return TCL.TCL_ERROR;
            }
            _in = new StreamReader( zFile );//fopen(zFile, "rb");
            if ( _in == null )
            {
              TCL.Tcl_AppendResult( interp, "Error: cannot open file: ", zFile );
              sqlite3_finalize( ref pStmt );
              return TCL.TCL_ERROR;
            }
            azCol = new string[nCol + 1];//malloc( sizeof(azCol[0])*(nCol+1) );
            if ( azCol == null )
            {
              TCL.Tcl_AppendResult( interp, "Error: can't malloc()" );
              _in.Close();//fclose(_in);
              return TCL.TCL_ERROR;
            }
            sqlite3_exec( pDb.db, "BEGIN", 0, 0, 0 );
            zCommit = "COMMIT";
            while ( ( zLine = _in.ReadLine() ) != null )//local_getline(0, _in))!=0 )
            {
              string z;
              i = 0;
              lineno++;
              azCol = zLine.Split( zSep[0] );
              //for(i=0, z=zLine; *z; z++){
              //  if( *z==zSep[0] && strncmp(z, zSep, nSep)==0 ){
              //    *z = 0;
              //    i++;
              //    if( i<nCol ){
              //      azCol[i] = z[nSep];
              //      z += nSep-1;
              //    }
              //  }
              //}
              if ( azCol.Length != nCol )
              {
                string zErr = "";
                int nErr = strlen30( zFile ) + 200;
                //zErr = malloc(nErr);
                //if( zErr ){
                sqlite3_snprintf( nErr, ref zErr,
                   "Error: %s line %d: expected %d columns of data but found %d",
                   zFile, lineno, nCol, i + 1 );
                TCL.Tcl_AppendResult( interp, zErr );
                //  free(zErr);
                //}
                zCommit = "ROLLBACK";
                break;
              }
              for ( i = 0 ; i < nCol ; i++ )
              {
                /* check for null data, if so, bind as null */
                if ( ( nNull > 0 && azCol[i] == zNull )
                  || strlen30( azCol[i] ) == 0
                )
                {
                  sqlite3_bind_null( pStmt, i + 1 );
                }
                else
                {
                  sqlite3_bind_text( pStmt, i + 1, azCol[i], -1, SQLITE_STATIC );
                }
              }
              sqlite3_step( pStmt );
              rc = sqlite3_reset( pStmt );
              //free(zLine);
              if ( rc != SQLITE_OK )
              {
                TCL.Tcl_AppendResult( interp, "Error: ", sqlite3_errmsg( pDb.db ) );
                zCommit = "ROLLBACK";
                break;
              }
            }
            //free(azCol);
            _in.Close();// fclose( _in );
            sqlite3_finalize( ref pStmt );
            sqlite3_exec( pDb.db, zCommit, 0, 0, 0 );

            if ( zCommit[0] == 'C' )
            {
              /* success, set result as number of lines processed */
              pResult = TCL.Tcl_GetObjResult( interp );
              TCL.Tcl_SetIntObj( pResult, lineno );
              rc = TCL.TCL_OK;
            }
            else
            {
              /* failure, append lineno where failed */
              sqlite3_snprintf( 80, ref zLineNum, "%d", lineno );
              TCL.Tcl_AppendResult( interp, ", failed while processing line: ", zLineNum );
              rc = TCL.TCL_ERROR;
            }
            break;
          }

        /*
        **    $db enable_load_extension BOOLEAN
        **
        ** Turn the extension loading feature on or off.  It if off by
        ** default.
        */
        case (int)DB_enum.DB_ENABLE_LOAD_EXTENSION:
          {
#if !SQLITE_OMIT_LOAD_EXTENSION
            bool onoff = false;
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "BOOLEAN" );
              return TCL.TCL_ERROR;
            }
            if ( TCL.Tcl_GetBooleanFromObj( interp, objv[2], ref onoff ) )
            {
              return TCL.TCL_ERROR;
            }
            sqlite3_enable_load_extension( pDb.db, onoff ? 1 : 0 );
            break;
#else
      TCL.Tcl_AppendResult(interp, "extension loading is turned off at compile-time",
                       0);
      return TCL.TCL_ERROR;
#endif
          }

        /*
        **    $db errorcode
        **
        ** Return the numeric error code that was returned by the most recent
        ** call to sqlite3_exec().
        */
        case (int)DB_enum.DB_ERRORCODE:
          {
            TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( sqlite3_errcode( pDb.db ) ) );
            break;
          }

        /*
        **    $db eval $sql ?array? ?{  ...code... }?
        **    $db onecolumn $sql
        **
        ** The SQL statement in $sql is evaluated.  For each row, the values are
        ** placed in elements of the array named "array" and ...code... is executed.
        ** If "array" and "code" are omitted, then no callback is every invoked.
        ** If "array" is an empty string, then the values are placed in variables
        ** that have the same name as the fields extracted by the query.
        **
        ** The onecolumn method is the equivalent of:
        **     lindex [$db eval $sql] 0
        */
        case (int)DB_enum.DB_ONECOLUMN:
        case (int)DB_enum.DB_EVAL:
        case (int)DB_enum.DB_EXISTS:
          {
            string zSql;      /* Next SQL statement to execute */
            string zLeft = "";     /* What is left after first stmt in zSql */
            sqlite3_stmt pStmt;   /* Compiled SQL statment */
            Tcl_Obj pArray;       /* Name of array into which results are written */
            Tcl_Obj pScript;      /* Script to run for each result set */
            Tcl_Obj[] apParm;      /* Parameters that need a TCL.Tcl_DecrRefCount() */
            int nParm;             /* Number of entries used in apParm[] */
            Tcl_Obj[] aParm = new Tcl_Obj[10];    /* Static space for apParm[] in the common case */
            Tcl_Obj pRet;         /* Value to be returned */
            SqlPreparedStmt pPreStmt;  /* Pointer to a prepared statement */
            int rc2;

            if ( choice == (int)DB_enum.DB_EVAL )
            {
              if ( objc < 3 || objc > 5 )
              {
                TCL.Tcl_WrongNumArgs( interp, 2, objv, "SQL ?ARRAY-NAME? ?SCRIPT?" );
                return TCL.TCL_ERROR;
              }
              pRet = TCL.Tcl_NewObj();
              TCL.Tcl_IncrRefCount( pRet );
            }
            else
            {
              if ( objc != 3 )
              {
                TCL.Tcl_WrongNumArgs( interp, 2, objv, "SQL" );
                return TCL.TCL_ERROR;
              }
              if ( choice == (int)DB_enum.DB_EXISTS )
              {
                pRet = TCL.Tcl_NewBooleanObj( 0 );
                TCL.Tcl_IncrRefCount( pRet );
              }
              else
              {
                pRet = null;
              }
            }
            if ( objc == 3 )
            {
              pArray = pScript = null;
            }
            else if ( objc == 4 )
            {
              pArray = null;
              pScript = objv[3];
            }
            else
            {
              pArray = objv[3];
              if ( TCL.Tcl_GetString( pArray ).Length == 0 ) pArray = null;
              pScript = objv[4];
            }

            TCL.Tcl_IncrRefCount( objv[2] );
            zSql = TCL.Tcl_GetStringFromObj( objv[2], 0 );
            while ( rc == TCL.TCL_OK && zSql.Length > 0 )
            {
              int i;                     /* Loop counter */
              int nVar;                  /* Number of bind parameters in the pStmt */
              int nCol = -1;             /* Number of columns in the result set */
              Tcl_Obj[] apColName = null;/* Array of column names */
              int len;                   /* String length of zSql */

              /* Try to find a SQL statement that has already been compiled and
              ** which matches the next sequence of SQL.
              */
              pStmt = null;
              zSql = zSql.TrimStart();// while ( isspace( zSql[0] ) ) { zSql++; }
              len = strlen30( zSql );
              for ( pPreStmt = pDb.stmtList ; pPreStmt != null ; pPreStmt = pPreStmt.pNext )
              {
                int n = pPreStmt.nSql;
                if ( len >= n
                && memcmp( pPreStmt.zSql, zSql, n ) == 0
                && ( n == zSql.Length || zSql[n - 1] == ';' )
                )
                {
                  pStmt = pPreStmt.pStmt;
                  zLeft = zSql.Substring( pPreStmt.nSql );

                  /* When a prepared statement is found, unlink it from the
                  ** cache list.  It will later be added back to the beginning
                  ** of the cache list in order to implement LRU replacement.
                  */
                  if ( pPreStmt.pPrev != null )
                  {
                    pPreStmt.pPrev.pNext = pPreStmt.pNext;
                  }
                  else
                  {
                    pDb.stmtList = pPreStmt.pNext;
                  }
                  if ( pPreStmt.pNext != null )
                  {
                    pPreStmt.pNext.pPrev = pPreStmt.pPrev;
                  }
                  else
                  {
                    pDb.stmtLast = pPreStmt.pPrev;
                  }
                  pDb.nStmt--;
                  break;
                }
              }

              /* If no prepared statement was found.  Compile the SQL text
              */
              if ( pStmt == null )
              {
#if TRACE
                int zLen = zSql.IndexOfAny(new char[] {'\n',';'} );
                Console.WriteLine( zLen ==-1 ? zSql : zSql.Substring(0,zLen+1));
#endif
                if ( SQLITE_OK != sqlite3_prepare_v2( pDb.db, zSql, -1, ref pStmt, ref zLeft ) )
                {
                  TCL.Tcl_SetObjResult( interp, dbTextToObj( sqlite3_errmsg( pDb.db ) ) );
                  rc = TCL.TCL_ERROR;
                  break;
                }
                if ( pStmt == null )
                {
                  if ( SQLITE_OK != sqlite3_errcode( pDb.db ) )
                  {
                    /* A compile-time error in the statement
                    */
                    TCL.Tcl_SetObjResult( interp, dbTextToObj( sqlite3_errmsg( pDb.db ) ) );
                    rc = TCL.TCL_ERROR;
                    break;
                  }
                  else
                  {
                    /* The statement was a no-op.  Continue to the next statement
                    ** in the SQL string.
                    */
                    zSql = zLeft;
                    continue;
                  }
                }
                Debug.Assert( pPreStmt == null );
              }

              /* Bind values to parameters that begin with $ or :
              */
              nVar = sqlite3_bind_parameter_count( pStmt );
              nParm = 0;
              if ( nVar > aParm.Length )
              {//sizeof(aParm)/sizeof(aParm[0]) ){
                apParm = new Tcl_Obj[nVar];//(Tcl_Obj**)Tcl_Alloc(nVar*sizeof(apParm[0]));
              }
              else
              {
                apParm = aParm;
              }
              for ( i = 1 ; i <= nVar ; i++ )
              {
                string zVar = sqlite3_bind_parameter_name( pStmt, i );
                if ( !String.IsNullOrEmpty( zVar ) && ( zVar[0] == '$' || zVar[0] == ':' || zVar[0] == '@' ) )
                {
                  Tcl_Obj pVar = TCL.Tcl_GetVar2Ex( interp, zVar.Substring( 1 ), null, 0 );
                  if ( pVar != null )
                  {
                    int n = 0;
                    string data;
                    string zType = pVar.typePtr != null ? pVar.typePtr : "";
                    char c = zType != "" ? zType[0] : '\0';
                    if ( zVar[0] == '@' ||
                       ( c == 'b' && zType == "bytearray" && pVar.InternalRep.ToString().Length == 0 ) )
                    {
                      /* Load a BLOB type if the Tcl variable is a bytearray and
                      ** it has no string representation or the host
                      ** parameter name begins with "@". */
                      data = Encoding.UTF8.GetString( TCL.Tcl_GetByteArrayFromObj( pVar, ref n ) );
                      sqlite3_bind_blob( pStmt, i, data, n, SQLITE_STATIC );
                      TCL.Tcl_IncrRefCount( pVar );
                      apParm[nParm++] = pVar;
                    }
                    else if ( c == 'b' && zType == "boolean" )
                    {
                      TCL.Tcl_GetIntFromObj( interp, pVar, ref n );
                      sqlite3_bind_int( pStmt, i, n );
                    }
                    else if ( c == 'd' && zType == "double" )
                    {
                      double r = 0;
                      TCL.Tcl_GetDoubleFromObj( interp, pVar, ref r );
                      sqlite3_bind_double( pStmt, i, r );
                    }
                    else if ( ( c == 'w' && zType == "wideInt" ) ||
                         ( c == 'i' && zType == "int" ) )
                    {
                      Tcl_WideInt v = 0;
                      TCL.Tcl_GetWideIntFromObj( interp, pVar, ref v );
                      sqlite3_bind_int64( pStmt, i, v );
                    }
                    else
                    {
                      data = TCL.Tcl_GetStringFromObj( pVar, ref n );
                      sqlite3_bind_text( pStmt, i, data, n, SQLITE_STATIC );
                      if ( pVar.typePtr == "bytearray" )
                      {
                        pStmt.aVar[i - 1].type = SQLITE_BLOB;
                        pStmt.aVar[i - 1].flags = MEM_Blob;
                        pStmt.aVar[i - 1].zBLOB = TCL.Tcl_GetByteArrayFromObj( pVar, ref n );
                      }
                      TCL.Tcl_IncrRefCount( pVar );
                      apParm[nParm++] = pVar;
                    }
                  }
                  else
                  {
                    sqlite3_bind_null( pStmt, i );
                  }
                }
              }

              /* Execute the SQL
              */
              while ( rc == TCL.TCL_OK && pStmt != null && SQLITE_ROW == sqlite3_step( pStmt ) )
              {

                /* Compute column names. This must be done after the first successful
                ** call to sqlite3_step(), in case the query is recompiled and the
                ** number or names of the returned columns changes.
                */
                Debug.Assert( pArray == null || pScript != null );
                if ( nCol < 0 )
                {
                  apColName = new Tcl_Obj[pStmt.nResColumn];
                  Tcl_Obj[] ap = ( pScript != null ? apColName : null );
                  nCol = computeColumnNames( interp, pStmt, ref ap, pArray );
                  if ( ap != null ) apColName = ap;
                }

                for ( i = 0 ; i < nCol ; i++ )
                {
                  Tcl_Obj pVal;

                  /* Set pVal to contain the i'th column of this row. */
                  switch ( sqlite3_column_type( pStmt, i ) )
                  {
                    case SQLITE_BLOB:
                      {
                        int bytes = sqlite3_column_bytes( pStmt, i );
                        byte[] zBlob = sqlite3_column_blob( pStmt, i );
                        if ( zBlob == null ) bytes = 0;
                        pVal = TCL.Tcl_NewByteArrayObj( zBlob, bytes );
                        break;
                      }
                    case SQLITE_INTEGER:
                      {
                        sqlite_int64 v = sqlite3_column_int64( pStmt, i );
                        if ( v >= -2147483647 && v <= 2147483647 )
                        {
                          pVal = TCL.Tcl_NewIntObj( (int)( v + 0 ) );
                        }
                        else
                        {
                          pVal = TCL.Tcl_NewWideIntObj( v );
                        }
                        break;
                      }
                    case SQLITE_FLOAT:
                      {
                        double r = sqlite3_column_double( pStmt, i );
                        pVal = TCL.Tcl_NewDoubleObj( r );
                        break;
                      }
                    case SQLITE_NULL:
                      {
                        pVal = dbTextToObj( pDb.zNull );
                        break;
                      }
                    default:
                      {
                        pVal = dbTextToObj( sqlite3_column_text( pStmt, i ) );
                        break;
                      }
                  }

                  if ( pScript != null )
                  {
                    if ( pArray == null )
                    {
                      TCL.Tcl_ObjSetVar2( interp, apColName[i], null, pVal, 0 );
                    }
                    else
                    {
                      TCL.Tcl_ObjSetVar2( interp, pArray, apColName[i], pVal, 0 );
                    }
                  }
                  else if ( choice == (int)DB_enum.DB_ONECOLUMN )
                  {
                    Debug.Assert( pRet == null );
                    if ( pRet == null )
                    {
                      pRet = pVal;
                      TCL.Tcl_IncrRefCount( pRet );
                    }
                    rc = TCL.TCL_BREAK;
                    i = nCol;
                  }
                  else if ( choice == (int)DB_enum.DB_EXISTS )
                  {
                    TCL.Tcl_DecrRefCount( ref pRet );
                    pRet = TCL.Tcl_NewBooleanObj( 1 );
                    TCL.Tcl_IncrRefCount( pRet );
                    rc = TCL.TCL_BREAK;
                    i = nCol;
                  }
                  else
                  {
                    TCL.Tcl_ListObjAppendElement( interp, pRet, pVal );
                  }
                }
                if ( pScript != null )
                {
                  rc = TCL.Tcl_EvalObjEx( interp, pScript, 0 );
                  if ( rc == TCL.TCL_CONTINUE )
                  {
                    rc = TCL.TCL_OK;
                  }
                }
              }
              if ( rc == TCL.TCL_BREAK )
              {
                rc = TCL.TCL_OK;
              }

              /* Free the column name objects */
              if ( pScript != null )
              {
                pDb.nStep = sqlite3_stmt_status( pStmt,
                                    SQLITE_STMTSTATUS_FULLSCAN_STEP, 0 );
                pDb.nSort = sqlite3_stmt_status( pStmt,
                                    SQLITE_STMTSTATUS_SORT, 0 );
                /* If the query returned no rows, but an array variable was
                ** specified, call computeColumnNames() now to populate the
                ** arrayname(*) variable.
                */
                if ( pArray != null && nCol < 0 )
                {
                  apColName = new Tcl_Obj[pStmt.nResColumn];
                  Tcl_Obj[] ap = ( pScript != null ? apColName : null );
                  nCol = computeColumnNames( interp, pStmt, ref ap, pArray );
                  if ( ap != null ) apColName = ap;
                }
                for ( i = 0 ; i < nCol ; i++ )
                {
                  if ( apColName != null && apColName[i] != null ) TCL.Tcl_DecrRefCount( ref apColName[i] );
                }
                TCL.Tcl_Free( ref apColName );
              }

              /* Free the bound string and blob parameters */
              for ( i = 0 ; i < nParm ; i++ )
              {
                TCL.Tcl_DecrRefCount( ref apParm[i] );
              }
              if ( apParm != aParm )
              {
                TCL.Tcl_Free( ref apParm );
              }

              /* Reset the statement.  If the result code is SQLITE_SCHEMA, then
              ** flush the statement cache and try the statement again.
              */
              rc2 = sqlite3_reset( pStmt );
              pDb.nStep = sqlite3_stmt_status( pStmt,
                                  SQLITE_STMTSTATUS_FULLSCAN_STEP, 1 );
              pDb.nSort = sqlite3_stmt_status( pStmt,
                                  SQLITE_STMTSTATUS_SORT, 1 );
              if ( SQLITE_OK != rc2 )
              {
                /* If a run-time error occurs, report the error and stop reading
                ** the SQL
                */
                TCL.Tcl_SetObjResult( interp, dbTextToObj( sqlite3_errmsg( pDb.db ) ) );
                sqlite3_finalize( ref pStmt );
                rc = TCL.TCL_ERROR;
                if ( pPreStmt != null ) TCL.Tcl_Free( ref pPreStmt );
                break;
              }
              else if ( pDb.maxStmt <= 0 )
              {
                /* If the cache is turned off, deallocated the statement */
                if ( pPreStmt != null ) TCL.Tcl_Free( ref pPreStmt );
                sqlite3_finalize( ref pStmt );
              }
              else
              {
                /* Everything worked and the cache is operational.
                ** Create a new SqlPreparedStmt structure if we need one.
                ** (If we already have one we can just reuse it.)
                */
                if ( pPreStmt == null )
                {
                  len = zSql.Length - zLeft.Length; // zLeft - zSql;
                  pPreStmt = new SqlPreparedStmt();//(SqlPreparedStmt*)Tcl_Alloc( sizeof(*pPreStmt) );
                  if ( pPreStmt == null ) return TCL.TCL_ERROR;
                  pPreStmt.pStmt = pStmt;
                  pPreStmt.nSql = len;
                  pPreStmt.zSql = sqlite3_sql( pStmt );
                  Debug.Assert( strlen30( pPreStmt.zSql ) == len );
                  Debug.Assert( 0 == memcmp( pPreStmt.zSql, zSql, len ) );
                }

                /* Add the prepared statement to the beginning of the cache list
                */
                pPreStmt.pNext = pDb.stmtList;
                pPreStmt.pPrev = null;
                if ( pDb.stmtList != null )
                {
                  pDb.stmtList.pPrev = pPreStmt;
                }
                pDb.stmtList = pPreStmt;
                if ( pDb.stmtLast == null )
                {
                  Debug.Assert( pDb.nStmt == 0 );
                  pDb.stmtLast = pPreStmt;
                }
                else
                {
                  Debug.Assert( pDb.nStmt > 0 );
                }
                pDb.nStmt++;

                /* If we have too many statement in cache, remove the surplus from the
                ** end of the cache list.
                */
                while ( pDb.nStmt > pDb.maxStmt )
                {
                  sqlite3_finalize( ref pDb.stmtLast.pStmt );
                  pDb.stmtLast = pDb.stmtLast.pPrev;
                  TCL.Tcl_Free( ref pDb.stmtLast.pNext );
                  pDb.stmtLast.pNext = null;
                  pDb.nStmt--;
                }
              }

              /* Proceed to the next statement */
              zSql = zLeft;
            }
            TCL.Tcl_DecrRefCount( ref objv[2] );

            if ( pRet != null )
            {
              if ( rc == TCL.TCL_OK )
              {
                TCL.Tcl_SetObjResult( interp, pRet );
              }
              TCL.Tcl_DecrRefCount( ref pRet );
            }
            else if ( rc == TCL.TCL_OK )
            {
              TCL.Tcl_ResetResult( interp );
            }
            break;
          }

        /*
        **     $db function NAME [-argcount N] SCRIPT
        **
        ** Create a new SQL function called NAME.  Whenever that function is
        ** called, invoke SCRIPT to evaluate the function.
        */
        case (int)DB_enum.DB_FUNCTION:
          {
            SqlFunc pFunc;
            Tcl_Obj pScript;
            string zName;
            int nArg = -1;
            if ( objc == 6 )
            {
              string z = TCL.Tcl_GetString( objv[3] );
              int n = strlen30( z );
              if ( n > 2 && z.StartsWith( "-argcount" ) )//strncmp( z, "-argcount", n ) == 0 )
              {
                if ( TCL.Tcl_GetIntFromObj( interp, objv[4], ref nArg ) ) return TCL.TCL_ERROR;
                if ( nArg < 0 )
                {
                  TCL.Tcl_AppendResult( interp, "number of arguments must be non-negative" );
                  return TCL.TCL_ERROR;
                }
              }
              pScript = objv[5];
            }
            else if ( objc != 4 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "NAME [-argcount N] SCRIPT" );
              return TCL.TCL_ERROR;
            }
            else
            {
              pScript = objv[3];
            }
            zName = TCL.Tcl_GetStringFromObj( objv[2], 0 );
            pFunc = findSqlFunc( pDb, zName );
            if ( pFunc == null ) return TCL.TCL_ERROR;
            if ( pFunc.pScript != null )
            {
              TCL.Tcl_DecrRefCount( ref pFunc.pScript );
            }
            pFunc.pScript = pScript;
            TCL.Tcl_IncrRefCount( pScript );
            pFunc.useEvalObjv = safeToUseEvalObjv( interp, pScript );
            rc = sqlite3_create_function( pDb.db, zName, nArg, SQLITE_UTF8,
            pFunc, tclSqlFunc, null, null );
            if ( rc != SQLITE_OK )
            {
              rc = TCL.TCL_ERROR;
              TCL.Tcl_SetResult( interp, sqlite3_errmsg( pDb.db ), TCL.TCL_VOLATILE );
            }
            break;
          }

        /*
        **     $db incrblob ?-readonly? ?DB? TABLE COLUMN ROWID
        */
        case (int)DB_enum.DB_INCRBLOB:
          {
#if SQLITE_OMIT_INCRBLOB
            TCL.Tcl_AppendResult( interp, "incrblob not available in this build" );
            return TCL.TCL_ERROR;
#else
int isReadonly = 0;
string zDb = "main" ;
string zTable;
string zColumn;
long iRow = 0;

/* Check for the -readonly option */
if ( objc > 3 && TCL.Tcl_GetString( objv[2] ) == "-readonly" )
{
isReadonly = 1;
}

if ( objc != ( 5 + isReadonly ) && objc != ( 6 + isReadonly ) )
{
TCL.Tcl_WrongNumArgs( interp, 2, objv, "?-readonly? ?DB? TABLE COLUMN ROWID" );
return TCL.TCL_ERROR;
}

if ( objc == ( 6 + isReadonly ) )
{
zDb =  TCL.Tcl_GetString( objv[2] )  ;
}
zTable = TCL.Tcl_GetString( objv[objc - 3] );
zColumn =  TCL.Tcl_GetString( objv[objc - 2] )  ;
rc = TCL.Tcl_GetWideIntFromObj( interp, objv[objc - 1], ref iRow ) ? 1 : 0;

if ( rc == TCL.TCL_OK )
{
rc = createIncrblobChannel(
interp, pDb, zDb, zTable, zColumn, iRow, isReadonly
);
}
#endif
            break;
          }
        //  /*
        //  **     $db interrupt
        //  **
        //  ** Interrupt the execution of the inner-most SQL interpreter.  This
        //  ** causes the SQL statement to return an error of SQLITE_INTERRUPT.
        //  */
        //  case (int)DB_enum.DB_INTERRUPT: {
        //    sqlite3_interrupt(pDb.db);
        //    break;
        //  }

        //  /*
        //  **     $db nullvalue ?STRING?
        //  **
        //  ** Change text used when a NULL comes back from the database. If ?STRING?
        //  ** is not present, then the current string used for NULL is returned.
        //  ** If STRING is present, then STRING is returned.
        //  **
        //  */
        case (int)DB_enum.DB_NULLVALUE:
          {
            if ( objc != 2 && objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "NULLVALUE" );
              return TCL.TCL_ERROR;
            }
            if ( objc == 3 )
            {
              int len = 0;
              string zNull = TCL.Tcl_GetStringFromObj( objv[2], ref   len );
              if ( pDb.zNull != null )
              {
                TCL.Tcl_Free( ref pDb.zNull );
              }
              if ( zNull != null && len > 0 )
              {
                pDb.zNull = zNull;
                //pDb.zNull = TCL.Tcl_Alloc( len + 1 );
                //strncpy( pDb.zNull, zNull, len );
                //pDb.zNull[len] = '\0';
              }
              else
              {
                pDb.zNull = null;
              }
            }
            TCL.Tcl_SetObjResult( interp, dbTextToObj( pDb.zNull ) );
            break;
          }

        /*
        **     $db last_insert_rowid
        **
        ** Return an integer which is the ROWID for the most recent insert.
        */
        case (int)DB_enum.DB_LAST_INSERT_ROWID:
          {
            Tcl_Obj pResult;
            Tcl_WideInt rowid;
            if ( objc != 2 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "" );
              return TCL.TCL_ERROR;
            }
            rowid = sqlite3_last_insert_rowid( pDb.db );
            pResult = TCL.Tcl_GetObjResult( interp );
            TCL.Tcl_SetLongObj( pResult, rowid );
            break;
          }

        /*
        ** The DB_ONECOLUMN method is implemented together with DB_EVAL.
        */

        /*    $db progress ?N CALLBACK?
        **
        ** Invoke the given callback every N virtual machine opcodes while executing
        ** queries.
        */
        case (int)DB_enum.DB_PROGRESS:
          {
            if ( objc == 2 )
            {
              if ( !String.IsNullOrEmpty( pDb.zProgress ) )
              {
                TCL.Tcl_AppendResult( interp, pDb.zProgress );
              }
            }
            else if ( objc == 4 )
            {
              string zProgress;
              int len = 0;
              int N = 0;
              if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref N ) )
              {
                return TCL.TCL_ERROR;
              };
              if ( !String.IsNullOrEmpty( pDb.zProgress ) )
              {
                TCL.Tcl_Free( ref pDb.zProgress );
              }
              zProgress = TCL.Tcl_GetStringFromObj( objv[3], len );
              if ( !String.IsNullOrEmpty( zProgress ) )
              {
                //pDb.zProgress = TCL.Tcl_Alloc( len + 1 );
                //memcpy( pDb.zProgress, zProgress, len + 1 );
                pDb.zProgress = zProgress;
              }
              else
              {
                pDb.zProgress = null;
              }
#if !SQLITE_OMIT_PROGRESS_CALLBACK
              if ( !String.IsNullOrEmpty( pDb.zProgress ) )
              {
                pDb.interp = interp;
                sqlite3_progress_handler( pDb.db, N, DbProgressHandler, pDb );
              }
              else
              {
                sqlite3_progress_handler( pDb.db, 0, null, 0 );
              }
#endif
            }
            else
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "N CALLBACK" );
              return TCL.TCL_ERROR;
            }
            break;
          }

        /*    $db profile ?CALLBACK?
        **
        ** Make arrangements to invoke the CALLBACK routine after each SQL statement
        ** that has run.  The text of the SQL and the amount of elapse time are
        ** appended to CALLBACK before the script is run.
        */
        case (int)DB_enum.DB_PROFILE:
          {
            if ( objc > 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?CALLBACK?" );
              return TCL.TCL_ERROR;
            }
            else if ( objc == 2 )
            {
              if ( !String.IsNullOrEmpty( pDb.zProfile ) )
              {
                TCL.Tcl_AppendResult( interp, pDb.zProfile );
              }
            }
            else
            {
              string zProfile;
              int len = 0;
              if ( !String.IsNullOrEmpty( pDb.zProfile ) )
              {
                TCL.Tcl_Free( ref pDb.zProfile );
              }
              zProfile = TCL.Tcl_GetStringFromObj( objv[2], ref len );
              if ( !String.IsNullOrEmpty( zProfile ) && len > 0 )
              {
                //pDb.zProfile = TCL.Tcl_Alloc( len + 1 );
                //memcpy( pDb.zProfile, zProfile, len + 1 );
                pDb.zProfile = zProfile;
              }
              else
              {
                pDb.zProfile = null;
              }
#if !SQLITE_OMIT_TRACE
              if ( !String.IsNullOrEmpty( pDb.zProfile ) )
              {
                pDb.interp = interp;
                sqlite3_profile( pDb.db, DbProfileHandler, pDb );
              }
              else
              {
                sqlite3_profile( pDb.db, null, null );
              }
#endif
            }
            break;
          }

        /*
        **     $db rekey KEY
        **
        ** Change the encryption key on the currently open database.
        */
        case (int)DB_enum.DB_REKEY:
          {
            int nKey = 0;
            byte[] pKey;
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "KEY" );
              return TCL.TCL_ERROR;
            }
            pKey = TCL.Tcl_GetByteArrayFromObj( objv[2], ref nKey );
#if SQLITE_HAS_CODEC
      rc = sqlite3_rekey(pDb.db, pKey, nKey);
      if( rc !=0){
        TCL.Tcl_AppendResult(interp, sqlite3ErrStr(rc));
        rc = TCL.TCL_ERROR;
      }
#endif
            break;
          }

        /*    $db restore ?DATABASE? FILENAME
        **
        ** Open a database file named FILENAME.  Transfer the content
        ** of FILENAME into the local database DATABASE (default: "main").
        */
        case (int)DB_enum.DB_RESTORE:
          {
            string zSrcFile;
            string zDestDb;
            sqlite3 pSrc = null;
            sqlite3_backup pBackup;
            int nTimeout = 0;

            if ( objc == 3 )
            {
              zDestDb = "main";
              zSrcFile = TCL.Tcl_GetString( objv[2] );
            }
            else if ( objc == 4 )
            {
              zDestDb = TCL.Tcl_GetString( objv[2] );
              zSrcFile = TCL.Tcl_GetString( objv[3] );
            }
            else
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?DATABASE? FILENAME" );
              return TCL.TCL_ERROR;
            }
            rc = sqlite3_open_v2( zSrcFile, ref pSrc, SQLITE_OPEN_READONLY, null );
            if ( rc != SQLITE_OK )
            {
              TCL.Tcl_AppendResult( interp, "cannot open source database: ",
              sqlite3_errmsg( pSrc ) );
              sqlite3_close( pSrc );
              return TCL.TCL_ERROR;
            }
            pBackup = sqlite3_backup_init( pDb.db, zDestDb, pSrc, "main" );
            if ( pBackup == null )
            {
              TCL.Tcl_AppendResult( interp, "restore failed: ",
              sqlite3_errmsg( pDb.db ) );
              sqlite3_close( pSrc );
              return TCL.TCL_ERROR;
            }
            while ( ( rc = sqlite3_backup_step( pBackup, 100 ) ) == SQLITE_OK
            || rc == SQLITE_BUSY )
            {
              if ( rc == SQLITE_BUSY )
              {
                if ( nTimeout++ >= 3 ) break;
                sqlite3_sleep( 100 );
              }
            }
            sqlite3_backup_finish( pBackup );
            if ( rc == SQLITE_DONE )
            {
              rc = TCL.TCL_OK;
            }
            else if ( rc == SQLITE_BUSY || rc == SQLITE_LOCKED )
            {
              TCL.Tcl_AppendResult( interp, "restore failed: source database busy"
               );
              rc = TCL.TCL_ERROR;
            }
            else
            {
              TCL.Tcl_AppendResult( interp, "restore failed: ",
              sqlite3_errmsg( pDb.db ) );
              rc = TCL.TCL_ERROR;
            }
            sqlite3_close( pSrc );
            break;
          }

        /*
        **     $db status (step|sort)
        **
        ** Display SQLITE_STMTSTATUS_FULLSCAN_STEP or
        ** SQLITE_STMTSTATUS_SORT for the most recent eval.
        */
        case (int)DB_enum.DB_STATUS:
          {
            int v;
            string zOp;
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "(step|sort)" );
              return TCL.TCL_ERROR;
            }
            zOp = TCL.Tcl_GetString( objv[2] );
            if ( zOp == "step" )
            {
              v = pDb.nStep;
            }
            else if ( zOp == "sort" )
            {
              v = pDb.nSort;
            }
            else
            {
              TCL.Tcl_AppendResult( interp, "bad argument: should be step or sort" );
              return TCL.TCL_ERROR;
            }
            TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( v ) );
            break;
          }

        /*
        **     $db timeout MILLESECONDS
        **
        ** Delay for the number of milliseconds specified when a file is locked.
        */
        case (int)DB_enum.DB_TIMEOUT:
          {
            int ms = 0;
            if ( objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "MILLISECONDS" );
              return TCL.TCL_ERROR;
            }
            if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref ms ) ) return TCL.TCL_ERROR;
            sqlite3_busy_timeout( pDb.db, ms );
            break;
          }

        /*
        **     $db total_changes
        **
        ** Return the number of rows that were modified, inserted, or deleted
        ** since the database handle was created.
        */
        case (int)DB_enum.DB_TOTAL_CHANGES:
          {
            Tcl_Obj pResult;
            if ( objc != 2 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "" );
              return TCL.TCL_ERROR;
            }
            pResult = TCL.Tcl_GetObjResult( interp );
            TCL.Tcl_SetIntObj( pResult, sqlite3_total_changes( pDb.db ) );
            break;
          }

        /*    $db trace ?CALLBACK?
        **
        ** Make arrangements to invoke the CALLBACK routine for each SQL statement
        ** that is executed.  The text of the SQL is appended to CALLBACK before
        ** it is executed.
        */
        case (int)DB_enum.DB_TRACE:
          {
            if ( objc > 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?CALLBACK?" );
              return TCL.TCL_ERROR;
            }
            else if ( objc == 2 )
            {
              if ( pDb.zTrace != null )
              {
                TCL.Tcl_AppendResult( interp, pDb.zTrace );
              }
            }
            else
            {
              string zTrace;
              int len = 0;
              if ( pDb.zTrace != null )
              {
                TCL.Tcl_Free( ref pDb.zTrace );
              }
              zTrace = TCL.Tcl_GetStringFromObj( objv[2], ref len );
              if ( zTrace != null && len > 0 )
              {
                //pDb.zTrace = TCL.Tcl_Alloc( len + 1 );
                pDb.zTrace = zTrace;//memcpy( pDb.zTrace, zTrace, len + 1 );
              }
              else
              {
                pDb.zTrace = null;
              }
#if !SQLITE_OMIT_TRACE
              if ( pDb.zTrace != null )
              {
                pDb.interp = interp;
                sqlite3_trace( pDb.db, (dxTrace)DbTraceHandler, pDb );
              }
              else
              {
                sqlite3_trace( pDb.db, null, null );
              }
#endif
            }
            break;
          }

        //  /*    $db transaction [-deferred|-immediate|-exclusive] SCRIPT
        //  **
        //  ** Start a new transaction (if we are not already in the midst of a
        //  ** transaction) and execute the TCL script SCRIPT.  After SCRIPT
        //  ** completes, either commit the transaction or roll it back if SCRIPT
        //  ** throws an exception.  Or if no new transation was started, do nothing.
        //  ** pass the exception on up the stack.
        //  **
        //  ** This command was inspired by Dave Thomas's talk on Ruby at the
        //  ** 2005 O'Reilly Open Source Convention (OSCON).
        //  */
        case (int)DB_enum.DB_TRANSACTION:
          {
            Tcl_Obj pScript;
            string zBegin = "SAVEPOINT _tcl_transaction";
            string zEnd;
            if ( objc != 3 && objc != 4 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "[TYPE] SCRIPT" );
              return TCL.TCL_ERROR;
            }
            if ( pDb.nTransaction != 0 )
            {
              zBegin = "SAVEPOINT _tcl_transaction";
            }
            else if ( pDb.nTransaction == 0 && objc == 4 )
            {
              string[] TTYPE_strs = {
"deferred",   "exclusive",  "immediate", null
};

              int ttype = 0;
              if ( TCL.Tcl_GetIndexFromObj( interp, objv[2], TTYPE_strs, "transaction type",
                              0, ref ttype ) )
              {
                return TCL.TCL_ERROR;
              }
              switch ( ttype )
              {
                case (int)TTYPE_enum.TTYPE_DEFERRED:    /* no-op */ ; break;
                case (int)TTYPE_enum.TTYPE_EXCLUSIVE: zBegin = "BEGIN EXCLUSIVE"; break;
                case (int)TTYPE_enum.TTYPE_IMMEDIATE: zBegin = "BEGIN IMMEDIATE"; break;
              }
            }
            pScript = objv[objc - 1];
            pDb.disableAuth++;
            rc = sqlite3_exec( pDb.db, zBegin, 0, 0, 0 );
            pDb.disableAuth--;
            if ( rc != SQLITE_OK )
            {
              TCL.Tcl_AppendResult( interp, sqlite3_errmsg( pDb.db ) );
              return TCL.TCL_ERROR;
            }
            pDb.nTransaction++;
            rc = TCL.Tcl_EvalObjEx( interp, pScript, 0 );
            pDb.nTransaction--;
            pDb.nTransaction--;

            if ( rc != TCL.TCL_ERROR )
            {
              if ( pDb.nTransaction != 0 )
              {
                zEnd = "RELEASE _tcl_transaction";
              }
              else
              {
                zEnd = "COMMIT";
              }
            }
            else
            {
              if ( pDb.nTransaction != 0 )
              {
                zEnd = "ROLLBACK TO _tcl_transaction ; RELEASE _tcl_transaction";
              }
              else
              {
                zEnd = "ROLLBACK";
              }
            }
            pDb.disableAuth++;
            if ( sqlite3_exec( pDb.db, zEnd, 0, 0, 0 ) != 0 )
            {
              /* This is a tricky scenario to handle. The most likely cause of an
              ** error is that the exec() above was an attempt to commit the
              ** top-level transaction that returned SQLITE_BUSY. Or, less likely,
              ** that an IO-error has occurred. In either case, throw a Tcl exception
              ** and try to rollback the transaction.
              **
              ** But it could also be that the user executed one or more BEGIN,
              ** COMMIT, SAVEPOINT, RELEASE or ROLLBACK commands that are confusing
              ** this method's logic. Not clear how this would be best handled.
              */
              if ( rc != TCL.TCL_ERROR )
              {
                TCL.Tcl_AppendResult( interp, sqlite3_errmsg( pDb.db ) );
                rc = TCL.TCL_ERROR;
              }
              sqlite3_exec( pDb.db, "ROLLBACK", 0, 0, 0 );
            }
            pDb.disableAuth--;
            break;
          }

        /*
        **    $db update_hook ?script?
        **    $db rollback_hook ?script?
        */
        case (int)DB_enum.DB_UPDATE_HOOK:
        case (int)DB_enum.DB_ROLLBACK_HOOK:
          {

            /* set ppHook to point at pUpdateHook or pRollbackHook, depending on
            ** whether [$db update_hook] or [$db rollback_hook] was invoked.
            */
            Tcl_Obj ppHook;
            if ( choice == (int)DB_enum.DB_UPDATE_HOOK )
            {
              ppHook = pDb.pUpdateHook;
            }
            else
            {
              ppHook = pDb.pRollbackHook;
            }

            if ( objc != 2 && objc != 3 )
            {
              TCL.Tcl_WrongNumArgs( interp, 2, objv, "?SCRIPT?" );
              return TCL.TCL_ERROR;
            }
            if ( ppHook != null )
            {
              TCL.Tcl_SetObjResult( interp, ppHook );
              if ( objc == 3 )
              {
                TCL.Tcl_DecrRefCount( ref ppHook );
                ppHook = null;
              }
            }
            if ( objc == 3 )
            {
              Debug.Assert( null == ppHook );
              if ( objv[2] != null )//TCL.Tcl_GetCharLength( objv[2] ) > 0 )
              {
                ppHook = objv[2];
                TCL.Tcl_IncrRefCount( ppHook );
              }
            }
            if ( choice == (int)DB_enum.DB_UPDATE_HOOK )
            {
              pDb.pUpdateHook = ppHook;
            }
            else
            {
              pDb.pRollbackHook = ppHook;
            }
            sqlite3_update_hook( pDb.db, ( pDb.pUpdateHook != null ? (dxUpdateCallback)DbUpdateHandler : null ), pDb );
            sqlite3_rollback_hook( pDb.db, ( pDb.pRollbackHook != null ? (dxRollbackCallback)DbRollbackHandler : null ), pDb );

            break;
          }

        /*    $db version
        **
        ** Return the version string for this database.
        */
        case (int)DB_enum.DB_VERSION:
          {
            TCL.Tcl_SetResult( interp, sqlite3_libversion(), TCL.TCL_STATIC );
            break;
          }

        default:
          Debug.Assert( false, "Missing switch:" + objv[1].ToString() );
          break;
      } /* End of the SWITCH statement */
      return rc;
    }

    /*
    **   sqlite3 DBNAME FILENAME ?-vfs VFSNAME? ?-key KEY? ?-readonly BOOLEAN?
    **                           ?-create BOOLEAN? ?-nomutex BOOLEAN?
    **
    ** This is the main Tcl command.  When the "sqlite" Tcl command is
    ** invoked, this routine runs to process that command.
    **
    ** The first argument, DBNAME, is an arbitrary name for a new
    ** database connection.  This command creates a new command named
    ** DBNAME that is used to control that connection.  The database
    ** connection is deleted when the DBNAME command is deleted.
    **
    ** The second argument is the name of the database file.
    **
    */
    static int DbMain( object cd, Tcl_Interp interp, int objc, Tcl_Obj[] objv )
    {
      SqliteDb p;
      byte[] pKey = null;
      int nKey = 0;
      string zArg;
      string zErrMsg;
      int i;
      string zFile;
      string zVfs = null;
      int flags;
      Tcl_DString translatedFilename;
      /* In normal use, each TCL interpreter runs in a single thread.  So
      ** by default, we can turn of mutexing on SQLite database connections.
      ** However, for testing purposes it is useful to have mutexes turned
      ** on.  So, by default, mutexes default off.  But if compiled with
      ** SQLITE_TCL_DEFAULT_FULLMUTEX then mutexes default on.
      */
#if SQLITE_TCL_DEFAULT_FULLMUTEX
flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX;
#else
      flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_NOMUTEX;
#endif
      if ( objc == 2 )
      {
        zArg = TCL.Tcl_GetStringFromObj( objv[1], 0 );
        if ( zArg == "-version" )
        {
          TCL.Tcl_AppendResult( interp, sqlite3_version, null );
          return TCL.TCL_OK;
        }
        if ( zArg == "-has-codec" )
        {
#if SQLITE_HAS_CODEC
TCL.Tcl_AppendResult(interp,"1");
#else
          TCL.Tcl_AppendResult( interp, "0", null );
#endif
          return TCL.TCL_OK;
        }
        if ( zArg == "-tcl-uses-utf" ) { TCL.Tcl_AppendResult( interp, "1", null ); return TCL.TCL_OK; }
      }
      for ( i = 3 ; i + 1 < objc ; i += 2 )
      {
        zArg = TCL.Tcl_GetString( objv[i] );
        if ( zArg == "-key" )
        {
          pKey = TCL.Tcl_GetByteArrayFromObj( objv[i + 1], ref nKey );
        }
        else if ( zArg == "-vfs" )
        {
          i++;
          zVfs = TCL.Tcl_GetString( objv[i] );
        }
        else if ( zArg == "-readonly" )
        {
          bool b = false;
          if ( TCL.Tcl_GetBooleanFromObj( interp, objv[i + 1], ref b ) ) return TCL.TCL_ERROR;
          if ( b )
          {
            flags &= ~( SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE );
            flags |= SQLITE_OPEN_READONLY;
          }
          else
          {
            flags &= ~SQLITE_OPEN_READONLY;
            flags |= SQLITE_OPEN_READWRITE;
          }
        }
        else if ( zArg == "-create" )
        {
          bool b = false;
          if ( TCL.Tcl_GetBooleanFromObj( interp, objv[i + 1], ref b ) ) return TCL.TCL_ERROR;
          if ( b && ( flags & SQLITE_OPEN_READONLY ) == 0 )
          {
            flags |= SQLITE_OPEN_CREATE;
          }
          else
          {
            flags &= ~SQLITE_OPEN_CREATE;
          }
        }
        else if ( zArg == "-nomutex" )
        {
          bool b = false;
          if ( TCL.Tcl_GetBooleanFromObj( interp, objv[i + 1], ref b ) ) return TCL.TCL_ERROR;
          if ( b )
          {
            flags |= SQLITE_OPEN_NOMUTEX;
            flags &= ~SQLITE_OPEN_FULLMUTEX;
          }
          else
          {
            flags &= ~SQLITE_OPEN_NOMUTEX;
          }
        }
        else if ( zArg == "-fullmutex" )//strcmp( zArg, "-fullmutex" ) == 0 )
        {
          bool b = false;
          if ( TCL.Tcl_GetBooleanFromObj( interp, objv[i + 1], ref b ) ) return TCL.TCL_ERROR;
          if ( b )
          {
            flags |= SQLITE_OPEN_FULLMUTEX;
            flags &= ~SQLITE_OPEN_NOMUTEX;
          }
          else
          {
            flags &= ~SQLITE_OPEN_FULLMUTEX;
          }
        }
        else
        {
          TCL.Tcl_AppendResult( interp, "unknown option: ", zArg, null );
          return TCL.TCL_ERROR;
        }
      }
      if ( objc < 3 || ( objc & 1 ) != 1 )
      {
        TCL.Tcl_WrongNumArgs( interp, 1, objv,
        "HANDLE FILENAME ?-vfs VFSNAME? ?-readonly BOOLEAN? ?-create BOOLEAN? ?-nomutex BOOLEAN? ?-fullmutex BOOLEAN?"
#if SQLITE_HAS_CODEC
" ?-key CODECKEY?"
#endif
 );
        return TCL.TCL_ERROR;
      }
      zErrMsg = "";
      p = new SqliteDb();//(SqliteDb*)Tcl_Alloc( sizeof(*p) );
      if ( p == null )
      {
        TCL.Tcl_SetResult( interp, "malloc failed", TCL.TCL_STATIC );
        return TCL.TCL_ERROR;
      }
      //memset(p, 0, sizeof(*p));
      zFile = TCL.Tcl_GetStringFromObj( objv[2], 0 );
      //zFile = TCL.Tcl_TranslateFileName( interp, zFile, ref translatedFilename );
      sqlite3_open_v2( zFile, ref p.db, flags, zVfs );
      //Tcl_DStringFree( ref translatedFilename );
      if ( SQLITE_OK != sqlite3_errcode( p.db ) )
      {
        zErrMsg = sqlite3_errmsg( p.db );// sqlite3_mprintf( "%s", sqlite3_errmsg( p.db ) );
        sqlite3_close( p.db );
        p.db = null;
      }
#if SQLITE_HAS_CODEC
if( p.db ){
sqlite3_key(p.db, pKey, nKey);
}
#endif
      if ( p.db == null )
      {
        TCL.Tcl_SetResult( interp, zErrMsg, TCL.TCL_VOLATILE );
        TCL.Tcl_Free( ref p );
        zErrMsg = "";// //sqlite3DbFree( db, ref zErrMsg );
        return TCL.TCL_ERROR;
      }
      p.maxStmt = NUM_PREPARED_STMTS;
      p.interp = interp;
      zArg = TCL.Tcl_GetStringFromObj( objv[1], 0 );
      TCL.Tcl_CreateObjCommand( interp, zArg, (Interp.dxObjCmdProc)DbObjCmd, p, (Interp.dxCmdDeleteProc)DbDeleteCmd );
      return TCL.TCL_OK;
    }

    /*
    ** Provide a dummy TCL.Tcl_InitStubs if we are using this as a static
    ** library.
    */
#if !USE_TCL_STUBS
    //# undef  TCL.Tcl_InitStubs
    static void Tcl_InitStubs( Tcl_Interp interp, string s, int i ) { }
#endif

    /*
** Make sure we have a PACKAGE_VERSION macro defined.  This will be
** defined automatically by the TEA makefile.  But other makefiles
** do not define it.
*/
#if !PACKAGE_VERSION
    const string PACKAGE_VERSION = SQLITE_VERSION;//# define PACKAGE_VERSION SQLITE_VERSION
#endif

    /*
** Initialize this module.
**
** This Tcl module contains only a single new Tcl command named "sqlite".
** (Hence there is no namespace.  There is no point in using a namespace
** if the extension only supplies one new name!)  The "sqlite" command is
** used to open a new SQLite database.  See the DbMain() routine above
** for additional information.
*/
    //EXTERN int Sqlite3_Init(Tcl_Interp interp){
    static public int Sqlite3_Init( Tcl_Interp interp )
    {
      Tcl_InitStubs( interp, "tclsharp 1.1", 0 );
      TCL.Tcl_CreateObjCommand( interp, "sqlite3", (Interp.dxObjCmdProc)DbMain, null, null );
      TCL.Tcl_PkgProvide( interp, "sqlite3", PACKAGE_VERSION );
      TCL.Tcl_CreateObjCommand( interp, "sqlite", (Interp.dxObjCmdProc)DbMain, null, null );
      TCL.Tcl_PkgProvide( interp, "sqlite", PACKAGE_VERSION );
      return TCL.TCL_OK;
    }
    //EXTERN int Tclsqlite3_Init(Tcl_Interp interp){ return Sqlite3_Init(interp); }
    //EXTERN int Sqlite3_SafeInit(Tcl_Interp interp){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite3_SafeInit(Tcl_Interp interp){ return TCL.TCL_OK; }
    //EXTERN int Sqlite3_Unload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite3_Unload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Sqlite3_SafeUnload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite3_SafeUnload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK;}


#if !SQLITE_3_SUFFIX_ONLY
    //EXTERN int Sqlite_Init(Tcl_Interp interp){ return Sqlite3_Init(interp); }
    //EXTERN int Tclsqlite_Init(Tcl_Interp interp){ return Sqlite3_Init(interp); }
    //EXTERN int Sqlite_SafeInit(Tcl_Interp interp){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite_SafeInit(Tcl_Interp interp){ return TCL.TCL_OK; }
    //EXTERN int Sqlite_Unload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite_Unload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Sqlite_SafeUnload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK; }
    //EXTERN int Tclsqlite_SafeUnload(Tcl_Interp *interp, int flags){ return TCL.TCL_OK;}
#endif

#if TCLSH
/*****************************************************************************
** The code that follows is used to build standalone TCL interpreters
** that are statically linked with SQLite.
*/

/*
** If the macro TCLSH is one, then put in code this for the
** "main" routine that will initialize Tcl and take input from
** standard input, or if a file is named on the command line
** the TCL interpreter reads and evaluates that file.
*/
#if TCLSH // TCLSH==1
static char zMainloop[] =
"set line {}\n"
"while {![eof stdin]} {\n"
"if {$line!=\"\"} {\n"
"puts -nonewline \"> \"\n"
"} else {\n"
"puts -nonewline \"% \"\n"
"}\n"
"flush stdout\n"
"append line [gets stdin]\n"
"if {[info complete $line]} {\n"
"if {[catch {uplevel #0 $line} result]} {\n"
"puts stderr \"Error: $result\"\n"
"} elseif {$result!=\"\"} {\n"
"puts $result\n"
"}\n"
"set line {}\n"
"} else {\n"
"append line \\n\n"
"}\n"
"}\n"
;
#endif

/*
** If the macro TCLSH is two, then get the main loop code out of
** the separate file "spaceanal_tcl.h".
*/
#if TCLSH // TCLSH==2
//static char zMainloop[] =
//    #include "spaceanal_tcl.h"
//;
#endif

#define TCLSH_MAIN //main   /* Needed to fake out mktclapp */
int TCLSH_MAIN(int argc, char **argv){
Tcl_Interp interp;
/* Call sqlite3_shutdown() once before doing anything else. This is to
** test that sqlite3_shutdown() can be safely called by a process before
** sqlite3_initialize() is. */
sqlite3_shutdown();

TCL.Tcl_FindExecutable(argv[0]);
interp = TCL.Tcl_CreateInterp();
Sqlite3_Init(interp);
#if SQLITE_TEST
{
extern int Md5_Init(Tcl_Interp*);
extern int Sqliteconfig_Init(Tcl_Interp*);
extern int Sqlitetest1_Init(Tcl_Interp*);
extern int Sqlitetest2_Init(Tcl_Interp*);
extern int Sqlitetest3_Init(Tcl_Interp*);
extern int Sqlitetest4_Init(Tcl_Interp*);
extern int Sqlitetest5_Init(Tcl_Interp*);
extern int Sqlitetest6_Init(Tcl_Interp*);
extern int Sqlitetest7_Init(Tcl_Interp*);
extern int Sqlitetest8_Init(Tcl_Interp*);
extern int Sqlitetest9_Init(Tcl_Interp*);
extern int Sqlitetestasync_Init(Tcl_Interp*);
extern int Sqlitetest_autoext_Init(Tcl_Interp*);
extern int Sqlitetest_func_Init(Tcl_Interp*);
extern int Sqlitetest_hexio_Init(Tcl_Interp*);
extern int Sqlitetest_malloc_Init(Tcl_Interp*);
extern int Sqlitetest_mutex_Init(Tcl_Interp*);
extern int Sqlitetestschema_Init(Tcl_Interp*);
extern int Sqlitetestsse_Init(Tcl_Interp*);
extern int Sqlitetesttclvar_Init(Tcl_Interp*);
extern int SqlitetestThread_Init(Tcl_Interp*);
extern int SqlitetestOnefile_Init();
extern int SqlitetestOsinst_Init(Tcl_Interp*);
extern int Sqlitetestbackup_Init(Tcl_Interp*);

Md5_Init(interp);
Sqliteconfig_Init(interp);
Sqlitetest1_Init(interp);
Sqlitetest2_Init(interp);
Sqlitetest3_Init(interp);
Sqlitetest4_Init(interp);
Sqlitetest5_Init(interp);
Sqlitetest6_Init(interp);
Sqlitetest7_Init(interp);
Sqlitetest8_Init(interp);
Sqlitetest9_Init(interp);
Sqlitetestasync_Init(interp);
Sqlitetest_autoext_Init(interp);
Sqlitetest_func_Init(interp);
Sqlitetest_hexio_Init(interp);
Sqlitetest_malloc_Init(interp);
Sqlitetest_mutex_Init(interp);
Sqlitetestschema_Init(interp);
Sqlitetesttclvar_Init(interp);
SqlitetestThread_Init(interp);
SqlitetestOnefile_Init(interp);
SqlitetestOsinst_Init(interp);
Sqlitetestbackup_Init(interp);

#if SQLITE_SSE
Sqlitetestsse_Init(interp);
#endif
}
#endif
if( argc>=2 || TCLSH==2 ){
int i;
char zArgc[32];
sqlite3_snprintf(sizeof(zArgc), zArgc, "%d", argc-(3-TCLSH));
TCL.Tcl_SetVar(interp,"argc", zArgc, TCL.TCL_GLOBAL_ONLY);
TCL.Tcl_SetVar(interp,"argv0",argv[1],TCL_GLOBAL_ONLY);
TCL.Tcl_SetVar(interp,"argv", "", TCL.TCL_GLOBAL_ONLY);
for(i=3-TCLSH; i<argc; i++){
TCL.Tcl_SetVar(interp, "argv", argv[i],
TCL.TCL_GLOBAL_ONLY | TCL.Tcl_LIST_ELEMENT | TCL.Tcl_APPEND_VALUE);
}
if( TCLSH==1 && TCL.Tcl_EvalFile(interp, argv[1])!=TCL_OK ){
const string zInfo = TCL.Tcl_GetVar(interp, "errorInfo", TCL.TCL_GLOBAL_ONLY);
if( zInfo==0 ) zInfo = TCL.Tcl_GetStringResult(interp);
fprintf(stderr,"%s: %s\n", *argv, zInfo);
return 1;
}
}
if( argc<=1 || TCLSH==2 ){
TCL.Tcl_GlobalEval(interp, zMainloop);
}
return 0;
}
#endif // * TCLSH */

  }
#endif
}
