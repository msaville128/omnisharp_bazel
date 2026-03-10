// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.Composition;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using static System.IO.NotifyFilters;

namespace OmniSharp.Bazel;

/// <summary>
/// A document source that notifies the Bazel Project Manager of new, changed,
/// and removed documents.
/// </summary>
[Export, Shared]
[method: ImportingConstructor]
public class FileSystemSource
    (
        BazelProjectManager manager,
        IOmniSharpEnvironment environment,
        ILoggerFactory loggerFactory
    )
{
    readonly record struct FileSystemEvent(string Path, bool IsDeleted);

    readonly Channel<FileSystemEvent> eventQueue =
        Channel.CreateBounded<FileSystemEvent>(capacity: 100);

    readonly FileSystemWatcher watcher =
        new(environment.TargetDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = FileName | DirectoryName | LastWrite
        };

    readonly ILogger logger = loggerFactory.CreateLogger<FileSystemSource>();

    /// <summary>
    /// Begins monitoring the file system for documents.
    /// </summary>
    public void Init()
    {
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;

        watcher.EnableRaisingEvents = true;

        // Do the heavy lifting async so that file system events keep coming in.
        Task.Run(ProcessEventsAsync);
    }

    async Task ProcessEventsAsync()
    {
        var events = eventQueue.Reader.ReadAllAsync();
        await foreach (FileSystemEvent @event in events)
        {
            if (@event.IsDeleted)
            {
                await HandleDeletedAsync(@event.Path);
            }
            else
            {
                await HandleChangedAsync(@event.Path);
            }
        }
    }

    async Task HandleChangedAsync(string path)
    {
        // No-op if directory but one watcher for both files and directories is
        // needed to have file system events received in order.
        if (!File.Exists(path))
        {
            return;
        }

        if (!Document.TryCreate(path, out Document document))
        {
            return;
        }

        await manager.NotifyDocumentAsync(document);
    }

    async Task HandleDeletedAsync(string path)
    {
        await manager.NotifyDeletionAsync(path);
    }

    void OnChanged(object _, FileSystemEventArgs args)
    {
        FileSystemEvent changeEvent = new(args.FullPath, IsDeleted: false);
        if (!eventQueue.Writer.TryWrite(changeEvent))
        {
            logger.LogError("Dropped change event for {path}", args.FullPath);
        }
    }

    void OnDeleted(object _, FileSystemEventArgs args)
    {
        FileSystemEvent deleteEvent = new(args.FullPath, IsDeleted: true);
        if (!eventQueue.Writer.TryWrite(deleteEvent))
        {
            logger.LogError("Dropped delete event for {path}", args.FullPath);
        }
    }

    void OnRenamed(object _, RenamedEventArgs args)
    {
        FileSystemEvent deleteEvent = new(args.OldFullPath, IsDeleted: true);
        if (!eventQueue.Writer.TryWrite(deleteEvent))
        {
            logger.LogError("Dropped delete event for {path}", args.OldFullPath);
        }

        FileSystemEvent changeEvent = new(args.FullPath, IsDeleted: false);
        if (!eventQueue.Writer.TryWrite(changeEvent))
        {
            logger.LogError("Dropped change event for {path}", args.FullPath);
        }
    }

    void OnError(object _, ErrorEventArgs args)
    {
        logger.LogError(args.GetException(),
            "Exception thrown by FileSystemWatcher. "
            + "OmniSharp may need to be restarted.");
    }
}
