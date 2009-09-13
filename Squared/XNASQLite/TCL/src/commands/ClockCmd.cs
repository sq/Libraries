#define DEBUG
/*
* ClockCmd.java --
*
*	Implements the built-in "clock" Tcl command.
*
* Copyright (c) 1998-2000 Christian Krone.
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1995-1997 Sun Microsystems, Inc.
* Copyright (c) 1992-1995 Karl Lehenbauer and Mark Diekhans.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ClockCmd.java,v 1.6 2003/02/03 04:48:46 mdejong Exp $
*
*/
using System;
using System.Globalization;
using System.Text;
using System.Collections;


namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "clock" command in Tcl.</summary>
	
	class ClockCmd : Command
	{
		
		private static readonly string[] validCmds = new string[]{"clicks", "format", "scan", "seconds"};
		
		private const int CMD_CLICKS = 0;
		private const int CMD_FORMAT = 1;
		private const int CMD_SCAN = 2;
		private const int CMD_SECONDS = 3;
		
		private static readonly string[] clicksOpts = new string[]{"-milliseconds"};
		
		private const int OPT_CLICKS_MILLISECONDS = 0;
		
		private static readonly string[] formatOpts = new string[]{"-format", "-gmt"};
		
		private const int OPT_FORMAT_FORMAT = 0;
		private const int OPT_FORMAT_GMT = 1;
		
		private static readonly string[] scanOpts = new string[]{"-base", "-gmt"};
		
		private const int OPT_SCAN_BASE = 0;
		private const int OPT_SCAN_GMT = 1;
		
		internal const int EPOCH_YEAR = 1970;
		internal const int MILLIS_PER_HOUR = 60 * 60 * 1000;
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			int clockVal; // Time value as seconds of epoch.
			string dateString; // Time value as string.
			int argIx; // Counter over arguments.
			string format = null; // User specified format string.
			bool useGmt = false; // User specified flag to use gmt.
			TclObject baseObj = null; // User specified raw value of baseClock.
			System.DateTime baseClock; // User specified time value.
			System.DateTime date; // Parsed date value.
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "option ?arg ...?");
			}
			int cmd = TclIndex.get(interp, objv[1], validCmds, "option", 0);
			
			switch (cmd)
			{
				
				case CMD_CLICKS:  {
						if (objv.Length > 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "?-milliseconds?");
						}
						if (objv.Length == 3)
						{
							// We can safely ignore the -milliseconds options, since
							// we measure the clicks in milliseconds anyway...
							int clicksOpt = TclIndex.get(interp, objv[2], clicksOpts, "switch", 0);
						}
						long millis = (System.DateTime.Now.Ticks - 621355968000000000) / 10000;
						int clicks = (int) (millis % System.Int32.MaxValue);
						interp.setResult(clicks);
						break;
					}
				
				
				case CMD_FORMAT:  {
						if ((objv.Length < 3) || (objv.Length > 7))
						{
							throw new TclNumArgsException(interp, 2, objv, "clockval ?-format string? ?-gmt boolean?");
						}
						clockVal = TclInteger.get(interp, objv[2]);
						
						for (argIx = 3; argIx + 1 < objv.Length; argIx += 2)
						{
							int formatOpt = TclIndex.get(interp, objv[argIx], formatOpts, "switch", 0);
							switch (formatOpt)
							{
								
								case OPT_FORMAT_FORMAT:  {
										
										format = objv[argIx + 1].ToString();
										break;
									}
								
								case OPT_FORMAT_GMT:  {
										useGmt = TclBoolean.get(interp, objv[argIx + 1]);
										break;
									}
								}
						}
						if (argIx < objv.Length)
						{
							throw new TclNumArgsException(interp, 2, objv, "clockval ?-format string? ?-gmt boolean?");
						}
						FormatClock(interp, clockVal, useGmt, format);
						break;
					}
				
				
				case CMD_SCAN:  {
						if ((objv.Length < 3) || (objv.Length > 7))
						{
							throw new TclNumArgsException(interp, 2, objv, "dateString ?-base clockValue? ?-gmt boolean?");
						}
						
						dateString = objv[2].ToString();
						
						for (argIx = 3; argIx + 1 < objv.Length; argIx += 2)
						{
							int scanOpt = TclIndex.get(interp, objv[argIx], scanOpts, "switch", 0);
							switch (scanOpt)
							{
								
								case OPT_SCAN_BASE:  {
										baseObj = objv[argIx + 1];
										break;
									}
								
								case OPT_SCAN_GMT:  {
										useGmt = TclBoolean.get(interp, objv[argIx + 1]);
										break;
									}
								}
						}
						if (argIx < objv.Length)
						{
							throw new TclNumArgsException(interp, 2, objv, "clockval ?-format string? ?-gmt boolean?");
						}
						if (baseObj != null)
						{
							long seconds = TclInteger.get(interp, baseObj);
							baseClock = new System.DateTime((long)seconds * 10000 * 1000 + 621355968000000000);
						}
						else
						{
							baseClock = System.DateTime.Now;
						}
						try {
							date = GetDate(dateString, baseClock, useGmt);
						}
						catch (FormatException) {
							throw new TclException(interp, "unable to convert date-time string \"" + dateString + "\"");
						}
						long millis = (date.Ticks - 621355968000000000) / 10000;
						int seconds2 = (int) (millis / 1000);
						interp.setResult(seconds2);
						break;
					}
				
				
				case CMD_SECONDS:  {
						if (objv.Length != 2)
						{
							throw new TclNumArgsException(interp, 2, objv, null);
						}
						long millis = (System.DateTime.Now.Ticks - 621355968000000000) / 10000;
						int seconds = (int) (millis / 1000);
						interp.setResult(seconds);
						break;
					}
				}
        return TCL.CompletionCode.RETURN;
      }
		private void  FormatClock(Interp interp, int clockVal, bool useGMT, string format)
		{
			DateTime date = new System.DateTime((long) clockVal * 10000 * 1000 + 621355968000000000);

			System.Globalization.DateTimeFormatInfo formatInfo = new System.Globalization.DateTimeFormatInfo();
			string fmt, locFmt;
			
			GregorianCalendar calendar = new GregorianCalendar();
			
			System.Int32[] temp_int_array;
			temp_int_array = new System.Int32[3];
			temp_int_array[0] = 0;
			temp_int_array[1] = 0;
			temp_int_array[2] = 0;
			System.Int32[] fp = temp_int_array;
			System.Text.StringBuilder result = new System.Text.StringBuilder();
			
			if ((System.Object) format == null)
			{
				format = new System.Text.StringBuilder("%a %b %d %H:%M:%S %Z %Y").ToString();
			}
			
			if (useGMT)
			{
				date = date.ToUniversalTime();
			}
			if (format.Equals("%Q"))
			{
				// Enterprise Stardate. (seems to be Star Track fan coding)
				// ATK not tested 
				int trekYear = date.Year + 377 - 2323;
				int trekDay = (date.DayOfYear * 1000) / (calendar.IsLeapYear(date.Year)?366:365);
				int trekHour = (24 * 60 + date.Minute) / 144;
				
				interp.setResult("Stardate " + (trekYear < 10?"0":"") + (trekYear * 1000 + trekDay) + '.' + trekHour);
				return ;
			}
			
			for (int ix = 0; ix < format.Length; ix++)
			{
				if (format[ix] == '%' && ix + 1 < format.Length)
				{
					switch (format[++ix])
					{
						
						case '%': 
							result.Append('%');
							break;
						
						case 'a': 
							result.Append(date.ToString("ddd",formatInfo));
							break;
						
						case 'A': 
							result.Append(date.ToString("dddd",formatInfo));
							break;
						case 'b': 
						case 'h': 
							result.Append(date.ToString("MMM",formatInfo));
							break;
						case 'B': 
							result.Append(date.ToString("MMMM",formatInfo));
							break;
						case 'c': 
							result.Append(date.ToString());
							break;
						case 'C': 
							int century = date.Year / 100;
							result.Append((century < 10?"0":"") + century);
							break;
						case 'd': 
							result.Append(date.ToString("dd",formatInfo));
							break;
						case 'D': 
							result.Append(date.ToString("MM/dd/yy",formatInfo));
							break;
						case 'e': 
							result.Append(date.ToString("%d",formatInfo));
							break;
						case 'H': 
							result.Append(date.ToString("HH",formatInfo));
							break;
						case 'I': 
							result.Append(date.ToString("hh",formatInfo));
							break;
						case 'j': 
							result.Append(date.Year.ToString("0###"));
							break;
						case 'k': 
							result.Append(date.ToString("H",formatInfo));
							break;
						case 'l': 
							result.Append(date.ToString("%h",formatInfo));
							break;
						case 'm': 
							// Month number (01 - 12). 
							result.Append(date.ToString("MM",formatInfo));
							break;
						case 'M': 
							// Minute (00 - 59). 
							result.Append(date.ToString("mm",formatInfo));
							break;
						case 'n': 
							// Insert a newline.
							result.Append('\n');
							break;
						case 'p': 
							// AM/PM indicator. 
							result.Append(date.ToString("tt",formatInfo));
							break;
						case 'r': 
							// %r 
							//Time in a locale-specific "meridian" format. The "meridian" format in the default "C" locale is "%I:%M:%S %p". 
							result.Append(date.ToString("hh:mm:ss tt",formatInfo));
							break;
						case 'R': 
							//%R 
							//Time as %H:%M. 
							result.Append(date.ToString("HH:MM",formatInfo));
							break;
						case 's': 
							//%s 
							//Count of seconds since the epoch, expressed as a decimal integer. 
							result.Append((date.Ticks/1000).ToString());
							break;
						
						case 'S': 
							//%S 
							//Seconds (00 - 59). 
							result.Append(date.ToString("ss",formatInfo));
							break;
						case 't': 
							//%t 
							//Insert a tab. 
							result.Append('\t');
							break;
						
						case 'T': 
							//%T 
							//Time as %H:%M:%S. 
							result.Append(date.ToString("HH:mm:ss",formatInfo));
							break;
						
						case 'u': 
							//%u 
							//Weekday number (Monday = 1, Sunday = 7). 
							if (date.DayOfWeek==DayOfWeek.Sunday) {
								result.Append("7");
							} else {
								result.Append(((int)date.DayOfWeek).ToString());
							}
							break;
						case 'U': 
							//%U 
							//Week of year (00 - 52), Sunday is the first day of the week. 
							int weekS = GetWeek(date, System.DayOfWeek.Sunday, false);
							result.Append((weekS < 10?"0":"") + weekS);
							break;
						
						case 'V': 
							//%V 
							//Week of year according to ISO-8601 rules. Week 1 of a given year is the week containing 4 January. 
							int isoWeek = GetWeek(date, System.DayOfWeek.Monday, true);
							result.Append((isoWeek < 10?"0":"") + isoWeek);
							break;
						
						case 'w': 
							//%w 
							//Weekday number (Sunday = 0, Saturday = 6). 
							result.Append(((int)date.DayOfWeek).ToString());
							break;
						
						case 'W': 
							//%W 
							//Week of year (00 - 52), Monday is the first day of the week. 
							int weekM = GetWeek(date, System.DayOfWeek.Monday, false);
							result.Append((weekM < 10?"0":"") + weekM);
							break;
						case 'x': 
							//%x 
							//Locale specific date format. The format for a date in the default "C" locale for Unix/Mac is "%m/%d/%y". On Windows, this value is the locale specific short date format, as specified in the Regional Options control panel settings. 
							result.Append(date.ToShortDateString());
							break;
						
						case 'X': 
							//%X 
							//Locale specific 24-hour time format. The format for a 24-hour time in the default "C" locale for Unix/Mac is "%H:%M:%S". On Windows, this value is the locale specific time format, as specified in the Regional Options control panel settings. 
							result.Append(date.ToShortTimeString());
							break;
						case 'y': 
							//%y 
							//Year without century (00 - 99). 
							result.Append(date.ToString("yy",formatInfo));
							break;
						
						case 'Y': 
							//%Y 
							//Year with century (e.g. 1990) 
							result.Append(date.ToString("yyyy",formatInfo));
							break;
						case 'Z': 
							//%Z 
							//Time zone name. 
							result.Append(date.ToString("zzz",formatInfo));
							break;
						default: 
							result.Append(format[ix]);
							break;
					}
				}
				else
				{
					result.Append(format[ix]);
				}
			}
			interp.setResult(result.ToString());
		}
		private int GetWeek(DateTime date, System.DayOfWeek firstDayOfWeek, bool iso)
		{
			GregorianCalendar cal = new GregorianCalendar();
			CalendarWeekRule weekRule = CalendarWeekRule.FirstFullWeek;
			if (iso)
			{
				firstDayOfWeek = System.DayOfWeek.Monday;
				weekRule = CalendarWeekRule.FirstFourDayWeek;
			}
			return cal.GetWeekOfYear(date, weekRule, firstDayOfWeek);
		}
		private void  SetWeekday(TclDateTime calendar, ClockRelTimespan diff)
		// time difference to evaluate
		{
			int weekday = diff.getWeekday();
			int dayOrdinal = diff.DayOrdinal;
			
			// ATK
//			while (SupportClass.CalendarManager.manager.Get(calendar, SupportClass.CalendarManager.DAY_OF_WEEK) != weekday)
//			{
//				
//				calendar.add(SupportClass.CalendarManager.DATE, 1);
//			}
//			if (dayOrdinal > 1)
//			{
//				
//				calendar.add(SupportClass.CalendarManager.DATE, 7 * (dayOrdinal - 1));
//			}
		}
		private void  SetOrdMonth(TclDateTime calendar, ClockRelTimespan diff)
		// time difference to evaluate
		{
			int month = diff.Months;
			int ordMonth = diff.OrdMonth;
			
//			calendar.add(SupportClass.CalendarManager.MONTH, 1); /* we want to get the next month... */
//			while (SupportClass.CalendarManager.manager.Get(calendar, SupportClass.CalendarManager.MONTH) != month)
//			{
//				calendar.add(SupportClass.CalendarManager.MONTH, 1);
//			}
//			if (ordMonth > 1)
//			{
//				calendar.add(SupportClass.CalendarManager.YEAR, ordMonth - 1);
//			}
			calendar.day = 1;
			calendar.hour = 0;
			calendar.minute = 0;
			calendar.second = 0;
		}
		private System.DateTime GetDate(string dateString, System.DateTime baseDate, bool useGMT)
		{
			if (useGMT)
			{
				baseDate = baseDate.ToUniversalTime();
			}
			TclDateTime calendar = new TclDateTime();
			calendar.dateTime = baseDate;
			calendar.hour = 0;
			calendar.minute = 0;
			calendar.second = 0;
			calendar.millisecond = 0;
			
			ClockToken[] dt = GetTokens(dateString, false);
			
			System.Int32 parsePos = 0;
			ClockRelTimespan diff = new ClockRelTimespan();
			int hasTime = 0;
			int hasZone = 0;
			int hasDate = 0;
			int hasDay = 0;
			int hasOrdMonth = 0;
			int hasRel = 0;
			
			while (parsePos < dt.Length)
			{
				if (ParseTime(dt, ref parsePos, calendar))
				{
					hasTime++;
				}
				else if (ParseZone(dt, ref parsePos, calendar))
				{
					hasZone++;
				}
				else if (ParseIso(dt, ref parsePos, calendar))
				{
					hasDate++;
				}
				else if (ParseDate(dt, ref parsePos, calendar))
				{
					hasDate++;
				}
				else if (ParseDay(dt, ref parsePos, diff))
				{
					hasDay++;
				}
				else if (ParseOrdMonth(dt, ref parsePos, diff))
				{
					hasOrdMonth++;
				}
				else if (ParseRelSpec(dt, ref parsePos, diff))
				{
					hasRel++;
				}
				else if (ParseNumber(dt, ref parsePos, calendar, hasDate > 0 && hasTime > 0 && hasRel == 0))
				{
					if (hasDate == 0 || hasTime == 0 || hasRel > 0)
					{
						hasTime++;
					}
				}
				else if (ParseTrek(dt, ref parsePos, calendar))
				{
					hasDate++; hasTime++;
				}
				else
				{
					goto failed;
				}
			}
			
			if (hasTime > 1 || hasZone > 1 || hasDate > 1 || hasDay > 1 || hasOrdMonth > 1)
			{
				goto failed;
			}
			
			// The following line handles years that are specified using
			// only two digits.  The line of code below implements a policy
			// defined by the X/Open workgroup on the millinium rollover.
			// Note: some of those dates may not actually be valid on some
			// platforms.  The POSIX standard startes that the dates 70-99
			// shall refer to 1970-1999 and 00-38 shall refer to 2000-2038.
			// This later definition should work on all platforms.
			
			int thisYear = calendar.year;
			if (thisYear < 100)
			{
				if (thisYear >= 69)
				{
					calendar.year = thisYear + 1900;
				}
				else
				{
					calendar.year = thisYear + 2000;
				}
			}
			
			if (hasRel > 0)
			{
				if (hasTime == 0 && hasDate == 0 && hasDay == 0)
				{
					calendar.dateTime = baseDate;
				}
				// Certain JDK implementations are buggy WRT DST.
				// Work around this issue by adding a day instead
				// of a days worth of seconds.
								int seconds_in_day = (60 * 60 * 24);
				int seconds = diff.Seconds;
				bool negative_seconds = (seconds < 0);
				int days = 0;
				if (negative_seconds)
					seconds *= (- 1);
				while (seconds >= seconds_in_day)
				{
					seconds -= seconds_in_day;
					days++;
				}
				if (negative_seconds)
				{
					seconds *= (- 1);
					days *= (- 1);
				}
				if (days != 0)
				{
					
//					calendar.add(SupportClass.CalendarManager.DATE, days);
				}
				if (seconds != 0)
				{
					
//					calendar.add(SupportClass.CalendarManager.SECOND, seconds);
				}
				
//				calendar.add(SupportClass.CalendarManager.MONTH, diff.Months);
			}
			
			if (hasDay > 0 && hasDate == 0)
			{
				SetWeekday(calendar, diff);
			}
			
			if (hasOrdMonth > 0)
			{
				SetOrdMonth(calendar, diff);
			}
			try {
				return calendar.dateTime;
			}
			catch (Exception) {
				throw new FormatException();	
			}
		failed:
			throw new FormatException();
		}
		private bool ParseTime(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar)
		// calendar object to set
		{
			int pos = parsePos;
						
			if (pos + 6 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(':') && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed(':') && dt[pos + 4].UNumber && dt[pos + 5].is_Renamed('-') && dt[pos + 6].UNumber)
			{
				ClockToken zone = GetTimeZoneFromRawOffset((- dt[pos + 6].Int) / 100);
				if (zone != null)
				{
					calendar.hour = dt[pos].Int;
					calendar.minute = dt[pos + 2].Int;
					calendar.second = dt[pos + 4].Int;
					// TODO
//					calendar.setTimeZone(zone.Zone);
					parsePos = pos + 7;
					return true;
				}
			}
			if (pos + 4 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(':') && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed(':') && dt[pos + 4].UNumber)
			{
				parsePos = pos + 5;
				ParseMeridianAndSetHour(dt, ref parsePos, calendar, dt[pos].Int);
				calendar.minute = dt[pos + 2].Int;
				calendar.second = dt[pos + 4].Int;
				return true;
			}
			if (pos + 4 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(':') && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed('-') && dt[pos + 4].UNumber)
			{
				ClockToken zone = GetTimeZoneFromRawOffset((- dt[pos + 4].Int) / 100);
				if (zone != null)
				{
					calendar.hour = dt[pos].Int;
					calendar.minute = dt[pos + 2].Int;
					
//					calendar.setTimeZone(zone.Zone);
					parsePos = pos + 5;
					return true;
				}
			}
			if (pos + 2 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(':') && dt[pos + 2].UNumber)
			{
				parsePos = pos + 3;
				ParseMeridianAndSetHour(dt, ref parsePos, calendar, dt[pos].Int);
				calendar.minute = dt[pos + 2].Int;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(ClockToken.MERIDIAN))
			{
				parsePos = pos + 1;
				ParseMeridianAndSetHour(dt, ref parsePos, calendar, dt[pos].Int);
				return true;
			}
			return false;
		}
		private bool ParseZone(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar)
		// calendar object to set
		{
			int pos = parsePos;
			
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.ZONE) && dt[pos + 1].is_Renamed(ClockToken.DST))
			{
				
//				calendar.setTimeZone(dt[pos].Zone);
				parsePos = pos + 2;
				return true;
			}
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.ZONE))
			{
				
//				calendar.setTimeZone(dt[pos].Zone);
				parsePos = pos + 1;
				return true;
			}
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.DAYZONE))
			{
				
//				calendar.setTimeZone(dt[pos].Zone);
				parsePos = pos + 1;
				return true;
			}
			return false;
		}
		private bool ParseDay(ClockToken[] dt, ref System.Int32 parsePos, ClockRelTimespan diff)
		// time difference to evaluate
		{
			int pos = parsePos;
			
			if (pos + 2 < dt.Length && dt[pos].is_Renamed('+') && dt[pos + 1].UNumber && dt[pos + 2].is_Renamed(ClockToken.DAY))
			{
				diff.setWeekday(dt[pos + 2].Int, dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 2 < dt.Length && dt[pos].is_Renamed('-') && dt[pos + 1].UNumber && dt[pos + 2].is_Renamed(ClockToken.DAY))
			{
				diff.setWeekday(dt[pos + 2].Int, - dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.NEXT) && dt[pos + 1].is_Renamed(ClockToken.DAY))
			{
				diff.setWeekday(dt[pos + 1].Int, 2);
				parsePos = pos + 2;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.DAY) && dt[pos + 1].is_Renamed(','))
			{
				diff.setWeekday(dt[pos].Int);
				parsePos = pos + 2;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(ClockToken.DAY))
			{
				diff.setWeekday(dt[pos + 1].Int, dt[pos].Int);
				parsePos = pos + 2;
				return true;
			}
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.DAY))
			{
				diff.setWeekday(dt[pos].Int);
				parsePos = pos + 1;
				return true;
			}
			return false;
		}
		private bool ParseDate(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar)
		// calendar object to set
		{
			int pos = parsePos;
			
			if (pos + 4 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed('/') && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed('/') && dt[pos + 4].UNumber)
			{
				calendar.day = dt[pos + 2].Int;
				calendar.month = dt[pos].Int;
				calendar.year = dt[pos+4].Int;
				parsePos = pos + 5;
				return true;
			}
			if (pos + 4 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed('-') && dt[pos + 2].is_Renamed(ClockToken.MONTH) && dt[pos + 3].is_Renamed('-') && dt[pos + 4].UNumber)
			{
				calendar.year = dt[pos+4].Int;
				calendar.month = dt[pos+2].Int;
				calendar.day = dt[pos].Int;
				parsePos = pos + 5;
				return true;
			}
			if (pos + 4 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed('-') && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed('-') && dt[pos + 4].UNumber)
			{
				calendar.year = dt[pos].Int;
				calendar.month = dt[pos+2].Int;
				calendar.day = dt[pos+4].Int;
				parsePos = pos + 5;
				return true;
			}
			if (pos + 3 < dt.Length && dt[pos].is_Renamed(ClockToken.MONTH) && dt[pos + 1].UNumber && dt[pos + 2].is_Renamed(',') && dt[pos + 3].UNumber)
			{
				calendar.day = dt[pos+1].Int;
				calendar.month = dt[pos].Int;
				calendar.year = dt[pos+3].Int;
				parsePos = pos + 4;
				return true;
			}
			if (pos + 2 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed('/') && dt[pos + 2].UNumber)
			{
				calendar.day = dt[pos+2].Int;
				calendar.month = dt[pos].Int;
				parsePos = pos + 3;
				return true;
			}
			if (pos + 2 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(ClockToken.MONTH) && dt[pos + 2].UNumber)
			{
				calendar.day = dt[pos].Int;
				calendar.month = dt[pos+1].Int;
				calendar.year = dt[pos+2].Int;
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.MONTH) && dt[pos + 1].UNumber)
			{
				calendar.day = dt[pos+1].Int;
				calendar.month = dt[pos].Int;
				parsePos = pos + 2;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].UNumber && dt[pos + 1].is_Renamed(ClockToken.MONTH))
			{
				calendar.day = dt[pos].Int;
				calendar.month = dt[pos+1].Int;
				parsePos = pos + 2;
				return true;
			}
			if (pos < dt.Length && dt[pos].IsoBase)
			{
				calendar.day = dt[pos].Int % 100;
				calendar.month = (dt[pos].Int % 10000) / 100;
				calendar.year = dt[pos].Int / 10000;
				parsePos = pos + 1;
				return true;
			}
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.EPOCH))
			{
				calendar.day = 1;
				calendar.month = 0;
				calendar.year = EPOCH_YEAR;
				parsePos = pos + 1;
				return true;
			}
			return false;
		}
		private bool ParseNumber(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar, bool mayBeYear)
		// number is considered to be year?
		{
			int pos = parsePos;
			
			if (pos < dt.Length && dt[pos].UNumber)
			{
				parsePos = pos + 1;
				if (mayBeYear)
				{
					calendar.year = dt[pos].Int;
				}
				else
				{
					calendar.hour = dt[pos].Int / 100;
					calendar.minute = dt[pos].Int % 100;
					calendar.second = 0;
				}
				return true;
			}
			return false;
		}
		private bool ParseRelSpec(ClockToken[] dt, ref System.Int32 parsePos, ClockRelTimespan diff)
		// time difference to evaluate
		{
			if (!ParseRelUnits(dt, ref parsePos, diff))
			{
				return false;
			}
			
			int pos = parsePos;
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.AGO))
			{
				diff.negate();
				parsePos = pos + 1;
			}
			return true;
		}
		private bool ParseRelUnits(ClockToken[] dt, ref System.Int32 parsePos, ClockRelTimespan diff)
		// time difference to evaluate
		{
			int pos = parsePos;
			
			if (pos + 2 < dt.Length && dt[pos].is_Renamed('+') && dt[pos + 1].UNumber && dt[pos + 2].Unit)
			{
				diff.addUnit(dt[pos + 2], dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 2 < dt.Length && dt[pos].is_Renamed('-') && dt[pos + 1].UNumber && dt[pos + 2].Unit)
			{
				diff.addUnit(dt[pos + 2], - dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].UNumber && dt[pos + 1].Unit)
			{
				diff.addUnit(dt[pos + 1], dt[pos].Int);
				parsePos = pos + 2;
				return true;
			}
			else if (pos + 2 < dt.Length && dt[pos].is_Renamed(ClockToken.NEXT) && dt[pos + 1].UNumber && dt[pos + 2].Unit)
			{
				diff.addUnit(dt[pos + 2], dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.NEXT) && dt[pos + 1].Unit)
			{
				diff.addUnit(dt[pos + 1]);
				parsePos = pos + 2;
				return true;
			}
			if (pos < dt.Length && dt[pos].Unit)
			{
				diff.addUnit(dt[pos]);
				parsePos = pos + 1;
				return true;
			}
			return false;
		}
		private bool ParseOrdMonth(ClockToken[] dt, ref System.Int32 parsePos, ClockRelTimespan diff)
		// time difference to evaluate
		{
			int pos = parsePos;
			
			if (pos + 2 < dt.Length && dt[pos].is_Renamed(ClockToken.NEXT) && dt[pos + 1].UNumber && dt[pos + 2].is_Renamed(ClockToken.MONTH))
			{
				diff.addOrdMonth(dt[pos + 2].Int, dt[pos + 1].Int);
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].is_Renamed(ClockToken.NEXT) && dt[pos + 1].is_Renamed(ClockToken.MONTH))
			{
				diff.addOrdMonth(dt[pos + 1].Int, 1);
				parsePos = pos + 2;
				return true;
			}
			return false;
		}
		private bool ParseIso(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar)
		// calendar object to set
		{
			int pos = parsePos;
			
			if (pos + 6 < dt.Length && dt[pos].IsoBase && dt[pos + 1].is_Renamed(ClockToken.ZONE) && dt[pos + 2].UNumber && dt[pos + 3].is_Renamed(':') && dt[pos + 4].UNumber && dt[pos + 5].is_Renamed(':') && dt[pos + 6].UNumber)
			{
				calendar.day = dt[pos].Int % 100;
				calendar.month = (dt[pos].Int % 10000) / 100;
				calendar.year = dt[pos].Int / 10000;
				calendar.hour = dt[pos + 2].Int;
				calendar.minute = dt[pos + 4].Int;
				calendar.second = dt[pos + 6].Int;
				parsePos = pos + 7;
				return true;
			}
			if (pos + 2 < dt.Length && dt[pos].IsoBase && dt[pos + 1].is_Renamed(ClockToken.ZONE) && dt[pos + 1].Zone.GetUtcOffset(calendar.dateTime).Hours == (- 7) * MILLIS_PER_HOUR && dt[pos + 2].IsoBase)
			{
				calendar.day = dt[pos].Int % 100;
				calendar.month = (dt[pos].Int % 10000) / 100;
				calendar.year = dt[pos].Int / 10000;
				calendar.hour = dt[pos + 2].Int / 10000;
				calendar.minute = (dt[pos + 2].Int % 10000) / 100;
				calendar.second = dt[pos + 2].Int % 100;
				parsePos = pos + 3;
				return true;
			}
			if (pos + 1 < dt.Length && dt[pos].IsoBase && dt[pos + 1].IsoBase)
			{
				calendar.day = dt[pos].Int % 100;
				calendar.month = (dt[pos].Int % 10000) / 100;
				calendar.year = dt[pos].Int / 10000;
				calendar.hour = dt[pos + 1].Int / 10000;
				calendar.minute = (dt[pos + 1].Int % 10000) / 100;
				calendar.second = dt[pos + 1].Int % 100;
				parsePos = pos + 2;
				return true;
			}
			return false;
		}
		private bool ParseTrek(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar)
		// calendar object to set
		{
			int pos = parsePos;
			
			if (pos + 3 < dt.Length && dt[pos].is_Renamed(ClockToken.STARDATE) && dt[pos + 1].UNumber && dt[pos + 2].is_Renamed('.') && dt[pos + 3].UNumber)
			{
				GregorianCalendar gcal = new GregorianCalendar();
				int trekYear = dt[pos + 1].Int / 1000 + 2323 - 377;
				int trekDay = 1 + ((dt[pos + 1].Int % 1000) * (gcal.IsLeapYear(trekYear)?366:365)) / 1000;
				int trekSeconds = dt[pos + 3].Int * 144 * 60;
				calendar.year = trekYear;
				calendar.dateTime = gcal.AddDays(calendar.dateTime,trekDay);
				calendar.second = trekSeconds;
				parsePos = pos + 4;
				return true;
			}
			return false;
		}
		private void  ParseMeridianAndSetHour(ClockToken[] dt, ref System.Int32 parsePos, TclDateTime calendar, int hour)
		// hour value (1-12 or 0-23) to set.
		{
			int pos = parsePos;
			int hourField;
			
			if (pos < dt.Length && dt[pos].is_Renamed(ClockToken.MERIDIAN))
			{
				// SupportClass.CalendarManager.manager.Set(calendar, SupportClass.CalendarManager.AM_PM, dt[pos].Int);
				parsePos = pos + 1;
				hourField = SupportClass.CalendarManager.HOUR;
			}
			else
			{
				hourField = SupportClass.CalendarManager.HOUR_OF_DAY;
			}
			
			if (hourField == SupportClass.CalendarManager.HOUR && hour == 12)
			{
				hour = 0;
			}
			calendar.hour = hour;
		}
		private ClockToken[] GetTokens(string in_Renamed, bool debug)
		// Send the generated token list to stderr?
		{
			System.Int32 parsePos = 0;
			ClockToken dt;
			ArrayList tokenVector = new ArrayList(in_Renamed.Length);
			
			while ((dt = GetNextToken(in_Renamed, ref parsePos)) != null)
			{
				tokenVector.Add(dt);
			}
			
			ClockToken[] tokenArray = new ClockToken[tokenVector.Count];
			tokenVector.CopyTo(tokenArray);
			
#if DEBUG
				for (int ix = 0; ix < tokenArray.Length; ix++)
				{
					if (ix != 0)
					{
						System.Console.Error.Write(",");
					}
					
					System.Console.Error.Write(tokenArray[ix].ToString());
				}
				System.Console.Error.WriteLine("");
#endif
			
			return tokenArray;
		}
		private ClockToken GetNextToken(string in_Renamed, ref System.Int32 parsePos)
		// Current position in input
		{
			int pos = parsePos;
			int sign;
			
			while (true)
			{
				while (pos < in_Renamed.Length && (System.Char.GetUnicodeCategory(in_Renamed[pos]) == System.Globalization.UnicodeCategory.SpaceSeparator))
				{
					pos++;
				}
				if (pos >= in_Renamed.Length)
				{
					break;
				}
				
				char c = in_Renamed[pos];
				if (System.Char.IsDigit(c))
				{
					int number = 0;
					int count = 0;
					while (pos < in_Renamed.Length && System.Char.IsDigit(c = in_Renamed[pos]))
					{
						number = 10 * number + c - '0';
						pos++;
						count++;
					}
					parsePos = pos;
					return new ClockToken(number, count >= 6);
				}
				if (System.Char.IsLetter(c))
				{
					int beginPos = pos;
					while (++pos < in_Renamed.Length)
					{
						c = in_Renamed[pos];
						if (!System.Char.IsLetter(c) && c != '.')
						{
							break;
						}
					}
					parsePos = pos;
					return LookupWord(in_Renamed.Substring(beginPos, (pos) - (beginPos)));
				}
				parsePos = pos + 1;
				return new ClockToken(in_Renamed[pos]);
			}
			parsePos = pos + 1;
			return null;
		}
		private ClockToken LookupWord(string word)
		// word to lookup
		{
			int ix;
			string[] names;
			string[][] zones;
			
			if (word.ToUpper().Equals("am".ToUpper()) || word.ToUpper().Equals("a.m.".ToUpper()))
			{
				return new ClockToken(ClockToken.MERIDIAN, SupportClass.CalendarManager.AM);
			}
			if (word.ToUpper().Equals("pm".ToUpper()) || word.ToUpper().Equals("p.m.".ToUpper()))
			{
				return new ClockToken(ClockToken.MERIDIAN, SupportClass.CalendarManager.PM);
			}
			
			// See if we have an abbreviation for a day or month.
			
			bool abbrev;
			if (word.Length == 3)
			{
				abbrev = true;
			}
			else if (word.Length == 4 && word[3] == '.')
			{
				abbrev = true;
				word = word.Substring(0, (3) - (0));
			}
			else
			{
				abbrev = false;
			}
			
			
			System.Globalization.DateTimeFormatInfo symbols = new System.Globalization.CultureInfo("en-US").DateTimeFormat;
			if (abbrev)
			{
				names = symbols.AbbreviatedMonthNames;
			}
			else
			{
				names = (string[]) symbols.MonthNames;
			}
			for (ix = 0; ix < names.Length; ix++)
			{
				if (word.ToUpper().Equals(names[ix].ToUpper()))
				{
					return new ClockToken(ClockToken.MONTH, ix+1);
				}
			}
			if (abbrev)
			{
				names = symbols.AbbreviatedDayNames;
			}
			else
			{
				names = symbols.DayNames;
			}
			for (ix = 0; ix < names.Length; ix++)
			{
				if (word.ToUpper().Equals(names[ix].ToUpper()))
				{
					return new ClockToken(ClockToken.DAY, ix);
				}
			}
			
			// Drop out any periods and try the timezone table.
			
			System.Text.StringBuilder withoutDotsBuf = new System.Text.StringBuilder(word.Length);
			for (ix = 0; ix < word.Length; ix++)
			{
				if (word[ix] != '.')
				{
					withoutDotsBuf.Append(word[ix]);
				}
			}
			
			string withoutDots = new string(withoutDotsBuf.ToString().ToCharArray());
			
//			zones = symbols.getZoneStrings();
			
//			for (ix = 0; ix < zones.Length; ix++)
//			{
//				if (withoutDots.ToUpper().Equals(zones[ix][2].ToUpper()) || withoutDots.ToUpper().Equals(zones[ix][4].ToUpper()))
//				{
//					
//					System.TimeZone zone = TimeZone.getTimeZone(zones[ix][0]);
//					return new ClockToken(ClockToken.ZONE, zone);
//				}
//			}
			if (withoutDots.ToUpper().Equals("dst".ToUpper()))
			{
				return new ClockToken(ClockToken.DST, null);
			}
			
			// Strip off any plural and try the units.
			
			string singular;
			if (word.EndsWith("s"))
			{
				singular = word.Substring(0, (word.Length - 1) - (0));
			}
			else
			{
				singular = word;
			}
			if (singular.ToUpper().Equals("year".ToUpper()))
			{
				return new ClockToken(ClockToken.MONTH_UNIT, 12);
			}
			else if (singular.ToUpper().Equals("month".ToUpper()))
			{
				return new ClockToken(ClockToken.MONTH_UNIT, 1);
			}
			else if (singular.ToUpper().Equals("fortnight".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 14 * 24 * 60);
			}
			else if (singular.ToUpper().Equals("week".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 7 * 24 * 60);
			}
			else if (singular.ToUpper().Equals("day".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 24 * 60);
			}
			else if (singular.ToUpper().Equals("hour".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 60);
			}
			else if (singular.ToUpper().Equals("minute".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 1);
			}
			else if (singular.ToUpper().Equals("min".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 1);
			}
			else if (singular.ToUpper().Equals("second".ToUpper()))
			{
				return new ClockToken(ClockToken.SEC_UNIT, 1);
			}
			else if (singular.ToUpper().Equals("sec".ToUpper()))
			{
				return new ClockToken(ClockToken.SEC_UNIT, 1);
			}
			
			if (singular.ToUpper().Equals("tomorrow".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 1 * 24 * 60);
			}
			else if (singular.ToUpper().Equals("yesterday".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, (- 1) * 24 * 60);
			}
			else if (singular.ToUpper().Equals("today".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 0);
			}
			else if (singular.ToUpper().Equals("now".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 0);
			}
			else if (singular.ToUpper().Equals("last".ToUpper()))
			{
				return new ClockToken(- 1, false);
			}
			else if (singular.ToUpper().Equals("this".ToUpper()))
			{
				return new ClockToken(ClockToken.MINUTE_UNIT, 0);
			}
			else if (singular.ToUpper().Equals("next".ToUpper()))
			{
				return new ClockToken(ClockToken.NEXT, 1);
			}
			else if (singular.ToUpper().Equals("ago".ToUpper()))
			{
				return new ClockToken(ClockToken.AGO, 1);
			}
			else if (singular.ToUpper().Equals("epoch".ToUpper()))
			{
				return new ClockToken(ClockToken.EPOCH, 0);
			}
			else if (singular.ToUpper().Equals("stardate".ToUpper()))
			{
				return new ClockToken(ClockToken.STARDATE, 0);
			}
			
			// Since a military timezone (T) is used in the clock test of 8.3,
			// we can't ignore these timezones any longer...
			
			if (withoutDots.Length == 1)
			{
				int rawOffset = 0;
				bool found = true;
				char milTz = System.Char.ToLower(withoutDots[0]);
				
				if (milTz >= 'a' && milTz <= 'm')
				{
					rawOffset = milTz - 'a' + 1;
				}
				else if (milTz >= 'n' && milTz < 'z')
				{
					rawOffset = 'n' - milTz - 1;
				}
				else if (milTz != 'z')
				{
					found = false;
				}
				if (found)
				{
					ClockToken zone = GetTimeZoneFromRawOffset(rawOffset);
					if (zone != null)
					{
						return zone;
					}
				}
			}
			
			return new ClockToken(word);
		}
		private ClockToken GetTimeZoneFromRawOffset(int rawOffset)
		{
			
//			string[] tzNames = TimeZone.getAvailableIDs(rawOffset * MILLIS_PER_HOUR);
			
//			if (tzNames.Length > 0)
//			{
//				
//				System.TimeZone zone = TimeZone.getTimeZone(tzNames[0]);
//				return new ClockToken(ClockToken.ZONE, zone);
//			}
			return null;
		}
	} // end ClockCmd
	class ClockToken
	{
		 public bool UNumber
		{
			get
			{
				return kind == UNUMBER;
			}
			
		}
		 public bool IsoBase
		{
			get
			{
				return kind == ISOBASE;
			}
			
		}
		 public bool Unit
		{
			get
			{
				return kind == MINUTE_UNIT || kind == MONTH_UNIT || kind == SEC_UNIT;
			}
			
		}
		 internal int Int
		{
			get
			{
				return number;
			}
			
		}
		 internal System.TimeZone Zone
		{
			get
			{
				return zone;
			}
			
		}
		internal const int ISOBASE = 1;
		internal const int UNUMBER = 2;
		internal const int WORD = 3;
		internal const int CHAR = 4;
		internal const int MONTH = 5;
		internal const int DAY = 6;
		internal const int MONTH_UNIT = 7;
		internal const int MINUTE_UNIT = 8;
		internal const int SEC_UNIT = 9;
		internal const int AGO = 10;
		internal const int EPOCH = 11;
		internal const int ZONE = 12;
		internal const int DAYZONE = 13;
		internal const int DST = 14;
		internal const int MERIDIAN = 15;
		internal const int NEXT = 16;
		internal const int STARDATE = 17;
		
		internal ClockToken(int number, bool isIsoBase)
		{
			this.kind = isIsoBase?ISOBASE:UNUMBER;
			this.number = number;
		}
		internal ClockToken(int kind, int number)
		{
			this.kind = kind;
			this.number = number;
		}
		internal ClockToken(int kind, System.TimeZone zone)
		{
			this.kind = kind;
			this.zone = zone;
		}
		internal ClockToken(string word)
		{
			this.kind = WORD;
			this.word = word;
		}
		internal ClockToken(char c)
		{
			this.kind = CHAR;
			this.c = c;
		}
		public  bool is_Renamed(char c)
		{
			return this.kind == CHAR && this.c == c;
		}
		public  bool is_Renamed(int kind)
		{
			return this.kind == kind;
		}
		
		public override string ToString()
		{
			if (UNumber)
			{
				return "U" + System.Convert.ToString(Int);
			}
			else if (IsoBase)
			{
				return "I" + System.Convert.ToString(Int);
			}
			else if (kind == WORD)
			{
				return word;
			}
			else if (kind == CHAR)
			{
				return c.ToString();
			}
			else if (kind == ZONE || kind == DAYZONE)
			{
				return zone.StandardName;
			}
			else
			{
				return "(" + kind + "," + Int + ")";
			}
		}
		
		private int kind;
		private int number;
		private string word;
		private char c;
		private System.TimeZone zone;
	} // end ClockToken
	class ClockRelTimespan
	{
		 internal int Seconds
		{
			get
			{
				return seconds;
			}
			
		}
		 internal int Months
		{
			get
			{
				return months;
			}
			
		}
		 internal int OrdMonth
		{
			get
			{
				return ordMonth;
			}
			
		}
		 internal int DayOrdinal
		{
			get
			{
				return dayOrdinal;
			}
			
		}
		internal ClockRelTimespan()
		{
			seconds = 0;
			months = 0;
			ordMonth = 0;
			weekday = 0;
			dayOrdinal = 0;
		}
		internal  void  addSeconds(int s)
		{
			seconds += s;
		}
		internal  void  addMonths(int m)
		{
			months += m;
		}
		internal  void  addOrdMonth(int m, int c)
		{
			months = m;
			ordMonth += c;
		}
		internal  void  addUnit(ClockToken unit, int amount)
		{
			if (unit.is_Renamed(ClockToken.SEC_UNIT))
			{
				addSeconds(unit.Int * amount);
			}
			else if (unit.is_Renamed(ClockToken.MINUTE_UNIT))
			{
				addSeconds(unit.Int * 60 * amount);
			}
			else if (unit.is_Renamed(ClockToken.MONTH_UNIT))
			{
				addMonths(unit.Int * amount);
			}
		}
		internal  void  addUnit(ClockToken unit)
		{
			addUnit(unit, 1);
		}
		internal  void  setWeekday(int w, int ord)
		{
			weekday = w;
			dayOrdinal = ord;
		}
		internal  void  setWeekday(int w)
		{
			setWeekday(w, 1);
		}
		internal  void  negate()
		{
			seconds = - seconds;
			months = - months;
		}
		internal  int getWeekday()
		{
			return weekday;
		}
		private int seconds;
		private int months;
		private int ordMonth;
		private int weekday;
		private int dayOrdinal;
	}
	class TclDateTime {
		public int year,month,day,hour,minute,second,millisecond;
		public DateTime dateTime {
			get {
				return new DateTime(year,month,day,hour,minute,second,millisecond);
			}
			set {
				DateTime dt = value;
				year = dt.Year;
				month = dt.Month;
				day = dt.Day;
				hour = dt.Hour;
				minute = dt.Minute;
				second = dt.Second;
				millisecond = dt.Millisecond;
			}
		}
	}
}
