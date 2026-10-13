using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>
/// Move earlier and Move later answer to the keypress a person makes for <c>Alt+Up</c> and
/// <c>Alt+Down</c>, in the shape a <c>KeyboardEvent</c> gives it: the key <c>ArrowUp</c> with Alt held.
/// The definition writes <c>Alt+Up</c>; passing that text whole as the key matched no keypress at all
/// (spec 025, T032/T033).
/// </summary>
public class AbmShortcutsTests
{
    private const string Text = "## Behavior\n- **Do in order:** A\n  - **Do:** B\n  - **Do:** C\n  - **Do:** D\n";

    [Theory]
    [InlineData(AbmContextActionProvider.MoveEarlierActionId, "ArrowUp")]
    [InlineData(AbmContextActionProvider.MoveLaterActionId, "ArrowDown")]
    public void AMove_AnswersToAltAndTheArrowKey_AsTheBrowserReportsIt(string actionId, string key)
    {
        // Arrange.
        var entry = AbmEntries.Read(Text);
        var pressed = new ContextShortcutDefinition(key, Alt: true);

        // Act.
        var action = AbmDefinition.Menus(entry, "1.2").SelectMany(group => group.Actions).Single(action => action.Id == actionId);

        // Assert.
        Assert.NotNull(action.Shortcut);
        Assert.True(action.Shortcut.Matches(pressed), $"{actionId} carries {action.Shortcut}, which does not match {pressed}.");
        Assert.False(action.Shortcut.Matches(new ContextShortcutDefinition(key)), $"{actionId} also answers to a bare {key}.");
    }
}
