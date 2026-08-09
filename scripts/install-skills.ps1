$ErrorActionPreference = "Stop"

function Install-AgentSkill {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Skill
    )

    Write-Host "Installing skill '$Skill' from $Repository..."
    & npx skills add $Repository --skill $Skill
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install skill '$Skill' from '$Repository' (exit code $LASTEXITCODE)."
    }
}

Install-AgentSkill "https://github.com/heygen-com/hyperframes" "music-to-video"
Install-AgentSkill "https://github.com/codewithmukesh/dotnet-claude-kit" "clean-architecture"
Install-AgentSkill "https://github.com/wshobson/agents" "microservices-patterns"
Install-AgentSkill "https://github.com/wshobson/agents" "tailwind-design-system"
Install-AgentSkill "https://github.com/vercel-labs/agent-skills" "vercel-react-best-practices"
Install-AgentSkill "https://github.com/vercel-labs/agent-skills" "web-design-guidelines"
Install-AgentSkill "https://github.com/wshobson/agents" "prompt-engineering-patterns"
Install-AgentSkill "https://github.com/wshobson/agents" "error-handling-patterns"
Install-AgentSkill "https://github.com/erichowens/some_claude_skills" "video-processing-editing"

Write-Host "OpenMusicVideoCreator core agent skills installed."
