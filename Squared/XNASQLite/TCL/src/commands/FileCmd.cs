/*
* FileCmd.java --
*
*	This file contains the Jacl implementation of the built-in Tcl "file"
*	command.
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
* RCS @(#) $Id: FileCmd.java,v 1.9 2003/02/03 01:39:02 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "file" command in Tcl.
	*/
	
	class FileCmd : Command
	{
		
		/// <summary> Reference to File.listRoots, null when JDK < 1.2</summary>
		private static System.Reflection.MethodInfo listRootsMethod;
		
		internal static Type procClass = null;
		
		private static readonly string[] validCmds = new string[]{"atime", "attributes", "channels", "copy", "delete", "dirname", "executable", "exists", "extension", "isdirectory", "isfile", "join", "link", "lstat", "mtime", "mkdir", "nativename", "normalize", "owned", "pathtype", "readable", "readlink", "rename", "rootname", "separator", "size", "split", "stat", "system", "tail", "type", "volumes", "writable"};
		
		private const int OPT_ATIME = 0;
		private const int OPT_ATTRIBUTES = 1;
		private const int OPT_CHANNELS = 2;
		private const int OPT_COPY = 3;
		private const int OPT_DELETE = 4;
		private const int OPT_DIRNAME = 5;
		private const int OPT_EXECUTABLE = 6;
		private const int OPT_EXISTS = 7;
		private const int OPT_EXTENSION = 8;
		private const int OPT_ISDIRECTORY = 9;
		private const int OPT_ISFILE = 10;
		private const int OPT_JOIN = 11;
		private const int OPT_LINK = 12;
		private const int OPT_LSTAT = 13;
		private const int OPT_MTIME = 14;
		private const int OPT_MKDIR = 15;
		private const int OPT_NATIVENAME = 16;
		private const int OPT_NORMALIZE = 17;
		private const int OPT_OWNED = 18;
		private const int OPT_PATHTYPE = 19;
		private const int OPT_READABLE = 20;
		private const int OPT_READLINK = 21;
		private const int OPT_RENAME = 22;
		private const int OPT_ROOTNAME = 23;
		private const int OPT_SEPARATOR = 24;
		private const int OPT_SIZE = 25;
		private const int OPT_SPLIT = 26;
		private const int OPT_STAT = 27;
		private const int OPT_SYSTEM = 28;
		private const int OPT_TAIL = 29;
		private const int OPT_TYPE = 30;
		private const int OPT_VOLUMES = 31;
		private const int OPT_WRITABLE = 32;
		
		private static readonly string[] validOptions = new string[]{"-force", "--"};
		
		private const int OPT_FORCE = 0;
		private const int OPT_LAST = 1;
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "option ?arg ...?");
			}
			
			int opt = TclIndex.get(interp, argv[1], validCmds, "option", 0);
			string path;
			System.IO.FileInfo fileObj = null;
			
			switch (opt)
			{
				
				case OPT_ATIME: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					// FIXME:  Currently returns the same thing as MTIME.
					// Java does not support retrieval of access time.
					
					
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					
					interp.setResult(getMtime(interp, argv[2].ToString(), fileObj));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_ATTRIBUTES:
          if ( argv[3].ToString() == "-readonly" )
            fileSetReadOnly( interp, argv );
          else
            throw new TclException( interp, "sorry, \"file attributes\" is not implemented yet" );
          return TCL.CompletionCode.RETURN;
					
				
				case OPT_CHANNELS: 
					
					throw new TclException(interp, "sorry, \"file channels\" is not implemented yet");
				
				
				case OPT_COPY: 
					fileCopyRename(interp, argv, true);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_DELETE: 
					fileDelete(interp, argv);
          return TCL.CompletionCode.RETURN;
          				
				
				case OPT_DIRNAME: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					path = argv[2].ToString();
					
					// Return all but the last component.  If there is only one
					// component, return it if the path was non-relative, otherwise
					// return the current directory.
					
					
					TclObject[] splitArrayObj = TclList.getElements(interp, FileUtil.splitAndTranslate(interp, path));
					
					if (splitArrayObj.Length > 1)
					{
						interp.setResult(FileUtil.joinPath(interp, splitArrayObj, 0, splitArrayObj.Length - 1));
					}
					else if ((splitArrayObj.Length == 0) || (FileUtil.getPathType(path) == FileUtil.PATH_RELATIVE))
					{
						if (JACL.PLATFORM == JACL.PLATFORM_MAC)
						{
							interp.setResult(":");
						}
						else
						{
							interp.setResult(".");
						}
					}
					else
					{
						
						interp.setResult(splitArrayObj[0].ToString());
					}
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_EXECUTABLE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					bool isExe = false;
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					
					// A file must exist to be executable.  Directories are always
					// executable. 
					
					bool tmpBool;
					if (System.IO.File.Exists(fileObj.FullName))
						tmpBool = true;
					else
						tmpBool = System.IO.Directory.Exists(fileObj.FullName);
					if (tmpBool)
					{
						isExe = System.IO.Directory.Exists(fileObj.FullName);
						if (isExe)
						{
							interp.setResult(isExe);
              return TCL.CompletionCode.RETURN;
						}
						
						if (Util.Windows)
						{
							// File that ends with .exe, .com, or .bat is executable.
							
							
							string fileName = argv[2].ToString();
							isExe = (fileName.EndsWith(".exe") || fileName.EndsWith(".com") || fileName.EndsWith(".bat"));
						}
						else if (Util.Mac)
						{
							// FIXME:  Not yet implemented on Mac.  For now, return true.
							// Java does not support executability checking.
							
							isExe = true;
						}
						else
						{
							// FIXME:  Not yet implemented on Unix.  For now, return true.
							// Java does not support executability checking.
							
							isExe = true;
						}
					}
					interp.setResult(isExe);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_EXISTS: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					bool tmpBool2;
					if (System.IO.File.Exists(fileObj.FullName))
						tmpBool2 = true;
					else
						tmpBool2 = System.IO.Directory.Exists(fileObj.FullName);
					interp.setResult(tmpBool2);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_EXTENSION: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					interp.setResult(getExtension(argv[2].ToString()));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_ISDIRECTORY: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					interp.setResult(System.IO.Directory.Exists(fileObj.FullName));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_ISFILE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					interp.setResult(System.IO.File.Exists(fileObj.FullName));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_JOIN: 
					if (argv.Length < 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name ?name ...?");
					}
					interp.setResult(FileUtil.joinPath(interp, argv, 2, argv.Length));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_LINK: 
					
					throw new TclException(interp, "sorry, \"file link\" is not implemented yet");
				
				
				case OPT_LSTAT: 
					if (argv.Length != 4)
					{
						throw new TclNumArgsException(interp, 2, argv, "name varName");
					}
					
					// FIXME:  Not yet implemented.
					// Java does not support link access.
					
					
					throw new TclException(interp, "file command with opt " + argv[1].ToString() + " is not yet implemented");
				
				
				
				case OPT_MTIME: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					
					interp.setResult(getMtime(interp, argv[2].ToString(), fileObj));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_MKDIR: 
					fileMakeDirs(interp, argv);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_NATIVENAME: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					
					interp.setResult(FileUtil.translateFileName(interp, argv[2].ToString()));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_NORMALIZE: 
					
					throw new TclException(interp, "sorry, \"file normalize\" is not implemented yet");
				
				
				case OPT_OWNED: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					interp.setResult(isOwner(interp, fileObj));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_PATHTYPE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					switch (FileUtil.getPathType(argv[2].ToString()))
					{
						
						case FileUtil.PATH_RELATIVE: 
							interp.setResult("relative");
              return TCL.CompletionCode.RETURN;
						
						case FileUtil.PATH_VOLUME_RELATIVE: 
							interp.setResult("volumerelative");
              return TCL.CompletionCode.RETURN;
						
						case FileUtil.PATH_ABSOLUTE: 
							interp.setResult("absolute");
							break;
						}
            return TCL.CompletionCode.RETURN;
				
				
				case OPT_READABLE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					
					// interp.setResult(fileObj.canRead());
					// HACK
					interp.setResult(true);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_READLINK: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					// FIXME:  Not yet implemented.
					// Java does not support link access.
					
					
					throw new TclException(interp, "file command with opt " + argv[1].ToString() + " is not yet implemented");
				
				
				case OPT_RENAME: 
					fileCopyRename(interp, argv, false);
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_ROOTNAME: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					string fileName2 = argv[2].ToString();
					string extension = getExtension(fileName2);
					int diffLength = fileName2.Length - extension.Length;
					interp.setResult(fileName2.Substring(0, (diffLength) - (0)));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_SEPARATOR: 
					
					throw new TclException(interp, "sorry, \"file separator\" is not implemented yet");
				
				
				case OPT_SIZE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					bool tmpBool3;
					if (System.IO.File.Exists(fileObj.FullName))
						tmpBool3 = true;
					else
						tmpBool3 = System.IO.Directory.Exists(fileObj.FullName);
					if (!tmpBool3)
					{
						
						throw new TclPosixException(interp, TclPosixException.ENOENT, true, "could not read \"" + argv[2].ToString() + "\"");
					}
					interp.setResult((int) SupportClass.FileLength(fileObj));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_SPLIT: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					interp.setResult(FileUtil.splitPath(interp, argv[2].ToString()));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_STAT: 
					if (argv.Length != 4)
					{
						throw new TclNumArgsException(interp, 2, argv, "name varName");
					}
					
					getAndStoreStatData(interp, argv[2].ToString(), argv[3].ToString());
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_SYSTEM: 
					
					throw new TclException(interp, "sorry, \"file system\" is not implemented yet");
				
				
				case OPT_TAIL: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					interp.setResult(getTail(interp, argv[2].ToString()));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_TYPE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					
					interp.setResult(getType(interp, argv[2].ToString(), fileObj));
          return TCL.CompletionCode.RETURN;
				
				
				case OPT_VOLUMES: 
					if (argv.Length != 2)
					{
						throw new TclNumArgsException(interp, 2, argv, null);
					}
					
					// use Java 1.2's File.listRoots() method if available
					
					if (listRootsMethod == null)
						throw new TclException(interp, "\"file volumes\" is not supported");
					
					try
					{
						System.IO.FileInfo[] roots = (System.IO.FileInfo[]) listRootsMethod.Invoke(null, (System.Object[]) new System.Object[0]);
						if (roots != null)
						{
							TclObject list = TclList.newInstance();
							for (int i = 0; i < roots.Length; i++)
							{
								string root = roots[i].FullName;
								TclList.append(interp, list, TclString.newInstance(root));
							}
							interp.setResult(list);
						}
					}
					catch (System.UnauthorizedAccessException ex)
					{
						throw new TclRuntimeError("IllegalAccessException in volumes cmd");
					}
					catch (System.ArgumentException ex)
					{
						throw new TclRuntimeError("IllegalArgumentException in volumes cmd");
					}
					catch (System.Reflection.TargetInvocationException ex)
					{
												System.Exception t = ex.GetBaseException();
						
						if (t is System.ApplicationException)
						{
							throw (System.ApplicationException) t;
						}
						else
						{
							throw new TclRuntimeError("unexected exception in volumes cmd");
						}
					}

          return TCL.CompletionCode.RETURN;
				
				case OPT_WRITABLE: 
					if (argv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, argv, "name");
					}
					
					fileObj = FileUtil.getNewFileObj(interp, argv[2].ToString());
					interp.setResult(SupportClass.FileCanWrite(fileObj));
          return TCL.CompletionCode.RETURN;
				
				default: 
					
					throw new TclRuntimeError("file command with opt " + argv[1].ToString() + " is not implemented");
				
			}
		}
		private static bool isOwner(Interp interp, System.IO.FileInfo fileObj)
		{
			// If the file doesn't exist, return false;
			
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if (!tmpBool)
			{
				return false;
			}
			bool owner = true;
			
			// For Windows and Macintosh, there are no user ids 
			// associated with a file, so we always return 1.
			
			if (Util.Unix)
			{
				// FIXME:  Not yet implemented on Unix.  Do no checking, for now.
				// Java does not support ownership checking.
			}
			return owner;
		}
		private static int getMtime(Interp interp, string fileName, System.IO.FileInfo fileObj)
		{
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if (!tmpBool)
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "could not read \"" + fileName + "\"");
			}
			// Divide to convert msecs to seconds
			return (int) (fileObj.LastWriteTime.Ticks / 1000);
		}
		private static string getType(Interp interp, string fileName, System.IO.FileInfo fileObj)
		{
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if (!tmpBool)
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "could not read \"" + fileName + "\"");
			}
			
			if (System.IO.File.Exists(fileObj.FullName))
			{
				return "file";
			}
			else if (System.IO.Directory.Exists(fileObj.FullName))
			{
				return "directory";
			}
			return "link";
		}
		private static void  getAndStoreStatData(Interp interp, string fileName, string varName)
		{
			System.IO.FileInfo fileObj = FileUtil.getNewFileObj(interp, fileName);
			
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if (!tmpBool)
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "could not read \"" + fileName + "\"");
			}
			
			try
			{
				int mtime = getMtime(interp, fileName, fileObj);
				TclObject mtimeObj = TclInteger.newInstance(mtime);
				TclObject atimeObj = TclInteger.newInstance(mtime);
				TclObject ctimeObj = TclInteger.newInstance(mtime);
				interp.setVar(varName, "atime", atimeObj, 0);
				interp.setVar(varName, "ctime", ctimeObj, 0);
				interp.setVar(varName, "mtime", mtimeObj, 0);
			}
			catch (System.Security.SecurityException e)
			{
				throw new TclException(interp, e.Message);
			}
			catch (TclException e)
			{
				throw new TclException(interp, "can't set \"" + varName + "(dev)\": variable isn't array");
			}
			
			try
			{
				TclObject sizeObj = TclInteger.newInstance((int) SupportClass.FileLength(fileObj));
				interp.setVar(varName, "size", sizeObj, 0);
			}
			catch (System.Exception e)
			{
				// Do nothing.
			}
			
			try
			{
				TclObject typeObj = TclString.newInstance(getType(interp, fileName, fileObj));
				interp.setVar(varName, "type", typeObj, 0);
			}
			catch (System.Exception e)
			{
			}
			
			try
			{
				TclObject uidObj = TclBoolean.newInstance(isOwner(interp, fileObj));
				interp.setVar(varName, "uid", uidObj, 0);
			}
			catch (TclException e)
			{
				// Do nothing.
			}
		}
		private static string getExtension(string path)
		// Path for which we find extension.
		{
			if (path.Length < 1)
			{
				return "";
			}
			
			// Set lastSepIndex to the first index in the last component of the path.
			
			int lastSepIndex = - 1;
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_WINDOWS: 
					string tmpPath = path.Replace('\\', '/').Replace(':', '/');
					lastSepIndex = tmpPath.LastIndexOf((System.Char) '/');
					break;
				
				case JACL.PLATFORM_MAC: 
					lastSepIndex = path.LastIndexOf((System.Char) ':');
					if (lastSepIndex == - 1)
					{
						lastSepIndex = path.LastIndexOf((System.Char) '/');
					}
					break;
				
				default: 
					lastSepIndex = path.LastIndexOf((System.Char) '/');
					break;
				
			}
			++lastSepIndex;
			
			// Return "" if the last character is a separator.
			
			if (lastSepIndex >= path.Length)
			{
				return ("");
			}
			
			// Find the last dot in the last component of the path.
			
			string lastSep = path.Substring(lastSepIndex);
			int dotIndex = lastSep.LastIndexOf((System.Char) '.');
			
			// Return "" if no dot was found in the file's name.
			
			if (dotIndex == - 1)
			{
				return "";
			}
			
			// In earlier versions, we used to back up to the first period in a series
			// so that "foo..o" would be split into "foo" and "..o".  This is a
			// confusing and usually incorrect behavior, so now we split at the last
			// period in the name.
			
			return (lastSep.Substring(dotIndex));
		}
		private static string getTail(Interp interp, string path)
		{
			// Split the path and return the string form of the last component,
			// unless there is only one component which is the root or an absolute
			// path. 
			
			TclObject splitResult = FileUtil.splitAndTranslate(interp, path);
			
			int last = TclList.getLength(interp, splitResult) - 1;
			
			if (last >= 0)
			{
				if ((last > 0) || (FileUtil.getPathType(path) == FileUtil.PATH_RELATIVE))
				{
					TclObject tailObj = TclList.index(interp, splitResult, last);
					
					return tailObj.ToString();
				}
			}
			return "";
		}
		private static void  fileMakeDirs(Interp interp, TclObject[] argv)
		{
			bool madeDir = false;
			
			for (int currentDir = 2; currentDir < argv.Length; currentDir++)
			{
				
				string dirName = argv[currentDir].ToString();
				if (dirName.Length == 0)
				{
					throw new TclPosixException(interp, TclPosixException.ENOENT, true, "can't create directory \"\"");
				}
				System.IO.FileInfo dirObj = FileUtil.getNewFileObj(interp, dirName);
				bool tmpBool;
				if (System.IO.File.Exists(dirObj.FullName))
					tmpBool = true;
				else
					tmpBool = System.IO.Directory.Exists(dirObj.FullName);
				if (tmpBool)
				{
					// If the directory already exists, do nothing.
					if (System.IO.Directory.Exists(dirObj.FullName))
					{
						continue;
					}
					throw new TclPosixException(interp, TclPosixException.EEXIST, true, "can't create directory \"" + dirName + "\"");
				}
				try
				{
					System.IO.Directory.CreateDirectory(dirObj.FullName);
					madeDir = true;
				}
				catch (Exception e)
				{
					throw new TclException(interp, e.Message);
				}
				if (!madeDir)
				{
					throw new TclPosixException(interp, TclPosixException.EACCES, true, "can't create directory \"" + dirName + "\":  best guess at reason");
				}
			}
		}
		private static void  fileDelete(Interp interp, TclObject[] argv)
		{
			bool force = false;
			int firstSource = 2;
			
			for (bool last = false; (firstSource < argv.Length) && (!last); firstSource++)
			{
				
				
				if (!argv[firstSource].ToString().StartsWith("-"))
				{
					break;
				}
				int opt = TclIndex.get(interp, argv[firstSource], validOptions, "option", 1);
				switch (opt)
				{
					
					case OPT_FORCE: 
						force = true;
						break;
					
					case OPT_LAST: 
						last = true;
						break;
					
					default: 
						throw new TclRuntimeError("FileCmd.cmdProc: bad option " + opt + " index to validOptions");
					
				}
			}
			
			if (firstSource >= argv.Length)
			{
				throw new TclNumArgsException(interp, 2, argv, "?options? file ?file ...?");
			}
			
			for (int i = firstSource; i < argv.Length; i++)
			{
				
				deleteOneFile(interp, argv[i].ToString(), force);
			}
		}
		private static void  deleteOneFile(Interp interp, string fileName, bool force)
		{
      if ( fileName == ":memory:" ) return;
      bool isDeleted = true;
			System.IO.FileInfo fileObj = FileUtil.getNewFileObj(interp, fileName);
			
			// Trying to delete a file that does not exist is not
			// considered an error, just a no-op
			
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if ((!tmpBool) || (fileName.Length == 0))
			{
				return ;
			}
			
			// If the file is a non-empty directory, recursively delete its children if
			// the -force option was chosen.  Otherwise, throw an error.
			
			if (System.IO.Directory.Exists(fileObj.FullName) && (System.IO.Directory.GetFileSystemEntries(fileObj.FullName).Length > 0))
			{
				if (force)
				{
					string[] fileList = System.IO.Directory.GetFileSystemEntries(fileObj.FullName);
					for (int i = 0; i < fileList.Length; i++)
					{
						
						TclObject[] joinArrayObj = new TclObject[2];
						joinArrayObj[0] = TclString.newInstance(fileName);
						joinArrayObj[1] = TclString.newInstance(fileList[i]);
						
						string child = FileUtil.joinPath(interp, joinArrayObj, 0, 2);
						deleteOneFile(interp, child, force);
					}
				}
				else
				{
					throw new TclPosixException(interp, TclPosixException.ENOTEMPTY, "error deleting \"" + fileName + "\": directory not empty");
				}
			}
			try
			{
				bool tmpBool2;
				if (System.IO.File.Exists(fileObj.FullName))
				{
          fileObj.Attributes = System.IO.FileAttributes.Normal;
          System.IO.File.Delete( fileObj.FullName );
					tmpBool2 = true;
				}
				else if (System.IO.Directory.Exists(fileObj.FullName))
				{
					System.IO.Directory.Delete(fileObj.FullName);
					tmpBool2 = true;
				}
				else
					tmpBool2 = false;
				isDeleted = tmpBool2;
			}
			catch (System.IO.IOException e) {
				throw new TclException(interp, e.Message);
			}
			catch (System.Security.SecurityException e)
			{
				throw new TclException(interp, e.Message);
			}
			if (!isDeleted)
			{
				throw new TclPosixException(interp, TclPosixException.EACCES, true, "error deleting \"" + fileName + "\":  best guess at reason");
			}
		}
		private static void  fileCopyRename(Interp interp, TclObject[] argv, bool copyFlag)
		{
			int firstSource = 2;
			bool force = false;
			
			for (bool last = false; (firstSource < argv.Length) && (!last); firstSource++)
			{
				
				
				if (!argv[firstSource].ToString().StartsWith("-"))
				{
					break;
				}
				int opt = TclIndex.get(interp, argv[firstSource], validOptions, "option", 1);
				switch (opt)
				{
					
					case OPT_FORCE: 
						force = true;
						break;
					
					case OPT_LAST: 
						last = true;
						break;
					
					default: 
						throw new TclRuntimeError("FileCmd.cmdProc: bad option " + opt + " index to validOptions");
					
				}
			}
			
			if (firstSource >= (argv.Length - 1))
			{
				throw new TclNumArgsException(interp, firstSource, argv, "?options? source ?source ...? target");
			}
			
			// WARNING:  ignoring links because Java does not support them.
			
			int target = argv.Length - 1;
			
			string targetName = argv[target].ToString();
			
			System.IO.FileInfo targetObj = FileUtil.getNewFileObj(interp, targetName);
			if (System.IO.Directory.Exists(targetObj.FullName))
			{
				// If the target is a directory, move each source file into target
				// directory.  Extract the tailname from each source, and append it to
				// the end of the target path.  
				
				for (int source = firstSource; source < target; source++)
				{
					
					
					string sourceName = argv[source].ToString();
					
					if (targetName.Length == 0)
					{
						copyRenameOneFile(interp, sourceName, targetName, copyFlag, force);
					}
					else
					{
						string tailName = getTail(interp, sourceName);
						
						TclObject[] joinArrayObj = new TclObject[2];
						joinArrayObj[0] = TclString.newInstance(targetName);
						joinArrayObj[1] = TclString.newInstance(tailName);
						
						string fullTargetName = FileUtil.joinPath(interp, joinArrayObj, 0, 2);
						
						copyRenameOneFile(interp, sourceName, fullTargetName, copyFlag, force);
					}
				}
			}
			else
			{
				// If there is more than 1 source file and the target is not a
				// directory, then throw an exception.
				
				if (firstSource + 1 != target)
				{
					string action;
					if (copyFlag)
					{
						action = "copying";
					}
					else
					{
						action = "renaming";
					}
					
					throw new TclPosixException(interp, TclPosixException.ENOTDIR, "error " + action + ": target \"" + argv[target].ToString() + "\" is not a directory");
				}
				
				string sourceName = argv[firstSource].ToString();
				copyRenameOneFile(interp, sourceName, targetName, copyFlag, force);
			}
		}
		private static void  copyRenameOneFile(Interp interp, string sourceName, string targetName, bool copyFlag, bool force)
		{
			// Copying or renaming a file onto itself is a no-op if force is chosen,
			// otherwise, it will be caught later as an EEXISTS error.
			
			if (force && sourceName.Equals(targetName))
			{
				return ;
			}
			
			// Check that the source exists and that if -force was not specified, the
			// target doesn't exist.
			//
			// Prevent copying/renaming a file onto a directory and
			// vice-versa.  This is a policy decision based on the fact that
			// existing implementations of copy and rename on all platforms
			// also prevent this.
			
			string action;
			if (copyFlag)
			{
				action = "copying";
			}
			else
			{
				action = "renaming";
			}
			
			System.IO.FileInfo sourceFileObj = FileUtil.getNewFileObj(interp, sourceName);
			bool tmpBool;
			if (System.IO.File.Exists(sourceFileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(sourceFileObj.FullName);
			if ((!tmpBool) || (sourceName.Length == 0))
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "error " + action + " \"" + sourceName + "\"");
			}
			
			if (targetName.Length == 0)
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "error " + action + " \"" + sourceName + "\" to \"" + targetName + "\"");
			}
			System.IO.FileInfo targetFileObj = FileUtil.getNewFileObj(interp, targetName);
			bool tmpBool2;
			if (System.IO.File.Exists(targetFileObj.FullName))
				tmpBool2 = true;
			else
				tmpBool2 = System.IO.Directory.Exists(targetFileObj.FullName);
			if (tmpBool2 && !force)
			{
				throw new TclPosixException(interp, TclPosixException.EEXIST, true, "error " + action + " \"" + sourceName + "\" to \"" + targetName + "\"");
			}
			
			if (System.IO.Directory.Exists(sourceFileObj.FullName) && !System.IO.Directory.Exists(targetFileObj.FullName))
			{
				throw new TclPosixException(interp, TclPosixException.EISDIR, "can't overwrite file \"" + targetName + "\" with directory \"" + sourceName + "\"");
			}
			if (System.IO.Directory.Exists(targetFileObj.FullName) && !System.IO.Directory.Exists(sourceFileObj.FullName))
			{
				throw new TclPosixException(interp, TclPosixException.EISDIR, "can't overwrite directory \"" + targetName + "\" with file \"" + sourceName + "\"");
			}
			
			if (!copyFlag)
			{
				// Perform the rename procedure.
				
				try 
				{
					sourceFileObj.MoveTo(targetFileObj.FullName);
				}
				catch (Exception e) 
				{
					throw new TclPosixException(interp, TclPosixException.EACCES, true, "error renaming \"" + sourceName + "\" to \"" + targetName + "\"");
				}
//				{
//					
//					if (System.IO.Directory.Exists(targetFileObj.FullName))
//					{
//						throw new TclPosixException(interp, TclPosixException.EEXIST, true, "error renaming \"" + sourceName + "\" to \"" + targetName + "\"");
//					}
//					
//					throw new TclPosixException(interp, TclPosixException.EACCES, true, "error renaming \"" + sourceName + "\" to \"" + targetName + "\":  best guess at reason");
//				}
			}
			else
			{
				// Perform the copy procedure.
				
				try
				{
          sourceFileObj.CopyTo( targetFileObj.FullName, true );
				}
				catch (System.IO.IOException e)
				{
					throw new TclException(interp, "error copying: " + e.Message);
				}
			}
		}
    private static void fileSetReadOnly( Interp interp, TclObject[] argv )
    {
      int firstSource = 2;

      for ( bool last = false ; ( firstSource < argv.Length ) && ( !last ) ; firstSource++ )
      {
        if ( !argv[firstSource].ToString().StartsWith( "-" ) )
        {
          break;
        }
      }

      if ( firstSource >= argv.Length )
      {
        throw new TclNumArgsException( interp, 2, argv, "?options? file ?file ...?" );
      }

      for ( int i = firstSource ; i < argv.Length ; i++ )
      {

        setReadOnlyOneFile( interp, argv[i].ToString());
      }
    }
    private static void setReadOnlyOneFile( Interp interp, string fileName )
    {
      System.IO.FileInfo fileObj = FileUtil.getNewFileObj( interp, fileName );
      try
      {
        fileObj.Attributes = System.IO.FileAttributes.ReadOnly;
      }
      catch ( System.IO.IOException e )
      {
        throw new TclException( interp, e.Message );
      }
      catch ( System.Security.SecurityException e )
      {
        throw new TclException( interp, e.Message );
      }
    }
    static FileCmd()
		{
			{
				// File.listRoots()
				System.Type[] parameterTypes = new System.Type[0];
				try
				{
					listRootsMethod = typeof(System.IO.FileInfo).GetMethod("listRoots", (System.Type[]) parameterTypes);
				}
				catch (System.MethodAccessException e)
				{
					listRootsMethod = null;
				}
			}
		}
	} // end FileCmd class
}
