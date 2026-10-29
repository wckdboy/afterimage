using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Afterimage
{
    internal sealed class InstanceChannel : IDisposable
    {
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Task server;
        private static string Name => "Afterimage-" + WindowsIdentity.GetCurrent().User.Value;
        internal InstanceChannel(Action<string> receive, string name = null) { server = ListenAsync(name ?? Name, receive); }
        private async Task ListenAsync(string name, Action<string> receive)
        {
            while (!stop.IsCancellationRequested) {
                try {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(2000);
                    byte[] command = new byte[1]; int count = await pipe.ReadAsync(command, timeout.Token).ConfigureAwait(false);
                    if (count == 1 && (command[0] == 1 || command[0] == 2)) { receive(command[0] == 1 ? "show" : "quit"); await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false); }
                } catch (OperationCanceledException) { } catch (IOException) { if (!stop.IsCancellationRequested) await Task.Delay(100).ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
            }
        }
        internal static async Task<bool> SendAsync(string command, string name = null)
        {
            using var timeout = new CancellationTokenSource(2000);
            try {
                using var pipe = new NamedPipeClientStream(".", name ?? Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                await pipe.WriteAsync(new byte[] { command == "quit" ? (byte)2 : (byte)1 }, timeout.Token).ConfigureAwait(false);
                byte[] ack = new byte[1]; return await pipe.ReadAsync(ack, timeout.Token).ConfigureAwait(false) == 1 && ack[0] == 1;
            } catch (OperationCanceledException) { return false; } catch (IOException) { return false; }
        }
        public void Dispose() { stop.Cancel(); }
    }
}
