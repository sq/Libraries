using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CS_SQLite3.XNA {
    public static class EncodingExtensions {
        public static string GetString (this Encoding encoding, byte[] bytes) {
            return encoding.GetString(bytes, 0, bytes.Length);
        }
    }

    public class DataTable {
        public readonly DataRowCollection Rows;
        public readonly DataColumnCollection Columns;

        public string TableName;

        public DataTable () 
            : this (null) {
        }

        public DataTable (string tableName) {
            TableName = tableName;
            Rows = new DataRowCollection();
            Columns = new DataColumnCollection();
        }

        public void Clear () {
            Rows.Clear();
        }
    }

    public class DataRow {
        public DataRow (params object[] values) {
            ItemArray = values;
        }

        public object[] ItemArray {
            get;
            set;
        }
    }

    public class DataRowCollection : List<DataRow> {
        public DataRow Add (params object[] values) {
            var result = new DataRow(values);
            base.Add(result);
            return result;
        }
    }

    public class DataColumn {
        public readonly string ColumnName;
        public readonly Type DataType;

        public DataColumn (string columnName, Type dataType) {
            ColumnName = columnName;
            DataType = dataType;
        }
    }

    public class DataColumnCollection : List<DataColumn> {
        public DataColumn Add (string columnName, Type dataType) {
            var result = new DataColumn(columnName, dataType);
            base.Add(result);
            return result;
        }
    }
}
