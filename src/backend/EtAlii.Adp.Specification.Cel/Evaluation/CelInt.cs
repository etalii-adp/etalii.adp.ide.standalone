namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL's int arithmetic: 64-bit, and an overflow is an error rather than a wrapped value. The caller
/// has already refused a zero divisor; dividing the smallest int by -1 is the one overflow left there.
/// </summary>
internal static class CelInt
{
    public static long Negate(long value) => value == long.MinValue ? throw Overflow() : -value;

    public static long Add(long a, long b) => Checked(() => checked(a + b));

    public static long Subtract(long a, long b) => Checked(() => checked(a - b));

    public static long Multiply(long a, long b) => Checked(() => checked(a * b));

    public static long Divide(long a, long b) => a == long.MinValue && b == -1 ? throw Overflow() : a / b;

    public static long Remainder(long a, long b) => a == long.MinValue && b == -1 ? throw Overflow() : a % b;

    private static long Checked(Func<long> operation)
    {
        try
        {
            return operation();
        }
        catch (OverflowException)
        {
            throw Overflow();
        }
    }

    private static CelException Overflow() => new("Integer overflow.");
}
