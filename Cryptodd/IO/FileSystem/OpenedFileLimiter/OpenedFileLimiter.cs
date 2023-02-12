using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Maxisoft.Utils.Collections.LinkedLists;

namespace Cryptodd.IO.FileSystem.OpenedFileLimiter;

public class OpenedFileLimiter : IOpenedFileLimiter
{
    private readonly ConcurrentDictionary<string, LinkedList<OpenedFileSource>> _files = new();
    private readonly ConcurrentBag<TaskCompletionSource> _awaiters = new();
    private int _openedCount;
    public int OpenedCount => _openedCount;
    public int Count => _files.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string KeySelector(in OpenedFileSource source) => source.FileName;

    [SuppressMessage("ReSharper", "InvertIf")]
    internal bool Remove(ref LinkedListNode<OpenedFileSource>? node)
    {
        var list = node?.List;
        var res = false;
        if (list is not null)
        {
            var notify = false;
            lock (list)
            {
                if (node is not null)
                {
                    list.Remove(node);
                    Interlocked.Decrement(ref _openedCount);
                    Debug.Assert(_openedCount >= 0);
                    if (list.Count <= 0)
                    {
                        notify = _files.TryRemove(
                            new KeyValuePair<string, LinkedList<OpenedFileSource>>(KeySelector(in node.ValueRef),
                                list));
                    }

                    res = true;
                }
            }

            if (notify)
            {
                while (_awaiters.TryTake(out var awaiter))
                {
                    awaiter.TrySetResult();
                }
            }
        }

        return res;
    }

    public async Task Wait(int fileLimit, CancellationToken cancellationToken)
    {
        while (Count >= fileLimit && !cancellationToken.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource();
            _awaiters.Add(tcs);
            await using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task.ConfigureAwait(true);
            }
        }
    }

    public OpenedFileLimiterUnregisterOnDispose Register(in OpenedFileSource fileSource)
    {
        var list = _files.GetOrAdd(KeySelector(in fileSource), OpenedFileSourceLinkedListValueFactory);

        LinkedListNode<OpenedFileSource> node;
        lock (list)
        {
            node = list.AddLast(fileSource);
        }

        Interlocked.Increment(ref _openedCount);

        return new OpenedFileLimiterUnregisterOnDispose(this, node);
    }

    public bool TryRegister(OpenedFileSource fileSource, int fileLimit, out OpenedFileLimiterUnregisterOnDispose res)
    {
        if (Count >= fileLimit)
        {
            res = new OpenedFileLimiterUnregisterOnDispose(this, null);
            return false;
        }

        res = Register(in fileSource);
        return true;
    }

    private static LinkedListAsIList<OpenedFileSource> OpenedFileSourceLinkedListValueFactory(string arg) =>
        new LinkedListAsIList<OpenedFileSource>();
}

public interface IDefaultOpenedFileLimiter
{
    int Limit { get; }
    int OpenedCount { get; }
    int Count { get; }
    bool TryRegister(OpenedFileSource fileSource, out OpenedFileLimiterUnregisterOnDispose res);
    Task Wait(CancellationToken cancellationToken);
}