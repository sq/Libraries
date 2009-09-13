/* 
* PackageCmd.java --
*
*	This class implements the built-in "package" command in Tcl.
*
* Copyright (c) 1997 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: PackageCmd.java,v 1.4 2002/04/12 21:00:26 mdejong Exp $
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	class PackageCmd : Command
	{
		
		private static readonly string[] validCmds = new string[]{"forget", "ifneeded", "names", "present", "provide", "require", "unknown", "vcompare", "versions", "vsatisfies"};
		
		private const int OPT_FORGET = 0;
		private const int OPT_IFNEEDED = 1;
		private const int OPT_NAMES = 2;
		private const int OPT_PRESENT = 3;
		private const int OPT_PROVIDE = 4;
		private const int OPT_REQUIRE = 5;
		private const int OPT_UNKNOWN = 6;
		private const int OPT_VCOMPARE = 7;
		private const int OPT_VERSIONS = 8;
		private const int OPT_VSATISFIES = 9;
		internal static void  pkgProvide(Interp interp, string pkgName, string version)
		{
			Package pkg;
			
			// Validate the version string that was passed in.
			
			checkVersion(interp, version);
			pkg = findPackage(interp, pkgName);
			if ((System.Object) pkg.version == null)
			{
				pkg.version = version;
				return ;
			}
			if (compareVersions(pkg.version, version, null) != 0)
			{
				throw new TclException(interp, "conflicting versions provided for package \"" + pkgName + "\": " + pkg.version + ", then " + version);
			}
		}
		internal static string pkgRequire(Interp interp, string pkgName, string version, bool exact)
		{
			VersionSatisfiesResult vsres;
			Package pkg;
			PkgAvail avail, best;
			string script;
			System.Text.StringBuilder sbuf;
			int pass, result;
			
			// Do extra check to make sure that version is not
			// null when the exact flag is set to true.
			
			if ((System.Object) version == null && exact)
			{
				throw new TclException(interp, "conflicting arguments : version == null and exact == true");
			}
			
			// Before we can compare versions the version string
			// must be verified but if it is null we are just looking
			// for the latest version so skip the check in this case.
			
			if ((System.Object) version != null)
			{
				checkVersion(interp, version);
			}
			
			// It can take up to three passes to find the package:  one pass to
			// run the "package unknown" script, one to run the "package ifneeded"
			// script for a specific version, and a final pass to lookup the
			// package loaded by the "package ifneeded" script.
			
			vsres = new VersionSatisfiesResult();
			for (pass = 1; ; pass++)
			{
				pkg = findPackage(interp, pkgName);
				if ((System.Object) pkg.version != null)
				{
					break;
				}
				
				// The package isn't yet present.  Search the list of available
				// versions and invoke the script for the best available version.
				
				best = null;
				for (avail = pkg.avail; avail != null; avail = avail.next)
				{
					if ((best != null) && (compareVersions(avail.version, best.version, null) <= 0))
					{
						continue;
					}
					if ((System.Object) version != null)
					{
						result = compareVersions(avail.version, version, vsres);
						if ((result != 0) && exact)
						{
							continue;
						}
						if (!vsres.satisfies)
						{
							continue;
						}
					}
					best = avail;
				}
				if (best != null)
				{
					// We found an ifneeded script for the package.  Be careful while
					// executing it:  this could cause reentrancy, so (a) protect the
					// script itself from deletion and (b) don't assume that best
					// will still exist when the script completes.
					
					script = best.script;
					try
					{
						interp.eval(script, TCL.EVAL_GLOBAL);
					}
					catch (TclException e)
					{
						interp.addErrorInfo("\n    (\"package ifneeded\" script)");
						
						// Throw the error with new info added to errorInfo.
						
						throw ;
					}
					interp.resetResult();
					pkg = findPackage(interp, pkgName);
					break;
				}
				
				// Package not in the database.  If there is a "package unknown"
				// command, invoke it (but only on the first pass;  after that,
				// we should not get here in the first place).
				
				if (pass > 1)
				{
					break;
				}
				script = interp.packageUnknown;
				if ((System.Object) script != null)
				{
					sbuf = new System.Text.StringBuilder();
					try
					{
						Util.appendElement(interp, sbuf, script);
						Util.appendElement(interp, sbuf, pkgName);
						if ((System.Object) version == null)
						{
							Util.appendElement(interp, sbuf, "");
						}
						else
						{
							Util.appendElement(interp, sbuf, version);
						}
						if (exact)
						{
							Util.appendElement(interp, sbuf, "-exact");
						}
					}
					catch (TclException e)
					{
						throw new TclRuntimeError("unexpected TclException: " + e.Message);
					}
					try
					{
						interp.eval(sbuf.ToString(), TCL.EVAL_GLOBAL);
					}
					catch (TclException e)
					{
						interp.addErrorInfo("\n    (\"package unknown\" script)");
						
						// Throw the first exception.
						
						throw ;
					}
					interp.resetResult();
				}
			}
			if ((System.Object) pkg.version == null)
			{
				sbuf = new System.Text.StringBuilder();
				sbuf.Append("can't find package " + pkgName);
				if ((System.Object) version != null)
				{
					sbuf.Append(" " + version);
				}
				throw new TclException(interp, sbuf.ToString());
			}
			
			// At this point we know that the package is present.  Make sure that the
			// provided version meets the current requirement.
			
			if ((System.Object) version == null)
			{
				return pkg.version;
			}
			
			result = compareVersions(pkg.version, version, vsres);
			if ((vsres.satisfies && !exact) || (result == 0))
			{
				return pkg.version;
			}
			
			// If we have a version conflict we throw a TclException.
			
			throw new TclException(interp, "version conflict for package \"" + pkgName + "\": have " + pkg.version + ", need " + version);
		}
		internal static string pkgPresent(Interp interp, string pkgName, string version, bool exact)
		{
			Package pkg;
			VersionSatisfiesResult vsres = new VersionSatisfiesResult();
			int result;
			
			pkg = (Package) interp.packageTable[pkgName];
			if (pkg != null)
			{
				if ((System.Object) pkg.version != null)
				{
					
					// At this point we know that the package is present.  Make sure
					// that the provided version meets the current requirement.
					
					if ((System.Object) version == null)
					{
						return pkg.version;
					}
					result = compareVersions(pkg.version, version, vsres);
					if ((vsres.satisfies && !exact) || (result == 0))
					{
						return pkg.version;
					}
					throw new TclException(interp, "version conflict for package \"" + pkgName + "\": have " + pkg.version + ", need " + version);
				}
			}
			
			if ((System.Object) version != null)
			{
				throw new TclException(interp, "package " + pkgName + " " + version + " is not present");
			}
			else
			{
				throw new TclException(interp, "package " + pkgName + " is not present");
			}
		}
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			VersionSatisfiesResult vsres;
			Package pkg;
			PkgAvail avail;
			PkgAvail prev;
			string version;
			string pkgName;
			string key;
			string cmd;
			string ver1, ver2;
			System.Text.StringBuilder sbuf;
			IDictionaryEnumerator enum_Renamed;
			int i, opt, exact;
			bool once;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "option ?arg arg ...?");
			}
			opt = TclIndex.get(interp, objv[1], validCmds, "option", 0);
			switch (opt)
			{
				
				case OPT_FORGET:  {
						// Forget takes 0 or more arguments.
						
						for (i = 2; i < objv.Length; i++)
						{
							// We do not need to check to make sure
							// package name is "" because it would not
							// be in the hash table so name will be ignored.
							
							
							pkgName = objv[i].ToString();
							pkg = (Package) interp.packageTable[pkgName];
							
							// If this package does not exist, go to next one.
							
							if (pkg == null)
							{
								continue;
							}
							SupportClass.HashtableRemove(interp.packageTable, pkgName);
							while (pkg.avail != null)
							{
								avail = pkg.avail;
								pkg.avail = avail.next;
								avail = null;
							}
							pkg = null;
						}
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_IFNEEDED:  {
						if ((objv.Length < 4) || (objv.Length > 5))
						{
							throw new TclNumArgsException(interp, 1, objv, "ifneeded package version ?script?");
						}
						pkgName = objv[2].ToString();
						version = objv[3].ToString();
						
						// Verify that this version string is valid.
						
						checkVersion(interp, version);
						if (objv.Length == 4)
						{
							pkg = (Package) interp.packageTable[pkgName];
							if (pkg == null)
                return TCL.CompletionCode.RETURN;
						}
						else
						{
							pkg = findPackage(interp, pkgName);
						}
						for (avail = pkg.avail, prev = null; avail != null; prev = avail, avail = avail.next)
						{
							if (compareVersions(avail.version, version, null) == 0)
							{
								if (objv.Length == 4)
								{
									// If doing a query return current script.
									
									interp.setResult(avail.script);
                  return TCL.CompletionCode.RETURN;
								}
								
								// We matched so we must be setting the script.
								
								break;
							}
						}
						
						// When we do not match on a query return nothing.
						
						if (objv.Length == 4)
						{
              return TCL.CompletionCode.RETURN;
						}
						if (avail == null)
						{
							avail = new PkgAvail();
							avail.version = version;
							if (prev == null)
							{
								avail.next = pkg.avail;
								pkg.avail = avail;
							}
							else
							{
								avail.next = prev.next;
								prev.next = avail;
							}
						}
						
						avail.script = objv[4].ToString();
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_NAMES:  {
						if (objv.Length != 2)
						{
							throw new TclNumArgsException(interp, 1, objv, "names");
						}
						
						try
						{
							sbuf = new System.Text.StringBuilder();
							enum_Renamed = interp.packageTable.GetEnumerator();
							once = false;
							while (enum_Renamed.MoveNext())
							{
								once = true;
								key = ((string) enum_Renamed.Current);
								pkg = (Package) enum_Renamed.Value;
								if (((System.Object) pkg.version != null) || (pkg.avail != null))
								{
									Util.appendElement(interp, sbuf, key);
								}
							}
							if (once)
							{
								interp.setResult(sbuf.ToString());
							}
						}
						catch (TclException e)
						{
							
							throw new TclRuntimeError("unexpected TclException: " + e);
						}
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_PRESENT:  {
						if (objv.Length < 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "?-exact? package ?version?");
						}
						
						if (objv[2].ToString().Equals("-exact"))
						{
							exact = 1;
						}
						else
						{
							exact = 0;
						}
						
						version = null;
						if (objv.Length == (4 + exact))
						{
							
							version = objv[3 + exact].ToString();
							checkVersion(interp, version);
						}
						else if ((objv.Length != 3) || (exact == 1))
						{
							throw new TclNumArgsException(interp, 2, objv, "?-exact? package ?version?");
						}
						if (exact == 1)
						{
							
							version = pkgPresent(interp, objv[3].ToString(), version, true);
						}
						else
						{
							
							version = pkgPresent(interp, objv[2].ToString(), version, false);
						}
						interp.setResult(version);
						break;
					}
				
				case OPT_PROVIDE:  {
						if ((objv.Length < 3) || (objv.Length > 4))
						{
							throw new TclNumArgsException(interp, 1, objv, "provide package ?version?");
						}
						if (objv.Length == 3)
						{
							
							pkg = (Package) interp.packageTable[objv[2].ToString()];
							if (pkg != null)
							{
								if ((System.Object) pkg.version != null)
								{
									interp.setResult(pkg.version);
								}
							}
              return TCL.CompletionCode.RETURN;
						}
						
						pkgProvide(interp, objv[2].ToString(), objv[3].ToString());
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_REQUIRE:  {
						if ((objv.Length < 3) || (objv.Length > 5))
						{
							throw new TclNumArgsException(interp, 1, objv, "require ?-exact? package ?version?");
						}
						
						if (objv[2].ToString().Equals("-exact"))
						{
							exact = 1;
						}
						else
						{
							exact = 0;
						}
						version = null;
						if (objv.Length == (4 + exact))
						{
							
							version = objv[3 + exact].ToString();
							checkVersion(interp, version);
						}
						else if ((objv.Length != 3) || (exact == 1))
						{
							throw new TclNumArgsException(interp, 1, objv, "require ?-exact? package ?version?");
						}
						if (exact == 1)
						{
							
							version = pkgRequire(interp, objv[3].ToString(), version, true);
						}
						else
						{
							
							version = pkgRequire(interp, objv[2].ToString(), version, false);
						}
						interp.setResult(version);
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_UNKNOWN:  {
						if (objv.Length > 3)
						{
							throw new TclNumArgsException(interp, 1, objv, "unknown ?command?");
						}
						if (objv.Length == 2)
						{
							if ((System.Object) interp.packageUnknown != null)
							{
								interp.setResult(interp.packageUnknown);
							}
						}
						else if (objv.Length == 3)
						{
							interp.packageUnknown = null;
							
							cmd = objv[2].ToString();
							if (cmd.Length > 0)
							{
								interp.packageUnknown = cmd;
							}
						}
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_VCOMPARE:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 1, objv, "vcompare version1 version2");
						}
						
						ver1 = objv[2].ToString();
						
						ver2 = objv[3].ToString();
						checkVersion(interp, ver1);
						checkVersion(interp, ver2);
						interp.setResult(compareVersions(ver1, ver2, null));
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_VERSIONS:  {
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 1, objv, "versions package");
						}
						
						pkg = (Package) interp.packageTable[objv[2].ToString()];
						if (pkg != null)
						{
							try
							{
								sbuf = new System.Text.StringBuilder();
								once = false;
								for (avail = pkg.avail; avail != null; avail = avail.next)
								{
									once = true;
									Util.appendElement(interp, sbuf, avail.version);
								}
								if (once)
								{
									interp.setResult(sbuf.ToString());
								}
							}
							catch (TclException e)
							{
								throw new TclRuntimeError("unexpected TclException: " + e.Message,e);
							}
						}
            return TCL.CompletionCode.RETURN;
					}
				
				case OPT_VSATISFIES:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 1, objv, "vsatisfies version1 version2");
						}
						
						
						ver1 = objv[2].ToString();
						
						ver2 = objv[3].ToString();
						checkVersion(interp, ver1);
						checkVersion(interp, ver2);
						vsres = new VersionSatisfiesResult();
						compareVersions(ver1, ver2, vsres);
						interp.setResult(vsres.satisfies);
            return TCL.CompletionCode.RETURN;
					}
				
				default:  {
						throw new TclRuntimeError("TclIndex.get() error");
					}
				
			} // end switch(opt)
      return TCL.CompletionCode.RETURN;
    }
		private static Package findPackage(Interp interp, string pkgName)
		{
			Package pkg;
			
			// check package name to make sure it is not null or "".
			
			if ((System.Object) pkgName == null || pkgName.Length == 0)
			{
				throw new TclException(interp, "expected package name but got \"\"");
			}
			
			pkg = (Package) interp.packageTable[pkgName];
			if (pkg == null)
			{
				// We should add a package with this name.
				
				pkg = new Package();
				SupportClass.PutElement(interp.packageTable, pkgName, pkg);
			}
			return pkg;
		}
		private static void  checkVersion(Interp interp, string version)
		{
			int i, len;
			char c;
			bool error = true;
			
			try
			{
				if (((System.Object) version == null) || (version.Length == 0))
				{
					version = "";
					return ;
				}
				if (!System.Char.IsDigit(version[0]))
				{
					return ;
				}
				len = version.Replace(".C#","").Length;
				for (i = 1; i < len; i++)
				{
					c = version[i];
					if (!System.Char.IsDigit(c) && (c != '.'))
					{
						return ;
					}
				}
				if (version[len - 1] == '.')
				{
					return ;
				}
				error = false;
			}
			finally
			{
				if (error)
				{
					throw new TclException(interp, "expected version number but got \"" + version + "\"");
				}
			}
		}
		private static int compareVersions(string v1, string v2, VersionSatisfiesResult vsres)
		{
			int i;
			int max;
			int n1 = 0;
			int n2 = 0;
			bool thisIsMajor = true;
			string[] v1ns;
			string[] v2ns;
			
			// Each iteration of the following loop processes one number from
			// each string, terminated by a ".".  If those numbers don't match
			// then the comparison is over;  otherwise, we loop back for the
			// next number.
			
			
			// This should never happen because null strings would not
			// have gotten past the version verify.
			
			if (((System.Object) v1 == null) || ((System.Object) v2 == null))
			{
				throw new TclRuntimeError("null version in package version compare");
			}
			v1ns = split(v1, '.');
			v2ns = split(v2, '.');
			
			// We are sure there is at least one string in each array so 
			// this should never happen.
			
			if (v1ns.Length == 0 || v2ns.Length == 0)
			{
				throw new TclRuntimeError("version length is 0");
			}
			if (v1ns.Length > v2ns.Length)
			{
				max = v1ns.Length;
			}
			else
			{
				max = v2ns.Length;
			}
			
			for (i = 0; i < max; i++)
			{
				n1 = n2 = 0;
				
				// Grab number from each version ident if version spec
				// ends the use a 0 as value.
				
				try
				{
					if (i < v1ns.Length)
					{
						n1 = System.Int32.Parse(v1ns[i]);
					}
					if (i < v2ns.Length)
					{
						n2 = System.Int32.Parse(v2ns[i]);
					}
				}
				catch (System.FormatException ex)
				{
					throw new TclRuntimeError("NumberFormatException for package versions \"" + v1 + "\" or \"" + v2 + "\"");
				}
				
				// Compare and go on to the next version number if the
				// current numbers match.
				
				if (n1 != n2)
				{
					break;
				}
				thisIsMajor = false;
			}
			if (vsres != null)
			{
				vsres.satisfies = ((n1 == n2) || ((n1 > n2) && !thisIsMajor));
			}
			if (n1 > n2)
			{
				return 1;
			}
			else if (n1 == n2)
			{
				return 0;
			}
			else
			{
				return - 1;
			}
		}
		internal static string[] split(string in_Renamed, char splitchar)
		{
			ArrayList words;
			string[] ret;
			int i;
			int len;
			char[] str;
			int wordstart = 0;
			
			// Create an array that is as big as the input
			// str plus one for an extra split char.
			
			len = in_Renamed.Length;
			str = new char[len + 1];
			SupportClass.GetCharsFromString(in_Renamed, 0, len, ref str, 0);
			str[len++] = splitchar;
			words = new ArrayList(5);
			
			for (i = 0; i < len; i++)
			{
				
				// Compare this char to the split char
				// if they are the same the we need to
				// add the last word to the array.
				
				if (str[i] == splitchar)
				{
					if (wordstart <= (i - 1))
					{
						words.Add(new string(str, wordstart, i - wordstart));
					}
					wordstart = (i + 1);
				}
			}
			
			// Create an array that is as big as the number
			// of elements in the vector, copy over and return.
			
			ret = new string[words.Count];
			words.CopyTo(ret);
			return ret;
		}
		
		
		
		
		
		
		
		
		// If compare versions is called with a third argument then one of
		// these structures needs to be created and passed in
		
		
		internal class VersionSatisfiesResult
		{
			internal bool satisfies = false;
		}
		
		// Each invocation of the "package ifneeded" command creates a class
		// of the following type, which is used to load the package into the
		// interpreter if it is requested with a "package require" command.
		
		internal class PkgAvail
		{
			internal string version = null; // Version string.
			internal string script = null; // Script to invoke to provide this package version
			internal PkgAvail next = null; // Next in list of available package versions
		}
		
		
		
		// For each package that is known in any way to an interpreter, there
		// is one record of the following type.  These records are stored in
		// the "packageTable" hash table in the interpreter, keyed by
		// package name such as "Tk" (no version number).
		
		internal class Package
		{
			internal string version = null; // Version that has been supplied in this
			// interpreter via "package provide"
			// null means the package doesn't
			// exist in this interpreter yet.
			
			internal PkgAvail avail = null; // First in list of all available package versions
		}
	} //end of class PackageCmd
}
