namespace Fishbone.Debugging;

/// <summary>
/// The named pipe a SpineIDE that a host opened listens on between runs. A host's next run sends
/// <c>attach &lt;port&gt;</c>, and an idle SpineIDE answers <c>ok</c> and attaches, or <c>busy</c>.
/// One per user, so another user's host can't take over the window.
/// </summary>
public static class SpineIdeAttachPipe
{
    public static string Name { get; } = $"fishbone-spineide-{Environment.UserName}";
}
