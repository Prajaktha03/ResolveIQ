# ==============================================================================
# ResolveIQ ASP.NET Core Web API - Multi-Stage Dockerfile (.NET 10)
# ==============================================================================

# Stage 1: Build & Restore Environment
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first to maximize Docker layer caching
COPY ["ResolveIQ.slnx", "./"]
COPY ["ResolveIQ.Api/ResolveIQ.Api.csproj", "ResolveIQ.Api/"]
COPY ["ResolveIQ.Tests/ResolveIQ.Tests.csproj", "ResolveIQ.Tests/"]

# Restore dependencies
RUN dotnet restore "ResolveIQ.slnx"

# Copy the remaining project sources
COPY . .

# Build the API project in Release mode
WORKDIR "/src/ResolveIQ.Api"
RUN dotnet build "ResolveIQ.Api.csproj" -c Release -o /app/build --no-restore

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "ResolveIQ.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
# Stage 3: Production Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Expose internal listening ports (8080 for Docker Compose, 10000 for Render)
EXPOSE 8080 10000
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

# Run container process as built-in non-root user (app)
USER app

# Copy published application binaries with non-root ownership
COPY --from=publish --chown=app:app /app/publish .

# Define execution entrypoint
ENTRYPOINT ["dotnet", "ResolveIQ.Api.dll"]
