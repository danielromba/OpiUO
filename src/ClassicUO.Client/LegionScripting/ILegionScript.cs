using System.Threading;

namespace ClassicUO.LegionScripting;

/// <summary>
/// Interface that all C# scripts must implement.
/// Each script should be in the ClassicUO.LegionScripting.Scripts namespace.
/// </summary>
public interface ILegionScript
{
    /// <summary>
    /// The display name of the script
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The Legion API instance for this script.
    /// This should be set in the constructor and stored as a private field or property.
    /// </summary>
    LegionAPI Api { get; set; }

    /// <summary>
    /// The cancellation token to check if script should stop.
    /// This should be set in the constructor and stored as a private field or property.
    /// </summary>
    CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// The main execution method for the script.
    /// Use the API and CancellationToken properties to access the Legion API and check for cancellation.
    /// </summary>
    void Execute();

    /// <summary>
    /// Run method after the script has been cancelled to perform any necessary cleanup.
    /// </summary>
    void Cleanup();
}
