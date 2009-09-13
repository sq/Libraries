/*
* InterpCmd.java --
*
*	Implements the built-in "interp" Tcl command.
*
* Copyright (c) 2000 Christian Krone.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: InterpCmd.java,v 1.1 2000/08/20 06:08:43 mo Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "interp" command in Tcl.</summary>
	
	class InterpCmd : Command
	{
		
		private static readonly string[] options = new string[]{"alias", "aliases", "create", "delete", "eval", "exists", "expose", "hide", "hidden", "issafe", "invokehidden", "marktrusted", "slaves", "share", "target", "transfer"};
		private const int OPT_ALIAS = 0;
		private const int OPT_ALIASES = 1;
		private const int OPT_CREATE = 2;
		private const int OPT_DELETE = 3;
		private const int OPT_EVAL = 4;
		private const int OPT_EXISTS = 5;
		private const int OPT_EXPOSE = 6;
		private const int OPT_HIDE = 7;
		private const int OPT_HIDDEN = 8;
		private const int OPT_ISSAFE = 9;
		private const int OPT_INVOKEHIDDEN = 10;
		private const int OPT_MARKTRUSTED = 11;
		private const int OPT_SLAVES = 12;
		private const int OPT_SHARE = 13;
		private const int OPT_TARGET = 14;
		private const int OPT_TRANSFER = 15;
		
		private static readonly string[] createOptions = new string[]{"-safe", "--"};
		private const int OPT_CREATE_SAFE = 0;
		private const int OPT_CREATE_LAST = 1;
		
		private static readonly string[] hiddenOptions = new string[]{"-global", "--"};
		private const int OPT_HIDDEN_GLOBAL = 0;
		private const int OPT_HIDDEN_LAST = 1;
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "cmd ?arg ...?");
			}
			int cmd = TclIndex.get(interp, objv[1], options, "option", 0);
			
			switch (cmd)
			{
				
				case OPT_ALIAS:  {
						if (objv.Length >= 4)
						{
							Interp slaveInterp = getInterp(interp, objv[2]);
							
							if (objv.Length == 4)
							{
								InterpAliasCmd.describe(interp, slaveInterp, objv[3]);
                return TCL.CompletionCode.RETURN;
              }
							
							if ((objv.Length == 5) && ("".Equals(objv[4].ToString())))
							{
								InterpAliasCmd.delete(interp, slaveInterp, objv[3]);
                return TCL.CompletionCode.RETURN;
              }
							if (objv.Length > 5)
							{
								Interp masterInterp = getInterp(interp, objv[4]);
								
								if ("".Equals(objv[5].ToString()))
								{
									if (objv.Length == 6)
									{
										InterpAliasCmd.delete(interp, slaveInterp, objv[3]);
                    return TCL.CompletionCode.RETURN;
                  }
								}
								else
								{
									InterpAliasCmd.create(interp, slaveInterp, masterInterp, objv[3], objv[5], 6, objv);
                  return TCL.CompletionCode.RETURN;
                }
							}
						}
						throw new TclNumArgsException(interp, 2, objv, "slavePath slaveCmd ?masterPath masterCmd? ?args ..?");
					}
				
				case OPT_ALIASES:  {
						Interp slaveInterp = getInterp(interp, objv);
						InterpAliasCmd.list(interp, slaveInterp);
						break;
					}
				
				case OPT_CREATE:  {
						
						// Weird historical rules: "-safe" is accepted at the end, too.
						
						bool safe = interp.isSafe;
						
						TclObject slaveNameObj = null;
						bool last = false;
						for (int i = 2; i < objv.Length; i++)
						{
							
							if ((!last) && (objv[i].ToString()[0] == '-'))
							{
								int index = TclIndex.get(interp, objv[i], createOptions, "option", 0);
								if (index == OPT_CREATE_SAFE)
								{
									safe = true;
									continue;
								}
								i++;
								last = true;
							}
							if (slaveNameObj != null)
							{
								throw new TclNumArgsException(interp, 2, objv, "?-safe? ?--? ?path?");
							}
							slaveNameObj = objv[i];
						}
						if (slaveNameObj == null)
						{
							
							// Create an anonymous interpreter -- we choose its name and
							// the name of the command. We check that the command name
							// that we use for the interpreter does not collide with an
							// existing command in the master interpreter.
							
							int i = 0;
							while (interp.getCommand("interp" + i) != null)
							{
								i++;
							}
							slaveNameObj = TclString.newInstance("interp" + i);
						}
						InterpSlaveCmd.create(interp, slaveNameObj, safe);
						interp.setResult(slaveNameObj);
						break;
					}
				
				case OPT_DELETE:  {
						for (int i = 2; i < objv.Length; i++)
						{
							Interp slaveInterp = getInterp(interp, objv[i]);
							
							if (slaveInterp == interp)
							{
								throw new TclException(interp, "cannot delete the current interpreter");
							}
							InterpSlaveCmd slave = slaveInterp.slave;
							slave.masterInterp.deleteCommandFromToken(slave.interpCmd);
						}
						break;
					}
				
				case OPT_EVAL:  {
						if (objv.Length < 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "path arg ?arg ...?");
						}
						Interp slaveInterp = getInterp(interp, objv[2]);
						InterpSlaveCmd.eval(interp, slaveInterp, 3, objv);
						break;
					}
				
				case OPT_EXISTS:  {
						bool exists = true;
						
						try
						{
							getInterp(interp, objv);
						}
						catch (TclException e)
						{
							if (objv.Length > 3)
							{
								throw ;
							}
							exists = false;
						}
						interp.setResult(exists);
						break;
					}
				
				case OPT_EXPOSE:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "path hiddenCmdName ?cmdName?");
						}
						Interp slaveInterp = getInterp(interp, objv[2]);
						InterpSlaveCmd.expose(interp, slaveInterp, 3, objv);
						break;
					}
				
				case OPT_HIDE:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "path cmdName ?hiddenCmdName?");
						}
						Interp slaveInterp = getInterp(interp, objv[2]);
						InterpSlaveCmd.hide(interp, slaveInterp, 3, objv);
						break;
					}
				
				case OPT_HIDDEN:  {
						Interp slaveInterp = getInterp(interp, objv);
						InterpSlaveCmd.hidden(interp, slaveInterp);
						break;
					}
				
				case OPT_ISSAFE:  {
						Interp slaveInterp = getInterp(interp, objv);
						interp.setResult(slaveInterp.isSafe);
						break;
					}
				
				case OPT_INVOKEHIDDEN:  {
						bool global = false;
						int i;
						for (i = 3; i < objv.Length; i++)
						{
							
							if (objv[i].ToString()[0] != '-')
							{
								break;
							}
							int index = TclIndex.get(interp, objv[i], hiddenOptions, "option", 0);
							if (index == OPT_HIDDEN_GLOBAL)
							{
								global = true;
							}
							else
							{
								i++;
								break;
							}
						}
						if (objv.Length - i < 1)
						{
							throw new TclNumArgsException(interp, 2, objv, "path ?-global? ?--? cmd ?arg ..?");
						}
						Interp slaveInterp = getInterp(interp, objv[2]);
						InterpSlaveCmd.invokeHidden(interp, slaveInterp, global, i, objv);
						break;
					}
				
				case OPT_MARKTRUSTED:  {
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "path");
						}
						Interp slaveInterp = getInterp(interp, objv[2]);
						InterpSlaveCmd.markTrusted(interp, slaveInterp);
						break;
					}
				
				case OPT_SLAVES:  {
						Interp slaveInterp = getInterp(interp, objv);
						
						TclObject result = TclList.newInstance();
						interp.setResult(result);
						
						IEnumerator keys = slaveInterp.slaveTable.Keys.GetEnumerator();
						while (keys.MoveNext())
						{
							string inString = (string) keys.Current;
							TclList.append(interp, result, TclString.newInstance(inString));
						}
						
						break;
					}
				
				case OPT_SHARE:  {
						if (objv.Length != 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "srcPath channelId destPath");
						}
						Interp masterInterp = getInterp(interp, objv[2]);
						
						
						Channel chan = TclIO.getChannel(masterInterp, objv[3].ToString());
						if (chan == null)
						{
							
							throw new TclException(interp, "can not find channel named \"" + objv[3].ToString() + "\"");
						}
						
						Interp slaveInterp = getInterp(interp, objv[4]);
						TclIO.registerChannel(slaveInterp, chan);
						break;
					}
				
				case OPT_TARGET:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "path alias");
						}
						
						Interp slaveInterp = getInterp(interp, objv[2]);
						
						string aliasName = objv[3].ToString();
						Interp targetInterp = InterpAliasCmd.getTargetInterp(slaveInterp, aliasName);
						if (targetInterp == null)
						{
							
							throw new TclException(interp, "alias \"" + aliasName + "\" in path \"" + objv[2].ToString() + "\" not found");
						}
						if (!getInterpPath(interp, targetInterp))
						{
							
							throw new TclException(interp, "target interpreter for alias \"" + aliasName + "\" in path \"" + objv[2].ToString() + "\" is not my descendant");
						}
						break;
					}
				
				case OPT_TRANSFER:  {
						if (objv.Length != 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "srcPath channelId destPath");
						}
						Interp masterInterp = getInterp(interp, objv[2]);
						
						
						Channel chan = TclIO.getChannel(masterInterp, objv[3].ToString());
						if (chan == null)
						{
							
							throw new TclException(interp, "can not find channel named \"" + objv[3].ToString() + "\"");
						}
						
						Interp slaveInterp = getInterp(interp, objv[4]);
						TclIO.registerChannel(slaveInterp, chan);
						TclIO.unregisterChannel(masterInterp, chan);
						break;
					}
				}
        return TCL.CompletionCode.RETURN;
      }
		private static Interp getInterp(Interp interp, TclObject[] objv)
		{
			if (objv.Length == 2)
			{
				return interp;
			}
			else if (objv.Length == 3)
			{
				return getInterp(interp, objv[2]);
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?path?");
			}
		}
		private static bool getInterpPath(Interp askingInterp, Interp targetInterp)
		{
			if (targetInterp == askingInterp)
			{
				return true;
			}
			if (targetInterp == null || targetInterp.slave == null)
			{
				return false;
			}
			
			if (!getInterpPath(askingInterp, targetInterp.slave.masterInterp))
			{
				return false;
			}
			askingInterp.appendElement(targetInterp.slave.path);
			return true;
		}
		internal static Interp getInterp(Interp interp, TclObject path)
		{
			TclObject[] objv = TclList.getElements(interp, path);
			Interp searchInterp = interp; //Interim storage for interp. to find.
			
			for (int i = 0; i < objv.Length; i++)
			{
				
				string name = objv[i].ToString();
				if (!searchInterp.slaveTable.ContainsKey(name))
				{
					searchInterp = null;
					break;
				}
				InterpSlaveCmd slave = (InterpSlaveCmd) searchInterp.slaveTable[name];
				searchInterp = slave.slaveInterp;
				if (searchInterp == null)
				{
					break;
				}
			}
			
			if (searchInterp == null)
			{
				
				throw new TclException(interp, "could not find interpreter \"" + path.ToString() + "\"");
			}
			
			return searchInterp;
		}
	} // end InterpCmd
}
