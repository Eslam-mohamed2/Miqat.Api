# ── Build ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files are copied on their own first so the restore layer stays cached
# and does not re-run on every source edit.
COPY Miqat.Domain/Miqat.Domain.csproj                                         Miqat.Domain/
COPY Miqat.Application/Miqat.Application.csproj                               Miqat.Application/
COPY Miqat.Infrastructure/Miqat.Infrastructure.csproj                         Miqat.Infrastructure/
COPY Miqat.infrastructure.persistence/Miqat.infrastructure.persistence.csproj Miqat.infrastructure.persistence/
COPY Miqat.API.Controller/Miqat.API.Controller.csproj                         Miqat.API.Controller/
COPY Miqat.Persistence/Miqat.API.csproj                                       Miqat.Persistence/

# Restoring the entry project pulls in every referenced project transitively.
RUN dotnet restore Miqat.Persistence/Miqat.API.csproj

COPY . .

RUN dotnet publish Miqat.Persistence/Miqat.API.csproj \
    -c Release \
    -o /app/out \
    --no-restore \
    /p:UseAppHost=false

# ── Runtime ───────────────────────────────────────────────────────────────────
# Ends on the runtime image rather than the SDK: ~220MB instead of ~3GB, and the
# compiler toolchain is not shipped to production.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/out .

EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Miqat.API.dll"]
