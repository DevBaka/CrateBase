#!/usr/bin/env bash
# Startet Cratebase unter Linux/Mac. Doppelklick oder ./start.sh im Terminal.
set -e
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
    # Fällt auf eine übliche User-lokale .NET-Installation zurück (z.B. nötig auf
    # rpm-ostree/immutable Distros wie Bazzite, wo es kein System-dotnet-Paket gibt).
    if [ -x "$HOME/.dotnet/dotnet" ]; then
        export DOTNET_ROOT="$HOME/.dotnet"
        export PATH="$HOME/.dotnet:$PATH"
    else
        echo "Fehler: 'dotnet' wurde nicht gefunden." >&2
        echo "Bitte das .NET SDK installieren: https://dotnet.microsoft.com/download" >&2
        exit 1
    fi
fi

dotnet run --project src/Cratebase.App.Desktop/Cratebase.App.Desktop.csproj -- "$@"
