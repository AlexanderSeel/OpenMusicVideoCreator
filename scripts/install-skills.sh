#!/usr/bin/env bash
set -euo pipefail

install_skill() {
  local repository="$1"
  local skill="$2"

  echo "Installing skill '$skill' from $repository..."
  npx skills add "$repository" --skill "$skill"
}

install_skill "https://github.com/heygen-com/hyperframes" "music-to-video"
install_skill "https://github.com/codewithmukesh/dotnet-claude-kit" "clean-architecture"
install_skill "https://github.com/wshobson/agents" "microservices-patterns"
install_skill "https://github.com/wshobson/agents" "tailwind-design-system"
install_skill "https://github.com/vercel-labs/agent-skills" "vercel-react-best-practices"
install_skill "https://github.com/vercel-labs/agent-skills" "web-design-guidelines"
install_skill "https://github.com/wshobson/agents" "prompt-engineering-patterns"
install_skill "https://github.com/wshobson/agents" "error-handling-patterns"
install_skill "https://github.com/erichowens/some_claude_skills" "video-processing-editing"

echo "OpenMusicVideoCreator core agent skills installed."
