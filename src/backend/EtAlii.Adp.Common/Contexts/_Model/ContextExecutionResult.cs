namespace EtAlii.Adp.Common;

/// <summary>
/// What a provider wants to happen next after an action is triggered: gather a value,
/// have the user confirm, have the user choose from a tree of options, or nothing at all
/// (the action already did its work).
/// </summary>
/// <remarks>
/// A closed set: every case is one of the <c>ContextExecution*</c> records declared beside
/// this one, each in its own file per tech.md's no-nested-types rule.
/// </remarks>
public abstract record ContextExecutionResult;
