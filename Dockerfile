FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /app

COPY . .

RUN dotnet restore "Miqat.Persistence/Miqat.API.csproj" && \
    dotnet restore "Miqat.Application/Miqat.Application.csproj" && \
    dotnet restore "Miqat.Domain/Miqat.Domain.csproj" && \
    dotnet restore "Miqat.Infrastructure/Miqat.Infrastructure.csproj" && \
    dotnet restore "Miqat.infrastructure.persistence/Miqat.infrastructure.persistence.csproj"

RUN dotnet publish "Miqat.Persistence/Miqat.API.csproj" \
    -c Release \
    -o /app/out \
    /p:UseAppHost=false

WORKDIR /app/out

EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Miqat.API.dll"]