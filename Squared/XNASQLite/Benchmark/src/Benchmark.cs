//  $Header$

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using System.Data;
using System.Data.SQLite;

using CS_SQLite3;

/*
 * Benchmark Test for both SQLite and C#-SQLite
 */

public class Benchmark
{
  private static int nRecords;

  private static string[] PRAGMA_Commands = {
    "PRAGMA synchronous =  OFF",
    "PRAGMA temp_store =  MEMORY",
    "PRAGMA journal_mode = OFF" ,
    "PRAGMA locking_mode=EXCLUSIVE"
  };

  private static string[] CREATE_Commands = {
    "CREATE TABLE Root (intIndex INTEGER PRIMARY KEY, strIndex TEXT)",
    "CREATE INDEX RootStrIndex ON Root (strIndex)"
  };

  private static string INSERT_Command = "INSERT INTO Root VALUES (?,?)";
  private static string SELECT_Bind_i = "SELECT * FROM Root WHERE intIndex = ?";
  private static string SELECT_Bind_s = "SELECT * FROM Root WHERE strIndex = ?";

  private static string SELECT_Command_i = "SELECT * FROM Root ORDER BY intIndex";
  private static string SELECT_Command_s = "SELECT * FROM Root ORDER BY strIndex";

  private static string DELETE_Bind = "DELETE FROM Root WHERE intIndex = ?";

  public static void Main()
  {
    for ( nRecords = 10000 ; nRecords <= 100000 ; nRecords *= 10 )
    {
      TestSQLite();
      TestcsSQLite();
    }
    Console.WriteLine( "Enter to Continue: " );
    Console.ReadKey();
  }

  private static void TestcsSQLite()
  {
    SQLiteDatabase db;
    SQLiteVdbe stmt;
    SQLiteVdbe c1, c2;

    bool found;
    int i;

    string databaseName = "Benchmark_cs-SQLite.sqlite";
    Console.WriteLine( "\n\r" + databaseName );
    if ( File.Exists( databaseName ) ) File.Delete( databaseName );

    db = new SQLiteDatabase( databaseName );
    for (i = 0; i < PRAGMA_Commands.Length; i++) {db.ExecuteNonQuery( PRAGMA_Commands[i]);}

    db.ExecuteNonQuery( "BEGIN EXCLUSIVE" );
    for (i = 0; i < CREATE_Commands.Length; i++) {db.ExecuteNonQuery( CREATE_Commands[i]);}
    stmt = new SQLiteVdbe( db, INSERT_Command );
    long start = DateTime.Now.Ticks;
    long key = 1999;
    for ( i = 0 ; i < nRecords ; i++ )
    {
      key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
      stmt.Reset();
      stmt.BindLong( 1, key );
      stmt.BindText( 2, key.ToString() );
      stmt.ExecuteStep();
    }
    stmt.Close();
    db.ExecuteNonQuery( "END" );
    Console.WriteLine( "inserting " + nRecords + " records: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );

    db.ExecuteNonQuery( "BEGIN EXCLUSIVE" );
    start = DateTime.Now.Ticks;
    c1 = new SQLiteVdbe( db, SELECT_Bind_i );
    c2 = new SQLiteVdbe( db, SELECT_Bind_s);
    key = 1999;
    for ( i = 0 ; i < nRecords ; i++ )
    {
      key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
      c1.Reset();
      c1.BindLong( 1, key );
      c1.ExecuteStep();

      c2.Reset(); 
      c2.BindText( 1, key.ToString() );
      c2.ExecuteStep();

      long id = (long)c1.Result_Long( 0 );
      Debug.Assert( id == ( long)c2.Result_Long(0) );

    }
    c1.Close();
    c2.Close();
    db.ExecuteNonQuery( "END" );
    Console.WriteLine( "performing " + nRecords * 2 + " index searches: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );

    db.ExecuteNonQuery( "BEGIN EXCLUSIVE" );
    start = DateTime.Now.Ticks;
    key = Int64.MinValue;
    i = 0;
    c1 = new SQLiteVdbe( db, SELECT_Command_i );
    while ( c1.ExecuteStep() != csSQLite.SQLITE_DONE )
    {
      long intKey = (long)c1.Result_Long( 0 );
      Debug.Assert( intKey>= key );
      key = intKey;
      i += 1;
    }
    c1.Close();
    Debug.Assert( i == nRecords );

    String strKey = ""; 
    i = 0;
    c2 = new SQLiteVdbe( db, SELECT_Command_s );
    while ( c2.ExecuteStep() != csSQLite.SQLITE_DONE )
    {
      string recStrKey = (string)c2.Result_Text( 1 );
      Debug.Assert( recStrKey.CompareTo( strKey ) >= 0 );
      strKey = recStrKey;
      i += 1;
    }
    c2.Close();
    Debug.Assert( i == nRecords );
    Console.WriteLine( "iterating through " + ( nRecords * 2 ) + " records: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );
    db.ExecuteNonQuery( "END" );

    db.ExecuteNonQuery( "BEGIN EXCLUSIVE" );
    start = DateTime.Now.Ticks;
    key = 1999;
    stmt = new SQLiteVdbe( db, DELETE_Bind);
    for ( i = 0 ; i < nRecords ; i++ )
    {
      key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
      stmt.Reset(); 
      stmt.BindLong( 1, key );
      stmt.ExecuteStep();
    }
    stmt.Close();
    db.ExecuteNonQuery( "END" );
    Console.WriteLine( "deleting " + nRecords + " records: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );
    db.CloseDatabase();

  }
private static void TestSQLite()
  {
    int i;
    string databaseName = "Benchmark_SQLite.sqlite";
    Console.WriteLine( "\n\r" + databaseName );
    if ( File.Exists( databaseName ) ) File.Delete( databaseName );

    SQLiteConnectionStringBuilder constring = new SQLiteConnectionStringBuilder();
    constring.PageSize = 1024;
    constring.SyncMode = SynchronizationModes.Off;
    constring.DataSource = databaseName;

    SQLiteConnection con = new SQLiteConnection( constring.ToString() );
    con.Open();
    SQLiteCommand com = con.CreateCommand();
    for (i = 0; i < PRAGMA_Commands.Length; i++) {
    com.CommandText = PRAGMA_Commands[i];
    com.ExecuteNonQuery();
    }
    for (i = 0; i < CREATE_Commands.Length; i++) {
    com.CommandText = CREATE_Commands[i];
    com.ExecuteNonQuery();
    }

    com.CommandText = "BEGIN EXCLUSIVE";
    com.ExecuteNonQuery();

    com.CommandText = "INSERT INTO Root VALUES (?,?)";
    SQLiteParameter p1 = com.CreateParameter();
    p1.DbType = DbType.Int64;
    com.Parameters.Add( p1 );
    SQLiteParameter p2 = com.CreateParameter();
    p2.DbType = DbType.String;
    com.Parameters.Add( p2 );

    long start = DateTime.Now.Ticks;
    long key = 1999;
    for ( i = 0 ; i < nRecords ; i++ )
    {
      key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
      p1.Value = key;
      p2.Value = key.ToString();
      com.ExecuteNonQuery();
    }
    com.CommandText = "END";
    com.Parameters.Clear();
    com.ExecuteNonQuery();
    Console.WriteLine( "inserting " + nRecords + " records: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );

    com.CommandText = "BEGIN EXCLUSIVE";
    com.ExecuteNonQuery();

    using ( SQLiteCommand com2 = con.CreateCommand() )
    {
      com.CommandText = SELECT_Bind_i;
      com.Parameters.Clear();
      com.Parameters.Add( p1 );

      com2.CommandText = SELECT_Bind_s; 
      com2.Parameters.Clear();
      com2.Parameters.Add( p2 );

      start = DateTime.Now.Ticks;
      key = 1999;
      object[] resValues = new object[2];
      for ( i = 0 ; i < nRecords ; i++ )
      {
        key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
        p1.Value = key;
        p2.Value = key.ToString();
        using ( SQLiteDataReader res = com.ExecuteReader() )
        {
          res.Read();
          res.GetValues( resValues );
        }
        long id = (long)resValues[0];
        using ( SQLiteDataReader res = com2.ExecuteReader() )
        {
          res.Read();
          res.GetValues( resValues );
        }
        Debug.Assert( id == ( (long)resValues[0] ) );
      }
    }

    Console.WriteLine( "performing " + nRecords * 2 + " index searches: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );
    com.CommandText = "END";
    com.Parameters.Clear();
    com.ExecuteNonQuery();

    com.CommandText = "BEGIN EXCLUSIVE";
    com.ExecuteNonQuery();

    start = DateTime.Now.Ticks;
    com.CommandText = SELECT_Command_i;
    com.Parameters.Clear();
    key = Int64.MinValue;
    i = 0;
    using ( SQLiteDataReader reader = com.ExecuteReader() )
    {
      object[] resValues = new object[2];
      while ( reader.Read() )
      {
        reader.GetValues( resValues );
        long intKey = (long)resValues[0];
        Debug.Assert( intKey >= key );
        key = intKey;
        i += 1;
      }
      Debug.Assert( i == nRecords );
    }
    com.CommandText = SELECT_Command_s;
    using ( SQLiteDataReader reader = com.ExecuteReader() )
    {
      i = 0;
      String strKey = "";
      object[] resValues = new object[2];
      while ( reader.Read() )
      {
        reader.GetValues( resValues );
        string recStrKey = (string)resValues[1];
        Debug.Assert( recStrKey.CompareTo( strKey ) >= 0 );
        strKey = recStrKey;
        i += 1;
      }
      Debug.Assert( i == nRecords );
    }
    Console.WriteLine( "iteration through " + ( nRecords * 2 ) + " records: " +
    ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );

    com.CommandText = "END";
    com.Parameters.Clear();
    com.ExecuteNonQuery();

    com.CommandText = "BEGIN EXCLUSIVE";
    com.ExecuteNonQuery();

    com.CommandText = DELETE_Bind;
    com.Parameters.Clear();
    com.Parameters.Add( p1 );

    start = DateTime.Now.Ticks;
    key = 1999;
    for ( i = 0 ; i < nRecords ; i++ )
    {
      key = ( 3141592621L * key + 2718281829L ) % 1000000007L;
      p1.Value = key;
      com.ExecuteNonQuery();
    }
    com.CommandText = "END";
    com.Parameters.Clear();
    com.ExecuteNonQuery();

    Console.WriteLine( "deleting " + nRecords + " records: "
    + ( ( DateTime.Now.Ticks - start ) * 10e-8 + .05).ToString( "#.0" ) + " seconds" );
    con.Close();
  }
}
