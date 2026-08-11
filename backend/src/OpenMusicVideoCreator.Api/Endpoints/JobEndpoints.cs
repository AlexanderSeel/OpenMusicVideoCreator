using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Api.Contracts.Jobs;
using OpenMusicVideoCreator.Application.Jobs;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class JobEndpoints
{
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("/", async (JobService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(cancellationToken)).Select(JobResponse.FromDomain).ToArray()))
            .WithName("ListJobs")
            .Produces<JobResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (
            Guid id,
            JobService service,
            CancellationToken cancellationToken) =>
        {
            var job = await service.GetAsync(id, cancellationToken);
            return job is null
                ? (IResult)Results.NotFound()
                : Results.Ok(JobResponse.FromDomain(job));
        })
            .WithName("GetJob")
            .Produces<JobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/attempts", async (
            Guid id,
            JobService service,
            CancellationToken cancellationToken) =>
        {
            if (await service.GetAsync(id, cancellationToken) is null)
            {
                return (IResult)Results.NotFound();
            }

            var attempts = await service.GetAttemptsAsync(id, cancellationToken);
            return Results.Ok(attempts.Select(JobAttemptResponse.FromDomain).ToArray());
        })
            .WithName("GetJobAttempts")
            .Produces<JobAttemptResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/dependencies", async (
            Guid id,
            JobService service,
            CancellationToken cancellationToken) =>
        {
            if (await service.GetAsync(id, cancellationToken) is null)
            {
                return (IResult)Results.NotFound();
            }

            return Results.Ok(await service.GetDependenciesAsync(id, cancellationToken));
        })
            .WithName("GetJobDependencies")
            .Produces<Guid[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            JobCreateRequest request,
            IJobQueue queue,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var job = await queue.EnqueueAsync(
                    request.ToDefinition(),
                    request.Dependencies ?? [],
                    cancellationToken);
                return (IResult)Results.Created(
                    $"/api/jobs/{job.Id}",
                    JobResponse.FromDomain(job));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["job"] = [exception.Message],
                });
            }
        })
            .WithName("CreateJob")
            .Produces<JobResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        MapJobAction(group, "pause", (service, id, token) => service.PauseAsync(id, token));
        MapJobAction(group, "resume", (service, id, token) => service.ResumeAsync(id, token));
        MapJobAction(group, "retry", (service, id, token) => service.RetryAsync(id, token));
        MapJobAction(group, "restart", (service, id, token) => service.RestartAsync(id, token));
        MapJobAction(group, "cancel", (service, id, token) => service.CancelAsync(id, token), signalCancellation: true);

        group.MapPost("/projects/{projectId:guid}/pause", async (
            Guid projectId,
            JobService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new JobScopeActionResponse(
                await service.PauseProjectAsync(projectId, cancellationToken))))
            .WithName("PauseProjectJobs");

        group.MapPost("/projects/{projectId:guid}/resume", async (
            Guid projectId,
            JobService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new JobScopeActionResponse(
                await service.ResumeProjectAsync(projectId, cancellationToken))))
            .WithName("ResumeProjectJobs");

        group.MapPost("/projects/{projectId:guid}/cancel", async (
            Guid projectId,
            JobService service,
            IJobExecutionCancellationRegistry executionCancellations,
            CancellationToken cancellationToken) =>
        {
            var matching = (await service.ListAsync(cancellationToken))
                .Where(job => job.ProjectId == projectId)
                .Select(job => job.Id)
                .ToArray();
            var count = await service.CancelProjectAsync(projectId, cancellationToken);
            foreach (var jobId in matching) executionCancellations.Cancel(jobId);
            return Results.Ok(new JobScopeActionResponse(count));
        })
            .WithName("CancelProjectJobs");

        group.MapPost("/projects/{projectId:guid}/scenes/{sceneId:guid}/pause", async (
            Guid projectId,
            Guid sceneId,
            JobService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new JobScopeActionResponse(
                await service.PauseSceneAsync(projectId, sceneId, cancellationToken))))
            .WithName("PauseSceneJobs");

        group.MapPost("/projects/{projectId:guid}/scenes/{sceneId:guid}/resume", async (
            Guid projectId,
            Guid sceneId,
            JobService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new JobScopeActionResponse(
                await service.ResumeSceneAsync(projectId, sceneId, cancellationToken))))
            .WithName("ResumeSceneJobs");

        group.MapPost("/projects/{projectId:guid}/scenes/{sceneId:guid}/cancel", async (
            Guid projectId,
            Guid sceneId,
            JobService service,
            IJobExecutionCancellationRegistry executionCancellations,
            CancellationToken cancellationToken) =>
        {
            var matching = (await service.ListAsync(cancellationToken))
                .Where(job => job.ProjectId == projectId && job.SceneId == sceneId)
                .Select(job => job.Id)
                .ToArray();
            var count = await service.CancelSceneAsync(projectId, sceneId, cancellationToken);
            foreach (var jobId in matching) executionCancellations.Cancel(jobId);
            return Results.Ok(new JobScopeActionResponse(count));
        })
            .WithName("CancelSceneJobs");

        group.MapGet("/events", StreamJobEventsAsync)
            .WithName("StreamJobEvents")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        return endpoints;
    }

    private static void MapJobAction(
        RouteGroupBuilder group,
        string actionName,
        Func<JobService, Guid, CancellationToken, Task<bool>> action,
        bool signalCancellation = false)
    {
        group.MapPost($"/{{id:guid}}/{actionName}", async (
            Guid id,
            JobService service,
            IJobExecutionCancellationRegistry executionCancellations,
            CancellationToken cancellationToken) =>
        {
            if (await service.GetAsync(id, cancellationToken) is null)
            {
                return (IResult)Results.NotFound();
            }

            if (!await action(service, id, cancellationToken))
            {
                return Results.Conflict();
            }

            if (signalCancellation)
            {
                executionCancellations.Cancel(id);
            }
            return Results.Ok();
        });
    }

    private static async Task StreamJobEventsAsync(
        HttpContext context,
        IJobChangeStream changeStream,
        JobService service,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        await context.Response.WriteAsync("event: ready\ndata: {}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);

        await foreach (var jobId in changeStream.SubscribeAsync(cancellationToken))
        {
            var job = await service.GetAsync(jobId, cancellationToken);
            if (job is null)
            {
                continue;
            }

            var json = JsonSerializer.Serialize(JobResponse.FromDomain(job), StreamJsonOptions);
            await context.Response.WriteAsync($"event: job\ndata: {json}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
