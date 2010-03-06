using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util.Bind;
using System.Linq.Expressions;
using System.Data;

namespace Squared.Task.Data {
    public class PropertySerializer : IDisposable {
        public Func<IBoundMember, string> GetMemberName;
        public List<IBoundMember> Bindings = new List<IBoundMember>();
        public readonly Query WriteValue;
        public readonly Query ReadValue;

        public PropertySerializer (
            ConnectionWrapper database, string tableName
        ) : this (
            database, tableName,
            "name", "value"
        ) {
        }

        public PropertySerializer (
            ConnectionWrapper database, string tableName, 
            string nameColumn, string valueColumn
        ) : this (
            database, tableName,
            nameColumn, valueColumn,
            null
        ) {
            GetMemberName = GetDefaultMemberName;
        }

        public PropertySerializer (
            ConnectionWrapper database, string tableName, 
            string nameColumn, string valueColumn, 
            Func<IBoundMember, string> getMemberName
        ) : this (
            database.BuildQuery(String.Format("REPLACE INTO {0} ({1}, {2}) VALUES (?, ?)", tableName, nameColumn, valueColumn)),
            database.BuildQuery(String.Format("SELECT {2} FROM {0} WHERE {1} = ? LIMIT 1", tableName, nameColumn, valueColumn)),
            getMemberName
        ) {
        }

        public PropertySerializer (
            Query writeValue, Query readValue
        ) : this (
            writeValue, readValue, null
        ) {
            GetMemberName = GetDefaultMemberName;
        }

        public PropertySerializer (
            Query writeValue, Query readValue, 
            Func<IBoundMember, string> getMemberName
        ) {
            WriteValue = writeValue;
            writeValue.Parameters[0].DbType = DbType.String;
            writeValue.Parameters[1].DbType = DbType.Object;
            ReadValue = readValue;
            readValue.Parameters[0].DbType = DbType.String;
            GetMemberName = getMemberName;
        }

        public string GetDefaultMemberName (IBoundMember member) {
            return member.Name;
        }

        public void Bind<T> (Expression<Func<T>> target) {
            Bindings.Add(BoundMember.New(target));
        }

        public IEnumerator<object> Save () {
            foreach (var member in Bindings) {
                var name = GetMemberName(member);

                yield return WriteValue.ExecuteNonQuery(name, member.Value);
            }
        }

        public IEnumerator<object> Load () {
            foreach (var member in Bindings) {
                var name = GetMemberName(member);

                var fResult = ReadValue.ExecuteReader(name);
                yield return fResult;

                using (var reader = fResult.Result) {
                    if (reader.Reader.Read())
                        member.Value = reader.Reader.GetValue(0);
                }
            }
        }

        public void Dispose () {
            Bindings.Clear();
            WriteValue.Dispose();
            ReadValue.Dispose();
        }
    }
}
