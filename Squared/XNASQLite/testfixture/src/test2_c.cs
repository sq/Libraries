using System;
using System.Diagnostics;
using System.Text;

using Bitmask = System.UInt64;
using i64 = System.Int64;
using u32 = System.UInt32;
using u64 = System.UInt64;
namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_CmdProc = tcl.lang.Interp.dxObjCmdProc;
  using DbPage = csSQLite.PgHdr;

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
    ** Code for testing the pager.c module in SQLite.  This code
    ** is not included in the SQLite library.  It is used for automated
    ** testing of the SQLite library.
    **
    ** $Id: test2.c,v 1.74 2009/07/24 19:01:20 drh Exp $
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
    //#include <ctype.h>

    ///*
    //** Interpret an SQLite error number
    //*/
    //static string errorName(int rc){
    //  string zName;
    //  switch( rc ){
    //    case SQLITE_OK:         zName = "SQLITE_OK";          break;
    //    case SQLITE_ERROR:      zName = "SQLITE_ERROR";       break;
    //    case SQLITE_PERM:       zName = "SQLITE_PERM";        break;
    //    case SQLITE_ABORT:      zName = "SQLITE_ABORT";       break;
    //    case SQLITE_BUSY:       zName = "SQLITE_BUSY";        break;
    //    case SQLITE_NOMEM:      zName = "SQLITE_NOMEM";       break;
    //    case SQLITE_READONLY:   zName = "SQLITE_READONLY";    break;
    //    case SQLITE_INTERRUPT:  zName = "SQLITE_INTERRUPT";   break;
    //    case SQLITE_IOERR:      zName = "SQLITE_IOERR";       break;
    //    case SQLITE_CORRUPT:    zName = "SQLITE_CORRUPT";     break;
    //    case SQLITE_FULL:       zName = "SQLITE_FULL";        break;
    //    case SQLITE_CANTOPEN:   zName = "SQLITE_CANTOPEN";    break;
    //    case SQLITE_PROTOCOL:   zName = "SQLITE_PROTOCOL";    break;
    //    case SQLITE_EMPTY:      zName = "SQLITE_EMPTY";       break;
    //    case SQLITE_SCHEMA:     zName = "SQLITE_SCHEMA";      break;
    //    case SQLITE_CONSTRAINT: zName = "SQLITE_CONSTRAINT";  break;
    //    case SQLITE_MISMATCH:   zName = "SQLITE_MISMATCH";    break;
    //    case SQLITE_MISUSE:     zName = "SQLITE_MISUSE";      break;
    //    case SQLITE_NOLFS:      zName = "SQLITE_NOLFS";       break;
    //    default:                zName = "SQLITE_Unknown";     break;
    //  }
    //  return zName;
    //}

    /*
    ** Page size and reserved size used for testing.
    */
    static int test_pagesize = 1024;

    /*
    ** Dummy page reinitializer
    */
    static void pager_test_reiniter( DbPage pNotUsed )
    {
      return;
    }
    
    /*
    ** Usage:   pager_open FILENAME N-PAGE
    **
    ** Open a new pager
    */
    //static int pager_open(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  u16 pageSize;
    //  Pager *pPager;
    //  Pgno nPage;
    //  int rc;
    //  char zBuf[100];
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " FILENAME N-PAGE\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  if( Tcl_GetInt(interp, argv[2], nPage) ) return TCL.TCL_ERROR;
    //  rc = sqlite3PagerOpen(sqlite3_vfs_find(0), pPager, argv[1], 0, 0,
    //      SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_MAIN_DB,
    // pager_test_reiniter);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  sqlite3PagerSetCachesize(pPager, nPage);
    //  pageSize = test_pagesize;
    //  sqlite3PagerSetPagesize(pPager, pageSize,-1);
    //  sqlite3_snprintf(100, ref zBuf,"%p",pPager);
    //  Tcl_AppendResult(interp, zBuf);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_close ID
    **
    ** Close the given pager.
    */
    //static int pager_close(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerClose(pPager);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_rollback ID
    **
    ** Rollback changes
    */
    //static int pager_rollback(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerRollback(pPager);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_commit ID
    **
    ** Commit all changes
    */
    //static int pager_commit(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerCommitPhaseOne(pPager,  0, 0);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  rc = sqlite3PagerCommitPhaseTwo(pPager);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_stmt_begin ID
    **
    ** Start a new checkpoint.
    */
    //static int pager_stmt_begin(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerOpenSavepoint(pPager, 1);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_stmt_rollback ID
    **
    ** Rollback changes to a checkpoint
    */
    //static int pager_stmt_rollback(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //rc = sqlite3PagerSavepoint(pPager, SAVEPOINT_ROLLBACK, 0);
    //sqlite3PagerSavepoint(pPager, SAVEPOINT_RELEASE, 0);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_stmt_commit ID
    **
    ** Commit changes to a checkpoint
    */
    //static int pager_stmt_commit(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int rc;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerSavepoint(pPager, SAVEPOINT_RELEASE, 0);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_stats ID
    **
    ** Return pager statistics.
    */
    //static int pager_stats(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int i, *a;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  a = sqlite3PagerStats(pPager);
    //  for(i=0; i<9; i++){
    //    static char *zName[] = {
    //      "ref", "page", "max", "size", "state", "err",
    //      "hit", "miss", "ovfl",
    //    };
    //    char zBuf[100];
    //    Tcl_AppendElement(interp, zName[i]);
    //    sqlite3_snprintf(100, ref zBuf,"%d",a[i]);
    //    Tcl_AppendElement(interp, zBuf);
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_pagecount ID
    **
    ** Return the size of the database file.
    */
    //static int pager_pagecount(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  char zBuf[100];
    //  Pgno nPage;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  sqlite3PagerPagecount(pPager, nPage);
    //  sqlite3_snprintf(100, ref zBuf, "%d", nPage);
    //  Tcl_AppendResult(interp, zBuf);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   page_get ID PGNO
    **
    ** Return a pointer to a page from the database.
    */
    //static int page_get(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  char zBuf[100];
    //  DbPage *pPage;
    //  int pgno;
    //  int rc;
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID PGNO\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  if( Tcl_GetInt(interp, argv[2], pgno) ) return TCL.TCL_ERROR;
    //rc = sqlite3PagerSharedLock(pPager);
    //if( rc==SQLITE_OK ){
    //  rc = sqlite3PagerGet(pPager, pgno, &pPage);
    //}
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  sqlite3_snprintf(100, ref zBuf,"%p",pPage);
    //  Tcl_AppendResult(interp, zBuf);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   page_lookup ID PGNO
    **
    ** Return a pointer to a page if the page is already in cache.
    ** If not in cache, return an empty string.
    */
    //static int page_lookup(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  char zBuf[100];
    //  DbPage *pPage;
    //  int pgno;
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID PGNO\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  if( Tcl_GetInt(interp, argv[2], pgno) ) return TCL.TCL_ERROR;
    //  pPage = sqlite3PagerLookup(pPager, pgno);
    //  if( pPage ){
    //    sqlite3_snprintf(100, ref zBuf,"%p",pPage);
    //    Tcl_AppendResult(interp, zBuf);
    //  }
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   pager_truncate ID PGNO
    */
    //static int pager_truncate(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  Pager *pPager;
    //  int pgno;
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " ID PGNO\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPager = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  if( Tcl_GetInt(interp, argv[2], pgno) ) return TCL.TCL_ERROR;
    //  sqlite3PagerTruncateImage(pPager, pgno);
    //  return TCL.TCL_OK;
    //}


    /*
    ** Usage:   page_unref PAGE
    **
    ** Drop a pointer to a page.
    */
    //static int page_unref(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  DbPage *pPage;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " PAGE\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPage = (DbPage *)sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  sqlite3PagerUnref(pPage);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   page_read PAGE
    **
    ** Return the content of a page
    */
    //static int page_read(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  char zBuf[100];
    //  DbPage *pPage;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " PAGE\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPage = sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  memcpy(zBuf, sqlite3PagerGetData(pPage), sizeof(zBuf));
    //  Tcl_AppendResult(interp, zBuf);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   page_number PAGE
    **
    ** Return the page number for a page.
    */
    //static int page_number(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  char zBuf[100];
    //  DbPage *pPage;
    //  if( argc!=2 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " PAGE\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPage = (DbPage *)sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  sqlite3_snprintf(100, ref zBuf, "%d", sqlite3PagerPagenumber(pPage));
    //  Tcl_AppendResult(interp, zBuf);
    //  return TCL.TCL_OK;
    //}

    /*
    ** Usage:   page_write PAGE DATA
    **
    ** Write something into a page.
    */
    //static int page_write(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  DbPage *pPage;
    //  char *pData;
    //  int rc;
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " PAGE DATA\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pPage = (DbPage *)sqlite3TestTextToPtr(interp,argv[1].ToString());
    //  rc = sqlite3PagerWrite(pPage);
    //  if( rc!=SQLITE_OK ){
    //    Tcl_AppendResult(interp, errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  pData = sqlite3PagerGetData(pPage);
    //  strncpy(pData, argv[2], test_pagesize-1);
    //  pData[test_pagesize-1] = 0;
    //  return TCL.TCL_OK;
    //}

#if !SQLITE_OMIT_DISKIO
    /*
** Usage:   fake_big_file  N  FILENAME
**
** Write a few bytes at the N megabyte point of FILENAME.  This will
** create a large file.  If the file was a valid SQLite database, then
** the next time the database is opened, SQLite will begin allocating
** new pages after N.  If N is 2096 or bigger, this will test the
** ability of SQLite to write to large files.
*/
    //static int fake_big_file(
    //  object NotUsed,
    //  Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    //  int argc,              /* Number of arguments */
    //  TclObject[] argv      /* Text of each argument */
    //){
    //  sqlite3_vfs *pVfs;
    //  sqlite3_file *fd = 0;
    //  int rc;
    //  int n;
    //  i64 offset;
    //  if( argc!=3 ){
    //    Tcl_AppendResult(interp, "wrong # args: should be \"", argv[0],
    //       " N-MEGABYTES FILE\"", 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  if( Tcl_GetInt(interp, argv[1], n) ) return TCL.TCL_ERROR;

    //  pVfs = sqlite3_vfs_find(0);
    //  rc = sqlite3OsOpenMalloc(pVfs, argv[2], fd,
    //      (SQLITE_OPEN_CREATE|SQLITE_OPEN_READWRITE|SQLITE_OPEN_MAIN_DB), 0
    //  );
    //  if( rc !=0){
    //    Tcl_AppendResult(interp, "open failed: ", errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  offset = n;
    //  offset *= 1024*1024;
    //  rc = sqlite3OsWrite(fd, "Hello, World!", 14, offset);
    //  sqlite3OsCloseFree(fd);
    //  if( rc !=0){
    //    Tcl_AppendResult(interp, "write failed: ", errorName(rc), 0);
    //    return TCL.TCL_ERROR;
    //  }
    //  return TCL.TCL_OK;
    //}
#endif


    /*
** test_control_pending_byte  PENDING_BYTE
**
** Set the PENDING_BYTE using the sqlite3_test_control() interface.
*/
    static int testPendingByte(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,              /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      int pbyte = 0;
      int rc;
      if ( argc != 2 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0],
        " PENDING-BYTE\"" );
      }
      if ( TCL.Tcl_GetInt( interp, argv[1], ref pbyte ) ) return TCL.TCL_ERROR;
      rc = sqlite3_test_control( SQLITE_TESTCTRL_PENDING_BYTE, pbyte );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return TCL.TCL_OK;
    }

    /*
    ** sqlite3BitvecBuiltinTest SIZE PROGRAM
    **
    ** Invoke the SQLITE_TESTCTRL_BITVEC_TEST operator on test_control.
    ** See comments on sqlite3BitvecBuiltinTest() for additional information.
    */
    static int testBitvecBuiltinTest(
    object NotUsed,
    Tcl_Interp interp,    /* The TCL interpreter that invoked this command */
    int argc,             /* Number of arguments */
    TclObject[] argv      /* Text of each argument */
    )
    {
      int sz = 0, rc;
      int nProg = 0;
      int[] aProg = new int[100];
      string z;
      if ( argc != 3 )
      {
        TCL.Tcl_AppendResult( interp, "wrong # args: should be \"", argv[0],
        " SIZE PROGRAM\"" );
      }
      if ( TCL.Tcl_GetInt( interp, argv[1], ref sz ) ) return TCL.TCL_ERROR;
      z = argv[2].ToString() + '\0';
      int iz = 0;
      while ( nProg < 99 && z[iz] != 0 )
      {
        while ( z[iz] != 0 && !sqlite3Isdigit( z[iz] ) ) { iz++; }
        if ( z[iz] == 0 ) break;
        while ( sqlite3Isdigit( z[iz] ) ) { aProg[nProg] = aProg[nProg] * 10 + ( z[iz] - 48 ); iz++; }
        nProg++;
      }
      aProg[nProg] = 0;
      rc = sqlite3_test_control( SQLITE_TESTCTRL_BITVEC_TEST, sz, aProg );
      TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( rc ) );
      return TCL.TCL_OK;
    }

    static Var.SQLITE3_GETSET sqlite3_io_error_persist = new Var.SQLITE3_GETSET( "sqlite3_io_error_persist" );
    static Var.SQLITE3_GETSET sqlite3_io_error_pending = new Var.SQLITE3_GETSET( "sqlite3_io_error_pending" );
    static Var.SQLITE3_GETSET sqlite3_io_error_hit = new Var.SQLITE3_GETSET( "sqlite3_io_error_hit" );
    static Var.SQLITE3_GETSET sqlite3_io_error_hardhit = new Var.SQLITE3_GETSET( "sqlite3_io_error_hardhit" );
    static Var.SQLITE3_GETSET sqlite3_diskfull_pending = new Var.SQLITE3_GETSET( "sqlite3_diskfull_pending" );
    static Var.SQLITE3_GETSET sqlite3_diskfull = new Var.SQLITE3_GETSET( "sqlite3_diskfull" );

    /*
    ** Register commands with the TCL interpreter.
    */
    static Var.SQLITE3_GETSET TCLsqlite3PendingByte = new Var.SQLITE3_GETSET( "sqlite_pending_byte" );


    public static int Sqlitetest2_Init( Tcl_Interp interp )
    {
      //extern int sqlite3_io_error_persist;
      //extern int sqlite3_io_error_pending;
      //extern int sqlite3_io_error_hit;
      //extern int sqlite3_io_error_hardhit;
      //extern int sqlite3_diskfull_pending;
      //extern int sqlite3_diskfull;
      //extern int sqlite3_pager_n_sort_bucket;
      //static struct {
      //  char *zName;
      //  Tcl_CmdProc *xProc;
      //} aCmd[] = {
      _aCmd[] aCmd = new _aCmd[] {
//new _aCmd( "pager_open",              (Tcl_CmdProc)pager_open          ),
//new _aCmd( "pager_close",             (Tcl_CmdProc)pager_close         ),
//    { "pager_commit",            (Tcl_CmdProc*)pager_commit        },
//    { "pager_rollback",          (Tcl_CmdProc*)pager_rollback      },
//    { "pager_stmt_begin",        (Tcl_CmdProc*)pager_stmt_begin    },
//    { "pager_stmt_commit",       (Tcl_CmdProc*)pager_stmt_commit   },
//    { "pager_stmt_rollback",     (Tcl_CmdProc*)pager_stmt_rollback },
//    { "pager_stats",             (Tcl_CmdProc*)pager_stats         },
//    { "pager_pagecount",         (Tcl_CmdProc*)pager_pagecount     },
//    { "page_get",                (Tcl_CmdProc*)page_get            },
//    { "page_lookup",             (Tcl_CmdProc*)page_lookup         },
//    { "page_unref",              (Tcl_CmdProc*)page_unref          },
//    { "page_read",               (Tcl_CmdProc*)page_read           },
//    { "page_write",              (Tcl_CmdProc*)page_write          },
//    { "page_number",             (Tcl_CmdProc*)page_number         },
//    { "pager_truncate",          (Tcl_CmdProc*)pager_truncate      },
#if !SQLITE_OMIT_DISKIO
//    { "fake_big_file",           (Tcl_CmdProc*)fake_big_file       },
#endif
new _aCmd( "sqlite3BitvecBuiltinTest",(Tcl_CmdProc)testBitvecBuiltinTest),
new _aCmd( "sqlite3_test_control_pending_byte",(Tcl_CmdProc)testPendingByte),
};
      int i;
      for ( i = 0 ; i < aCmd.Length ; i++ )
      {//sizeof(aCmd)/sizeof(aCmd[0]); i++){
        TCL.Tcl_CreateCommand( interp, aCmd[i].zName, aCmd[i].xProc, null, null );
      }
      TCL.Tcl_LinkVar( interp, "sqlite_io_error_pending",
      sqlite3_io_error_pending, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_io_error_persist",
      sqlite3_io_error_persist, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_io_error_hit",
      sqlite3_io_error_hit, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_io_error_hardhit",
      sqlite3_io_error_hardhit, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_diskfull_pending",
      sqlite3_diskfull_pending, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_diskfull",
      sqlite3_diskfull, VarFlags.SQLITE3_LINK_INT );
      TCL.Tcl_LinkVar( interp, "sqlite_pending_byte",
      TCLsqlite3PendingByte, VarFlags.SQLITE3_LINK_INT );
      TCLsqlite3PendingByte.iValue = sqlite3PendingByte;
      return TCL.TCL_OK;
    }
  }
#endif
}
