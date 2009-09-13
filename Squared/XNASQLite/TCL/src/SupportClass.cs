//
// In order to convert some functionality to Visual C#, the Java Language Conversion Assistant
// creates "support classes" that duplicate the original functionality.
//
// Support classes replicate the functionality of the original code, but in some cases they are
// substantially different architecturally. Although every effort is made to preserve the
// original architecture of the application in the converted project, the user should be aware that
// the primary goal of these support classes is to replicate functionality, and that at times
// the architecture of the resulting solution may differ somewhat.
//
// Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
//$Header$

using System;
using System.Collections;


	/// <summary>
	/// This interface should be implemented by any class whose instances are intended
	/// to be executed by a thread.
	/// </summary>
	public interface IThreadRunnable
	{
		/// <summary>
		/// This method has to be implemented in order that starting of the thread causes the object's
		/// run method to be called in that separately executing thread.
		/// </summary>
		void Run();
	}

/// <summary>
/// Contains conversion support elements such as classes, interfaces and static methods.
/// </summary>
public class SupportClass
{
	/// <summary>
	/// Support class used to handle threads
	/// </summary>
	public class ThreadClass : IThreadRunnable
	{
		/// <summary>
		/// The instance of System.Threading.Thread
		/// </summary>
		private System.Threading.Thread threadField;

		/// <summary>
		/// Initializes a new instance of the ThreadClass class
		/// </summary>
		public ThreadClass()
		{
			threadField = new System.Threading.Thread(new System.Threading.ThreadStart(Run));
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Name">The name of the thread</param>
		public ThreadClass(string Name)
		{
			threadField = new System.Threading.Thread(new System.Threading.ThreadStart(Run));
			this.Name = Name;
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Start">A ThreadStart delegate that references the methods to be invoked when this thread begins executing</param>
		public ThreadClass(System.Threading.ThreadStart Start)
		{
			threadField = new System.Threading.Thread(Start);
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Start">A ThreadStart delegate that references the methods to be invoked when this thread begins executing</param>
		/// <param name="Name">The name of the thread</param>
		public ThreadClass(System.Threading.ThreadStart Start, string Name)
		{
			threadField = new System.Threading.Thread(Start);
			this.Name = Name;
		}

		/// <summary>
		/// This method has no functionality unless the method is overridden
		/// </summary>
		public virtual void Run()
		{
		}

		/// <summary>
		/// Causes the operating system to change the state of the current thread instance to ThreadState.Running
		/// </summary>
		public  void Start()
		{
			threadField.Start();
		}

		/// <summary>
		/// Interrupts a thread that is in the WaitSleepJoin thread state
		/// </summary>
		public  void Interrupt()
		{
			threadField.Interrupt();
		}

		/// <summary>
		/// Gets the current thread instance
		/// </summary>
		public System.Threading.Thread Instance
		{
			get
			{
				return threadField;
			}
			set
			{
				threadField = value;
			}
		}

		/// <summary>
		/// Gets or sets the name of the thread
		/// </summary>
		public string Name
		{
			get
			{
				return threadField.Name;
			}
			set
			{
				if (threadField.Name == null)
					threadField.Name = value;
			}
		}

		/// <summary>
		/// Gets or sets a value indicating the scheduling priority of a thread
		/// </summary>
		public System.Threading.ThreadPriority Priority
		{
			get
			{
				return threadField.Priority;
			}
			set
			{
				threadField.Priority = value;
			}
		}

		/// <summary>
		/// Gets a value indicating the execution status of the current thread
		/// </summary>
		public bool IsAlive
		{
			get
			{
				return threadField.IsAlive;
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether or not a thread is a background thread.
		/// </summary>
		public bool IsBackground
		{
			get
			{
				return threadField.IsBackground;
			}
			set
			{
				threadField.IsBackground = value;
			}
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates
		/// </summary>
		public void Join()
		{
			threadField.Join();
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates or the specified time elapses
		/// </summary>
		/// <param name="MiliSeconds">Time of wait in milliseconds</param>
		public void Join(long MiliSeconds)
		{
			lock(this)
			{
				threadField.Join(new System.TimeSpan(MiliSeconds * 10000));
			}
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates or the specified time elapses
		/// </summary>
		/// <param name="MiliSeconds">Time of wait in milliseconds</param>
		/// <param name="NanoSeconds">Time of wait in nanoseconds</param>
		public void Join(long MiliSeconds, int NanoSeconds)
		{
			lock(this)
			{
				threadField.Join(new System.TimeSpan(MiliSeconds * 10000 + NanoSeconds * 100));
			}
		}

		/// <summary>
		/// Resumes a thread that has been suspended
		/// </summary>
		public void Resume()
		{
			threadField.Resume();
		}

		/// <summary>
		/// Raises a ThreadAbortException in the thread on which it is invoked,
		/// to begin the process of terminating the thread. Calling this method
		/// usually terminates the thread
		/// </summary>
		public void Abort()
		{
			threadField.Abort();
		}

		/// <summary>
		/// Raises a ThreadAbortException in the thread on which it is invoked,
		/// to begin the process of terminating the thread while also providing
		/// exception information about the thread termination.
		/// Calling this method usually terminates the thread.
		/// </summary>
		/// <param name="stateInfo">An object that contains application-specific information, such as state, which can be used by the thread being aborted</param>
		public void Abort(System.Object stateInfo)
		{
			lock(this)
			{
				threadField.Abort(stateInfo);
			}
		}

		/// <summary>
		/// Suspends the thread, if the thread is already suspended it has no effect
		/// </summary>
		public void Suspend()
		{
			threadField.Suspend();
		}

		/// <summary>
		/// Obtain a String that represents the current Object
		/// </summary>
		/// <returns>A String that represents the current Object</returns>
		public override string ToString()
		{
			return "Thread[" + Name + "," + Priority.ToString() + "," + "" + "]";
		}

		/// <summary>
		/// Gets the currently running thread
		/// </summary>
		/// <returns>The currently running thread</returns>
		public static ThreadClass Current()
		{
			ThreadClass CurrentThread = new ThreadClass();
			CurrentThread.Instance = System.Threading.Thread.CurrentThread;
			return CurrentThread;
		}
	}


	/*******************************/
	/// <summary>
	/// Removes the first occurrence of an specific object from an ArrayList instance.
	/// </summary>
	/// <param name="arrayList">The ArrayList instance</param>
	/// <param name="element">The element to remove</param>
	/// <returns>True if item is found in the ArrayList; otherwise, false</returns>
	public static System.Boolean VectorRemoveElement(ArrayList arrayList, Object element)
	{
		System.Boolean containsItem = arrayList.Contains(element);
		arrayList.Remove(element);
		return containsItem;
	}

	/*******************************/
	/// <summary>
	/// Converts an array of sbytes to an array of bytes
	/// </summary>
	/// <param name="sbyteArray">The array of sbytes to be converted</param>
	/// <returns>The new array of bytes</returns>
	public static byte[] ToByteArray(sbyte[] sbyteArray)
	{
		byte[] byteArray = new byte[sbyteArray.Length];
		for(int index=0; index < sbyteArray.Length; index++)
			byteArray[index] = (byte) sbyteArray[index];
		return byteArray;
	}

	/// <summary>
	/// Converts a string to an array of bytes
	/// </summary>
	/// <param name="sourceString">The string to be converted</param>
	/// <returns>The new array of bytes</returns>
	public static byte[] ToByteArray(string sourceString)
	{
		byte[] byteArray = new byte[sourceString.Length];
		for (int index=0; index < sourceString.Length; index++)
			byteArray[index] = (byte) sourceString[index];
		return byteArray;
	}

	/// <summary>
	/// Converts a array of object-type instances to a byte-type array.
	/// </summary>
	/// <param name="tempObjectArray">Array to convert.</param>
	/// <returns>An array of byte type elements.</returns>
	public static byte[] ToByteArray(object[] tempObjectArray)
	{
		byte[] byteArray = new byte[tempObjectArray.Length];
		for (int index = 0; index < tempObjectArray.Length; index++)
			byteArray[index] = (byte)tempObjectArray[index];
		return byteArray;
	}


	/*******************************/
	/// <summary>
	/// This method returns the literal value received
	/// </summary>
	/// <param name="literal">The literal to return</param>
	/// <returns>The received value</returns>
	public static long Identity(long literal)
	{
		return literal;
	}

	/// <summary>
	/// This method returns the literal value received
	/// </summary>
	/// <param name="literal">The literal to return</param>
	/// <returns>The received value</returns>
	public static ulong Identity(ulong literal)
	{
		return literal;
	}

	/// <summary>
	/// This method returns the literal value received
	/// </summary>
	/// <param name="literal">The literal to return</param>
	/// <returns>The received value</returns>
	public static float Identity(float literal)
	{
		return literal;
	}

	/// <summary>
	/// This method returns the literal value received
	/// </summary>
	/// <param name="literal">The literal to return</param>
	/// <returns>The received value</returns>
	public static double Identity(double literal)
	{
		return literal;
	}

	/*******************************/
	/// <summary>
	/// Copies an array of chars obtained from a String into a specified array of chars
	/// </summary>
	/// <param name="sourceString">The String to get the chars from</param>
	/// <param name="sourceStart">Position of the String to start getting the chars</param>
	/// <param name="sourceEnd">Position of the String to end getting the chars</param>
	/// <param name="destinationArray">Array to return the chars</param>
	/// <param name="destinationStart">Position of the destination array of chars to start storing the chars</param>
	/// <returns>An array of chars</returns>
	public static void GetCharsFromString(string sourceString, int sourceStart, int sourceEnd, ref char[] destinationArray, int destinationStart)
	{
		int sourceCounter;
		int destinationCounter;
		sourceCounter = sourceStart;
		destinationCounter = destinationStart;
		while (sourceCounter < sourceEnd)
		{
			destinationArray[destinationCounter] = (char) sourceString[sourceCounter];
			sourceCounter++;
			destinationCounter++;
		}
	}

	/*******************************/
	/// <summary>
	/// This class manages different issues for calendars.
	/// The different calendars are internally managed using a hash table structure.
	/// </summary>
	public class CalendarManager
	{
		/// <summary>
		/// Field number for get and set indicating the year.
		/// </summary>
    public const int YEAR = 0;

		/// <summary>
		/// Field number for get and set indicating the month.
		/// </summary>
    public const int MONTH = 1;

		/// <summary>
		/// Field number for get and set indicating the day of the month.
		/// </summary>
    public const int DATE = 2;

		/// <summary>
		/// Field number for get and set indicating the hour of the morning or afternoon.
		/// </summary>
    public const int HOUR = 3;

		/// <summary>
		/// Field number for get and set indicating the minute within the hour.
		/// </summary>
    public const int MINUTE = 4;

		/// <summary>
		/// Field number for get and set indicating the second within the minute.
		/// </summary>
    public const int SECOND = 5;

		/// <summary>
		/// Field number for get and set indicating the millisecond within the second.
		/// </summary>
    public const int MILLISECOND = 6;

		/// <summary>
		/// Field number for get and set indicating the day of the month.
		/// </summary>
    public const int DAY_OF_MONTH = 7;

		/// <summary>
		/// Field used to get or set the day of the week.
		/// </summary>
    public const int DAY_OF_WEEK = 8;

		/// <summary>
		/// Field number for get and set indicating the hour of the day.
		/// </summary>
    public const int HOUR_OF_DAY = 9;

		/// <summary>
		/// Field number for get and set indicating whether the HOUR is before or after noon.
		/// </summary>
    public const int AM_PM = 10;

		/// <summary>
		/// Value of the AM_PM field indicating the period of the day from midnight to just
		/// before noon.
		/// </summary>
    public const int AM = 11;

		/// <summary>
		/// Value of the AM_PM field indicating the period of the day from noon to just before midnight.
		/// </summary>
    public const int PM = 12;

		/// <summary>
		/// The hash table that contains the type of calendars and its properties.
		/// </summary>
		static public CalendarHashTable manager = new CalendarHashTable();

		/// <summary>
		/// Internal class that inherits from HashTable to manage the different calendars.
		/// This structure will contain an instance of System.Globalization.Calendar that represents
		/// a type of calendar and its properties (represented by an instance of CalendarProperties
		/// class).
		/// </summary>
		public class CalendarHashTable:Hashtable
		{
			/// <summary>
			/// Gets the calendar current date and time.
			/// </summary>
			/// <param name="calendar">The calendar to get its current date and time.</param>
			/// <returns>A System.DateTime value that indicates the current date and time for the
			/// calendar given.</returns>
			public System.DateTime GetDateTime(System.Globalization.Calendar calendar)
			{
				if (this[calendar] != null)
					return ((CalendarProperties) this[calendar]).dateTime;
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTime = System.DateTime.Now;
					this.Add(calendar, tempProps);
					return this.GetDateTime(calendar);
				}
			}

			/// <summary>
			/// Sets the specified System.DateTime value to the specified calendar.
			/// </summary>
			/// <param name="calendar">The calendar to set its date.</param>
			/// <param name="date">The System.DateTime value to set to the calendar.</param>
			public void SetDateTime(System.Globalization.Calendar calendar, System.DateTime date)
			{
				if (this[calendar] != null)
				{
					((CalendarProperties) this[calendar]).dateTime = date;
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTime = date;
					this.Add(calendar, tempProps);
				}
			}

			/// <summary>
			/// Sets the corresponding field in an specified calendar with the value given.
			/// If the specified calendar does not have exist in the hash table, it creates a
			/// new instance of the calendar with the current date and time and then assings it
			/// the new specified value.
			/// </summary>
			/// <param name="calendar">The calendar to set its date or time.</param>
			/// <param name="field">One of the fields that composes a date/time.</param>
			/// <param name="fieldValue">The value to be set.</param>
			public void Set(System.Globalization.Calendar calendar, int field, int fieldValue)
			{
				if (this[calendar] != null)
				{
					System.DateTime tempDate = ((CalendarProperties) this[calendar]).dateTime;
					switch (field)
					{
						case CalendarManager.DATE:
							tempDate = tempDate.AddDays(fieldValue - tempDate.Day);
							break;
						case CalendarManager.HOUR:
							tempDate = tempDate.AddHours(fieldValue - tempDate.Hour);
							break;
						case CalendarManager.MILLISECOND:
							tempDate = tempDate.AddMilliseconds(fieldValue - tempDate.Millisecond);
							break;
						case CalendarManager.MINUTE:
							tempDate = tempDate.AddMinutes(fieldValue - tempDate.Minute);
							break;
						case CalendarManager.MONTH:
							//Month value is 0-based. e.g., 0 for January
							tempDate = tempDate.AddMonths(fieldValue - (tempDate.Month + 1));
							break;
						case CalendarManager.SECOND:
							tempDate = tempDate.AddSeconds(fieldValue - tempDate.Second);
							break;
						case CalendarManager.YEAR:
							tempDate = tempDate.AddYears(fieldValue - tempDate.Year);
							break;
						case CalendarManager.DAY_OF_MONTH:
							tempDate = tempDate.AddDays(fieldValue - tempDate.Day);
							break;
						case CalendarManager.DAY_OF_WEEK:;
							tempDate = tempDate.AddDays((fieldValue - 1) - (int)tempDate.DayOfWeek);
							break;
						case CalendarManager.HOUR_OF_DAY:
							tempDate = tempDate.AddHours(fieldValue - tempDate.Hour);
							break;

						default:
							break;
					}
					((CalendarProperties) this[calendar]).dateTime = tempDate;
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTime = System.DateTime.Now;
					this.Add(calendar, tempProps);
					this.Set(calendar, field, fieldValue);
				}
			}

			/// <summary>
			/// Sets the corresponding date (day, month and year) to the calendar specified.
			/// If the calendar does not exist in the hash table, it creates a new instance and sets
			/// its values.
			/// </summary>
			/// <param name="calendar">The calendar to set its date.</param>
			/// <param name="year">Integer value that represent the year.</param>
			/// <param name="month">Integer value that represent the month.</param>
			/// <param name="day">Integer value that represent the day.</param>
			public void Set(System.Globalization.Calendar calendar, int year, int month, int day)
			{
				if (this[calendar] != null)
				{
					this.Set(calendar, CalendarManager.YEAR, year);
					this.Set(calendar, CalendarManager.MONTH, month);
					this.Set(calendar, CalendarManager.DATE, day);
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					//Month value is 0-based. e.g., 0 for January
					tempProps.dateTime = new System.DateTime(year, month + 1, day);
					this.Add(calendar, tempProps);
				}
			}

			/// <summary>
			/// Sets the corresponding date (day, month and year) and hour (hour and minute)
			/// to the calendar specified.
			/// If the calendar does not exist in the hash table, it creates a new instance and sets
			/// its values.
			/// </summary>
			/// <param name="calendar">The calendar to set its date and time.</param>
			/// <param name="year">Integer value that represent the year.</param>
			/// <param name="month">Integer value that represent the month.</param>
			/// <param name="day">Integer value that represent the day.</param>
			/// <param name="hour">Integer value that represent the hour.</param>
			/// <param name="minute">Integer value that represent the minutes.</param>
			public void Set(System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute)
			{
				if (this[calendar] != null)
				{
					this.Set(calendar, CalendarManager.YEAR, year);
					this.Set(calendar, CalendarManager.MONTH, month);
					this.Set(calendar, CalendarManager.DATE, day);
					this.Set(calendar, CalendarManager.HOUR, hour);
					this.Set(calendar, CalendarManager.MINUTE, minute);
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					//Month value is 0-based. e.g., 0 for January
					tempProps.dateTime = new System.DateTime(year, month + 1, day, hour, minute, 0);
					this.Add(calendar, tempProps);
				}
			}

			/// <summary>
			/// Sets the corresponding date (day, month and year) and hour (hour, minute and second)
			/// to the calendar specified.
			/// If the calendar does not exist in the hash table, it creates a new instance and sets
			/// its values.
			/// </summary>
			/// <param name="calendar">The calendar to set its date and time.</param>
			/// <param name="year">Integer value that represent the year.</param>
			/// <param name="month">Integer value that represent the month.</param>
			/// <param name="day">Integer value that represent the day.</param>
			/// <param name="hour">Integer value that represent the hour.</param>
			/// <param name="minute">Integer value that represent the minutes.</param>
			/// <param name="second">Integer value that represent the seconds.</param>
			public void Set(System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute, int second)
			{
				if (this[calendar] != null)
				{
					this.Set(calendar, CalendarManager.YEAR, year);
					this.Set(calendar, CalendarManager.MONTH, month);
					this.Set(calendar, CalendarManager.DATE, day);
					this.Set(calendar, CalendarManager.HOUR, hour);
					this.Set(calendar, CalendarManager.MINUTE, minute);
					this.Set(calendar, CalendarManager.SECOND, second);
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					//Month value is 0-based. e.g., 0 for January
					tempProps.dateTime = new System.DateTime(year, month + 1, day, hour, minute, second);
					this.Add(calendar, tempProps);
				}
			}

			/// <summary>
			/// Gets the value represented by the field specified.
			/// </summary>
			/// <param name="calendar">The calendar to get its date or time.</param>
			/// <param name="field">One of the field that composes a date/time.</param>
			/// <returns>The integer value for the field given.</returns>
			public int Get(System.Globalization.Calendar calendar, int field)
			{
				if (this[calendar] != null)
				{
					int tempHour;
					switch (field)
					{
						case CalendarManager.DATE:
							return ((CalendarProperties) this[calendar]).dateTime.Day;
						case CalendarManager.HOUR:
							tempHour = ((CalendarProperties) this[calendar]).dateTime.Hour;
							return tempHour > 12 ? tempHour - 12 : tempHour;
						case CalendarManager.MILLISECOND:
							return ((CalendarProperties) this[calendar]).dateTime.Millisecond;
						case CalendarManager.MINUTE:
							return ((CalendarProperties) this[calendar]).dateTime.Minute;
						case CalendarManager.MONTH:
							//Month value is 0-based. e.g., 0 for January
							return ((CalendarProperties) this[calendar]).dateTime.Month - 1;
						case CalendarManager.SECOND:
							return ((CalendarProperties) this[calendar]).dateTime.Second;
						case CalendarManager.YEAR:
							return ((CalendarProperties) this[calendar]).dateTime.Year;
						case CalendarManager.DAY_OF_MONTH:
							return ((CalendarProperties) this[calendar]).dateTime.Day;
						case CalendarManager.DAY_OF_WEEK:
							return (int)(((CalendarProperties) this[calendar]).dateTime.DayOfWeek);
						case CalendarManager.HOUR_OF_DAY:
							return ((CalendarProperties) this[calendar]).dateTime.Hour;
						case CalendarManager.AM_PM:
							tempHour = ((CalendarProperties) this[calendar]).dateTime.Hour;
							return tempHour > 12 ? CalendarManager.PM : CalendarManager.AM;

						default:
							return 0;
					}
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTime = System.DateTime.Now;
					this.Add(calendar, tempProps);
					return this.Get(calendar, field);
				}
			}

			/// <summary>
			/// Sets the time in the specified calendar with the long value.
			/// </summary>
			/// <param name="calendar">The calendar to set its date and time.</param>
			/// <param name="milliseconds">A long value that indicates the milliseconds to be set to
			/// the hour for the calendar.</param>
			public void SetTimeInMilliseconds(System.Globalization.Calendar calendar, long milliseconds)
			{
				if (this[calendar] != null)
				{
					((CalendarProperties) this[calendar]).dateTime = new System.DateTime(milliseconds);
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTime = new System.DateTime(System.TimeSpan.TicksPerMillisecond * milliseconds);
					this.Add(calendar, tempProps);
				}
			}

			/// <summary>
			/// Gets what the first day of the week is; e.g., Sunday in US, Monday in France.
			/// </summary>
			/// <param name="calendar">The calendar to get its first day of the week.</param>
			/// <returns>A System.DayOfWeek value indicating the first day of the week.</returns>
			public System.DayOfWeek GetFirstDayOfWeek(System.Globalization.Calendar calendar)
			{
				if (this[calendar] != null && ((CalendarProperties)this[calendar]).dateTimeFormat != null)
				{
					return ((CalendarProperties) this[calendar]).dateTimeFormat.FirstDayOfWeek;
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTimeFormat = new System.Globalization.DateTimeFormatInfo();
					tempProps.dateTimeFormat.FirstDayOfWeek = System.DayOfWeek.Sunday;
					this.Add(calendar, tempProps);
					return this.GetFirstDayOfWeek(calendar);
				}
			}

			/// <summary>
			/// Sets what the first day of the week is; e.g., Sunday in US, Monday in France.
			/// </summary>
			/// <param name="calendar">The calendar to set its first day of the week.</param>
			/// <param name="firstDayOfWeek">A System.DayOfWeek value indicating the first day of the week
			/// to be set.</param>
			public void SetFirstDayOfWeek(System.Globalization.Calendar calendar, System.DayOfWeek  firstDayOfWeek)
			{
				if (this[calendar] != null && ((CalendarProperties)this[calendar]).dateTimeFormat != null)
				{
					((CalendarProperties) this[calendar]).dateTimeFormat.FirstDayOfWeek = firstDayOfWeek;
				}
				else
				{
					CalendarProperties tempProps = new CalendarProperties();
					tempProps.dateTimeFormat = new System.Globalization.DateTimeFormatInfo();
					this.Add(calendar, tempProps);
					this.SetFirstDayOfWeek(calendar, firstDayOfWeek);
				}
			}

			/// <summary>
			/// Removes the specified calendar from the hash table.
			/// </summary>
			/// <param name="calendar">The calendar to be removed.</param>
			public void Clear(System.Globalization.Calendar calendar)
			{
				if (this[calendar] != null)
					this.Remove(calendar);
			}

			/// <summary>
			/// Removes the specified field from the calendar given.
			/// If the field does not exists in the calendar, the calendar is removed from the table.
			/// </summary>
			/// <param name="calendar">The calendar to remove the value from.</param>
			/// <param name="field">The field to be removed from the calendar.</param>
			public void Clear(System.Globalization.Calendar calendar, int field)
			{
				if (this[calendar] != null)
					this.Remove(calendar);
				else
					this.Set(calendar, field, 0);
			}

			/// <summary>
			/// Internal class that represents the properties of a calendar instance.
			/// </summary>
			class CalendarProperties
			{
				/// <summary>
				/// The date and time of a calendar.
				/// </summary>
				public System.DateTime dateTime;

				/// <summary>
				/// The format for the date and time in a calendar.
				/// </summary>
				public System.Globalization.DateTimeFormatInfo dateTimeFormat;
			}
		}
	}

	/*******************************/
/// <summary>
/// Provides support for DateFormat
/// </summary>
public class DateTimeFormatManager
{
	static public DateTimeFormatHashTable manager = new DateTimeFormatHashTable();

	/// <summary>
	/// Hashtable class to provide functionality for dateformat properties
	/// </summary>
	public class DateTimeFormatHashTable :Hashtable
	{
		/// <summary>
		/// Sets the format for datetime.
		/// </summary>
		/// <param name="format">DateTimeFormat instance to set the pattern</param>
		/// <param name="newPattern">A string with the pattern format</param>
		public void SetDateFormatPattern(System.Globalization.DateTimeFormatInfo format, string newPattern)
		{
			if (this[format] != null)
				((DateTimeFormatProperties) this[format]).DateFormatPattern = newPattern;
			else
			{
				DateTimeFormatProperties tempProps = new DateTimeFormatProperties();
				tempProps.DateFormatPattern  = newPattern;
				Add(format, tempProps);
			}
		}

		/// <summary>
		/// Gets the current format pattern of the DateTimeFormat instance
		/// </summary>
		/// <param name="format">The DateTimeFormat instance which the value will be obtained</param>
		/// <returns>The string representing the current datetimeformat pattern</returns>
		public string GetDateFormatPattern(System.Globalization.DateTimeFormatInfo format)
		{
			if (this[format] == null)
				return "d-MMM-yy";
			else
				return ((DateTimeFormatProperties) this[format]).DateFormatPattern;
		}

		/// <summary>
		/// Sets the datetimeformat pattern to the giving format
		/// </summary>
		/// <param name="format">The datetimeformat instance to set</param>
		/// <param name="newPattern">The new datetimeformat pattern</param>
		public void SetTimeFormatPattern(System.Globalization.DateTimeFormatInfo format, string newPattern)
		{
			if (this[format] != null)
				((DateTimeFormatProperties) this[format]).TimeFormatPattern = newPattern;
			else
			{
				DateTimeFormatProperties tempProps = new DateTimeFormatProperties();
				tempProps.TimeFormatPattern  = newPattern;
				Add(format, tempProps);
			}
		}

		/// <summary>
		/// Gets the current format pattern of the DateTimeFormat instance
		/// </summary>
		/// <param name="format">The DateTimeFormat instance which the value will be obtained</param>
		/// <returns>The string representing the current datetimeformat pattern</returns>
		public string GetTimeFormatPattern(System.Globalization.DateTimeFormatInfo format)
		{
			if (this[format] == null)
				return "h:mm:ss tt";
			else
				return ((DateTimeFormatProperties) this[format]).TimeFormatPattern;
		}

		/// <summary>
		/// Internal class to provides the DateFormat and TimeFormat pattern properties on .NET
		/// </summary>
		class DateTimeFormatProperties
		{
			public string DateFormatPattern = "d-MMM-yy";
			public string TimeFormatPattern = "h:mm:ss tt";
		}
	}
}
	/*******************************/
	/// <summary>
	/// Gets the DateTimeFormat instance using the culture passed as parameter and sets the pattern to the time or date depending of the value
	/// </summary>
	/// <param name="dateStyle">The desired date style.</param>
	/// <param name="timeStyle">The desired time style</param>
	/// <param name="culture">The CultureInfo instance used to obtain the DateTimeFormat</param>
	/// <returns>The DateTimeFomatInfo of the culture and with the desired date or time style</returns>
	public static System.Globalization.DateTimeFormatInfo GetDateTimeFormatInstance(int dateStyle, int timeStyle, System.Globalization.CultureInfo culture)
	{
		System.Globalization.DateTimeFormatInfo format = culture.DateTimeFormat;

		switch (timeStyle)
		{
			case -1:
				DateTimeFormatManager.manager.SetTimeFormatPattern(format, "");
				break;

			case 0:
				DateTimeFormatManager.manager.SetTimeFormatPattern(format, "h:mm:ss 'o clock' tt zzz");
				break;

			case 1:
				DateTimeFormatManager.manager.SetTimeFormatPattern(format, "h:mm:ss tt zzz");
				break;

			case 2:
				DateTimeFormatManager.manager.SetTimeFormatPattern(format, "h:mm:ss tt");
				break;

			case 3:
				DateTimeFormatManager.manager.SetTimeFormatPattern(format, "h:mm tt");
				break;
		}

		switch (dateStyle)
		{
			case -1:
				DateTimeFormatManager.manager.SetDateFormatPattern(format, "");
				break;

			case 0:
				DateTimeFormatManager.manager.SetDateFormatPattern(format, "dddd, MMMM dd%, yyy");
				break;

			case 1:
				DateTimeFormatManager.manager.SetDateFormatPattern(format, "MMMM dd%, yyy" );
				break;

			case 2:
				DateTimeFormatManager.manager.SetDateFormatPattern(format, "d-MMM-yy" );
				break;

			case 3:
				DateTimeFormatManager.manager.SetDateFormatPattern(format, "M/dd/yy");
				break;
		}

		return format;
	}

	/*******************************/
	/// <summary>
	/// Gets the DateTimeFormat instance and date instance to obtain the date with the format passed
	/// </summary>
	/// <param name="format">The DateTimeFormat to obtain the time and date pattern</param>
	/// <param name="date">The date instance used to get the date</param>
	/// <returns>A string representing the date with the time and date patterns</returns>
	public static string FormatDateTime(System.Globalization.DateTimeFormatInfo format, System.DateTime date)
	{
		string timePattern = DateTimeFormatManager.manager.GetTimeFormatPattern(format);
		string datePattern = DateTimeFormatManager.manager.GetDateFormatPattern(format);
		return date.ToString(datePattern + " " + timePattern, format);
	}

	/*******************************/
	/// <summary>
	/// Adds a new key-and-value pair into the hash table
	/// </summary>
	/// <param name="collection">The collection to work with</param>
	/// <param name="key">Key used to obtain the value</param>
	/// <param name="newValue">Value asociated with the key</param>
	/// <returns>The old element associated with the key</returns>
	public static Object PutElement(IDictionary collection, Object key, Object newValue)
	{
		Object element = collection[key];
		collection[key] = newValue;
		return element;
	}

	/*******************************/
	/// <summary>
	/// Provides support functions to create read-write random acces files and write functions
	/// </summary>
	public class RandomAccessFileSupport
	{
		/// <summary>
		/// Creates a new random acces stream with read-write or read rights
		/// </summary>
		/// <param name="fileName">A relative or absolute path for the file to open</param>
		/// <param name="mode">Mode to open the file in</param>
		/// <returns>The new System.IO.FileStream</returns>
		public static System.IO.FileStream CreateRandomAccessFile(string fileName, string mode)
		{
			System.IO.FileStream newFile = null;

			if (mode.CompareTo("rw") == 0)
				newFile =  new System.IO.FileStream(fileName, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite);
			else if (mode.CompareTo("r") == 0 )
				newFile =  new System.IO.FileStream(fileName, System.IO.FileMode.Open, System.IO.FileAccess.Read);
			else
				throw new System.ArgumentException();

			return newFile;
		}

		/// <summary>
		/// Creates a new random acces stream with read-write or read rights
		/// </summary>
		/// <param name="fileName">File infomation for the file to open</param>
		/// <param name="mode">Mode to open the file in</param>
		/// <returns>The new System.IO.FileStream</returns>
		public static System.IO.FileStream CreateRandomAccessFile(System.IO.FileInfo fileName, string mode)
		{
			return CreateRandomAccessFile(fileName.FullName, mode);
		}

		/// <summary>
		/// Writes the data to the specified file stream
		/// </summary>
		/// <param name="data">Data to write</param>
		/// <param name="fileStream">File to write to</param>
		public static void WriteBytes(string data,System.IO.FileStream fileStream)
		{
			int index = 0;
			int length = data.Length;

			while(index < length)
				fileStream.WriteByte((byte)data[index++]);
		}

		/// <summary>
		/// Writes the received string to the file stream
		/// </summary>
		/// <param name="data">String of information to write</param>
		/// <param name="fileStream">File to write to</param>
		public static void WriteChars(string data,System.IO.FileStream fileStream)
		{
			WriteBytes(data, fileStream);
		}

		/// <summary>
		/// Writes the received data to the file stream
		/// </summary>
		/// <param name="sByteArray">Data to write</param>
		/// <param name="fileStream">File to write to</param>
		public static void WriteRandomFile(sbyte[] sByteArray,System.IO.FileStream fileStream)
		{
			byte[] byteArray = ToByteArray(sByteArray);
			fileStream.Write(byteArray, 0, byteArray.Length);
		}
	}

	/*******************************/
	/// <summary>
	/// Checks if a file have write permissions
	/// </summary>
	/// <param name="file">The file instance to check</param>
	/// <returns>True if have write permissions otherwise false</returns>
public static bool FileCanWrite(System.IO.FileInfo file)
{
return (System.IO.File.GetAttributes(file.FullName) & System.IO.FileAttributes.ReadOnly) != System.IO.FileAttributes.ReadOnly;
}

	/*******************************/
	/// <summary>
	/// Checks if the giving File instance is a directory or file, and returns his Length
	/// </summary>
	/// <param name="file">The File instance to check</param>
	/// <returns>The length of the file</returns>
	public static long FileLength(System.IO.FileInfo file)
	{
		if (System.IO.Directory.Exists(file.FullName))
			return 0;
		else
			return file.Length;
	}

	/*******************************/
	/// <summary>Reads a number of characters from the current source Stream and writes the data to the target array at the specified index.</summary>
	/// <param name="sourceStream">The source Stream to read from.</param>
	/// <param name="target">Contains the array of characteres read from the source Stream.</param>
	/// <param name="start">The starting index of the target array.</param>
	/// <param name="count">The maximum number of characters to read from the source Stream.</param>
	/// <returns>The number of characters read. The number will be less than or equal to count depending on the data available in the source Stream. Returns -1 if the end of the stream is reached.</returns>
	public static System.Int32 ReadInput(System.IO.Stream sourceStream, ref byte[] target, int start, int count)
	{
		// Returns 0 bytes if not enough space in target
		if (target.Length == 0)
			return 0;

		byte[] receiver = new byte[target.Length];
		int bytesRead   = sourceStream.Read(receiver, start, count);

		// Returns -1 if EOF
		if (bytesRead == 0)
			return -1;

		for(int i = start; i < start + bytesRead; i++)
			target[i] = (byte)receiver[i];

		return bytesRead;
	}

	/// <summary>Reads a number of characters from the current source TextReader and writes the data to the target array at the specified index.</summary>
	/// <param name="sourceTextReader">The source TextReader to read from</param>
	/// <param name="target">Contains the array of characteres read from the source TextReader.</param>
	/// <param name="start">The starting index of the target array.</param>
	/// <param name="count">The maximum number of characters to read from the source TextReader.</param>
	/// <returns>The number of characters read. The number will be less than or equal to count depending on the data available in the source TextReader. Returns -1 if the end of the stream is reached.</returns>
	public static System.Int32 ReadInput(System.IO.TextReader sourceTextReader, ref sbyte[] target, int start, int count)
	{
		// Returns 0 bytes if not enough space in target
		if (target.Length == 0) return 0;

		char[] charArray = new char[target.Length];
		int bytesRead = sourceTextReader.Read(charArray, start, count);

		// Returns -1 if EOF
		if (bytesRead == 0) return -1;

		for(int index=start; index<start+bytesRead; index++)
			target[index] = (sbyte)charArray[index];

		return bytesRead;
	}

	/*******************************/
	/// <summary>
	/// Performs an unsigned bitwise right shift with the specified number
	/// </summary>
	/// <param name="number">Number to operate on</param>
	/// <param name="bits">Ammount of bits to shift</param>
	/// <returns>The resulting number from the shift operation</returns>
	public static int URShift(int number, int bits)
	{
		if ( number >= 0)
			return number >> bits;
		else
			return (number >> bits) + (2 << ~bits);
	}

	/// <summary>
	/// Performs an unsigned bitwise right shift with the specified number
	/// </summary>
	/// <param name="number">Number to operate on</param>
	/// <param name="bits">Ammount of bits to shift</param>
	/// <returns>The resulting number from the shift operation</returns>
	public static int URShift(int number, long bits)
	{
		return URShift(number, (int)bits);
	}

	/// <summary>
	/// Performs an unsigned bitwise right shift with the specified number
	/// </summary>
	/// <param name="number">Number to operate on</param>
	/// <param name="bits">Ammount of bits to shift</param>
	/// <returns>The resulting number from the shift operation</returns>
	public static long URShift(long number, int bits)
	{
		if ( number >= 0)
			return number >> bits;
		else
			return (number >> bits) + (2L << ~bits);
	}

	/// <summary>
	/// Performs an unsigned bitwise right shift with the specified number
	/// </summary>
	/// <param name="number">Number to operate on</param>
	/// <param name="bits">Ammount of bits to shift</param>
	/// <returns>The resulting number from the shift operation</returns>
	public static long URShift(long number, long bits)
	{
		return URShift(number, (int)bits);
	}

	/*******************************/
	/// <summary>
	/// Writes the exception stack trace to the received stream
	/// </summary>
	/// <param name="throwable">Exception to obtain information from</param>
	/// <param name="stream">Output sream used to write to</param>
	public static void WriteStackTrace(System.Exception throwable, System.IO.TextWriter stream)
	{
		stream.Write(throwable.StackTrace);
		stream.Flush();
	}

	/*******************************/
	/// <summary>
	/// Removes the element with the specified key from a Hashtable instance.
	/// </summary>
	/// <param name="hashtable">The Hashtable instance</param>
	/// <param name="key">The key of the element to remove</param>
	/// <returns>The element removed</returns>
	public static Object HashtableRemove(Hashtable hashtable, Object key)
	{
		Object element = hashtable[key];
		hashtable.Remove(key);
		return element;
	}

	/*******************************/
	/// <summary>
	/// Converts an array of sbytes to an array of chars
	/// </summary>
	/// <param name="sByteArray">The array of sbytes to convert</param>
	/// <returns>The new array of chars</returns>
	public static char[] ToCharArray(sbyte[] sByteArray)
	{
		char[] charArray = new char[sByteArray.Length];
		sByteArray.CopyTo(charArray, 0);
		return charArray;
	}

	/// <summary>
	/// Converts an array of bytes to an array of chars
	/// </summary>
	/// <param name="byteArray">The array of bytes to convert</param>
	/// <returns>The new array of chars</returns>
	public static char[] ToCharArray(byte[] byteArray)
	{
		char[] charArray = new char[byteArray.Length];
		byteArray.CopyTo(charArray, 0);
		return charArray;
	}

	/*******************************/
	/// <summary>
	/// Receives a byte array and returns it transformed in an sbyte array
	/// </summary>
	/// <param name="byteArray">Byte array to process</param>
	/// <returns>The transformed array</returns>
	public static sbyte[] ToSByteArray(byte[] byteArray)
	{
		sbyte[] sbyteArray = new sbyte[byteArray.Length];
		for(int index=0; index < byteArray.Length; index++)
			sbyteArray[index] = (sbyte) byteArray[index];
		return sbyteArray;
	}
	/*******************************/
	/// <summary>
	/// Returns the last element of an ArrayList instance.
	/// </summary>
	/// <param name="arrayList">The ArrayList instance</param>
	/// <returns>The last element of the ArrayList</returns>
	public static Object VectorLastElement(ArrayList arrayList)
	{
		return arrayList[arrayList.Count - 1];
	}

	/// <summary>
	/// Returns the last element of a Stack instance.
	/// </summary>
	/// <param name="stack">The Stack instance</param>
	/// <returns>The last element of the Stack</returns>
	public static Object VectorLastElement(Stack stack)
	{
		return stack.ToArray()[0];
	}


	/*******************************/
	/// <summary>
	/// Adds an element to the top end of a Stack instance.
	/// </summary>
	/// <param name="stack">The Stack instance</param>
	/// <param name="element">The element to add</param>
	/// <returns>The element added</returns>
	public static Object StackPush(Stack stack, Object element)
	{
		stack.Push(element);
		return element;
	}

	/*******************************/
	/// <summary>
	/// Creates an instance of a received Type.
	/// </summary>
	/// <param name="classType">The Type of the new class instance to return.</param>
	/// <returns>An Object containing the new instance.</returns>
	public static Object CreateNewInstance(System.Type classType)
	{
		Object instance = null;
		System.Type[] constructor = new System.Type[]{};
		System.Reflection.ConstructorInfo[] constructors = null;

		constructors = classType.GetConstructors();

		if (constructors.Length == 0)
			throw new System.UnauthorizedAccessException();
		else
		{
			for(int i = 0; i < constructors.Length; i++)
			{
				System.Reflection.ParameterInfo[] parameters = constructors[i].GetParameters();

				if (parameters.Length == 0)
				{
					instance = classType.GetConstructor(constructor).Invoke(new System.Object[]{});
					break;
				}
				else if (i == constructors.Length -1)
					throw new System.MethodAccessException();
			}
		}
		return instance;
	}


	/*******************************/
	/// <summary>
	/// Obtains the int value depending of the type of modifiers that the constructor have
	/// </summary>
	/// <param name="constructor">The ConstructorInfo used to obtain the int value</param>
	/// <returns>The int value of the modifier present in the constructor. 1 if it's public, 2 if it's private, otherwise 4</returns>
	public static int GetConstructorModifiers(System.Reflection.ConstructorInfo constructor)
	{
		int temp;
		if (constructor.IsPublic)
			temp = 1;
		else if (constructor.IsPrivate)
			temp = 2;
		else
			temp= 4;
		return temp;
	}

	/*******************************/
	/// <summary>
	/// Write an array of bytes int the FileStream specified.
	/// </summary>
	/// <param name="FileStreamWrite">FileStream that must be updated.</param>
	/// <param name="Source">Array of bytes that must be written in the FileStream.</param>
	public static void WriteOutput(System.IO.FileStream FileStreamWrite, sbyte[] Source)
	{
		FileStreamWrite.Write(ToByteArray(Source), 0, Source.Length);
	}


}
