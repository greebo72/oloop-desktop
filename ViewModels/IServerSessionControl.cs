namespace OloopApp.ViewModels;

/// <summary>Where a session with the server stands.</summary>
public enum ServerSessionState
{
    /// <summary>This computer is not signed in.</summary>
    SignedOut,

    /// <summary>Signed in, the keys are locked.</summary>
    Locked,

    /// <summary>Signed in and open, the keys are kept in memory only.</summary>
    Open,

    /// <summary>Signed in and open, the keys are also kept in the secret store of the system.</summary>
    OpenKeyKept,
}

/// <summary>
/// What the settings dialog may do with the session with the server. The shell implements it, so
/// the dialog changes the session and the shell stops showing the data that is no longer open.
/// </summary>
public interface IServerSessionControl
{
    ServerSessionState State { get; }

    /// <summary>Forgets the keys on this computer; the sign-in stays.</summary>
    Task LockAsync();

    /// <summary>Cuts this computer off at the server and forgets the token and the keys.</summary>
    Task SignOutAsync();

    /// <summary>
    /// Forgets the highest revisions this computer has seen, for a server that was restored from a
    /// backup on purpose. The data is read again afterwards.
    /// </summary>
    Task ForgetRevisionsAsync();

    /// <summary>
    /// The view model of the agents of the signed-in user, or null while the keys are not open. Making
    /// an agent needs the secret key. A control that does not know agents has none.
    /// </summary>
    AgentsViewModel? CreateAgentsViewModel() => null;

    /// <summary>
    /// The view model that imports a folder into the server, or null while the keys are not open: the
    /// import writes with the key of the user. A control that does not know the import has none.
    /// </summary>
    ImportFolderViewModel? CreateImportViewModel() => null;
}
