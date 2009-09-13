//  $Header$
using System;
using System.Collections.Generic;
using System.Text;
#if XBOX360
using CS_SQLite3.XNA;
#else
using System.Data;
#endif
using System.Collections;

namespace CS_SQLite3
{

  using sqlite = csSQLite.sqlite3;
  using Vdbe = csSQLite.Vdbe;

  /// <summary>
  /// C#-SQLite wrapper with functions for opening, closing and executing queries.
  /// </summary>
  public class SQLiteVdbe : IDisposable
  {
    public class RowToken {
      public class ColumnSet {
        public RowToken Row;

        public int Count {
          get {
            return csSQLite.sqlite3_column_count(Row.Statement.VirtualMachine());
          }
        }
      }

      public readonly SQLiteVdbe Statement;
      public readonly ColumnSet Columns;

      public RowToken (SQLiteVdbe statement) {
        Statement = statement;
        Columns = new ColumnSet { Row = this };
      }
    }

    private Vdbe vm = null;
    private string LastError = "";
    private int LastResult = 0;

    /// <summary>
    /// Creates new instance of SQLiteVdbe class by compiling a statement
    /// </summary>
    /// <param name="query"></param>
    /// <returns>Vdbe</returns>
    public SQLiteVdbe( SQLiteDatabase db, String query )
    {
      vm = null;

      // prepare and compile 
      csSQLite.sqlite3_prepare_v2( db.Connection(), query, query.Length, ref vm, 0 );
    }

    /// <summary>
    /// Return Virtual Machine Pointer
    /// </summary>
    /// <param name="query"></param>
    /// <returns>Vdbe</returns>
    public Vdbe VirtualMachine()
    {
      return vm;
    }
    
    /// <summary>
    /// BindInteger
    /// </summary>
    /// <param name="index"></param>
    /// <param name="bInteger"></param>
    /// <returns>LastResult</returns>
    public int BindInteger(int index, int bInteger )
    {
      if ( (LastResult = csSQLite.sqlite3_bind_int( vm, index, bInteger ))== csSQLite.SQLITE_OK )
      { LastError = ""; }
      else
      {
        LastError = "Error " + LastError + "binding Integer [" + bInteger + "]";
      }
      return LastResult;
    }

    /// <summary>
    /// BindLong
    /// </summary>
    /// <param name="index"></param>
    /// <param name="bLong"></param>
    /// <returns>LastResult</returns>
    public int BindLong( int index, long bLong )
    {
      if ( ( LastResult = csSQLite.sqlite3_bind_int64( vm, index, bLong ) ) == csSQLite.SQLITE_OK )
      { LastError = ""; }
      else
      {
        LastError = "Error " + LastError + "binding Long [" + bLong + "]";
      }
      return LastResult;
    }

    /// <summary>
    /// BindNull
    /// </summary>
    /// <param name="index"></param>
    /// <returns>LastResult</returns>
    public int BindNull( int index )
    {
      if ( ( LastResult = csSQLite.sqlite3_bind_null( vm, index ) ) == csSQLite.SQLITE_OK )
      { LastError = ""; }
      else
      {
        LastError = "Error " + LastError + "binding Null";
      }
      return LastResult;
    }

    /// <summary>
    /// BindText
    /// </summary>
    /// <param name="index"></param>
    /// <param name="bText"></param>
    /// <returns>LastResult</returns>
    public int BindText(  int index, string bText )
    {
      if ( ( LastResult = csSQLite.sqlite3_bind_text( vm, index, bText ,-1,null) ) == csSQLite.SQLITE_OK )
      { LastError = ""; }
      else
      {
        LastError = "Error " + LastError + "binding Text [" + bText + "]";
      }
      return LastResult;
    }

    /// <summary>
    /// BindDouble
    /// </summary>
    /// <param name="index"></param>
    /// <param name="bDouble"></param>
    /// <returns>LastResult</returns>
    public int BindDouble (int index, double bDouble) {
        if ((LastResult = csSQLite.sqlite3_bind_double(vm, index, bDouble)) == csSQLite.SQLITE_OK) { LastError = ""; } else {
            LastError = "Error " + LastError + "binding Double [" + bDouble + "]";
        }
        return LastResult;
    }
    
    /// <summary>
    /// Execute statement
    /// </summary>
    /// </param>
    /// <returns>LastResult</returns>
    public int ExecuteStep(   )
    {
      // Execute the statement
      int LastResult = csSQLite.sqlite3_step( vm );

      return LastResult;
    }
    
    public IEnumerable<RowToken> Execute () {
        try {
            var vm = VirtualMachine();
            var rt = new RowToken(this);
            while (true) {
                var rc = csSQLite.sqlite3_step(vm);
                if (rc == csSQLite.SQLITE_DONE)
                    break;
                else if (rc == csSQLite.SQLITE_MISUSE)
                    throw new InvalidOperationException();

                if (rc == csSQLite.SQLITE_ROW)
                    yield return rt;
            }
        } finally {
            Reset();
        }
    }

    /// <summary>
    /// Returns Result column as Long
    /// </summary>
    /// </param>
    /// <returns>Result column</returns>
    public long Result_Long(int index)
    {
      return csSQLite.sqlite3_column_int64( vm, index );
    }

    /// <summary>
    /// Returns Result column as Text
    /// </summary>
    /// </param>
    /// <returns>Result column</returns>
    public string Result_Text( int index )
    {
      return csSQLite.sqlite3_column_text( vm, index );
    }

    
    /// <summary>
    /// Returns Count of Result Rows
    /// </summary>
    /// </param>
    /// <returns>Count of Results</returns>
    public int ResultColumnCount( )
    {
      return vm.pResultSet == null ? 0 : vm.pResultSet.Length;
    }

    /// <summary>
    /// Reset statement
    /// </summary>
    /// </param>
    /// </returns>
    public void Reset()
    {
      // Reset the statment so it's ready to use again
      csSQLite.sqlite3_reset( vm );
    }
    
    /// <summary>
    /// Closes statement
    /// </summary>
    /// </param>
    /// <returns>LastResult</returns>
    public void Close()
    {
      csSQLite.sqlite3_finalize( ref vm );
    }

    public void Dispose () {
      Close();
    }  
  }
}
