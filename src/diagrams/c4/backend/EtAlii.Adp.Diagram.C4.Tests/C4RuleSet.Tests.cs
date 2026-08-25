using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// One test per C4 rule, each written to fail before the rule exists. The validator is a pure
/// function, so every one of these is a plain string of DSL in and a list of problems out - no
/// file, no canvas, no connection (c4-diagrams Requirement 10 and its non-functional
/// requirements).
/// </summary>
public class C4RuleSetTests
{
    private static IReadOnlyList<DiagramProblem> Validate(string dsl) =>
        C4RuleSet.Validate(C4Parser.Parse(C4Document.Parse(dsl)));

    private static string[] RuleIds(string dsl) => Validate(dsl).Select(problem => problem.RuleId).ToArray();

    /// <summary>A workspace with everything C4 asks for, so a rule firing here is a false positive.</summary>
    private const string Clean = """
        workspace "Clean" {
            model {
                u = person "User" "Someone who uses the system."
                s = softwareSystem "System" "Does the thing." {
                    web = container "Web App" "Serves pages." "React"
                    db = container "Database" "Stores things." "PostgreSQL"
                    web -> db "Reads from and writes to" "SQL/TCP"
                }
                u -> web "Visits" "HTTPS"
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    [Fact]
    public void ACleanModel_HasNoProblems()
    {
        // Arrange, act and assert.
        Assert.Empty(Validate(Clean));
    }

    [Fact]
    public void AnElementWithoutADescription_IsReported()
    {
        // Arrange.
        var dsl = Clean.Replace("person \"User\" \"Someone who uses the system.\"", "person \"User\"", StringComparison.Ordinal);

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.MissingDescription);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("User", problem.Message, StringComparison.Ordinal);
        // Requirement 10.8: a problem names the element, so clicking it can select it.
        Assert.Equal(new DiagramProblemElementLocation("u"), problem.Location);
    }

    [Fact]
    public void AContainerWithoutATechnology_IsReported()
    {
        // Arrange.
        var dsl = Clean.Replace("container \"Web App\" \"Serves pages.\" \"React\"", "container \"Web App\" \"Serves pages.\"", StringComparison.Ordinal);

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.MissingTechnology);
        Assert.Contains("technology", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AComponentWithoutATechnology_IsReported()
    {
        // Arrange and act.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        c = container "C" "desc" "Kotlin" {
                            comp = component "Comp" "desc"
                        }
                    }
                }
            }
            """;

        // Assert.
        Assert.Contains(C4Rules.MissingTechnology, RuleIds(dsl));
    }

    [Fact]
    public void APersonWithoutATechnology_IsNotReported()
    {
        // Arrange, act and assert.
        // C4 asks for a technology on containers and components. A person does not have one,
        // and reporting it would be noise that teaches users to ignore the panel.
        Assert.DoesNotContain(C4Rules.MissingTechnology, RuleIds(Clean));
    }

    [Fact]
    public void AnUnlabelledRelationship_IsReported()
    {
        // Arrange.
        var dsl = Clean.Replace("u -> web \"Visits\" \"HTTPS\"", "u -> web", StringComparison.Ordinal);

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.UnlabelledRelationship);
        Assert.IsType<DiagramProblemLineLocation>(problem.Location);
    }

    [Fact]
    public void ARelationshipBetweenContainersWithNoProtocol_IsReported()
    {
        // Act.
        // Act.
        var dsl = Clean.Replace("web -> db \"Reads from and writes to\" \"SQL/TCP\"", "web -> db \"Reads from and writes to\"", StringComparison.Ordinal);

        // Assert.
        // Assert.
        Assert.Contains(C4Rules.MissingProtocol, RuleIds(dsl));
    }

    [Fact]
    public void ARelationshipFromAPersonWithNoProtocol_IsReported()
    {
        // Arrange.
        // This test used to assert the opposite, on the reasoning that a person does not speak
        // a protocol and the rule is about how containers communicate. But a person visiting a
        // web application does so over something - HTTPS, here, which is what the fixture says
        // before this line strips it - and Structurizr inspects every relationship for a
        // technology rather than only the container-to-container ones
        // (quality-gates Requirement 1.3).
        var dsl = Clean.Replace("u -> web \"Visits\" \"HTTPS\"", "u -> web \"Visits\"", StringComparison.Ordinal);

        // Act and assert.
        Assert.Contains(C4Rules.MissingProtocol, RuleIds(dsl));
    }

    /// <summary>One software system, nothing else, and no view - a file someone has just started.</summary>
    private const string Lonely = """
        workspace "Lonely" {
            model {
                s = softwareSystem "System" "Does the thing."
            }
        }
        """;

    [Fact]
    public void ALonelySystem_InAModelWithNoViews_IsNotReported()
    {
        // Act and assert.
        // Every element is disconnected for the minute between being declared and being wired
        // up. A tool that greets a new file with warnings teaches people to ignore warnings, so
        // the rule waits until the model declares a view to be inconsistent with.
        Assert.DoesNotContain(C4Rules.DisconnectedElement, RuleIds(Lonely));
    }

    [Fact]
    public void ALonelySystem_InAModelWithAView_IsReported()
    {
        // Arrange.
        // The same lonely model, once it has a view. This is the other half of the
        // suppression: what it defers, it does not abandon.
        var dsl = """
            workspace "Lonely" {
                model {
                    s = softwareSystem "System" "Does the thing."
                }
                views {
                    systemLandscape "landscape" {
                        include *
                    }
                }
            }
            """;

        // Act and assert.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.DisconnectedElement);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("System", problem.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void ASystemWhoseContainersAreConnected_IsNotReportedAsDisconnected()
    {
        // Act and assert.
        // The clean fixture declares no relationship naming `s` at all - they are between the
        // person and a container, and between two containers. C4 draws the same conversation at
        // several levels, so a system whose containers talk to the world is not left over.
        Assert.DoesNotContain(C4Rules.DisconnectedElement, RuleIds(Clean));
    }
    [Fact]
    public void ARelationshipNamingSomethingUndeclared_IsReported()
    {
        // Arrange.
        var dsl = Clean.Replace("u -> web \"Visits\" \"HTTPS\"", "u -> ghost \"Visits\" \"HTTPS\"", StringComparison.Ordinal);

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.DanglingRelationship);
        Assert.Contains("ghost", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AContainerOnAContextView_IsReported()
    {
        // Arrange.
        // Requirement 5.2: a system context diagram shows people and software systems only.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        web = container "Web" "desc" "React"
                    }
                }
                views {
                    systemContext s "context" {
                        include web
                    }
                }
            }
            """;

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.KindNotPermitted);
        Assert.Contains("container", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DiagramProblemElementLocation("web"), problem.Location);
    }

    [Fact]
    public void AContainerOnAContainerView_IsNotReported()
    {
        // Arrange and act.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        web = container "Web" "desc" "React"
                    }
                }
                views {
                    container s "containers" {
                        include web
                    }
                }
            }
            """;

        // Assert.
        Assert.DoesNotContain(C4Rules.KindNotPermitted, RuleIds(dsl));
    }

    [Fact]
    public void ADynamicViewMixingLevels_IsNotReported_BecauseTheWorkedExampleMixesThem()
    {
        // Arrange and act.
        // Requirement 7.7 said a dynamic view draws systems OR containers OR components, and
        // there was a rule here enforcing it - until the C4 worked example joined the corpus
        // and broke it. Its sign-in view is scoped to a container and shows that container's
        // components talking to the single-page application and the database, which are
        // containers. That is C4's own canonical dynamic view, so the requirement was the
        // thing that was wrong, and the rule went. What follows is the same shape.
        var dsl = """
            workspace {
                model {
                    a = softwareSystem "A" "desc" {
                        web = container "Web" "desc" "React"
                    }
                    b = softwareSystem "B" "desc"
                    web -> b "Calls" "HTTPS"
                    a -> b "Calls" "HTTPS"
                }
                views {
                    dynamic a "scenario" {
                        web -> b "Calls"
                        a -> b "Calls"
                    }
                }
            }
            """;

        // Assert.
        Assert.DoesNotContain("c4.mixed-abstraction-levels", RuleIds(dsl));
    }

    [Fact]
    public void AViewScopedToSomethingUndeclared_IsReported()
    {
        // Act.
        // Act.
        var dsl = Clean.Replace("systemContext s \"context\"", "systemContext ghost \"context\"", StringComparison.Ordinal);

        // Assert.
        // Assert.
        Assert.Contains(C4Rules.UnknownViewScope, RuleIds(dsl));
    }

    [Fact]
    public void AnEmptyView_IsReported()
    {
        // Arrange and act.
        var dsl = """
            workspace {
                model {
                }
                views {
                    systemLandscape "all" {
                    }
                }
            }
            """;

        // Assert.
        Assert.Contains(C4Rules.EmptyView, RuleIds(dsl));
    }

    [Fact]
    public void AComponentDeclaredOutsideAContainer_IsReported()
    {
        // Arrange.
        // C4's hierarchy is what gives each level its meaning: a component is part of a
        // container, and one written straight into the model is not a C4 component at all
        // (Requirement 10.6).
        var dsl = """
            workspace {
                model {
                    stray = component "Stray" "desc" "Kotlin"
                }
            }
            """;

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.MisplacedElement);
        Assert.Contains("part of a container", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DiagramProblemElementLocation("stray"), problem.Location);
    }

    [Fact]
    public void AContainerInsideAnotherContainer_IsReported()
    {
        // Arrange.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        outer = container "Outer" "desc" "Kotlin" {
                            inner = container "Inner" "desc" "Kotlin"
                        }
                    }
                }
            }
            """;

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.MisplacedElement);
        Assert.Equal(new DiagramProblemElementLocation("inner"), problem.Location);
    }

    [Fact]
    public void ProperlyNestedElements_AreNotReported()
    {
        // Arrange, act and assert.
        Assert.DoesNotContain(C4Rules.MisplacedElement, RuleIds(Clean));
    }

    [Fact]
    public void AnIncludeIsReported_BecauseWhatItDeclaresIsMissingFromTheDiagram()
    {
        // Arrange.
        // ADP reads only the primary document. Silently showing half a model would be worse
        // than saying so (design "Prerequisites and blockers" 4).
        var dsl = """
            workspace {
                !include shared/model.dsl
                model {
                    s = softwareSystem "S" "desc"
                }
            }
            """;

        // Act and assert, step by step.
        var problem = Assert.Single(Validate(dsl), p => p.RuleId == C4Rules.IncludeNotFollowed);
        Assert.Contains("shared/model.dsl", problem.Message, StringComparison.Ordinal);
        // The file itself is the subject, so there is no element or line to point at.
        Assert.Null(problem.Location);
    }

    [Fact]
    public void ADocumentWithNoIncludes_IsNotReported()
    {
        // Arrange, act and assert.
        Assert.DoesNotContain(C4Rules.IncludeNotFollowed, RuleIds(Clean));
    }

    [Fact]
    public void EveryProblem_IsAWarning_SoAnUnfinishedModelStillSaves()
    {
        // Arrange.
        // Requirement 10.7: a model mid-edit is routinely incomplete. Anything that cannot be
        // a work in progress is refused by the command that would create it, not reported here.
        var dsl = """
            workspace {
                model {
                    u = person "User"
                    s = softwareSystem "System" {
                        web = container "Web"
                    }
                    u -> web
                }
                views {
                    systemContext s "context" {
                        include *
                    }
                }
            }
            """;

        // Act.
        var problems = Validate(dsl);

        // Assert.
        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity));
    }

    [Fact]
    public void EveryRuleId_IsPrefixedWithTheModulesName()
    {
        // Act.
        // Act.
        // Core's convention: "<module>.<rule>", so a problem's origin is readable in the panel.
        var dsl = "workspace {\n  model {\n    u = person \"U\"\n  }\n  views {\n    systemLandscape \"all\" {\n    }\n  }\n}\n";

        // Assert.
        // Assert.
        Assert.All(Validate(dsl), problem => Assert.StartsWith("c4.", problem.RuleId, StringComparison.Ordinal));
    }

    [Fact]
    public void PermittedKinds_MatchWhatEachC4ViewShows()
    {
        // Arrange, act and assert.
        Assert.Equal([C4ElementKind.Person, C4ElementKind.SoftwareSystem], C4RuleSet.PermittedKinds(C4ViewKind.SystemContext));
        Assert.Equal([C4ElementKind.Person, C4ElementKind.SoftwareSystem], C4RuleSet.PermittedKinds(C4ViewKind.SystemLandscape));
        Assert.Contains(C4ElementKind.Container, C4RuleSet.PermittedKinds(C4ViewKind.Container));
        Assert.DoesNotContain(C4ElementKind.Component, C4RuleSet.PermittedKinds(C4ViewKind.Container));
        Assert.Contains(C4ElementKind.Component, C4RuleSet.PermittedKinds(C4ViewKind.Component));
        Assert.Contains(C4ElementKind.DeploymentNode, C4RuleSet.PermittedKinds(C4ViewKind.Deployment));
        Assert.DoesNotContain(C4ElementKind.Person, C4RuleSet.PermittedKinds(C4ViewKind.Deployment));
    }

    [Fact]
    public async Task TheValidator_ReachesCoresSeam()
    {
        // Arrange.
        var validator = new C4Validator(new DiagramOrigin("c4", "context"));

        // Act.
        var problems = await validator.ValidateAsync(Clean, "clean", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(problems);
        Assert.Equal("c4/context", validator.Origin.Key);
    }
}
