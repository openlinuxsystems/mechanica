namespace Mechanica.Core.ModTools;

/// <summary>
/// A mod tool refused a request: an unknown mod, a path outside the mod folder, a file that is too big.
/// The message says what to change. The AI endpoint answers it as a rejected call.
/// </summary>
public sealed class ModToolException(string message) : Exception(message);
