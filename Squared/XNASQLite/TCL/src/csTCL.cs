using System;
using System.Collections.Generic;
using System.Text;
using tcl.lang;
using System.Reflection;

class csTCL
{
  /*
  ** 2009 July 17
  **
  ** The author disclaims copyright to this source code.  In place of
  ** a legal notice, here is a blessing:
  **
  **    May you do good and not evil.
  **    May you find forgiveness for yourself and forgive others.
  **    May you share freely, never taking more than you give.
  **
  *************************************************************************
  ** This file contains code to implement the "sqlite" test harness
  ** which runs TCL commands for testing the C#-SQLite port.
  **
  ** $Header$
  */
  public static void Main(string[] args)
  {
    // Array of command-line argument strings.
    {
      string fileName = null;

      // Create the interpreter. This will also create the built-in
      // Tcl commands.

      Interp interp = new Interp();

      // Make command-line arguments available in the Tcl variables "argc"
      // and "argv".  If the first argument doesn't start with a "-" then
      // strip it off and use it as the name of a script file to process.
      // We also set the argv0 and TCL.Tcl_interactive vars here.

      if ((args.Length > 0) && !(args[0].StartsWith("-")))
      {
        fileName = args[0];
      }

      TclObject argv = TclList.newInstance();
      argv.preserve();
      try
      {
        int i = 0;
        int argc = args.Length;
        if ((System.Object)fileName == null)
        {
          interp.setVar("argv0", "tcl.lang.Shell", TCL.VarFlag.GLOBAL_ONLY);
          interp.setVar("tcl_interactive", "1", TCL.VarFlag.GLOBAL_ONLY);
        }
        else
        {
          interp.setVar("argv0", fileName, TCL.VarFlag.GLOBAL_ONLY);
          interp.setVar("tcl_interactive", "0", TCL.VarFlag.GLOBAL_ONLY);
          i++;
          argc--;
        }
        for (; i < args.Length; i++)
        {
          TclList.append(interp, argv, TclString.newInstance(args[i]));
        }
        interp.setVar("argv", argv, TCL.VarFlag.GLOBAL_ONLY);
        interp.setVar("argc", System.Convert.ToString(argc), TCL.VarFlag.GLOBAL_ONLY);
      }
      catch (TclException e)
      {
        throw new TclRuntimeError("unexpected TclException: " + e.Message);
      }
      finally
      {
        argv.release();
      }

      // Normally we would do application specific initialization here.
      // However, that feature is not currently supported.
      // If a script file was specified then just source that file
      // and quit.

      Console.WriteLine("C#-TCL version " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
      Console.WriteLine("==============================================================");
      Console.WriteLine("");

      if ((System.Object)fileName != null)
      {
        try
        {
          interp.evalFile(fileName);
        }
        catch (TclException e)
        {
          TCL.CompletionCode code = e.getCompletionCode();
          if (code == TCL.CompletionCode.RETURN)
          {
            code = interp.updateReturnInfo();
            if (code != TCL.CompletionCode.OK)
            {
              System.Console.Error.WriteLine("command returned bad code: " + code);
              if (tcl.lang.ConsoleThread.debug) System.Diagnostics.Debug.WriteLine("command returned bad code: " + code);
            }
          }
          else if (code == TCL.CompletionCode.ERROR)
          {
            System.Console.Error.WriteLine(interp.getResult().ToString());
            if (tcl.lang.ConsoleThread.debug) System.Diagnostics.Debug.WriteLine(interp.getResult().ToString());
            System.Diagnostics.Debug.Assert(false, interp.getResult().ToString());
          }
          else
          {
            System.Console.Error.WriteLine("command returned bad code: " + code);
            if (tcl.lang.ConsoleThread.debug) System.Diagnostics.Debug.WriteLine("command returned bad code: " + code);
          }
        }

        // Note that if the above interp.evalFile() returns the main
        // thread will exit.  This may bring down the VM and stop
        // the execution of Tcl.
        //
        // If the script needs to handle events, it must call
        // vwait or do something similar.
        //
        // Note that the script can create AWT widgets. This will
        // start an AWT event handling thread and keep the VM up. However,
        // the interpreter thread (the same as the main thread) would
        // have exited and no Tcl scripts can be executed.

        interp.dispose();

        System.Environment.Exit(0);
      }

      if ((System.Object)fileName == null)
      {
        // We are running in interactive mode. Start the ConsoleThread
        // that loops, grabbing stdin and passing it to the interp.

        ConsoleThread consoleThread = new ConsoleThread(interp);
        consoleThread.IsBackground = true;
        consoleThread.Start();

        // Loop forever to handle user input events in the command line.

        Notifier notifier = interp.getNotifier();
        while (true)
        {
          // process events until "exit" is called.

          notifier.doOneEvent(TCL.ALL_EVENTS);
        }
      }
    }
  }
}

namespace tcl.lang
{
  class ConsoleThread : SupportClass.ThreadClass
  {
    private class AnonymousClassTclEvent : TclEvent
    {
      public AnonymousClassTclEvent(string command, ConsoleThread enclosingInstance)
      {
        InitBlock(command, enclosingInstance);
      }
      private void InitBlock(string command, ConsoleThread enclosingInstance)
      {
        this.command = command;
        this.enclosingInstance = enclosingInstance;
      }
      private string command;
      private ConsoleThread enclosingInstance;
      public ConsoleThread Enclosing_Instance
      {
        get
        {
          return enclosingInstance;
        }

      }
      public override int processEvent(int flags)
      {

        // See if the command is a complete Tcl command

        if (Interp.commandComplete(command))
        {
          if (tcl.lang.ConsoleThread.debug)
          {
            WriteLine("line was a complete command");
          }

          bool eval_exception = true;
          TclObject commandObj = TclString.newInstance(command);

          try
          {
            commandObj.preserve();
            Enclosing_Instance.interp.recordAndEval(commandObj, 0);
            eval_exception = false;
          }
          catch (TclException e)
          {
            if (tcl.lang.ConsoleThread.debug)
            {
              WriteLine("eval returned exceptional condition");
            }

            TCL.CompletionCode code = e.getCompletionCode();
            switch (code)
            {

              case TCL.CompletionCode.ERROR:

                Enclosing_Instance.putLine(Enclosing_Instance.err, Enclosing_Instance.interp.getResult().ToString());
                break;

              case TCL.CompletionCode.BREAK:
                Enclosing_Instance.putLine(Enclosing_Instance.err, "invoked \"break\" outside of a loop");
                break;

              case TCL.CompletionCode.CONTINUE:
                Enclosing_Instance.putLine(Enclosing_Instance.err, "invoked \"continue\" outside of a loop");
                break;

              default:
                Enclosing_Instance.putLine(Enclosing_Instance.err, "command returned bad code: " + code);
                break;

            }
          }
          finally
          {
            commandObj.release();
          }

          if (!eval_exception)
          {
            if (tcl.lang.ConsoleThread.debug)
            {
              WriteLine("eval returned normally");
            }


            string evalResult = Enclosing_Instance.interp.getResult().ToString();

            if (tcl.lang.ConsoleThread.debug)
            {
              WriteLine("eval result was \"" + evalResult + "\"");
            }

            if (evalResult.Length > 0)
            {
              Enclosing_Instance.putLine(Enclosing_Instance.out_Renamed, evalResult);
            }
          }

          // Empty out the incoming command buffer
          Enclosing_Instance.sbuf.Length = 0;

          // See if the user set a custom shell prompt for the next command

          TclObject prompt;

          try
          {
            prompt = Enclosing_Instance.interp.getVar("tcl_prompt1", TCL.VarFlag.GLOBAL_ONLY);
          }
          catch (TclException e)
          {
            prompt = null;
          }
          if (prompt != null)
          {
            try
            {

              Enclosing_Instance.interp.eval(prompt.ToString(), TCL.EVAL_GLOBAL);
            }
            catch (TclException e)
            {
              Enclosing_Instance.put(Enclosing_Instance.out_Renamed, "% ");
            }
          }
          else
          {
            Enclosing_Instance.put(Enclosing_Instance.out_Renamed, "% ");
          }

          return 1;
        }
        else
        {
          // Interp.commandComplete() returned false

          if (tcl.lang.ConsoleThread.debug)
          {
            WriteLine("line was not a complete command");
          }

          // We don't have a complete command yet. Print out a level 2
          // prompt message and wait for further inputs.

          TclObject prompt;

          try
          {
            prompt = Enclosing_Instance.interp.getVar("tcl_prompt2", TCL.VarFlag.GLOBAL_ONLY);
          }
          catch (TclException)
          {
            prompt = null;
          }
          if (prompt != null)
          {
            try
            {

              Enclosing_Instance.interp.eval(prompt.ToString(), TCL.EVAL_GLOBAL);
            }
            catch (TclException e)
            {
              Enclosing_Instance.put(Enclosing_Instance.out_Renamed, "");
            }
          }
          else
          {
            Enclosing_Instance.put(Enclosing_Instance.out_Renamed, "");
          }

          return 1;
        }
      } // end processEvent method
    }

    // Interpreter associated with this console thread.

    internal Interp interp;

    // Collect the user input in this buffer until it forms a complete Tcl
    // command.

    internal System.Text.StringBuilder sbuf;

    // Used to for interactive input/output

    private Channel out_Renamed;
    private Channel err;

    // set to true to get extra debug output
    public const bool debug = true;

    // used to keep track of wether or not System.in.available() works
    private static bool sysInAvailableWorks = false;

    internal ConsoleThread(Interp i)
    {
      Name = "ConsoleThread";
      interp = i;
      sbuf = new System.Text.StringBuilder(100);

      out_Renamed = TclIO.getStdChannel(StdChannel.STDOUT);
      err = TclIO.getStdChannel(StdChannel.STDERR);
    }
    override public void Run()
    {
      if (debug)
      {
        WriteLine("entered ConsoleThread run() method");
      }


      put(out_Renamed, "% ");

      while (true)
      {
        // Loop forever to collect user inputs in a StringBuffer.
        // When we have a complete command, then execute it and print
        // out the results.
        //
        // The loop is broken under two conditions: (1) when EOF is
        // received inside getLine(). (2) when the "exit" command is
        // executed in the script.

        getLine();
        string command = sbuf.ToString();

        if (debug)
        {
          WriteLine("got line from console");
          WriteLine("\"" + command + "\"");
        }

        // When interacting with the interpreter, one must
        // be careful to never call a Tcl method from
        // outside of the event loop thread. If we did
        // something like just call interp.eval() it
        // could crash the whole process because two
        // threads might write over each other.

        // The only safe way to interact with Tcl is
        // to create an event and add it to the thread
        // safe event queue.

        TclEvent Tevent = new AnonymousClassTclEvent(command, this); // end TclEvent innerclass

        // Add the event to the thread safe event queue
        interp.getNotifier().queueEvent(Tevent, TCL.QUEUE_TAIL);

        // Tell this thread to wait until the event has been processed.
        Tevent.sync();
      }
    }
    private static void WriteLine(string s)
    {
      System.Console.Out.WriteLine(s);
      if (debug) System.Diagnostics.Debug.WriteLine(s);
    }
    private void getLine()
    {
      sbuf.Append(Console.In.ReadLine());
    }
    private void putLine(Channel channel, string s)
    // The String to print.
    {
      try
      {
        channel.write(interp, s);
        channel.write(interp, "\n");
        channel.flush(interp);
      }
      catch (System.IO.IOException ex)
      {
        System.Console.Error.WriteLine("IOException in Shell.putLine()");
        SupportClass.WriteStackTrace(ex, System.Console.Error);
      }
      catch (TclException ex)
      {
        System.Console.Error.WriteLine("TclException in Shell.putLine()");
        SupportClass.WriteStackTrace(ex, System.Console.Error);
      }
    }
    private void put(Channel channel, string s)
    // The String to print.
    {
      try
      {
        channel.write(interp, s);
        channel.flush(interp);
      }
      catch (System.IO.IOException ex)
      {
        System.Console.Error.WriteLine("IOException in Shell.put()");
        SupportClass.WriteStackTrace(ex, System.Console.Error);
      }
      catch (TclException ex)
      {
        System.Console.Error.WriteLine("TclException in Shell.put()");
        SupportClass.WriteStackTrace(ex, System.Console.Error);
      }
    }
    static ConsoleThread()
    {
      {
        try
        {
          // There is no way to tell whether System.in will block AWT
          // threads, so we assume it does block if we can use
          // System.in.available().

          long available = 0;
          // HACK ATK
          // available = System.Console.In.Length - System.Console.In.Position;
          int generatedAux5 = (int)available;
          sysInAvailableWorks = true;
        }
        catch (System.Exception e)
        {
          // If System.in.available() causes an exception -- it's probably
          // no supported on this platform (e.g. MS Java SDK). We assume
          // sysInAvailableWorks is false and let the user suffer ...
        }

        // Sun's JDK 1.2 on Windows systems is screwed up, it does not
        // echo chars to the console unless blocking IO is used.
        // For this reason we need to use blocking IO under Windows.

        if (Util.Windows)
        {
          sysInAvailableWorks = false;
        }
        if (debug)
        {
          WriteLine("sysInAvailableWorks = " + sysInAvailableWorks);
        }
      }
    }
  } // end of class ConsoleThread
}
