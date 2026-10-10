namespace GTMP.Scripting.Language;

/// <summary>
/// Result of parsing a complete script file: its global variable declarations and its
/// function declarations.
/// </summary>
/// <param name="Globals">
/// Top level <c>new</c> declarations. They are evaluated once when the script is loaded
/// and live in the interpreter's global scope, so every function of every script can
/// reach them.
/// </param>
/// <param name="Functions">Function declarations in source order.</param>
public readonly record struct ParsedProgram(
    IReadOnlyList<VariableDeclaration> Globals,
    IReadOnlyList<FunctionDeclaration> Functions);