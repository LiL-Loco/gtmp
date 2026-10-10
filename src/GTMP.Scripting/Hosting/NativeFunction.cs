namespace GTMP.Scripting.Hosting;

/// <summary>
/// Signature every native exposed to scripts has to match.
/// </summary>
/// <param name="args">Arguments exactly as the call site produced them; never <c>null</c>.</param>
/// <returns>The return value, or <c>null</c> for a native that returns nothing.</returns>
/// <remarks>
/// A single untyped signature keeps the engine independent of the game's C# API. The cost is
/// that a native has to validate its own arguments, which is why the registry checks the
/// argument count before the delegate is ever called.
/// </remarks>
public delegate object? NativeFunctionDelegate(object?[] args);

/// <summary>
/// A host function registered for scripts, with the argument count it was declared with.
/// </summary>
/// <remarks>
/// Immutable by design: registration happens once at server start, so a running script can never
/// observe a native changing underneath it.
/// </remarks>
public sealed class NativeFunction
{
    /// <summary>
    /// Creates a native binding.
    /// </summary>
    /// <param name="name">Name used by scripts, case sensitive, may contain dots.</param>
    /// <param name="minArgs">Lowest accepted argument count.</param>
    /// <param name="maxArgs">Highest accepted argument count, <see cref="int.MaxValue"/> for variadic.</param>
    /// <param name="implementation">The C# implementation.</param>
    /// <exception cref="ArgumentException">The name is empty or the argument range is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="implementation"/> is null.</exception>
    public NativeFunction(string name, int minArgs, int maxArgs, NativeFunctionDelegate implementation)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Der Name einer nativen Funktion darf nicht leer sein.", nameof(name));
        }

        if (minArgs < 0 || maxArgs < minArgs)
        {
            throw new ArgumentException($"Ungültiger Argumentbereich {minArgs}..{maxArgs} für '{name}'.", nameof(maxArgs));
        }

        ArgumentNullException.ThrowIfNull(implementation);

        Name = name;
        MinArgs = minArgs;
        MaxArgs = maxArgs;
        Implementation = implementation;
    }

    /// <summary>Name used by scripts.</summary>
    public string Name { get; }

    /// <summary>Lowest accepted argument count.</summary>
    public int MinArgs { get; }

    /// <summary>Highest accepted argument count, <see cref="int.MaxValue"/> for variadic.</summary>
    public int MaxArgs { get; }

    /// <summary>The C# implementation.</summary>
    public NativeFunctionDelegate Implementation { get; }

    /// <summary>
    /// Checks whether this native accepts the given number of arguments.
    /// </summary>
    /// <param name="argCount">Number of arguments the call site passed.</param>
    /// <returns><c>true</c> when the call is allowed.</returns>
    public bool Accepts(int argCount) => argCount >= MinArgs && argCount <= MaxArgs;

    /// <inheritdoc />
    public override string ToString() => $"{Name}({MinArgs}..{(MaxArgs == int.MaxValue ? "n" : MaxArgs.ToString())})";
}
