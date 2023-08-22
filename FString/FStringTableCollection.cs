using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.FString {
    public class FStringTableCollection {
        protected readonly Dictionary<string, FStringTable> Cache = new Dictionary<string, FStringTable>();

        public event OnMissingString MissingString;

        private OnMissingString ForwardOnMissingString;

        public FStringTableCollection () {
            ForwardOnMissingString = (table, key) => {
                if (MissingString != null)
                    MissingString(table, key);
            };
        }

        public void ReloadAll () {
            lock (Cache) {
                foreach (var kvp in Cache) {
                    using (var stream = File.OpenRead(kvp.Key))
                        kvp.Value.PopulateFromXmlStream(stream, true);
                }
            }
        }

        public void ClearCache () {
            lock (Cache)
                Cache.Clear();
        }

        public FStringTable LoadFromPath (string path, bool optional) {
            FStringTable result;
            lock (Cache)
                if (Cache.TryGetValue(path, out result))
                    return result;

            if (!File.Exists(path) && optional)
                return null;

            using (var stream = File.OpenRead(path)) {
                result = new FStringTable(Path.GetFileName(path), stream);
                result.MissingString += ForwardOnMissingString;
            }

            lock (Cache) {
                if (Cache.TryGetValue(path, out var lostARace))
                    return lostARace;

                Cache[path] = result;
                return result;
            }
        }
    }
}
