using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Afterimage
{
    internal static class Night
    {
        internal static readonly Color Paper = Color.FromArgb(23, 26, 34), Panel = Color.FromArgb(32, 36, 47), Ink = Color.FromArgb(232, 237, 247), Muted = Color.FromArgb(166, 177, 197), Line = Color.FromArgb(58, 65, 81);
        internal static Label Label(string text, float size = 9, bool bold = false) => new Label { Text = text, AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0) };
        internal static Button Button(string text, int width = 80, bool primary = false)
        {
            var b = Theme.Button(text, width, primary); b.Height = 34; b.BackColor = primary ? Brand.Frost : Panel; b.ForeColor = primary ? Paper : Ink; b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = primary ? Color.White : Line; b.Margin = new Padding(0, 0, 8, 0); return b;
        }
        internal static void Chrome(Form form)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            int dark = 1; DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { int round = 2; DwmSetWindowAttribute(form.Handle, 33, ref round, 4); }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
    internal sealed class CompanionForm : Form
    {
        private readonly WorkspaceStore store;
        private readonly AssistantClient client;
        private WorkspaceData data = new WorkspaceData();
        private AiConnection connection = new AiConnection();
        private readonly RichTextBox transcript = new RichTextBox(), notes = new RichTextBox();
        private readonly TextBox input = new TextBox();
        private readonly Button send, attach, configure, clear, chatTab, notesTab;
        private readonly Label status, model, noteStatus;
        private readonly Panel chatPage = new Panel(), notesPage = new Panel();
        private readonly System.Windows.Forms.Timer flush = new System.Windows.Forms.Timer { Interval = 40 }, autosave = new System.Windows.Forms.Timer { Interval = 500 };
        private readonly ConcurrentQueue<string> chunks = new ConcurrentQueue<string>();
        private readonly Func<string, bool> addMarker;
        private readonly Func<string> recordings;
        private CancellationTokenSource request;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool loaded, loading, saving, allowClose;
        private string attachment = "";
        private ChatMessage reply;
        private Task networkRequest;
        private readonly Stopwatch responseClock = new Stopwatch();
        internal bool IsStreaming => request != null;
        internal event Action ActivityChanged;
        internal CompanionForm(Func<string> recordings, Func<string, bool> addMarker, WorkspaceStore store = null, AssistantClient client = null)
        {
            SuspendLayout();
            this.store = store ?? new WorkspaceStore(); this.client = client ?? new AssistantClient(); this.recordings = recordings; this.addMarker = addMarker;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Afterimage"; BackColor = Night.Paper; ForeColor = Night.Ink; Font = new Font("Segoe UI", 10);
            ClientSize = new Size(440, 600); MinimumSize = new Size(400, 520); StartPosition = FormStartPosition.CenterScreen; Icon = Brand.Icon();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 18), ColumnCount = 1, RowCount = 4 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); Controls.Add(root);
            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Size = new Size(400, 54) };
            var logo = new Panel { Bounds = new Rectangle(0, 1, 30, 30) }; logo.Paint += (_, e) => Brand.Draw(e.Graphics, logo.ClientRectangle); header.Controls.Add(logo);
            var title = Night.Label("Afterimage", 12, true); title.Location = new Point(40, 0); header.Controls.Add(title);
            model = Night.Label("Local AI · your workspace", 8); model.ForeColor = Night.Muted; model.AutoSize = false; model.AutoEllipsis = true; model.SetBounds(41, 25, 205, 22); header.Controls.Add(model);
            configure = Night.Button("Connect", 82); configure.Anchor = AnchorStyles.Top | AnchorStyles.Right; configure.Location = new Point(318, 0); configure.Click += async (_, _) => await ConfigureAsync(); header.Controls.Add(configure); root.Controls.Add(header, 0, 0);
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            chatTab = Night.Button("Chat", 72, true); notesTab = Night.Button("Notes", 72); chatTab.Click += (_, _) => SelectPage("chat"); notesTab.Click += (_, _) => SelectPage("notes"); tabs.Controls.AddRange(new Control[] { chatTab, notesTab });
            clear = Night.Button("New chat", 90); clear.Click += async (_, _) => { if (IsStreaming) return; data.Messages.Clear(); attachment = ""; attach.Text = "Attach"; RenderHistory(); await SaveAsync(); }; tabs.Controls.Add(clear);
            var export = Night.Button("Export", 74); export.Click += async (_, _) => await ExportAsync(); tabs.Controls.Add(export); root.Controls.Add(tabs, 0, 1);
            var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; root.Controls.Add(pages, 0, 2); pages.Controls.Add(chatPage); pages.Controls.Add(notesPage); chatPage.Dock = notesPage.Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); chatPage.Controls.Add(layout);
            SetupEditor(transcript); transcript.ReadOnly = true; transcript.DetectUrls = false; transcript.AccessibleName = "Conversation"; transcript.BackColor = Night.Paper; transcript.Margin = new Padding(0, 10, 0, 10); layout.Controls.Add(transcript, 0, 0);
            status = Night.Label("Loading…", 8); status.ForeColor = Night.Muted; status.AutoSize = false; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AccessibleName = "AI status"; layout.Controls.Add(status, 0, 1);
            input.Multiline = true; input.AcceptsReturn = true; input.ScrollBars = ScrollBars.Vertical; input.MaxLength = 16000; input.Dock = DockStyle.Fill; input.BackColor = Night.Panel; input.ForeColor = Night.Ink; input.BorderStyle = BorderStyle.FixedSingle; input.PlaceholderText = "Ask anything…"; input.AccessibleName = "Message"; input.Margin = new Padding(0, 0, 0, 8); layout.Controls.Add(input, 0, 2);
            input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; if (!IsStreaming) await SendAsync(); } };
            input.TextChanged += (_, _) => ScheduleSave();
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) }; actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            attach = Night.Button("Attach", 150); attach.TextAlign = ContentAlignment.MiddleLeft; attach.Click += async (_, _) => await AttachAsync(); actions.Controls.Add(attach, 0, 0);
            send = Night.Button("Send", 84, true); send.Margin = Padding.Empty; send.Click += async (_, _) => { if (IsStreaming) { request.Cancel(); status.Text = "Stopping…"; } else await SendAsync(); }; actions.Controls.Add(send, 1, 0); layout.Controls.Add(actions, 0, 3);
            var noteLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty }; noteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); noteLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); noteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); notesPage.Controls.Add(noteLayout);
            SetupEditor(notes); notes.MaxLength = 120000; notes.AccessibleName = "Notes"; notes.Margin = new Padding(0, 10, 0, 10); notes.TextChanged += (_, _) => { if (loaded) { noteStatus.Text = "Saving…"; ScheduleSave(); } }; noteLayout.Controls.Add(notes, 0, 0);
            var noteActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var useInChat = Night.Button("Use in chat", 108); useInChat.Click += (_, _) => { string text = notes.SelectedText.Length > 0 ? notes.SelectedText : notes.Text; input.Text = text.Substring(0, Math.Min(16000, text.Length)); SelectPage("chat"); input.Focus(); }; noteActions.Controls.Add(useInChat);
            var mark = Night.Button("Mark selection", 130); mark.Click += (_, _) => { string text = notes.SelectedText.Trim(); if (text.Length == 0) { noteStatus.Text = "Select a note first"; return; } noteStatus.Text = addMarker(text.Substring(0, Math.Min(240, text.Length))) ? "Marked" : "Start recording first"; }; noteActions.Controls.Add(mark);
            noteStatus = Night.Label("Saved on this PC", 8); noteStatus.ForeColor = Night.Muted; noteStatus.Margin = new Padding(2, 9, 0, 0); noteActions.Controls.Add(noteStatus); noteLayout.Controls.Add(noteActions, 0, 1);
            var privacy = Night.Label("Text stays here until you send it to the selected AI.", 8); privacy.ForeColor = Night.Muted; privacy.Dock = DockStyle.Fill; privacy.TextAlign = ContentAlignment.BottomLeft; root.Controls.Add(privacy, 0, 3);
            flush.Tick += (_, _) => FlushChunks(); autosave.Tick += async (_, _) => { autosave.Stop(); await SaveAsync(); };
            SelectPage("chat"); SetReady(false); Shown += async (_, _) => await LoadAsync(); ResumeLayout(true);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Night.Chrome(this); }
        private static void SetupEditor(RichTextBox editor) { editor.Dock = DockStyle.Fill; editor.BorderStyle = BorderStyle.None; editor.BackColor = Night.Panel; editor.ForeColor = Night.Ink; editor.Font = new Font("Segoe UI", 10); editor.ScrollBars = RichTextBoxScrollBars.Vertical; editor.WordWrap = true; }
        internal void OpenPage(string page) { SelectPage(page); Show(); WindowState = FormWindowState.Normal; Activate(); (page == "notes" ? (Control)notes : input).Focus(); }
        private void SelectPage(string page)
        {
            bool chat = page != "notes"; chatPage.Visible = chat; notesPage.Visible = !chat;
            chatTab.BackColor = chat ? Brand.Frost : Night.Panel; chatTab.ForeColor = chat ? Night.Paper : Night.Ink;
            notesTab.BackColor = chat ? Night.Panel : Brand.Frost; notesTab.ForeColor = chat ? Night.Ink : Night.Paper; clear.Visible = chat;
        }
        private async Task LoadAsync()
        {
            if (loaded || loading) return; loading = true;
            try {
                data = await store.LoadAsync<WorkspaceData>("workspace.json"); connection = await store.LoadAsync<AiConnection>("ai-connection.json");
                if (IsDisposed) return;
                data.Notes ??= ""; data.Draft ??= ""; data.Messages ??= new();
                data.Messages = data.Messages.Where(m => m != null && (m.Role == "user" || m.Role == "assistant") && m.Content != null).TakeLast(100).ToList();
                foreach (var message in data.Messages) message.Attachment ??= "";
                notes.Text = data.Notes; input.Text = data.Draft; RenderHistory(); loaded = true; UpdateConnection(); SetReady(true);
            } catch (Exception) { status.Text = "Could not read the workspace. Your files have been preserved."; SetReady(false); }
            finally { loading = false; }
        }
        private void SetReady(bool ready) { send.Enabled = attach.Enabled = configure.Enabled = clear.Enabled = input.Enabled = notes.Enabled = ready; }
        private void UpdateConnection() { model.Text = connection.Model.Length > 0 ? connection.Label + " · " + connection.Model : "Local AI · choose a model"; configure.Text = "Connect"; status.Text = connection.Model.Length == 0 ? "Connect to a model to begin" : "Ready · " + connection.Label; }
        private async Task ConfigureAsync()
        {
            if (IsStreaming) return;
            using var form = new ConnectionForm(connection, client);
            if (form.ShowDialog(this) != DialogResult.OK) return;
            try { await store.SaveAsync("ai-connection.json", Json.Encode(form.Result)); if (IsDisposed) return; connection = form.Result; UpdateConnection(); }
            catch (Exception) { status.Text = "Connection could not be saved. Try again."; }
        }
        internal async Task SendAsync()
        {
            string text = input.Text.Trim(); if (!loaded || IsStreaming || text.Length == 0) return;
            if (connection.Model.Length == 0) { await ConfigureAsync(); if (connection.Model.Length == 0) return; }
            if (data.Messages.Count >= 98) { status.Text = "Export this conversation, then start a new chat."; return; }
            if (data.Messages.Count == 0) transcript.Clear();
            var user = new ChatMessage { Role = "user", Content = text, Attachment = attachment }; data.Messages.Add(user); AppendMessage(user);
            input.Clear(); attachment = ""; attach.Text = "Attach";
            var history = data.Messages.ToArray(); reply = new ChatMessage { Role = "assistant", Complete = false }; data.Messages.Add(reply); AppendHeading("Afterimage");
            request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); responseClock.Restart(); ActivityChanged?.Invoke(); send.Text = "Stop"; configure.Enabled = attach.Enabled = clear.Enabled = false; status.Text = "Connecting · " + connection.Label; flush.Start();
            await SaveAsync();
            try {
                networkRequest = client.StreamAsync(connection, history, chunk => chunks.Enqueue(chunk), request.Token);
                await networkRequest;
                if (IsDisposed) return; FlushChunks(); reply.Complete = true; status.Text = "Ready · " + connection.Label;
            } catch (OperationCanceledException) { if (!IsDisposed) { FlushChunks(); status.Text = request.IsCancellationRequested ? "Stopped" : "Request timed out. Try again."; } }
            catch (Exception e) { if (!IsDisposed) { FlushChunks(); status.Text = e is HttpRequestException ? "Connection failed. Check the server, model and API key." : e.Message; } }
            finally {
                var pending = request; request = null; pending?.Dispose();
                networkRequest = null;
                if (!reply.Complete) user.Complete = false;
                if (!IsDisposed) {
                    if (!reply.Complete && input.Text.Length == 0) { input.Text = user.Content; attachment = user.Attachment; attach.Text = attachment.Length > 0 ? "Annotations ×" : "Attach"; }
                    flush.Stop(); send.Text = "Send"; configure.Enabled = attach.Enabled = clear.Enabled = true; transcript.AppendText("\n\n"); ActivityChanged?.Invoke(); await SaveAsync();
                }
            }
        }
        private void FlushChunks()
        {
            var text = new StringBuilder(); while (chunks.TryDequeue(out var chunk)) text.Append(chunk);
            if (reply == null) return;
            if (text.Length == 0) {
                if (IsStreaming && reply.Content.Length == 0 && responseClock.Elapsed.TotalSeconds >= 1) status.Text = "Waiting · " + connection.Label + " · " + (int)responseClock.Elapsed.TotalSeconds + "s";
                return;
            }
            string value = text.ToString(); reply.Content += value;
            transcript.SelectionStart = transcript.TextLength; transcript.SelectionColor = Night.Ink; transcript.AppendText(value); transcript.ScrollToCaret(); status.Text = "Writing · " + connection.Label;
        }
        private void AppendHeading(string text)
        {
            transcript.SelectionStart = transcript.TextLength; transcript.SelectionColor = Night.Muted; transcript.AppendText(text + "\n"); transcript.SelectionColor = Night.Ink;
        }
        private void AppendMessage(ChatMessage message) { AppendHeading(message.Role == "user" ? "You" : "Afterimage" + (message.Complete ? "" : " · interrupted")); transcript.AppendText(message.Content + (message.Attachment.Length > 0 ? "\n[Recording annotations attached]" : "") + "\n\n"); }
        private void RenderHistory()
        {
            transcript.Clear();
            foreach (var message in data.Messages) AppendMessage(message);
        }
        private void ScheduleSave() { if (!loaded) return; autosave.Stop(); autosave.Start(); }
        private async Task<bool> SaveAsync()
        {
            if (!loaded || IsDisposed) return true;
            data.Notes = notes.Text; data.Draft = input.Text; string json = Json.Encode(data);
            try { saving = true; await store.SaveAsync("workspace.json", json); if (!IsDisposed) noteStatus.Text = "Saved on this PC"; return true; }
            catch (Exception) { if (!IsDisposed) { noteStatus.Text = "Could not save"; status.Text = "Workspace could not be saved. Export a copy."; } return false; }
            finally { saving = false; }
        }
        private async Task AttachAsync()
        {
            if (attachment.Length > 0) { attachment = ""; attach.Text = "Attach"; return; }
            using var picker = new OpenFileDialog { Title = "Attach recording annotations", Filter = "Afterimage annotations|*.annotations.json", InitialDirectory = recordings(), CheckFileExists = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try {
                string text = await Task.Run(() => WorkspaceStore.ReadAnnotations(picker.FileName)); if (IsDisposed) return;
                using var preview = new AttachmentForm(text, connection.Label);
                if (preview.ShowDialog(this) != DialogResult.OK) return; attachment = text; attach.Text = "Annotations ×"; status.Text = "Attached · sent with your next message";
            } catch (Exception e) { status.Text = e.Message; }
        }
        private async Task ExportAsync()
        {
            using var picker = new SaveFileDialog { FileName = notesPage.Visible ? "Afterimage-notes.txt" : "Afterimage-chat.txt", Filter = "Text file|*.txt" };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            string text = notesPage.Visible ? notes.Text : string.Join("\n\n", data.Messages.Select(m => (m.Role == "user" ? "You" : "Afterimage") + (m.Complete ? "" : " [interrupted]") + "\n" + m.Content + (m.Attachment.Length > 0 ? "\n\n" + m.Attachment : "")));
            try { await File.WriteAllTextAsync(picker.FileName, text, new UTF8Encoding(false)); if (!IsDisposed) status.Text = "Exported"; } catch (Exception) { if (!IsDisposed) status.Text = "Could not export to that file."; }
        }
        internal async Task<bool> ShutdownAsync()
        {
            allowClose = true; autosave.Stop(); request?.Cancel();
            if (networkRequest != null) { try { await networkRequest; } catch (Exception) { } }
            if (!IsDisposed) {
                FlushChunks();
                if (!await SaveAsync()) { allowClose = false; Show(); Activate(); return false; }
                Close();
            }
            return true;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); autosave.Stop(); _ = SaveAsync(); return; }
            lifetime.Cancel(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); request?.Cancel(); flush.Dispose(); autosave.Dispose(); Icon?.Dispose(); } base.Dispose(disposing); }
        internal async Task TestLoadAsync() { await LoadAsync(); }
        internal void TestConfigure(AiConnection value) { connection = value; UpdateConnection(); }
        internal void TestInput(string text) { input.Text = text; }
        internal void TestNotes(string text) { notes.Text = text; }
        internal string ConversationText => transcript.Text;
        internal bool SavePending => saving;
    }
    internal sealed class AttachmentForm : Form
    {
        internal AttachmentForm(string text, string destination)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Text = "Recording context"; BackColor = Night.Paper; ForeColor = Night.Ink; ClientSize = new Size(480, 470); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(root);
            var label = Night.Label("This text will be sent to " + destination + " with your message."); label.AutoSize = false; label.Dock = DockStyle.Fill; root.Controls.Add(label);
            var content = new RichTextBox { Text = text, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Night.Panel, ForeColor = Night.Ink, BorderStyle = BorderStyle.None, Font = Font, DetectUrls = false }; root.Controls.Add(content);
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) }; var use = Night.Button("Attach", 90, true); use.DialogResult = DialogResult.OK; var cancel = Night.Button("Cancel", 90); cancel.DialogResult = DialogResult.Cancel; row.Controls.AddRange(new Control[] { use, cancel }); root.Controls.Add(row); AcceptButton = use; CancelButton = cancel; ResumeLayout(true);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Night.Chrome(this); }
    }
}
