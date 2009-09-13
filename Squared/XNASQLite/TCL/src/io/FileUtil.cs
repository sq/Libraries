#undef DEBUG
/*
* FileUtil.java --
*
*	This file contains utility methods for file-related operations.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: FileUtil.java,v 1.6 2003/02/02 00:59:16 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements utility methods for file-related operations.
	*/
	
	public class FileUtil
	{
		
		internal const int PATH_RELATIVE = 0;
		internal const int PATH_VOLUME_RELATIVE = 1;
		internal const int PATH_ABSOLUTE = 2;
		
		/*
		*-----------------------------------------------------------------------------
		*
		* getWinHomePath --
		*
		*	In the Windows file system, one type of absolute path follows this
		*	regular expression:  ^(//+[a-zA-Z]+/+[a-zA-Z]+) 
		*
		*	If "path" doesn't fit the pattern, then return 0.
		*	If the stopEarly bool is true, then return the index of the first
		*	non-slash character in path, as soon as we know that path fits the
		*	pattern.  Otherwise, return the index of the slash (or end of string) 
		*	following the entire absolute path.
		*
		* Results:
		*	Returns an integer index in path.
		*
		* Side effects:
		*	If "path" fits the pattern, and "stopEarly" is not chosen, the absolute
		*	path is coppied (without extra slashes) to "absBuf".  Otherwise, absBuf
		*	is set to "".
		*
		*-----------------------------------------------------------------------------
		*/
		
		private static int getWinHomePath(string path, bool stopEarly, System.Text.StringBuilder absBuf)
		// Buffer to store side effect.
		{
			int pIndex, oldIndex, firstNonSlash;
			
			// The first 2 or more chars must be slashes.
			
			for (pIndex = 0; pIndex < path.Length; pIndex++)
			{
				if (path[pIndex] != '/')
				{
					break;
				}
			}
			if (pIndex < 2)
			{
				absBuf.Length = 0;
				return 0;
			}
			firstNonSlash = pIndex;
			
			
			// The next 1 or more chars may not be slashes.
			
			for (; pIndex < path.Length; pIndex++)
			{
				if (path[pIndex] == '/')
				{
					break;
				}
			}
			if (pIndex == firstNonSlash)
			{
				absBuf.Length = 0;
				return 0;
			}
			absBuf.EnsureCapacity(absBuf.Length + path.Length);
			absBuf.Append("//");
			absBuf.Append(path.Substring(firstNonSlash, (pIndex) - (firstNonSlash)));
			
			// The next 1 or more chars must be slashes.
			
			oldIndex = pIndex;
			for (; pIndex < path.Length; pIndex++)
			{
				if (path[pIndex] != '/')
				{
					if (pIndex == oldIndex)
					{
						absBuf.Length = 0;
						return 0;
					}
					
					// We know that the path fits the pattern.
					
					if (stopEarly)
					{
						absBuf.Length = 0;
						return firstNonSlash;
					}
					firstNonSlash = pIndex;
					
					// Traverse the path until a new slash (or end of string) is found.
					// Return the index of the new slash.
					
					pIndex++;
					for (; pIndex < path.Length; pIndex++)
					{
						if (path[pIndex] == '/')
						{
							break;
						}
					}
					absBuf.Append('/');
					absBuf.Append(path.Substring(firstNonSlash, (pIndex) - (firstNonSlash)));
					return pIndex;
				}
			}
			absBuf.Length = 0;
			return 0;
		}
		private static int beginsWithLetterColon(string path)
		// Path to check start pattern.
		{
			if ((path.Length > 1) && (System.Char.IsLetter(path[0])) && (path[1] == ':'))
			{
				
				int pIndex;
				for (pIndex = 2; pIndex < path.Length; pIndex++)
				{
					if (path[pIndex] != '/')
					{
						break;
					}
				}
				return pIndex;
			}
			return 0;
		}
		private static int getWinAbsPath(string path, System.Text.StringBuilder absBuf)
		// Buffer to store side effect.
		{
			absBuf.Length = 0;
			
			if (path.Length < 1)
			{
				return 0;
			}
			
			absBuf.EnsureCapacity(absBuf.Length + path.Length);
			
			int colonIndex = beginsWithLetterColon(path);
			if (colonIndex > 0)
			{
				if (colonIndex > 2)
				{
					absBuf.Append(path.Substring(0, (3) - (0)));
				}
				else
				{
					absBuf.Append(path.Substring(0, (2) - (0)));
				}
				return colonIndex;
			}
			else
			{
				int absIndex = getWinHomePath(path, false, absBuf);
				if (absIndex > 0)
				{
					return absIndex;
				}
				else if (path[0] == '/')
				{
					int pIndex;
					for (pIndex = 1; pIndex < path.Length; pIndex++)
					{
						if (path[pIndex] != '/')
						{
							break;
						}
					}
					absBuf.Append("/");
					return pIndex;
				}
			}
			return 0;
		}
		private static int getDegenerateUnixPath(string path)
		// Path to check.
		{
			int pIndex = 0;
			
			while ((pIndex < path.Length) && (path[pIndex] == '/'))
			{
				++pIndex;
			}
			
			// "path" doesn't begin with a '/'.
			
			if (pIndex == 0)
			{
				return 0;
			}
			while (pIndex < path.Length)
			{
				string tmpPath = path.Substring(pIndex);
				if (tmpPath.StartsWith("./"))
				{
					pIndex += 2;
				}
				else if (tmpPath.StartsWith("../"))
				{
					pIndex += 3;
				}
				else
				{
					break;
				}
				while ((pIndex < path.Length) && (path[pIndex] == '/'))
				{
					++pIndex;
				}
			}
			if ((pIndex < path.Length) && (path[pIndex] == '.'))
			{
				++pIndex;
			}
			if ((pIndex < path.Length) && (path[pIndex] == '.'))
			{
				++pIndex;
			}
			
			// pIndex may be 1 past the end of "path".
			
			return pIndex;
		}
		internal static int getPathType(string path)
		// Path for which we find pathtype.
		{
			char c;
			if (path.Length < 1)
			{
				return PATH_RELATIVE;
			}
			
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_WINDOWS: 
					path = path.Replace('\\', '/');
					
					// Windows absolute pathes start with '~' or [a-zA-Z]:/ or home
					// path.
					
					c = path[0];
					if (c == '~')
					{
						return PATH_ABSOLUTE;
					}
					if (c == '/')
					{
						System.Text.StringBuilder absBuf = new System.Text.StringBuilder(0);
						if (getWinHomePath(path, true, absBuf) > 0)
						{
							return PATH_ABSOLUTE;
						}
						return PATH_VOLUME_RELATIVE;
					}
					int colonIndex = beginsWithLetterColon(path);
					if (colonIndex > 0)
					{
						if (colonIndex > 2)
						{
							return PATH_ABSOLUTE;
						}
						return PATH_VOLUME_RELATIVE;
					}
					return PATH_RELATIVE;
				
				
				case JACL.PLATFORM_MAC: 
					if (path[0] == '~')
					{
						return PATH_ABSOLUTE;
					}
					
					switch (path.IndexOf((System.Char) ':'))
					{
						
						case - 1: 
							
							if ((path[0] == '/') && (getDegenerateUnixPath(path) < path.Length))
							{
								return PATH_ABSOLUTE;
							}
							break;
						
						case 0: 
							
							return PATH_RELATIVE;
						
						default: 
							
							return PATH_ABSOLUTE;
						
					}
					return PATH_RELATIVE;
				
				
				default: 
					
					c = path[0];
					if ((c == '/') || (c == '~'))
					{
						return PATH_ABSOLUTE;
					}
					break;
				
			}
			return PATH_RELATIVE;
		}
		internal static System.IO.FileInfo getNewFileObj(Interp interp, string fileName)
		{
			fileName = translateFileName(interp, fileName);
			System.Diagnostics.Debug.WriteLine("File name is \"" + fileName + "\"");
			switch (getPathType(fileName))
			{
				
				case PATH_RELATIVE:
          if ( fileName == ":memory:" ) return null;
					System.Diagnostics.Debug.WriteLine("File name is PATH_RELATIVE");
					return new System.IO.FileInfo(interp.getWorkingDir().FullName + "\\" + fileName);
				
				case PATH_VOLUME_RELATIVE: 
					System.Diagnostics.Debug.WriteLine("File name is PATH_VOLUME_RELATIVE");
					
					// Something is very wrong if interp.getWorkingDir()
					// does not start with C: or another drive letter
					string cwd = interp.getWorkingDir().ToString();
					int index = beginsWithLetterColon(cwd);
					if (index == 0)
					{
						throw new TclRuntimeError("interp working directory \"" + cwd + "\" does not start with a drive letter");
					}
					
					// We can not use the joinPath() method because joing("D:/", "/f.txt")
					// returns "/f.txt" for some wacky reason. Just do it ourselves.
					System.Text.StringBuilder buff = new System.Text.StringBuilder();
					buff.Append(cwd.Substring(0, (2) - (0)));
					buff.Append('\\');
					for (int i = 0; i < fileName.Length; i++)
					{
						if (fileName[i] != '\\')
						{
							// Once we skip all the \ characters at the front
							// append the rest of the fileName onto the buffer
							buff.Append(fileName.Substring(i));
							break;
						}
					}
					
					fileName = buff.ToString();
					
					System.Diagnostics.Debug.WriteLine("After PATH_VOLUME_RELATIVE join \"" + fileName + "\"");
					
					return new System.IO.FileInfo(fileName);
				
				case PATH_ABSOLUTE: 
					System.Diagnostics.Debug.WriteLine("File name is PATH_ABSOLUTE");
					return new System.IO.FileInfo(fileName);
				
				default: 
					throw new TclRuntimeError("type for fileName \"" + fileName + "\" not matched in case statement");
				
			}
		}
		private static void  appendComponent(string component, int compIndex, int compSize, System.Text.StringBuilder buf)
		// Buffer to append the component.
		{
			for (; compIndex < component.Length; compIndex++)
			{
				char c = component[compIndex];
				if (c == '/')
				{
					// Eliminate duplicate slashes.
					
					while ((compIndex < compSize) && (component[compIndex + 1] == '/'))
					{
						compIndex++;
					}
					
					// Only add a slash if following non-slash elements exist.
					
					if (compIndex < compSize)
					{
						buf.EnsureCapacity(buf.Length + 1);
						buf.Append('/');
					}
				}
				else
				{
					buf.EnsureCapacity(buf.Length + 1);
					buf.Append(c);
				}
			}
		}
		internal static string joinPath(Interp interp, TclObject[] argv, int startIndex, int endIndex)
		{
			System.Text.StringBuilder result = new System.Text.StringBuilder(10);
			
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_WINDOWS: 
					
					for (int i = startIndex; i < endIndex; i++)
					{
						
						
						string p = argv[i].ToString().Replace('\\', '/');
						int pIndex = 0;
						int pLastIndex = p.Length - 1;
						
						if (p.Length == 0)
						{
							continue;
						}
						
						System.Text.StringBuilder absBuf = new System.Text.StringBuilder(0);
						pIndex = getWinAbsPath(p, absBuf);
						if (pIndex > 0)
						{
							// If the path is absolute or volume relative (except those
							// beginning with '~'), reset the result buffer to the absolute
							// substring. 
							
							result = absBuf;
						}
						else if (p[0] == '~')
						{
							// If the path begins with '~', reset the result buffer to "".
							
							result.Length = 0;
						}
						else
						{
							// This is a relative path.  Remove the ./ from tilde prefixed
							// elements unless it is the first component.
							
							if ((result.Length != 0) && (String.Compare(p, pIndex, "./~", 0, 3) == 0))
							{
								pIndex = 2;
							}
							
							// Check to see if we need to append a separator before adding
							// this relative component.
							
							if (result.Length != 0)
							{
								char c = result[result.Length - 1];
								if ((c != '/') && (c != ':'))
								{
									result.EnsureCapacity(result.Length + 1);
									result.Append('/');
								}
							}
						}
						
						// Append the element.
						
						appendComponent(p, pIndex, pLastIndex, result);
						pIndex = p.Length;
					}
					return result.ToString();
				
				
				case JACL.PLATFORM_MAC: 
					
					
					bool needsSep = true;
					for (int i = startIndex; i < endIndex; i++)
					{
						
						
						TclObject[] splitArrayObj = TclList.getElements(interp, splitPath(interp, argv[i].ToString()));
						
						if (splitArrayObj.Length == 0)
						{
							continue;
						}
						
						// If 1st path element is absolute, reset the result to "" and
						// append the 1st path element to it. 
						
						int start = 0;
						
						string p = splitArrayObj[0].ToString();
						if ((p[0] != ':') && (p.IndexOf((System.Char) ':') != - 1))
						{
							result.Length = 0;
							result.Append(p);
							start++;
							needsSep = false;
						}
						
						// Now append the rest of the path elements, skipping
						// : unless it is the first element of the path, and
						// watching out for :: et al. so we don't end up with
						// too many colons in the result.
						
						for (int j = start; j < splitArrayObj.Length; j++)
						{
							
							
							p = splitArrayObj[j].ToString();
							
							if (p.Equals(":"))
							{
								if (result.Length != 0)
								{
									continue;
								}
								else
								{
									needsSep = false;
								}
							}
							else
							{
								char c = 'o';
								if (p.Length > 1)
								{
									c = p[1];
								}
								if (p[0] == ':')
								{
									if (!needsSep)
									{
										p = p.Substring(1);
									}
								}
								else
								{
									if (needsSep)
									{
										result.Append(':');
									}
								}
								if (c == ':')
								{
									needsSep = false;
								}
								else
								{
									needsSep = true;
								}
							}
							result.Append(p);
						}
					}
					return result.ToString();
				
				
				default: 
					
					for (int i = startIndex; i < endIndex; i++)
					{
						
						
						string p = argv[i].ToString();
						int pIndex = 0;
						int pLastIndex = p.Length - 1;
						
						if (p.Length == 0)
						{
							continue;
						}
						
						if (p[pIndex] == '/')
						{
							// If the path is absolute (except those beginning with '~'), 
							// reset the result buffer to the absolute substring. 
							
							while ((pIndex <= pLastIndex) && (p[pIndex] == '/'))
							{
								pIndex++;
							}
							result.Length = 0;
							result.Append('/');
						}
						else if (p[pIndex] == '~')
						{
							// If the path begins with '~', reset the result buffer to "".
							
							result.Length = 0;
						}
						else
						{
							// This is a relative path.  Remove the ./ from tilde prefixed
							// elements unless it is the first component.
							
							if ((result.Length != 0) && (String.Compare(p, pIndex, "./~", 0, 3) == 0))
							{
								pIndex += 2;
							}
							
							// Append a separator if needed.
							
							if ((result.Length != 0) && (result[result.Length - 1] != '/'))
							{
								result.EnsureCapacity(result.Length + 1);
								result.Append('/');
							}
						}
						
						// Append the element.
						
						appendComponent(p, pIndex, pLastIndex, result);
						pIndex = p.Length;
					}
					break;
				
			}
			return result.ToString();
		}
		internal static TclObject splitPath(Interp interp, string path)
		{
			TclObject resultListObj = TclList.newInstance();
			TclObject componentObj;
			string component = "";
			string tmpPath;
			bool foundComponent = false;
			bool convertDotToColon = false;
			bool isColonSeparator = false;
			bool appendColon = false;
			bool prependColon = false;
			string thisDir = "./";
			
			// If the path is the empty string, returnan empty result list.
			
			if (path.Length == 0)
			{
				return resultListObj;
			}
			
			// Handling the 1st component is file system dependent.
			
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_WINDOWS: 
					tmpPath = path.Replace('\\', '/');
					
					System.Text.StringBuilder absBuf = new System.Text.StringBuilder(0);
					int absIndex = getWinAbsPath(tmpPath, absBuf);
					if (absIndex > 0)
					{
						componentObj = TclString.newInstance(absBuf.ToString());
						TclList.append(interp, resultListObj, componentObj);
						tmpPath = tmpPath.Substring(absIndex);
						foundComponent = true;
					}
					break;
				
				
				case JACL.PLATFORM_MAC: 
					
					tmpPath = "";
					thisDir = ":";
					
					switch (path.IndexOf((System.Char) ':'))
					{
						
						case - 1: 
							
							if (path[0] != '/')
							{
								tmpPath = path;
								convertDotToColon = true;
								if (path[0] == '~')
								{
									// If '~' is the first char, then append a colon to end
									// of the 1st component. 
									
									appendColon = true;
								}
								break;
							}
							int degenIndex = getDegenerateUnixPath(path);
							if (degenIndex < path.Length)
							{
								// First component of absolute unix path is followed by a ':',
								// instead of being preceded by a degenerate unix-style
								// pattern.
								
								
								tmpPath = path.Substring(degenIndex);
								convertDotToColon = true;
								appendColon = true;
								break;
							}
							
							// Degenerate unix path can't be split.  Return a list with one
							// element:  ":" prepended to "path".
							
							componentObj = TclString.newInstance(":" + path);
							TclList.append(interp, resultListObj, componentObj);
							return resultListObj;
						
						case 0: 
							
							if (path.Length == 1)
							{
								// If path == ":", then return a list with ":" as its only
								// element.
								
								componentObj = TclString.newInstance(":");
								TclList.append(interp, resultListObj, componentObj);
								return resultListObj;
							}
							
							
							// For each component, if slashes exist in the remaining filename,
							// prepend a colon to the component.  Since this path is relative,
							// pretend that we have already processed 1 components so a
							// tilde-prefixed 1st component will have ":" prepended to it.
							
							
							tmpPath = path.Substring(1);
							foundComponent = true;
							prependColon = true;
							isColonSeparator = true;
							break;
						
						
						default: 
							
							tmpPath = path;
							appendColon = true;
							prependColon = true;
							isColonSeparator = true;
							break;
						
					}
					break;
				
				
				default: 
					
					if (path[0] == '/')
					{
						componentObj = TclString.newInstance("/");
						TclList.append(interp, resultListObj, componentObj);
						tmpPath = path.Substring(1);
						foundComponent = true;
					}
					else
					{
						tmpPath = path;
					}
					break;
				
			}
			
			// Iterate over all of the components of the path.
			
			int sIndex = 0;
			while (sIndex != - 1)
			{
				if (isColonSeparator)
				{
					sIndex = tmpPath.IndexOf(":");
					// process adjacent ':'
					
					if (sIndex == 0)
					{
						componentObj = TclString.newInstance("::");
						TclList.append(interp, resultListObj, componentObj);
						foundComponent = true;
						tmpPath = tmpPath.Substring(sIndex + 1);
						continue;
					}
				}
				else
				{
					sIndex = tmpPath.IndexOf("/");
					// Ignore a redundant '/'
					
					if (sIndex == 0)
					{
						tmpPath = tmpPath.Substring(sIndex + 1);
						continue;
					}
				}
				if (sIndex == - 1)
				{
					// Processing the last component.  If it is empty, exit loop.
					
					if (tmpPath.Length == 0)
					{
						break;
					}
					component = tmpPath;
				}
				else
				{
					component = tmpPath.Substring(0, (sIndex) - (0));
				}
				
				if (convertDotToColon && (component.Equals(".") || component.Equals("..")))
				{
					// If platform = MAC, convert .. to :: or . to :
					
					component = component.Replace('.', ':');
				}
				if (foundComponent)
				{
					if (component[0] == '~')
					{
						// If a '~' preceeds a component (other than the 1st one), then
						// prepend "./" or ":" to the component.
						
						component = thisDir + component;
					}
					else if (prependColon)
					{
						// If the prependColon flag is set, either unset it or prepend
						// ":" to the component, depending on whether any '/'s remain
						// in tmpPath.
						
						if (tmpPath.IndexOf((System.Char) '/') == - 1)
						{
							prependColon = false;
						}
						else
						{
							component = ":" + component;
						}
					}
				}
				else if (appendColon)
				{
					//If platform = MAC, append a ':' to the first component.
					
					component = component + ":";
				}
				componentObj = TclString.newInstance(component);
				TclList.append(interp, resultListObj, componentObj);
				foundComponent = true;
				tmpPath = tmpPath.Substring(sIndex + 1);
			}
			return resultListObj;
		}
		internal static string doTildeSubst(Interp interp, string user)
		{
			string dir;
			
			if (user.Length == 0)
			{
				try
				{
					
					dir = interp.getVar("env", "HOME", TCL.VarFlag.GLOBAL_ONLY).ToString();
				}
				catch (System.Exception e)
				{
					throw new TclException(interp, "couldn't find HOME environment variable to expand path");
				}
				return dir;
			}
			
			// WARNING:  Java does not support other users.  "dir" is always null,
			// but it should be the home directory (corresponding to the user name), as
			// specified in the password file.
			
			dir = null;
			if ((System.Object) dir == null)
			{
				throw new TclException(interp, "user \"" + user + "\" doesn't exist");
			}
			return dir;
		}
		public static string translateFileName(Interp interp, string path)
		{
			string fileName = "";
			
			if ((path.Length == 0) || (path[0] != '~'))
			{
				// 	    fileName = path;
				TclObject[] joinArrayObj = new TclObject[1];
				joinArrayObj[0] = TclString.newInstance(path);
				fileName = joinPath(interp, joinArrayObj, 0, 1);
			}
			else
			{
				TclObject[] splitArrayObj = TclList.getElements(interp, splitPath(interp, path));
				
				
				string user = splitArrayObj[0].ToString().Substring(1);
				
				
				// Strip the trailing ':' off of a Mac path
				// before passing the user name to DoTildeSubst.
				
				if ((JACL.PLATFORM == JACL.PLATFORM_MAC) && (user.EndsWith(":")))
				{
					user = user.Substring(0, (user.Length - 1) - (0));
				}
				
				user = doTildeSubst(interp, user);
				
				// 	if (splitArrayObj.length < 2) {
				// 	    fileName = user;
				// 	} else {
				splitArrayObj[0] = TclString.newInstance(user);
				fileName = joinPath(interp, splitArrayObj, 0, splitArrayObj.Length);
				// 	}
			}
			
			
			// Convert forward slashes to backslashes in Windows paths because
			// some system interfaces don't accept forward slashes.
			
			if (JACL.PLATFORM == JACL.PLATFORM_WINDOWS)
			{
				fileName = fileName.Replace('/', '\\');
			}
			return fileName;
		}
		internal static TclObject splitAndTranslate(Interp interp, string path)
		{
			TclObject splitResult = splitPath(interp, path);
			
			int len = TclList.getLength(interp, splitResult);
			if (len == 1)
			{
				
				string fileName = TclList.index(interp, splitResult, 0).ToString();
				if (fileName[0] == '~')
				{
					string user = translateFileName(interp, fileName);
					splitResult = splitPath(interp, user);
				}
			}
			return splitResult;
		}
	} // end FileUtil class
}
