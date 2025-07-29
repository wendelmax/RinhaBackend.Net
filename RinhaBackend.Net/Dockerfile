FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Install native tools for AOT compilation
RUN apt-get update && apt-get install -y clang gcc g++ libc6-dev && rm -rf /var/lib/apt/lists/*

COPY ["RinhaBackend.Net/RinhaBackend.Net.csproj", "RinhaBackend.Net/"]
RUN dotnet restore "RinhaBackend.Net/RinhaBackend.Net.csproj"
COPY . .
WORKDIR "/src/RinhaBackend.Net"
RUN dotnet build "./RinhaBackend.Net.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publish with optimizations but without AOT to support Dapper
RUN dotnet publish "./RinhaBackend.Net.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:PublishTrimmed=true /p:PublishReadyToRun=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RinhaBackend.Net.dll"]
