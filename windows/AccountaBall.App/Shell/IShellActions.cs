namespace AccountaBall.App.Shell;

/// The actions the phase views can invoke. Implemented by the App-layer host
/// (M4.4), which routes them to the engine + AppState. Keeping views behind this
/// interface lets them stay free of engine/store references.
public interface IShellActions
{
    // Setup / session lifecycle
    void OpenSetup();
    void AddTask(string title, string context);
    void RemoveTask(int index);
    void SetDriftLimit(int limit);
    void StartSession();
    void EndSession();

    // Ambiguous resolution (ask once)
    void AcceptAmbiguous(string reason);
    void RejectAmbiguous();

    // Off-task card
    void TakeBreak();
    void ResumeWatching(bool graceForCurrentActivity);

    // Completion
    void DismissCompletion();

    // AI unavailable
    void RetryAi();
    void OpenOllamaDownload();

    // FreeBall passive mode
    void StartFreeBall();
    void EndFreeBall();
    void ViewFreeBallHistory();
    void CloseFreeBallHistory();

    /// Past FreeBall sessions (newest first) for the history browser.
    System.Collections.Generic.IReadOnlyList<AccountaBall.Core.Models.FreeBallRecap> FreeBallHistory();

    // Tapping the compact ball opens its control card (so the user can act on it).
    void BallTapped();

    // Quit the app
    void ExitApp();
}
