using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS_SQLite3;
using Squared.Game.Serialization;
using System.Xml.Serialization;
using System.IO;

#if XBOX360
using CS_SQLite3.XNA;
#else
using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using Squared.Util;
#endif

namespace Squared.Game.Graph {
    public class SQLiteGraphWriter : IGraphWriter, IDisposable {
        public class NodeWriteContext : WriteContext {
            public long NodeID;
            public readonly SQLiteGraphWriter GraphWriter;
            public readonly SQLiteVdbe Statement;

            public NodeWriteContext (SQLiteGraphWriter gw, SQLiteVdbe statement, long nodeID) 
                : base (gw.Context) {
                GraphWriter = gw;
                NodeID = nodeID;

                Statement = statement;
            }

            public void WriteAttribute<T> (long attributeID, Type attributeType, T value) {
                var typeID = GraphWriter.GetTypeID(attributeType);

                BindValue(4, attributeType, value);

                Statement.BindLong(1, NodeID);
                Statement.BindLong(2, attributeID);
                Statement.BindLong(3, typeID);

                GraphWriter.Database.ExecuteNonQuery(Statement);
            }

            public override void WriteAttribute<T> (string attributeName, Type attributeType, T value) {
                var attributeID = GraphWriter.GetStringID(attributeName);
                WriteAttribute<T>(attributeID, attributeType, value);
            }

            public void BindValue (int columnIndex, Type valueType, object value) {
                if (SerializationHelper.IsNullable(valueType)) {
                    value = SerializationHelper.UnpackNullable(value);
                    valueType = SerializationHelper.GetNullableInnerType(valueType);
                }

                ITypeSerializer serializer;
                if (value == null) {
                    csSQLite.sqlite3_bind_null(Statement.VirtualMachine(), columnIndex);
                } else if (GraphWriter.Context.Serializers.TryGetValue(valueType, out serializer)) {
                    var vs = serializer as IValueSerializer;
                    if (vs != null) {
                        var blob = vs.Write(value);
                        Statement.BindLong(columnIndex, GraphWriter.GetStringID(Convert.ToBase64String(blob)));
                    } else {
                        throw new InvalidDataException();
                    }
                } else if (valueType.IsPrimitive) {
                    if ((valueType == typeof(double)) ||
                        (valueType == typeof(float))
                    ) {
                        Statement.BindDouble(columnIndex, Convert.ToDouble(value));
                    } else if (valueType == typeof(decimal)) {
                        throw new InvalidDataException();
                    } else if (valueType == typeof(ulong)) {
                        throw new InvalidDataException();
                    } else {
                        Statement.BindLong(columnIndex, Convert.ToInt64(value));
                    }
                } else if (valueType == typeof(DateTime)) {
                    Statement.BindText(columnIndex, ((DateTime)value).ToString("u"));
                } else if (valueType == typeof(string)) {
                    Statement.BindLong(columnIndex, GraphWriter.GetStringID((string)value));
                } else if (valueType.IsEnum) {
                    Statement.BindLong(columnIndex, Convert.ToInt64(value));
                } else {
                    Statement.BindLong(columnIndex, GraphWriter.SerializeAnonymousObject(value));
                }
            }
        }

        public readonly SQLiteDatabase Database;
        public readonly SerializationContext Context;

        protected Stack<long> _NodeIDStack = new Stack<long>(128);
        protected Dictionary<object, long> _NodeIDs = new Dictionary<object, long>(1024, new ReferenceComparer<object>());
        protected Dictionary<Type, long> _TypeIDs = new Dictionary<Type, long>(128, new ReferenceComparer<Type>());
        protected Dictionary<string, long> _StringIDs = new Dictionary<string, long>(1024, StringComparer.Ordinal);

        protected SQLiteVdbe _WriteString, _GetString;
        protected SQLiteVdbe _WriteType, _GetType;
        protected SQLiteVdbe _WriteAttribute;
        protected SQLiteVdbe _WriteNode;
        protected SQLiteVdbe _WriteNodeRelationship;

        public SQLiteGraphWriter (SQLiteDatabase database)
            : this(database, SerializationExtensions.DefaultContext) {
        }

        public SQLiteGraphWriter (SQLiteDatabase database, SerializationContext context) {
            Database = database;
            Context = context;
        }

        protected void Dispose (ref SQLiteVdbe statement) {
            if (statement != null) {
                statement.Dispose();
                statement = null;
            }
        }

        public void Dispose () {
            Dispose(ref _WriteString);
            Dispose(ref _GetString);
            Dispose(ref _WriteType);
            Dispose(ref _GetType);
            Dispose(ref _WriteAttribute);
            Dispose(ref _WriteNode);
            Dispose(ref _WriteNodeRelationship);
        }

        protected void CreateTable (string tableName, params string[] columnNames) {
            var sql = String.Format(
                @"CREATE TABLE IF NOT EXISTS {0} ({1});",
                tableName, String.Join(", \r\n", columnNames)
            );

            Database.ExecuteNonQuery(sql);
        }

        protected void CreateIndex (string tableName, string columnName) {
            var indexName = String.Format("{0}_{1}", tableName, columnName);
            var sql = String.Format(
                @"CREATE INDEX IF NOT EXISTS {0} ON {1} ({2})",
                indexName, tableName, columnName
            );

            Database.ExecuteNonQuery(sql);
        }

        protected void CreateTables () {
            CreateTable(
                "Types",
                "TypeID INTEGER PRIMARY KEY",
                "TypeName TEXT"
            );

            CreateTable(
                "Nodes",
                "NodeID INTEGER PRIMARY KEY",
                "TypeID INTEGER"
            );

            CreateTable(
                "NodeRelationships",
                "NodeID INTEGER",
                "ParentNodeID INTEGER",
                "PRIMARY KEY (NodeID, ParentNodeID)"
            );

            CreateTable(
                "NodeAttributes",
                "NodeID INTEGER",
                "AttributeNameID INTEGER",
                "ValueTypeID INTEGER",
                "Value VARIANT",
                "PRIMARY KEY (NodeID DESC, AttributeNameID ASC)"
            );

            CreateTable(
                "Strings",
                "StringID INTEGER PRIMARY KEY",
                "Text TEXT"
            );
        }

        protected void CreateStatements () {
            _WriteString = new SQLiteVdbe(Database, "INSERT INTO Strings (StringID, Text) VALUES (NULL, ?)");
            _GetString = new SQLiteVdbe(Database, "SELECT StringID FROM Strings WHERE Text = ?");
            _WriteType = new SQLiteVdbe(Database, "INSERT INTO Types (TypeID, TypeName) VALUES (NULL, ?)");
            _GetType = new SQLiteVdbe(Database, "SELECT TypeID FROM Types WHERE TypeName = ?");
            _WriteAttribute = new SQLiteVdbe(Database, "INSERT INTO NodeAttributes (NodeID, AttributeNameID, ValueTypeID, Value) VALUES (?, ?, ?, ?)");
            _WriteNode = new SQLiteVdbe(Database, "INSERT INTO Nodes (NodeID, TypeID) VALUES (NULL, ?)");
            _WriteNodeRelationship = new SQLiteVdbe(Database, "INSERT INTO NodeRelationships (NodeID, ParentNodeID) VALUES (?, ?)");
        }

        protected long GetStringID (string text) {
            long result;
            if (!_StringIDs.TryGetValue(text, out result))
                result = _StringIDs[text] = GetStringIDFromDatabase(text);

            return result;
        }

        protected long GetStringIDFromDatabase (string text) {
            _WriteString.BindText(1, text);
            Database.ExecuteNonQuery(_WriteString);

            return Database.Connection().lastRowid;
        }

        protected long GetTypeIDFromDatabase (Type type) {
            var typeName = Context.TypeResolver.TypeToName(type);

            _WriteType.BindText(1, typeName);
            Database.ExecuteNonQuery(_WriteType);

            return Database.Connection().lastRowid;
        }

        protected long GetTypeID (Type type) {
            long result;
            if (!_TypeIDs.TryGetValue(type, out result)) {
                result = GetTypeIDFromDatabase(type);
                _TypeIDs[type] = result;
            }

            return result;
        }

        protected long SerializeAnonymousObject (object obj) {
            long result;
            if (_NodeIDs.TryGetValue(obj, out result))
                return result;

            long typeID = GetTypeID(obj.GetType());
            return SerializeNode(obj, typeID);
        }

        protected long SerializeNode (object node, long typeID) {
            var helper = SerializationHelper.Create(node.GetType());

            _WriteNode.BindLong(1, typeID);

            Database.ExecuteNonQuery(_WriteNode);
            long nodeID = Database.Connection().lastRowid;
            _NodeIDs[node] = nodeID;

            var wc = new NodeWriteContext(this, _WriteAttribute, nodeID);

            foreach (var member in helper.SerializedMembers.Values)
                wc.WriteAttribute<object>(member.Name, member.Type, member.GetValue(node));

            int itemCount = 0;
            if (!(node is INode) && (helper.GetItemCount != null))
                itemCount = helper.GetItemCount(node);

            if (itemCount > 0) {
                // Start at -n, end at -1 - so that iterating over the attributes later returns them in the correct order
                int i = -(itemCount);
                foreach (object element in (System.Collections.IEnumerable)node) {
                    wc.WriteAttribute<object>(i, helper.ItemType, element);
                    i += 1;
                }
            }

            return nodeID;
        }

        protected long WriteNode (INode node) {
            var type = node.GetType();
            long nodeID, typeID;
            typeID = GetTypeID(type);

            if (!_NodeIDs.TryGetValue(node, out nodeID))
                nodeID = SerializeNode(node, typeID);

            _WriteNodeRelationship.BindLong(1, nodeID);
            if (_NodeIDStack.Count > 0)
                _WriteNodeRelationship.BindLong(2, _NodeIDStack.Peek());
            else
                _WriteNodeRelationship.BindNull(2);

            Database.ExecuteNonQuery(_WriteNodeRelationship);

            return nodeID;
        }

        public void BeginWrite (INode root) {
            CreateTables();
            CreateStatements();
            Database.ExecuteNonQuery("BEGIN");
        }

        public void WriteNodeHeader (INode node) {
            var nodeID = WriteNode(node);
            _NodeIDStack.Push(nodeID);
        }

        public void WriteNodeFooter (INode node) {
            _NodeIDStack.Pop();
        }

        public void EndWrite () {
            Database.ExecuteNonQuery("COMMIT");
        }
    }

    public class SQLiteGraphReader : IDisposable {
        protected struct DeferredAttribute {
            public object Object;
            public Action<object, object> Setter;
            public long NodeID;
        }

        public readonly SQLiteDatabase Database;
        public readonly SerializationContext Context;

        protected Dictionary<long, Type> _Types = new Dictionary<long, Type>(128);
        protected Dictionary<long, object> _Nodes = new Dictionary<long, object>(1024);
        protected Dictionary<long, string> _Strings = new Dictionary<long, string>(1024);
        protected List<DeferredAttribute> _DeferredAttributes = new List<DeferredAttribute>(1024);

        protected long _RootNodeID;

        public SQLiteGraphReader (SQLiteDatabase database)
            : this(database, SerializationExtensions.DefaultContext) {
            Database = database;
        }

        public SQLiteGraphReader (SQLiteDatabase database, SerializationContext context) {
            Database = database;
            Context = context;
        }

        public void Dispose () {
        }

        protected void ReadTypes () {
            var sql = "SELECT TypeID, TypeName FROM Types;";

            using (var statement = new SQLiteVdbe(Database, sql))
            foreach (var row in statement.Execute()) {
                var typeID = csSQLite.sqlite3_column_int64(statement.VirtualMachine(), 0);
                var typeName = csSQLite.sqlite3_column_text(statement.VirtualMachine(), 1);
                _Types[typeID] = Context.TypeResolver.NameToType(typeName);
            }
        }

        protected void ReadStrings () {
            var sql = "SELECT StringID, Text FROM Strings;";

            using (var statement = new SQLiteVdbe(Database, sql))
            foreach (var row in statement.Execute()) {
                var stringID = csSQLite.sqlite3_column_int64(statement.VirtualMachine(), 0);
                var text = csSQLite.sqlite3_column_text(statement.VirtualMachine(), 1);
                _Strings[stringID] = text;
            }
        }

        protected void ReadNodes () {
            var sql = "SELECT NodeID, TypeID FROM Nodes";

            var result = new Dictionary<long, object>();

            using (var statement = new SQLiteVdbe(Database, sql))
            foreach (var row in statement.Execute()) {
                var nodeID = csSQLite.sqlite3_column_int64(statement.VirtualMachine(), 0);
                var typeID = csSQLite.sqlite3_column_int64(statement.VirtualMachine(), 1);
                var type = _Types[typeID];
                var constructor = type.GetConstructor(Type.EmptyTypes);
                object instance;

                if (constructor != null) {
                    instance = constructor.Invoke(null);
                } else if (type.IsValueType) {
                    instance = Activator.CreateInstance(type);
                } else {
                    throw new InvalidDataException(String.Format("Type {0} has no parameterless constructor", type.Name));
                }

                _Nodes[nodeID] = instance;
            }
       }

        protected void ReadRelationships () {
            using (var statement = new SQLiteVdbe(Database, "SELECT NodeID, ParentNodeID FROM NodeRelationships"))
            foreach (var row in statement.Execute()) {
                var vm = statement.VirtualMachine();
                var nodeID = csSQLite.sqlite3_column_int64(vm, 0);
                var child = (INode)(_Nodes[nodeID]);

                if (csSQLite.sqlite3_column_type(vm, 1) == csSQLite.SQLITE_NULL) {
                    _RootNodeID = nodeID;
                    continue;
                }

                var parentID = csSQLite.sqlite3_column_int64(vm, 1);
                var parent = (INode)(_Nodes[parentID]);

                parent.AddChild(child);
            }
        }

        protected void ReadNodeAttributes () {
            // We need to iterate through the nodes in reverse order so that
            // we populate value types before assigning them to parent objects
            // (god this is disgusting :|)
            var sql = "SELECT NodeID, AttributeNameID, ValueTypeID, Value FROM NodeAttributes ORDER BY NodeID";

            long lastNodeID = -1;

            object node = null, value = null;
            SerializationHelper helper = null;
            SerializationHelper.Member member;
            using (var statement = new SQLiteVdbe(Database, sql))
            foreach (var row in statement.Execute()) {
                var vm = statement.VirtualMachine();

                var nodeID = csSQLite.sqlite3_column_int64(vm, 0);
                if (nodeID != lastNodeID) {
                    lastNodeID = nodeID;
                    node = _Nodes[nodeID];
                    helper = SerializationHelper.Create(node.GetType());
                }

                var attributeID = csSQLite.sqlite3_column_int64(vm, 1);
                var typeID = csSQLite.sqlite3_column_int64(vm, 2);
                var attributeType = _Types[typeID];
                var columnType = csSQLite.sqlite3_column_type(vm, 3);
                bool forceConversion = false;

                if (SerializationHelper.IsNullable(attributeType)) {
                    attributeType = SerializationHelper.GetNullableInnerType(attributeType);
                    forceConversion = true;
                }

                ITypeSerializer serializer;
                if (columnType == csSQLite.SQLITE_NULL) {
                    value = null;
                } else if (Context.Serializers.TryGetValue(attributeType, out serializer)) {
                    var vs = serializer as IValueSerializer;
                    if (vs != null) {
                        long stringID = csSQLite.sqlite3_column_int64(vm, 3);
                        var blob = Convert.FromBase64String(_Strings[stringID]);
                        value = vs.Read(blob);
                    } else {
                        throw new InvalidDataException();
                    }
                } else if (attributeType.IsPrimitive ||
                    attributeType.IsEnum ||
                    (attributeType == typeof(decimal)) ||
                    (attributeType == typeof(ulong)) ||
                    (attributeType == typeof(DateTime))) {

                    if (columnType == csSQLite.SQLITE_INTEGER)
                        value = csSQLite.sqlite3_column_int64(vm, 3);
                    else if (columnType == csSQLite.SQLITE_FLOAT)
                        value = csSQLite.sqlite3_column_double(vm, 3);
                    else if (columnType == csSQLite.SQLITE_TEXT)
                        value = csSQLite.sqlite3_column_text(vm, 3);
                    else
                        throw new InvalidDataException();

                } else if (attributeType == typeof(string)) {
                    long stringID = csSQLite.sqlite3_column_int64(vm, 3);
                    value = _Strings[stringID];
                } else {
                    long valueID = csSQLite.sqlite3_column_int64(vm, 3);

                    if (attributeID > 0) {
                        var attributeName = _Strings[attributeID];
                        if (!helper.SerializedMembers.TryGetValue(attributeName, out member))
                            continue;

                        _DeferredAttributes.Add(new DeferredAttribute {
                            NodeID = valueID,
                            Setter = member.SetValue,
                            Object = node
                        });
                    } else {
                        _DeferredAttributes.Add(new DeferredAttribute {
                            NodeID = valueID,
                            Setter = helper.AddItem,
                            Object = node
                        });
                    }
                    continue;
                }

                if (forceConversion && (value != null))
                    value = Convert.ChangeType(value, attributeType);

                if (attributeID < 0) {
                    helper.AddItem(node, value);
                } else {
                    var attributeName = _Strings[attributeID];
                    if (!helper.SerializedMembers.TryGetValue(attributeName, out member))
                        continue;

                    member.SetValue(node, value);
                }
            }
        }

        protected void SetDeferredAttributes () {
            foreach (var da in _DeferredAttributes)
                da.Setter(da.Object, _Nodes[da.NodeID]);

            _DeferredAttributes.Clear();
        }

        public INode Read () {
            ReadTypes();
            ReadStrings();
            ReadNodes();
            ReadNodeAttributes();
            SetDeferredAttributes();
            ReadRelationships();

            return (INode)_Nodes[_RootNodeID];
        }
    }
}
