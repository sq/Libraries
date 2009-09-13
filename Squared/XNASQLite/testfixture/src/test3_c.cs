using System.Diagnostics;

using i64 = System.Int64;
using u32 = System.UInt32;
using u64 = System.UInt64;

using Pgno = System.UInt32;


namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_CmdInfo = tcl.lang.Command;
  using Tcl_CmdProc = tcl.lang.Interp.dxObjCmdProc;
  using System.Text;

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
    ** Code for testing the btree.c module in SQLite.  This code
    ** is not included in the SQLite library.  It is used for automated
    ** testing of the SQLite library.
    **
    ** $Id: test3.c,v 1.111 2009/07/09 05:07:38 danielk1977 Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */
    //#include "sqliteInt.h"
    //#include "btreeInt.h"
    //#include "tcl.h"
    //#include <stdlib.h>
    //#include <string.h>

    /*
    ** Interpret an SQLite error number
    */
    static string errorName( int rc )
    {
      string zName;
      switch ( rc )
      {
        case SQLITE_OK: zName = "SQLITE_OK"; break;
        case SQLITE_ERROR: zName = "SQLITE_ERROR"; break;
        case SQLITE_PERM: zName = "SQLITE_PERM"; break;
        case SQLITE_ABORT: zName = "SQLITE_ABORT"; break;
        case SQLITE_BUSY: zName = "SQLITE_BUSY"; break;
        case SQLITE_NOMEM: zName = "SQLITE_NOMEM"; break;
        case SQLITE_READONLY: zName = "SQLITE_READONLY"; break;
        case SQLITE_INTERRUPT: zName = "SQLITE_INTERRUPT"; break;
        case SQLITE_IOERR: zName = "SQLITE_IOERR"; break;
        case SQLITE_CORRUPT: zName = "SQLITE_CORRUPT"; break;
        case SQLITE_FULL: zName = "SQLITE_FULL"; break;
        case SQLITE_CANTOPEN: zName = "SQLITE_CANTOPEN"; break;
        case SQLITE_PROTOCOL: zName = "SQLITE_PROTOCOL"; break;
        case SQLITE_EMPTY: zName = "SQLITE_EMPTY"; break;
        case SQLITE_LOCKED: zName = "SQLITE_LOCKED"; break;
        default: zName = "SQLITE_Unknown"; break;
      }
      return zName;
    }

    /*
    ** A bogus sqlite3 connection structure for use in the btree
    ** tests.
    */
    static sqlite3 sDb = new sqlite3();
    static int nRefSqlite3 = 0;

    /*
    ** Usage:   btree_open FILENAME NCACHE FLAGS
    **
    ** Open a new database
    */
    static int btree_open(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt = null;
      int rc; int nCache = 0; int flags = 0;
      string zBuf = "";
      if ( argc != 4 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " FILENAME NCACHE FLAGS\"", "" );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetInt( interp, argv[2], ref nCache ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetInt( interp, argv[3], ref flags ) ) return TCL.TCL_ERROR;
      nRefSqlite3++;
      if ( nRefSqlite3 == 1 )
      {
        sDb.pVfs = sqlite3_vfs_find( null );
        sDb.mutex = sqlite3MutexAlloc( SQLITE_MUTEX_RECURSIVE );
        sqlite3_mutex_enter( sDb.mutex );
      }
      rc = sqlite3BtreeOpen( argv[1].ToString(), sDb, ref pBt, flags,
      SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_MAIN_DB );
      if ( rc != SQLITE_OK )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null );
        return TCL.TCL_ERROR;
      }
      sqlite3BtreeSetCacheSize( pBt, nCache );
      sqlite3_snprintf( 100, ref zBuf, "->%p", pBt );
      if ( TCL.Tcl_CreateCommandPointer( interp, zBuf, pBt ) )
      {
        return TCL.TCL_ERROR;
      }
      else
        TCL.Tcl_AppendResult( interp, zBuf, null );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:   btree_close ID
    **
    ** Close the given database.
    */
    static int btree_close(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt;
      int rc;
      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"", null );
        return TCL.TCL_ERROR;
      }
      pBt = (Btree)sqlite3TestTextToPtr( interp, argv[1].ToString() );
      rc = sqlite3BtreeClose( ref pBt );
      if ( rc != SQLITE_OK )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null );
        return TCL.TCL_ERROR;
      }
      nRefSqlite3--;
      if ( nRefSqlite3 == 0 )
      {
        sqlite3_mutex_leave( sDb.mutex );
        sqlite3_mutex_free( ref sDb.mutex );
        sDb.mutex = null;
        sDb.pVfs = null;
      }
      return TCL.TCL_OK;
    }


    /*
    ** Usage:   btree_begin_transaction ID
    **
    ** Start a new transaction
    */
    static int btree_begin_transaction(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt;
      int rc;
      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"", null );
        return TCL.TCL_ERROR;
      }
      pBt = (Btree)sqlite3TestTextToPtr( interp, argv[1].ToString() );
      sqlite3BtreeEnter( pBt );
      rc = sqlite3BtreeBeginTrans( pBt, 1 );
      sqlite3BtreeLeave( pBt );
      if ( rc != SQLITE_OK )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null ); ;
        return TCL.TCL_ERROR;
      }
      return TCL.TCL_OK;
    }

    /*
    ** Usage:   btree_pager_stats ID
    **
    ** Returns pager statistics
    */
    static int btree_pager_stats(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt;
      int i;
      int[] a;

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pBt = (Btree)sqlite3TestTextToPtr( interp, argv[1].ToString() );

      /* Normally in this file, with a b-tree handle opened using the
      ** [btree_open] command it is safe to call sqlite3BtreeEnter() directly.
      ** But this function is sometimes called with a btree handle obtained
      ** from an open SQLite connection (using [btree_from_db]). In this case
      ** we need to obtain the mutex for the controlling SQLite handle before
      ** it is safe to call sqlite3BtreeEnter().
      */
      sqlite3_mutex_enter( pBt.db.mutex );

      sqlite3BtreeEnter( pBt );
      a = sqlite3PagerStats( sqlite3BtreePager( pBt ) );
      for ( i = 0 ; i < 11 ; i++ )
      {
        string[] zName = new string[]{
"ref", "page", "max", "size", "state", "err",
"hit", "miss", "ovfl", "read", "write"
};
        string zBuf = "";//char zBuf[100];
        TCL.Tcl_AppendElement( interp, zName[i] );
        sqlite3_snprintf( 100, ref zBuf, "%d", a[i] );
        TCL.Tcl_AppendElement( interp, zBuf );
      }
      sqlite3BtreeLeave( pBt );
      /* Release the mutex on the SQLite handle that controls this b-tree */
      sqlite3_mutex_leave( pBt.db.mutex );
      return TCL.TCL_OK;
    }

    /*
    ** Usage:   btree_cursor ID TABLENUM WRITEABLE
    **
    ** Create a new cursor.  Return the ID for the cursor.
    */
    static int btree_cursor(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt;
      int iTable = 0;
      BtCursor pCur;
      int rc=0;
      int wrFlag = 0;
      string zBuf = "";//char zBuf[30];

      if ( argc != 4 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID TABLENUM WRITEABLE\"" );
        return TCL.TCL_ERROR;
      }
      pBt = (Btree)sqlite3TestTextToPtr( interp, argv[1].ToString() );
      if ( TCL.Tcl_GetInt( interp, argv[2], ref  iTable ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetBoolean( interp, argv[3], ref wrFlag ) ) return TCL.TCL_ERROR;
      //pCur = (BtCursor )ckalloc(sqlite3BtreeCursorSize());
      pCur = new BtCursor();// memset( pCur, 0, sqlite3BtreeCursorSize() );
      sqlite3BtreeEnter( pBt );
#if !SQLITE_OMIT_SHARED_CACHE
      rc = sqlite3BtreeLockTable( pBt, iTable, wrFlag );
#endif
      if ( rc == SQLITE_OK )
      {
        rc = sqlite3BtreeCursor( pBt, iTable, wrFlag, null, pCur );
      }
      sqlite3BtreeLeave( pBt );
      if ( rc != 0 )
      {
        pCur = null;// ckfree( pCur );
        TCL.Tcl_AppendResult( interp, errorName( rc ), null ); ;
        return TCL.TCL_ERROR;
      }
      sqlite3_snprintf( 30, ref  zBuf, "->%p", pCur );
      if ( TCL.Tcl_CreateCommandPointer( interp, zBuf, pCur ) )
      {
        return TCL.TCL_ERROR;
      }
      else
        TCL.Tcl_AppendResult( interp, zBuf );
      return SQLITE_OK;
    }

    /*
    ** Usage:   btree_close_cursor ID
    **
    ** Close a cursor opened using btree_cursor.
    */
    static int btree_close_cursor(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      BtCursor pCur;
      Btree pBt;
      int rc;

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pCur = (BtCursor)sqlite3TestTextToPtr( interp, argv[1].ToString() );
      pBt = pCur.pBtree;
      sqlite3BtreeEnter( pBt );
      rc = sqlite3BtreeCloseCursor( pCur );
      sqlite3BtreeLeave( pBt );
      pCur = null;//ckfree( (char*)pCur );
      if ( rc != 0 )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null ); ;
        return TCL.TCL_ERROR;
      }
      return SQLITE_OK;
    }

    /*
    ** Usage:   btree_next ID
    **
    ** Move the cursor to the next entry in the table.  Return 0 on success
    ** or 1 if the cursor was already on the last entry in the table or if
    ** the table is empty.
    */
    static int btree_next(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      BtCursor pCur;
      int rc;
      int res = 0;
      string zBuf = "";//char zBuf[100];

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pCur = (BtCursor)sqlite3TestTextToPtr( interp, argv[1].ToString() );
#if SQLITE_TEST
      sqlite3BtreeEnter( pCur.pBtree );
#endif
      rc = sqlite3BtreeNext( pCur, ref res );
#if SQLITE_TEST
      sqlite3BtreeLeave( pCur.pBtree );
#endif
      if ( rc != 0 )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null ); ;
        return TCL.TCL_ERROR;
      }
      sqlite3_snprintf( 100, ref zBuf, "%d", res );
      TCL.Tcl_AppendResult( interp, zBuf );
      return SQLITE_OK;
    }

    /*
    ** Usage:   btree_first ID
    **
    ** Move the cursor to the first entry in the table.  Return 0 if the
    ** cursor was left point to something and 1 if the table is empty.
    */
    static int btree_first(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      BtCursor pCur;
      int rc;
      int res = 0;
      string zBuf = "";//[100];

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pCur = (BtCursor)sqlite3TestTextToPtr( interp, argv[1].ToString() );
#if SQLITE_TEST
      sqlite3BtreeEnter( pCur.pBtree );
#endif
      rc = sqlite3BtreeFirst( pCur, ref res );
#if SQLITE_TEST
      sqlite3BtreeLeave( pCur.pBtree );
#endif
      if ( rc != 0 )
      {
        TCL.Tcl_AppendResult( interp, errorName( rc ), null ); ;
        return TCL.TCL_ERROR;
      }
      sqlite3_snprintf( 100, ref zBuf, "%d", res );
      TCL.Tcl_AppendResult( interp, zBuf );
      return SQLITE_OK;
    }

    /*
    ** Usage:   btree_eof ID
    **
    ** Return TRUE if the given cursor is not pointing at a valid entry.
    ** Return FALSE if the cursor does point to a valid entry.
    */
    //static int btree_eof(
    //  object NotUsed,
    // Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  BtCursor pCur;
    //  int rc;
    //  char zBuf[50];

    //  if( argc!=2 ){
    //   TCL.Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0].ToString(),
    //       " ID\"");
    //    return TCL.TCL_ERROR;
    //  }
    //  pCur = (BtCursor)sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  sqlite3BtreeEnter(pCur.pBtree);
    //  rc = sqlite3BtreeEof(pCur);
    //  sqlite3BtreeLeave(pCur.pBtree);
    //  sqlite3_snprintf(100, ref zBuf, "%d", rc);
    // TCL.Tcl_AppendResult(interp, zBuf);
    //  return SQLITE_OK;
    //}

    /*
    ** Usage:   btree_payload_size ID
    **
    ** Return the number of bytes of payload
    */
    static int btree_payload_size(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      BtCursor pCur;
      i64 n1 = 0;
      u32 n2 = 0;
      string zBuf = "";//[50];

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pCur = (BtCursor)sqlite3TestTextToPtr( interp, argv[1].ToString() );
#if SQLITE_TEST
      sqlite3BtreeEnter( pCur.pBtree );
#endif

  /* The cursor may be in "require-seek" state. If this is the case, the
  ** call to BtreeDataSize() will fix it. */
      sqlite3BtreeDataSize( pCur, ref n2 );
      if ( pCur.apPage[pCur.iPage].intKey != 0 )
      {
        n1 = 0;
      }
      else
      {
        sqlite3BtreeKeySize( pCur, ref n1 );
      }
      sqlite3BtreeLeave( pCur.pBtree );
      sqlite3_snprintf( 30, ref zBuf, "%d", (int)( n1 + n2 ) );
      TCL.Tcl_AppendResult( interp, zBuf );
      return SQLITE_OK;
    }

    /*
    ** usage:   varint_test  START  MULTIPLIER  COUNT  INCREMENT
    **
    ** This command tests the putVarint() and getVarint()
    ** routines, both for accuracy and for speed.
    **
    ** An integer is written using putVarint() and read back with
    ** getVarint() and varified to be unchanged.  This repeats COUNT
    ** times.  The first integer is START*MULTIPLIER.  Each iteration
    ** increases the integer by INCREMENT.
    **
    ** This command returns nothing if it works.  It returns an error message
    ** if something goes wrong.
    */
    static int btree_varint_test(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that _invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      int start = 0, mult = 0, count = 0, incr = 0;
      int _in;
      u32 _out = 0;
      int n1, n2, i, j;
      byte[] zBuf = new byte[100];
      if ( argc != 5 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " START MULTIPLIER COUNT incrEMENT\"", 0 );
        return TCL.TCL_ERROR;
      }
      if ( TCL.Tcl_GetInt( interp, argv[1], ref start ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetInt( interp, argv[2], ref mult ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetInt( interp, argv[3], ref count ) ) return TCL.TCL_ERROR;
      if ( TCL.Tcl_GetInt( interp, argv[4], ref incr ) ) return TCL.TCL_ERROR;
      _in = start;
      _in *= mult;
      for ( i = 0 ; i < count ; i++ )
      {
        string zErr = "";//char zErr[200];
        n1 = putVarint( zBuf, 0, _in );
        if ( n1 > 9 || n1 < 1 )
        {
          sqlite3_snprintf( 100, ref zErr, "putVarint returned %d - should be between 1 and 9", n1 );
          TCL.Tcl_AppendResult( interp, zErr );
          return TCL.TCL_ERROR;
        }
        n2 = getVarint( zBuf, 0, ref _out );
        if ( n1 != n2 )
        {
          sqlite3_snprintf( 100, ref zErr, "putVarint returned %d and GetVar_int returned %d", n1, n2 );
          TCL.Tcl_AppendResult( interp, zErr );
          return TCL.TCL_ERROR;
        }
        if ( _in != (int)_out )
        {
          sqlite3_snprintf( 100, ref zErr, "Wrote 0x%016llx and got back 0x%016llx", _in, _out );
          TCL.Tcl_AppendResult( interp, zErr );
          return TCL.TCL_ERROR;
        }
        if ( ( _in & 0xffffffff ) == _in )
        {
          u32 _out32 = 0;
          n2 = getVarint32( zBuf, ref _out32 );
          _out = _out32;
          if ( n1 != n2 )
          {
            sqlite3_snprintf( 100, ref zErr, "putVarint returned %d and GetVar_int32 returned %d",
            n1, n2 );
            TCL.Tcl_AppendResult( interp, zErr );
            return TCL.TCL_ERROR;
          }
          if ( _in != (int)_out )
          {
            sqlite3_snprintf( 100, ref zErr, "Wrote 0x%016llx and got back 0x%016llx from GetVar_int32",
            _in, _out );
            TCL.Tcl_AppendResult( interp, zErr );
            return TCL.TCL_ERROR;
          }
        }

        /* _in order to get realistic tim_ings, run getVar_int 19 more times.
        ** This is because getVar_int is called ab_out 20 times more often
        ** than putVarint.
        */
        for ( j = 0 ; j < 19 ; j++ )
        {
          getVarint( zBuf, 0, ref _out );
        }
        _in += incr;
      }
      return TCL.TCL_OK;
    }

    /*
    ** usage:   btree_from_db  DB-HANDLE
    **
    ** This command returns the btree handle for the main database associated
    ** with the database-handle passed as the argument. Example usage:
    **
    ** sqlite3 db test.db
    ** set bt [btree_from_db db]
    */
    static int btree_from_db(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      string zBuf = "";//char zBuf[100];
      WrappedCommand info = null;
      sqlite3 db;
      Btree pBt;
      int iDb = 0;

      if ( argc != 2 && argc != 3 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0].ToString(),
        " DB-HANDLE ?N?\"" );
        return TCL.TCL_ERROR;
      }

      if ( TCL.Tcl_GetCommandInfo( interp, argv[1].ToString(), ref info ) )
      {
        TCL.Tcl_AppendResult( interp, "No such db-handle: \"", argv[1], "\"" );
        return TCL.TCL_ERROR;
      }
      if ( argc == 3 )
      {
        iDb = atoi( argv[2].ToString() );
      }

      db = ( (SqliteDb)info.objClientData ).db;
      Debug.Assert( db != null );

      pBt = db.aDb[iDb].pBt;
      sqlite3_snprintf( 50, ref zBuf, "->%p", pBt );
      if ( TCL.Tcl_CreateCommandPointer( interp, zBuf, pBt ) )
      {
        return TCL.TCL_ERROR;
      }
      else
        TCL.Tcl_SetResult( interp, zBuf, TCL.TCL_VOLATILE );
      return TCL.TCL_OK;
    }


    /*
    ** Usage:   btree_ismemdb ID
    **
    ** Return true if the B-Tree is in-memory.
    */
    static int btree_ismemdb(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      Btree pBt;
      int res;

      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0],
        " ID\"" );
        return TCL.TCL_ERROR;
      }
      pBt = (Btree)sqlite3TestTextToPtr( interp, argv[1].ToString() );
      sqlite3_mutex_enter( pBt.db.mutex );
      sqlite3BtreeEnter( pBt );
      res = sqlite3PagerIsMemdb( sqlite3BtreePager( pBt ) ) ? 1 : 0;
      sqlite3BtreeLeave( pBt );
      sqlite3_mutex_leave( pBt.db.mutex );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewBooleanObj( res ) );
      return TCL.TCL_OK;
    }

    /*
    ** usage:   btree_set_cache_size ID NCACHE
    **
    ** Set the size of the cache used by btree $ID.
    */
    //static int btree_set_cache_size(
    //  object NotUsed,
    // Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  int nCache;
    //  Btree pBt;

    //  if( argc!=3 ){
    //   TCL.Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0].ToString(),
    //       " BT NCACHE\"");
    //    return TCL.TCL_ERROR;
    //  }
    //  pBt = (Btree)sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  if(TCL.Tcl_GetInt(interp, argv[2], nCache) ) return TCL.TCL_ERROR;

    //  sqlite3_mutex_enter(pBt.db.mutex);
    //  sqlite3BtreeEnter(pBt);
    //  sqlite3BtreeSetCacheSize(pBt, nCache);
    //  sqlite3BtreeLeave(pBt);
    //  sqlite3_mutex_leave(pBt.db.mutex);

    //  return TCL.TCL_OK;
    //}


    /*
    ** Register commands with the TCL interpreter.
    */
    public class _aCmd
    {
      public string zName;
      public Tcl_CmdProc xProc;

      public _aCmd( string zName, Tcl_CmdProc xProc )
      {
        this.zName = zName;
        this.xProc = xProc;
      }
    }


    public static int Sqlitetest3_Init( Tcl_Interp interp )
    {
      _aCmd[] aCmd = new _aCmd[] {
new _aCmd( "btree_open",               (Tcl_CmdProc)btree_open               ),
new _aCmd( "btree_close",              (Tcl_CmdProc)btree_close              ),
new _aCmd( "btree_begin_transaction",  (Tcl_CmdProc)btree_begin_transaction  ),
new _aCmd( "btree_pager_stats",        (Tcl_CmdProc)btree_pager_stats        ),
new _aCmd( "btree_cursor",             (Tcl_CmdProc)btree_cursor             ),
new _aCmd( "btree_close_cursor",       (Tcl_CmdProc)btree_close_cursor       ),
new _aCmd( "btree_next",               (Tcl_CmdProc)btree_next               ),
//new _aCmd( "btree_eof",                (Tcl_CmdProc)btree_eof                ),
new _aCmd( "btree_payload_size",       (Tcl_CmdProc)btree_payload_size       ),
new _aCmd( "btree_first",              (Tcl_CmdProc)btree_first              ),
new _aCmd( "btree_varint_test",        (Tcl_CmdProc)btree_varint_test        ),
new _aCmd( "btree_from_db",            (Tcl_CmdProc)btree_from_db            ),
new _aCmd( "btree_ismemdb",        (Tcl_CmdProc)btree_ismemdb       ),
//new _aCmd( "btree_set_cache_size",     (Tcl_CmdProc)btree_set_cache_size     ),
};
      int i;

      for ( i = 0 ; i < aCmd.Length ; i++ )
      { //sizeof(aCmd)/sizeof(aCmd[0]); i++){
        TCL.Tcl_CreateCommand( interp, aCmd[i].zName, aCmd[i].xProc, null, null );
      }

      return TCL.TCL_OK;
    }
  }
#endif
}
