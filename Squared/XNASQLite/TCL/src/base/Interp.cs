#undef DEBUG
/*
* Interp.java --
*
*	Implements the core Tcl interpreter.
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997-1998 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Interp.java,v 1.44 2003/07/25 16:38:35 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{

  /// <summary> The Tcl interpreter class.</summary>

  public class Interp : EventuallyFreed
  {
    private void InitBlock()
    {
      reflectObjTable = new Hashtable();
      reflectConflictTable = new Hashtable();
      importTable = new Hashtable[] { new Hashtable(), new Hashtable() };
    }
    /// <summary> Returns the name of the script file currently under execution.
    /// 
    /// </summary>
    /// <returns> the name of the script file currently under execution.
    /// </returns>
    internal string ScriptFile
    {
      get
      {
        return dbg.fileName;
      }

    }

    // The following three variables are used to maintain a translation
    // table between ReflectObject's and their string names. These
    // variables are accessed by the ReflectObject class, they
    // are defined here be cause we need them to be per interp data.

    // Translates Object to ReflectObject. This makes sure we have only
    // one ReflectObject internalRep for the same Object -- this
    // way Object identity can be done by string comparison.

    internal Hashtable reflectObjTable;

    // Number of reflect objects created so far inside this Interp
    // (including those that have be freed)

    internal long reflectObjCount = 0;

    // Table used to store reflect hash index conflicts, see
    // ReflectObject implementation for more details

    internal Hashtable reflectConflictTable;

    // The number of chars to copy from an offending command into error
    // message.

    private const int MAX_ERR_LENGTH = 200;


    // We pretend this is Tcl 8.0, patch level 0.

    internal const string TCL_VERSION = "8.0";
    internal const string TCL_PATCH_LEVEL = "8.0";


    // Total number of times a command procedure
    // has been called for this interpreter.

    protected internal int cmdCount;

    // FIXME : remove later
    // Table of commands for this interpreter.
    //Hashtable cmdTable;

    // Table of channels currently registered in this interp.

    internal Hashtable interpChanTable;

    // The Notifier associated with this Interp.

    private Notifier notifier;

    // Hash table for associating data with this interpreter. Cleaned up
    // when this interpreter is deleted.

    internal Hashtable assocData;

    // Current working directory.

    private System.IO.FileInfo workingDir;

    // Points to top-most in stack of all nested procedure
    // invocations.  null means there are no active procedures.

    internal CallFrame frame;

    // Points to the call frame whose variables are currently in use
    // (same as frame unless an "uplevel" command is being
    // executed).  null means no procedure is active or "uplevel 0" is
    // being exec'ed.

    internal CallFrame varFrame;

    // The interpreter's global namespace.

    internal NamespaceCmd.Namespace globalNs;

    // Hash table used to keep track of hidden commands on a per-interp basis.

    internal Hashtable hiddenCmdTable;

    // Information used by InterpCmd.java to keep
    // track of master/slave interps on a per-interp basis.

    // Keeps track of all interps for which this interp is the Master.
    // First, slaveTable (a hashtable) maps from names of commands to
    // slave interpreters. This hashtable is used to store information
    // about slave interpreters of this interpreter, to map over all slaves, etc.

    internal Hashtable slaveTable;

    // Hash table for Target Records. Contains all Target records which denote
    // aliases from slaves or sibling interpreters that direct to commands in
    // this interpreter. This table is used to remove dangling pointers
    // from the slave (or sibling) interpreters when this interpreter is deleted.

    internal Hashtable targetTable;

    // Information necessary for this interp to function as a slave.
    internal InterpSlaveCmd slave;

    // Table which maps from names of commands in slave interpreter to
    // InterpAliasCmd objects.

    internal Hashtable aliasTable;

    // FIXME : does globalFrame need to be replaced by globalNs?
    // Points to the global variable frame.

    //CallFrame globalFrame;

    // The script file currently under execution. Can be null if the
    // interpreter is not evaluating any script file.

    internal string scriptFile;

    // Number of times the interp.eval() routine has been recursively
    // invoked.

    internal int nestLevel;

    // Used to catch infinite loops in Parser.eval2.

    internal int maxNestingDepth;

    // Flags used when evaluating a command.

    internal int evalFlags;

    // Flags used when evaluating a command.

    public int flags;

    // Is this interpreted marked as safe?

    internal bool isSafe;

    // Offset of character just after last one compiled or executed
    // by Parser.eval2().

    internal int termOffset;

    // List of name resolution schemes added to this interpreter.
    // Schemes are added/removed by calling addInterpResolver and
    // removeInterpResolver.

    internal ArrayList resolvers;

    // The expression parser for this interp.

    internal Expression expr;

    // Used by the Expression class.  If it is equal to zero, then the 
    // parser will evaluate commands and retrieve variable values from 
    // the interp.

    internal int noEval;

    // Used in the Expression.java file for the 
    // SrandFunction.class and RandFunction.class.
    // Set to true if a seed has been set.

    internal bool randSeedInit;

    // Used in the Expression.java file for the SrandFunction.class and
    // RandFunction.class.  Stores the value of the seed.

    internal long randSeed;

    // If returnCode is TCL.CompletionCode.ERROR, stores the errorInfo.

    internal string errorInfo;

    // If returnCode is TCL.CompletionCode.ERROR, stores the errorCode.

    internal string errorCode;

    // Completion code to return if current procedure exits with a
    // TCL_RETURN code.

    protected internal TCL.CompletionCode returnCode;

    // True means the interpreter has been deleted: don't process any
    // more commands for it, and destroy the structure as soon as all
    // nested invocations of eval() are done.

    protected internal bool deleted;

    // True means an error unwind is already in progress. False
    // means a command proc has been invoked since last error occurred.

    protected internal bool errInProgress;

    // True means information has already been logged in $errorInfo
    // for the current eval() instance, so eval() needn't log it
    // (used to implement the "error" command).

    protected internal bool errAlreadyLogged;

    // True means that addErrorInfo has been called to record
    // information for the current error. False means Interp.eval
    // must clear the errorCode variable if an error is returned.

    protected internal bool errCodeSet;

    // When TCL_ERROR is returned, this gives the line number within
    // the command where the error occurred (1 means first line).


    internal int errorLine;

    // Stores the current result in the interpreter.

    private TclObject m_result;

    // Value m_result is set to when resetResult() is called.

    private TclObject m_nullResult;

    // Used ONLY by PackageCmd.

    internal Hashtable packageTable;
    internal string packageUnknown;


    // Used ONLY by the Parser.

    internal TclObject[][][] parserObjv;
    internal int[] parserObjvUsed;

    internal TclToken[] parserTokens;
    internal int parserTokensUsed;


    // Used ONLY by JavaImportCmd
    internal Hashtable[] importTable;

    // List of unsafe commands:

    internal static readonly string[] unsafeCmds = new string[] { "encoding", "exit", "load", "cd", "fconfigure", "file", "glob", "open", "pwd", "socket", "beep", "echo", "ls", "resource", "source", "exec", "source" };

    // Flags controlling the call of invoke.

    internal const int INVOKE_HIDDEN = 1;
    internal const int INVOKE_NO_UNKNOWN = 2;
    internal const int INVOKE_NO_TRACEBACK = 4;

    public Interp()
    {
      InitBlock();

      //freeProc         = null;
      errorLine = 0;

      // An empty result is used pretty often. We will use a shared
      // TclObject instance to represent the empty result so that we
      // don't need to create a new TclObject instance every time the
      // interpreter result is set to empty.

      m_nullResult = TclString.newInstance( "" );
      m_nullResult.preserve(); // Increment refCount to 1
      m_nullResult.preserve(); // Increment refCount to 2 (shared)
      m_result = TclString.newInstance( "" ); //m_nullResult; // correcponds to iPtr->objResultPtr
      m_result.preserve();

      expr = new Expression();
      nestLevel = 0;
      maxNestingDepth = 1000;

      frame = null;
      varFrame = null;

      returnCode = TCL.CompletionCode.OK;
      errorInfo = null;
      errorCode = null;

      packageTable = new Hashtable();
      packageUnknown = null;
      cmdCount = 0;
      termOffset = 0;
      resolvers = null;
      evalFlags = 0;
      scriptFile = null;
      flags = 0;
      isSafe = false;
      assocData = null;


      globalNs = null; // force creation of global ns below
      globalNs = NamespaceCmd.createNamespace( this, null, null );
      if ( globalNs == null )
      {
        throw new TclRuntimeError( "Interp(): can't create global namespace" );
      }


      // Init things that are specific to the Jacl implementation

      workingDir = new System.IO.FileInfo( System.Environment.CurrentDirectory );
      noEval = 0;

      notifier = Notifier.getNotifierForThread( System.Threading.Thread.CurrentThread );
      notifier.preserve();

      randSeedInit = false;

      deleted = false;
      errInProgress = false;
      errAlreadyLogged = false;
      errCodeSet = false;

      dbg = initDebugInfo();

      slaveTable = new Hashtable();
      targetTable = new Hashtable();
      aliasTable = new Hashtable();

      // init parser variables
      Parser.init( this );
      TclParse.init( this );

      // Initialize the Global (static) channel table and the local
      // interp channel table.

      interpChanTable = TclIO.getInterpChanTable( this );

      // Sets up the variable trace for tcl_precision.

      Util.setupPrecisionTrace( this );

      // Create the built-in commands.

      createCommands();

      try
      {
        // Set up tcl_platform, tcl_version, tcl_library and other
        // global variables.

        setVar( "tcl_platform", "platform", "windows", TCL.VarFlag.GLOBAL_ONLY );
        setVar( "tcl_platform", "byteOrder", "bigEndian", TCL.VarFlag.GLOBAL_ONLY );

        setVar( "tcl_platform", "os", Environment.OSVersion.Platform.ToString(), TCL.VarFlag.GLOBAL_ONLY );
        setVar( "tcl_platform", "osVersion", Environment.OSVersion.Version.ToString(), TCL.VarFlag.GLOBAL_ONLY );
        setVar( "tcl_platform", "machine", Util.tryGetSystemProperty( "os.arch", "?" ), TCL.VarFlag.GLOBAL_ONLY );

        setVar( "tcl_version", TCL_VERSION, TCL.VarFlag.GLOBAL_ONLY );
        setVar( "tcl_patchLevel", TCL_PATCH_LEVEL, TCL.VarFlag.GLOBAL_ONLY );
        setVar( "tcl_library", "resource:/tcl/lang/library", TCL.VarFlag.GLOBAL_ONLY );
        if ( Util.Windows )
        {
          setVar( "tcl_platform", "host_platform", "windows", TCL.VarFlag.GLOBAL_ONLY );
        }
        else if ( Util.Mac )
        {
          setVar( "tcl_platform", "host_platform", "macintosh", TCL.VarFlag.GLOBAL_ONLY );
        }
        else
        {
          setVar( "tcl_platform", "host_platform", "unix", TCL.VarFlag.GLOBAL_ONLY );
        }

        // Create the env array an populated it with proper
        // values.

        Env.initialize( this );

        // Register Tcl's version number. Note: This MUST be 
        // done before the call to evalResource, otherwise
        // calls to "package require tcl" will fail.

        pkgProvide( "Tcl", TCL_VERSION );

        // Source the init.tcl script to initialize auto-loading.

        evalResource( "/tcl/lang/library/init.tcl" );
      }
      catch ( TclException e )
      {
        System.Diagnostics.Debug.WriteLine( getResult().ToString() );
        SupportClass.WriteStackTrace( e, Console.Error );
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }
    }
    public override void eventuallyDispose()
    {
      if ( deleted )
      {
        return;
      }

      deleted = true;

      if ( nestLevel > 0 )
      {
        //-- TODO -- Determine why this is an error             throw new TclRuntimeError("dispose() called with active evals");
      }

      // Remove our association with the notifer (if we had one).

      if ( notifier != null )
      {
        notifier.release();
        notifier = null;
      }

      // Dismantle everything in the global namespace except for the
      // "errorInfo" and "errorCode" variables. These might be needed
      // later on if errors occur while deleting commands. We are careful
      // to destroy and recreate the "errorInfo" and "errorCode"
      // variables, in case they had any traces on them.
      //
      // Dismantle the namespace here, before we clear the assocData. If any
      // background errors occur here, they will be deleted below.


      // FIXME : check impl of TclTeardownNamespace
      NamespaceCmd.teardownNamespace( globalNs );

      // Delete all variables.

      TclObject errorInfoObj = null, errorCodeObj = null;

      try
      {
        errorInfoObj = getVar( "errorInfo", null, TCL.VarFlag.GLOBAL_ONLY );
      }
      catch ( TclException e )
      {
        // Do nothing when var does not exist.
      }

      if ( errorInfoObj != null )
      {
        errorInfoObj.preserve();
      }

      try
      {
        errorCodeObj = getVar( "errorCode", null, TCL.VarFlag.GLOBAL_ONLY );
      }
      catch ( TclException e )
      {
        // Do nothing when var does not exist.
      }

      if ( errorCodeObj != null )
      {
        errorCodeObj.preserve();
      }

      frame = null;
      varFrame = null;

      try
      {
        if ( errorInfoObj != null )
        {
          setVar( "errorInfo", null, errorInfoObj, TCL.VarFlag.GLOBAL_ONLY );
          errorInfoObj.release();
        }
        if ( errorCodeObj != null )
        {
          setVar( "errorCode", null, errorCodeObj, TCL.VarFlag.GLOBAL_ONLY );
          errorCodeObj.release();
        }
      }
      catch ( TclException e )
      {
        // Ignore it -- same behavior as Tcl 8.0.
      }

      // Tear down the math function table.

      expr = null;

      // Remove all the assoc data tied to this interp and invoke
      // deletion callbacks; note that a callback can create new
      // callbacks, so we iterate.

      // ATK The java code was somethink strong
      if ( assocData != null )
      {
        foreach ( AssocData data in assocData.Values )
        {
          data.disposeAssocData( this );
        }
        assocData.Clear();
      }

      // Close any remaining channels

      for ( IDictionaryEnumerator e = interpChanTable.GetEnumerator() ; e.MoveNext() ; )
      {
        Object key = e.Key;
        Channel chan = (Channel)e.Value;
        try
        {
          chan.close();
        }
        catch ( System.IO.IOException ex )
        {
          // Ignore any IO errors
        }
      }

      // Finish deleting the global namespace.

      // FIXME : check impl of Tcl_DeleteNamespace
      NamespaceCmd.deleteNamespace( globalNs );
      globalNs = null;

      // Free up the result *after* deleting variables, since variable
      // deletion could have transferred ownership of the result string
      // to Tcl.

      frame = null;
      varFrame = null;
      resolvers = null;

      resetResult();
    }
    ~Interp()
    {
      dispose();
    }
    protected internal void createCommands()
    {
      Extension.loadOnDemand( this, "after", "tcl.lang.AfterCmd" );
      Extension.loadOnDemand( this, "append", "tcl.lang.AppendCmd" );
      Extension.loadOnDemand( this, "array", "tcl.lang.ArrayCmd" );
      Extension.loadOnDemand( this, "binary", "tcl.lang.BinaryCmd" );
      Extension.loadOnDemand( this, "break", "tcl.lang.BreakCmd" );
      Extension.loadOnDemand( this, "case", "tcl.lang.CaseCmd" );
      Extension.loadOnDemand( this, "catch", "tcl.lang.CatchCmd" );
      Extension.loadOnDemand( this, "cd", "tcl.lang.CdCmd" );
      Extension.loadOnDemand( this, "clock", "tcl.lang.ClockCmd" );
      Extension.loadOnDemand( this, "close", "tcl.lang.CloseCmd" );
      Extension.loadOnDemand( this, "continue", "tcl.lang.ContinueCmd" );
      Extension.loadOnDemand( this, "concat", "tcl.lang.ConcatCmd" );
      Extension.loadOnDemand( this, "encoding", "tcl.lang.EncodingCmd" );
      Extension.loadOnDemand( this, "eof", "tcl.lang.EofCmd" );
      Extension.loadOnDemand( this, "eval", "tcl.lang.EvalCmd" );
      Extension.loadOnDemand( this, "error", "tcl.lang.ErrorCmd" );
      if ( !Util.Mac )
      {
        Extension.loadOnDemand( this, "exec", "tcl.lang.ExecCmd" );
      }
      Extension.loadOnDemand( this, "exit", "tcl.lang.ExitCmd" );
      Extension.loadOnDemand( this, "expr", "tcl.lang.ExprCmd" );
      Extension.loadOnDemand( this, "fblocked", "tcl.lang.FblockedCmd" );
      Extension.loadOnDemand( this, "fconfigure", "tcl.lang.FconfigureCmd" );
      Extension.loadOnDemand( this, "file", "tcl.lang.FileCmd" );
      Extension.loadOnDemand( this, "flush", "tcl.lang.FlushCmd" );
      Extension.loadOnDemand( this, "for", "tcl.lang.ForCmd" );
      Extension.loadOnDemand( this, "foreach", "tcl.lang.ForeachCmd" );
      Extension.loadOnDemand( this, "format", "tcl.lang.FormatCmd" );
      Extension.loadOnDemand( this, "gets", "tcl.lang.GetsCmd" );
      Extension.loadOnDemand( this, "global", "tcl.lang.GlobalCmd" );
      Extension.loadOnDemand( this, "glob", "tcl.lang.GlobCmd" );
      Extension.loadOnDemand( this, "if", "tcl.lang.IfCmd" );
      Extension.loadOnDemand( this, "incr", "tcl.lang.IncrCmd" );
      Extension.loadOnDemand( this, "info", "tcl.lang.InfoCmd" );
      Extension.loadOnDemand( this, "interp", "tcl.lang.InterpCmd" );
      Extension.loadOnDemand( this, "list", "tcl.lang.ListCmd" );
      Extension.loadOnDemand( this, "join", "tcl.lang.JoinCmd" );
      Extension.loadOnDemand( this, "lappend", "tcl.lang.LappendCmd" );
      Extension.loadOnDemand( this, "lindex", "tcl.lang.LindexCmd" );
      Extension.loadOnDemand( this, "linsert", "tcl.lang.LinsertCmd" );
      Extension.loadOnDemand( this, "llength", "tcl.lang.LlengthCmd" );
      Extension.loadOnDemand( this, "lrange", "tcl.lang.LrangeCmd" );
      Extension.loadOnDemand( this, "lreplace", "tcl.lang.LreplaceCmd" );
      Extension.loadOnDemand( this, "lsearch", "tcl.lang.LsearchCmd" );
      Extension.loadOnDemand( this, "lsort", "tcl.lang.LsortCmd" );
      Extension.loadOnDemand( this, "namespace", "tcl.lang.NamespaceCmd" );
      Extension.loadOnDemand( this, "open", "tcl.lang.OpenCmd" );
      Extension.loadOnDemand( this, "package", "tcl.lang.PackageCmd" );
      Extension.loadOnDemand( this, "proc", "tcl.lang.ProcCmd" );
      Extension.loadOnDemand( this, "puts", "tcl.lang.PutsCmd" );
      Extension.loadOnDemand( this, "pwd", "tcl.lang.PwdCmd" );
      Extension.loadOnDemand( this, "read", "tcl.lang.ReadCmd" );
      Extension.loadOnDemand( this, "regsub", "tcl.lang.RegsubCmd" );
      Extension.loadOnDemand( this, "rename", "tcl.lang.RenameCmd" );
      Extension.loadOnDemand( this, "return", "tcl.lang.ReturnCmd" );
      Extension.loadOnDemand( this, "scan", "tcl.lang.ScanCmd" );
      Extension.loadOnDemand( this, "seek", "tcl.lang.SeekCmd" );
      Extension.loadOnDemand( this, "set", "tcl.lang.SetCmd" );
      Extension.loadOnDemand( this, "socket", "tcl.lang.SocketCmd" );
      Extension.loadOnDemand( this, "source", "tcl.lang.SourceCmd" );
      Extension.loadOnDemand( this, "split", "tcl.lang.SplitCmd" );
      Extension.loadOnDemand( this, "string", "tcl.lang.StringCmd" );
      Extension.loadOnDemand( this, "subst", "tcl.lang.SubstCmd" );
      Extension.loadOnDemand( this, "switch", "tcl.lang.SwitchCmd" );
      Extension.loadOnDemand( this, "tell", "tcl.lang.TellCmd" );
      Extension.loadOnDemand( this, "time", "tcl.lang.TimeCmd" );
      Extension.loadOnDemand( this, "trace", "tcl.lang.TraceCmd" );
      Extension.loadOnDemand( this, "unset", "tcl.lang.UnsetCmd" );
      Extension.loadOnDemand( this, "update", "tcl.lang.UpdateCmd" );
      Extension.loadOnDemand( this, "uplevel", "tcl.lang.UplevelCmd" );
      Extension.loadOnDemand( this, "upvar", "tcl.lang.UpvarCmd" );
      Extension.loadOnDemand( this, "variable", "tcl.lang.VariableCmd" );
      Extension.loadOnDemand( this, "vwait", "tcl.lang.VwaitCmd" );
      Extension.loadOnDemand( this, "while", "tcl.lang.WhileCmd" );


      // Add "regexp" and related commands to this interp.
      RegexpCmd.init( this );


      // The Java package is only loaded when the user does a
      // "package require java" in the interp. We need to create a small
      // command that will load when "package require java" is called.

      Extension.loadOnDemand( this, "jaclloadjava", "tcl.lang.JaclLoadJavaCmd" );

      try
      {
        eval( "package ifneeded java 1.3.1 jaclloadjava" );
      }
      catch ( TclException e )
      {
        System.Diagnostics.Debug.WriteLine( getResult().ToString() );
        SupportClass.WriteStackTrace( e, Console.Error );
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }

    }
    public void setAssocData( string name, AssocData data )
    // Object associated with the name.
    {
      if ( assocData == null )
      {
        assocData = new Hashtable();
      }
      SupportClass.PutElement( assocData, name, data );
    }
    public void deleteAssocData( string name )
    // Name of association.
    {
      if ( assocData == null )
      {
        return;
      }

      SupportClass.HashtableRemove( assocData, name );
    }
    public AssocData getAssocData( string name )
    // Name of association.
    {
      if ( assocData == null )
      {
        return null;
      }
      else
      {
        return (AssocData)assocData[name];
      }
    }

    public void backgroundError()
    {
      BgErrorMgr mgr = (BgErrorMgr)getAssocData( "tclBgError" );
      if ( mgr == null )
      {
        mgr = new BgErrorMgr( this );
        setAssocData( "tclBgError", mgr );
      }
      mgr.addBgError();
    }

    /*-----------------------------------------------------------------
    *
    *	                     VARIABLES
    *
    *-----------------------------------------------------------------
    */
    public TclObject setVar( TclObject nameObj, TclObject value, TCL.VarFlag flags )
    {
      return Var.setVar( this, nameObj, value, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public TclObject setVar( string name, TclObject value, TCL.VarFlag flags )
    {
      return Var.setVar( this, name, value, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public TclObject setVar( string name1, string name2, TclObject value, TCL.VarFlag flags )
    {
      return Var.setVar( this, name1, name2, value, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void setVar( string name, string strValue, TCL.VarFlag flags )
    {
      Var.setVar( this, name, TclString.newInstance( strValue ), ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void setVar( string name1, string name2, string strValue, TCL.VarFlag flags )
    {
      Var.setVar( this, name1, name2, TclString.newInstance( strValue ), ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public TclObject getVar( TclObject nameObj, TCL.VarFlag flags )
    {
      return Var.getVar( this, nameObj, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public TclObject getVar( string name, TCL.VarFlag flags )
    {
      return Var.getVar( this, name, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public TclObject getVar( string name1, string name2, TCL.VarFlag flags )
    {
      return Var.getVar( this, name1, name2, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void unsetVar( TclObject nameObj, TCL.VarFlag flags )
    {
      Var.unsetVar( this, nameObj, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void unsetVar( string name, TCL.VarFlag flags )
    {
      Var.unsetVar( this, name, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void unsetVar( string name1, string name2, TCL.VarFlag flags )
    {
      Var.unsetVar( this, name1, name2, ( flags | TCL.VarFlag.LEAVE_ERR_MSG ) );
    }
    public void traceVar( TclObject nameObj, VarTrace trace, TCL.VarFlag flags )
    {
      Var.traceVar( this, nameObj, flags, trace );
    }
    public void traceVar( string name, VarTrace trace, TCL.VarFlag flags )
    {
      Var.traceVar( this, name, flags, trace );
    }
    public void traceVar( string part1, string part2, VarTrace trace, TCL.VarFlag flags )
    {
      Var.traceVar( this, part1, part2, flags, trace );
    }
    public void untraceVar( TclObject nameObj, VarTrace trace, TCL.VarFlag flags )
    // OR-ed collection of bits describing current
    // trace, including any of TCL.VarFlag.TRACE_READS,
    // TCL.VarFlag.TRACE_WRITES, TCL.VarFlag.TRACE_UNSETS,
    // TCL.VarFlag.GLOBAL_ONLY and TCL.VarFlag.NAMESPACE_ONLY.
    {
      Var.untraceVar( this, nameObj, flags, trace );
    }
    public void untraceVar( string name, VarTrace trace, TCL.VarFlag flags )
    // OR-ed collection of bits describing current
    // trace, including any of TCL.VarFlag.TRACE_READS,
    // TCL.VarFlag.TRACE_WRITES, TCL.VarFlag.TRACE_UNSETS,
    // TCL.VarFlag.GLOBAL_ONLY and TCL.VarFlag.NAMESPACE_ONLY.
    {
      Var.untraceVar( this, name, flags, trace );
    }
    public void untraceVar( string part1, string part2, VarTrace trace, TCL.VarFlag flags )
    // OR-ed collection of bits describing current
    // trace, including any of TCL.VarFlag.TRACE_READS,
    // TCL.VarFlag.TRACE_WRITES, TCL.VarFlag.TRACE_UNSETS,
    // TCL.VarFlag.GLOBAL_ONLY and TCL.VarFlag.NAMESPACE_ONLY.
    {
      Var.untraceVar( this, part1, part2, flags, trace );
    }
    public void createCommand( string cmdName, Command cmdImpl )
    // Command object to associate with
    // cmdName.
    {
      ImportRef oldRef = null;
      NamespaceCmd.Namespace ns;
      WrappedCommand cmd, refCmd;
      string tail;
      ImportedCmdData data;

      if ( deleted )
      {
        // The interpreter is being deleted.  Don't create any new
        // commands; it's not safe to muck with the interpreter anymore.

        return;
      }

      // Determine where the command should reside. If its name contains 
      // namespace qualifiers, we put it in the specified namespace; 
      // otherwise, we always put it in the global namespace.

      if ( cmdName.IndexOf( "::" ) != -1 )
      {
        // Java does not support passing an address so we pass
        // an array of size 1 and then assign arr[0] to the value
        NamespaceCmd.Namespace[] nsArr = new NamespaceCmd.Namespace[1];
        NamespaceCmd.Namespace[] dummyArr = new NamespaceCmd.Namespace[1];
        string[] tailArr = new string[1];

        NamespaceCmd.getNamespaceForQualName( this, cmdName, null, TCL.VarFlag.CREATE_NS_IF_UNKNOWN, nsArr, dummyArr, dummyArr, tailArr );

        ns = nsArr[0];
        tail = tailArr[0];

        if ( ( ns == null ) || ( (System.Object)tail == null ) )
        {
          return;
        }
      }
      else
      {
        ns = globalNs;
        tail = cmdName;
      }

      cmd = (WrappedCommand)ns.cmdTable[tail];
      if ( cmd != null )
      {
        // Command already exists. Delete the old one.
        // Be careful to preserve any existing import links so we can
        // restore them down below.  That way, you can redefine a
        // command and its import status will remain intact.

        oldRef = cmd.importRef;
        cmd.importRef = null;

        deleteCommandFromToken( cmd );

        // FIXME : create a test case for this condition!

        cmd = (WrappedCommand)ns.cmdTable[tail];
        if ( cmd != null )
        {
          // If the deletion callback recreated the command, just throw
          // away the new command (if we try to delete it again, we
          // could get stuck in an infinite loop).

          SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
        }
      }

      cmd = new WrappedCommand();
      ns.cmdTable.Add( tail, cmd );
      cmd.table = ns.cmdTable;
      cmd.hashKey = tail;
      cmd.ns = ns;
      cmd.cmd = cmdImpl;
      cmd.deleted = false;
      // FIXME : import feature not implemented
      //cmd.importRef = null;

      // Plug in any existing import references found above.  Be sure
      // to update all of these references to point to the new command.

      if ( oldRef != null )
      {
        cmd.importRef = oldRef;
        while ( oldRef != null )
        {
          refCmd = oldRef.importedCmd;
          data = (ImportedCmdData)refCmd.cmd;
          data.realCmd = cmd;
          oldRef = oldRef.next;
        }
      }

      // There are no shadowed commands in Jacl because they are only
      // used in the 8.0 compiler

      return;
    }
    /*
     *----------------------------------------------------------------------
     *
     * Tcl_CreateObjCommand --
     *
     *	Define a new object-based command in a command table.
     *
     * Results:
     *	The return value is a token for the command, which can
     *	be used in future calls to Tcl_GetCommandName.
     *
     * Side effects:
     *	If no command named "cmdName" already exists for interp, one is
     *	created. Otherwise, if a command does exist, then if the
     *	object-based Tcl_ObjCmdProc is TclInvokeStringCommand, we assume
     *	Tcl_CreateCommand was called previously for the same command and
     *	just set its Tcl_ObjCmdProc to the argument "proc"; otherwise, we
     *	delete the old command.
     *
     *	In the future, during bytecode evaluation when "cmdName" is seen as
     *	the name of a command by Tcl_EvalObj or Tcl_Eval, the object-based
     *	Tcl_ObjCmdProc proc will be called. When the command is deleted from
     *	the table, deleteProc will be called. See the manual entry for
     *	details on the calling sequence.
     *
     *----------------------------------------------------------------------
     */

    public delegate int dxObjCmdProc( object clientData, Interp interp, int argc, TclObject[] argv );
    public delegate void dxCmdDeleteProc( ref object clientData );

    public void createObjCommand( string cmdName, dxObjCmdProc proc, object clientData, dxCmdDeleteProc deleteProc )
    // Command object to associate with cmdName.
    {
      ImportRef oldRef = null;
      NamespaceCmd.Namespace ns;
      WrappedCommand cmd, refCmd;
      string tail;
      ImportedCmdData data;
      int _new;

      if ( deleted )
      {
        // The interpreter is being deleted.  Don't create any new
        // commands; it's not safe to muck with the interpreter anymore.

        return;
      }

      // Determine where the command should reside. If its name contains 
      // namespace qualifiers, we put it in the specified namespace; 
      // otherwise, we always put it in the global namespace.

      if ( cmdName.IndexOf( "::" ) != -1 )
      {
        // Java does not support passing an address so we pass
        // an array of size 1 and then assign arr[0] to the value
        NamespaceCmd.Namespace[] nsArr = new NamespaceCmd.Namespace[1];
        NamespaceCmd.Namespace[] dummyArr = new NamespaceCmd.Namespace[1];
        string[] tailArr = new string[1];

        NamespaceCmd.getNamespaceForQualName( this, cmdName, null, TCL.VarFlag.CREATE_NS_IF_UNKNOWN, nsArr, dummyArr, dummyArr, tailArr );

        ns = nsArr[0];
        tail = tailArr[0];

        if ( ( ns == null ) || ( (System.Object)tail == null ) )
        {
          return;
        }
      }
      else
      {
        ns = globalNs;
        tail = cmdName;
      }

      cmd = (WrappedCommand)ns.cmdTable[tail];
      if ( cmd != null )
      {
        /*
 * Command already exists. If its object-based Tcl_ObjCmdProc is
 * TclInvokeStringCommand, we just set its Tcl_ObjCmdProc to the
 * argument "proc". Otherwise, we delete the old command. 
 */
        if ( cmd.objProc != null && cmd.objProc.GetType().Name == "TclInvokeStringCommand" )
        {
          cmd.objProc = proc;
          cmd.objClientData = clientData;
          cmd.deleteProc = deleteProc;
          cmd.deleteData = clientData;
          return;
        }
        /*
         * Otherwise, we delete the old command.  Be careful to preserve
         * any existing import links so we can restore them down below.
         * That way, you can redefine a command and its import status
         * will remain intact.
         */
        oldRef = cmd.importRef;
        cmd.importRef = null;

        deleteCommandFromToken( cmd );

        // FIXME : create a test case for this condition!

        cmd = (WrappedCommand)ns.cmdTable[tail];
        if ( cmd != null )
        {
          // If the deletion callback recreated the command, just throw
          // away the new command (if we try to delete it again, we
          // could get stuck in an infinite loop).

          SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
        }
      }

      cmd = new WrappedCommand();
      ns.cmdTable.Add( tail, cmd );
      cmd.table = ns.cmdTable;
      cmd.hashKey = tail;
      cmd.ns = ns;
      cmd.cmd = null;
      cmd.deleted = false;
      // FIXME : import feature not implemented
      //cmd.importRef = null;

      // TODO -- Determine if this is all correct
      cmd.objProc = proc;
      cmd.objClientData = clientData;
      //cmd.proc = TclInvokeObjectCommand;
      cmd.clientData = (object)cmd;
      cmd.deleteProc = deleteProc;
      cmd.deleteData = clientData;
      cmd.flags = 0;


      // Plug in any existing import references found above.  Be sure
      // to update all of these references to point to the new command.

      if ( oldRef != null )
      {
        cmd.importRef = oldRef;
        while ( oldRef != null )
        {
          refCmd = oldRef.importedCmd;
          data = (ImportedCmdData)refCmd.cmd;
          data.realCmd = cmd;
          oldRef = oldRef.next;
        }
      }

      // There are no shadowed commands in Jacl because they are only
      // used in the 8.0 compiler

      return;
    }
    internal string getCommandFullName( WrappedCommand cmd )
    // Token for the command.
    {
      Interp interp = this;
      System.Text.StringBuilder name = new System.Text.StringBuilder();

      // Add the full name of the containing namespace, followed by the "::"
      // separator, and the command name.

      if ( cmd != null )
      {
        if ( cmd.ns != null )
        {
          name.Append( cmd.ns.fullName );
          if ( cmd.ns != interp.globalNs )
          {
            name.Append( "::" );
          }
        }
        if ( cmd.table != null )
        {
          name.Append( cmd.hashKey );
        }
      }

      return name.ToString();
    }
    public int deleteCommand( string cmdName )
    // Name of command to remove.
    {
      WrappedCommand cmd;

      //  Find the desired command and delete it.

      try
      {
        cmd = NamespaceCmd.findCommand( this, cmdName, null, 0 );
      }
      catch ( TclException e )
      {
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }
      if ( cmd == null )
      {
        return -1;
      }
      if ( cmd.deleteProc != null ) cmd.deleteProc( ref cmd.deleteData );
      return deleteCommandFromToken( cmd );
    }
    protected internal int deleteCommandFromToken( WrappedCommand cmd )
    // Wrapper Token for command to delete.
    {
      if ( cmd == null )
      {
        return -1;
      }

      ImportRef ref_Renamed, nextRef;
      WrappedCommand importCmd;

      // The code here is tricky.  We can't delete the hash table entry
      // before invoking the deletion callback because there are cases
      // where the deletion callback needs to invoke the command (e.g.
      // object systems such as OTcl). However, this means that the
      // callback could try to delete or rename the command. The deleted
      // flag allows us to detect these cases and skip nested deletes.

      if ( cmd.deleted )
      {
        // Another deletion is already in progress.  Remove the hash
        // table entry now, but don't invoke a callback or free the
        // command structure.

        if ( (System.Object)cmd.hashKey != null && cmd.table != null )
        {
          SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
          cmd.table = null;
          cmd.hashKey = null;
        }
        return 0;
      }

      cmd.deleted = true;
      if ( cmd.cmd is CommandWithDispose )
      {
        ( (CommandWithDispose)cmd.cmd ).disposeCmd();
      }
      if ( cmd.deleteProc != null )
      {
        cmd.deleteProc( ref cmd.objClientData );
      }
      // If this command was imported into other namespaces, then imported
      // commands were created that refer back to this command. Delete these
      // imported commands now.

      for ( ref_Renamed = cmd.importRef ; ref_Renamed != null ; ref_Renamed = nextRef )
      {
        nextRef = ref_Renamed.next;
        importCmd = ref_Renamed.importedCmd;
        deleteCommandFromToken( importCmd );
      }

      // FIXME : what does this mean? Is this a mistake in the C comment?

      // Don't use hPtr to delete the hash entry here, because it's
      // possible that the deletion callback renamed the command.
      // Instead, use cmdPtr->hptr, and make sure that no-one else
      // has already deleted the hash entry.

      if ( cmd.table != null )
      {
        SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
        cmd.table = null;
        cmd.hashKey = null;
      }

      // Drop the reference to the Command instance inside the WrappedCommand

      cmd.cmd = null;

      // We do not need to cleanup the WrappedCommand because GC will get it.

      return 0;
    }
    protected internal void renameCommand( string oldName, string newName )
    {
      Interp interp = this;
      string newTail;
      NamespaceCmd.Namespace cmdNs, newNs;
      WrappedCommand cmd;
      Hashtable table, oldTable;
      string hashKey, oldHashKey;

      // Find the existing command. An error is returned if cmdName can't
      // be found.

      cmd = NamespaceCmd.findCommand( interp, oldName, null, 0 );
      if ( cmd == null )
      {
        throw new TclException( interp, "can't " + ( ( ( (System.Object)newName == null ) || ( newName.Length == 0 ) ) ? "delete" : "rename" ) + " \"" + oldName + "\": command doesn't exist" );
      }
      cmdNs = cmd.ns;

      // If the new command name is NULL or empty, delete the command. Do this
      // with Tcl_DeleteCommandFromToken, since we already have the command.

      if ( ( (System.Object)newName == null ) || ( newName.Length == 0 ) )
      {
        deleteCommandFromToken( cmd );
        return;
      }

      // Make sure that the destination command does not already exist.
      // The rename operation is like creating a command, so we should
      // automatically create the containing namespaces just like
      // Tcl_CreateCommand would.

      NamespaceCmd.Namespace[] newNsArr = new NamespaceCmd.Namespace[1];
      NamespaceCmd.Namespace[] dummyArr = new NamespaceCmd.Namespace[1];
      string[] newTailArr = new string[1];

      NamespaceCmd.getNamespaceForQualName( interp, newName, null, TCL.VarFlag.CREATE_NS_IF_UNKNOWN, newNsArr, dummyArr, dummyArr, newTailArr );

      newNs = newNsArr[0];
      newTail = newTailArr[0];

      if ( ( newNs == null ) || ( (System.Object)newTail == null ) )
      {
        throw new TclException( interp, "can't rename to \"" + newName + "\": bad command name" );
      }
      if ( newNs.cmdTable[newTail] != null )
      {
        throw new TclException( interp, "can't rename to \"" + newName + "\": command already exists" );
      }

      // Warning: any changes done in the code here are likely
      // to be needed in Tcl_HideCommand() code too.
      // (until the common parts are extracted out)     --dl

      // Put the command in the new namespace so we can check for an alias
      // loop. Since we are adding a new command to a namespace, we must
      // handle any shadowing of the global commands that this might create.

      oldTable = cmd.table;
      oldHashKey = cmd.hashKey;
      newNs.cmdTable.Add( newTail, cmd );
      cmd.table = newNs.cmdTable;
      cmd.hashKey = newTail;
      cmd.ns = newNs;

      // FIXME : this is a nasty hack that fixes renaming for Procedures
      // that move from one namespace to another, but the real problem
      // is that a rename does not work for Command instances in general

      if ( cmd.cmd is Procedure )
      {
        Procedure p = (Procedure)cmd.cmd;
        p.ns = cmd.ns;
      }

      // Now check for an alias loop. If we detect one, put everything back
      // the way it was and report the error.

      try
      {
        interp.preventAliasLoop( interp, cmd );
      }
      catch ( TclException e )
      {
        newNs.cmdTable.Remove( newTail );
        cmd.table = oldTable;
        cmd.hashKey = oldHashKey;
        cmd.ns = cmdNs;
        throw;
      }

      // The new command name is okay, so remove the command from its
      // current namespace. This is like deleting the command, so bump
      // the cmdEpoch to invalidate any cached references to the command.

      SupportClass.HashtableRemove( oldTable, oldHashKey );

      return;
    }
    internal void preventAliasLoop( Interp cmdInterp, WrappedCommand cmd )
    {
      // If we are not creating or renaming an alias, then it is
      // always OK to create or rename the command.

      if ( !( cmd.cmd is InterpAliasCmd ) )
      {
        return;
      }

      // OK, we are dealing with an alias, so traverse the chain of aliases.
      // If we encounter the alias we are defining (or renaming to) any in
      // the chain then we have a loop.

      InterpAliasCmd alias = (InterpAliasCmd)cmd.cmd;
      InterpAliasCmd nextAlias = alias;
      while ( true )
      {

        // If the target of the next alias in the chain is the same as
        // the source alias, we have a loop.

        WrappedCommand aliasCmd = nextAlias.getTargetCmd( this );
        if ( aliasCmd == null )
        {
          return;
        }
        if ( aliasCmd.cmd == cmd.cmd )
        {

          throw new TclException( this, "cannot define or rename alias \"" + alias.name + "\": would create a loop" );
        }

        // Otherwise, follow the chain one step further. See if the target
        // command is an alias - if so, follow the loop to its target
        // command. Otherwise we do not have a loop.

        if ( !( aliasCmd.cmd is InterpAliasCmd ) )
        {
          return;
        }
        nextAlias = (InterpAliasCmd)aliasCmd.cmd;
      }
    }
    public Command getCommand( string cmdName )
    // String name of the command.
    {
      //  Find the desired command and return it.

      WrappedCommand cmd;

      try
      {
        cmd = NamespaceCmd.findCommand( this, cmdName, null, 0 );
      }
      catch ( TclException e )
      {
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }

      return ( ( cmd == null ) ? null : cmd.cmd );
    }
    public WrappedCommand getObjCommand( string cmdName )
    // String name of the command.
    {
      //  Find the desired command and return it.

      WrappedCommand cmd;

      try
      {
        cmd = NamespaceCmd.findCommand( this, cmdName, null, 0 );
      }
      catch ( TclException e )
      {
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }

      return ( ( cmd == null ) ? null : ( cmd.objProc == null && cmd.clientData == null ) ? null : cmd );
    }
    public static bool commandComplete( string inString )
    // The string to check.
    {
      return Parser.commandComplete( inString, inString.Length );
    }


    /*-----------------------------------------------------------------
    *
    *	                     EVAL
    *
    *-----------------------------------------------------------------
    */

    public TclObject getResult()
    {
      return m_result;
    }
    public void setResult( TclObject r )
    // A Tcl Object to be set as the result.
    {
      if ( r == null )
      {
        throw new System.NullReferenceException( "Interp.setResult() called with null TclObject argument." );
      }

      if ( r == m_result )
      {
        // Setting to current value (including m_nullResult) is a no-op.
        return;
      }

      if ( m_result != m_nullResult )
      {
        m_result.release();
      }

      m_result = r;

      if ( m_result != m_nullResult )
      {
        m_result.preserve();
      }
    }
    public void setResult( string r )
    // A string result.
    {
      if ( (System.Object)r == null )
      {
        resetResult();
      }
      else
      {
        setResult( TclString.newInstance( r ) );
      }
    }
    public void setResult( int r )
    // An int result.
    {
      setResult( TclInteger.newInstance( r ) );
    }
    public void setResult( double r )
    // A double result.
    {
      setResult( TclDouble.newInstance( r ) );
    }
    public void setResult( bool r )
    // A boolean result.
    {
      setResult( TclBoolean.newInstance( r ) );
    }
    public void resetResult()
    {
      if ( m_result != m_nullResult )
      {
        m_result.release();
        m_result = TclString.newInstance( "" ); //m_nullResult;
        m_result.preserve();
        if ( !m_nullResult.Shared )
        {
          throw new TclRuntimeError( "m_nullResult is not shared" );
        }
      }
      errAlreadyLogged = false;
      errInProgress = false;
      errCodeSet = false;
      returnCode = TCL.CompletionCode.OK;
    }
    public void appendElement( object Element )
    {
      TclObject result;

      result = getResult();
      if ( result.Shared )
      {
        result = result.duplicate();
      }
      TclList.append( this, result, TclObj.newInstance( Element ) );
      setResult( result );
    }

    public void appendElement(
      string Element )
    {
      TclObject result;

      result = getResult();
      if ( result.Shared )
      {
        result = result.duplicate();
      }
      TclList.append( this, result, TclString.newInstance( Element ) );
      setResult( result );
    }
    public void eval( string inString, int flags )
    {
      int evalFlags = this.evalFlags;
      this.evalFlags &= ~Parser.TCL_ALLOW_EXCEPTIONS;

      CharPointer script = new CharPointer( inString );
      try
      {
        Parser.eval2( this, script.array, script.index, script.length(), flags );
      }
      catch ( TclException e )
      {

        if ( nestLevel != 0 )
        {
          throw;
        }

        // Update the interpreter's evaluation level count. If we are again at
        // the top level, process any unusual return code returned by the
        // evaluated code. Note that we don't propagate an exception that
        // has a TCL.CompletionCode.RETURN error code when updateReturnInfo() returns TCL.CompletionCode.OK.

        TCL.CompletionCode result = e.getCompletionCode();

        if ( result == TCL.CompletionCode.RETURN )
        {
          result = updateReturnInfo();
        }
        if ( result != TCL.CompletionCode.OK && result != TCL.CompletionCode.ERROR && ( evalFlags & Parser.TCL_ALLOW_EXCEPTIONS ) == 0 )
        {
          processUnexpectedResult( result );
        }
        if ( result != TCL.CompletionCode.OK )
        {
          e.setCompletionCode( result );
          throw;
        }
      }
    }
    public void eval( string script )
    {
      eval( script, 0 );
    }
    public void eval( TclObject tobj, int flags )
    {

      eval( tobj.ToString(), flags );
    }
    public void recordAndEval( TclObject script, int flags )
    {
      // Append the script to the event list by calling "history add <script>".
      // We call the eval method with the command of type TclObject, so that
      // we don't have to deal with funny chars ("{}[]$\) in the script.

      TclObject cmd = null;
      try
      {
        cmd = TclList.newInstance();
        TclList.append( this, cmd, TclString.newInstance( "history" ) );
        TclList.append( this, cmd, TclString.newInstance( "add" ) );
        TclList.append( this, cmd, script );
        cmd.preserve();
        eval( cmd, TCL.EVAL_GLOBAL );
      }
      catch ( System.Exception e )
      {
      }
      finally
      {
        cmd.release();
      }

      // Execute the command.

      if ( ( flags & TCL.NO_EVAL ) == 0 )
      {
        eval( script, flags & TCL.EVAL_GLOBAL );
      }
    }
    public void evalFile( string sFilename )
    {
      string fileContent; // Contains the content of the file.

      fileContent = readScriptFromFile( sFilename );

      if ( (System.Object)fileContent == null )
      {
        throw new TclException( this, "couldn't read file \"" + sFilename + "\"" );
      }

      string oldScript = scriptFile;
      scriptFile = sFilename;

      try
      {
        pushDebugStack( sFilename, 1 );
        eval( fileContent, 0 );
      }
      catch ( TclException e )
      {
        if ( e.getCompletionCode() == TCL.CompletionCode.ERROR )
        {
          addErrorInfo( "\n    (file \"" + sFilename + "\" line " + errorLine + ")" );
        }
        throw;
      }
      finally
      {
        scriptFile = oldScript;
        popDebugStack();
      }
    }
    internal void evalURL( System.Uri context, string s )
    {
      string fileContent; // Contains the content of the file.

      fileContent = readScriptFromURL( context, s );
      if ( (System.Object)fileContent == null )
      {
        throw new TclException( this, "cannot read URL \"" + s + "\"" );
      }

      string oldScript = scriptFile;
      scriptFile = s;

      try
      {
        eval( fileContent, 0 );
      }
      finally
      {
        scriptFile = oldScript;
      }
    }
    private string readScriptFromFile( string sFilename )
    // The name of the file.
    {
      System.IO.FileInfo sourceFile;
      System.IO.StreamReader fs;
      try
      {
        sourceFile = FileUtil.getNewFileObj( this, sFilename );
      }
      catch ( TclException e )
      {
        resetResult();
        return null;
      }
      catch ( System.IO.FileNotFoundException e )
      {
        return null;
      }
      catch ( System.Security.SecurityException sec_e )
      {
        return null;
      }
      try
      {
        // HACK only UTF8 will be read
        using ( fs = new System.IO.StreamReader( sourceFile.FullName, System.Text.Encoding.UTF8 ) )
        {
          // read all an do the new line conversations
          return fs.ReadToEnd().Replace( "\r\n", "\n" );
        }
      }
      catch ( System.IO.IOException )
      {
        return null;
      }
    }
    private string readScriptFromURL( System.Uri context, string s )
    {
      Object content = null;
      System.Uri url;

      try
      {
        url = new System.Uri( context, s );
      }
      catch ( System.UriFormatException e )
      {
        return null;
      }

      try
      {

        // ATK content = url.getContent();
        content = url.ToString();
      }
      catch ( System.Exception e )
      {
        Type jar_class;

        try
        {
          jar_class = System.Type.GetType( "java.net.JarURLConnection" );
        }
        catch ( System.Exception e2 )
        {
          return null;
        }

        Object jar;
        try
        {
          jar = (System.Net.HttpWebRequest)System.Net.WebRequest.Create( url );
        }
        catch ( System.IO.IOException e2 )
        {
          return null;
        }

        if ( jar == null )
        {
          return null;
        }

        // We must call JarURLConnection.getInputStream() dynamically
        // Because the class JarURLConnection does not exist in JDK1.1

        try
        {
          System.Reflection.MethodInfo m = jar_class.GetMethod( "openConnection", (System.Type[])null );
          content = m.Invoke( jar, (System.Object[])null );
        }
        catch ( System.Exception e2 )
        {
          return null;
        }
      }
      // HACK
      //			catch (System.IO.IOException e)
      //			{
      //				return null;
      //			}
      //			catch (System.Security.SecurityException e)
      //			{
      //				return null;
      //			}

      if ( content is string )
      {
        return (string)content;
      }
      else if ( content is System.IO.Stream )
      {
        // FIXME : use custom stream handler
        System.IO.Stream fs = (System.IO.Stream)content;

        try
        {
          // FIXME : read does not check return values
          long available;
          available = fs.Length - fs.Position;
          byte[] charArray = new byte[(int)available];
          SupportClass.ReadInput( fs, ref charArray, 0, charArray.Length );
          return new string( SupportClass.ToCharArray( charArray ) );
        }
        catch ( System.IO.IOException e2 )
        {
          return null;
        }
        finally
        {
          closeInputStream( fs );
        }
      }
      else
      {
        return null;
      }
    }
    private void closeInputStream( System.IO.Stream fs )
    {
      try
      {
        fs.Close();
      }
      catch ( System.IO.IOException e )
      {
        ;
      }
    }
    internal void evalResource( string resName )
    {
      //			System.IO.Stream stream = null;
      //			
      //			try
      //			{
      //				
      //				stream = typeof(Interp).getResourceAsStream(resName);
      //			}
      //			catch (System.Security.SecurityException e2)
      //			{
      //				// This catch is necessary if Jacl is to work in an applet
      //				// at all. Note that java::new will not work from within Jacl
      //				// in an applet.
      //				
      //				System.Console.Error.WriteLine("evalResource: Ignoring SecurityException, " + "it is likely we are running in an applet: " + "cannot read resource \"" + resName + "\"" + e2);
      //				
      //				return ;
      //			}
      //			
      //			if (stream == null)
      //			{
      //				throw new TclException(this, "cannot read resource \"" + resName + "\"");
      //			}
      //			
      //			try
      //			{
      //				// FIXME : ugly JDK 1.2 only hack
      //				// Ugly workaround for compressed files BUG in JDK1.2
      //				// this bug first showed up in  JDK1.2beta4. I have sent
      //				// a number of emails to Sun but they have deemed this a "feature"
      //				// of 1.2. This is flat out wrong but I do not seem to change thier
      //				// minds. Because of this, there is no way to do non blocking IO
      //				// on a compressed Stream in Java. (mo)
      //				
      //				
      //				if (System_Renamed.getProperty("java.version").StartsWith("1.2") && stream.GetType().FullName.Equals("java.util.zip.ZipFile$1"))
      //				{
      //					
      //					System.IO.MemoryStream baos = new System.IO.MemoryStream(1024);
      //					byte[] buffer = new byte[1024];
      //					int numRead;
      //					
      //					// Read all data from the stream into a resizable buffer
      //					while ((numRead = SupportClass.ReadInput(stream, ref buffer, 0, buffer.Length)) != - 1)
      //					{
      //						baos.Write(SupportClass.ToByteArray(buffer), 0, numRead);
      //					}
      //					
      //					// Convert bytes into a String and eval them
      //					eval(new string(SupportClass.ToCharArray(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())))), 0);
      //				}
      //				else
      //				{
      //					// Other systems do not need the compressed jar hack
      //					
      //					long available;
      //					available = stream.Length - stream.Position;
      //					int num = (int) available;
      //					byte[] byteArray = new byte[num];
      //					int offset = 0;
      //					while (num > 0)
      //					{
      //						int readLen = SupportClass.ReadInput(stream, ref byteArray, offset, num);
      //						offset += readLen;
      //						num -= readLen;
      //					}
      //					
      //					eval(new string(SupportClass.ToCharArray(SupportClass.ToByteArray(byteArray))), 0);
      //				}
      //			}
      //			catch (System.IO.IOException e)
      //			{
      //				return ;
      //			}
      //			finally
      //			{
      //				closeInputStream(stream);
      //			}
    }
    internal static BackSlashResult backslash( string s, int i, int len )
    {
      CharPointer script = new CharPointer( s.Substring( 0, ( len ) - ( 0 ) ) );
      script.index = i;
      return Parser.backslash( script.array, script.index );
    }


    public void setErrorCode( TclObject code )
    // The errorCode object.
    {
      try
      {
        setVar( "errorCode", null, code, TCL.VarFlag.GLOBAL_ONLY );
        errCodeSet = true;
      }
      catch ( TclException excp )
      {
        // Ignore any TclException's, possibly caused by variable traces on
        // the errorCode variable. This is compatible with the behavior of
        // the Tcl C API.
      }
    }


    public void addErrorInfo( string message )
    // The message to record.
    {
      if ( !errInProgress )
      {
        errInProgress = true;

        try
        {

          setVar( "errorInfo", null, getResult().ToString(), TCL.VarFlag.GLOBAL_ONLY );
        }
        catch ( TclException e1 )
        {
          // Ignore (see try-block above).
        }

        // If the errorCode variable wasn't set by the code
        // that generated the error, set it to "NONE".

        if ( !errCodeSet )
        {
          try
          {
            setVar( "errorCode", null, "NONE", TCL.VarFlag.GLOBAL_ONLY );
          }
          catch ( TclException e1 )
          {
            // Ignore (see try-block above).
          }
        }
      }

      try
      {
        setVar( "errorInfo", null, message, TCL.VarFlag.APPEND_VALUE | TCL.VarFlag.GLOBAL_ONLY );
      }
      catch ( TclException e1 )
      {
        // Ignore (see try-block above).
      }
    }
    internal void processUnexpectedResult( TCL.CompletionCode returnCode )
    {
      resetResult();
      if ( returnCode == TCL.CompletionCode.BREAK )
      {
        throw new TclException( this, "invoked \"break\" outside of a loop" );
      }
      else if ( returnCode == TCL.CompletionCode.CONTINUE )
      {
        throw new TclException( this, "invoked \"continue\" outside of a loop" );
      }
      else
      {
        throw new TclException( this, "command returned bad code: " + returnCode );
      }
    }
    public TCL.CompletionCode updateReturnInfo()
    {
      TCL.CompletionCode code;

      code = returnCode;
      returnCode = TCL.CompletionCode.OK;

      if ( code == TCL.CompletionCode.ERROR )
      {
        try
        {
          setVar( "errorCode", null, ( (System.Object)errorCode != null ) ? errorCode : "NONE", TCL.VarFlag.GLOBAL_ONLY );
        }
        catch ( TclException e )
        {
          // An error may happen during a trace to errorCode. We ignore it.
          // This may leave error messages inside Interp.result (which
          // is compatible with Tcl 8.0 behavior.
        }
        errCodeSet = true;

        if ( (System.Object)errorInfo != null )
        {
          try
          {
            setVar( "errorInfo", null, errorInfo, TCL.VarFlag.GLOBAL_ONLY );
          }
          catch ( TclException e )
          {
            // An error may happen during a trace to errorInfo. We
            // ignore it.  This may leave error messages inside
            // Interp.result (which is compatible with Tcl 8.0
            // behavior.
          }
          errInProgress = true;
        }
      }

      return code;
    }
    protected internal CallFrame newCallFrame( Procedure proc, TclObject[] objv )
    {
      return new CallFrame( this, proc, objv );
    }
    protected internal CallFrame newCallFrame()
    {
      return new CallFrame( this );
    }
    internal System.IO.FileInfo getWorkingDir()
    {
      if ( workingDir == null )
      {
        try
        {

          string dirName = getVar( "env", "HOME", 0 ).ToString();
          workingDir = FileUtil.getNewFileObj( this, dirName );
        }
        catch ( TclException e )
        {
          resetResult();
        }
        workingDir = new System.IO.FileInfo( Util.tryGetSystemProperty( "user.home", "." ) );
      }
      return workingDir;
    }
    internal void setWorkingDir( string dirName )
    {
      System.IO.FileInfo dirObj = FileUtil.getNewFileObj( this, dirName );

      //  Use the canonical name of the path, if possible.

      try
      {
        dirObj = new System.IO.FileInfo( dirObj.FullName );
      }
      catch ( System.IO.IOException e )
      {
      }


      if ( System.IO.Directory.Exists( dirObj.FullName ) )
      {
        workingDir = dirObj;
      }
      else
      {
        throw new TclException( this, "couldn't change working directory to \"" + dirObj.Name + "\": no such file or directory" );
      }
    }

    public Notifier getNotifier()
    {
      return notifier;
    }
    public void pkgProvide( string name, string version )
    {
      PackageCmd.pkgProvide( this, name, version );
    }
    public string pkgRequire( string pkgname, string version, bool exact )
    {
      return PackageCmd.pkgRequire( this, pkgname, version, exact );
    }

    /*
    * Debugging API.
    *
    * The following section defines two debugging API functions for
    * logging information about the point of execution of Tcl scripts:
    *
    * - pushDebugStack() is called when a procedure body is
    *       executed, or when a file is source'd.
    *	   - popDebugStack() is called when the flow of control is about
    *       to return from a procedure body, or from a source'd file.
    *
    * Two other API functions are used to determine the current point of
    * execution:
    *
    *	   - getScriptFile() returns the script file current being executed.
    *	   - getArgLineNumber(i) returns the line number of the i-th argument
    *	     of the current command.
    *
    * Note: The point of execution is automatically maintained for
    *       control structures such as while, if, for and foreach,
    *	     as long as they use Interp.eval(argv[?]) to evaluate control
    *	     blocks.
    *	    
    *	     The case and switch commands need to set dbg.cmdLine explicitly
    *	     because they may evaluate control blocks that are not elements
    *	     inside the argv[] array. ** This feature not yet implemented. **
    *
    *	     The proc command needs to call getScriptFile() and
    *       getArgLineNumber(3) to find out the location of the proc
    *       body.
    *
    * The debugging API functions in the Interp class are just dummy stub
    * functions. These functions are usually implemented in a subclass of
    * Interp (e.g. DbgInterp) that has real debugging support.
    *
    */

    protected internal DebugInfo dbg;

    /// <summary> Initialize the debugging information.</summary>
    /// <returns> a DebugInfo object used by Interp in non-debugging mode.
    /// </returns>
    protected internal DebugInfo initDebugInfo()
    {
      return new DebugInfo( null, 1 );
    }

    /// <summary> Add more more level at the top of the debug stack.
    /// 
    /// </summary>
    /// <param name="fileName">the filename for the new stack level
    /// </param>
    /// <param name="lineNumber">the line number at which the execution of the
    /// new stack level begins.
    /// </param>
    internal void pushDebugStack( string fileName, int lineNumber )
    {
      // do nothing.
    }

    /// <summary> Remove the top-most level of the debug stack.</summary>
    internal void popDebugStack()
    {
      // do nothing
    }
    /// <summary> Returns the line number where the given command argument begins. E.g, if
    /// the following command is at line 10:
    /// 
    /// foo {a
    /// b } c
    /// 
    /// getArgLine(0) = 10
    /// getArgLine(1) = 10
    /// getArgLine(2) = 11
    /// 
    /// </summary>
    /// <param name="index">specifies an argument.
    /// </param>
    /// <returns> the line number of the given argument.
    /// </returns>
    internal int getArgLineNumber( int index )
    {
      return 0;
    }
    internal void transferResult( Interp sourceInterp, TCL.CompletionCode result )
    {
      if ( sourceInterp == this )
      {
        return;
      }

      if ( result == TCL.CompletionCode.ERROR )
      {
        TclObject obj;

        // An error occurred, so transfer error information from the source
        // interpreter to the target interpreter.  Setting the flags tells
        // the target interp that it has inherited a partial traceback
        // chain, not just a simple error message.

        if ( !sourceInterp.errAlreadyLogged )
        {
          sourceInterp.addErrorInfo( "" );
        }
        sourceInterp.errAlreadyLogged = true;

        resetResult();

        obj = sourceInterp.getVar( "errorInfo", TCL.VarFlag.GLOBAL_ONLY );
        setVar( "errorInfo", obj, TCL.VarFlag.GLOBAL_ONLY );

        obj = sourceInterp.getVar( "errorCode", TCL.VarFlag.GLOBAL_ONLY );
        setVar( "errorCode", obj, TCL.VarFlag.GLOBAL_ONLY );

        errInProgress = true;
        errCodeSet = true;
      }

      returnCode = result;
      setResult( sourceInterp.getResult() );
      sourceInterp.resetResult();

      if ( result != TCL.CompletionCode.OK )
      {

        throw new TclException( this, getResult().ToString(), result );
      }
    }
    internal void hideCommand( string cmdName, string hiddenCmdToken )
    {
      WrappedCommand cmd;

      if ( deleted )
      {
        // The interpreter is being deleted. Do not create any new
        // structures, because it is not safe to modify the interpreter.
        return;
      }

      // Disallow hiding of commands that are currently in a namespace or
      // renaming (as part of hiding) into a namespace.
      //
      // (because the current implementation with a single global table
      //  and the needed uniqueness of names cause problems with namespaces)
      //
      // we don't need to check for "::" in cmdName because the real check is
      // on the nsPtr below.
      //
      // hiddenCmdToken is just a string which is not interpreted in any way.
      // It may contain :: but the string is not interpreted as a namespace
      // qualifier command name. Thus, hiding foo::bar to foo::bar and then
      // trying to expose or invoke ::foo::bar will NOT work; but if the
      // application always uses the same strings it will get consistent
      // behavior.
      //
      // But as we currently limit ourselves to the global namespace only
      // for the source, in order to avoid potential confusion,
      // lets prevent "::" in the token too.  --dl

      if ( hiddenCmdToken.IndexOf( "::" ) >= 0 )
      {
        throw new TclException( this, "cannot use namespace qualifiers as " + "hidden commandtoken (rename)" );
      }

      // Find the command to hide. An error is returned if cmdName can't
      // be found. Look up the command only from the global namespace.
      // Full path of the command must be given if using namespaces.

      cmd = NamespaceCmd.findCommand( this, cmdName, null, TCL.VarFlag.LEAVE_ERR_MSG | TCL.VarFlag.GLOBAL_ONLY );

      // Check that the command is really in global namespace

      if ( cmd.ns != globalNs )
      {
        throw new TclException( this, "can only hide global namespace commands" + " (use rename then hide)" );
      }

      // Initialize the hidden command table if necessary.

      if ( hiddenCmdTable == null )
      {
        hiddenCmdTable = new Hashtable();
      }

      // It is an error to move an exposed command to a hidden command with
      // hiddenCmdToken if a hidden command with the name hiddenCmdToken already
      // exists.

      if ( hiddenCmdTable.ContainsKey( hiddenCmdToken ) )
      {
        throw new TclException( this, "hidden command named \"" + hiddenCmdToken + "\" already exists" );
      }

      // Nb : This code is currently 'like' a rename to a specialy set apart
      // name table. Changes here and in TclRenameCommand must
      // be kept in synch untill the common parts are actually
      // factorized out.

      // Remove the hash entry for the command from the interpreter command
      // table. This is like deleting the command, so bump its command epoch;
      // this invalidates any cached references that point to the command.

      if ( cmd.table.ContainsKey( cmd.hashKey ) )
      {
        SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
      }

      // Now link the hash table entry with the command structure.
      // We ensured above that the nsPtr was right.

      cmd.table = hiddenCmdTable;
      cmd.hashKey = hiddenCmdToken;
      SupportClass.PutElement( hiddenCmdTable, hiddenCmdToken, cmd );
    }
    internal void exposeCommand( string hiddenCmdToken, string cmdName )
    {
      WrappedCommand cmd;

      if ( deleted )
      {
        // The interpreter is being deleted. Do not create any new
        // structures, because it is not safe to modify the interpreter.
        return;
      }

      // Check that we have a regular name for the command
      // (that the user is not trying to do an expose and a rename
      //  (to another namespace) at the same time)

      if ( cmdName.IndexOf( "::" ) >= 0 )
      {
        throw new TclException( this, "can not expose to a namespace " + "(use expose to toplevel, then rename)" );
      }

      // Get the command from the hidden command table:

      if ( hiddenCmdTable == null || !hiddenCmdTable.ContainsKey( hiddenCmdToken ) )
      {
        throw new TclException( this, "unknown hidden command \"" + hiddenCmdToken + "\"" );
      }
      cmd = (WrappedCommand)hiddenCmdTable[hiddenCmdToken];

      // Check that we have a true global namespace
      // command (enforced by Tcl_HideCommand() but let's double
      // check. (If it was not, we would not really know how to
      // handle it).

      if ( cmd.ns != globalNs )
      {

        // This case is theoritically impossible,
        // we might rather panic() than 'nicely' erroring out ?

        throw new TclException( this, "trying to expose " + "a non global command name space command" );
      }

      // This is the global table
      NamespaceCmd.Namespace ns = cmd.ns;

      // It is an error to overwrite an existing exposed command as a result
      // of exposing a previously hidden command.

      if ( ns.cmdTable.ContainsKey( cmdName ) )
      {
        throw new TclException( this, "exposed command \"" + cmdName + "\" already exists" );
      }

      // Remove the hash entry for the command from the interpreter hidden
      // command table.

      if ( (System.Object)cmd.hashKey != null )
      {
        SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
        cmd.table = ns.cmdTable;
        cmd.hashKey = cmdName;
      }

      // Now link the hash table entry with the command structure.
      // This is like creating a new command, so deal with any shadowing
      // of commands in the global namespace.

      ns.cmdTable.Add( cmdName, cmd );

      // Not needed as we are only in the global namespace
      // (but would be needed again if we supported namespace command hiding)

      // TclResetShadowedCmdRefs(interp, cmdPtr);
    }
    internal void hideUnsafeCommands()
    {
      for ( int ix = 0 ; ix < unsafeCmds.Length ; ix++ )
      {
        try
        {
          hideCommand( unsafeCmds[ix], unsafeCmds[ix] );
        }
        catch ( TclException e )
        {
          if ( !e.Message.StartsWith( "unknown command" ) )
          {
            throw;
          }
        }
      }
    }
    internal TCL.CompletionCode invokeGlobal( TclObject[] objv, int flags )
    {
      CallFrame savedVarFrame = varFrame;

      try
      {
        varFrame = null;
        return invoke( objv, flags );
      }
      finally
      {
        varFrame = savedVarFrame;
      }
    }
    internal TCL.CompletionCode invoke( TclObject[] objv, int flags )
    {
      if ( ( objv.Length < 1 ) || ( objv == null ) )
      {
        throw new TclException( this, "illegal argument vector" );
      }


      string cmdName = objv[0].ToString();
      WrappedCommand cmd;
      TclObject[] localObjv = null;

      if ( ( flags & INVOKE_HIDDEN ) != 0 )
      {

        // We never invoke "unknown" for hidden commands.

        if ( hiddenCmdTable == null || !hiddenCmdTable.ContainsKey( cmdName ) )
        {
          throw new TclException( this, "invalid hidden command name \"" + cmdName + "\"" );
        }
        cmd = (WrappedCommand)hiddenCmdTable[cmdName];
      }
      else
      {
        cmd = NamespaceCmd.findCommand( this, cmdName, null, TCL.VarFlag.GLOBAL_ONLY );
        if ( cmd == null )
        {
          if ( ( flags & INVOKE_NO_UNKNOWN ) == 0 )
          {
            cmd = NamespaceCmd.findCommand( this, "unknown", null, TCL.VarFlag.GLOBAL_ONLY );
            if ( cmd != null )
            {
              localObjv = new TclObject[objv.Length + 1];
              localObjv[0] = TclString.newInstance( "unknown" );
              localObjv[0].preserve();
              for ( int i = 0 ; i < objv.Length ; i++ )
              {
                localObjv[i + 1] = objv[i];
              }
              objv = localObjv;
            }
          }

          // Check again if we found the command. If not, "unknown" is
          // not present and we cannot help, or the caller said not to
          // call "unknown" (they specified TCL_INVOKE_NO_UNKNOWN).

          if ( cmd == null )
          {
            throw new TclException( this, "invalid command name \"" + cmdName + "\"" );
          }
        }
      }

      // Invoke the command procedure. First reset the interpreter's string
      // and object results to their default empty values since they could
      // have gotten changed by earlier invocations.

      resetResult();
      cmdCount++;

      TCL.CompletionCode result = TCL.CompletionCode.OK;
      try
      {
        cmd.cmd.cmdProc( this, objv );
      }
      catch ( TclException e )
      {
        result = e.getCompletionCode();
      }

      // If we invoke a procedure, which was implemented as AutoloadStub,
      // it was entered into the ordinary cmdTable. But here we know
      // for sure, that this command belongs into the hiddenCmdTable.
      // So if we can find an entry in cmdTable with the cmdName, just
      // move it into the hiddenCmdTable.

      if ( ( flags & INVOKE_HIDDEN ) != 0 )
      {
        cmd = NamespaceCmd.findCommand( this, cmdName, null, TCL.VarFlag.GLOBAL_ONLY );
        if ( cmd != null )
        {
          // Basically just do the same as in hideCommand...
          SupportClass.HashtableRemove( cmd.table, cmd.hashKey );
          cmd.table = hiddenCmdTable;
          cmd.hashKey = cmdName;
          SupportClass.PutElement( hiddenCmdTable, cmdName, cmd );
        }
      }

      // If an error occurred, record information about what was being
      // executed when the error occurred.

      if ( ( result == TCL.CompletionCode.ERROR ) && ( ( flags & INVOKE_NO_TRACEBACK ) == 0 ) && !errAlreadyLogged )
      {
        System.Text.StringBuilder ds;

        if ( errInProgress )
        {
          ds = new System.Text.StringBuilder( "\n    while invoking\n\"" );
        }
        else
        {
          ds = new System.Text.StringBuilder( "\n    invoked from within\n\"" );
        }
        for ( int i = 0 ; i < objv.Length ; i++ )
        {

          ds.Append( objv[i].ToString() );
          if ( i < ( objv.Length - 1 ) )
          {
            ds.Append( " " );
          }
          else if ( ds.Length > 100 )
          {
            ds.Append( "..." );
            break;
          }
        }
        ds.Append( "\"" );
        addErrorInfo( ds.ToString() );
        errInProgress = true;
      }

      // Free any locally allocated storage used to call "unknown".

      if ( localObjv != null )
      {
        localObjv[0].release();
      }

      return result;
    }
    internal void allowExceptions()
    {
      evalFlags |= Parser.TCL_ALLOW_EXCEPTIONS;
    }

    internal class ResolverScheme
    {
      private void InitBlock( Interp enclosingInstance )
      {
        this.enclosingInstance = enclosingInstance;
      }
      private Interp enclosingInstance;
      public Interp Enclosing_Instance
      {
        get
        {
          return enclosingInstance;
        }

      }

      internal string name; // Name identifying this scheme.
      internal Resolver resolver;

      internal ResolverScheme( Interp enclosingInstance, string name, Resolver resolver )
      {
        InitBlock( enclosingInstance );
        this.name = name;
        this.resolver = resolver;
      }
    }

    public void addInterpResolver( string name, Resolver resolver )
    // Object to resolve commands/variables.
    {
      IEnumerator enum_Renamed;
      ResolverScheme res;

      //  Look for an existing scheme with the given name.
      //  If found, then replace its rules.

      if ( resolvers != null )
      {
        for ( enum_Renamed = resolvers.GetEnumerator() ; enum_Renamed.MoveNext() ; )
        {
          res = (ResolverScheme)enum_Renamed.Current;
          if ( name.Equals( res.name ) )
          {
            res.resolver = resolver;
            return;
          }
        }
      }

      if ( resolvers == null )
      {
        resolvers = new ArrayList( 10 );
      }

      //  Otherwise, this is a new scheme.  Add it to the FRONT
      //  of the linked list, so that it overrides existing schemes.

      res = new ResolverScheme( this, name, resolver );

      resolvers.Insert( 0, res );
    }
    public Resolver getInterpResolver( string name )
    // Look for a scheme with this name.
    {
      //IEnumerator enum;

      //  Look for an existing scheme with the given name.  If found,
      //  then return pointers to its procedures.

      if ( resolvers != null )
      {
        foreach ( ResolverScheme res in resolvers )
        {
          if ( name.Equals( res.name ) )
          {
            return res.resolver;
          }
        }
      }

      return null;
    }
    internal bool removeInterpResolver( string name )
    // Name of the scheme to be removed.
    {
      ResolverScheme res;
      IEnumerator enum_Renamed;
      bool found = false;

      //  Look for an existing scheme with the given name.

      if ( resolvers != null )
      {
        enum_Renamed = resolvers.GetEnumerator();
        while ( !found && enum_Renamed.MoveNext() )
        {
          res = (ResolverScheme)enum_Renamed.Current;
          if ( name.Equals( res.name ) )
          {
            found = true;
          }
        }
      }

      //  If we found the scheme, delete it.

      if ( found )
      {
        SupportClass.VectorRemoveElement( resolvers, name );
      }

      return found;
    }

  } // end Interp
}
