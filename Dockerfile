# ── Stage 1: Build ────────────────────────────────────────────────────────────
# We use the full SDK image to build and publish the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy each project file first (Docker caches this layer — speeds up rebuilds)
COPY ["Miqat.API.Controller/Miqat.API.Controller.csproj", "Miqat.API.Controller/"]
COPY ["Miqat.Application/Miqat.Application.csproj", "Miqat.Application/"]
COPY ["Miqat.Domain/Miqat.Domain.csproj", "Miqat.Domain/"]
COPY ["Miqat.Infrastructure/Miqat.Infrastructure.csproj", "Miqat.Infrastructure/"]
COPY ["Miqat.infrastructure.persistence/Miqat.infrastructure.persistence.csproj", "Miqat.infrastructure.persistence/"]
COPY ["Miqat.Persistence/Miqat.Persistence.csproj", "Miqat.Persistence/"]

# Restore all NuGet packages
RUN dotnet restore "Miqat.API.Controller/Miqat.API.Controller.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish in Release mode
WORKDIR "/src/Miqat.API.Controller"
RUN dotnet publish "Miqat.API.Controller.csproj" -c Release -o /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
# We use the smaller runtime-only image (no SDK) to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy only the published output from the build stage
COPY --from=build /app/publish .

# Start the application
ENTRYPOINT ["dotnet", "Miqat.API.Controller.dll"]
