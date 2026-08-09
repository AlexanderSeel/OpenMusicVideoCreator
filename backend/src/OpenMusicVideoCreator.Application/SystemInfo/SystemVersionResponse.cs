namespace OpenMusicVideoCreator.Application.SystemInfo;

public sealed record SystemVersionResponse(
    string ApplicationName,
    string Version,
    string Environment);
