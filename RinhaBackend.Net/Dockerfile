FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet build -c Release -o /app/build /p:TieredPGO=1

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:PublishReadyToRun=true /p:TieredPGO=1 /p:PublishTrimmed=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RinhaBackend.Net.dll"]
