FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Miqat.API.Controller/Miqat.API.Controller.csproj", "Miqat.API.Controller/"]
COPY ["Miqat.Application/Miqat.Application.csproj", "Miqat.Application/"]
COPY ["Miqat.Domain/Miqat.Domain.csproj", "Miqat.Domain/"]
COPY ["Miqat.Infrastructure/Miqat.Infrastructure.csproj", "Miqat.Infrastructure/"]
COPY ["Miqat.infrastructure.persistence/Miqat.infrastructure.persistence.csproj", "Miqat.infrastructure.persistence/"]

RUN dotnet restore "Miqat.API.Controller/Miqat.API.Controller.csproj"

COPY . .

WORKDIR "/src/Miqat.API.Controller"
RUN dotnet publish "Miqat.API.Controller.csproj" -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 10000

ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Miqat.API.Controller.dll"]