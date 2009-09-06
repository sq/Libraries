using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS_SQLite3;
using Squared.Game.Serialization;
using System.Xml.Serialization;
using System.IO;

namespace Squared.Game.Graph {
    public class SQLiteGraphWriter : IGraphWriter {
        public class NodeWriteContext : WriteContext, IDisposable {
            public int NodeID;
            public readonly SQLiteGraphWriter GraphWriter;

            protected SQLiteVdbe Statement;

            public NodeWriteContext (SQLiteGraphWriter gw, int nodeID) 
                : base (gw.SerializationContext) {
                GraphWriter = gw;
                NodeID = nodeID;

                Statement = new SQLiteVdbe(
                    gw.Database,
                    "INSERT INTO NodeAttributes (GraphID, NodeID, AttributeName, ValueTypeID, Value) VALUES (?, ?, ?, ?, ?)"
                );
                Statement.BindInteger(1, gw._GraphID);
            }

            public override void WriteAttribute<T> (string attributeName, T value) {
                var t = value.GetType();
                var typeID = GraphWriter.GetTypeID(t);

                Statement.BindInteger(2, NodeID);
                Statement.BindText(3, attributeName);
                Statement.BindInteger(4, typeID);

                BindValue(5, t, value);

                GraphWriter.Database.ExecuteNonQuery(Statement);
                Statement.Reset();
            }

            protected void BindValue (int columnIndex, Type valueType, object value) {
                if (value == null)
                    csSQLite.sqlite3_bind_null(Statement.VirtualMachine(), columnIndex);
                else if (typeof(int).IsAssignableFrom(valueType))
                    Statement.BindInteger(columnIndex, (int)value);
                else if (typeof(long).IsAssignableFrom(valueType))
                    Statement.BindLong(columnIndex, (long)value);
                else if (typeof(string).IsAssignableFrom(valueType))
                    Statement.BindText(columnIndex, (string)value);
                else {
                    var sw = new StringWriter();
                    var xs = new XmlSerializer(valueType);
                    xs.Serialize(sw, value);
                    Statement.BindText(columnIndex, sw.GetStringBuilder().ToString());
                }
            }

            public void Dispose () {
                Statement.Close();
            }
        }

        public readonly SQLiteDatabase Database;
        public readonly string GraphName;
        public readonly ITypeResolver TypeResolver;
        public readonly SerializationContext SerializationContext;

        protected int _GraphID;
        protected Stack<int> _NodeIDStack = new Stack<int>();
        protected Dictionary<INode, int> _NodeIDs = new Dictionary<INode, int>();
        protected Dictionary<Type, int> _TypeIDs = new Dictionary<Type, int>();

        public SQLiteGraphWriter (SQLiteDatabase database, string graphName)
            : this(database, graphName, SerializationExtensions.DefaultTypeResolver) {
            Database = database;
            GraphName = graphName;
        }

        public SQLiteGraphWriter (SQLiteDatabase database, string graphName, ITypeResolver typeResolver) {
            Database = database;
            GraphName = graphName;
            TypeResolver = typeResolver;
        }

        protected void CreateTable (string tableName, params string[] columnNames) {
            var sql = String.Format(
                @"CREATE TABLE IF NOT EXISTS {0} ({1});",
                tableName, String.Join(", \r\n", columnNames)
            );

            Console.WriteLine(sql);
            Database.ExecuteNonQuery(sql);
        }

        protected void CreateIndex (string tableName, string columnName) {
            var indexName = String.Format("{0}_{1}", tableName, columnName);
            var sql = String.Format(
                @"CREATE INDEX IF NOT EXISTS {0} ON {1} ({2})",
                indexName, tableName, columnName
            );

            Console.WriteLine(sql);
            Database.ExecuteNonQuery(sql);
        }

        protected void CreateTables () {
            CreateTable(
                "Graphs", 
                "GraphID int",
                "GraphName text",
                "RootNodeID int",
                "PRIMARY KEY (GraphID)"
            );

            CreateIndex("Graphs", "GraphName");

            CreateTable(
                "Types",
                "TypeID int",
                "TypeName string",
                "PRIMARY KEY (TypeID)"
            );

            CreateIndex("Types", "TypeName");

            CreateTable(
                "Nodes",
                "GraphID int",
                "NodeID int",
                "TypeID int",
                "PRIMARY KEY (GraphID, NodeID)"
            );

            CreateTable(
                "NodeRelationships",
                "GraphID int",
                "NodeID int",
                "ParentNodeID int",
                "PRIMARY KEY (GraphID, ParentNodeID, NodeID)"
            );

            CreateTable(
                "NodeAttributes",
                "GraphID int",
                "NodeID int",
                "AttributeName text",
                "ValueTypeID int",
                "Value variant",
                "PRIMARY KEY (GraphID, NodeID, AttributeName)"
            );
        }

        protected int GetTypeIDFromDatabase (Type type) {
            var typeName = TypeResolver.TypeToName(type);
            var sql = String.Format(
                "SELECT TypeID FROM Types WHERE TypeName = '{0}';",
                typeName
            );
            var results = Database.ExecuteQuery(sql);
            if (results.Rows.Count > 0) {
                return Convert.ToInt32(results.Rows[0][0]);
            } else {
                var insertSql = String.Format(
                    @"INSERT INTO Types (TypeID, TypeName) 
                    VALUES (
                        (SELECT IFNULL(MAX(TypeID) + 1, 0) FROM Types),
                        '{0}'
                    );",
                    typeName
                );
                Database.ExecuteNonQuery(insertSql);

                results = Database.ExecuteQuery(sql);
                return Convert.ToInt32(results.Rows[0][0]);
            }
        }

        protected void CreateGraphID () {
            var sql = String.Format(
                "SELECT GraphID FROM Graphs WHERE GraphName = '{0}';",
                GraphName
            );

            var results = Database.ExecuteQuery(sql);
            if (results.Rows.Count > 0) {
                _GraphID = Convert.ToInt32(results.Rows[0][0]);
            } else {
                var insertSql = String.Format(
                    @"INSERT INTO Graphs (GraphID, GraphName) 
                    VALUES (
                        (SELECT IFNULL(MAX(GraphID) + 1, 0) FROM Graphs),
                        '{0}'
                    );",
                    GraphName
                );
                Database.ExecuteNonQuery(insertSql);

                results = Database.ExecuteQuery(sql);
                _GraphID = Convert.ToInt32(results.Rows[0][0]);
            }
        }

        protected int GetTypeID (Type type) {
            int result;
            if (!_TypeIDs.TryGetValue(type, out result)) {
                result = GetTypeIDFromDatabase(type);
                _TypeIDs[type] = result;
            }

            return result;
        }

        protected bool GetNodeID (INode node, out int result) {
            if (!_NodeIDs.TryGetValue(node, out result)) {
                result = _NodeIDs.Count;
                _NodeIDs[node] = result;
                return true;
            }

            return false;
        }

        protected void SerializeNode (INode node, int nodeID, int typeID) {
            var sql = String.Format(
                @"INSERT INTO Nodes (GraphID, NodeID, TypeID) VALUES ({0}, {1}, {2})",
                _GraphID, nodeID, typeID
            );
            Database.ExecuteNonQuery(sql);

            using (var wc = new NodeWriteContext(this, nodeID)) {
                var helper = new SerializationHelper(node.GetType());

                foreach (var field in helper.SerializedFields)
                    wc.WriteAttribute<object>(field.Name, field.GetValue(node));

                foreach (var property in helper.SerializedProperties)
                    wc.WriteAttribute<object>(property.Name, property.GetValue(node, null));
            }            
        }

        protected int WriteNode (INode node) {
            var type = node.GetType();
            int nodeID, typeID;
            typeID = GetTypeID(type);

            if (GetNodeID(node, out nodeID))
                SerializeNode(node, nodeID, typeID);

            Console.WriteLine("{0}", node);

            int? parentNodeId = null;
            if (_NodeIDStack.Count > 0)
                parentNodeId = _NodeIDStack.Peek();

            var sql = String.Format(
                @"INSERT INTO NodeRelationships (GraphID, NodeID, ParentNodeID) VALUES ({0}, {1}, {2});",
                _GraphID,
                nodeID,
                (parentNodeId.HasValue) ? parentNodeId.Value.ToString() : "NULL"
            );
            Database.ExecuteNonQuery(sql);

            if (!parentNodeId.HasValue) {
                sql = String.Format(
                    "UPDATE Graphs SET RootNodeID = {0} WHERE GraphID = {1}",
                    nodeID,
                    _GraphID
                );
                Database.ExecuteNonQuery(sql);
            }

            return nodeID;
        }

        public void BeginWrite (INode root) {
            CreateTables();
            Database.ExecuteNonQuery("BEGIN");
            CreateGraphID();
        }

        public void WriteNodeHeader (INode node) {
            var nodeID = WriteNode(node);
            _NodeIDStack.Push(nodeID);
        }

        public void WriteNodeFooter (INode node) {
            _NodeIDStack.Pop();
        }

        public void EndWrite () {
            Database.ExecuteNonQuery("END");
        }
    }
}
