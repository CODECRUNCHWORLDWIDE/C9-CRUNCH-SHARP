# Multi-stage Dockerfile for the Polyglot Workshop backend (Workshop.Api).
# Chiseled runtime, non-root, restore-before-source for cache hits.
# See lecture-notes/01-multi-stage-and-native-aot-dockerfiles.md.
#
# Replace the project paths below with YOUR solution's real project layout.

# ---- build stage: the SDK compiles and publishes; it is discarded after ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore FIRST from just the .csproj files so this layer is cached across
# source-only changes. List every project the backend host references.
COPY ["src/Workshop.Api/Workshop.Api.csproj", "src/Workshop.Api/"]
COPY ["src/Workshop.Contracts/Workshop.Contracts.csproj", "src/Workshop.Contracts/"]
COPY ["src/Workshop.Domain/Workshop.Domain.csproj", "src/Workshop.Domain/"]
# If you use central package management, copy the props so restore is reproducible.
COPY ["Directory.Packages.props", "Directory.Build.props", "./"]
RUN dotnet restore "src/Workshop.Api/Workshop.Api.csproj"

# Now the rest of the source, then publish.
COPY . .
RUN dotnet publish "src/Workshop.Api/Workshop.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---- final stage: chiseled ASP.NET runtime, ~110 MB, no shell, non-root ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app

# Only the published output crosses this line. No SDK, no source, no .git.
COPY --from=build /app/publish .

# Drop root. The chiseled image defines the non-root user as $APP_UID.
USER $APP_UID

# Non-root cannot bind < 1024; chiseled images default Kestrel to 8080.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "Workshop.Api.dll"]
