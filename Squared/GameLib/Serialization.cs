using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Xml;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace Squared.Game {
    public interface INamedObject {
        string Name { get; }
    }
}

namespace Squared.Game.Serialization {
    public interface ITypeResolver {
        string TypeToName (Type t);
        Type NameToType (string name);
    }

    public class TypeResolverChain : ITypeResolver {
        public ITypeResolver[] Resolvers;

        public TypeResolverChain (params ITypeResolver[] resolvers) {
            Resolvers = resolvers;
        }

        public string TypeToName (Type t) {
            foreach (var resolver in Resolvers) {
                try {
                    var result = resolver.TypeToName(t);
                    if (result != null)
                        return result;
                } catch (InvalidOperationException) {
                }
            }

            throw new InvalidOperationException(
                String.Format("None of the resolvers in the chain could resolve the type '{0}'.", t.Name)
            );
        }

        public Type NameToType (string name) {
            foreach (var resolver in Resolvers) {
                try {
                    var type = resolver.NameToType(name);
                    if (type != null)
                        return type;
                } catch {
                }
            }

            throw new InvalidOperationException(
                String.Format("None of the resolvers in the chain could resolve the name '{0}'.", name)
            );
        }
    }

    public class AssemblyTypeResolver : ITypeResolver {
        private Assembly _Assembly;

        public AssemblyTypeResolver (Assembly assembly) {
            _Assembly = assembly;
        }

        public string TypeToName (Type t) {
            if (t.Assembly != _Assembly)
                return null;

            return t.Namespace + "." + t.Name;
        }

        public Type NameToType (string name) {
            return _Assembly.GetType(name, false);
        }
    }

    public class ReflectionTypeResolver : ITypeResolver {
        public string TypeToName(Type t) {
            return t.AssemblyQualifiedName;
        }

        public Type NameToType(string name) {
            return Type.GetType(name, true);
        }
    }

    public class StringValueDictionary<TValue> : Dictionary<string, TValue>, IXmlSerializable {
        public StringValueDictionary ()
            : base() {
        }
        public StringValueDictionary (IDictionary<string, TValue> dictionary)
            : base(dictionary) {
        }
        public StringValueDictionary (IEqualityComparer<string> comparer)
            : base(comparer) {
        }
        public StringValueDictionary (int capacity)
            : base(capacity) {
        }
        public StringValueDictionary (IDictionary<string, TValue> dictionary, IEqualityComparer<string> comparer)
            : base(dictionary, comparer) {
        }
        public StringValueDictionary (int capacity, IEqualityComparer<string> comparer)
            : base(capacity, comparer) {
        }

        public StringValueDictionary (XmlReader reader)
            : base() {
            SerializationExtensions.ReadDictionary<TValue>(reader, this, SerializationExtensions.DefaultTypeResolver);
        }

        System.Xml.Schema.XmlSchema IXmlSerializable.GetSchema () {
            return null;
        }

        void IXmlSerializable.ReadXml (XmlReader reader) {
            SerializationExtensions.ReadDictionary<TValue>(reader, this, SerializationExtensions.DefaultTypeResolver);
        }

        void IXmlSerializable.WriteXml (XmlWriter writer) {
            writer.WriteDictionary(this);
        }
    }

    public static class SerializationExtensions {
        public static readonly ReflectionTypeResolver DefaultTypeResolver = new ReflectionTypeResolver();

        public static StringValueDictionary<T> ReadDictionary<T> (this XmlReader reader) {
            return ReadDictionary<T>(reader, DefaultTypeResolver);
        }

        public static StringValueDictionary<T> ReadDictionary<T> (this XmlReader reader, ITypeResolver resolver) {
            var result = new StringValueDictionary<T>();

            ReadDictionary(reader, result, resolver);

            return result;
        }

        public static void ReadDictionary<T> (XmlReader reader, StringValueDictionary<T> result, ITypeResolver resolver) {
            if (reader.NodeType != XmlNodeType.Element)
                throw new InvalidDataException("Provided XmlReader must be at the start of an element");

            string key;
            int typeId;
            T value;
            var typeIds = new Dictionary<int, Type>();
            var serializers = new Dictionary<int, XmlSerializer>();
            string sentinel = reader.Name;
            var outputType = typeof(T);

            reader.ReadToDescendant("types");

            while (reader.Read()) {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "types")
                    break;
                else if (reader.NodeType == XmlNodeType.Element && reader.Name == "type") {
                    int id = int.Parse(reader.GetAttribute("id"));
                    string typeName = reader.GetAttribute("name");
                    var t = resolver.NameToType(typeName);
                    if (outputType.IsAssignableFrom(t))
                        typeIds[id] = t;
                    else
                        throw new InvalidDataException(String.Format("Cannot store an {0} into a Dictionary<String, {1}>.", t.Name, outputType.Name));
                }
            }

            reader.ReadToFollowing("values");
            reader.Read();
            while (!reader.EOF) {
                if (reader.NodeType == XmlNodeType.Element) {
                    key = reader.GetAttribute("key");
                    typeId = int.Parse(reader.GetAttribute("typeId"));

                    XmlSerializer ser;
                    if (!serializers.TryGetValue(typeId, out ser)) {
                        ser = new XmlSerializer(typeIds[typeId]);
                        serializers[typeId] = ser;
                    }
                    value = (T)ser.Deserialize(reader);

                    if (key == null) {
                        if (value is INamedObject)
                            key = ((INamedObject)value).Name;
                    }

                    if (key == null)
                        throw new InvalidDataException(String.Format("Item has no key and is not INamedObject: {0}", value));

                    result.Add(key, value);
                } else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "values") {
                    break;
                } else {
                    if (!reader.Read())
                        break;
                }
            }

            while (reader.NodeType != XmlNodeType.EndElement || reader.Name != sentinel)
                reader.Read();
        }

        public static void WriteDictionary<T> (this XmlWriter writer, IDictionary<string, T> values) {
            WriteDictionary<T>(writer, values, DefaultTypeResolver);
        }

        public static void WriteDictionary<T> (this XmlWriter writer, IDictionary<string, T> values, ITypeResolver resolver) {
            var typeIds = new Dictionary<Type, int>();
            string xml = null;
            {
                var sb = new StringBuilder();
                using (var tempWriter = XmlWriter.Create(sb)) {
                    tempWriter.WriteStartDocument();
                    tempWriter.WriteStartElement("root");

                    foreach (var kvp in values) {
                        var t = kvp.Value.GetType();
                        var ser = new XmlSerializer(t);
                        ser.Serialize(tempWriter, kvp.Value);
                        if (!typeIds.ContainsKey(t))
                            typeIds[t] = typeIds.Count;
                    }

                    tempWriter.WriteEndElement();
                    tempWriter.WriteEndDocument();
                }
                xml = sb.ToString();
            }

            writer.WriteStartElement("types");
            foreach (var item in typeIds) {
                writer.WriteStartElement("type");
                writer.WriteAttributeString("id", item.Value.ToString());
                writer.WriteAttributeString("name", resolver.TypeToName(item.Key));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            writer.WriteStartElement("values");
            using (var iter = values.GetEnumerator())
            using (var tempReader = XmlReader.Create(new StringReader(xml))) {
                tempReader.ReadToDescendant("root");

                while (tempReader.Read()) {
                    if (tempReader.NodeType == XmlNodeType.EndElement && tempReader.Name == "root")
                        break;

                    if (tempReader.NodeType == XmlNodeType.Element) {
                        if (!iter.MoveNext())
                            throw new InvalidDataException();

                        var v = iter.Current.Value;
                        var t = v.GetType();
                        var sentinel = tempReader.Name;
                        var typeId = typeIds[t];
                        var fieldName = iter.Current.Key;

                        writer.WriteStartElement(sentinel);

                        bool omitKey = false;
                        if ((v is INamedObject) && ((v as INamedObject).Name == fieldName)) {
                            T temp = default(T);
                            if (values.TryGetValue(fieldName, out temp)) {
                                if (object.Equals(v, temp))
                                    omitKey = true;
                            }
                        }

                        if (!omitKey)
                            writer.WriteAttributeString("key", fieldName);
                        writer.WriteAttributeString("typeId", typeId.ToString());

                        if (!tempReader.IsEmptyElement) {
                            using (var subtree = tempReader.ReadSubtree()) {
                                subtree.Read();
                                while (!subtree.EOF)
                                    if (subtree.Name != sentinel)
                                        writer.WriteNode(subtree, true);
                                    else
                                        subtree.Read();
                            }

                            while (tempReader.Name != sentinel || tempReader.NodeType != XmlNodeType.EndElement)
                                if (!tempReader.Read())
                                    break;
                        }

                        writer.WriteEndElement();
                    }
                }
            }

            writer.WriteEndElement();
        }
    }
}
