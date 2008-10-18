using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.IO;

namespace Squared.Util.Dependency {
    public delegate object DependencyResolver(Stream input);

    public struct DependencyTableEntry {
        public FieldInfo Field;
        public DependencyAttribute Attribute;
    }

    public class DependencyTable : List<DependencyTableEntry> {
    }

    public static class DependencyResolvers {
        [DependencyResolver]
        public static string ResolveString (Stream input) {
            StreamReader sr = new StreamReader(input, true);
            return sr.ReadToEnd();
        }

        [DependencyResolver]
        public static byte[] ResolveBinaryFile (Stream input) {
            MemoryStream ms = new MemoryStream();
            {
                byte[] buffer = new byte[1024];
                int bytesRead = 0;
                do {
                    bytesRead = input.Read(buffer, 0, buffer.Length);
                    ms.Write(buffer, 0, bytesRead);
                } while (bytesRead > 0);
            }

            byte[] result = new byte[ms.Length];
            ms.Seek(0, SeekOrigin.Begin);
            ms.Read(result, 0, result.Length);
            return result;
        }
    }

    public class DependencyContext {
        private Dictionary<Type, DependencyResolver> _Resolvers = new Dictionary<Type, DependencyResolver>();
        private Dictionary<Type, DependencyTable> _DependencyCache = new Dictionary<Type, DependencyTable>();

        protected string _Root = "";

        public string Root {
            get {
                return _Root;
            }
        }

        public DependencyContext (string root) {
            _Root = root;
            AddResolversFromType(typeof(DependencyResolvers));
        }

        public void AddResolversFromType (Type type) {
            foreach (MethodInfo mi in type.GetMethods()) {
                object[] attributes = mi.GetCustomAttributes(typeof(DependencyResolverAttribute), true);

                if (attributes.Length == 1) {
                    Type rt = mi.ReturnType;
                    Delegate d = Delegate.CreateDelegate(typeof(DependencyResolver), mi);
                    AddResolver(rt, (DependencyResolver)d);
                }
            }
        }

        public void AddResolver (Type type, DependencyResolver resolver) {
            _Resolvers.Add(type, resolver);
        }

        public DependencyResolver GetResolver (Type type) {
            DependencyResolver dr = null;
            if (_Resolvers.TryGetValue(type, out dr)) {
                return dr;
            } else {
                throw new Exception(String.Format("No dependency resolver registered for type {0}.", type));
            }
        }

        protected DependencyTable GetDependenciesForType (Type type) {
            DependencyTable dt = null;
            if (!_DependencyCache.TryGetValue(type, out dt)) {
                dt = new DependencyTable();

                foreach (FieldInfo fi in type.GetFields()) {
                    object[] attributes = fi.GetCustomAttributes(typeof(DependencyAttribute), true);

                    if (attributes.Length == 1) {
                        var dte = new DependencyTableEntry();
                        var da = (DependencyAttribute)attributes[0];
                        dte.Field = fi;
                        dte.Attribute = da;
                        dt.Add(dte);
                    }
                }

                _DependencyCache.Add(type, dt);
            }

            return dt;
        }

        public void ResolveDependencies<T> (ref T obj) {
            object value = obj;
            Type t = typeof(T);
            DependencyTable dt = GetDependenciesForType(t);

            foreach (DependencyTableEntry dte in dt) {
                dte.Attribute.Resolve(this, value, dte.Field);
            }

            obj = (T)value;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class DependencyResolverAttribute : Attribute {
        public DependencyResolverAttribute () {
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class DependencyAttribute : Attribute {
        public DependencyAttribute (string filename) {
            Filename = filename;
        }

        public string Filename {
            get;
            set;
        }

        public bool Available () {
            return System.IO.File.Exists(Filename);
        }

        public void Resolve (DependencyContext context, object targetInstance, FieldInfo targetField) {
            DependencyResolver r = context.GetResolver(targetField.FieldType);
            string filename = System.IO.Path.Combine(context.Root, Filename);
            Stream input = System.IO.File.OpenRead(filename);
            object value = r(input);
            targetField.SetValue(targetInstance, value);
        }
    }
}
