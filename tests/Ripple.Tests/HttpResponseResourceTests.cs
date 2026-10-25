using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    [TestClass]
    public class HttpResponseResourceTests
    {
        private const int Limit = 16 * 1024 * 1024;

        [DataTestMethod]
        [DataRow(Limit)]
        [DataRow(Limit + 1)]
        public async Task ResponseSizeBoundaryIsEnforcedWhileReadingTheBody(int length)
        {
            var body = new GeneratedBodyStream(length);
            using (var http = new HttpClient(new BodyHandler(body)))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                if (length == Limit) Assert.AreEqual(0.00001m, await client.GetFeeAsync());
                else await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetFeeAsync());
                Assert.IsTrue(body.BytesRead <= Limit + 8192, "An oversized body must stop at the response limit.");
                Assert.IsTrue(body.Disposed);
            }
        }

        [TestMethod]
        public async Task RequestDeadlineAlsoCancelsAStalledResponseBody()
        {
            var body = new StalledBodyStream();
            var options = RpcHandler.Options(); options.RequestTimeout = TimeSpan.FromMilliseconds(100);
            using (var http = new HttpClient(new BodyHandler(body)))
            using (var client = new XrplClient(options, http))
            {
                Task request = client.GetFeeAsync();
                Assert.AreSame(request, await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(3))),
                    "The body read did not observe the request deadline.");
                try { await request; Assert.Fail("A stalled body returned a fee."); } catch (OperationCanceledException) { }
                Assert.IsTrue(body.Disposed);
            }
        }

        private sealed class BodyHandler : HttpMessageHandler
        {
            private readonly Stream _body;
            internal BodyHandler(Stream body) { _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_body) }); }
        }

        private class GeneratedBodyStream : Stream
        {
            private static readonly byte[] Prefix = Encoding.ASCII.GetBytes(
                "{\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"}}}");
            private readonly int _length;
            internal int BytesRead;
            internal bool Disposed;
            internal GeneratedBodyStream(int length) { _length = length; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = Math.Min(count, _length - BytesRead);
                for (int i = 0; i < read; i++) buffer[offset + i] = BytesRead + i < Prefix.Length ? Prefix[BytesRead + i] : (byte)' ';
                BytesRead += read; return read;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { token.ThrowIfCancellationRequested(); return Task.FromResult(Read(buffer, offset, count)); }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private sealed class StalledBodyStream : GeneratedBodyStream
        {
            internal StalledBodyStream() : base(0) { }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { await Task.Delay(Timeout.Infinite, token); return 0; }
        }
    }
}
