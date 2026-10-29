using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Afterimage
{
    internal static class AssistantTest
    {
        internal static int Run(string folder)
        {
            folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder); int result = 1;
            using var driver = new Form { ShowInTaskbar = false, Opacity = 0 };
            driver.Shown += async (_, _) => {
                var checks = new List<string>();
                try {
                    await BackendAsync(checks);
                    string pipeName = "Afterimage-test-" + Guid.NewGuid().ToString("N"); string received = "";
                    using (var pipe = new InstanceChannel(value => received = value, pipeName)) {
                        Check(await InstanceChannel.SendAsync("show", pipeName) && received == "show", "Second launch did not reach the running instance.");
                        Check(await InstanceChannel.SendAsync("quit", pipeName) && received == "quit", "Graceful quit was not delivered.");
                    }
                    checks.Add("single-instance show and graceful quit");
                    string protectedKey = Secret.Protect("test-only-secret"); Check(!protectedKey.Contains("test-only-secret") && Secret.Unprotect(protectedKey) == "test-only-secret", "API key protection failed."); checks.Add("Windows DPAPI round trip");
                    var store = new WorkspaceStore(Path.Combine(folder, "profile-" + Guid.NewGuid().ToString("N")));
                    await Task.WhenAll(store.SaveAsync("ordered.json", "{\"value\":1}"), store.SaveAsync("ordered.json", "{\"value\":2}"));
                    Check(File.ReadAllText(Path.Combine(store.Folder, "ordered.json")).Contains(":2"), "Writes completed out of order."); checks.Add("atomic ordered storage");
                    string cues = Path.Combine(folder, "clip.annotations.json");
                    File.WriteAllText(cues, Json.Encode(new { video = "clip.mp4", annotations = Enumerable.Range(0, 200).Select(i => new { at_ms = i * 1000, text = i == 0 ? "A user-written marker" : "App: test" }).ToArray() }));
                    string context = WorkspaceStore.ReadAnnotations(cues); Check(context.Contains("A user-written marker") && context.Contains("first 180 of 200") && context.Length < 19000, "Annotation attachment is not bounded or loses labels."); checks.Add("bounded, explicit annotation context");
                    var handler = new FakeHandler { Payload = Ollama("A shorter workflow: café → record → review.") };
                    using var http = new HttpClient(handler); var client = new AssistantClient(http);
                    using var form = new CompanionForm(() => folder, _ => true, store, client);
                    form.Show(); await form.TestLoadAsync(); await Task.Delay(100); Check(form.ClientSize.Width >= 430 * form.DeviceDpi / 96f, "The companion window failed DPI scaling."); CheckLayout(form); Snapshot(form, folder, "chat-empty");
                    form.TestConfigure(new AiConnection { Model = "fixture", Provider = "ollama" }); form.TestInput("Help me make this workflow faster.");
                    int ticks = 0; using var heartbeat = new System.Windows.Forms.Timer { Interval = 15 }; heartbeat.Tick += (_, _) => ticks++; heartbeat.Start();
                    await form.SendAsync(); heartbeat.Stop();
                    Check(ticks >= 5 && !form.IsStreaming && form.ConversationText.Contains("café → record"), "Streaming blocked the interface or lost Unicode.");
                    Snapshot(form, folder, "chat-reply"); checks.Add("streamed UI remains responsive (" + ticks + " timer ticks)");
                    handler.Hang = true; form.TestInput("A request to cancel"); Task pending = form.SendAsync(); await Task.Delay(150);
                    FindButton(form, "Stop").PerformClick(); await pending;
                    Check(!form.IsStreaming, "The Stop button left the AI request active."); handler.Hang = false; checks.Add("native Stop button cancels AI independently");
                    form.OpenPage("notes"); form.TestNotes("Workflow ideas\n\nCapture one task at a time.\nMark repeated steps.\nReview the slow parts before automating."); await Task.Delay(700); Snapshot(form, folder, "notes");
                    using (var config = new ConnectionForm(new AiConnection(), client)) { config.Show(); await Task.Delay(100); CheckLayout(config); Snapshot(config, folder, "connection"); config.Close(); }
                    await form.ShutdownAsync(); var restored = await store.LoadAsync<WorkspaceData>("workspace.json");
                    Check(restored.Notes.StartsWith("Workflow ideas") && restored.Messages.Count == 4 && restored.Messages[1].Complete && !restored.Messages[3].Complete && restored.Draft == "A request to cancel", "Notes, interrupted replies or drafts were not restored."); checks.Add("notes, chat and cancelled draft persist across closing");
                    string blocked = Path.Combine(folder, "blocked-store"); File.WriteAllText(blocked, "test fixture");
                    using (var unsaved = new CompanionForm(() => folder, _ => false, new WorkspaceStore(blocked), client)) {
                        unsaved.Show(); await unsaved.TestLoadAsync(); await Task.Delay(100); unsaved.TestNotes("Must remain available");
                        Check(!await unsaved.ShutdownAsync() && !unsaved.IsDisposed, "A failed save discarded the workspace on exit.");
                    }
                    checks.Add("failed saves keep the workspace open");
                    File.WriteAllText(Path.Combine(folder, "assistant-result.json"), Json.Encode(new { passed = true, checks })); result = 0;
                } catch (Exception e) { File.WriteAllText(Path.Combine(folder, "assistant-result.json"), Json.Encode(new { passed = false, checks, error = e.ToString() })); }
                finally { driver.Close(); }
            };
            Application.Run(driver); return result;
        }
        private static async Task BackendAsync(List<string> checks)
        {
            var handler = new FakeHandler(); using var http = new HttpClient(handler); var client = new AssistantClient(http); var settings = new AiConnection { Model = "fixture" };
            var history = new[] { new ChatMessage { Content = "Hello" } }; var output = new StringBuilder();
            handler.Payload = Ollama("Héllo 🌙"); await client.StreamAsync(settings, history, text => output.Append(text), CancellationToken.None);
            Check(output.ToString() == "Héllo 🌙", "Ollama Unicode stream failed."); Check(handler.Url.AbsolutePath == "/api/chat" && handler.Authorization == null, "Local request endpoint or authentication is wrong.");
            using (var sent = JsonDocument.Parse(handler.Body)) { Check(sent.RootElement.GetProperty("messages").GetArrayLength() == 2 && !handler.Body.Contains("annotations attached"), "Unexpected context sent to AI."); }
            checks.Add("Ollama NDJSON, fragmented UTF-8 and no implicit context");
            output.Clear(); settings.Provider = "compatible"; settings.Endpoint = "https://example.test/v1";
            handler.Payload = ": keepalive\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"One\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\" two\"}}]}\n\ndata: [DONE]\n\n";
            await client.StreamAsync(settings, history, text => output.Append(text), CancellationToken.None); Check(output.ToString() == "One two" && handler.Url.AbsolutePath == "/v1/chat/completions", "Compatible SSE failed."); checks.Add("compatible API streaming and heartbeat frames");
            output.Clear(); settings.Provider = "openai";
            handler.Payload = "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"Works\"}\n\nevent: response.completed\ndata: {\"type\":\"response.completed\"}\n\n";
            await client.StreamAsync(settings, history, text => output.Append(text), CancellationToken.None);
            Check(output.ToString() == "Works" && handler.Url.AbsolutePath == "/v1/responses" && handler.Body.Contains("\"store\":false"), "Responses API payload or stream failed."); checks.Add("OpenAI Responses stream with storage disabled");
            handler.Payload = "data: {\"type\":\"response.failed\"}\n\n"; await MustFail(() => client.StreamAsync(settings, history, _ => { }, CancellationToken.None));
            settings.Provider = "ollama"; settings.Endpoint = "http://localhost:11434"; handler.Payload = "{\"message\":{\"content\":\"partial\"}}\n";
            await MustFail(() => client.StreamAsync(settings, history, _ => { }, CancellationToken.None)); checks.Add("failed and truncated streams surface errors");
            handler.Status = HttpStatusCode.Unauthorized; handler.Payload = "not read"; await MustFail(() => client.StreamAsync(settings, history, _ => { }, CancellationToken.None)); handler.Status = HttpStatusCode.OK; checks.Add("HTTP errors do not expose response bodies");
            handler.Hang = true; using (var cancel = new CancellationTokenSource(80)) { var clock = Stopwatch.StartNew(); await MustFail(() => client.StreamAsync(settings, history, _ => { }, cancel.Token)); Check(clock.ElapsedMilliseconds < 1000, "Cancellation took too long."); } handler.Hang = false; checks.Add("cancel an unresponsive server");
            handler.Payload = "{\"models\":[{\"name\":\"z\"},{\"name\":\"a\"}]}";
            var models = await client.ModelsAsync(settings, CancellationToken.None); Check(models.SequenceEqual(new[] { "a", "z" }), "Model discovery failed."); checks.Add("model discovery");
            foreach (string address in new[] { "http://remote.test", "https://user:secret@example.test", "https://example.test/?key=secret" }) { bool rejected = false; try { new AiConnection { Provider = "compatible", Endpoint = address }.BaseUri(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Unsafe endpoint accepted."); }
            checks.Add("remote HTTPS and endpoint credential checks");
        }
        private static string Ollama(string text) => Json.Encode(new { message = new { content = text }, done = false }) + "\n{\"done\":true}\n";
        private static async Task MustFail(Func<Task> action) { bool failed = false; try { await action(); } catch (Exception) { failed = true; } Check(failed, "An invalid response was treated as success."); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Snapshot(Form form, string folder, string name) { using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
        private static Button FindButton(Control parent, string text)
        {
            foreach (Control child in parent.Controls) { if (child is Button button && button.Text == text) return button; var found = FindButton(child, text); if (found != null) return found; } return null;
        }
        private static void CheckLayout(Control parent)
        {
            foreach (Control child in parent.Controls) {
                if (!child.Visible) continue;
                Check(child.Right <= parent.ClientSize.Width + 2 && child.Bottom <= parent.ClientSize.Height + 2, "A control exceeds its parent: " + child.GetType().Name + " " + child.Text);
                if (child is Button b) {
                    Check(child.Right <= parent.ClientSize.Width + 2 && child.Bottom <= parent.ClientSize.Height + 2, "A button is clipped: " + b.Text);
                    int needed = TextRenderer.MeasureText(b.Text, b.Font).Width;
                    Check(b.Width >= needed, "A button label is clipped: " + b.Text);
                }
                CheckLayout(child);
            }
        }
        private sealed class FakeHandler : HttpMessageHandler
        {
            internal string Payload = "", Body = "", Authorization; internal Uri Url; internal bool Hang; internal HttpStatusCode Status = HttpStatusCode.OK;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Url = request.RequestUri; Authorization = request.Headers.Authorization?.ToString(); Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
                if (Hang) await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(Status) { Content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(Payload))) };
            }
        }
        private sealed class FragmentedStream : MemoryStream
        {
            internal FragmentedStream(byte[] bytes) : base(bytes) { }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(5, token); return await base.ReadAsync(buffer.Slice(0, Math.Min(7, buffer.Length)), token); }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { await Task.Delay(5, token); return base.Read(buffer, offset, Math.Min(7, count)); }
        }
    }
}
