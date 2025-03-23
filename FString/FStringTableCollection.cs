using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.FString {
    public class FStringTableCollection {
        protected readonly Dictionary<(string folder, string name), FStringTable> Cache = 
            new Dictionary<(string folder, string name), FStringTable>();

        public string Language = CultureInfo.CurrentCulture.Name;
        public event OnMissingString MissingString;

        private OnMissingString ForwardOnMissingString;

        public FStringTableCollection () {
            ForwardOnMissingString = (table, key) => {
                if (MissingString != null)
                    MissingString(table, key);
            };
        }

        private string BuildPath ((string folder, string name) key) =>
            BuildPath(key.folder, key.name, Language);

        private string BuildPath (string folder, string name, string language) =>
            Path.Combine(folder, $"{name}_{language}.xml");

        private void Reload ((string folder, string name) key, FStringTable table) {
            // HACK
            if (Language == "missing") {
                table.Clear();
                return;
            }

            var path = BuildPath(key);
            if (!File.Exists(path)) {
                System.Diagnostics.Debug.WriteLine($"String table missing during reload: '{path}'");
                return;
            }

            using (var stream = File.OpenRead(path)) {
                table.Path = path;
                table.Clear();
                table.PopulateFromXmlStream(stream, true);
                System.Diagnostics.Debug.WriteLine($"Reloaded string table '{path}'");
            }
        }

        public void ReloadAll () {
            lock (Cache) {
                foreach (var kvp in Cache)
                    Reload(kvp.Key, kvp.Value);
            }
        }

        public void ClearCache () {
            lock (Cache)
                Cache.Clear();
        }

        public FStringTable LoadFromPath (string folder, string name, bool optional, bool forceReload = false) {
            FStringTable result;
            var key = (folder, name);
            lock (Cache)
                if (Cache.TryGetValue(key, out result)) {
                    if (forceReload)
                        Reload(key, result);
                    return result;
                }

            var path = BuildPath(key);
            if (!File.Exists(path) && optional)
                return null;

            using (var stream = File.OpenRead(path)) {
                result = new FStringTable(name, stream) {
                    Path = path,
                };
                result.MissingString += ForwardOnMissingString;
            }

            lock (Cache) {
                if (Cache.TryGetValue(key, out var lostARace)) {
                    if (forceReload)
                        Reload(key, lostARace);
                    return lostARace;
                }

                Cache[key] = result;
                return result;
            }
        }
    }
}
