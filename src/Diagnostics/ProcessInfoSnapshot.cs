namespace CodexQuotaHud;

internal sealed record ProcessInfoSnapshot(
    int ProcessId,
    string Name,
    int? ParentProcessId,
    string? ParentName,
    string? ExecutablePath,
    string CommandLine,
    DateTime? StartTime,
    IReadOnlyList<int> ParentChain);
