using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace Ndexer {
    public class DiskMonitor {
        private Queue<string> _ChangedFiles = new Queue<string>();
        private Queue<string> _DeletedFiles = new Queue<string>();
        private FileSystemWatcher[] _Watchers;
        private Regex[] _Filters;
        private Regex[] _Exclusions;
        private bool _Monitoring = false;

        public DiskMonitor (string[] folders, string[] filters, string[] exclusions) {
            _Watchers = (from f in folders select CreateWatcher(f)).ToArray();
            _Filters = (from f in filters select CompileFilter(f)).ToArray();
            _Exclusions = (from e in exclusions select
                new Regex(e, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)
            ).ToArray();
        }

        public bool Monitoring {
            get {
                return _Monitoring;
            }
            set {
                foreach (var watcher in _Watchers)
                    watcher.EnableRaisingEvents = value;
                _Monitoring = value;
            }
        }

        private Regex CompileFilter (string filter) {
            if (filter.EndsWith(".*"))
                filter = filter.Substring(0, filter.Length - 2);
            filter = "^" + Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(
                filter, 
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture
            );
        }

        private FileSystemWatcher CreateWatcher (string folder) {
            folder = System.IO.Path.GetFullPath(folder);
            var result = new FileSystemWatcher(folder);
            result.InternalBufferSize = 32 * 1024;
            result.IncludeSubdirectories = true;
            result.Filter = "*";
            result.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
            result.Changed += new FileSystemEventHandler(file_Changed);
            result.Created += new FileSystemEventHandler(file_Changed);
            result.Deleted += new FileSystemEventHandler(file_Deleted);
            result.Renamed += new RenamedEventHandler(file_Renamed);
            result.Error += new ErrorEventHandler(BufferOverflowed);
            return result;
        }

        void file_Renamed (object sender, RenamedEventArgs e) {
            lock (_ChangedFiles) {
                _ChangedFiles.Enqueue(e.OldFullPath);
                _ChangedFiles.Enqueue(e.FullPath);
            }
        }

        void file_Changed (object sender, FileSystemEventArgs e) {
            lock (_ChangedFiles) {
                _ChangedFiles.Enqueue(e.FullPath);
            }
        }

        void file_Deleted (object sender, FileSystemEventArgs e) {
            lock (_DeletedFiles) {
                _DeletedFiles.Enqueue(e.FullPath);
            }
        }

        void BufferOverflowed (object sender, ErrorEventArgs e) {
            Console.WriteLine("File system monitoring buffer overflow");
        }

        public IEnumerable<string> GetChangedFiles () {
            while (true) {
                string item = null;
                lock (_ChangedFiles) {
                    if (_ChangedFiles.Count > 0)
                        item = _ChangedFiles.Dequeue();
                    else
                        yield break;
                }

                bool excluded = false;
                foreach (var exclusion in _Exclusions) {
                    if (exclusion.IsMatch(item)) {
                        excluded = true;
                        break;
                    }
                }

                if (excluded)
                    continue;

                bool filtered = true;
                foreach (var filter in _Filters) {
                    if (filter.IsMatch(item)) {
                        filtered = false;
                        break;
                    }
                }

                if (!filtered)
                    yield return item;
            }
        }

        public IEnumerable<string> GetDeletedFiles () {
            while (true) {
                string item = null;
                lock (_DeletedFiles) {
                    if (_DeletedFiles.Count > 0)
                        item = _DeletedFiles.Dequeue();
                    else
                        yield break;
                }

                bool excluded = false;
                foreach (var exclusion in _Exclusions) {
                    if (exclusion.IsMatch(item)) {
                        excluded = true;
                        break;
                    }
                }

                if (excluded)
                    continue;

                yield return item;
            }
        }
    }
}
