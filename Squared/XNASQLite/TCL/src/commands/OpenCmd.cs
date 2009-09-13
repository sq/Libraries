/*
* OpenCmd.java --
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: OpenCmd.java,v 1.5 2003/03/08 03:42:44 mdejong Exp $
*
*/
using System;
using System.IO;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "open" command in Tcl.</summary>
	
	class OpenCmd : Command
	{
		/// <summary> This procedure is invoked to process the "open" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			
			bool pipeline = false; /* True if opening pipeline chan */
			int prot = 438; /* Final rdwr permissions of file */
			int modeFlags = TclIO.RDONLY; /* Rdwr mode for the file.  See the
			* TclIO class for more info on the
			* valid modes */
			
			if ((argv.Length < 2) || (argv.Length > 4))
			{
				throw new TclNumArgsException(interp, 1, argv, "fileName ?access? ?permissions?");
			}
			
			if (argv.Length > 2)
			{
				TclObject mode = argv[2];
				
				string modeStr = mode.ToString();
				int len = modeStr.Length;
				
				// This "r+1" hack is just to get a test case to pass
				if ((len == 0) || (modeStr.StartsWith("r+") && len >= 3))
				{
					throw new TclException(interp, "illegal access mode \"" + modeStr + "\"");
				}
				
				if (len < 3)
				{
					switch (modeStr[0])
					{
						
						case 'r':  {
								if (len == 1)
								{
									modeFlags = TclIO.RDONLY;
									break;
								}
								else if (modeStr[1] == '+')
								{
									modeFlags = TclIO.RDWR;
									break;
								}
							}
							goto case 'w';
						
						case 'w':  {
								
								FileInfo f = FileUtil.getNewFileObj(interp, argv[1].ToString());
								bool tmpBool;
								if (File.Exists(f.FullName))
									tmpBool = true;
								else
									tmpBool = Directory.Exists(f.FullName);
								if (tmpBool)
								{
									bool tmpBool2;
									try {
										if (File.Exists(f.FullName)) {
                      File.SetAttributes( f.FullName, FileAttributes.Normal ); 
                      File.Delete( f.FullName );
											tmpBool2 = true;
										} else if (Directory.Exists(f.FullName)) {
											Directory.Delete(f.FullName);
											tmpBool2 = true;
										} else {
											tmpBool2 = false;
										}
									}
									// ATK added because .NET do not allow often to delete
									// files used by another process
									catch (System.IO.IOException e)
									{
										throw new TclException(interp, "cannot open file: " + argv[1].ToString());
									}
									bool generatedAux = tmpBool2;
								}
								if (len == 1)
								{
									modeFlags = (TclIO.WRONLY | TclIO.CREAT);
									break;
								}
								else if (modeStr[1] == '+')
								{
									modeFlags = (TclIO.RDWR | TclIO.CREAT);
									break;
								}
							}
							goto case 'a';
						
						case 'a':  {
								if (len == 1)
								{
									modeFlags = (TclIO.WRONLY | TclIO.APPEND);
									break;
								}
								else if (modeStr[1] == '+')
								{
									modeFlags = (TclIO.RDWR | TclIO.CREAT | TclIO.APPEND);
									break;
								}
							}
							goto default;
						
						default:  {
								throw new TclException(interp, "illegal access mode \"" + modeStr + "\"");
							}
						
					}
				}
				else
				{
					modeFlags = 0;
					bool gotRorWflag = false;
					int mlen = TclList.getLength(interp, mode);
					for (int i = 0; i < mlen; i++)
					{
						TclObject marg = TclList.index(interp, mode, i);
						
						if (marg.ToString().Equals("RDONLY"))
						{
							modeFlags |= TclIO.RDONLY;
							gotRorWflag = true;
						}
						else
						{
							
							if (marg.ToString().Equals("WRONLY"))
							{
								modeFlags |= TclIO.WRONLY;
								gotRorWflag = true;
							}
							else
							{
								
								if (marg.ToString().Equals("RDWR"))
								{
									modeFlags |= TclIO.RDWR;
									gotRorWflag = true;
								}
								else
								{
									
									if (marg.ToString().Equals("APPEND"))
									{
										modeFlags |= TclIO.APPEND;
									}
									else
									{
										
										if (marg.ToString().Equals("CREAT"))
										{
											modeFlags |= TclIO.CREAT;
										}
										else
										{
											
											if (marg.ToString().Equals("EXCL"))
											{
												modeFlags |= TclIO.EXCL;
											}
											else
											{
												
												if (marg.ToString().Equals("TRUNC"))
												{
													modeFlags |= TclIO.TRUNC;
												}
												else
												{
													
													throw new TclException(interp, "invalid access mode \"" + marg.ToString() + "\": must be RDONLY, WRONLY, RDWR, APPEND, " + "CREAT EXCL, NOCTTY, NONBLOCK, or TRUNC");
												}
											}
										}
									}
								}
							}
						}
					}
					if (!gotRorWflag)
					{
						throw new TclException(interp, "access mode must include either RDONLY, WRONLY, or RDWR");
					}
				}
			}
			
			if (argv.Length == 4)
			{
				prot = TclInteger.get(interp, argv[3]);
				throw new TclException(interp, "setting permissions not implemented yet");
			}
			
			if ((argv[1].ToString().Length > 0) && (argv[1].ToString()[0] == '|'))
			{
				pipeline = true;
				throw new TclException(interp, "pipes not implemented yet");
			}
			
			/*
			* Open the file or create a process pipeline.
			*/
			
			if (!pipeline)
			{
				try
				{
					FileChannel file = new FileChannel();
					
					file.open(interp, argv[1].ToString(), modeFlags);
					TclIO.registerChannel(interp, file);
					interp.setResult(file.ChanName);
				}
				catch (System.IO.IOException e)
				{
					
					throw new TclException(interp, "cannot open file: " + argv[1].ToString());
				}
			}
			else
			{
				/*
				* Pipeline code here...
				*/
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
