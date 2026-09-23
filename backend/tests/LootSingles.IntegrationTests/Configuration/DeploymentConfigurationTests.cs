namespace LootSingles.IntegrationTests.Configuration;

/// <summary>
/// 019 T023 / T028 / T039, research.md §16. Text assertions over the deployment workflows, following
/// the precedent set by <see cref="DatabaseConfigurationTests"/>.
///
/// These cover three concerns, deliberately narrow. An earlier plan listed nine assertions; the ones
/// dropped asserted that YAML written the same day said what it said, which is tautological when
/// written and earns its keep only on a later careless edit. What is kept guards something that
/// either sits on the no-compromise list or fails silently: a missing concurrency group is invisible
/// until two merges land a minute apart, a default on the production commit input hollows out the
/// approval gate without breaking anything a runtime test would notice, and a `0.0.0.0` rule opens
/// the database to every Azure tenant while everything still works.
/// </summary>
public sealed class DeploymentConfigurationTests
{
    [Fact]
    public void Stage_deploy_workflow_serialises_its_runs()
    {
        var workflow = ReadWorkflow("deploy-stage.yml");

        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("group: deploy-stage", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_deploy_workflow_reuses_the_quality_gate()
    {
        var workflow = ReadWorkflow("deploy-stage.yml");

        // The gate runs against the merge commit, which no pull request ever tested.
        Assert.Contains(
            "./.github/workflows/pr-quality-gate.yml",
            workflow,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Production_deploy_workflow_requires_a_named_commit()
    {
        var workflow = ReadWorkflow("deploy-production.yml");

        // The whole promotion design rests on this. A job carrying `environment:` does not start
        // until a reviewer approves, so anything resolved inside it is resolved AFTER approval —
        // meaning the approver would be agreeing to a lookup, not to an image. A required input with
        // no default is a literal value, fixed when the run starts, that a merge to main cannot
        // change while the run waits (FR-013, SC-004).
        Assert.Contains("required: true", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("default:", workflow, StringComparison.Ordinal);

        // And the approver has to be able to see it: job names render in the run's job list, which
        // is the panel the "Review deployments" button sits in.
        Assert.Contains("inputs.commit", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_deploy_workflow_serialises_its_runs()
    {
        var workflow = ReadWorkflow("deploy-production.yml");

        Assert.Contains("group: deploy-production", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_deploy_workflow_is_manual_only_and_gated()
    {
        var workflow = ReadWorkflow("deploy-production.yml");

        // Merging code must never change production (FR-011, SC-003).
        Assert.DoesNotContain("push:", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: production", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_smoke_test_proves_the_app_reaches_its_database_and_stage_does_not_ask()
    {
        var production = ReadWorkflow("deploy-production.yml");
        var stage = ReadWorkflow("deploy-stage.yml");

        // The deliberate asymmetry, pinned. Production must prove the *application's* identity can
        // read — a successful migration does not, since the migrate job runs as a different identity
        // (FR-024). Stage must not ask, because each call on a paused free-tier database wakes it and
        // bills an hour of a ~55-hour monthly allowance. Without this test the asymmetry looks like
        // an oversight and gets "fixed" into burning the free tier.
        // Asserting the *call*, not the mention: both files discuss the asymmetry in comments, and a
        // check that trips over its own explanation is a check nobody keeps.
        Assert.Contains("check /health/database", production, StringComparison.Ordinal);
        Assert.DoesNotContain("check /health/database", stage, StringComparison.Ordinal);
    }

    [Fact]
    public void No_tracked_file_permits_every_address_to_reach_the_database()
    {
        // FR-020. A `0.0.0.0` SQL firewall rule is labelled "Allow Azure services" and sounds
        // harmless; it admits every other Azure customer's resources to a database holding customer
        // names and addresses. This is a guard rather than a Red → Green pair: it passes when written
        // and exists to fail on a later careless edit.
        //
        // Committed credentials are NOT checked here. pr-quality-gate.yml already greps tracked files
        // for credential-bearing assignments, and DatabaseConfigurationTests already asserts that
        // step exists — a second check would be duplication, not defence (SC-009).
        var root = FindRepositoryRoot();
        var offenders = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path, root))
            // This file necessarily contains the pattern it searches for, so it excludes itself.
            .Where(path =>
                !Path.GetFileName(path)
                    .Equals(nameof(DeploymentConfigurationTests) + ".cs", StringComparison.Ordinal)
            )
            .Where(path => File.ReadAllText(path).Contains("0.0.0.0", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsIgnored(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        string[] skip =
        [
            ".git/",
            "node_modules/",
            "/bin/",
            "/obj/",
            "TestResults/",
            "playwright-report/",
            "test-results/",
        ];
        if (skip.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
        {
            return true;
        }

        // Configuration only. Markdown is deliberately excluded: the risk this guards against is a
        // configuration file that permits every address, not a document explaining why you must not
        // write one — and this repository's runbook and research notes discuss that rule at length.
        // A check that trips over its own documentation gets deleted rather than heeded.
        string[] extensions = [".cs", ".yml", ".yaml", ".json", ".ps1", ".sh", ".csproj"];
        return !extensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)
            && Path.GetFileName(relative) != "Dockerfile";
    }

    private static string ReadWorkflow(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
