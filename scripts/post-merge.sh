#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

dotnet restore GPOE26.slnx --verbosity quiet
dotnet build GPOE26.slnx --no-restore --configuration Debug --verbosity quiet
