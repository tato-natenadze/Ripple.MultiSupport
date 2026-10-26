using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    internal static class TestAwait
    {
        internal static async Task UntilAsync(Task task)
        {
            using (var stopTimer = new CancellationTokenSource())
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(5), stopTimer.Token);
                var completed = await Task.WhenAny(task, timeout).ConfigureAwait(false);
                Assert.AreSame(task, completed, "The expected test synchronization signal did not complete within five seconds.");
                stopTimer.Cancel();
                await task.ConfigureAwait(false);
            }
        }
    }
}
