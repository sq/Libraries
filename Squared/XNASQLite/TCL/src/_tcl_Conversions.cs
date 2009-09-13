using System;
using System.Diagnostics;
using System.Text;

using sqlite_int64 = System.Int64;
using i32 = System.Int32;
using i64 = System.Int64;
using u32 = System.UInt32;

namespace tcl.lang
{
#if !NO_TCL
  using lang;
  using Tcl_Channel = Channel;
  using Tcl_DString = TclString;
  using Tcl_Interp = Interp;
  using Tcl_Obj = TclObject;
  using Tcl_WideInt = System.Int64;

  public partial class TCL
  {

    // -- Conversion from TCL to tclsharp coding
    // Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
    // $Header$
    public static void Tcl_AppendElement( Interp interp, string toAppend )
    {
      interp.appendElement( toAppend );
    }

    public static void Tcl_AppendResult( Interp interp, params object[] tos )
    {
      if ( tos != null )
      {
        StringBuilder result = new StringBuilder( 100 );
        for ( int i = 0 ; i < tos.Length && tos[i] != null ; i++ ) result.Append( tos[i].ToString() );
        interp.appendElement( result.ToString() );
      }
    }

    public static void Tcl_AppendResult( Interp interp, params string[] strings )
    {
      if ( strings != null )
      {
        StringBuilder result = new StringBuilder( 100 );
        for ( int i = 0 ; i < strings.Length && strings[i] != null && strings[i] != "" ; i++ ) result.Append( strings[i] );
        interp.appendElement( result.ToString() );
      }
    }

    public static void Tcl_BackgroundError( Interp interp )
    {
      interp.setErrorCode( TclInteger.newInstance( TCL_ERROR ) );
      interp.addErrorInfo( "Background Error" );
    }

    public static void Tcl_CreateCommand( Interp interp, string cmdName, Interp.dxObjCmdProc ObjCmdProc, object ClientData, Interp.dxCmdDeleteProc DbDeleteCmd )
    {
      interp.createObjCommand( cmdName, ObjCmdProc, ClientData, DbDeleteCmd );
    }

    public static void Tcl_CreateObjCommand( Interp interp, string cmdName, Interp.dxObjCmdProc ObjCmdProc, object ClientData, Interp.dxCmdDeleteProc DbDeleteCmd )
    {
      interp.createObjCommand( cmdName, ObjCmdProc, ClientData, DbDeleteCmd );
    }


    public static bool Tcl_CreateCommandPointer( Interp interp, string command, object clientData )
    {
      try { interp.createObjCommand( command, null, clientData, null ); return false; }
      catch { return true; }
    }

    public static void Tcl_DecrRefCount( ref TclObject to )
    {
      to.release();
      if ( to.internalRep == null ) to = null;
    }

    public static int Tcl_DeleteCommand( Interp interp, string cmdName )
    {
      return interp.deleteCommand( cmdName );
    }

    public static void Tcl_DStringAppendElement( TclObject str, string append )
    {
      TclString.append( str, append );
    }

    public static void Tcl_DStringFree( ref TclObject str )
    {
      str.release();
    }

    public static void Tcl_DStringInit( ref   TclObject str )
    {
      str = TclString.newInstance( "" );
      str.preserve();
    }

    public static int Tcl_DStringLength( TclObject str )
    { return str.ToString().Length; }

    public static TclObject Tcl_DuplicateObj( TclObject to )
    { return to.duplicate(); }

    public static int Tcl_Eval( Interp interp, string s )
    {
      try { interp.eval( s ); return 0; }
      catch { return 1; };
    }
    public static int Tcl_EvalObjEx( Interp interp, TclObject tobj, int flags )
    {
      try { interp.eval( tobj, flags ); return 0; }
      catch ( TclException e )
      {
        if ( e.getCompletionCode() == TCL.CompletionCode.RETURN ) return TCL_RETURN;
        else if ( e.getCompletionCode() == TCL.CompletionCode.BREAK || interp.getResult().ToString() == "invoked \"break\" outside of a loop" ) return TCL_BREAK;
        else return TCL_ERROR;
      };
    }

    public static void Tcl_Free( ref TclObject[] to )
    {
      if ( to != null ) for ( int i = 0 ; i < to.Length ; i++ ) while ( to[i] != null && to[i].refCount > 0 ) to[i].release();
      to = null;
    }

    public static void Tcl_Free( ref TclObject to )
    {
      while ( to.refCount > 0 ) to.release();
    }

    public static void Tcl_Free<T>( ref T x ) where T : class
    {
      x = null;
    }

    public static bool Tcl_GetBoolean( Interp interp, TclObject to, ref int result )
    {
      try { result = ( TclBoolean.get( interp, to ) ? 1 : 0 ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetBoolean( Interp interp, TclObject to, ref bool result )
    {
      try { result = TclBoolean.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetBooleanFromObj( Interp interp, TclObject to, ref bool result )
    {
      try { result = TclBoolean.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetCommandInfo( Interp interp, string command, ref WrappedCommand value )
    {
      try { value = interp.getObjCommand( command ); return false; }
      catch { return true; }
    }

    public static byte[] Tcl_GetByteArrayFromObj( TclObject to, ref int n )
    {
      return Encoding.UTF8.GetBytes( to.ToString() );
    }

    public static bool Tcl_GetDouble( Interp interp, TclObject to, ref double value )
    {
      try { value = TclDouble.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetDoubleFromObj( Interp interp, TclObject to, ref double value )
    {
      try
      {
        if ( to.ToString() == "NaN" ) value = Double.NaN;
        else value = TclDouble.get( interp, to );
        return false;
      }
      catch { return true; }
    }

    public static bool Tcl_GetIndexFromObj( Interp interp, TclObject to, string[] table, string msg, int flags, ref int index )
    {
      try { index = TclIndex.get( interp, to, table, msg, flags ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetInt( Interp interp, TclObject to, ref int value )
    {
      try { value = TclInteger.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetInt( Interp interp, TclObject to, ref u32 value )
    {
      try { value = (u32)TclInteger.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetIntFromObj( Interp interp, TclObject to, ref int value )
    {
      try { value = TclInteger.get( interp, to ); return false; }
      catch { return true; }
    }

    public static bool Tcl_GetLong( Interp interp, TclObject to, ref i64 value )
    {
      try { value = (i64)TclLong.get( interp, to ); return false; }
      catch { return true; }
    }

    public static TclObject Tcl_GetObjResult( Interp interp )
    {
      TclObject toReturn = interp.getResult();
      return toReturn;
    }

    public static string Tcl_GetString( TclObject to )
    {
      return to.ToString();
    }

    public static string Tcl_GetStringFromObj( TclObject to, int n )
    {
      Debug.Assert( n == 0, "Try calling by ref" );
      return to.ToString();
    }

    public static string Tcl_GetStringFromObj( TclObject to, ref int n )
    {
      string ts = System.Text.Encoding.UTF8.GetString( System.Text.Encoding.UTF8.GetBytes( to.ToString() ) );
      n = ts.Length;
      return ts;
    }

    public static string Tcl_GetStringResult( Interp interp )
    {
      return interp.getResult().ToString();
    }

    public static TclObject Tcl_GetVar2Ex( Interp interp, string part1, string part2, VarFlag flags )
    {
      try
      {
        Var[] result = Var.lookupVar( interp, part1, part2, flags, "read", false, true );
        if ( result == null )
        {
          // lookupVar() returns null only if VarFlag.LEAVE_ERR_MSG is
          // not part of the flags argument, return null in this case.

          return null;
        }

        Var var = result[0];
        Var array = result[1];
        TclObject to = null;

        if ( var.isVarScalar() && !var.isVarUndefined() )
        {
          to = (TclObject)var.value;
          double D = 0;
          if ( !Double.TryParse( to.ToString(), out D ) ) { if ( String.IsNullOrEmpty(to.typePtr )) to.typePtr = "string"; }
          else if ( to.ToString().Contains( "." ) ) to.typePtr = "double";
          else to.typePtr = "int";

          return to;
        }
        if ( var.isSQLITE3_Link() )
        {
          to = (TclObject)var.sqlite3_get();
        }
        return to;
      }
      catch { return null; };
    }

    public static TclObject Tcl_GetVarType( Interp interp, string part1, string part2, VarFlag flags )
    {
      try
      {
        TclObject to = interp.getVar( part1, part2, flags );
        return to;
      }
      catch { return null; };
    }

    public static bool Tcl_GetWideIntFromObj( Interp interp, TclObject to, ref sqlite_int64 value )
    {
      try
      {
        if ( to.ToString() == "NaN" )
          unchecked { value = (long)Double.NaN; }
        else value = TclLong.get( interp, to );
        return false;
      }
      catch { return true; };
    }

    public static void Tcl_IncrRefCount( TclObject to )
    {
      to.preserve();
    }

    public static void Tcl_LinkVar( Interp interp, string name, Object GetSet, VarFlags flags )
    {
      Debug.Assert( ( ( flags & VarFlags.SQLITE3_LINK_READ_ONLY ) != 0 ) || GetSet.GetType().Name == "SQLITE3_GETSET" );
      Var[] linkvar = Var.lookupVar( interp, name, null, VarFlag.GLOBAL_ONLY, "define", true, false );
      linkvar[0].flags |= VarFlags.SQLITE3_LINK | flags;
      linkvar[0].sqlite3_get_set = GetSet;
      linkvar[0].refCount++;
    }

    public static bool Tcl_ListObjAppendElement( Interp interp, TclObject to, TclObject elemObj )
    {
      try
      {
        TclList.append( interp, to, elemObj );
        return false;
      }
      catch { return true; }
    }

    public static bool Tcl_ListObjGetElements( Interp interp, TclObject to, ref int nArg, ref Tcl_Obj[] aArg )
    {
      try
      {
        aArg = TclList.getElements( interp, to );
        nArg = aArg.Length;
        return false;
      }
      catch { return true; }
    }

    public static TclObject Tcl_NewBooleanObj( int value )
    {
      return TclBoolean.newInstance( value != 0 );
    }

    public static TclObject Tcl_NewByteArrayObj( byte[] value, int bytes )
    {
      if ( value == null || value.Length == 0 || bytes == 0 ) return TclByteArray.newInstance();
      else return TclByteArray.newInstance( value, 0, bytes );
    }

    public static TclObject Tcl_NewByteArrayObj( string value, int bytes )
    {
      if ( value == null || bytes == 0 ) return TclByteArray.newInstance();
      else return TclByteArray.newInstance( System.Text.Encoding.UTF8.GetBytes( value.Substring( 0, bytes ) ) );
    }

    public static TclObject Tcl_NewDoubleObj( double value )
    {
      return TclDouble.newInstance( value );
    }

    public static TclObject Tcl_NewIntObj( int value )
    {
      return TclInteger.newInstance( value );
    }

    public static TclObject Tcl_NewListObj( int nArg, TclObject[] aArg )
    {
      TclObject to = TclList.newInstance();
      for ( int i = 0 ; i < nArg ; i++ ) TclList.append( null, to, aArg[i] );
      return to;
    }

    public static TclObject Tcl_NewObj()
    {
      return TclString.newInstance( "" );
    }

    public static TclObject Tcl_NewStringObj( byte[] value, int iLength )
    {
      if ( iLength > 0 && iLength < value.Length )
        return TclString.newInstance( Encoding.UTF8.GetString( value, 0, iLength ) );
      else
        return TclString.newInstance( Encoding.UTF8.GetString( value ) );
    }

    public static TclObject Tcl_NewStringObj( string value, int iLength )
    {
      if ( value == null ) value = "";
      else value = value.Split( '\0' )[0];
      if ( iLength <= 0 ) iLength = value.Length;
      return TclString.newInstance( value.Substring( 0, iLength ) );
    }

    public static TclObject Tcl_NewWideIntObj( long value )
    {
      return TclLong.newInstance( value );
    }

    public static bool Tcl_ObjSetVar2( Interp interp, TclObject toName, TclObject part2, TclObject toValue, VarFlag flags )
    {
      try
      {
        if ( part2 == null ) interp.setVar( toName, toValue, flags );
        else
          interp.setVar( toName.ToString(), part2.ToString(), toValue.ToString(), flags );
        return false;
      }
      catch { return true; }
    }
    public static void Tcl_PkgProvide( Interp interp, string name, string version )
    {
      interp.pkgProvide( name, version );
    }

    public static void Tcl_ResetResult( Interp interp )
    {
      interp.resetResult();
    }

    public static void Tcl_SetBooleanObj( TclObject to, int result )
    {
      to.stringRep = TclBoolean.newInstance( result != 0 ).ToString();
      to.preserve();
    }

    public static bool Tcl_SetCommandInfo( Interp interp, string command, WrappedCommand value )
    {
      try { value = interp.getObjCommand( command ); return false; }
      catch { return true; }
    }

    public static void Tcl_SetIntObj( TclObject to, int result
      )
    {
      while ( to.Shared ) to.release();
      TclInteger.set( to, result );
      to.preserve();
    }

    public static void Tcl_SetLongObj( TclObject to, long result )
    {
      while ( to.Shared ) to.release();
      TclLong.set( to, result );
      to.preserve();
    }

    public static void Tcl_SetObjResult( Interp interp, TclObject to )
    {
      interp.resetResult();
      interp.setResult( to );
    }

    public static void Tcl_SetResult( Interp interp, string result, int dummy )
    {
      interp.resetResult();
      interp.setResult( result );
    }

    public static void Tcl_SetVar2( Interp interp, string part1, string part2, string value, int flags )
    {
      interp.setVar( part1, part2, value, (VarFlag)flags );
    }

    public static void Tcl_SetVar2( Interp interp, string part1, string part2, TclObject value, int flags )
    {
      interp.setVar( part1, part2, value, (VarFlag)flags );
    }

    public static void Tcl_UnregisterChannel( Interp interp, Channel chan )
    {
      TclIO.unregisterChannel( interp, chan );
    }

    public static int Tcl_VarEval( Interp interp, string Scriptname, params string[] argv )
    {
      try
      {
        //Tcl_Obj[] aArg = null;
        int rc = 0;
        Tcl_Obj pCmd = Tcl_NewStringObj( Scriptname, -1 );
        Tcl_IncrRefCount( pCmd );
        for ( int i = 0 ; i < argv.Length ; i++ )
        {
          if ( argv[i] != null && argv[i] != " " ) rc = Tcl_ListObjAppendElement( interp, pCmd, Tcl_NewStringObj( argv[i], -1 ) ) ? 1 : 0;
          if ( rc != 0 )
          {
            Tcl_DecrRefCount( ref pCmd );
            return 1;
          }
        }
        rc = Tcl_EvalObjEx( interp, pCmd, TCL_EVAL_DIRECT );
        Tcl_DecrRefCount( ref pCmd );
        return rc == TCL_BREAK ? 1 : 0;
      }
      catch { return 1; }
    }

    public static void Tcl_WrongNumArgs( Interp interp, int argc, TclObject[] argv, string message )
    {
      throw new TclNumArgsException( interp, argc, argv, message  == null ? "option ?arg ...?" : message );
    }

  }
#endif
}
