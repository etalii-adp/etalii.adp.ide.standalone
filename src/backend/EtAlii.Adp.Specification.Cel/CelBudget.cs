namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// The evaluation cost an expression may spend (DISL §2.5, FBL §16): every step and every function's
/// declared cost is charged, and the evaluation fails once the limit is passed, so no expression runs
/// away. A function that evaluates another expression passes this budget on, so the limit covers the
/// whole evaluation.
/// </summary>
public sealed class CelBudget(long limit)
{
    private long Limit { get; } = limit;

    public long Used { get; private set; }

    public void Charge(long cost)
    {
        Used += Math.Max(cost, 0);
        if (Used > Limit) throw new CelException("The expression exceeded its evaluation budget.");
    }
}
