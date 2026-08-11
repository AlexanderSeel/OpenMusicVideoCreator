using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Application.Costs;

public sealed class BudgetAwareJobQueue : IJobQueue
{
    private readonly JobService _jobs;
    private readonly IProjectRepository _projects;
    private readonly ProjectCostService _costs;

    public BudgetAwareJobQueue(
        JobService jobs,
        IProjectRepository projects,
        ProjectCostService costs)
    {
        _jobs = jobs;
        _projects = projects;
        _costs = costs;
    }

    public async Task<GenerationJob> EnqueueAsync(
        JobDefinition definition,
        IReadOnlyCollection<Guid>? dependencyIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.ProjectId is not Guid projectId)
        {
            return await _jobs.EnqueueAsync(definition, dependencyIds, cancellationToken);
        }

        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        return await _costs.ExecuteWithinBudgetAsync(
            project,
            definition.EstimatedCost,
            definition.Currency,
            token => _jobs.EnqueueAsync(definition, dependencyIds, token),
            cancellationToken);
    }
}
