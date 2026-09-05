using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed class TaskWorkspacePathResolver
{
    private readonly string _workspaceRoot;

    public TaskWorkspacePathResolver(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string WorkspaceRoot => _workspaceRoot;

    public string Resolve(TaskId taskId)
    {
        ValidateTaskId(taskId.Value);

        var taskDirectory = Path.GetFullPath(Path.Combine(_workspaceRoot, taskId.Value));
        var relativePath = Path.GetRelativePath(_workspaceRoot, taskDirectory);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The task identifier resolves outside the workspace root.", nameof(taskId));
        }

        return taskDirectory;
    }

    public void EnsureTaskDirectory(string taskDirectory)
    {
        Directory.CreateDirectory(taskDirectory);
        if ((File.GetAttributes(taskDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A task workspace cannot be a symbolic link or reparse point.");
        }
    }

    private static void ValidateTaskId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("The task identifier must be a safe directory name.", nameof(value));
        }
    }
}
