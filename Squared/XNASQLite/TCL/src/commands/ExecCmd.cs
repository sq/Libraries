/*
* ExecCmd.java --
*
*	This file contains the Jacl implementation of the built-in Tcl "exec"
*	command. The exec command is not available on the Mac.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ExecCmd.java,v 1.8 2002/01/19 00:15:01 mdejong Exp $
*/
using System;
using System.Text;
using System.IO;

namespace tcl.lang
{


  /*
  * This class implements the built-in "exec" command in Tcl.
  */

  class ExecCmd : Command
  {

    /// <summary> Reference to Runtime.exec, null when JDK < 1.3</summary>
    private static System.Reflection.MethodInfo execMethod;

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
    {
      int firstWord; /* Index to the first non-switch arg */
      int argLen = argv.Length; /* No of args to copy to argStrs     */
      int exit; /* denotes exit status of process    */
      int errorBytes = 0; /* number of bytes of process stderr */
      //bool background; /* Indicates a bg process            */
      //bool keepNewline; /* Retains newline in pipline output */
      System.Diagnostics.Process p; /* The exec-ed process               */
      string argStr; /* Conversion of argv to a string    */
      System.Text.StringBuilder sbuf;

      /*
      * Check for a leading "-keepnewline" argument.
      */

      for ( firstWord = 1 ; firstWord < argLen ; firstWord++ )
      {
        argStr = argv[firstWord].ToString();
        if ( ( argStr.Length > 0 ) && ( argStr[0] == '-' ) )
        {
          //if (argStr.Equals("-keepnewline"))
          //{
          //  keepNewline = true;
          //}
          //else 
          if ( argStr.Equals( "--" ) )
          {
            firstWord++;
            break;
          }
          else
          {
            throw new TclException( interp, "bad switch \"" + argStr + "\": must be -keepnewline or --" );
          }
        }
      }

      if ( argLen <= firstWord )
      {
        throw new TclNumArgsException( interp, 1, argv, "?switches? arg ?arg ...?" );
      }


      /*
      * See if the command is to be run in background.
      * Currently this does nothing, it is just for compatibility
      */


      //if (argv[argLen - 1].ToString().Equals("&"))
      //{
      //  argLen--;
      //  background = true;
      //}

      try
      {
        /*
        * It is necessary to perform system specific 
        * operations before calling exec.  For now Solaris
        * and Windows execs are somewhat supported, in all other cases
        * we simply call exec and give it our "best shot"
        */

        if ( execMethod != null )
        {
          p = execReflection( interp, argv, firstWord, argLen );
        }
        else if ( Util.Unix )
        {
          p = execUnix( interp, argv, firstWord, argLen );
        }
        else if ( Util.Windows )
        {
          p = execWin( interp, argv, firstWord, argLen );
        }
        else
        {
          p = execDefault( interp, argv, firstWord, argLen );
        }


        //note to self : buffer reading should be done in
        //a separate thread and not by calling waitFor()
        //because a process that is waited for can block


        //Wait for the process to finish running,
        try
        {
          p.Start();
          p.WaitForExit();
          exit = p.ExitCode;
        }
        catch ( Exception e )
        {
          throw new TclException( interp, "exception in exec process: " + e.Message );
        }


        //Make buffer for the results of the subprocess execution
        sbuf = new System.Text.StringBuilder();

        //read data on stdout stream into  result buffer
        readStreamIntoBuffer( p.StandardOutput.BaseStream, sbuf );

        //if there is data on the stderr stream then append
        //this data onto the result StringBuffer
        //check for the special case where there is no error
        //data but the process returns an error result

        errorBytes = readStreamIntoBuffer( p.StandardError.BaseStream, sbuf );

        if ( ( errorBytes == 0 ) && ( exit != 0 ) )
        {
          sbuf.Append( "child process exited abnormally" );
        }

        //If the last character of the result buffer is a newline, then 
        //remove the newline character (the newline would just confuse 
        //things).  Finally, we set pass the result to the interpreter.



        // Tcl supports lots of child status conditions.
        // Unfortunately, we can only find the child's
        // exit status using the Java API

        if ( exit != 0 )
        {
          TclObject childstatus = TclList.newInstance();
          TclList.append( interp, childstatus, TclString.newInstance( "CHILDSTATUS" ) );

          // We don't know how to find the child's pid
          TclList.append( interp, childstatus, TclString.newInstance( "?PID?" ) );

          TclList.append( interp, childstatus, TclInteger.newInstance( exit ) );

          interp.setErrorCode( childstatus );
        }

        //when the subprocess writes to its stderr stream or returns
        //a non zero result we generate an error
        if ( ( exit != 0 ) || ( errorBytes != 0 ) )
        {
          throw new TclException( interp, sbuf.ToString() );
        }

        //otherwise things went well so set the result
        interp.setResult( sbuf.ToString() );
      }
      catch ( System.IO.IOException e )
      {
        //if exec fails we end up catching the exception here


        throw new TclException( interp, "couldn't execute \"" + argv[firstWord].ToString() + "\": no such file or directory" );
      }
      catch ( System.Threading.ThreadInterruptedException e )
      {
        /*
        * Do Nothing...
        */
      }
      return TCL.CompletionCode.RETURN;
    }


    internal static int readStreamIntoBuffer( System.IO.Stream in_Renamed, System.Text.StringBuilder sbuf )
    {
      int numRead = 0;
      System.IO.StreamReader br = new System.IO.StreamReader( new System.IO.StreamReader( in_Renamed ).BaseStream, System.Text.Encoding.UTF7 );

      try
      {
        string line = br.ReadLine();

        while ( (System.Object)line != null )
        {
          sbuf.Append( line );
          numRead += line.Length;
          sbuf.Append( '\n' );
          numRead++;
          line = br.ReadLine();
        }
      }
      catch ( System.IO.IOException e )
      {
        //do nothing just return numRead
      }
      finally
      {
        try
        {
          br.Close();
        }
        catch ( System.IO.IOException e )
        {
        } //ignore IO error
      }

      return numRead;
    }


    internal static string escapeWinString( string str )
    {
      if ( str.IndexOf( (System.Char)'%' ) == -1 )
        return str;

      char[] arr = str.ToCharArray();
      System.Text.StringBuilder sb = new System.Text.StringBuilder( 50 );

      for ( int i = 0 ; i < arr.Length ; i++ )
      {
        if ( arr[i] == '%' )
        {
          sb.Append( '%' );
        }
        sb.Append( arr[i] );
      }

      return sb.ToString();
    }


    private System.Diagnostics.Process execUnix( Interp interp, TclObject[] argv, int first, int last )
    {
      return execWin( interp, argv, first, last );
    }

    private System.Diagnostics.Process execWin( Interp interp, TclObject[] argv, int first, int last )
    {
      StringBuilder sb = new StringBuilder();
      for ( int i = ( first + 1 ) ; i < last ; i++ )
      {
        sb.Append( '"' );
        sb.Append( escapeWinString( argv[i].ToString() ) );
        sb.Append( '"' );
        sb.Append( ' ' );
      }

      System.Diagnostics.Process proc = new System.Diagnostics.Process();
      proc.StartInfo.UseShellExecute = false;
      proc.StartInfo.RedirectStandardOutput = true;
      proc.StartInfo.RedirectStandardError = true;
      proc.StartInfo.RedirectStandardInput = true;
      proc.StartInfo.WorkingDirectory = interp.getWorkingDir().FullName;
      proc.StartInfo.FileName = argv[first].ToString();
      proc.StartInfo.Arguments = sb.ToString();
      return proc;
    }

    private System.Diagnostics.Process execDefault( Interp interp, TclObject[] argv, int first, int last )
    {
      return execWin( interp, argv, first, last );
    }

    private System.Diagnostics.Process execReflection( Interp interp, TclObject[] argv, int first, int last )
    {

      string[] strv = new string[last - first];

      for ( int i = first, j = 0 ; i < last ; j++, i++ )
      {

        strv[j] = argv[i].ToString();
      }

      System.Object[] methodArgs = new System.Object[3];
      methodArgs[0] = strv; // exec command arguments
      methodArgs[1] = null; // inherit all environment variables
      methodArgs[2] = interp.getWorkingDir();

      try
      {
        return (System.Diagnostics.Process)execMethod.Invoke( System.Diagnostics.Process.GetCurrentProcess(), (System.Object[])methodArgs );
      }
      catch ( System.UnauthorizedAccessException ex )
      {
        throw new TclRuntimeError( "IllegalAccessException in execReflection" );
      }
      catch ( System.ArgumentException ex )
      {
        throw new TclRuntimeError( "IllegalArgumentException in execReflection" );
      }
      catch ( System.Reflection.TargetInvocationException ex )
      {
                System.Exception t = ex.GetBaseException();

        if ( t is System.ApplicationException )
        {
          throw (System.ApplicationException)t;
        }
        else if ( t is System.IO.IOException )
        {
          throw (System.IO.IOException)t;
        }
        else
        {
          throw new TclRuntimeError( "unexected exception in execReflection" );
        }
      }
    }
    static ExecCmd()
    {
      {
        // Runtime.exec(String[] cmdArr, String[] envArr, File currDir)
        System.Type[] parameterTypes = new System.Type[] { typeof( string[] ), typeof( string[] ), typeof( System.IO.FileInfo ) };
        try
        {
          execMethod = System.Diagnostics.Process.GetCurrentProcess().GetType().GetMethod( "exec", (System.Type[])parameterTypes );
        }
        catch ( System.MethodAccessException e )
        {
          execMethod = null;
        }
      }
    }
  } // end ExecCmd
}
