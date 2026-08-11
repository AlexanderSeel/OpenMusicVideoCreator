using System.Collections.Concurrent;
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

public sealed record GenerationCostBreakdown(
    Guid JobId,
    Guid? SceneId,
    string Type,
    string? ProviderId,
    string? ModelId,
    JobState State,
    decimal ActualCost,
    decimal ReservedEstimatedCost,
    DateTimeOffset CreatedUtc);

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
    IReadOnlyList<GenerationCostBreakdown> Generations,
    IReadOnlyList<CostBreakdown> Providers,
    IReadOnlyList<SceneCostBreakdown> Scenes);

public sealed class ProjectCostService
{
    public const string BudgetCurrency = "USD";

    private readonly IProjectRepository _projects;
    private readonly IJobRepository _jobs;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectGates = new();

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
        return Build(project, await ListProjectJobsAsync(project.Id, cancellationToken));
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

        var gate = _projectGates.GetOrAdd(project.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureCanReserveInsideGateAsync(project, estimatedCost, currency, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> ExecuteWithinBudgetAsync<T>(
        MusicVideoProject project,
        decimal? estimatedCost,
        string? currency,
        Func<CancellationToken, Task<T>> enqueueOperation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(enqueueOperation);
        if (project.MaximumBudget is null)
        {
            return await enqueueOperation(cancellationToken);
        }

        var gate = _projectGates.GetOrAdd(project.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureCanReserveInsideGateAsync(project, estimatedCost, currency, cancellationToken);
            // The gate is intentionally held until the job is persisted. In the current MVP deployment
            // one backend process owns job enqueueing, so a concurrent request cannot pass the same
            // hard-cap check before this reservation becomes visible in the persisted job repository.
            return await enqueueOperation(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static ProjectCostSummary Build(MusicVideoProject project, IReadOnlyList<GenerationJob> jobs)
    {
        // Only jobs that have provider/cost provenance participate in accounting. Local/admin jobs with
        // no provider and no estimate/actual cost do not create false "unknown spend" warnings.
        // An estimated provider cost remains reserved until ActualCost is explicitly resolved, including
        // explicit zero after a terminal provider outcome.
        var billable = jobs.Where(IsCostTracked).ToArray();
        var actual = billable.Sum(job => Normalize(job.ActualCost));
        var reserved = billable
            .Where(job => job.ActualCost is null)
            .Sum(job => Normalize(job.EstimatedCost));
        var unknown = billable.Count(job => job.ActualCost is null && job.EstimatedCost is null);
        var projected = actual + reserved;
        var remaining = project.MaximumBudget is decimal maximum
            ? Math.Max(0, maximum - projected)
            : (decimal?)null;

        var generations = billable
            .OrderByDescending(job => job.CreatedUtc)
            .ThenBy(job => job.Id)
            .Select(job => new GenerationCostBreakdown(
                job.Id,
                job.SceneId,
                job.Type,
                job.ProviderId,
                job.ModelId,
                job.State,
                Normalize(job.ActualCost),
                job.ActualCost is null ? Normalize(job.EstimatedCost) : 0m,
                job.CreatedUtc))
            .ToArray();

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
            generations,
            providers,
            scenes);
    }

    private async Task EnsureCanReserveInsideGateAsync(
        MusicVideoProject project,
        decimal? estimatedCost,
        string? currency,
        CancellationToken cancellationToken)
    {
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
        if (estimatedCost == 0)
        {
            return;
        }

        var summary = Build(project, await ListProjectJobsAsync(project.Id, cancellationToken));
        if (summary.UnknownCostJobCount > 0)
        {
            throw new InvalidOperationException(
                "Project contains provider generation with unknown cost; hard budget compliance cannot be guaranteed until those costs are resolved.");
        }

        var projected = summary.ProjectedCost + estimatedCost.Value;
        if (projected > project.MaximumBudget!.Value)
        {
            throw new InvalidOperationException(
                $"Generation would exceed the project maximum budget: {projected:0.00} {BudgetCurrency} projected > {project.MaximumBudget.Value:0.00} {BudgetCurrency} maximum.");
        }
    }

    private async Task<IReadOnlyList<GenerationJob>> ListProjectJobsAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        (await _jobs.ListAsync(cancellationToken))
            .Where(job => job.ProjectId == projectId)
            .ToArray();

    private static bool IsCostTracked(GenerationJob job) =>
        job.ProviderId is not null || job.EstimatedCost is not null || job.ActualCost is not null;

    private static decimal Normalize(decimal? value) => value is > 0 ? value.Value : 0m;
}
