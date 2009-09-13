/*
* Extension.java --
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Extension.java,v 1.2 1999/05/09 21:18:54 dejong Exp $
*
*/
using System;
namespace tcl.lang
{

  /// <summary> Base class for all Tcl Extensions. A Tcl Extension defines a set of
  /// commands that can be loaded into an Interp as a single unit.
  /// 
  /// When a Tcl Extension is loaded into an Interp, either statically
  /// (using the "new" operator inside Java code) or dynamically (using
  /// the java::load command in Tcl scripts), it usually creates a set of
  /// commands inside the interpreter. Occasionally, loading an Extension
  /// may lead to additional side effects. For example, a communications
  /// Extension may open network connections when it's loaded. Please
  /// refer to the documentation of the specific Extension for details.
  /// </summary>

  abstract public class Extension
  {

    /// <summary> Default constructor. Does nothing. The purpose of this
    /// constructor is to make sure instances of this Extension can be
    /// loaded dynamically using the "java::load" command, which calls
    /// Class.newInstance().
    /// </summary>

    public Extension()
    {
    }

    /// <summary> Initialize the Extension to run in a normal (unsafe)
    /// interpreter. This usually means creating all the commands
    /// provided by this class. A particular implementation can arrange
    /// the commands to be loaded on-demand using the loadOnDemand()
    /// function.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>

    abstract public void init( Interp interp );

    /// <summary> Initialize the Extension to run in a safe interpreter.  This
    /// method should be written carefully, so that it initializes the
    /// safe interpreter only with partial functionality provided by
    /// the Extension that is safe for use by untrusted code.
    /// 
    /// The default implementation always throws a TclException, so that
    /// a subclass of Extension cannot be loaded into a safe interpreter
    /// unless it has overridden the safeInit() method.
    /// 
    /// </summary>
    /// <param name="safeInterp">the safe interpreter in which the Extension should
    /// be initialized.
    /// </param>

    public void safeInit( Interp safeInterp )
    {
      throw new TclException( safeInterp, "Extension \"" + GetType().ToString() + "\" cannot be loaded into a safe interpreter" );
    }

    /// <summary> Create a stub command which autoloads the real command the first time
    /// the stub command is invoked. Register the stub command in the	
    /// interpreter.
    /// 
    /// </summary>
    /// <param name="interp">current interp.
    /// </param>
    /// <param name="cmdName">name of the command, e.g., "after".
    /// </param>
    /// <param name="clsName">name of the Java class that implements this command,
    /// e.g. "tcl.lang.AfterCmd"
    /// </param>

    public static void loadOnDemand( Interp interp, string cmdName, string clsName )
    {
      interp.createCommand( cmdName, new AutoloadStub( clsName ) );
    }
  }

  /// <summary> The purpose of AutoloadStub is to load-on-demand the classes that
  /// implement Tcl commands. This reduces Jacl start up time and, when
  /// running Jacl off a web page, reduces download time significantly.
  /// </summary>

  class AutoloadStub : Command
  {
    internal string className;

    /// <summary> Create a stub command which autoloads the real command the first time
    /// the stub command is invoked.
    /// 
    /// </summary>
    /// <param name="clsName">name of the Java class that implements this command,
    /// e.g. "tcl.lang.AfterCmd"
    /// </param>
    internal AutoloadStub( string clsName )
    {
      className = clsName;
    }

    /// <summary> Load the class that implements the given command and execute it.
    /// 
    /// </summary>
    /// <param name="interp">the current interpreter.
    /// </param>
    /// <param name="argv">command arguments.
    /// </param>
    /// <exception cref=""> TclException if error happens inside the real command proc.
    /// </exception>
    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
    {
      Type cmdClass = null;
      Command cmd;
      try
      {
        cmdClass = System.Type.GetType( className, true );
      }
      catch ( System.Exception e )
      {
        throw new TclException( interp, "ClassNotFoundException for class \"" + className + "\"" );
      }

      try
      {
        cmd = (Command)SupportClass.CreateNewInstance( cmdClass );
      }
      catch ( System.UnauthorizedAccessException e1 )
      {
        throw new TclException( interp, "IllegalAccessException for class \"" + cmdClass.FullName + "\"" );
      }
      catch ( System.InvalidCastException e3 )
      {
        throw new TclException( interp, "ClassCastException for class \"" + cmdClass.FullName + "\"" );
      }
      catch ( System.Exception e2 )
      {
        throw new TclException( interp, "InstantiationException for class \"" + cmdClass.FullName + "\"" );
      }

      interp.createCommand( argv[0].ToString(), cmd );
      cmd.cmdProc( interp, argv );
      return TCL.CompletionCode.RETURN;
    }
  }
}
