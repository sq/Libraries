/*
* GlobCmd.java
*
*	This file contains the Jacl implementation of the built-in Tcl "glob"
*	command.
*
* Copyright (c) 1997-1998 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: GlobCmd.java,v 1.5 1999/08/28 03:55:18 mo Exp $
*
*/
using System;
using System.IO;

namespace tcl.lang
{
	
	/*
	* This class implements the built-in "glob" command in Tcl.
	*/
	
	class GlobCmd : Command
	{
		
		/*
		* Special characters that are used for string matching. 
		*/
		
		private static readonly char[] specCharArr = new char[]{'*', '[', ']', '?', '\\'};
		
		/*
		* Options to the glob command.
		*/
		
		private static readonly string[] validOptions = new string[]{"-nocomplain", "--"};
		private const int OPT_NOCOMPLAIN = 0;
		private const int OPT_LAST = 1;

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
		{
			bool noComplain = false; // If false, error msg will be returned 
			int index; // index of the char just after the end 
			//   of the user name 
			int firstArg = 1; // index of the first non-switch arg 
			int i; // generic index 
			string arg; // generic arg string 
			string head = ""; // abs path of user name if provided 
			string tail = ""; // the remaining file path and pattern 
			TclObject resultList; // list of files that match the pattern
			
			for (bool last = false; (firstArg < argv.Length) && (!last); firstArg++)
			{
				
				
				if (!argv[firstArg].ToString().StartsWith("-"))
				{
					break;
				}
				int opt = TclIndex.get(interp, argv[firstArg], validOptions, "switch", 1);
				switch (opt)
				{
					
					case OPT_NOCOMPLAIN: 
						noComplain = true;
						break;
					
					case OPT_LAST: 
						last = true;
						break;
					
					default: 
						throw new TclException(interp, "GlobCmd.cmdProc: bad option " + opt + " index to validOptions");
					
				}
			}
			
			if (firstArg >= argv.Length)
			{
				throw new TclNumArgsException(interp, 1, argv, "?switches? name ?name ...?");
			}
			
			resultList = TclList.newInstance();
			resultList.preserve();
			
			for (i = firstArg; i < argv.Length; i++)
			{
				
				arg = argv[i].ToString();
				
				string separators; // The system-specific file separators
				switch (JACL.PLATFORM)
				{
					
					case JACL.PLATFORM_WINDOWS: 
						separators = "/\\:";
						break;
					
					case JACL.PLATFORM_MAC: 
						if (arg.IndexOf((System.Char) ':') == - 1)
						{
							separators = "/";
						}
						else
						{
							separators = ":";
						}
						break;
					
					default: 
						separators = "/";
						break;
					
				}
				
				// Perform tilde substitution, if needed.
				
				index = 0;
				if (arg.StartsWith("~"))
				{
					// Find the first path separator after the tilde.
					
					for (; index < arg.Length; index++)
					{
						char c = arg[index];
						if (c == '\\')
						{
							if (separators.IndexOf((System.Char) arg[index + 1]) != - 1)
							{
								break;
							}
						}
						else if (separators.IndexOf((System.Char) c) != - 1)
						{
							break;
						}
					}
					
					// Determine the home directory for the specified user.  Note 
					// that we don't allow special characters in the user name.
					
					if (strpbrk(arg.Substring(1, (index) - (1)).ToCharArray(), specCharArr) < 0)
					{
						try
						{
							head = FileUtil.doTildeSubst(interp, arg.Substring(1, (index) - (1)));
						}
						catch (TclException e)
						{
							if (noComplain)
							{
								head = null;
							}
							else
							{
								throw new TclException(interp, e.Message);
							}
						}
					}
					else
					{
						if (!noComplain)
						{
							throw new TclException(interp, "globbing characters not supported in user names");
						}
						head = null;
					}
					
					if ((System.Object) head == null)
					{
						if (noComplain)
						{
							interp.setResult("");
              return TCL.CompletionCode.RETURN;
            }
						else
						{
              return TCL.CompletionCode.RETURN;
            }
					}
					if (index != arg.Length)
					{
						index++;
					}
				}
				
				tail = arg.Substring(index);
				
				try
				{
					doGlob(interp, separators, new System.Text.StringBuilder(head), tail, resultList);
				}
				catch (TclException e)
				{
					if (noComplain)
					{
						continue;
					}
					else
					{
						throw new TclException(interp, e.Message);
					}
				}
			}
			
			// If the list is empty and the nocomplain switch was not set then
			// generate and throw an exception.  Always release the TclList upon
			// completion.
			
			try
			{
				if ((TclList.getLength(interp, resultList) == 0) && !noComplain)
				{
					string sep = "";
					System.Text.StringBuilder ret = new System.Text.StringBuilder();
					
					ret.Append("no files matched glob pattern");
					ret.Append((argv.Length == 2)?" \"":"s \"");
					
					for (i = firstArg; i < argv.Length; i++)
					{
						
						ret.Append(sep + argv[i].ToString());
						if (i == firstArg)
						{
							sep = " ";
						}
					}
					ret.Append("\"");
					throw new TclException(interp, ret.ToString());
				}
				else if (TclList.getLength(interp, resultList) > 0)
				{
					interp.setResult(resultList);
				}
			}
			finally
			{
				resultList.release();
			}
      return TCL.CompletionCode.RETURN;
    }
		private static int SkipToChar(string str, int sIndex, char match)
		// Ccharacter to find.
		{
			int level, length, i;
			bool quoted = false;
			char c;
			
			level = 0;
			
			for (i = sIndex, length = str.Length; i < length; i++)
			{
				if (quoted)
				{
					quoted = false;
					continue;
				}
				c = str[i];
				if ((level == 0) && (c == match))
				{
					return i;
				}
				if (c == '{')
				{
					level++;
				}
				else if (c == '}')
				{
					level--;
				}
				else if (c == '\\')
				{
					quoted = true;
				}
			}
			return - 1;
		}
		private static void  doGlob(Interp interp, string separators, System.Text.StringBuilder headBuf, string tail, TclObject resultList)
		{
			int count = 0; // Counts the number of leading file 
			//   spearators for the tail. 
			int pIndex; // Current index into tail 
			int tailIndex; // First char after initial file 
			//   separators of the tail 
			int tailLen = tail.Length; // Cache the length of the tail 
			int headLen = headBuf.Length; // Cache the length of the head 
			int baseLen; // Len of the substring from tailIndex
			//   to the current specChar []*?{}\\ 
			int openBraceIndex; // Index of the current open brace 
			int closeBraceIndex; // Index of the current closed brace 
			int firstSpecCharIndex; // Index of the FSC, if any 
			char lastChar = (char) (0); // Used to see if last char is a file
			//   separator. 
			char ch; // Generic storage variable 
			bool quoted; // True if a char is '\\' 
			
			if (headLen > 0)
			{
				lastChar = headBuf[headLen - 1];
			}
			
			// Consume any leading directory separators, leaving tailIndex
			// just past the last initial separator.
			
			string name = tail;
			for (tailIndex = 0; tailIndex < tailLen; tailIndex++)
			{
				char c = tail[tailIndex];
				if ((c == '\\') && ((tailIndex + 1) < tailLen) && (separators.IndexOf((System.Char) tail[tailIndex + 1]) != - 1))
				{
					tailIndex++;
				}
				else if (separators.IndexOf((System.Char) c) == - 1)
				{
					break;
				}
				count++;
			}
			
			// Deal with path separators.  On the Mac, we have to watch out
			// for multiple separators, since they are special in Mac-style
			// paths.
			
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_MAC: 
					
					if (separators[0] == '/')
					{
						if (((headLen == 0) && (count == 0)) || ((headLen > 0) && (lastChar != ':')))
						{
							headBuf.Append(":");
						}
					}
					else
					{
						if (count == 0)
						{
							if ((headLen > 0) && (lastChar != ':'))
							{
								headBuf.Append(":");
							}
						}
						else
						{
							if (lastChar == ':')
							{
								count--;
							}
							while (count-- > 0)
							{
								headBuf.Append(":");
							}
						}
					}
					break;
				
				
				case JACL.PLATFORM_WINDOWS: 
					if (name.StartsWith(":"))
					{
						headBuf.Append(":");
						if (count > 1)
						{
							headBuf.Append("/");
						}
					}
					else if ((tailIndex < tailLen) && (((headLen > 0) && (separators.IndexOf((System.Char) lastChar) == - 1)) || ((headLen == 0) && (count > 0))))
					{
						headBuf.Append("/");
						if ((headLen == 0) && (count > 1))
						{
							headBuf.Append("/");
						}
					}
					break;
				
				default: 
					
					if ((tailIndex < tailLen) && (((headLen > 0) && (separators.IndexOf((System.Char) lastChar) == - 1)) || ((headLen == 0) && (count > 0))))
					{
						headBuf.Append("/");
					}
					break;
				
			}
			
			// Look for the first matching pair of braces or the first
			// directory separator that is not inside a pair of braces.
			
			openBraceIndex = closeBraceIndex = - 1;
			quoted = false;
			
			for (pIndex = tailIndex; pIndex != tailLen; pIndex++)
			{
				ch = tail[pIndex];
				if (quoted)
				{
					quoted = false;
				}
				else if (ch == '\\')
				{
					quoted = true;
					if (((pIndex + 1) < tailLen) && (separators.IndexOf((System.Char) tail[pIndex + 1]) != - 1))
					{
						// Quoted directory separator. 
						
						break;
					}
				}
				else if (separators.IndexOf((System.Char) ch) != - 1)
				{
					// Unquoted directory separator. 
					
					break;
				}
				else if (ch == '{')
				{
					openBraceIndex = pIndex;
					pIndex++;
					if ((closeBraceIndex = SkipToChar(tail, pIndex, '}')) != - 1)
					{
						break;
					}
					throw new TclException(interp, "unmatched open-brace in file name");
				}
				else if (ch == '}')
				{
					throw new TclException(interp, "unmatched close-brace in file name");
				}
			}
			
			// Substitute the alternate patterns from the braces and recurse.
			
			if (openBraceIndex != - 1)
			{
				int nextIndex;
				System.Text.StringBuilder baseBuf = new System.Text.StringBuilder();
				
				// For each element within in the outermost pair of braces,
				// append the element and the remainder to the fixed portion
				// before the first brace and recursively call doGlob.
				
				baseBuf.Append(tail.Substring(tailIndex, (openBraceIndex) - (tailIndex)));
				baseLen = baseBuf.Length;
				headLen = headBuf.Length;
				
				for (pIndex = openBraceIndex; pIndex < closeBraceIndex; )
				{
					pIndex++;
					nextIndex = SkipToChar(tail, pIndex, ',');
					if (nextIndex == - 1 || nextIndex > closeBraceIndex)
					{
						nextIndex = closeBraceIndex;
					}
					
					headBuf.Length = headLen;
					baseBuf.Length = baseLen;
					
					baseBuf.Append(tail.Substring(pIndex, (nextIndex) - (pIndex)));
					baseBuf.Append(tail.Substring(closeBraceIndex + 1));
					
					pIndex = nextIndex;
					doGlob(interp, separators, headBuf, baseBuf.ToString(), resultList);
				}
				return ;
			}
			
			// At this point, there are no more brace substitutions to perform on
			// this path component.  The variable p is pointing at a quoted or
			// unquoted directory separator or the end of the string.  So we need
			// to check for special globbing characters in the current pattern.
			// We avoid modifying tail if p is pointing at the end of the string.
			
			if (pIndex < tailLen)
			{
				firstSpecCharIndex = strpbrk(tail.Substring(0, (pIndex) - (0)).ToCharArray(), specCharArr);
			}
			else
			{
				firstSpecCharIndex = strpbrk(tail.Substring(tailIndex).ToCharArray(), specCharArr);
			}
			
			if (firstSpecCharIndex != - 1)
			{
				// Look for matching files in the current directory.  matchFiles
				// may recursively call TclDoGlob.  For each file that matches,
				// it will add the match onto the interp.result, or call TclDoGlob
				// if there are more characters to be processed.
				
				matchFiles(interp, separators, headBuf.ToString(), tail.Substring(tailIndex), (pIndex - tailIndex), resultList);
				return ;
			}
			headBuf.Append(tail.Substring(tailIndex, (pIndex) - (tailIndex)));
			if (pIndex < tailLen)
			{
				doGlob(interp, separators, headBuf, tail.Substring(pIndex), resultList);
				return ;
			}
			
			// There are no more wildcards in the pattern and no more unprocessed
			// characters in the tail, so now we can construct the path and verify
			// the existence of the file.
			
			string head;
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_MAC: 
					if (headBuf.ToString().IndexOf((System.Char) ':') == - 1)
					{
						headBuf.Append(":");
					}
					head = headBuf.ToString();
					break;
				
				case JACL.PLATFORM_WINDOWS: 
					if (headBuf.Length == 0)
					{
						if (((name.Length > 1) && (name[0] == '\\') && ((name[1] == '/') || (name[1] == '\\'))) || ((name.Length > 0) && (name[0] == '/')))
						{
							headBuf.Append("\\");
						}
						else
						{
							headBuf.Append(".");
						}
					}
					head = headBuf.ToString().Replace('\\', '/');
					break;
				
				default: 
					if (headBuf.Length == 0)
					{
						if (name.StartsWith("\\/") || name.StartsWith("/"))
						{
							headBuf.Append("/");
						}
						else
						{
							headBuf.Append(".");
						}
					}
					head = headBuf.ToString();
					break;
				
			}
			addFileToResult(interp, head, separators, resultList);
		}
		private static void  matchFiles(Interp interp, string separators, string dirName, string pattern, int pIndex, TclObject resultList)
		{
			bool matchHidden; // True if were matching hidden file 
			int patternEnd = pIndex; // Stores end index of the pattern 
			int dirLen = dirName.Length; // Caches the len of the dirName 
			int patLen = pattern.Length; // Caches the len of the pattern 
			string[] dirListing; // Listing of files in dirBuf 
			System.IO.FileInfo dirObj; // File object of dirBuf 
			System.Text.StringBuilder dirBuf = new System.Text.StringBuilder();
			// Converts the dirName to string 
			//   buffer or initializes it with '.' 
			
			switch (JACL.PLATFORM)
			{
				
				case JACL.PLATFORM_WINDOWS: 
					
					if (dirLen == 0)
					{
						dirBuf.Append("./");
					}
					else
					{
						dirBuf.Append(dirName);
						char c = dirBuf[dirLen - 1];
						if (((c == ':') && (dirLen == 2)) || (separators.IndexOf((System.Char) c) == - 1))
						{
							dirBuf.Append("/");
						}
					}
					
					// All comparisons should be case insensitive on Windows.
					
					pattern = pattern.ToLower();
					break;
				
				case JACL.PLATFORM_MAC: 
				// Fall through to unix case--mac is not yet implemented.
				default: 
					
					if (dirLen == 0)
					{
						dirBuf.Append(".");
					}
					else
					{
						dirBuf.Append(dirName);
					}
					break;
				}
			
			dirObj = createAbsoluteFileObj(interp, dirBuf.ToString());
			if (!System.IO.Directory.Exists(dirObj.FullName))
			{
				return ;
			}
			
			// Check to see if the pattern needs to compare with hidden files.
			// Get a list of the directory's contents.
			
			if (pattern.StartsWith(".") || pattern.StartsWith("\\."))
			{
				matchHidden = true;
				// TODO tcl await only file names
				dirListing = addHiddenToDirList(dirObj);
			}
			else
			{
				matchHidden = false;
				DirectoryInfo dirInfo = new System.IO.DirectoryInfo(dirObj.FullName);
				FileSystemInfo[] fileInfos = dirInfo.GetFileSystemInfos();
				// TCL await only file names
                // dirListing = System.IO.Directory.GetFileSystemEntries(dirObj.FullName);
				dirListing = new string[fileInfos.Length];
				for (int x=0;x<fileInfos.Length;x++) 
				{
					dirListing[x] = fileInfos[x].Name;
				}
			}
			
			// Iterate over the directory's contents.
			
			if (dirListing.Length == 0)
			{
				// Strip off a trailing '/' if necessary, before reporting 
				// the error.
				
				if (dirName.EndsWith("/"))
				{
					dirName = dirName.Substring(0, ((dirLen - 1)) - (0));
				}
			}
			
			// Clean up the end of the pattern and the tail pointer.  Leave
			// the tail pointing to the first character after the path 
			// separator following the pattern, or NULL.  Also, ensure that
			// the pattern is null-terminated.
			
			if ((pIndex < patLen) && (pattern[pIndex] == '\\'))
			{
				pIndex++;
			}
			if (pIndex < (patLen - 1))
			{
				pIndex++;
			}
			
			for (int i = 0; i < dirListing.Length; i++)
			{
				// Don't match names starting with "." unless the "." is
				// present in the pattern.
				
				if (!matchHidden && (dirListing[i].StartsWith(".")))
				{
					continue;
				}
				
				// Now check to see if the file matches.  If there are more
				// characters to be processed, then ensure matching files are
				// directories before calling TclDoGlob. Otherwise, just add
				// the file to the resultList.
				
				string tmp = dirListing[i];
				if (JACL.PLATFORM == JACL.PLATFORM_WINDOWS)
				{
					tmp = tmp.ToLower();
				}
				if (Util.stringMatch(tmp, pattern.Substring(0, (patternEnd) - (0))))
				{
					
					dirBuf.Length = dirLen;
					dirBuf.Append(dirListing[i]);
					if (pIndex == pattern.Length)
					{
						addFileToResult(interp, dirBuf.ToString(), separators, resultList);
					}
					else
					{
						dirObj = createAbsoluteFileObj(interp, dirBuf.ToString());
						if (System.IO.Directory.Exists(dirObj.FullName))
						{
							dirBuf.Append("/");
							doGlob(interp, separators, dirBuf, pattern.Substring(patternEnd + 1), resultList);
						}
					}
				}
			}
		}
		private static int strpbrk(char[] src, char[] matches)
		// The chars to search for in src.
		{
			for (int i = 0; i < src.Length; i++)
			{
				for (int j = 0; j < matches.Length; j++)
				{
					if (src[i] == matches[j])
					{
						return (i);
					}
				}
			}
			return - 1;
		}
		private static string[] addHiddenToDirList(System.IO.FileInfo dirObj)
		// File object to list contents of
		{
			string[] dirListing; // Listing of files in dirObj
			string[] fullListing; // dirListing + .. and .
			int i, arrayLen;
			
			
			dirListing = System.IO.Directory.GetFileSystemEntries(dirObj.FullName);
			arrayLen = ((System.Array) dirListing).Length;
			
			
			try
			{
				
				fullListing = (string[]) System.Array.CreateInstance(System.Type.GetType("java.lang.String"), arrayLen + 2);
			}
			catch (System.Exception e)
			{
				return dirListing;
			}
			for (i = 0; i < arrayLen; i++)
			{
				fullListing[i] = dirListing[i];
			}
			fullListing[arrayLen] = ".";
			fullListing[arrayLen + 1] = "..";
			
			return fullListing;
		}
		private static void  addFileToResult(Interp interp, string fileName, string separators, TclObject resultList)
		{
			string prettyFileName = fileName;
			int prettyLen = fileName.Length;
			
			// Java IO reuqires Windows volumes [A-Za-z]: to be followed by '\\'.
			
			if ((JACL.PLATFORM == JACL.PLATFORM_WINDOWS) && (prettyLen >= 2) && (fileName[1] == ':'))
			{
				if (prettyLen == 2)
				{
					fileName = fileName + '\\';
				}
				else if (fileName[2] != '\\')
				{
					fileName = fileName.Substring(0, (2) - (0)) + '\\' + fileName.Substring(2);
				}
			}
			
			TclObject[] arrayObj = TclList.getElements(interp, FileUtil.splitAndTranslate(interp, fileName));
			fileName = FileUtil.joinPath(interp, arrayObj, 0, arrayObj.Length);
			
			System.IO.FileInfo f;
			if (FileUtil.getPathType(fileName) == FileUtil.PATH_ABSOLUTE)
			{
				f = FileUtil.getNewFileObj(interp, fileName);
			}
			else
			{
				f = new System.IO.FileInfo(interp.getWorkingDir().FullName + "\\" + fileName);
			}
			
			// If the last character is a spearator, make sure the file is an
			// existing directory, otherwise check that the file exists.
			
			if ((prettyLen > 0) && (separators.IndexOf((System.Char) prettyFileName[prettyLen - 1]) != - 1))
			{
				if (System.IO.Directory.Exists(f.FullName))
				{
					TclList.append(interp, resultList, TclString.newInstance(prettyFileName));
				}
			}
			else
			{
				bool tmpBool;
				if (System.IO.File.Exists(f.FullName))
					tmpBool = true;
				else
					tmpBool = System.IO.Directory.Exists(f.FullName);
				if (tmpBool)
				{
					TclList.append(interp, resultList, TclString.newInstance(prettyFileName));
				}
			}
		}
		private static System.IO.FileInfo createAbsoluteFileObj(Interp interp, string fileName)
		{
			if (fileName.Equals(""))
			{
				return (interp.getWorkingDir());
			}
			
			if ((JACL.PLATFORM == JACL.PLATFORM_WINDOWS) && (fileName.Length >= 2) && (fileName[1] == ':'))
			{
				string tmp = null;
				if (fileName.Length == 2)
				{
					tmp = fileName.Substring(0, (2) - (0)) + '\\';
				}
				else if (fileName[2] != '\\')
				{
					tmp = fileName.Substring(0, (2) - (0)) + '\\' + fileName.Substring(2);
				}
				if ((System.Object) tmp != null)
				{
					return FileUtil.getNewFileObj(interp, tmp);
				}
			}
			
			return FileUtil.getNewFileObj(interp, fileName);
		}
	} // end GlobCmd class
}
