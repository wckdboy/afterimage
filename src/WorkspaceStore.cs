using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Afterimage
{
    internal sealed class WorkspaceStore
    {
        internal readonly string Folder;
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1, 1);
        internal WorkspaceStore(string folder = null) { Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Afterimage"); }
        internal async Task<T> LoadAsync<T>(string name) where T : new()
        {
            string path = Path.Combine(Folder, name);
            if (!File.Exists(path)) return new T();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            string json = await AssistantClient.ReadBoundedAsync(stream, 4 * 1024 * 1024, CancellationToken.None).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }
        internal async Task SaveAsync(string name, string json)
        {
            await writes.WaitAsync().ConfigureAwait(false);
            try {
                Directory.CreateDirectory(Folder); string path = Path.Combine(Folder, name), temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false)).ConfigureAwait(false);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { writes.Release(); }
        }
        internal static string ReadAnnotations(string path)
        {
            if (!path.EndsWith(".annotations.json", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an Afterimage annotation file.");
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("This annotation file is too large. Choose a shorter clip.");
            using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement;
            var text = new StringBuilder(); text.AppendLine("Clip: " + Path.GetFileName(root.GetProperty("video").GetString()));
            text.AppendLine("Observed events (text only):"); int included = 0, total = root.GetProperty("annotations").GetArrayLength();
            foreach (var cue in root.GetProperty("annotations").EnumerateArray()) {
                string label = cue.GetProperty("text").GetString() ?? ""; if (label.Length > 600) label = label.Substring(0, 600) + "…";
                string row = SessionLibrary.Duration(cue.GetProperty("at_ms").GetDouble() / 1000, true) + "  " + label;
                if (included >= 180 || text.Length + row.Length > 18000) break;
                text.AppendLine(row); included++;
            }
            if (included < total) text.AppendLine("Showing the first " + included + " of " + total + " events.");
            return text.ToString();
        }
    }
}
