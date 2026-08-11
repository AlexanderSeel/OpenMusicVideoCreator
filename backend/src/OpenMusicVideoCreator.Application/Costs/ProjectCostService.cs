using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Costs;

public sealed record CostBreakdown(
    string? ProviderId,
    string? ModelId,
    decimal ActualCost,
    decimal ReservedEstimatedCost,
    int JobCount);

public sealed record SceneCostBreakdown(
    Guid? SceneId,
    decimal ActualCost,
    decimal ReservedEstimatedCost,
    int JobCount);

public sealed record ProjectCostSummary(
    Guid ProjectId,
    string Currency,
    decimal? EstimatedBudget,
    decimal? MaximumBudget,
    decimal ActualCost,
    decimal ReservedEstimatedCost,
    decimal ProjectedCost,
    decimal? RemainingBudget,
    int UnknownCostJobCount,
    IReadOnlyList<CostBreakdown> Providers,
    IReadOnlyList<SceneCostBreakdown> Scenes);

public sealed class ProjectCostService
{
    public const string BudgetCurrency = "USD";

    private readonly IProjectRepository _projects;
    private readonly IJobRepository _jobs;

    public ProjectCostService(IProjectRepository projects, IJobRepository jobs)
    {
        _projects = projects;
        _jobs = jobs;
    }

    public async Task<ProjectCostSummary> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var jobs = (await _jobs.ListAsync(cancellationToken))
            .Where(job => job.ProjectId == projectId)
            .ToArray();
        return Build(project, jobs);
    }

    public async Task EnsureCanReserveAsync(
        MusicVideoProject project,
        decimal? estimatedCost,
        string? currency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.MaximumBudget is null)
        {
            return;
        }
        if (estimatedCost is null)
        {
            throw new InvalidOperationException(
                "This generation has no cost estimate, so it cannot be queued while a hard project budget cap is configured.");
        }
        if (estimatedCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedCost), "Estimated generation cost cannot be negative.");
        }
        if (!string.Equals(currency ?? BudgetCurrency, BudgetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Project budget enforcement currently requires {BudgetCurrency} cost estimates.");
        }

        var jobs = (await _jobs.ListAsync(cancellationToken))
            .Where(job => job.ProjectId == project.Id)
            .ToArray();
        var summary = Build(project, jobs);
        if (summary.UnknownCostJobCount > 0)
        {
            throw new InvalidOperationException(
                "Project contains active/completed generation with unknown cost; hard budget compliance cannot be guaranteed until those costs are resolved.");
        }

        var projected = summary.ProjectedCost + estimatedCost.Value;
        if (projected > project.MaximumBudget.Value)
        {
            throw new InvalidOperationException(
                $"Generation would exceed the project maximum budget: {projected:0.00} {BudgetCurrency} projected > {project.MaximumBudget.Value:0.00} {BudgetCurrency} maximum.");
        }
    }

    internal static ProjectCostSummary Build(MusicVideoProject project, IReadOnlyList<GenerationJob> jobs)
    {
        var billable = jobs.Where(IsPotentiallyBillable).ToArray();
        var actual = billable.Sum(job => Normalize(job.ActualCost));
        var reserved = billable
            .Where(job => job.ActualCost is null)
            .Sum(job => Normalize(job.EstimatedCost));
        var unknown = billable.Count(job => job.ActualCost is null && job.EstimatedCost is null);
        var projected = actual + reserved;
        var remaining = project.MaximumBudget is decimal maximum
            ? Math.Max(0, maximum - projected)
            : (decimal?)null;

        var providers = billable
            .GroupBy(job => (job.ProviderId, job.ModelId))
            .Select(group => new CostBreakdown(
                group.Key.ProviderId,
                group.Key.ModelId,
                group.Sum(job => Normalize(job.ActualCost)),
                group.Where(job => job.ActualCost is null).Sum(job => Normalize(job.EstimatedCost)),
                group.Count()))
            .OrderByDescending(item => item.ActualCost + item.ReservedEstimatedCost)
            .ThenBy(item => item.ProviderId, StringComparer.Ordinal)
            .ThenBy(item => item.ModelId, StringComparer.Ordinal)
            .ToArray();

        var scenes = billable
            .GroupBy(job => job.SceneId)
            .Select(group => new SceneCostBreakdown(
                group.Key,
                group.Sum(job => Normalize(job.ActualCost)),
                group.Where(job => job.ActualCost is null).Sum(job => Normalize(job.EstimatedCost)),
                group.Count()))
            .OrderByDescending(item => item.ActualCost + item.ReservedEstimatedCost)
            .ThenBy(item => item.SceneId)
            .ToArray();

        return new ProjectCostSummary(
            project.Id,
            BudgetCurrency,
            project.EstimatedBudget,
            project.MaximumBudget,
            actual,
            reserved,
            projected,
            remaining,
            unknown,
            providers,
            scenes);
    }

    private static bool IsPotentiallyBillable(GenerationJob job) =>
        job.State is not (JobState.Cancelled or JobState.Rejected or JobState.FailedPermanent) || job.ActualCost is not null;

    private static decimal Normalize(decimal? value) => value is > 0 ? value.Value : 0m;
}
