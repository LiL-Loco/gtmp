using GTMP.Scripting.Language;

namespace GTMP.Scripting.Runtime;

/// <summary>
/// A function declared inside a <c>.pwn</c> file, together with the file it came from.
/// </summary>
/// <remarks>
/// The owning <see cref="ScriptName"/> is what makes a runtime error useful: the host shows
/// "gamemode.pwn (Zeile 42)" instead of an anonymous stack trace, which is the only thing a
/// script author can act on.
/// </remarks>
public sealed class ScriptFunction
{
    /// <summary>
    /// Creates a callable script function.
    /// </summary>
    /// <param name="declaration">The parsed declaration providing the body and parameter list.</param>
    /// <param name="scriptName">Name of the script file the function was declared in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaration"/> is null.</exception>
    public ScriptFunction(FunctionDeclaration declaration, string scriptName)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        Declaration = declaration;
        ScriptName = string.IsNullOrEmpty(scriptName) ? "<script>" : scriptName;
    }

    /// <summary>The parsed declaration.</summary>
    public FunctionDeclaration Declaration { get; }

    /// <summary>Name of the script file the function was declared in.</summary>
    public string ScriptName { get; }

    /// <summary>Function name as written in the source.</summary>
    public string Name => Declaration.Name;

    /// <summary>Number of parameters the caller has to supply.</summary>
    public int ParameterCount => Declaration.Parameters.Count;

    /// <inheritdoc />
    public override string ToString() => $"{ScriptName}::{Name}";
}
