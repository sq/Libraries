using System;
using System.Diagnostics;
using System.Text;

using Bitmask = System.UInt64;
using i64 = System.Int64;
using u32 = System.UInt32;
using u64 = System.UInt64;

using ClientData = System.Object;

namespace CS_SQLite3
{
#if !NO_TCL
  using tcl.lang;
  using Tcl_Interp = tcl.lang.Interp;
  using Tcl_Obj = tcl.lang.TclObject;
  using Tcl_CmdInfo = tcl.lang.Command;
  using Tcl_DString = tcl.lang.TclString;

  using sqlite3_int64 = System.Int64;
  using sqlite3_u3264 = System.UInt64;
  using sqlite3_stmt = csSQLite.Vdbe;
  using sqlite3_value = csSQLite.MemRef;

  public partial class csSQLite
  {
    /*
    ** 2009 January 28
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
    ** $Id: test_backup.c,v 1.3 2009/03/30 12:56:52 drh Exp $
    **
    *************************************************************************
    **  Included in SQLite3 port to C#-SQLite;  2008 Noah B Hart
    **  C#-SQLite is an independent reimplementation of the SQLite software library
    **
    **  $Header$
    *************************************************************************
    */

    //#include "tcl.h"
    //#include <sqlite3.h>
    //#include <assert.h>

    /* These functions are implemented in test1.c. */
    //int getDbPointer(Tcl_Interp *, const char *, sqlite3 **);
    //const char *sqlite3TestErrorName(int);

    enum BackupSubCommandEnum
    {
      BACKUP_STEP, BACKUP_FINISH, BACKUP_REMAINING, BACKUP_PAGECOUNT
    };

    struct BackupSubCommand
    {
      public string zCmd;
      public BackupSubCommandEnum eCmd;
      public int nArg;
      public string zArg;

      public BackupSubCommand( string zCmd, BackupSubCommandEnum eCmd, int nArg, string zArg )
      {
        this.zCmd = zCmd;
        this.eCmd = eCmd;
        this.nArg = nArg;
        this.zArg = zArg;
      }
    }

    static int Tcl_GetIndexFromObjStruct( Interp interp, TclObject to, BackupSubCommand[] table, int len, string msg, int flags, ref int index )
    {
      string zCmd = to.ToString();
      for ( index = 0 ; index < len ; index++ )
      {
        if ( zCmd == table[index].zCmd ) return 0;
      }
      return 1;
    }

    static int backupTestCmd(
    ClientData clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      BackupSubCommand[] aSub = new BackupSubCommand[] {
new BackupSubCommand("step",      BackupSubCommandEnum.BACKUP_STEP      , 1, "npage" ),
new BackupSubCommand("finish",    BackupSubCommandEnum.BACKUP_FINISH    , 0, ""      ),
new BackupSubCommand("remaining", BackupSubCommandEnum.BACKUP_REMAINING , 0, ""      ),
new BackupSubCommand("pagecount", BackupSubCommandEnum.BACKUP_PAGECOUNT , 0, ""      ),
new BackupSubCommand(null,0,0,null)
};

      sqlite3_backup p = (sqlite3_backup)clientData;
      int iCmd = 0;
      int rc;

      rc = Tcl_GetIndexFromObjStruct(
      interp, objv[1], aSub, aSub.Length, "option", 0, ref iCmd
      );
      if ( rc != TCL.TCL_OK )
      {
        return rc;
      }
      if ( objc != ( 2 + aSub[iCmd].nArg ) )
      {
        TCL.Tcl_WrongNumArgs( interp, 2, objv, aSub[iCmd].zArg );
        return TCL.TCL_ERROR;
      }

      switch ( aSub[iCmd].eCmd )
      {

        case BackupSubCommandEnum.BACKUP_FINISH:
          {
            string zCmdName;
            WrappedCommand cmdInfo = null;
            zCmdName = TCL.Tcl_GetString( objv[0] );
            TCL.Tcl_GetCommandInfo( interp, zCmdName, ref cmdInfo );
            cmdInfo.deleteProc = null;
            TCL.Tcl_SetCommandInfo( interp, zCmdName, cmdInfo );
            TCL.Tcl_DeleteCommand( interp, zCmdName );

            rc = sqlite3_backup_finish( p );
            TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_STATIC );
            break;
          }

        case BackupSubCommandEnum.BACKUP_STEP:
          {
            int nPage = 0;
            if ( TCL.Tcl_GetIntFromObj( interp, objv[2], ref nPage ) )
            {
              return TCL.TCL_ERROR;
            }
            rc = sqlite3_backup_step( p, nPage );
            TCL.Tcl_SetResult( interp, sqlite3TestErrorName( rc ), TCL.TCL_STATIC );
            break;
          }

        case BackupSubCommandEnum.BACKUP_REMAINING:
          TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( sqlite3_backup_remaining( p ) ) );
          break;

        case BackupSubCommandEnum.BACKUP_PAGECOUNT:
          TCL.Tcl_SetObjResult( interp, TCL.Tcl_NewIntObj( sqlite3_backup_pagecount( p ) ) );
          break;
      }

      return TCL.TCL_OK;
    }

    static void backupTestFinish( ref ClientData clientData )
    {
      sqlite3_backup pBackup = (sqlite3_backup)clientData;
      sqlite3_backup_finish( pBackup );
    }

    /*
    **     sqlite3_backup CMDNAME DESTHANDLE DESTNAME SRCHANDLE SRCNAME
    **
    */
    static int backupTestInit(
    ClientData clientData,
    Tcl_Interp interp,
    int objc,
    Tcl_Obj[] objv
    )
    {
      sqlite3_backup pBackup;
      sqlite3 pDestDb = null;
      sqlite3 pSrcDb = null;
      string zDestName;
      string zSrcName;
      string zCmd;

      if ( objc != 6 )
      {
        TCL.Tcl_WrongNumArgs(
        interp, 1, objv, "CMDNAME DESTHANDLE DESTNAME SRCHANDLE SRCNAME"
        );
        return TCL.TCL_ERROR;
      }

      zCmd = TCL.Tcl_GetString( objv[1] );
      getDbPointer( interp, TCL.Tcl_GetString( objv[2] ), ref pDestDb );
      zDestName = TCL.Tcl_GetString( objv[3] );
      getDbPointer( interp, TCL.Tcl_GetString( objv[4] ), ref pSrcDb );
      zSrcName = TCL.Tcl_GetString( objv[5] );

      pBackup = sqlite3_backup_init( pDestDb, zDestName, pSrcDb, zSrcName );
      if ( null == pBackup )
      {
        TCL.Tcl_AppendResult( interp, "sqlite3_backup_init() failed" );
        return TCL.TCL_ERROR;
      }

      TCL.Tcl_CreateObjCommand( interp, zCmd, (Interp.dxObjCmdProc)backupTestCmd, pBackup, (Interp.dxCmdDeleteProc)backupTestFinish );
      TCL.Tcl_SetObjResult( interp, objv[1] );
      return TCL.TCL_OK;
    }

    public static int Sqlitetestbackup_Init( Tcl_Interp interp )
    {
      TCL.Tcl_CreateObjCommand( interp, "sqlite3_backup", backupTestInit, 0, null );
      return TCL.TCL_OK;
    }
  }
#endif
}
