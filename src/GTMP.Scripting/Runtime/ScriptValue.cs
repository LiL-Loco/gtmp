using System.Globalization;

namespace GTMP.Scripting.Runtime;

/// <summary>
/// Helpers that define how CLR values behave as Pawn values.
/// </summary>
/// <remarks>
/// The subset keeps exactly these runtime shapes: <see cref="double"/> for every number,
/// <see cref="string"/>, <see cref="bool"/>, <c>object?[]</c> for arrays, <see cref="ScriptFunction"/>
/// for script functions and <see cref="Hosting.NativeFunction"/> for natives. Anything else is a
/// host object leaking into a script and is rejected rather than coerced.
/// <para>
/// All conversions are invariant culture: the server writes numbers into database columns and
/// logs them, and a German or Turkish server locale must not change the result.
/// </para>
/// </remarks>
public static class ScriptValue
{
    /// <summary>
    /// Truthiness rule: <c>null</c> and <c>false</c> are false, <c>true</c> is true, a number is
    /// false when it is zero or NaN, a non empty string and every array are true.
    /// </summary>
    /// <param name="value">The runtime value to test.</param>
    /// <returns><c>true</c> when the value is considered true.</returns>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool boolean => boolean,
        double number => number != 0d && !double.IsNaN(number),
        string text => text.Length > 0,
        object?[] array => array.Length > 0,
        _ => true,
    };

    /// <summary>
    /// Equality used by <c>==</c> and <c>!=</c>. Numbers and strings compare by value, everything
    /// else falls back to reference equality with <c>null</c> handled explicitly.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when both operands are considered equal.</returns>
    public static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.Equals(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        return ReferenceEquals(left, right);
    }

    /// <summary>
    /// Strict ordering used by <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> and <c>&gt;=</c>. Only two
    /// numbers or two strings are comparable; mixing types is a script bug, not a coercion.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>A negative, zero or positive result like <see cref="IComparable.CompareTo(object)"/>.</returns>
    /// <exception cref="ScriptRuntimeException">The operands are not comparable.</exception>
    public static int Compare(object? left, object? right, string scriptName)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        throw new ScriptRuntimeException(
            $"Werte vom Typ {Describe(left)} und {Describe(right)} lassen sich nicht vergleichen.",
            scriptName,
            0,
            0);
    }

    /// <summary>
    /// Numeric coercion for arithmetic. Only <see cref="double"/>, <see cref="bool"/> and
    /// <see cref="string"/> that holds a number are accepted.
    /// </summary>
    /// <param name="value">The runtime value to convert.</param>
    /// <param name="scriptName">Script name used for the error message.</param>
    /// <returns>The numeric value.</returns>
    /// <exception cref="ScriptRuntimeException">The value cannot be used as a number.</exception>
    public static double ToNumber(object? value, string scriptName) => value switch
    {
        double number => number,
        bool boolean => boolean ? 1d : 0d,
        null => throw new ScriptRuntimeException("null kann nicht als Zahl verwendet werden.", scriptName, 0, 0),
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => throw new ScriptRuntimeException($"'{Describe(value)}' ist keine Zahl.", scriptName, 0, 0),
    };

    /// <summary>
    /// Array coercion for indexing and <c>foreach</c>.
    /// </summary>
    /// <param name="value">The runtime value to convert.</param>
    /// <param name="scriptName">Script name used for the error message.</param>
    /// <returns>The array.</returns>
    /// <exception cref="ScriptRuntimeException">The value is not an array.</exception>
    public static object?[] ToArray(object? value, string scriptName) => value switch
    {
        object?[] array => array,
        null => throw new ScriptRuntimeException("null ist kein Array.", scriptName, 0, 0),
        _ => throw new ScriptRuntimeException($"'{Describe(value)}' ist kein Array.", scriptName, 0, 0),
    };

    /// <summary>
    /// Renders a value the way Pawn's <c>printf %d/%s</c> style output does, which is also what a
    /// native receives when a script logs it. Used by tests and by host logging.
    /// </summary>
    /// <param name="value">The runtime value to render.</param>
    /// <returns>A readable representation; never <c>null</c>.</returns>
    public static string ToDisplayString(object? value) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        string text => text,
        double number => FormatNumber(number),
        object?[] array => "Array(" + array.Length + ")",
        _ => value.ToString() ?? "null",
    };

    /// <summary>
    /// Formats a double so that whole numbers stay whole (<c>3</c> instead of <c>3,0</c>) while
    /// fractions keep enough digits to be useful in a log line.
    /// </summary>
    /// <param name="number">The number to format.</param>
    /// <returns>The formatted number, invariant culture.</returns>
    public static string FormatNumber(double number)
    {
        if (double.IsNaN(number)) return "NaN";
        if (double.IsPositiveInfinity(number)) return "Infinity";
        if (double.IsNegativeInfinity(number)) return "-Infinity";

        if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
        {
            return ((long)number).ToString(CultureInfo.InvariantCulture);
        }

        return number.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        bool => "bool",
        double => "Zahl",
        string => "Zeichenkette",
        object?[] => "Array",
        _ => value?.GetType().Name ?? "null",
    };
}
