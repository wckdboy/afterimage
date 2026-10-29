using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Afterimage
{
    internal sealed class ConnectionForm : Form
    {
        private readonly ComboBox provider = new ComboBox(), model = new ComboBox();
        private readonly TextBox endpoint = new TextBox(), key = new TextBox();
        private readonly Label status;
        private readonly Button refresh, save;
        private readonly AssistantClient client;
        private readonly AiConnection original;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool changedProvider, keyEdited, loading;
        internal AiConnection Result { get; private set; }
        internal ConnectionForm(AiConnection current, AssistantClient client)
        {
            SuspendLayout();
            original = current; this.client = client;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Text = "AI connection"; Font = new Font("Segoe UI", 10); BackColor = Night.Paper; ForeColor = Night.Ink; ClientSize = new Size(420, 425); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            provider.DropDownStyle = ComboBoxStyle.DropDownList; provider.Items.AddRange(new object[] { "Local · Ollama", "OpenAI", "Compatible API" }); provider.SelectedIndex = current.Provider == "openai" ? 1 : current.Provider == "compatible" ? 2 : 0;
            endpoint.Text = current.Endpoint; key.UseSystemPasswordChar = true; key.PlaceholderText = current.ProtectedKey.Length > 0 ? "Saved securely · leave blank to keep" : "Optional API key"; key.MaxLength = 2048;
            model.DropDownStyle = ComboBoxStyle.DropDown; model.Text = current.Model; model.AccessibleName = "Model";
            AddField("Connection", provider, 18); AddField("Base URL", endpoint, 84); AddField("API key", key, 150); AddField("Model", model, 216);
            refresh = Night.Button("Find models", 116); refresh.SetBounds(24, 288, 116, 34); refresh.Click += async (_, _) => await FindModelsAsync(); Controls.Add(refresh);
            status = Night.Label("Ollama runs on your PC. Start it before connecting.", 8); status.AutoSize = false; status.SetBounds(24, 330, 372, 38); status.ForeColor = Night.Muted; Controls.Add(status);
            var cancel = Night.Button("Cancel", 88); cancel.SetBounds(208, 374, 88, 34); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
            save = Night.Button("Save", 92, true); save.SetBounds(304, 374, 92, 34); save.Click += (_, _) => { try { Result = Read(); if (Result.Model.Length == 0) throw new ArgumentException("Choose a model, or type its name."); DialogResult = DialogResult.OK; } catch (Exception e) { status.Text = e.Message; } }; Controls.Add(save); AcceptButton = save;
            provider.SelectedIndexChanged += (_, _) => {
                changedProvider = true; keyEdited = false; key.Clear(); key.PlaceholderText = "Optional API key"; model.Items.Clear(); model.Text = "";
                endpoint.Text = provider.SelectedIndex == 0 ? "http://localhost:11434" : provider.SelectedIndex == 1 ? "https://api.openai.com/v1" : "https://";
                UpdateFields();
            };
            key.TextChanged += (_, _) => keyEdited = true; UpdateFields(); ResumeLayout(true);
        }
        private void AddField(string text, Control control, int y)
        {
            var label = Night.Label(text, 9); label.Location = new Point(24, y); Controls.Add(label); control.SetBounds(24, y + 25, 372, 28); control.BackColor = Night.Panel; control.ForeColor = Night.Ink; control.AccessibleName = text; Controls.Add(control);
        }
        private void UpdateFields()
        {
            endpoint.Enabled = provider.SelectedIndex != 1; key.Enabled = provider.SelectedIndex != 0;
            status.Text = provider.SelectedIndex == 0 ? "Ollama runs on your PC. Start it before connecting." : "Only messages you send go to this API. Keys are protected by Windows.";
        }
        private AiConnection Read()
        {
            var value = new AiConnection { Provider = provider.SelectedIndex == 0 ? "ollama" : provider.SelectedIndex == 1 ? "openai" : "compatible", Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim() };
            value.BaseUri();
            // Never transfer a saved key to a different provider or endpoint.
            bool same = !changedProvider && value.Provider == original.Provider && value.BaseUri() == original.BaseUri();
            value.ProtectedKey = key.Enabled ? (keyEdited ? Secret.Protect(key.Text.Trim()) : same ? original.ProtectedKey : "") : "";
            if (value.Provider == "openai" && value.ProtectedKey.Length == 0) throw new ArgumentException("Enter your OpenAI API key.");
            return value;
        }
        private async Task FindModelsAsync()
        {
            if (loading) return;
            try {
                var config = Read(); loading = true; refresh.Enabled = provider.Enabled = endpoint.Enabled = key.Enabled = save.Enabled = false; status.Text = "Looking for models…";
                var names = await client.ModelsAsync(config, lifetime.Token); if (IsDisposed) return;
                string previous = model.Text; model.Items.Clear(); model.Items.AddRange(names); model.Text = previous.Length > 0 ? previous : names.Length > 0 ? names[0] : "";
                status.Text = names.Length == 0 ? "No models found. Install one in Ollama, or type a model name." : names.Length + " models available";
            } catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Connection timed out. Check that the server is running."; }
            catch (Exception) { if (!IsDisposed) status.Text = "Could not connect. Check the address, server and API key."; }
            finally { loading = false; if (!IsDisposed) { refresh.Enabled = provider.Enabled = save.Enabled = true; endpoint.Enabled = provider.SelectedIndex != 1; key.Enabled = provider.SelectedIndex != 0; } }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Night.Chrome(this); }
        protected override void OnFormClosed(FormClosedEventArgs e) { lifetime.Cancel(); base.OnFormClosed(e); }
    }
}
