FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743 AS net8
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317
COPY --from=net8 /usr/share/dotnet/packs/ /usr/share/dotnet/packs/
COPY --from=net8 /usr/share/dotnet/shared/ /usr/share/dotnet/shared/
COPY roslyncodelens.mcp.2.18.1.nupkg /feed/
RUN dotnet tool install RoslynCodeLens.Mcp --version 2.18.1 --tool-path /opt/roslyn --source /feed --ignore-failed-sources
ENV DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_EnableDiagnostics=0 HOME=/tmp/home DOTNET_CLI_HOME=/tmp/home NUGET_PACKAGES=/packages
USER 1654
WORKDIR /workspace
ENTRYPOINT ["/opt/roslyn/roslyn-codelens-mcp"]
